using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Mountly.Core.Mounting;

namespace Mountly.Windows;

/// <summary>
/// Runs one CLI command as administrator (one UAC prompt) and reads its result back, for operations that
/// need it: mounting a VHD/VHDX, choosing a drive letter. The app itself keeps running as the user.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedCli
{
    public const string ExeName = "mountly-cli.exe";
    public const string ResultFileArgument = "--result-file";

    public static string? ExePath =>
        Path.Combine(AppContext.BaseDirectory, ExeName) is var path && File.Exists(path) ? path : null;

    /// <summary>Returns null when the user declined the UAC prompt.</summary>
    public static async Task<CliResult?> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancel = default)
    {
        var exe = ExePath ?? throw new FileNotFoundException($"{ExeName} is missing next to Mountly.");
        var resultFile = Path.Combine(Path.GetTempPath(), $"mountly-{Guid.NewGuid():N}.json");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.ArgumentList.Add(ResultFileArgument);
        start.ArgumentList.Add(resultFile);

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The CLI didn't start.");
            await process.WaitForExitAsync(cancel).ConfigureAwait(false);
            if (!File.Exists(resultFile))
                return new CliResult { Ok = false, Error = MountError.Unknown, Message = $"The elevated command exited with code {process.ExitCode}." };
            await using var stream = File.OpenRead(resultFile);
            return await JsonSerializer.DeserializeAsync(stream, CliJson.Default.CliResult, cancel).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: the user said no
        {
            return null;
        }
        finally
        {
            try { File.Delete(resultFile); } catch (IOException) { }
        }
    }
}
