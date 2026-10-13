using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RelevanceCorpusReportsSearchFailuresAsync( )
    {
        await AssertRelevanceSearchFailureAsync(
            "removed-firefox",
            """
            {
              "visibleWindow": 9,
              "items": [
                {"id": "app:firefox.desktop", "kind": "application", "title": "Firefox", "sourceId": "applications"},
                {"id": "app:chromium.desktop", "kind": "application", "title": "Chromium", "sourceId": "applications"}
              ],
              "cases": [
                {
                  "id": "removed-firefox",
                  "rationale": "Removing the indexed Firefox row must fail the required result.",
                  "steps": [
                    {"query": "firefox", "acceptable": ["app:firefox.desktop"], "required": ["app:firefox.desktop"]}
                  ]
                }
              ]
            }
            """,
            new HashSet<string>( StringComparer.Ordinal ) { "app:firefox.desktop" } );

        await AssertRelevanceSearchFailureAsync(
            "forbidden-firefox",
            """
            {
              "visibleWindow": 9,
              "items": [
                {"id": "app:firefox.desktop", "kind": "application", "title": "Firefox", "sourceId": "applications"}
              ],
              "cases": [
                {
                  "id": "forbidden-firefox",
                  "rationale": "A forbidden row that is actually visible must fail.",
                  "steps": [
                    {"query": "firefox", "forbidden": ["app:firefox.desktop"]}
                  ]
                }
              ]
            }
            """ );

        await AssertRelevanceSearchFailureAsync(
            "below-window",
            """
            {
              "visibleWindow": 1,
              "items": [
                {"id": "app:alpha.desktop", "kind": "application", "title": "Alpha", "sourceId": "applications"},
                {"id": "file:alpha-body", "kind": "file", "title": "notes.md", "path": "/home/spec/Notes/alpha.md", "body": "alpha procedure", "sourceId": "files"}
              ],
              "cases": [
                {
                  "id": "below-window",
                  "rationale": "A required body match just below a one-row window must fail.",
                  "steps": [
                    {"query": "alpha", "required": ["file:alpha-body"]}
                  ]
                }
              ]
            }
            """ );

        await AssertRelevanceSearchFailureAsync(
            "flipped-prefix",
            """
            {
              "visibleWindow": 9,
              "items": [
                {"id": "app:alpha.desktop", "kind": "application", "title": "Alpha", "sourceId": "applications"}
              ],
              "cases": [
                {
                  "id": "flipped-prefix",
                  "rationale": "A later prefix that no longer names Alpha must fail.",
                  "steps": [
                    {"query": "alp", "required": ["app:alpha.desktop"]},
                    {"query": "zzzz", "required": ["app:alpha.desktop"]}
                  ]
                }
              ]
            }
            """ );
    }

    private static async Task RelevanceCorpusRejectsMalformedFixturesAsync( )
    {
        await AssertMalformedRelevanceAsync(
            """
            {
              "visibleWindow": 9,
              "items": [
                {"id": "app:firefox.desktop", "kind": "application", "title": "Firefox", "sourceId": "applications"}
              ],
              "cases": [
                {"id": "same", "rationale": "First.", "steps": [{"query": "firefox", "required": ["app:firefox.desktop"]}]},
                {"id": "same", "rationale": "Duplicate.", "steps": [{"query": "firefox", "required": ["app:firefox.desktop"]}]}
              ]
            }
            """ );
        await AssertMalformedRelevanceAsync(
            """
            {
              "visibleWindow": 9,
              "items": [
                {"id": "app:firefox.desktop", "kind": "application", "title": "Firefox", "sourceId": "applications"}
              ],
              "cases": [
                {"id": "unknown", "rationale": "Unknown id.", "steps": [{"query": "firefox", "required": ["app:missing.desktop"]}]}
              ]
            }
            """ );
        await AssertMalformedRelevanceAsync(
            """
            {
              "visibleWindow": 9,
              "items": [
                {"id": "app:firefox.desktop", "kind": "application", "title": "Firefox", "sourceId": "applications"}
              ],
              "cases": [
                {"id": "empty-assertion", "rationale": "No assertion.", "steps": [{"query": "firefox"}]}
              ]
            }
            """ );
        await AssertMalformedRelevanceAsync(
            """
            {
              "visibleWindow": 9,
              "items": [
                {"id": "app:firefox.desktop", "kind": "application", "title": "Firefox", "sourceId": "applications"}
              ],
              "cases": [
                {"id": "no-steps", "rationale": "Missing steps.", "steps": []}
              ]
            }
            """ );
    }

    private static async Task AssertRelevanceSearchFailureAsync(
        string caseId,
        string json,
        IReadOnlySet<string>? omitIndexedIds = null )
    {
        using var document = JsonDocument.Parse( json );
        try
        {
            await MeasureRelevanceCorpusAsync( document.RootElement, 4, omitIndexedIds );
        }
        catch ( InvalidOperationException exception )
            when ( exception.Message.Contains( $"case '{caseId}'", StringComparison.Ordinal ) )
        {
            return;
        }

        throw new InvalidOperationException( $"Expected relevance search failure for '{caseId}'." );
    }

    private static async Task AssertMalformedRelevanceAsync( string json )
    {
        using var document = JsonDocument.Parse( json );
        try
        {
            await MeasureRelevanceCorpusAsync( document.RootElement, 4 );
        }
        catch ( InvalidOperationException exception )
            when ( exception.Message.StartsWith( "Relevance fixture is invalid", StringComparison.Ordinal ) )
        {
            return;
        }

        throw new InvalidOperationException( "Expected a malformed relevance fixture rejection." );
    }
}
