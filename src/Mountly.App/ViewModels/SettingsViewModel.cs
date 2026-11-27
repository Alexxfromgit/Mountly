using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mountly.App.Services;
using Mountly.Core.Storage;

namespace Mountly.App.ViewModels;

/// <summary>The Settings page. Every change is saved and applied right away.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static IReadOnlyList<string> Themes { get; } = ["System", "Light", "Dark"];

    private readonly AppServices _services;
    private readonly bool _loading;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        _loading = true;
        var s = services.Settings.Current;
        Theme = Themes.Contains(s.Theme) ? s.Theme : "System";
        StartWithWindows = s.StartWithWindows;
        RemountOnStart = s.RemountOnStart;
        CloseToTray = s.CloseToTray;
        ShellIntegration = s.ShellIntegration;
        CheckForUpdates = s.CheckForUpdates;
        _loading = false;
    }

    public string Version => UpdateService.CurrentVersion;
    public string RepositoryUrl => UpdateService.RepositoryUrl;

    [ObservableProperty]
    public partial string Theme { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool RemountOnStart { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool ShellIntegration { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    [ObservableProperty]
    public partial string? UpdateStatus { get; private set; }

    [ObservableProperty]
    public partial bool IsUpdateReady { get; private set; }

    partial void OnThemeChanged(string value)
    {
        Apply(value);
        Save(s => s.Theme = value);
    }

    partial void OnStartWithWindowsChanged(bool value) => Save(s => s.StartWithWindows = value, integrate: true);
    partial void OnRemountOnStartChanged(bool value) => Save(s => s.RemountOnStart = value);
    partial void OnCloseToTrayChanged(bool value) => Save(s => s.CloseToTray = value);
    partial void OnShellIntegrationChanged(bool value) => Save(s => s.ShellIntegration = value, integrate: true);
    partial void OnCheckForUpdatesChanged(bool value) => Save(s => s.CheckForUpdates = value);

    private void Save(Action<AppSettings> change, bool integrate = false)
    {
        if (_loading) return;
        change(_services.Settings.Current);
        _services.Settings.Save();
        if (integrate) DesktopIntegration.Apply(_services.Settings.Current);
    }

    public static void Apply(string theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }

    [RelayCommand]
    public async Task CheckNowAsync(bool quiet = false)
    {
        if (!_services.Updates.IsInstalled)
        {
            if (!quiet) UpdateStatus = "Updates work in installed copies; this one runs from a build folder.";
            return;
        }

        UpdateStatus = "Checking for updates…";
        try
        {
            var version = await _services.Updates.CheckAndDownloadAsync();
            IsUpdateReady = version is not null;
            UpdateStatus = version is null ? "Mountly is up to date." : $"Mountly {version} is ready. Restart to update.";
            if (version is not null)
                _services.Notices.Show(NoticeKind.Info, $"Mountly {version} is ready", "Restart Mountly to update.",
                    new NoticeAction("Restart now", new RelayCommand(_services.Updates.ApplyAndRestart)));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            UpdateStatus = quiet ? null : "Couldn't check for updates: " + ex.Message;
        }
    }

    [RelayCommand]
    private void RestartToUpdate() => _services.Updates.ApplyAndRestart();

    [RelayCommand]
    private void OpenRepository() => DesktopIntegration.OpenUrl(RepositoryUrl);

    [RelayCommand]
    private void OpenDataFolder() => DesktopIntegration.Open(AppPaths.ConfigDirectory);
}
