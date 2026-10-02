using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EtchForge.Services;

namespace EtchForge;

public partial class SettingsWindow : Window
{
    private readonly Action _onLanguageChanged;
    private bool _ready;

    public SettingsWindow() : this(() => { }) { }

    public SettingsWindow(Action onLanguageChanged)
    {
        _onLanguageChanged = onLanguageChanged;
        InitializeComponent();
        LanguagePicker.ItemsSource = new[]
        {
            new LanguageChoice(UiLanguage.Chinese, "简体中文"),
            new LanguageChoice(UiLanguage.English, "English")
        };
        LanguagePicker.SelectedIndex = AppPreferences.Language == UiLanguage.English ? 1 : 0;
        ApplyLanguage();
        _ready = true;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        Heading = this.FindControl<TextBlock>("Heading")!;
        LanguageLabel = this.FindControl<TextBlock>("LanguageLabel")!;
        LanguagePicker = this.FindControl<ComboBox>("LanguagePicker")!;
        EngineLabel = this.FindControl<TextBlock>("EngineLabel")!;
        EngineValue = this.FindControl<TextBlock>("EngineValue")!;
        SaveStatus = this.FindControl<TextBlock>("SaveStatus")!;
        CloseButton = this.FindControl<Button>("CloseButton")!;
    }

    private void ApplyLanguage()
    {
        Title = UiText.Get("settings");
        Heading.Text = UiText.Get("settings");
        LanguageLabel.Text = UiText.Get("language");
        EngineLabel.Text = UiText.Get("engine");
        EngineValue.Text = UiText.Get(ConversionService.FindQemuImg() is null ? "engineMissing" : "engineReady");
        CloseButton.Content = UiText.Get("close");
    }

    private void Language_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguagePicker.SelectedItem is not LanguageChoice choice) return;
        var saved = AppPreferences.SetLanguage(choice.Language);
        _onLanguageChanged();
        ApplyLanguage();
        SaveStatus.Text = saved ? "" : UiText.Get("saveFailed");
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private sealed record LanguageChoice(UiLanguage Language, string Label);
}
