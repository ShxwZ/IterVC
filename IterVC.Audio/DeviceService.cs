using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;

namespace IterVC.Audio;

/// <summary>
/// Enumera dispositivos de audio del sistema usando WASAPI (NAudio.CoreAudioApi).
/// </summary>
public sealed class DeviceService : IDeviceService, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly ILogger<DeviceService> _logger;
    private readonly MMNotificationClient _notificationClient;

    public event EventHandler? DevicesChanged;

    public DeviceService(ILogger<DeviceService> logger)
    {
        _logger = logger;
        _notificationClient = new MMNotificationClient(() => DevicesChanged?.Invoke(this, EventArgs.Empty));
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        GetDevices(DataFlow.Render, AudioDeviceKind.Output);

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices() =>
        GetDevices(DataFlow.Capture, AudioDeviceKind.Input);

    public AudioDeviceInfo? FindVbCableDevice()
    {
        return GetOutputDevices().FirstOrDefault(d =>
            d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
            d.Name.Contains("Input", StringComparison.OrdinalIgnoreCase))
            ?? GetOutputDevices().FirstOrDefault(d =>
                d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));
    }

    private List<AudioDeviceInfo> GetDevices(DataFlow flow, AudioDeviceKind kind)
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                _logger.LogWarning("GetDevices llamado desde hilo MTA — puede fallar la enumeración WASAPI");
            }

            string? defaultId = null;
            try
            {
                defaultId = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo obtener el dispositivo por defecto para {Flow}", flow);
            }

            foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                result.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    Kind = kind,
                    IsDefault = device.ID == defaultId
                });
            }

            _logger.LogInformation("Dispositivos {Kind} encontrados: {Count}", kind, result.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerando dispositivos de audio ({Kind})", kind);
        }

        return result;
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _enumerator.Dispose();
    }

    /// <summary>Puente mínimo para recibir notificaciones de cambios de dispositivo de CoreAudioApi.</summary>
    private sealed class MMNotificationClient : IMMNotificationClient
    {
        private readonly Action _onChanged;

        public MMNotificationClient(Action onChanged) => _onChanged = onChanged;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChanged();
        public void OnDeviceAdded(string pwstrDeviceId) => _onChanged();
        public void OnDeviceRemoved(string deviceId) => _onChanged();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => _onChanged();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
