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

    /// <summary>Tiempo de silencio tras el último <see cref="QueueUpdate"/> antes de escribir a disco.</summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(400);

    private CancellationTokenSource? _debounceCts;
    private int _savePending;

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
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            mutate(Current);
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void QueueUpdate(Action<AppSettings> mutate)
    {
        _lock.Wait();
        try
        {
            mutate(Current);
        }
        finally
        {
            _lock.Release();
        }

        Interlocked.Exchange(ref _savePending, 1);

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _debounceCts, cts)?.Cancel();
        _ = SaveAfterDelayAsync(cts.Token);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _debounceCts, null)?.Cancel();
        if (Interlocked.Exchange(ref _savePending, 0) == 1)
            await SaveAsync(cancellationToken);
    }

    private async Task SaveAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SaveDebounce, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Interlocked.Exchange(ref _savePending, 0) == 1)
            await SaveAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Escritura real a disco. Requiere que el llamador tenga tomado <see cref="_lock"/>.</summary>
    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tmpPath = _filePath + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken);
            }
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo guardar settings.json");
        }
    }
}
