#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Security;

var repository = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, ".."));
var temporary = Directory.CreateTempSubdirectory("commanddeck-author-tests-").FullName;
try
{
    var feed = Path.Combine(temporary, "feed");
    await RunAsync(["scripts/build-extension-sdk.cs", "--output", feed], repository);
    File.WriteAllText(Path.Combine(temporary, "global.json"), """{"sdk":{"version":"10.0.400","rollForward":"disable"}}""");
    File.Copy(Path.Combine(repository, "Directory.Build.props"), Path.Combine(temporary, "Directory.Build.props"));
    File.Copy(Path.Combine(repository, "CodeMetricsConfig.txt"), Path.Combine(temporary, "CodeMetricsConfig.txt"));
    foreach (var name in new[] { "LocalDraft", "LocalDraft.Tests" })
    {
        var target = Path.Combine(temporary, name);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repository, "samples/extensions", name)))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
    }
    File.WriteAllText(Path.Combine(temporary, "NuGet.Config"), $"<configuration><packageSources><clear/><add key=\"preview\" value=\"{SecurityElement.Escape(feed)}\"/><add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\"/></packageSources><packageSourceMapping><packageSource key=\"preview\"><package pattern=\"CommandDeck.*\"/></packageSource><packageSource key=\"nuget\"><package pattern=\"*\"/></packageSource></packageSourceMapping></configuration>");
    await RunAsync(["run", "--project", "LocalDraft.Tests", "-c", "Release", "-p:RestorePackagesPath=" + Path.Combine(temporary, "packages")], temporary);
    await RunAsync(["publish", "LocalDraft.Tests", "-c", "Release", "-r", "linux-x64", "-p:PublishAot=true", "-p:RestorePackagesPath=" + Path.Combine(temporary, "packages"), "-o", Path.Combine(temporary, "aot")], temporary);
    using var test = Process.Start(new ProcessStartInfo(Path.Combine(temporary, "aot/LocalDraft.Tests")) { WorkingDirectory = temporary })!;
    await test.WaitForExitAsync();
    if (test.ExitCode != 0) throw new InvalidOperationException("Native AOT author test failed.");
}
finally { Directory.Delete(temporary, true); }

static async Task RunAsync(string[] arguments, string directory)
{
    ProcessStartInfo start = new("dotnet") { WorkingDirectory = directory };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(5));
    try { await process.WaitForExitAsync(deadline.Token); }
    catch { process.Kill(true); throw; }
    if (process.ExitCode != 0) throw new InvalidOperationException("Author testing command failed.");
}
