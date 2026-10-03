using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using EtchForge.Models;

namespace EtchForge.Services;

public interface IDesktopInteraction
{
    Task<string?> ChooseImageAsync(string title);
    Task<string?> ChooseFolderAsync(string title);
    Task CopyTextAsync(string text);
    void OpenFolder(string path);
    Task ShowSettingsAsync();
    Task ShowImageDetailsAsync(ImageInfo image, UiLanguage language);
}

public sealed class DesktopInteraction(IServiceProvider services) : IDesktopInteraction
{
    private static MainWindow Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow
        ?? throw new InvalidOperationException("Main window is unavailable.");

    public async Task<string?> ChooseImageAsync(string title)
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Disk images") { Patterns = ["*.img", "*.raw", "*.dd", "*.ima", "*.bin", "*.vmdk", "*.vhd", "*.vhdx", "*.qcow2", "*.vdi", "*.iso", "*.dmg"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> ChooseFolderAsync(string title)
    {
        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    public Task CopyTextAsync(string text) => Owner.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;

    public void OpenFolder(string path)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open")
                : new ProcessStartInfo("xdg-open");
        start.ArgumentList.Add(path);
        Process.Start(start);
    }

    public Task ShowSettingsAsync() => services.GetRequiredService<SettingsWindow>().ShowDialog(Owner);

    public Task ShowImageDetailsAsync(ImageInfo image, UiLanguage language) =>
        new ImageDetailsWindow(image, language).ShowDialog(Owner);
}
