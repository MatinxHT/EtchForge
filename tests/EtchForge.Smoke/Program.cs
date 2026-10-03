using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Buffers.Binary;
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
    bytes[446] = 0x80;
    bytes[450] = 0x83;
    BitConverter.GetBytes(1u).CopyTo(bytes, 454);
    BitConverter.GetBytes(2047u).CopyTo(bytes, 458);
    Random.Shared.NextBytes(bytes.AsSpan(512));
    await File.WriteAllBytesAsync(input, bytes);
    var image = await ImageInspector.InspectAsync(input, null, CancellationToken.None);
    if (image.Bytes != bytes.Length || image.PartitionScheme != "MBR" || image.Partitions.Count != 1 ||
        image.BootHints != "MBR 活动分区" || image.Sha256.Length != 64 || image.Md5.Length != 32)
        throw new Exception("镜像检查结果不正确。");

    var opticalPath = Path.Combine(work, "installer.iso");
    var opticalBytes = new byte[1024 * 1024];
    opticalBytes[16 * 2048] = 1;
    "CD001"u8.CopyTo(opticalBytes.AsSpan(16 * 2048 + 1));
    "INSTALLER"u8.CopyTo(opticalBytes.AsSpan(16 * 2048 + 40));
    await File.WriteAllBytesAsync(opticalPath, opticalBytes);
    var optical = await ImageInspector.InspectAsync(opticalPath, null, CancellationToken.None);
    if (!optical.IsOptical || optical.Partitions.Single().Label != "INSTALLER")
        throw new Exception("ISO 镜像信息未识别。");
    try
    {
        await ConversionService.ConvertAsync(optical, OutputFormat.All[0], work, null, CancellationToken.None);
        throw new Exception("ISO 不应被转换成硬盘镜像。");
    }
    catch (InvalidDataException) { }

    var gptPath = Path.Combine(work, "gpt.raw");
    var gptBytes = CreateGptImage();
    await File.WriteAllBytesAsync(gptPath, gptBytes);
    var gpt = await ImageInspector.InspectAsync(gptPath, null, CancellationToken.None);
    if (gpt.PartitionScheme != "GPT" || gpt.LayoutHealth != "GPT 主/备表头与分区项 CRC 正常" ||
        gpt.Partitions.Single().Type != "EFI 系统分区" || gpt.Partitions.Single().FileSystem != "FAT32")
        throw new Exception("GPT 或文件系统解析失败。");
    gptBytes[512 + 16] ^= 1;
    await File.WriteAllBytesAsync(gptPath, gptBytes);
    gpt = await ImageInspector.InspectAsync(gptPath, null, CancellationToken.None);
    if (!gpt.LayoutHealth.Contains("主 GPT 表头 CRC 错误")) throw new Exception("GPT 损坏未被检测。");

    var outputs = await ConversionService.ConvertAsync(image, OutputFormat.All[0], work, null, CancellationToken.None);
    if (outputs.Count != 2) throw new Exception("ESXi VMDK 文件对不完整。");
    var flat = outputs.Single(x => x.EndsWith("-flat.vmdk", StringComparison.Ordinal));
    if (!File.ReadAllBytes(flat).AsSpan().SequenceEqual(bytes)) throw new Exception("磁盘数据发生变化。");
    var descriptor = outputs.Single(x => !x.EndsWith("-flat.vmdk", StringComparison.Ordinal));
    if (!File.ReadAllText(descriptor).Contains("RW 2048 VMFS \"tiny-esxi-flat.vmdk\""))
        throw new Exception("VMDK 描述符的扇区数或数据文件名错误。");
    if (!OperatingSystem.IsWindows())
    {
        var fakeQemu = Path.Combine(work, "fake-qemu-img");
        await File.WriteAllTextAsync(fakeQemu, """
            #!/bin/sh
            command="$1"
            shift
            if [ "$command" = "info" ]; then
              for arg in "$@"; do path="$arg"; done
              size=$(wc -c < "$path" | tr -d ' ')
              printf '{"format":"qcow2","virtual-size":%s,"actual-size":%s}\n' "$size" "$size"
              exit 0
            fi
            if [ "$command" = "dd" ]; then
              for arg in "$@"; do
                case "$arg" in
                  if=*) input="${arg#if=}" ;;
                  of=*) output="${arg#of=}" ;;
                  skip=*) skip="$arg" ;;
                  count=*) count="$arg" ;;
                esac
              done
              /bin/dd "if=$input" "of=$output" bs=512 "$skip" "$count" 2>/dev/null
              exit $?
            fi
            if [ "$command" = "convert" ]; then
              previous=""
              for arg in "$@"; do previous_previous="$previous"; previous="$arg"; done
              /bin/cp "$previous_previous" "$previous"
              exit $?
            fi
            exit 1
            """ + "\n");
        File.SetUnixFileMode(fakeQemu, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var savedQemu = Environment.GetEnvironmentVariable("QEMU_IMG_PATH");
        Environment.SetEnvironmentVariable("QEMU_IMG_PATH", fakeQemu);
        try
        {
            var containerPath = Path.Combine(work, "container.qcow2");
            await File.WriteAllBytesAsync(containerPath, bytes);
            var container = await ImageInspector.InspectAsync(containerPath, null, CancellationToken.None);
            if (container.Format != "qcow2" || container.Partitions.Count != 1 || container.VirtualBytes != bytes.Length)
                throw new Exception("容器元数据或虚拟扇区读取失败。");
            var converted = await ConversionService.ConvertAsync(container, OutputFormat.All[0], work, null, CancellationToken.None);
            var convertedFlat = converted.Single(x => x.EndsWith("-flat.vmdk", StringComparison.Ordinal));
            if (!File.ReadAllBytes(convertedFlat).AsSpan().SequenceEqual(bytes))
                throw new Exception("容器到 ESXi 的数据不一致。");
            var vdi = OutputFormat.All.Single(x => x.Id == "vdi");
            if ((await ConversionService.ConvertAsync(container, vdi, work, null, CancellationToken.None)).Count != 1)
                throw new Exception("容器到 VDI 的转换调用失败。");
        }
        finally { Environment.SetEnvironmentVariable("QEMU_IMG_PATH", savedQemu); }
    }
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
    if (desktop.CopiedText != image.Sha256 || !viewModel.ConvertEnabled || !viewModel.DetailsEnabled)
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
        settings.FindControl<ComboBox>("LanguagePicker")?.SelectedIndex != 1 ||
        settings.FindControl<Button>("WindowsInstallButton") is null ||
        settings.FindControl<Button>("UnixInstallButton") is null)
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
    var details = new ImageDetailsWindow(image, UiLanguage.Chinese);
    details.Show();
    Dispatcher.UIThread.RunJobs();
    if (details.Title != "镜像详情" || details.Content is not ScrollViewer)
        throw new Exception("镜像详情窗口未加载。");
    var detailsPanel = (StackPanel)((ScrollViewer)details.Content).Content!;
    var expectedSha = detailsPanel.Children.OfType<TextBox>().First();
    var hashComparison = detailsPanel.Children.OfType<TextBlock>().Single(x => x.Name == "HashComparisonText");
    expectedSha.Text = image.Sha256;
    Dispatcher.UIThread.RunJobs();
    if (hashComparison.Text != "一致") throw new Exception("SHA-256 核对失败。");
    if (args.Length > 2)
    {
        using var detailsFrame = details.CaptureRenderedFrame() ?? throw new Exception("详情窗口未渲染。");
        detailsFrame.Save(args[2], Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    details.Close();
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

static byte[] CreateGptImage()
{
    const int sector = 512, totalSectors = 4096;
    var data = new byte[sector * totalSectors];
    data[510] = 0x55; data[511] = 0xAA; data[450] = 0xEE;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(454, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(458, 4), totalSectors - 1);
    var entries = new byte[128 * 128];
    Guid.Parse("c12a7328-f81f-11d2-ba4b-00a0c93ec93b").TryWriteBytes(entries);
    Guid.NewGuid().TryWriteBytes(entries.AsSpan(16));
    BinaryPrimitives.WriteUInt64LittleEndian(entries.AsSpan(32, 8), 2048);
    BinaryPrimitives.WriteUInt64LittleEndian(entries.AsSpan(40, 8), 3071);
    System.Text.Encoding.Unicode.GetBytes("EFI").CopyTo(entries, 56);
    entries.CopyTo(data, 2 * sector);
    entries.CopyTo(data, (totalSectors - 33) * sector);
    var entryCrc = Crc(entries);
    var diskId = Guid.NewGuid();
    WriteHeader(data.AsSpan(sector, sector), 1, totalSectors - 1, 2, entryCrc, diskId);
    WriteHeader(data.AsSpan((totalSectors - 1) * sector, sector), totalSectors - 1, 1,
        totalSectors - 33, entryCrc, diskId);
    var fat = data.AsSpan(2048 * sector, sector);
    "FAT32"u8.CopyTo(fat[82..]);
    "EFI"u8.CopyTo(fat[71..]);
    return data;
}

static void WriteHeader(Span<byte> target, int current, int backup, int entriesLba, uint entryCrc, Guid diskId)
{
    "EFI PART"u8.CopyTo(target);
    BinaryPrimitives.WriteUInt32LittleEndian(target[8..12], 0x00010000);
    BinaryPrimitives.WriteUInt32LittleEndian(target[12..16], 92);
    BinaryPrimitives.WriteUInt64LittleEndian(target[24..32], (ulong)current);
    BinaryPrimitives.WriteUInt64LittleEndian(target[32..40], (ulong)backup);
    BinaryPrimitives.WriteUInt64LittleEndian(target[40..48], 34);
    BinaryPrimitives.WriteUInt64LittleEndian(target[48..56], 4062);
    diskId.TryWriteBytes(target[56..72]);
    BinaryPrimitives.WriteUInt64LittleEndian(target[72..80], (ulong)entriesLba);
    BinaryPrimitives.WriteUInt32LittleEndian(target[80..84], 128);
    BinaryPrimitives.WriteUInt32LittleEndian(target[84..88], 128);
    BinaryPrimitives.WriteUInt32LittleEndian(target[88..92], entryCrc);
    BinaryPrimitives.WriteUInt32LittleEndian(target[16..20], Crc(target[..92]));
}

static uint Crc(ReadOnlySpan<byte> bytes)
{
    uint crc = 0xFFFFFFFF;
    foreach (var value in bytes)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
    }
    return ~crc;
}

sealed class FakeDesktopInteraction(string imagePath, string outputDirectory) : IDesktopInteraction
{
    public string? CopiedText { get; private set; }
    public Task<string?> ChooseImageAsync(string title) => Task.FromResult<string?>(imagePath);
    public Task<string?> ChooseFolderAsync(string title) => Task.FromResult<string?>(outputDirectory);
    public Task CopyTextAsync(string text) { CopiedText = text; return Task.CompletedTask; }
    public void OpenFolder(string path) { }
    public Task ShowSettingsAsync() => Task.CompletedTask;
    public Task ShowImageDetailsAsync(ImageInfo image, UiLanguage language) => Task.CompletedTask;
}
