namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardSequenceCommandsArePrivateRowsAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-sequence-rows-" ).FullName;
        try
        {
            // Consent stays off, so the runtime never starts a capture helper or touches a clipboard.
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions { Enabled = false } );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, default );
            await runtime.Indexer.ReindexAsync( progress: null, default );
            IAgentLauncherHost assistant = runtime;
            foreach ( var (query, id, actionId) in new[]
                {
                    ("Copy next clipboard entry", ClipboardSequenceCommands.NextId, ClipboardSequenceCommands.NextAction),
                    ("Restart clipboard sequence", ClipboardSequenceCommands.RestartId, ClipboardSequenceCommands.RestartAction)
                } )
            {
                var row = (await runtime.SearchAsync( new SearchRequest { Query = query }, default )).Single( result => result.Id == id );
                var action = row.Actions.Single( );
                Equal( ActionKinds.ClipboardHistory, action.Kind );
                Equal( actionId, action.Id );
                True( string.IsNullOrEmpty( row.DetailMarkdown ) && action.Arguments["entry"].Length == 0, "commands carry no clipboard content" );
                True( PrivateSources.IsPrivate( row ) && !row.CanCustomize, "sequence commands follow private clipboard rules" );
                Equal( AgentActionRisks.Denied, AgentLauncherTools.ClassifyRisk( row, action ) );
                await ThrowsAsync<InvalidOperationException>( async ( ) => await runtime.SetFavoriteAsync( row.Id, true, default ) );

                var assistantRows = await assistant.SearchAsync( new SearchRequest { Query = query }, default );
                True( assistantRows.All( static result => !ClipboardSequenceCommands.IsCommand( result.Id ) ), "assistant search omits sequence commands" );
                True( !(await assistant.InvokeAsync( row.Id, actionId, row.InvocationReference!, query, true, default )).Succeeded );
                Equal( "Enable clipboard capture before using clipboard history here.",
                    (await runtime.InvokeAsync( row.Id, actionId, confirmed: false, default )).Message );
            }

            var home = await runtime.SearchAsync( new SearchRequest { Query = string.Empty, Limit = 50 }, default );
            True( home.All( static result => !ClipboardSequenceCommands.IsCommand( result.Id ) ), "sequence commands never appear on Home" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
