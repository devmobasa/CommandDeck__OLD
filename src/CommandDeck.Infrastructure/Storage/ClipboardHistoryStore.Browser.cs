using System.Text;
using System.Text.Json;

namespace CommandDeck;

public sealed record ClipboardHistoryDocument( ClipboardEntrySummary Summary, string Text, ClipboardProjectionMetadata Metadata );

public sealed partial class ClipboardHistoryStore
{
    public long StoreRevision
    {
        get
        {
            lock ( _gate )
            {
                return Scalar( "SELECT revision FROM clipboard_state" );
            }
        }
    }

    public IReadOnlyList<ClipboardEntryIdentity> SnapshotEntries( QueryExpression expression, bool pinnedOnly, string? kind = null )
        => CreateBrowserSnapshot( expression, pinnedOnly, kind ).Entries;

    public (IReadOnlyList<ClipboardEntryIdentity> Entries, long StoreRevision) CreateBrowserSnapshot( QueryExpression expression, bool pinnedOnly, string? kind = null )
    {
        lock ( _gate )
        {
            Prune( );
            using var command = _connection.CreateCommand( );
            command.CommandText = """
                SELECT e.id,e.incarnation,e.text,e.keywords,s.kind,(SELECT group_concat(o.mime,' ') FROM clipboard_offers o WHERE o.entry_id=e.id)
                FROM entries e JOIN clipboard_selections s ON s.entry_id=e.id
                WHERE ($pinned=0 OR e.pinned=1) AND ($kind='' OR s.kind=$kind) ORDER BY e.pinned DESC,e.copied DESC,e.id LIMIT 1000
                """;
            command.Parameters.AddWithValue( "$pinned", pinnedOnly ? 1 : 0 );
            command.Parameters.AddWithValue( "$kind", kind ?? "" );
            using var reader = command.ExecuteReader( );
            List<ClipboardEntryIdentity> entries = [];
            while ( reader.Read( ) )
            {
                var text = reader.GetString( 2 );
                var keywords = ReadKeywords( reader.GetString( 3 ) );
                var metadata = reader.GetString( 4 ) + " " + reader.GetString( 5 );
                if ( expression.Clauses.All( clause => ClauseMatches( clause, text, keywords, metadata ) ) )
                {
                    entries.Add( new( reader.GetString( 0 ), reader.GetString( 1 ) ) );
                }
            }

            reader.Close( );
            return (entries, StoreRevision);
        }
    }

    public ClipboardHistoryDocument? ReadDocument( string id, string incarnation, long? expectedRevision = null )
    {
        lock ( _gate )
        {
            Prune( );
            using var command = _connection.CreateCommand( );
            command.CommandText = """
                SELECT e.text,e.pinned,e.copied,e.revision,e.keywords,s.kind,p.metadata FROM entries e
                JOIN clipboard_selections s ON s.entry_id=e.id JOIN clipboard_projections p ON p.entry_id=e.id
                WHERE e.id=$id AND e.incarnation=$incarnation
                """;
            command.Parameters.AddWithValue( "$id", id );
            command.Parameters.AddWithValue( "$incarnation", incarnation );
            using var reader = command.ExecuteReader( );
            if ( !reader.Read( ) || expectedRevision.HasValue && reader.GetInt64( 3 ) != expectedRevision.Value )
            {
                return null;
            }

            var text = reader.GetString( 0 );
            var metadata = ParseProjection( reader.GetString( 6 ) );
            ClipboardEntrySummary summary = new( id, incarnation, reader.GetInt64( 3 ), "", reader.GetBoolean( 1 ),
                reader.GetInt64( 2 ), ReadKeywords( reader.GetString( 4 ) ), Present( reader.GetString( 5 ), metadata ) );
            var available = 4096 - JsonSerializer.SerializeToUtf8Bytes( summary, CommandDeckJsonContext.Default.ClipboardEntrySummary ).Length;
            if ( available < 0 )
            {
                throw new IOException( "Clipboard summary metadata exceeds supported bounds." );
            }

            return new( summary with
            {
                Summary = SummaryText( text, available )
            }, text, metadata );
        }
    }

    public void MutateEntry( string id, string incarnation, long expectedRevision, string action, IReadOnlyList<string>? keywords = null )
    {
        var normalized = action == ClipboardActionIds.Keywords ? NormalizeKeywords( keywords ?? [] ) : [];
        lock ( _gate )
        {
            if ( ReadDocument( id, incarnation, expectedRevision ) is null )
            {
                throw new InvalidOperationException( "Clipboard entry changed or expired; refresh it before editing." );
            }

            switch ( action )
            {
                case ClipboardActionIds.Pin:
                case ClipboardActionIds.Unpin:
                    if ( !Pin( id, action == ClipboardActionIds.Pin ) )
                    {
                        throw new InvalidOperationException( "Clipboard pin capacity reached." );
                    }

                    break;
                case ClipboardActionIds.Delete:
                    Delete( id );
                    break;
                case ClipboardActionIds.Keywords:
                    Transaction( ( ) =>
                    {
                        Execute( "UPDATE entries SET keywords=$keywords,revision=revision+1 WHERE id=$id",
                            ("$keywords", JsonSerializer.Serialize( normalized, CommandDeckJsonContext.Default.StringArray )), ("$id", id) );
                        AdvanceRevision( );
                        return 0;
                    } );
                    break;
                default:
                    throw new InvalidOperationException( "Unknown clipboard entry action." );
            }
        }
    }

    private static bool ClauseMatches( QueryClause clause, string text, IReadOnlyList<string> keywords, string metadata )
    {
        var candidate = clause.Field switch
        {
            null => text + "\n" + string.Join( ' ', keywords ),
            QueryFieldIds.Title or QueryFieldIds.Content => text,
            QueryFieldIds.Metadata => metadata + " " + string.Join( ' ', keywords ),
            _ => null
        };
        var matches = candidate is not null && FuzzyMatcher.ScorePhrase( clause.Text, candidate ) > 0;
        return clause.Excluded ? !matches : matches;
    }

    private static string SummaryText( string text, int encodedBudget )
    {
        var bytes = 0;
        StringBuilder summary = new( );
        foreach ( var rune in text.EnumerateRunes( ) )
        {
            var display = rune.Value is '\r' or '\n' ? " " : rune.ToString( );
            var encoded = JsonEncodedText.Encode( display ).EncodedUtf8Bytes.Length;
            if ( bytes + rune.Utf8SequenceLength > 512 || encoded > encodedBudget )
            {
                break;
            }

            summary.Append( display );
            bytes += rune.Utf8SequenceLength;
            encodedBudget -= encoded;
        }

        return summary.ToString( );
    }

    private static string[] ReadKeywords( string json )
    {
        var values = JsonSerializer.Deserialize( json, CommandDeckJsonContext.Default.StringArray );
        if ( values is null )
        {
            throw new IOException( "Clipboard keywords are invalid." );
        }

        return NormalizeKeywords( values );
    }

    private static string[] NormalizeKeywords( IReadOnlyList<string> values )
    {
        if ( values.Count > 16 || values.Any( static value => !ValidKeyword( value ) ) )
        {
            throw new InvalidOperationException( "Use at most 16 keywords of 64 UTF-8 bytes without control characters." );
        }

        return values.Select( static value => value.Trim( ) ).Where( static value => value.Length > 0 )
            .Distinct( StringComparer.OrdinalIgnoreCase ).ToArray( );
    }

    private static bool ValidKeyword( string? value )
    {
        if ( value is null || value.Length > 64 || value.Any( char.IsControl ) )
        {
            return false;
        }

        try
        {
            return StrictUtf8.GetByteCount( value ) <= 64;
        }
        catch ( EncoderFallbackException )
        {
            return false;
        }
    }
}
