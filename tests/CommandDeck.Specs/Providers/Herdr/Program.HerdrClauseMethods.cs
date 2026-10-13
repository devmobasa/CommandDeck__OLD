namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrCoordinatorIntersectsTextClausesAsync( )
    {
        HerdrFake fake = new( );
        HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
        SearchCoordinator coordinator = new( [provider], new SearchOptions( ) );

        foreach ( var query in new[] { "htab: commanddeck tests", "htab: tests commanddeck", "htab: comanddeck tests" } )
        {
            var results = await coordinator.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, CancellationToken.None );
            Equal( "herdr:tab:w1:t1", string.Join( '|', results.Select( static result => result.Id ) ) );
        }

        var absent = await coordinator.SearchAsync(
            new SearchRequest { Query = "htab: commanddeck tests nonexistent", Limit = 20 }, CancellationToken.None );
        Equal( 0, absent.Count );
    }

    private static async Task HerdrCoordinatorIntersectsFieldClausesAsync( )
    {
        HerdrFake fake = new( );
        HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
        SearchCoordinator coordinator = new( [provider], new SearchOptions( ) );

        foreach ( var query in new[]
        {
            "htab: title:tests metadata:CommandDeck",
            "htab: metadata:CommandDeck title:tests",
            "htab: title:tests metadata:idle",
            "htab: commanddeck tests -title:release",
            "hspace: title:CommandDeck metadata:working"
        } )
        {
            var results = await coordinator.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, CancellationToken.None );
            Equal( query.StartsWith( "hspace:", StringComparison.Ordinal ) ? "herdr:workspace:w1" : "herdr:tab:w1:t1",
                string.Join( '|', results.Select( static result => result.Id ) ) );
        }

        foreach ( var query in new[]
        {
            "htab: title:CommandDeck metadata:tests",
            "htab: title:tests metadata:Notes",
            "htab: commanddeck tests -metadata:CommandDeck",
            "htab: commanddeck tests -tests",
            "htab: title:tests path:CommandDeck",
            "htab: title:tests content:CommandDeck"
        } )
        {
            var results = await coordinator.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, CancellationToken.None );
            True( results.Count == 0, $"{query}: {string.Join( '|', results.Select( static result => result.Id ) )}" );
        }

        var structured = await coordinator.SearchAsync( new SearchRequest
        {
            Expression = new QueryExpression
            {
                IncludeFilterIds = ["herdr-tabs"],
                Clauses = [new QueryClause { Field = QueryFieldIds.Title, Text = "tests" },
                    new QueryClause { Field = QueryFieldIds.Metadata, Text = "CommandDeck" }]
            },
            Limit = 20
        }, CancellationToken.None );
        Equal( "herdr:tab:w1:t1", structured.Single( ).Id );

        // config: is a source alias in text syntax; use the structured field
        // form to constrain configuration content, which Herdr does not supply.
        var configurationField = await coordinator.SearchAsync( new SearchRequest
        {
            Expression = new QueryExpression
            {
                IncludeFilterIds = ["herdr-tabs"],
                Clauses = [new QueryClause { Field = QueryFieldIds.Title, Text = "tests" },
                    new QueryClause { Field = QueryFieldIds.Config, Text = "CommandDeck" }]
            },
            Limit = 20
        }, CancellationToken.None );
        Equal( 0, configurationField.Count );

        // The surviving row starts beyond the coordinator's four-candidate
        // budget for a one-result request. Exclude before applying that budget.
        var limited = await coordinator.SearchAsync( new SearchRequest
        {
            Query = "htab: -metadata:CommandDeck -metadata:Notes",
            Limit = 1
        }, CancellationToken.None );
        Equal( "herdr:tab:gone:t1", limited.Single( ).Id );
    }

    private static async Task HerdrCoordinatorPreservesPhraseClausesAsync( )
    {
        HerdrFake fake = new( )
        {
            Respond = args => HerdrResult( args[2] == "workspace" ? HerdrWorkspaceListJson :
                HerdrTabListJson.Replace( "\"tests\"", "\"release notes\"", StringComparison.Ordinal )
                    .Replace( "\"release\"", "\"release planning notes\"", StringComparison.Ordinal ) )
        };
        HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
        SearchCoordinator coordinator = new( [provider], new SearchOptions( ) );

        var phrase = await coordinator.SearchAsync(
            new SearchRequest { Query = "htab: commanddeck \"release notes\"", Limit = 20 }, CancellationToken.None );
        Equal( "herdr:tab:w1:t1", string.Join( '|', phrase.Select( static result => result.Id ) ) );

        var separate = await coordinator.SearchAsync(
            new SearchRequest { Query = "htab: commanddeck release notes", Limit = 20 }, CancellationToken.None );
        Equal( "herdr:tab:w1:t1|herdr:tab:w1:t2", string.Join( '|', separate.Select( static result => result.Id ).Order( StringComparer.Ordinal ) ) );

        var acrossFields = await coordinator.SearchAsync(
            new SearchRequest { Query = "htab: \"CommandDeck release notes\"", Limit = 20 }, CancellationToken.None );
        Equal( 0, acrossFields.Count );
    }

    private static async Task HerdrCoordinatorNormalizesPhraseClausesAsync( )
    {
        // Normalize both the query and the inventory labels. The second tab
        // contains the same words with a gap and must never match the phrase.
        foreach ( var (tabLabel, spaceLabel) in new[]
        {
            ("café notes", "résumé drafts"),
            ("cafe\u0301 notes", "re\u0301sume\u0301 drafts"),
            ("cafe notes", "resume drafts")
        } )
        {
            HerdrFake fake = new( )
            {
                Respond = args => HerdrResult( args[2] == "workspace"
                    ? HerdrWorkspaceListJson.Replace( "CommandDeck", spaceLabel, StringComparison.Ordinal )
                    : HerdrTabListJson.Replace( "\"tests\"", $"\"{tabLabel}\"", StringComparison.Ordinal )
                        .Replace( "\"release\"", "\"café planning notes\"", StringComparison.Ordinal ) )
            };
            HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
            SearchCoordinator coordinator = new( [provider], new SearchOptions( ) );

            foreach ( var query in new[]
            {
                "htab: \"cafe notes\"",
                "htab: \"CAFÉ NOTES\"",
                "htab: \"cafe\u0301 notes\"",
                "htab: \"résumé drafts\" \"café notes\"",
                "hspace: \"resume drafts\"",
                "hspace: \"résumé drafts\"",
                "hspace: \"re\u0301sume\u0301 drafts\""
            } )
            {
                var results = await coordinator.SearchAsync(
                    new SearchRequest { Query = query, Limit = 20 }, CancellationToken.None );
                Equal( query.StartsWith( "hspace:", StringComparison.Ordinal ) ? "herdr:workspace:w1" : "herdr:tab:w1:t1",
                    string.Join( '|', results.Select( static result => result.Id ) ) );
            }

            var acrossFields = await coordinator.SearchAsync(
                new SearchRequest { Query = "htab: \"resume drafts cafe notes\"", Limit = 20 }, CancellationToken.None );
            Equal( 0, acrossFields.Count );
        }
    }
}
