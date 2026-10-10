using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Vamoose.Api.Data;

// LocalDB ships only as x64, so an ARM64 .NET process can't load SqlUserInstance.dll to resolve "(localdb)\Name".
// Dev-only workaround: start the instance with sqllocaldb and connect through its named pipe instead.
public static partial class LocalDbConnection
{
    public static string Resolve(string connectionString)
    {
        var match = LocalDbServer().Match(connectionString);
        if (!match.Success || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            return connectionString;
        }

        var instance = match.Groups["name"].Value;
        Run("start", instance);
        var pipe = PipeName().Match(Run("info", instance)).Groups["pipe"].Value;
        return string.IsNullOrEmpty(pipe) ? connectionString : connectionString.Replace(match.Value, $"Server={pipe}");
    }

    private static string Run(string verb, string instance)
    {
        using var process = Process.Start(new ProcessStartInfo("sqllocaldb", [verb, instance])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not run sqllocaldb.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    [GeneratedRegex(@"(Server|Data Source)=\(localdb\)\\(?<name>[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LocalDbServer();

    [GeneratedRegex(@"(?<pipe>np:\\\\\.\\pipe\\LOCALDB#[^\s]+)")]
    private static partial Regex PipeName();
}
