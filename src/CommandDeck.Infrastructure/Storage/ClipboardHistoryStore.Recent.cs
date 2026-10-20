namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    // Snippet {clipboard offset=N}: the plain-text entry N places before the newest
    // one, ordered by last copy; pins do not reorder it. Only plain-text entries have
    // a 64-character content digest as their ID, so rich and image entries are skipped.
    public string? ReadRecentText( int offset )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( offset );
        lock ( _gate )
        {
            RequireHistoryConsent( );
            Prune( );
            using var command = _connection.CreateCommand( );
            command.CommandText = "SELECT text FROM entries WHERE length(id)=64 ORDER BY copied DESC,rowid DESC LIMIT 1 OFFSET $offset";
            command.Parameters.AddWithValue( "$offset", offset );
            return command.ExecuteScalar( ) as string;
        }
    }

    private void RequireHistoryConsent( )
    {
        if ( ReadPolicy( ).Consent == ClipboardConsent.None || MigrationRequired( ) )
        {
            throw new InvalidOperationException( "Enable clipboard capture before using clipboard history here." );
        }
    }
}
