using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using EtchForge.Models;
using EtchForge.Services;

namespace EtchForge.ViewModels;

public sealed record FormatChoice(OutputFormat Format, string Name, string Description);

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IImageInspector _inspector;
    private readonly IImageConverter _converter;
    private readonly IDesktopInteraction _desktop;
    private readonly IAppPreferences _preferences;
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

    [ObservableProperty] private IReadOnlyList<FormatChoice> _formats = [];
    [ObservableProperty] private FormatChoice? _selectedFormat;
    [ObservableProperty] private string _settingsToolTip = "";
    [ObservableProperty] private string _importLabel = "";
    [ObservableProperty] private string _chooseFolderLabel = "";
    [ObservableProperty] private string _convertLabel = "";
    [ObservableProperty] private string _cancelLabel = "";
    [ObservableProperty] private string _openFolderLabel = "";
    [ObservableProperty] private string _copyShaLabel = "";
    [ObservableProperty] private string _copyMd5Label = "";
    [ObservableProperty] private string _detailsLabel = "";
    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private string _resultHint = "";
    [ObservableProperty] private string _elapsedLabel = "";
    [ObservableProperty] private string _productsLabel = "";
    [ObservableProperty] private string _sourceName = "";
    [ObservableProperty] private string _sourceSummary = "";
    [ObservableProperty] private string _shaText = "";
    [ObservableProperty] private string _md5Text = "";
    [ObservableProperty] private string _importStatus = "";
    [ObservableProperty] private double _importProgressValue;
    [ObservableProperty] private string _formatDescription = "";
    [ObservableProperty] private string _outputDirectoryText = "";
    [ObservableProperty] private string? _outputDirectoryToolTip;
    [ObservableProperty] private string _outputPreview = "";
    [ObservableProperty] private string? _outputPreviewToolTip;
    [ObservableProperty] private string _conversionStatus = "";
    [ObservableProperty] private double _conversionProgressValue;
    [ObservableProperty] private string _progressPercent = "0%";
    [ObservableProperty] private string _resultElapsedText = "";
    [ObservableProperty] private string _resultInfoText = "";
    [ObservableProperty] private string _resultFilesText = "";
    [ObservableProperty] private bool _sourceDetailsVisible;
    [ObservableProperty] private bool _importFeedbackVisible;
    [ObservableProperty] private bool _conversionPanelVisible;
    [ObservableProperty] private bool _cancelVisible;
    [ObservableProperty] private bool _resultPanelVisible;
    [ObservableProperty] private bool _resultHintVisible = true;
    [ObservableProperty] private bool _importEnabled = true;
    [ObservableProperty] private bool _chooseFolderEnabled = true;
    [ObservableProperty] private bool _convertEnabled;
    [ObservableProperty] private bool _openFolderEnabled;
    [ObservableProperty] private bool _copyShaEnabled;
    [ObservableProperty] private bool _copyMd5Enabled;
    [ObservableProperty] private bool _detailsEnabled;

    public MainWindowViewModel(IImageInspector inspector, IImageConverter converter,
        IDesktopInteraction desktop, IAppPreferences preferences)
    {
        _inspector = inspector;
        _converter = converter;
        _desktop = desktop;
        _preferences = preferences;
        _preferences.LanguageChanged += ApplyLanguage;
        ApplyLanguage();
    }

    private string T(string key) => UiText.Get(key, _preferences.Language);

    private void ApplyLanguage()
    {
        SettingsToolTip = T("settings");
        ImportLabel = T("chooseImage");
        ChooseFolderLabel = T("chooseFolder");
        ConvertLabel = T("convert");
        CancelLabel = T("cancel");
        OpenFolderLabel = T("openFolder");
        CopyShaLabel = T("copy");
        CopyMd5Label = T("copy");
        DetailsLabel = T("details");
        FooterText = T("localOnly");
        ResultHint = T("resultHint");
        ElapsedLabel = T("elapsed");
        ProductsLabel = T("products");
        if (_image is not null) UpdateSourceSummary();
        if (_lastOutputFiles is not null) UpdateResultSummary();
        if (_conversionStatusKey is not null) SetConversionStatus(_conversionStatusKey, _conversionError);

        var selectedId = SelectedFormat?.Format.Id ?? "esxi";
        var choices = OutputFormat.All.Select(format => new FormatChoice(format,
            _preferences.Language == UiLanguage.English ? format.EnglishName : format.Name,
            _preferences.Language == UiLanguage.English ? format.EnglishDescription : format.Description)).ToArray();
        _applyingLanguage = true;
        try
        {
            Formats = choices;
            SelectedFormat = choices.First(choice => choice.Format.Id == selectedId);
        }
        finally { _applyingLanguage = false; }
        RefreshPreview();
    }

    partial void OnSelectedFormatChanged(FormatChoice? value)
    {
        if (_applyingLanguage) return;
        if (value is not null && _lastFormatId is not null && value.Format.Id != _lastFormatId)
            ClearResult();
        RefreshPreview();
    }

    private void UpdateSourceSummary()
    {
        if (_image is null) return;
        var partition = _image.PartitionScheme switch
        {
            "GPT · 带保护 MBR" => T("gpt"),
            "未识别分区表 / 无分区磁盘" => T("unknownPartitions"),
            "未识别 / 无分区" => T("unknownLayout"),
            "ISO 9660 / 光盘" => T("isoLayout"),
            "UDF / 光盘" => T("udfLayout"),
            _ => _image.PartitionScheme
        };
        var format = _image.Format == "vpc" ? "VHD" : _image.Format.ToUpperInvariant();
        SourceSummary = $"{format} · {_image.SizeText}\n{partition}";
    }

    private void UpdateResultSummary()
    {
        if (_lastOutputFiles is null || _lastElapsed is null) return;
        var bytes = _lastOutputFiles.Where(File.Exists).Sum(path => new FileInfo(path).Length);
        var size = bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.00} GiB"
            : $"{bytes / 1024d / 1024d:0.0} MiB";
        ResultElapsedText = $"{_lastElapsed.Value.TotalSeconds:0.0} s";
        ResultInfoText = $"{_lastOutputFiles.Count} {T("files")} · {size}";
        ResultFilesText = string.Join("\n", _lastOutputFiles.Select(Path.GetFileName));
        ResultHintVisible = false;
        ResultPanelVisible = true;
    }

    private void ClearResult()
    {
        _lastOutputFiles = null;
        _lastElapsed = null;
        _lastFormatId = null;
        ResultPanelVisible = false;
        ResultHintVisible = true;
        ConversionPanelVisible = false;
        ConversionProgressValue = 0;
        ProgressPercent = "0%";
        _conversionStatusKey = null;
        _conversionError = null;
    }

    private void SetConversionStatus(string key, string? error = null)
    {
        _conversionStatusKey = key;
        _conversionError = error;
        ConversionStatus = error is null ? T(key) : $"{T(key)}: {error}";
    }

    private void RefreshPreview()
    {
        if (SelectedFormat is not { } choice) return;
        var format = choice.Format;
        FormatDescription = choice.Description;
        OutputDirectoryText = _outputDirectory ?? T("folderHint");
        OutputDirectoryToolTip = _outputDirectory;
        IReadOnlyList<string>? paths = null;
        if (_image is null || _outputDirectory is null)
            OutputPreview = T("pathHint");
        else
        {
            if (_lastOutputFiles is not null && _lastFormatId == format.Id)
                paths = _lastOutputFiles;
            else
            {
                var baseName = Path.GetFileNameWithoutExtension(_image.Path) + "-" + format.Id;
                var preview = new List<string> { Path.Combine(_outputDirectory, baseName + format.Extension) };
                if (format.IsNativeEsxi) preview.Add(Path.Combine(_outputDirectory, baseName + "-flat.vmdk"));
                paths = preview;
            }
            OutputPreview = string.Join("\n", paths.Select(Path.GetFileName));
        }
        OutputPreviewToolTip = paths is null ? null : string.Join("\n", paths);
        ConvertEnabled = !_busy && _image is { IsOptical: false } && _outputDirectory is not null &&
            (_image.Format == "raw" && format.IsNativeEsxi || _converter.IsQemuAvailable);
        OpenFolderEnabled = _outputDirectory is not null && Directory.Exists(_outputDirectory);
        if (_image?.IsOptical == true)
        {
            ConversionPanelVisible = false;
            return;
        }
        if ((_image is { Format: not "raw", IsOptical: false } || !format.IsNativeEsxi) && !_converter.IsQemuAvailable && !_busy)
        {
            ConversionPanelVisible = true;
            SetConversionStatus("needsQemu");
        }
        else if (!_busy && _conversionStatusKey == "needsQemu")
        {
            ConversionPanelVisible = false;
            SetConversionStatus("waiting");
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync() => await _desktop.ShowSettingsAsync();

    [RelayCommand]
    private async Task ShowDetailsAsync()
    {
        if (_image is not null) await _desktop.ShowImageDetailsAsync(_image, _preferences.Language);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_busy) return;
        var path = await _desktop.ChooseImageAsync(T("chooseImage"));
        if (path is null) return;
        _image = null;
        DetailsEnabled = false;
        _busy = true;
        ClearResult();
        ImportEnabled = false;
        SourceDetailsVisible = true;
        ImportFeedbackVisible = true;
        CopyShaEnabled = CopyMd5Enabled = false;
        CopyShaLabel = CopyMd5Label = T("copy");
        ShaText = Md5Text = T("calculating");
        SourceName = Path.GetFileName(path);
        SourceSummary = T("imageDetails");
        ImportStatus = T("reading");
        ImportProgressValue = 0;
        RefreshPreview();
        using var job = new CancellationTokenSource();
        _job = job;
        try
        {
            var progress = new Progress<double>(value => Dispatcher.UIThread.Post(() =>
            {
                if (_job != job) return;
                ImportProgressValue = value * 100;
                ImportStatus = $"{T("calculatingHashes")} · {value:P0}";
            }));
            _image = await Task.Run(() => _inspector.InspectAsync(path, progress, job.Token));
            DetailsEnabled = true;
            _outputDirectory ??= Path.Combine(Path.GetDirectoryName(path)!, "converted");
            UpdateSourceSummary();
            ShaText = _image.Sha256;
            Md5Text = _image.Md5;
            CopyShaEnabled = CopyMd5Enabled = true;
            ImportProgressValue = 100;
            ImportStatus = T(_image.IsOptical ? "opticalOnly" : "ready");
            ImportFeedbackVisible = _image.IsOptical;
        }
        catch (OperationCanceledException) { ImportStatus = T("canceled"); }
        catch (Exception ex)
        {
            SourceSummary = T("readFailed");
            ShaText = Md5Text = "—";
            CopyShaEnabled = CopyMd5Enabled = false;
            ImportStatus = ex.Message;
        }
        finally
        {
            _job = null;
            _busy = false;
            ImportEnabled = true;
            RefreshPreview();
        }
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (_busy) return;
        var path = await _desktop.ChooseFolderAsync(T("chooseFolder"));
        if (path is null) return;
        _outputDirectory = path;
        ClearResult();
        RefreshPreview();
    }

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (_busy || _image is null || _outputDirectory is null || SelectedFormat is not { } choice) return;
        var format = choice.Format;
        _busy = true;
        ClearResult();
        using var job = new CancellationTokenSource();
        _job = job;
        ConvertEnabled = ImportEnabled = ChooseFolderEnabled = false;
        CancelVisible = ConversionPanelVisible = true;
        SetConversionStatus("preparing");
        var timer = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(_outputDirectory);
            var progress = new Progress<double>(value => Dispatcher.UIThread.Post(() =>
            {
                if (_job != job) return;
                ConversionProgressValue = value * 100;
                ProgressPercent = $"{value:P0}";
                SetConversionStatus(value >= 1 ? "completed" : "converting");
            }));
            var files = await _converter.ConvertAsync(_image, format, _outputDirectory, progress, job.Token);
            timer.Stop();
            _lastOutputFiles = files;
            _lastElapsed = timer.Elapsed;
            _lastFormatId = format.Id;
            UpdateResultSummary();
            OutputPreview = string.Join("\n", files.Select(Path.GetFileName));
            OutputPreviewToolTip = string.Join("\n", files);
            OpenFolderEnabled = true;
            ConversionProgressValue = 100;
            ProgressPercent = "100%";
            SetConversionStatus("completed");
        }
        catch (OperationCanceledException) { SetConversionStatus("canceled"); }
        catch (Exception ex) { SetConversionStatus("failed", ex.Message); }
        finally
        {
            timer.Stop();
            _job = null;
            _busy = false;
            ImportEnabled = ChooseFolderEnabled = true;
            CancelVisible = false;
            RefreshPreview();
        }
    }

    [RelayCommand]
    private void Cancel() => _job?.Cancel();

    [RelayCommand]
    private async Task CopyShaAsync() => await CopyHashAsync(_image?.Sha256, true);

    [RelayCommand]
    private async Task CopyMd5Async() => await CopyHashAsync(_image?.Md5, false);

    private async Task CopyHashAsync(string? value, bool sha)
    {
        if (string.IsNullOrEmpty(value)) return;
        try
        {
            await _desktop.CopyTextAsync(value);
            if (sha) CopyShaLabel = T("copied"); else CopyMd5Label = T("copied");
        }
        catch
        {
            if (sha) CopyShaLabel = T("copyFailed"); else CopyMd5Label = T("copyFailed");
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (_outputDirectory is null || !Directory.Exists(_outputDirectory)) return;
        try { _desktop.OpenFolder(_outputDirectory); }
        catch (Exception ex)
        {
            ConversionPanelVisible = true;
            SetConversionStatus("failed", ex.Message);
        }
    }

    public void CancelActiveOperation() => _job?.Cancel();

    public void Dispose()
    {
        _preferences.LanguageChanged -= ApplyLanguage;
        CancelActiveOperation();
    }
}
