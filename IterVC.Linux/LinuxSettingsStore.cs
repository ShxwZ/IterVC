using System.Text.Json;
using IterVC.Core.Settings;

namespace IterVC.Linux;

/// <summary>
/// Persist the platform-independent AppSettings schema. Avoid writing a
/// partially updated JSON file when the process is terminated unexpectedly.
/// </summary>
internal sealed class LinuxSettingsStore
{
    private readonly string _settingsPath;

    public LinuxSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IterVC", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath))
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Do not crash the UI when settings are corrupted.
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryFile = _settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryFile,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            File.Move(temporaryFile, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
    }
}
