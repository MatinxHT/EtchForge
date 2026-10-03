using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EtchForge.Services;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace EtchForge.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppPreferences _preferences;
    private readonly IImageConverter _converter;
    private bool _ready;

    [ObservableProperty] private int _selectedLanguageIndex;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _languageLabel = "";
    [ObservableProperty] private string _engineLabel = "";
    [ObservableProperty] private string _engineValue = "";
    [ObservableProperty] private string _closeLabel = "";
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _systemInfo = "";
    [ObservableProperty] private string _installLabel = "";
    [ObservableProperty] private string _windowsInstallLabel = "";
    [ObservableProperty] private string _unixInstallLabel = "";
    [ObservableProperty] private string _installStatus = "";

    public event Action? CloseRequested;

    public SettingsViewModel(IAppPreferences preferences, IImageConverter converter)
    {
        _preferences = preferences;
        _converter = converter;
        _selectedLanguageIndex = preferences.Language == UiLanguage.English ? 1 : 0;
        ApplyLanguage();
        _ready = true;
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (!_ready || value is not (0 or 1)) return;
        var saved = _preferences.SetLanguage(value == 1 ? UiLanguage.English : UiLanguage.Chinese);
        ApplyLanguage();
        SaveStatus = saved ? "" : UiText.Get("saveFailed", _preferences.Language);
    }

    private void ApplyLanguage()
    {
        string T(string key) => UiText.Get(key, _preferences.Language);
        Title = T("settings");
        LanguageLabel = T("language");
        EngineLabel = T("engine");
        var separator = _preferences.Language == UiLanguage.Chinese ? "：" : ": ";
        EngineValue = $"{T("builtInEsxi")}{separator}{T("engineReady")}\n" +
                      $"qemu-img{separator}{T(_converter.IsQemuAvailable ? "engineReady" : "engineMissing")}";
        SystemInfo = $"{T("operatingSystem")}{separator}{RuntimeInformation.OSDescription.Trim()} · " +
                     $"{T("cpuArchitecture")}{separator}{RuntimeInformation.OSArchitecture.ToString().ToUpperInvariant()}";
        CloseLabel = T("close");
        InstallLabel = T("installQemu");
        WindowsInstallLabel = T("windowsInstall");
        UnixInstallLabel = T("unixInstall");
    }

    [RelayCommand] private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private void OpenQemuDocs()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.qemu.org/download/") { UseShellExecute = true });
            InstallStatus = "";
        }
        catch (Exception ex) { InstallStatus = ex.Message; }
    }

}
