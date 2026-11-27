using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mountly.App.Services;
using Mountly.App.ViewModels;
using Mountly.App.Views;
using Mountly.Core.Mounting;
using Mountly.Core.Storage;
using Mountly.Formats;
using Mountly.Windows;

namespace Mountly.App;

public partial class App : Application
{
    private MainWindow? _window;
    private MainWindowViewModel? _viewModel;
    private IDisposable? _listener;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || !OperatingSystem.IsWindows())
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var settings = new SettingsStore();
        SettingsViewModel.Apply(settings.Current.Theme); // before the first window, so it never flashes the other theme
        var firstRun = !File.Exists(AppPaths.SlotsFile);

        _window = new MainWindow();
        var services = new AppServices(
            new MountService(new StorageMountBackend(), new IsoConverter()),
            settings,
            new SlotStore(),
            new CatalogStore(),
            new DialogService(_window),
            new UpdateService(),
            new Notices());
        if (firstRun) services.Slots.Update(list => list.Add(new DriveSlot())); // one empty drive to start with

        _viewModel = new MainWindowViewModel(services);
        _window.DataContext = _viewModel;
        _window.Closing += OnWindowClosing;
        _window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) _viewModel.Drives.SetPolling(_window.IsVisible);
        };

        SetUpTray(desktop);
        _viewModel.Drives.Changed += (_, _) => RebuildTrayMenu();

        var request = StartupRequest.Parse(desktop.Args ?? []);
        _listener = SingleInstance.Listen(r => Dispatcher.UIThread.Post(() => Handle(r)));
        desktop.ShutdownRequested += (_, _) => _listener?.Dispose();

        if (!request.Tray) ShowWindow();
        _ = StartAsync(services, request);

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(AppServices services, StartupRequest request)
    {
        DesktopIntegration.Apply(services.Settings.Current); // keeps registrations pointing at this exe
        if (services.Settings.Current.RemountOnStart) await _viewModel!.Drives.RemountSavedAsync();
        else await _viewModel!.Drives.RefreshAsync();
        await _viewModel.HandleAsync(request);
        if (services.Settings.Current.CheckForUpdates) await _viewModel.Settings.CheckNowAsync(quiet: true);
    }

    private void Handle(StartupRequest request)
    {
        if (!request.Tray || request.Path is not null) ShowWindow();
        _ = _viewModel?.HandleAsync(request);
    }

    public void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _ = _viewModel?.Drives.RefreshAsync();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting || _viewModel is null) return;
        if (_viewModel.Services.Settings.Current.CloseToTray)
        {
            // Mounted images stay mounted either way; the tray keeps Mountly one click away.
            e.Cancel = true;
            _window!.Hide();
            return;
        }

        Exit();
    }

    private void Exit()
    {
        _exiting = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    private void SetUpTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (TrayIcon.GetIcons(this) is not { Count: > 0 } icons) return;
        icons[0].Clicked += (_, _) => ShowWindow();
        RebuildTrayMenu();
    }

    // The tray menu lists the mounted drives, each one click from ejecting.
    private void RebuildTrayMenu()
    {
        if (TrayIcon.GetIcons(this) is not { Count: > 0 } icons || _viewModel is null) return;
        var menu = new NativeMenu();
        menu.Items.Add(Item("Open Mountly", ShowWindow));
        menu.Items.Add(Item("Mount image…", () =>
        {
            ShowWindow();
            _viewModel.Drives.MountImageCommand.Execute(null);
        }));

        var mounted = _viewModel.Drives.Drives.Where(d => d.IsMounted).ToList();
        if (mounted.Count > 0)
        {
            menu.Items.Add(new NativeMenuItemSeparator());
            foreach (var drive in mounted)
            {
                var item = new NativeMenuItem($"{drive.Letter}  {Shorten(drive.ImageName)}") { Menu = new NativeMenu() };
                if (drive.CanOpen) item.Menu.Items.Add(Item("Open", () => drive.OpenCommand.Execute(null)));
                item.Menu.Items.Add(Item("Eject", () => drive.EjectCommand.Execute(null)));
                menu.Items.Add(item);
            }

            menu.Items.Add(Item("Eject all", () => _viewModel.Drives.EjectAllCommand.Execute(null)));
        }

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Exit", Exit));
        icons[0].Menu = menu;
        icons[0].ToolTipText = "Mountly · " + _viewModel.Drives.Summary;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..37] + "…";
}
