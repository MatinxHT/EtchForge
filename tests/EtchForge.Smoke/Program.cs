using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using EtchForge;
using EtchForge.Models;
using EtchForge.Services;
using System.Reflection;

AppPreferences.SetLanguage(UiLanguage.Chinese, persist: false);

var work = Path.Combine(Path.GetTempPath(), "etchforge-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var input = Path.Combine(work, "tiny.img");
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

    AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();
    var window = new MainWindow();
    window.Show();
    Dispatcher.UIThread.RunJobs();
    if (Math.Abs(window.ClientSize.Width / window.ClientSize.Height - 21d / 9d) > 0.001)
        throw new Exception("主窗口不是 21:9 比例。");
    if (window.FindControl<ComboBox>("FormatPicker")?.ItemCount != OutputFormat.All.Count)
        throw new Exception("格式选择器未正确加载。");
    if (window.FindControl<Button>("SettingsButton") is null ||
        window.FindControl<Button>("OpenOutputButton") is null ||
        window.FindControl<Button>("ChooseFolderButton") is null)
        throw new Exception("三段式流程控件缺失。");
    using var frame = window.CaptureRenderedFrame() ?? throw new Exception("界面未渲染。");
    if (args.Length > 0) frame.Save(args[0], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    Console.WriteLine($"Avalonia 界面加载与渲染通过：{frame.PixelSize.Width} × {frame.PixelSize.Height}");
    if (args.Length > 1)
    {
        SetField("_image", image);
        SetField("_outputDirectory", work);
        SetField("_lastOutputFiles", outputs);
        SetField("_lastElapsed", TimeSpan.FromSeconds(2.4));
        SetField("_lastFormatId", "esxi");
        window.FindControl<StackPanel>("SourceDetails")!.IsVisible = true;
        window.FindControl<TextBlock>("SourceName")!.Text = image.FileName;
        window.FindControl<TextBox>("ShaText")!.Text = image.Sha256;
        window.FindControl<TextBox>("Md5Text")!.Text = image.Md5;
        window.FindControl<Button>("CopyShaButton")!.IsEnabled = true;
        window.FindControl<Button>("CopyMd5Button")!.IsEnabled = true;
        Invoke("UpdateSourceSummary");
        Invoke("UpdateResultSummary");
        Invoke("RefreshPreview");
        window.FindControl<StackPanel>("ConversionPanel")!.IsVisible = true;
        window.FindControl<ProgressBar>("ConversionProgress")!.Value = 100;
        window.FindControl<TextBlock>("ProgressPercent")!.Text = "100%";
        typeof(MainWindow).GetMethod("SetConversionStatus", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["completed", null]);
        Dispatcher.UIThread.RunJobs();
        if (!window.FindControl<StackPanel>("ResultPanel")!.IsVisible ||
            window.FindControl<TextBlock>("ResultElapsedText")!.Text != "2.4 s")
            throw new Exception("右侧产物信息或转换用时未显示。");
        using var selectedFrame = window.CaptureRenderedFrame() ?? throw new Exception("镜像详情未渲染。");
        selectedFrame.Save(args[1], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    AppPreferences.SetLanguage(UiLanguage.English, persist: false);
    Invoke("ApplyLanguage");
    if ((string?)window.FindControl<Button>("ConvertButton")!.Content != "Convert" ||
        (string?)window.FindControl<Button>("OpenOutputButton")!.Content != "Open output folder" ||
        (args.Length > 1 && !window.FindControl<StackPanel>("ResultPanel")!.IsVisible) ||
        (window.FindControl<StackPanel>("ConversionPanel")!.IsVisible &&
         window.FindControl<TextBlock>("ConversionStatus")!.Text != "Conversion complete"))
        throw new Exception("英文界面未正确切换。");
    var settings = new SettingsWindow();
    if (settings.FindControl<TextBlock>("LanguageLabel")?.Text != "Display language" ||
        settings.FindControl<ComboBox>("LanguagePicker")?.SelectedIndex != 1)
        throw new Exception("设置窗口语言选项未正确加载。");
    if (args.Length > 2)
    {
        settings.Show();
        Dispatcher.UIThread.RunJobs();
        using var settingsFrame = settings.CaptureRenderedFrame() ?? throw new Exception("设置窗口未渲染。");
        settingsFrame.Save(args[2], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    settings.Close();
    AppPreferences.SetLanguage(UiLanguage.Chinese, persist: false);
    window.Close();

    void SetField(string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    void Invoke(string name) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
}
finally { Directory.Delete(work, recursive: true); }
