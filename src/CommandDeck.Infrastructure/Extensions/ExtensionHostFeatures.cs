using System.Collections.Immutable;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public static class ExtensionHostFeatures
{
    public static ImmutableArray<string> Interactive => ExtensionFeatures.Interactive;
}
