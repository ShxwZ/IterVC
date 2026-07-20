using IterVC.Core.Models;

namespace IterVC.Core.Interfaces;

/// <summary>
/// Enumera los dispositivos de audio disponibles en el sistema (entrada/salida)
/// abstrayendo la API concreta (NAudio/WASAPI) usada por debajo.
/// </summary>
public interface IDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();

    IReadOnlyList<AudioDeviceInfo> GetInputDevices();

    /// <summary>Intenta localizar automáticamente el dispositivo VB-Cable instalado, si existe.</summary>
    AudioDeviceInfo? FindVbCableDevice();

    /// <summary>Se dispara cuando Windows notifica una alta/baja de dispositivos de audio.</summary>
    event EventHandler? DevicesChanged;
}
