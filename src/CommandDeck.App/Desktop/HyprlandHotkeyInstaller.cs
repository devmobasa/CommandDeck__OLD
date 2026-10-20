using System.Globalization;

namespace CommandDeck;

public sealed class HyprlandHotkeyPaths
{
    public required string HyprDirectory { get; init; }
    public required string BackupDirectory { get; init; }
    public string HotkeyFile => Path.Combine( HyprDirectory, HyprlandHotkeyConfig.FileName );
    public string MainConfig => Path.Combine( HyprDirectory, "hyprland.lua" );
    public string Bindings => Path.Combine( HyprDirectory, "bindings.lua" );

    // The same XDG resolution the installer scripts use for the Hyprland
    // directory and for CommandDeck's binding backups.
    public static HyprlandHotkeyPaths FromEnvironment( Func<string, string?> environment )
    {
        var home = environment( "HOME" );
        if ( string.IsNullOrEmpty( home ) )
        {
            throw new InvalidOperationException( "HOME is not set; cannot locate the Hyprland configuration." );
        }

        var configHome = NonEmpty( environment( "XDG_CONFIG_HOME" ) ) ?? Path.Combine( home, ".config" );
        var stateHome = NonEmpty( environment( "XDG_STATE_HOME" ) ) ?? Path.Combine( home, ".local", "state" );
        return new( )
        {
            HyprDirectory = Path.Combine( configHome, "hypr" ),
            BackupDirectory = Path.Combine( stateHome, "commanddeck", "binding-backups" )
        };
    }

    private static string? NonEmpty( string? value ) => string.IsNullOrEmpty( value ) ? null : value;
}

// Writes CommandDeck's own Hyprland module and its one require line, migrates
// the block earlier installers appended to bindings.lua, and removes both again.
// Every edited user file is backed up first; an unchanged file is not rewritten.
public sealed class HyprlandHotkeyInstaller( HyprlandHotkeyPaths paths, TimeProvider? time = null )
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public IReadOnlyList<string> Install( string chord )
    {
        var normalized = HyprlandHotkeyConfig.NormalizeChord( chord );
        if ( !File.Exists( paths.MainConfig ) )
        {
            throw new InvalidOperationException(
                $"{paths.MainConfig} does not exist; the CommandDeck hotkey needs Omarchy's Lua Hyprland configuration." );
        }

        List<string> messages = [];
        var bindings = File.Exists( paths.Bindings ) ? File.ReadAllText( paths.Bindings ) : string.Empty;
        bindings = RemoveLegacyBlock( bindings, messages );
        if ( HyprlandHotkeyConfig.HasUserBinding( bindings, normalized ) )
        {
            messages.Add( $"{paths.Bindings} already binds {normalized} to CommandDeck; leaving your binding in place." );
            return messages;
        }

        if ( HyprlandHotkeyConfig.MentionsChord( bindings, normalized ) )
        {
            messages.Add( $"Note: {paths.Bindings} also binds {normalized}; the CommandDeck binding loads later and replaces it." );
        }

        var generated = HyprlandHotkeyConfig.GenerateFile( normalized );
        var current = File.Exists( paths.HotkeyFile ) ? File.ReadAllText( paths.HotkeyFile ) : null;
        if ( current != generated )
        {
            if ( current is not null && !HyprlandHotkeyConfig.IsGenerated( current ) )
            {
                messages.Add( $"Backed up an unrelated {paths.HotkeyFile} to {Backup( paths.HotkeyFile )}" );
            }

            WriteAtomically( paths.HotkeyFile, generated );
            messages.Add( $"Wrote {paths.HotkeyFile}: {normalized} toggles CommandDeck through {HyprlandHotkeyConfig.ShortcutId}." );
        }

        var main = File.ReadAllText( paths.MainConfig );
        var required = HyprlandHotkeyConfig.EnsureRequire( main );
        if ( required != main )
        {
            var backup = Backup( paths.MainConfig );
            WriteAtomically( paths.MainConfig, required );
            messages.Add( $"Added the CommandDeck require line to {paths.MainConfig}; backup: {backup}" );
        }

        if ( messages.Count == 0 )
        {
            messages.Add( $"The {normalized} CommandDeck hotkey is already installed." );
        }

        return messages;
    }

    // Removes the require line before the module, so Hyprland never requires a
    // missing file. A module CommandDeck did not generate is left in place.
    public IReadOnlyList<string> Remove( )
    {
        List<string> messages = [];
        if ( File.Exists( paths.Bindings ) )
        {
            RemoveLegacyBlock( File.ReadAllText( paths.Bindings ), messages );
        }

        if ( File.Exists( paths.MainConfig ) )
        {
            var main = File.ReadAllText( paths.MainConfig );
            var removed = HyprlandHotkeyConfig.RemoveRequire( main );
            if ( removed != main )
            {
                var backup = Backup( paths.MainConfig );
                WriteAtomically( paths.MainConfig, removed );
                messages.Add( $"Removed the CommandDeck require line from {paths.MainConfig}; backup: {backup}" );
            }
        }

        if ( File.Exists( paths.HotkeyFile ) )
        {
            if ( HyprlandHotkeyConfig.IsGenerated( File.ReadAllText( paths.HotkeyFile ) ) )
            {
                File.Delete( paths.HotkeyFile );
                messages.Add( $"Deleted {paths.HotkeyFile}" );
            }
            else
            {
                messages.Add( $"Left {paths.HotkeyFile} in place; CommandDeck did not generate it." );
            }
        }

        if ( messages.Count == 0 )
        {
            messages.Add( "No CommandDeck hotkey is installed." );
        }

        return messages;
    }

    private string RemoveLegacyBlock( string bindings, List<string> messages )
    {
        if ( !HyprlandHotkeyConfig.HasLegacyBlock( bindings ) )
        {
            return bindings;
        }

        var migrated = HyprlandHotkeyConfig.RemoveLegacyBlock( bindings );
        var backup = Backup( paths.Bindings );
        WriteAtomically( paths.Bindings, migrated );
        messages.Add( $"Removed the earlier CommandDeck block from {paths.Bindings}; backup: {backup}" );
        return migrated;
    }

    private string Backup( string path )
    {
        Directory.CreateDirectory( paths.BackupDirectory );
        var stamp = _time.GetLocalNow( ).ToString( "yyyyMMddHHmmss", CultureInfo.InvariantCulture );
        var backup = Path.Combine( paths.BackupDirectory, $"{Path.GetFileName( path )}.{stamp}" );
        for ( var suffix = 1; File.Exists( backup ); suffix++ )
        {
            backup = Path.Combine(
                paths.BackupDirectory,
                $"{Path.GetFileName( path )}.{stamp}-{suffix.ToString( CultureInfo.InvariantCulture )}" );
        }

        File.Copy( path, backup );
        return backup;
    }

    // Replaces a symlink's target rather than the link, and keeps the file mode.
    private static void WriteAtomically( string path, string content )
    {
        var target = File.Exists( path )
            ? File.ResolveLinkTarget( path, returnFinalTarget: true )?.FullName ?? path
            : path;
        var directory = Path.GetDirectoryName( target ) ?? ".";
        Directory.CreateDirectory( directory );

        var temporary = Path.Combine( directory, $".{Path.GetFileName( target )}.commanddeck-{Guid.NewGuid( ):N}" );
        try
        {
            File.WriteAllText( temporary, content );
            if ( File.Exists( target ) && !OperatingSystem.IsWindows( ) )
            {
                File.SetUnixFileMode( temporary, File.GetUnixFileMode( target ) );
            }

            File.Move( temporary, target, overwrite: true );
        }
        finally
        {
            File.Delete( temporary );
        }
    }
}
