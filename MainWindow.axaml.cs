using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using EtchForge.Models;
using EtchForge.Services;

namespace EtchForge;

public partial class MainWindow : Window
{
    private ImageInfo? _image;
    private string? _outputDirectory;
    private CancellationTokenSource? _job;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        FormatPicker.ItemsSource = OutputFormat.All;
        FormatPicker.SelectedIndex = 0;
        RefreshEngine();
        RefreshPreview();
        Closed += (_, _) => _job?.Cancel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        FormatPicker = this.FindControl<ComboBox>("FormatPicker")!;
        EngineStatus = this.FindControl<TextBlock>("EngineStatus")!;
        ImportButton = this.FindControl<Button>("ImportButton")!;
        SourceName = this.FindControl<TextBlock>("SourceName")!;
        SourceSummary = this.FindControl<TextBlock>("SourceSummary")!;
        SourceDetails = this.FindControl<StackPanel>("SourceDetails")!;
        ShaText = this.FindControl<TextBox>("ShaText")!;
        Md5Text = this.FindControl<TextBox>("Md5Text")!;
        ImportFeedback = this.FindControl<StackPanel>("ImportFeedback")!;
        ImportStatus = this.FindControl<TextBlock>("ImportStatus")!;
        ImportProgress = this.FindControl<ProgressBar>("ImportProgress")!;
        FormatDescription = this.FindControl<TextBlock>("FormatDescription")!;
        ConversionStatus = this.FindControl<TextBlock>("ConversionStatus")!;
        ConversionProgress = this.FindControl<ProgressBar>("ConversionProgress")!;
        ConversionPanel = this.FindControl<StackPanel>("ConversionPanel")!;
        ProgressPercent = this.FindControl<TextBlock>("ProgressPercent")!;
        ConvertButton = this.FindControl<Button>("ConvertButton")!;
        CancelButton = this.FindControl<Button>("CancelButton")!;
        OutputDirectoryText = this.FindControl<TextBlock>("OutputDirectoryText")!;
        OutputPreview = this.FindControl<TextBox>("OutputPreview")!;
        OpenOutputButton = this.FindControl<Button>("OpenOutputButton")!;
    }

    private void RefreshEngine()
    {
        EngineStatus.Text = ConversionService.FindQemuImg() is null
            ? "内置 ESXi 转换可用 · 其他格式需 qemu-img"
            : "转换引擎已就绪 · 全部格式可用";
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 raw IMG 磁盘镜像",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("磁盘镜像 (*.img)") { Patterns = ["*.img", "*.IMG"] }]
        });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        _image = null;
        _busy = true;
        ImportButton.IsEnabled = false;
        SourceDetails.IsVisible = true;
        ImportFeedback.IsVisible = true;
        ShaText.Text = "正在计算…";
        Md5Text.Text = "正在计算…";
        SourceName.Text = Path.GetFileName(path);
        SourceSummary.Text = "读取镜像结构并计算校验值";
        ImportStatus.Text = "正在读取镜像…";
        ImportProgress.Value = 0;
        RefreshPreview();
        _job = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(value =>
            {
                ImportProgress.Value = value * 100;
                ImportStatus.Text = $"计算 SHA-256 / MD5 · {value:P0}";
            });
            _image = await Task.Run(() => ImageInspector.InspectAsync(path, progress, _job.Token));
            _outputDirectory ??= Path.Combine(Path.GetDirectoryName(path)!, "converted");
            SourceSummary.Text = $"{_image.SizeText}\n{_image.PartitionScheme}";
            ShaText.Text = _image.Sha256;
            Md5Text.Text = _image.Md5;
            ImportProgress.Value = 100;
            ImportStatus.Text = "校验完成 · 可开始转换";
            ImportFeedback.IsVisible = false;
        }
        catch (Exception ex)
        {
            SourceSummary.Text = "镜像读取失败";
            ShaText.Text = "—";
            Md5Text.Text = "—";
            ImportStatus.Text = ex.Message;
        }
        finally
        {
            _job.Dispose();
            _job = null;
            _busy = false;
            ImportButton.IsEnabled = true;
            RefreshPreview();
        }
    }

    private async void Output_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择转换产物的保存位置",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        _outputDirectory = folders[0].Path.LocalPath;
        RefreshPreview();
    }

    private void Format_Changed(object? sender, SelectionChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (FormatPicker.SelectedItem is not OutputFormat format) return;
        FormatDescription.Text = format.Description;
        OutputDirectoryText.Text = _outputDirectory ?? "导入后建议保存位置";
        if (_image is null || _outputDirectory is null)
            OutputPreview.Text = "选择镜像后显示产物路径";
        else
        {
            var baseName = Path.GetFileNameWithoutExtension(_image.Path) + "-" + format.Id;
            var paths = new List<string> { Path.Combine(_outputDirectory, baseName + format.Extension) };
            if (format.IsNativeEsxi) paths.Add(Path.Combine(_outputDirectory, baseName + "-flat.vmdk"));
            OutputPreview.Text = string.Join("\n", paths);
        }
        ConvertButton.IsEnabled = !_busy && _image is not null && _outputDirectory is not null &&
            (format.IsNativeEsxi || ConversionService.FindQemuImg() is not null);
        if (!format.IsNativeEsxi && ConversionService.FindQemuImg() is null && !_busy)
            ConversionStatus.Text = "此格式需要安装 qemu-img";
        else if (!_busy && ConversionStatus.Text == "此格式需要安装 qemu-img")
            ConversionStatus.Text = "等待开始";
    }

    private async void Convert_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || _image is null || _outputDirectory is null || FormatPicker.SelectedItem is not OutputFormat format)
            return;
        _busy = true;
        _job = new CancellationTokenSource();
        ConvertButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        CancelButton.IsVisible = true;
        ConversionPanel.IsVisible = true;
        ConversionProgress.Value = 0;
        ProgressPercent.Text = "0%";
        ConversionStatus.Text = "准备转换…";
        try
        {
            Directory.CreateDirectory(_outputDirectory);
            var progress = new Progress<double>(value =>
            {
                ConversionProgress.Value = value * 100;
                ProgressPercent.Text = $"{value:P0}";
                ConversionStatus.Text = value >= 1 ? "转换完成" : "正在转换镜像…";
            });
            var files = await ConversionService.ConvertAsync(_image, format, _outputDirectory, progress, _job.Token);
            OutputPreview.Text = string.Join("\n", files);
            OpenOutputButton.IsEnabled = true;
            OpenOutputButton.IsVisible = true;
            ConversionProgress.Value = 100;
            ProgressPercent.Text = "100%";
            ConversionStatus.Text = $"完成 · 生成 {files.Count} 个文件";
        }
        catch (OperationCanceledException)
        {
            ConversionStatus.Text = "已取消；临时文件已清理";
        }
        catch (Exception ex)
        {
            ConversionStatus.Text = "转换失败：" + ex.Message;
        }
        finally
        {
            _job.Dispose();
            _job = null;
            _busy = false;
            ImportButton.IsEnabled = true;
            CancelButton.IsVisible = false;
            ConvertButton.IsEnabled = _image is not null && (format.IsNativeEsxi || ConversionService.FindQemuImg() is not null);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => _job?.Cancel();

    private void OpenOutput_Click(object? sender, RoutedEventArgs e)
    {
        if (_outputDirectory is null || !Directory.Exists(_outputDirectory)) return;
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open")
                : new ProcessStartInfo("xdg-open");
        start.ArgumentList.Add(_outputDirectory);
        Process.Start(start);
    }
}
