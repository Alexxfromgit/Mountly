using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mountly.App.Services;
using AppIcons = Mountly.App.Controls.Icons;

namespace Mountly.App.ViewModels;

public sealed record NavItem(string Title, Geometry Icon);

public sealed partial class MainWindowViewModel : ObservableObject
{
    public const int DrivesPage = 0;
    public const int ImagesPage = 1;
    public const int ToolsPage = 2;
    public const int SettingsPage = 3;

    public MainWindowViewModel(AppServices services)
    {
        Services = services;
        Drives = new DrivesViewModel(services);
        Tools = new ToolsViewModel(services, Drives);
        Catalog = new CatalogViewModel(services, Drives, Tools);
        Settings = new SettingsViewModel(services);
        Tools.ShowRequested += (_, _) => Page = ToolsPage;
    }

    public AppServices Services { get; }
    public DrivesViewModel Drives { get; }
    public CatalogViewModel Catalog { get; }
    public ToolsViewModel Tools { get; }
    public SettingsViewModel Settings { get; }
    public Notices Notices => Services.Notices;

    public IReadOnlyList<NavItem> Nav { get; } =
    [
        new("Drives", AppIcons.Disc),
        new("Images", AppIcons.Library),
        new("Tools", AppIcons.Tools),
        new("Settings", AppIcons.Settings),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDrives), nameof(IsImages), nameof(IsTools), nameof(IsSettings))]
    public partial int Page { get; set; }

    public bool IsDrives => Page == DrivesPage;
    public bool IsImages => Page == ImagesPage;
    public bool IsTools => Page == ToolsPage;
    public bool IsSettings => Page == SettingsPage;

    public string Version => "v" + UpdateService.CurrentVersion;

    partial void OnPageChanged(int value)
    {
        if (value == ImagesPage) Catalog.Reload();
    }

    /// <summary>Acts on a start request: Explorer verbs, a double-clicked image, or a second start.</summary>
    public async Task HandleAsync(StartupRequest request)
    {
        if (request.Path is not { } path) return;
        switch (request.Action)
        {
            case StartupAction.Mount:
                Page = DrivesPage;
                await Drives.MountImagesAsync([path]);
                break;
            case StartupAction.Convert:
                Tools.OpenConvert(path);
                break;
            case StartupAction.Verify:
                Tools.OpenVerify(path);
                break;
            case StartupAction.CreateIso:
                Tools.OpenCreateIso(path);
                break;
        }
    }
}
