using System.Text;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // Live windows carry only their class. The indexed application with that ID
    // or startup class supplies the icon; otherwise the lowercased class names it.
    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveApplicationIconsAsync(
        IReadOnlyList<string> windowClasses,
        CancellationToken cancellationToken )
    {
        var requested = windowClasses
            .Select( static windowClass => windowClass.Trim( ) )
            .Where( static windowClass => windowClass.Length > 0 )
            .Distinct( StringComparer.OrdinalIgnoreCase )
            .ToArray( );
        if ( requested.Length == 0 )
        {
            return new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        StringBuilder sql = new( "WITH requested(window_class) AS (VALUES " );
        for ( var index = 0; index < requested.Length; index++ )
        {
            if ( index > 0 )
            {
                sql.Append( ',' );
            }

            var parameter = $"$window_class_{index.ToString( System.Globalization.CultureInfo.InvariantCulture )}";
            sql.Append( '(' ).Append( parameter ).Append( ')' );
            command.Parameters.AddWithValue( parameter, requested[index] );
        }

        sql.Append( """
            )
            SELECT r.window_class,
                   COALESCE((
                       SELECT i.icon
                       FROM catalog_items i
                       WHERE i.kind = $application_kind
                         AND i.icon IS NOT NULL
                         AND i.icon <> ''
                         AND (
                           (i.stable_id COLLATE NOCASE) = ('app:' || r.window_class)
                           OR (i.startup_wm_class COLLATE NOCASE) = r.window_class
                           OR (i.stable_id COLLATE NOCASE) =
                              ('app:' || r.window_class || '.desktop'))
                       ORDER BY CASE
                           WHEN (i.stable_id COLLATE NOCASE) =
                                ('app:' || r.window_class) THEN 1
                           WHEN (i.startup_wm_class COLLATE NOCASE) =
                                r.window_class THEN 2
                           ELSE 3
                       END,
                       i.stable_id
                       LIMIT 1
                   ), lower(r.window_class))
            FROM requested r;
            """ );
        command.CommandText = sql.ToString( );
        command.Parameters.AddWithValue( "$application_kind", ResultKinds.Application );

        Dictionary<string, string> icons = new( StringComparer.OrdinalIgnoreCase );
        await using var reader =
            await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            icons[reader.GetString( 0 )] = reader.GetString( 1 );
        }

        return icons;
    }
}
