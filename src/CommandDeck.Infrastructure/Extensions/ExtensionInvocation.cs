namespace CommandDeck;

public enum InvocationOrigin { Open, UserAction }
public sealed record ExtensionInvocation( long Generation, string Id, InvocationOrigin Origin );
