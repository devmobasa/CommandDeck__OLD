using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static ViewUpdate AuthorityView( string text, long expectedRevision, long generation = 1 ) => new(
        "session", 2, "list", expectedRevision, generation,
        new ListView { Id = "list", Title = "Items", Items = [new ListItem { Id = "row", Title = "Copy value", Actions = [ViewAction.CopyText( "copy", "Copy", text )] }] } );

    private static string PresentedHandle( PresentedExtensionView view ) => view.Content.GetProperty( "items" )[0].GetProperty( "actions" )[0].GetProperty( "handle" ).GetString( )!;

    private static void ExtensionActionsBindDisplayedPayloads( )
    {
        ExtensionViewAuthority authority = new( "session", 2, 7 );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        var input = AuthorityView( "old payload", 0 );
        var first = authority.Replace( input );
        // Rebuilding immutable input cannot change the already admitted binding.
        input = input with { Content = (( ListView ) input.Content) with { Items = [] } };
        authority.Present( 1, first.Revision );
        var second = authority.Replace( AuthorityView( "new payload", first.Revision ) );
        var admitted = authority.AdmitAction( 2, first.Revision, PresentedHandle( first ), "row" );
        // The newer offered snapshot did not invalidate the still displayed action.
        authority.Present( 3, second.Revision );
        var effect = authority.BeginAction( admitted, 7, new HashSet<string> { "clipboard.write" } );
        Equal( "old payload", (( EffectBinding ) effect).Action.Text );
        True( !first.Content.GetRawText( ).Contains( "old payload", StringComparison.Ordinal ) );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( admitted, 7, new HashSet<string> { "clipboard.write" } ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitAction( 4, first.Revision, PresentedHandle( first ), "row" ) );
        var latest = authority.AdmitAction( 5, second.Revision, PresentedHandle( second ), "row" );
        Equal( "new payload", (( EffectBinding ) authority.BeginAction( latest, 7, new HashSet<string> { "clipboard.write" } )).Action.Text );
        Throws<InvalidOperationException>( ( ) => authority.AdmitAction( 5, second.Revision, PresentedHandle( second ), "row" ) );
    }

    private static void ExtensionAuthorityRejectsRevocationAndUnsolicitedViews( )
    {
        ExtensionViewAuthority authority = new( "session", 2, 7 );
        Throws<InvalidOperationException>( ( ) => authority.Replace( AuthorityView( "unsolicited", 0 ) ) );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        var first = authority.Replace( AuthorityView( "one", 0 ) );
        var second = authority.Replace( AuthorityView( "two", 1 ) );
        Throws<InvalidOperationException>( ( ) => authority.Replace( AuthorityView( "three", 2 ) ) );
        authority.Present( 1, second.Revision );
        var third = authority.Replace( AuthorityView( "three", 2 ) );
        authority.CompleteInvocation( invocation );
        Throws<InvalidOperationException>( ( ) => authority.Replace( AuthorityView( "late", 3 ) ) );
        var nextInvocation = authority.BeginInvocation( InvocationOrigin.UserAction );
        Throws<InvalidOperationException>( ( ) => authority.Replace( AuthorityView( "stale-generation", 3 ) ) );
        Throws<InvalidOperationException>( ( ) => authority.Replace( AuthorityView( "forged", 3, 2 ) with { ExecutionEpoch = 99 } ) );
        Throws<InvalidOperationException>( ( ) => authority.Present( 2, first.Revision ) );
        authority.Present( 3, third.Revision );
        var denied = authority.AdmitAction( 4, third.Revision, PresentedHandle( third ), "row" );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( denied, 7, new HashSet<string>( ) ) );
        var pending = authority.AdmitAction( 5, third.Revision, PresentedHandle( third ), "row" );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( pending, 8, new HashSet<string> { "clipboard.write" } ) );
        authority.Revoke( );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( pending, 7, new HashSet<string> { "clipboard.write" } ) );
        Throws<InvalidOperationException>( ( ) => authority.Replace( AuthorityView( "revoked", 3, 2 ) ) );
        Throws<InvalidOperationException>( ( ) => authority.BeginInvocation( InvocationOrigin.UserAction ) );
    }
}
