using IterVC.Core.Models;

namespace IterVC.Core.Interfaces;

/// <summary>
/// Enumera las aplicaciones con sesión de audio activa en el dispositivo de salida
/// seleccionado, como fuente de candidatas para incluir en la mezcla.
///
/// Nota de diseño: este servicio NO silencia ni modifica nada de la reproducción de esas
/// apps — sólo las lista. La captura real (sin afectar a su sonido local) la hace
/// <see cref="IAudioRouterService.AddAppSourceAsync"/> usando la API nativa de "process
/// loopback" de Windows 10 2004+.
/// </summary>
public interface IApplicationAudioService
{
    /// <summary>Cambia el dispositivo de salida sobre el que se listan sesiones candidatas.</summary>
    void UseDevice(string outputDeviceId);

    /// <summary>Devuelve las apps con sesión de audio activa (o reciente) en el dispositivo actual.</summary>
    IReadOnlyList<AudioAppInfo> GetRunningAudioApps();
}
