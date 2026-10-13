using CommandDeck.Extensions.Views;

namespace CommandDeck;

/// <summary>Host-owned dispatch targets. These bindings are never sent to the renderer.</summary>
public abstract record ExtensionActionBinding;
public sealed record EffectBinding( ViewAction Action ) : ExtensionActionBinding;
public sealed record CallbackBinding( string ActionId ) : ExtensionActionBinding;
public sealed record PageBinding( string Token ) : ExtensionActionBinding;
public sealed record BackBinding : ExtensionActionBinding;
public sealed record SubmitBinding( string ActionId ) : ExtensionActionBinding;
