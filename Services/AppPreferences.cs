using System.Text.Json;

namespace EtchForge.Services;

public enum UiLanguage { Chinese, English }

public static class AppPreferences
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EtchForge", "settings.json");

    public static UiLanguage Language { get; private set; } = LoadLanguage();

    public static bool SetLanguage(UiLanguage language, bool persist = true)
    {
        Language = language;
        if (!persist) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { language = language == UiLanguage.English ? "en" : "zh" }));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static UiLanguage LoadLanguage()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return doc.RootElement.TryGetProperty("language", out var value) && value.GetString() == "en"
                ? UiLanguage.English : UiLanguage.Chinese;
        }
        catch (IOException) { return UiLanguage.Chinese; }
        catch (UnauthorizedAccessException) { return UiLanguage.Chinese; }
        catch (JsonException) { return UiLanguage.Chinese; }
        catch (InvalidOperationException) { return UiLanguage.Chinese; }
    }
}
