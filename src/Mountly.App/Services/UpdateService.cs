using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Mountly.App.Services;

/// <summary>
/// Checks GitHub Releases for a newer Mountly and stages it with Velopack. Only installed copies update;
/// a <c>dotnet run</c> build reports <see cref="IsInstalled"/> false.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/Alexxfromgit/Mountly";

    private readonly Lazy<UpdateManager?> _manager = new(() =>
    {
        try
        {
            return new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // not started through Velopack (tests, design tools): updates are off
        }
    });

    /// <summary>Version for display, e.g. "0.1.0" (build metadata after '+' dropped).</summary>
    public static string CurrentVersion { get; } =
        typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public bool IsInstalled => _manager.Value?.IsInstalled ?? false;

    /// <summary>Downloads a newer release if there is one; returns its version, or null when up to date.</summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancel = default)
    {
        if (!IsInstalled || _manager.Value is not { } manager) return null;
        var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null) return null;
        await manager.DownloadUpdatesAsync(update, progress: null, cancelToken: cancel).ConfigureAwait(false);
        return update.TargetFullRelease.Version.ToString();
    }

    public void ApplyAndRestart()
    {
        if (_manager.Value is { UpdatePendingRestart: { } pending } manager) manager.ApplyUpdatesAndRestart(pending);
    }
}
