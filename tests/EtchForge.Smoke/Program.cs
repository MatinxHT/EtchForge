using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using EtchForge;
using EtchForge.Models;
using EtchForge.Services;
using EtchForge.ViewModels;
using Microsoft.Extensions.DependencyInjection;

var work = Path.Combine(Path.GetTempPath(), "etchforge-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var input = Path.Combine(work, "tiny.img");
    var outputDirectory = Path.Combine(work, "ui-output");
    var settingsPath = Path.Combine(work, "settings.json");
    Directory.CreateDirectory(outputDirectory);
    var bytes = new byte[1024 * 1024];
    bytes[510] = 0x55;
    bytes[511] = 0xAA;
    Random.Shared.NextBytes(bytes.AsSpan(512));
    await File.WriteAllBytesAsync(input, bytes);
    var image = await ImageInspector.InspectAsync(input, null, CancellationToken.None);
    if (image.Bytes != bytes.Length || image.PartitionScheme != "MBR" || image.Sha256.Length != 64 || image.Md5.Length != 32)
        throw new Exception("镜像检查结果不正确。");

    var outputs = await ConversionService.ConvertAsync(image, OutputFormat.All[0], work, null, CancellationToken.None);
    if (outputs.Count != 2) throw new Exception("ESXi VMDK 文件对不完整。");
    var flat = outputs.Single(x => x.EndsWith("-flat.vmdk", StringComparison.Ordinal));
    if (!File.ReadAllBytes(flat).AsSpan().SequenceEqual(bytes)) throw new Exception("磁盘数据发生变化。");
    var descriptor = outputs.Single(x => !x.EndsWith("-flat.vmdk", StringComparison.Ordinal));
    if (!File.ReadAllText(descriptor).Contains("RW 2048 VMFS \"tiny-esxi-flat.vmdk\""))
        throw new Exception("VMDK 描述符的扇区数或数据文件名错误。");
    Console.WriteLine("镜像读取、SHA-256/MD5 和 ESXi VMDK 转换通过。");

    var desktop = new FakeDesktopInteraction(input, outputDirectory);
    var preferences = new AppPreferences(settingsPath);
    using var host = AppHost.Create([], services =>
    {
        services.AddSingleton<IAppPreferences>(preferences);
        services.AddSingleton<IDesktopInteraction>(desktop);
    });
    await host.StartAsync();
    App.ConfigureServices(host.Services);
    AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();

    var viewModel = host.Services.GetRequiredService<MainWindowViewModel>();
    Complete(viewModel.ImportCommand.ExecuteAsync(null));
    Complete(viewModel.ChooseFolderCommand.ExecuteAsync(null));
    Complete(viewModel.CopyShaCommand.ExecuteAsync(null));
    if (desktop.CopiedText != image.Sha256 || !viewModel.ConvertEnabled)
        throw new Exception("导入、文件夹选择或校验值复制命令失败。");
    Complete(viewModel.ConvertCommand.ExecuteAsync(null));
    if (!viewModel.ResultPanelVisible || viewModel.ResultInfoText != "2 个文件 · 1.0 MiB" ||
        !File.Exists(Path.Combine(outputDirectory, "tiny-esxi-flat.vmdk")))
        throw new Exception("ViewModel 转换或产物状态失败。");

    var window = host.Services.GetRequiredService<MainWindow>();
    if (!ReferenceEquals(window.DataContext, viewModel)) throw new Exception("主窗口未通过 Host 注入 ViewModel。");
    window.Show();
    Dispatcher.UIThread.RunJobs();
    if (Math.Abs(window.ClientSize.Width / window.ClientSize.Height - 21d / 9d) > 0.001)
        throw new Exception("主窗口不是 21:9 比例。");
    if (window.FindControl<ComboBox>("FormatPicker")?.ItemCount != OutputFormat.All.Count ||
        window.FindControl<StackPanel>("ResultPanel")?.IsVisible != true)
        throw new Exception("绑定后的格式或结果控件未正确加载。");
    using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("界面未渲染。"))
    {
        if (args.Length > 0) frame.Save(args[0], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Console.WriteLine($"Host + MVVM 界面加载与渲染通过：{frame.PixelSize.Width} × {frame.PixelSize.Height}");
    }

    var settingsViewModel = host.Services.GetRequiredService<SettingsViewModel>();
    settingsViewModel.SelectedLanguageIndex = 1;
    Dispatcher.UIThread.RunJobs();
    if (viewModel.ConvertLabel != "Convert" ||
        window.FindControl<Button>("OpenOutputButton")?.Content is not "Open output folder" ||
        new AppPreferences(settingsPath).Language != UiLanguage.English)
        throw new Exception("英文界面或语言持久化失败。");
    var settings = host.Services.GetRequiredService<SettingsWindow>();
    settings.Show();
    Dispatcher.UIThread.RunJobs();
    if (settings.FindControl<TextBlock>("LanguageLabel")?.Text != "Display language" ||
        settings.FindControl<ComboBox>("LanguagePicker")?.SelectedIndex != 1)
        throw new Exception("设置窗口绑定失败。");
    if (args.Length > 1)
    {
        using var settingsFrame = settings.CaptureRenderedFrame() ?? throw new Exception("设置窗口未渲染。");
        settingsFrame.Save(args[1], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    settings.FindControl<ComboBox>("LanguagePicker")!.SelectedIndex = 0;
    Dispatcher.UIThread.RunJobs();
    if (viewModel.ConvertLabel != "开始转换" ||
        new AppPreferences(settingsPath).Language != UiLanguage.Chinese)
        throw new Exception("设置窗口切换回中文失败。");
    settings.Close();
    window.Close();
    Complete(host.StopAsync());

    static void Complete(Task task)
    {
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
        task.GetAwaiter().GetResult();
    }
}
finally { Directory.Delete(work, recursive: true); }

sealed class FakeDesktopInteraction(string imagePath, string outputDirectory) : IDesktopInteraction
{
    public string? CopiedText { get; private set; }
    public Task<string?> ChooseImageAsync(string title) => Task.FromResult<string?>(imagePath);
    public Task<string?> ChooseFolderAsync(string title) => Task.FromResult<string?>(outputDirectory);
    public Task CopyTextAsync(string text) { CopiedText = text; return Task.CompletedTask; }
    public void OpenFolder(string path) { }
    public Task ShowSettingsAsync() => Task.CompletedTask;
}
