using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EtchForge.Services;

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
        EngineValue = T(_converter.IsQemuAvailable ? "engineReady" : "engineMissing");
        CloseLabel = T("close");
    }

    [RelayCommand] private void Close() => CloseRequested?.Invoke();

}
