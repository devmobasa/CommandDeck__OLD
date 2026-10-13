#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:project ../src/CommandDeck.ExtensionTools/CommandDeck.ExtensionTools.csproj

using CommandDeck.ExtensionTools;

if ( args.Length != 6 )
{
    throw new ArgumentException( "Usage: dotnet scripts/pack-extension.cs --manifest FILE --publish-dir DIRECTORY --output FILE.cdext" );
}
var options = Enumerable.Range( 0, 3 ).ToDictionary( index => args[index * 2], index => Path.GetFullPath( args[index * 2 + 1] ) );
ExtensionPackageBuilder.Pack( options["--manifest"], options["--publish-dir"], options["--output"] );
Console.WriteLine( $"Packed {options["--output"]}; run commanddeck extensions inspect --archive before installing." );
