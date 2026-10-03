using EtchForge.Services;
using EtchForge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EtchForge;

public static class AppHost
{
    public static IHost Create(string[] args, Action<IServiceCollection>? configure = null)
    {
        // A desktop app does not need working-directory configuration or console logging defaults.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            DisableDefaults = true
        });
        builder.Services.AddSingleton<IAppPreferences>(_ => new AppPreferences());
        builder.Services.AddSingleton<IImageInspector, ImageInspectorAdapter>();
        builder.Services.AddSingleton<IImageConverter, ImageConverterAdapter>();
        builder.Services.AddSingleton<IDesktopInteraction, DesktopInteraction>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<SettingsWindow>();
        configure?.Invoke(builder.Services);
        return builder.Build();
    }
}
