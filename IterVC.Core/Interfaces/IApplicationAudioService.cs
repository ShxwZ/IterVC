using IterVC.Core.Models;

namespace IterVC.Core.Interfaces;

/// <summary>
/// Enumera las aplicaciones candidatas para incluir en la mezcla.
///
/// El servicio no modifica la reproducción de las aplicaciones. La captura real se realiza
/// mediante <see cref="IAudioRouterService.AddAppSourceAsync"/> usando process loopback.
/// </summary>
public interface IApplicationAudioService
{
    /// <summary>
    /// Se produce cuando cambia la lista de aplicaciones candidatas, por ejemplo al crearse
    /// una sesión de audio o al abrirse o cerrarse un navegador compatible.
    /// </summary>
    event EventHandler? ApplicationsChanged;

    /// <summary>Cambia el dispositivo de salida sobre el que se enumeran las sesiones.</summary>
    void UseDevice(string outputDeviceId);

    /// <summary>
    /// Devuelve las aplicaciones con sesión de audio activa o reciente y los navegadores
    /// compatibles que estén abiertos, aunque todavía no hayan reproducido audio.
    /// </summary>
    IReadOnlyList<AudioAppInfo> GetRunningAudioApps();
}
