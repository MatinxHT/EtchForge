using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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
    private IReadOnlyList<string>? _lastOutputFiles;
    private TimeSpan? _lastElapsed;
    private string? _lastFormatId;
    private CancellationTokenSource? _job;
    private bool _busy;
    private bool _applyingLanguage;
    private string? _conversionStatusKey;
    private string? _conversionError;

    private sealed record FormatChoice(OutputFormat Format, string Name, string Description);

    public MainWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        Closed += (_, _) => _job?.Cancel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        SettingsButton = this.FindControl<Button>("SettingsButton")!;
        FormatPicker = this.FindControl<ComboBox>("FormatPicker")!;
        ImportButton = this.FindControl<Button>("ImportButton")!;
        SourceName = this.FindControl<TextBlock>("SourceName")!;
        SourceSummary = this.FindControl<TextBlock>("SourceSummary")!;
        SourceDetails = this.FindControl<StackPanel>("SourceDetails")!;
        ShaText = this.FindControl<TextBox>("ShaText")!;
        Md5Text = this.FindControl<TextBox>("Md5Text")!;
        CopyShaButton = this.FindControl<Button>("CopyShaButton")!;
        CopyMd5Button = this.FindControl<Button>("CopyMd5Button")!;
        ImportFeedback = this.FindControl<StackPanel>("ImportFeedback")!;
        ImportStatus = this.FindControl<TextBlock>("ImportStatus")!;
        ImportProgress = this.FindControl<ProgressBar>("ImportProgress")!;
        FormatDescription = this.FindControl<TextBlock>("FormatDescription")!;
        ChooseFolderButton = this.FindControl<Button>("ChooseFolderButton")!;
        OutputDirectoryText = this.FindControl<TextBlock>("OutputDirectoryText")!;
        OutputPreview = this.FindControl<TextBox>("OutputPreview")!;
        ConvertButton = this.FindControl<Button>("ConvertButton")!;
        ConversionStatus = this.FindControl<TextBlock>("ConversionStatus")!;
        ConversionProgress = this.FindControl<ProgressBar>("ConversionProgress")!;
        ConversionPanel = this.FindControl<StackPanel>("ConversionPanel")!;
        ProgressPercent = this.FindControl<TextBlock>("ProgressPercent")!;
        CancelButton = this.FindControl<Button>("CancelButton")!;
        OpenOutputButton = this.FindControl<Button>("OpenOutputButton")!;
        ResultHint = this.FindControl<TextBlock>("ResultHint")!;
        ResultPanel = this.FindControl<StackPanel>("ResultPanel")!;
        ElapsedLabel = this.FindControl<TextBlock>("ElapsedLabel")!;
        ResultElapsedText = this.FindControl<TextBlock>("ResultElapsedText")!;
        ProductsLabel = this.FindControl<TextBlock>("ProductsLabel")!;
        ResultInfoText = this.FindControl<TextBlock>("ResultInfoText")!;
        ResultFilesText = this.FindControl<TextBox>("ResultFilesText")!;
        FooterText = this.FindControl<TextBlock>("FooterText")!;
    }

    private void ApplyLanguage()
    {
        ToolTip.SetTip(SettingsButton, UiText.Get("settings"));
        ImportButton.Content = UiText.Get("chooseImage");
        ChooseFolderButton.Content = UiText.Get("chooseFolder");
        ConvertButton.Content = UiText.Get("convert");
        CancelButton.Content = UiText.Get("cancel");
        OpenOutputButton.Content = UiText.Get("openFolder");
        CopyShaButton.Content = UiText.Get("copy");
        CopyMd5Button.Content = UiText.Get("copy");
        FooterText.Text = UiText.Get("localOnly");
        ResultHint.Text = UiText.Get("resultHint");
        ElapsedLabel.Text = UiText.Get("elapsed");
        ProductsLabel.Text = UiText.Get("products");
        if (_image is not null) UpdateSourceSummary();
        if (_lastOutputFiles is not null) UpdateResultSummary();
        if (_conversionStatusKey is not null) SetConversionStatus(_conversionStatusKey, _conversionError);

        var selectedId = (FormatPicker.SelectedItem as FormatChoice)?.Format.Id ?? "esxi";
        var choices = OutputFormat.All.Select(format => new FormatChoice(format,
            AppPreferences.Language == UiLanguage.English ? format.EnglishName : format.Name,
            AppPreferences.Language == UiLanguage.English ? format.EnglishDescription : format.Description)).ToArray();
        _applyingLanguage = true;
        try
        {
            FormatPicker.ItemsSource = choices;
            FormatPicker.SelectedItem = choices.First(choice => choice.Format.Id == selectedId);
        }
        finally { _applyingLanguage = false; }
        RefreshPreview();
    }

    private void UpdateSourceSummary()
    {
        if (_image is null) return;
        var partition = _image.PartitionScheme switch
        {
            "GPT · 带保护 MBR" => UiText.Get("gpt"),
            "未识别分区表 / 无分区磁盘" => UiText.Get("unknownPartitions"),
            _ => _image.PartitionScheme
        };
        SourceSummary.Text = $"{_image.SizeText}\n{partition}";
    }

    private void UpdateResultSummary()
    {
        if (_lastOutputFiles is null || _lastElapsed is null) return;
        var bytes = _lastOutputFiles.Where(File.Exists).Sum(path => new FileInfo(path).Length);
        var size = bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.00} GiB"
            : $"{bytes / 1024d / 1024d:0.0} MiB";
        ResultElapsedText.Text = $"{_lastElapsed.Value.TotalSeconds:0.0} s";
        ResultInfoText.Text = $"{_lastOutputFiles.Count} {UiText.Get("files")} · {size}";
        ResultFilesText.Text = string.Join("\n", _lastOutputFiles.Select(Path.GetFileName));
        ResultHint.IsVisible = false;
        ResultPanel.IsVisible = true;
    }

    private void ClearResult()
    {
        _lastOutputFiles = null;
        _lastElapsed = null;
        _lastFormatId = null;
        ResultPanel.IsVisible = false;
        ResultHint.IsVisible = true;
        ConversionPanel.IsVisible = false;
        ConversionProgress.Value = 0;
        ProgressPercent.Text = "0%";
        _conversionStatusKey = null;
        _conversionError = null;
    }

    private void SetConversionStatus(string key, string? error = null)
    {
        _conversionStatusKey = key;
        _conversionError = error;
        ConversionStatus.Text = error is null ? UiText.Get(key) : $"{UiText.Get(key)}: {error}";
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        SettingsButton.IsEnabled = false;
        try { await new SettingsWindow(ApplyLanguage).ShowDialog(this); }
        finally { SettingsButton.IsEnabled = true; }
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiText.Get("chooseImage"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("IMG (*.img)") { Patterns = ["*.img", "*.IMG"] }]
        });
        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        _image = null;
        _busy = true;
        ClearResult();
        ImportButton.IsEnabled = false;
        SourceDetails.IsVisible = true;
        ImportFeedback.IsVisible = true;
        CopyShaButton.IsEnabled = false;
        CopyMd5Button.IsEnabled = false;
        CopyShaButton.Content = UiText.Get("copy");
        CopyMd5Button.Content = UiText.Get("copy");
        ShaText.Text = UiText.Get("calculating");
        Md5Text.Text = UiText.Get("calculating");
        SourceName.Text = Path.GetFileName(path);
        SourceSummary.Text = UiText.Get("imageDetails");
        ImportStatus.Text = UiText.Get("reading");
        ImportProgress.Value = 0;
        RefreshPreview();
        _job = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(value =>
            {
                ImportProgress.Value = value * 100;
                ImportStatus.Text = $"{UiText.Get("calculatingHashes")} · {value:P0}";
            });
            _image = await Task.Run(() => ImageInspector.InspectAsync(path, progress, _job.Token));
            _outputDirectory ??= Path.Combine(Path.GetDirectoryName(path)!, "converted");
            UpdateSourceSummary();
            ShaText.Text = _image.Sha256;
            Md5Text.Text = _image.Md5;
            CopyShaButton.IsEnabled = true;
            CopyMd5Button.IsEnabled = true;
            ImportProgress.Value = 100;
            ImportStatus.Text = UiText.Get("ready");
            ImportFeedback.IsVisible = false;
        }
        catch (Exception ex)
        {
            SourceSummary.Text = UiText.Get("readFailed");
            ShaText.Text = "—";
            Md5Text.Text = "—";
            CopyShaButton.IsEnabled = false;
            CopyMd5Button.IsEnabled = false;
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
            Title = UiText.Get("chooseFolder"),
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        _outputDirectory = folders[0].Path.LocalPath;
        ClearResult();
        RefreshPreview();
    }

    private void Format_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingLanguage) return;
        if (FormatPicker.SelectedItem is FormatChoice choice && _lastFormatId is not null &&
            choice.Format.Id != _lastFormatId) ClearResult();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (FormatPicker.SelectedItem is not FormatChoice choice) return;
        var format = choice.Format;
        FormatDescription.Text = choice.Description;
        OutputDirectoryText.Text = _outputDirectory ?? UiText.Get("folderHint");
        ToolTip.SetTip(OutputDirectoryText, _outputDirectory);
        IReadOnlyList<string>? previewPaths = null;
        if (_image is null || _outputDirectory is null)
            OutputPreview.Text = UiText.Get("pathHint");
        else
        {
            if (_lastOutputFiles is not null && _lastFormatId == format.Id)
                previewPaths = _lastOutputFiles;
            else
            {
                var baseName = Path.GetFileNameWithoutExtension(_image.Path) + "-" + format.Id;
                var paths = new List<string> { Path.Combine(_outputDirectory, baseName + format.Extension) };
                if (format.IsNativeEsxi) paths.Add(Path.Combine(_outputDirectory, baseName + "-flat.vmdk"));
                previewPaths = paths;
            }
            OutputPreview.Text = string.Join("\n", previewPaths.Select(Path.GetFileName));
        }
        ToolTip.SetTip(OutputPreview, previewPaths is null ? null : string.Join("\n", previewPaths));
        var qemuAvailable = ConversionService.FindQemuImg() is not null;
        ConvertButton.IsEnabled = !_busy && _image is not null && _outputDirectory is not null &&
            (format.IsNativeEsxi || qemuAvailable);
        OpenOutputButton.IsEnabled = _outputDirectory is not null && Directory.Exists(_outputDirectory);
        if (!format.IsNativeEsxi && !qemuAvailable && !_busy)
        {
            ConversionPanel.IsVisible = true;
            SetConversionStatus("needsQemu");
        }
        else if (!_busy && _conversionStatusKey == "needsQemu")
        {
            ConversionPanel.IsVisible = false;
            SetConversionStatus("waiting");
        }
    }

    private async void Convert_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || _image is null || _outputDirectory is null ||
            FormatPicker.SelectedItem is not FormatChoice choice) return;
        var format = choice.Format;
        _busy = true;
        ClearResult();
        _job = new CancellationTokenSource();
        ConvertButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        ChooseFolderButton.IsEnabled = false;
        CancelButton.IsVisible = true;
        ConversionPanel.IsVisible = true;
        SetConversionStatus("preparing");
        var timer = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(_outputDirectory);
            var progress = new Progress<double>(value =>
            {
                ConversionProgress.Value = value * 100;
                ProgressPercent.Text = $"{value:P0}";
                SetConversionStatus(value >= 1 ? "completed" : "converting");
            });
            var files = await ConversionService.ConvertAsync(_image, format, _outputDirectory, progress, _job.Token);
            timer.Stop();
            _lastOutputFiles = files;
            _lastElapsed = timer.Elapsed;
            _lastFormatId = format.Id;
            UpdateResultSummary();
            OutputPreview.Text = string.Join("\n", files.Select(Path.GetFileName));
            OpenOutputButton.IsEnabled = true;
            ConversionProgress.Value = 100;
            ProgressPercent.Text = "100%";
            SetConversionStatus("completed");
        }
        catch (OperationCanceledException)
        {
            SetConversionStatus("canceled");
        }
        catch (Exception ex)
        {
            SetConversionStatus("failed", ex.Message);
        }
        finally
        {
            timer.Stop();
            _job.Dispose();
            _job = null;
            _busy = false;
            ImportButton.IsEnabled = true;
            ChooseFolderButton.IsEnabled = true;
            CancelButton.IsVisible = false;
            RefreshPreview();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => _job?.Cancel();

    private async void CopySha_Click(object? sender, RoutedEventArgs e) =>
        await CopyHashAsync(_image?.Sha256, CopyShaButton);

    private async void CopyMd5_Click(object? sender, RoutedEventArgs e) =>
        await CopyHashAsync(_image?.Md5, CopyMd5Button);

    private async Task CopyHashAsync(string? value, Button button)
    {
        if (string.IsNullOrEmpty(value) || Clipboard is null) return;
        try
        {
            await Clipboard.SetTextAsync(value);
            button.Content = UiText.Get("copied");
        }
        catch
        {
            button.Content = UiText.Get("copyFailed");
        }
    }

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
