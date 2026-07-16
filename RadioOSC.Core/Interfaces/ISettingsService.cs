using RadioOSC.Core.Settings;

namespace RadioOSC.Core.Interfaces;

/// <summary>
/// Carga y persiste la configuración de la aplicación en settings.json.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Aplica una mutación sobre la configuración actual y la persiste inmediatamente.</summary>
    Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken = default);
}
