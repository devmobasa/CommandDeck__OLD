using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionNavigationRetiresContextsAndBoundsStack( )
    {
        ExtensionViewAuthority authority = new( "session", 2, 7 );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        var root = authority.Replace( AuthorityView( "parent payload", 0 ) );
        authority.Present( 1, root.Revision );
        var oldCopy = authority.AdmitAction( 2, root.Revision, PresentedHandle( root ), "row" );
        ViewUpdate Detail( string id ) => new( "session", 2, id, 0, 1,
            new DetailView { Id = id, Title = "Detail", Body = "**Text**", Actions = [ViewAction.CopyText( "copy", "Copy", "detail payload" )] } );
        var detail = authority.Push( Detail( "details" ) );
        Equal( 2, detail.Content.GetProperty( "depth" ).GetInt32( ) );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( oldCopy, 7, new HashSet<string> { "clipboard.write" } ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitAction( 3, root.Revision, PresentedHandle( root ), "row" ) );
        authority.Present( 4, detail.Revision );
        var back = authority.AdmitAction( 5, detail.Revision, detail.Content.GetProperty( "backHandle" ).GetString( )!, null );
        True( authority.BeginAction( back, 7, new HashSet<string>( ) ) is BackBinding );
        var restored = authority.Pop( );
        Equal( "list", restored.ViewId );
        True( restored.Revision > detail.Revision );
        True( PresentedHandle( restored ) != PresentedHandle( root ) );
        True( !restored.Content.TryGetProperty( "backHandle", out _ ) );
        authority.Present( 6, restored.Revision );
        var copy = authority.AdmitAction( 7, restored.Revision, PresentedHandle( restored ), "row" );
        Equal( "parent payload", (( EffectBinding ) authority.BeginAction( copy, 7, new HashSet<string> { "clipboard.write" } )).Action.Text );
        Throws<InvalidOperationException>( ( ) => authority.Push( Detail( "list" ) ) );
        Throws<InvalidOperationException>( ( ) => authority.Replace( Detail( "different" ) ) );
        for ( var depth = 2; depth <= 8; depth++ )
        {
            var pushed = authority.Push( Detail( "detail-" + depth ) );
            Equal( depth, pushed.Content.GetProperty( "depth" ).GetInt32( ) );
        }
        Throws<InvalidOperationException>( ( ) => authority.Push( Detail( "overflow" ) ) );
        authority.Revoke( );
        Throws<InvalidOperationException>( ( ) => authority.Pop( ) );
    }

    private static void ExtensionPagesBindOpaqueTokensToCurrentRevision( )
    {
        ExtensionViewAuthority authority = new( "session", 2, 7 );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        var input = AuthorityView( "page one", 0 );
        input = input with
        {
            Content = (( ListView ) input.Content) with
            {
                NextPage = "secret-continuation"
            }
        };
        var first = authority.Replace( input );
        var tokenHandle = first.Content.GetProperty( "nextPage" ).GetString( )!;
        True( !first.Content.ToString( ).Contains( "secret-continuation", StringComparison.Ordinal ) );
        authority.Present( 1, first.Revision );
        var page = authority.AdmitAction( 2, first.Revision, tokenHandle, null );
        Equal( "list", page.ViewId );
        Equal( first.Revision, page.Revision );
        Equal( "secret-continuation", (( PageBinding ) authority.BeginAction( page, 7, new HashSet<string>( ) )).Token );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( page, 7, new HashSet<string>( ) ) );
        var delayed = authority.AdmitAction( 3, first.Revision, tokenHandle, null );
        var next = authority.Replace( AuthorityView( "page two", first.Revision ) );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( delayed, 7, new HashSet<string>( ) ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitAction( 4, first.Revision, tokenHandle, null ) );
        authority.Present( 5, next.Revision );
        Throws<InvalidOperationException>( ( ) => authority.AdmitAction( 6, next.Revision, tokenHandle, null ) );
        True( !next.Content.TryGetProperty( "nextPage", out _ ) );
        Throws<System.Text.Json.JsonException>( ( ) => System.Text.Json.JsonSerializer.Deserialize(
            """{"kind":"list","id":"forged","title":"Forged","actions":[{"id":"back","kind":"back","title":"Back"}]}""",
            ExtensionViewJsonContext.Default.ExtensionView ) );
    }
}
