using IterVC.Core.Settings;

namespace IterVC.Core.Interfaces;

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

    /// <summary>
    /// Aplica una mutación en memoria y programa un guardado diferido (debounce).
    /// Pensado para cambios de alta frecuencia (sliders, texto) donde escribir a disco
    /// en cada tick sería excesivo.
    /// </summary>
    void QueueUpdate(Action<AppSettings> mutate);

    /// <summary>Persiste inmediatamente cualquier guardado pendiente de <see cref="QueueUpdate"/>.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
