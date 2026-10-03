using Avalonia;

namespace EtchForge;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var host = AppHost.Create(args);
        host.StartAsync().GetAwaiter().GetResult();
        App.ConfigureServices(host.Services);
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { host.StopAsync().GetAwaiter().GetResult(); }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
