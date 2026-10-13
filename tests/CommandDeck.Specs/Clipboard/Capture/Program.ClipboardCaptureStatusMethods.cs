namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardBrowserReportsCaptureStatus( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-status-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new( ) );
            var status = "Capturing text";
            ClipboardHistoryService service = new( store, captureStatus: ( ) => status );
            service.RegisterOwner( "fixture" );
            ProtocolRequest request = new( )
            {
                RequestId = 1,
                Operation = ProtocolOperations.ClipboardSettingsGet,
                Clipboard = new( )
            };
            Equal( "Disabled", service.Handle( "fixture", request ).Status );
            store.Enabled = true;
            Equal( "Capturing text", service.Handle( "fixture", request ).Status );
            status = "Capture unavailable: native helper stopped; no wl-paste fallback was started";
            Equal( status, service.Handle( "fixture", request ).Status );
            store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, true );
            Equal( "Paused", service.Handle( "fixture", request ).Status );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
