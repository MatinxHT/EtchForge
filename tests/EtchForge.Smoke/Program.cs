using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using EtchForge;
using EtchForge.Models;
using EtchForge.Services;

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
    using var frame = window.CaptureRenderedFrame() ?? throw new Exception("界面未渲染。");
    if (args.Length > 0) frame.Save(args[0], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    Console.WriteLine($"Avalonia 界面加载与渲染通过：{frame.PixelSize.Width} × {frame.PixelSize.Height}");
    if (args.Length > 1)
    {
        window.FindControl<StackPanel>("SourceDetails")!.IsVisible = true;
        window.FindControl<TextBlock>("SourceName")!.Text = image.FileName;
        window.FindControl<TextBlock>("SourceSummary")!.Text = $"{image.SizeText}\n{image.PartitionScheme}";
        window.FindControl<TextBox>("ShaText")!.Text = image.Sha256;
        window.FindControl<TextBox>("Md5Text")!.Text = image.Md5;
        window.FindControl<Button>("CopyShaButton")!.IsEnabled = true;
        window.FindControl<Button>("CopyMd5Button")!.IsEnabled = true;
        Dispatcher.UIThread.RunJobs();
        using var selectedFrame = window.CaptureRenderedFrame() ?? throw new Exception("镜像详情未渲染。");
        selectedFrame.Save(args[1], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    window.Close();
}
finally { Directory.Delete(work, recursive: true); }
