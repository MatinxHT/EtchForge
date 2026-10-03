using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace EtchForge;

public partial class App : Application
{
    private static IServiceProvider? _services;

    public static void ConfigureServices(IServiceProvider services) => _services = services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = (_services ?? throw new InvalidOperationException("Host is not configured."))
                .GetRequiredService<MainWindow>();
        base.OnFrameworkInitializationCompleted();
    }
}
