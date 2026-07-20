using System.Text.Json;
using Microsoft.Extensions.Logging;
using IterVC.Core.Interfaces;
using IterVC.Core.Settings;

namespace IterVC.Audio;

/// <summary>
/// Persiste <see cref="AppSettings"/> en un archivo settings.json ubicado en
/// %AppData%/IterVC/settings.json.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<SettingsService> _logger;

    public AppSettings Current { get; private set; } = new();

    public SettingsService(ILogger<SettingsService> logger, string? settingsFolderOverride = null)
    {
        _logger = logger;
        var folder = settingsFolderOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IterVC");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                Current = new AppSettings();
                return Current;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
            Current = loaded ?? new AppSettings();
            return Current;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cargar settings.json, se usará configuración por defecto");
            Current = new AppSettings();
            return Current;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var tmpPath = _filePath + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken);
            }
            File.Copy(tmpPath, _filePath, overwrite: true);
            File.Delete(tmpPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar settings.json");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default)
    {
        mutate(Current);
        await SaveAsync(cancellationToken);
    }
}
