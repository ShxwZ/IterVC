using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace IterVC.Audio;

public sealed class ApplicationAudioService : IApplicationAudioService, IDisposable
{
    private static readonly TimeSpan BrowserPollInterval = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "brave", "msedge", "firefox", "opera", "opera_gx", "vivaldi", "arc", "zen"
    };

    private readonly ILogger<ApplicationAudioService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _sync = new();
    private readonly Timer _browserWatcher;

    private MMDevice? _device;
    private AudioSessionManager? _sessionManager;
    private HashSet<int> _knownBrowserProcessIds = [];
    private bool _disposed;

    public ApplicationAudioService(ILogger<ApplicationAudioService> logger)
    {
        _logger = logger;
        _browserWatcher = new Timer(
            CheckForBrowserChanges,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? ApplicationsChanged;

    public void UseDevice(string outputDeviceId)
    {
        lock (_sync)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ApplicationAudioService));

            DetachSessionManager();
            _device?.Dispose();

            _device = _enumerator.GetDevice(outputDeviceId);
            _sessionManager = _device.AudioSessionManager;

            // NAudio conserva internamente la colección de sesiones. Hay que reconstruirla
            // para incluir aplicaciones que hayan empezado a emitir audio posteriormente.
            _sessionManager.RefreshSessions();
            _sessionManager.OnSessionCreated += OnSessionCreated;

            _knownBrowserProcessIds = GetRunningBrowsers()
                .Select(browser => browser.ProcessId)
                .ToHashSet();

            _browserWatcher.Change(BrowserPollInterval, BrowserPollInterval);
        }
    }

    public IReadOnlyList<AudioAppInfo> GetRunningAudioApps()
    {
        var result = new List<AudioAppInfo>();

        lock (_sync)
        {
            if (_device is null || _sessionManager is null)
            {
                _logger.LogWarning("GetRunningAudioApps llamado sin dispositivo seleccionado");
                return result;
            }

            try
            {
                _sessionManager.RefreshSessions();
                var sessions = _sessionManager.Sessions;

#pragma warning disable CS0618 // NAudio 2.2.1 expone AudioClient mediante esta propiedad.
                using var endpointAudioClient = _device.AudioClient;
#pragma warning restore CS0618
                var endpointFormat = endpointAudioClient.MixFormat;

                for (var index = 0; index < sessions.Count; index++)
                {
                    var session = sessions[index];
                    var processId = (int)session.GetProcessID;
                    if (processId == 0) continue;

                    try
                    {
                        using var process = Process.GetProcessById(processId);
                        var processName = process.ProcessName;
                        var captureProcessId = BrowserProcessNames.Contains(processName)
                            ? GetRootProcessId(processId, processName)
                            : processId;

                        if (captureProcessId != processId)
                        {
                            Debug.WriteLine(
                                $"[AppAudio] Navegador {processName} — " +
                                $"PID sesión: {processId} → PID raíz: {captureProcessId}");
                        }

                        _logger.LogInformation(
                            "Sesión {ProcessId} en '{Device}': formato endpoint {Format}, " +
                            "volumen sesión {SessionVolume:F2}, volumen endpoint {EndpointVolume:F2}",
                            processId,
                            _device.FriendlyName,
                            endpointFormat,
                            session.SimpleAudioVolume.Volume,
                            _device.AudioEndpointVolume.MasterVolumeLevelScalar);

                        var displayName = GetSessionDisplayName(session.DisplayName, processName);
                        if (displayName is null)
                        {
                            _logger.LogDebug(
                                "Ignoring audio session {ProcessId} because its indirect display name could not be resolved: {DisplayName}",
                                processId,
                                session.DisplayName);
                            continue;
                        }

                        result.Add(new AudioAppInfo
                        {
                            ProcessId = captureProcessId,
                            ProcessName = processName,
                            DisplayName = displayName,
                            IsIncludedInMix = false
                        });
                    }
                    catch (ArgumentException)
                    {
                        // El proceso terminó entre la enumeración de la sesión y esta consulta.
                    }
                    catch (InvalidOperationException)
                    {
                        // La sesión o el proceso dejó de estar disponible durante el refresco.
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error enumerando sesiones de audio");
            }

            // Los navegadores se añaden también por proceso para que aparezcan aunque todavía
            // no hayan creado ninguna sesión de audio en el dispositivo seleccionado.
            var browsers = GetRunningBrowsers();
            result.AddRange(browsers);
            _knownBrowserProcessIds = browsers.Select(browser => browser.ProcessId).ToHashSet();
        }

        // Las sesiones se añaden antes que los procesos silenciosos. Si existe una sesión real,
        // se conserva su nombre para mostrar en lugar del nombre genérico del navegador.
        return result
            .GroupBy(application => application.ProcessId)
            .Select(group => group.First())
            .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void OnSessionCreated(
    object _,
    IAudioSessionControl __)
    {
        RaiseApplicationsChanged();
    }

    private void CheckForBrowserChanges(object? state)
    {
        bool changed;

        try
        {
            var currentProcessIds = GetRunningBrowsers()
                .Select(browser => browser.ProcessId)
                .ToHashSet();

            lock (_sync)
            {
                if (_disposed || _device is null) return;

                changed = !_knownBrowserProcessIds.SetEquals(currentProcessIds);
                if (changed)
                    _knownBrowserProcessIds = currentProcessIds;
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "No se pudo comprobar si cambió la lista de navegadores");
            return;
        }

        if (changed)
            RaiseApplicationsChanged();
    }

    private void RaiseApplicationsChanged()
    {
        try
        {
            ApplicationsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error notificando un cambio en las aplicaciones de audio");
        }
    }

    private static List<AudioAppInfo> GetRunningBrowsers()
    {
        var browsers = new List<AudioAppInfo>();

        foreach (var browserProcessName in BrowserProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(browserProcessName);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        var processName = process.ProcessName;
                        browsers.Add(new AudioAppInfo
                        {
                            ProcessId = GetRootProcessId(process.Id, processName),
                            ProcessName = processName,
                            DisplayName = GetApplicationDisplayName(processName),
                            IsIncludedInMix = false
                        });
                    }
                    catch
                    {
                        // El proceso puede finalizar mientras se está enumerando.
                    }
                }
            }
        }

        return browsers
            .GroupBy(browser => browser.ProcessId)
            .Select(group => group.First())
            .ToList();
    }

    private static string GetApplicationDisplayName(string processName) => processName.ToLowerInvariant() switch
    {
        "chrome" => "Google Chrome",
        "brave" => "Brave",
        "msedge" => "Microsoft Edge",
        "firefox" => "Mozilla Firefox",
        "opera" => "Opera",
        "opera_gx" => "Opera GX",
        "vivaldi" => "Vivaldi",
        "arc" => "Arc",
        "zen" => "Zen Browser",
        _ => processName
    };

    internal static string? GetSessionDisplayName(
        string? sessionDisplayName,
        string processName,
        Func<string, string?>? resolveIndirectString = null)
    {
        if (string.IsNullOrWhiteSpace(sessionDisplayName))
            return GetApplicationDisplayName(processName);

        var candidate = sessionDisplayName.Trim();
        if (!candidate.StartsWith('@'))
            return candidate;

        var resolved = (resolveIndirectString ?? ResolveIndirectString)(candidate);
        return string.IsNullOrWhiteSpace(resolved) || resolved.TrimStart().StartsWith('@')
            ? null
            : resolved.Trim();
    }

    private static string? ResolveIndirectString(string source)
    {
        const int bufferLength = 1024;
        var buffer = new StringBuilder(bufferLength);

        try
        {
            return SHLoadIndirectString(source, buffer, bufferLength, IntPtr.Zero) == 0
                ? buffer.ToString()
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sube por el árbol de procesos hasta encontrar el proceso raíz con el mismo nombre.
    /// Los navegadores crean procesos hijos para pestañas, renderizado y audio, mientras que
    /// ProcessLoopbackCapture necesita el proceso raíz al usar IncludeTargetProcessTree.
    /// </summary>
    private static int GetRootProcessId(int processId, string processName)
    {
        try
        {
            var currentProcessId = processId;

            while (true)
            {
                var parentProcessId = GetParentProcessId(currentProcessId);
                if (parentProcessId <= 0) break;

                try
                {
                    using var parent = Process.GetProcessById(parentProcessId);
                    if (!parent.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                        break;
                }
                catch
                {
                    break;
                }

                currentProcessId = parentProcessId;
            }

            return currentProcessId;
        }
        catch
        {
            return processId;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string source,
        StringBuilder outputBuffer,
        int outputBufferSize,
        IntPtr reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private static int GetParentProcessId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var information = new PROCESS_BASIC_INFORMATION();
            var status = NtQueryInformationProcess(
                process.Handle,
                0,
                ref information,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(),
                out _);

            return status == 0
                ? information.InheritedFromUniqueProcessId.ToInt32()
                : -1;
        }
        catch
        {
            return -1;
        }
    }

    private void DetachSessionManager()
    {
        if (_sessionManager is not null)
            _sessionManager.OnSessionCreated -= OnSessionCreated;

        _sessionManager = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            _browserWatcher.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            DetachSessionManager();
            _device?.Dispose();
            _device = null;
            _enumerator.Dispose();
        }

        _browserWatcher.Dispose();
    }
}
