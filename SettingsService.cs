using System.Text.Json;

namespace AudioMixerVB;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(AppContext.BaseDirectory, "mixer_settings.json");
    }

    public string SettingsPath { get; }

    public AppSettings Load(out bool settingsFileLoaded, out string? error)
    {
        settingsFileLoaded = File.Exists(SettingsPath);
        error = null;

        if (!settingsFileLoaded)
        {
            var defaults = new AppSettings();
            defaults.EnsureDefaults();
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.EnsureDefaults();
            return settings;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            var defaults = new AppSettings();
            defaults.EnsureDefaults();
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        settings.EnsureDefaults();

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
