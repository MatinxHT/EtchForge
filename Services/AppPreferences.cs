using System.Text.Json;

namespace EtchForge.Services;

public enum UiLanguage { Chinese, English }

public interface IAppPreferences
{
    UiLanguage Language { get; }
    event Action? LanguageChanged;
    bool SetLanguage(UiLanguage language);
}

public sealed class AppPreferences : IAppPreferences
{
    private readonly string _settingsPath;

    public AppPreferences(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EtchForge", "settings.json");
        Language = LoadLanguage();
    }

    public UiLanguage Language { get; private set; }
    public event Action? LanguageChanged;

    public bool SetLanguage(UiLanguage language)
    {
        var changed = Language != language;
        Language = language;
        if (changed) LanguageChanged?.Invoke();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { language = language == UiLanguage.English ? "en" : "zh" }));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private UiLanguage LoadLanguage()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            return doc.RootElement.TryGetProperty("language", out var value) && value.GetString() == "en"
                ? UiLanguage.English : UiLanguage.Chinese;
        }
        catch (IOException) { return UiLanguage.Chinese; }
        catch (UnauthorizedAccessException) { return UiLanguage.Chinese; }
        catch (JsonException) { return UiLanguage.Chinese; }
        catch (InvalidOperationException) { return UiLanguage.Chinese; }
    }
}
