using Avalonia;
using Mountly.App.Services;
using Velopack;

namespace Mountly.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles install/update/uninstall hooks when started by the Velopack installer.
        var velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
            velopack = velopack
                .OnAfterUpdateFastCallback(_ => DesktopIntegration.RefreshAfterUpdate())
                .OnBeforeUninstallFastCallback(_ => DesktopIntegration.RemoveAll());
        velopack.Run();

        // A second start (Explorer's "Mount with Mountly", a double-clicked image, the Start menu) hands its
        // request to the running Mountly and exits, so there is only ever one window and one tray icon.
        var request = StartupRequest.Parse(args);
        if (SingleInstance.TryForward(request)) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
