using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using RadioOSC.Core.Interfaces;
using RadioOSC.Core.Models;

namespace RadioOSC.Audio;

public sealed class ApplicationAudioService : IApplicationAudioService, IDisposable
{
    private readonly ILogger<ApplicationAudioService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;

    // Procesos de navegador conocidos — para estos buscamos el proceso raíz
    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "brave", "msedge", "firefox", "opera", "vivaldi", "arc"
    };

    public ApplicationAudioService(ILogger<ApplicationAudioService> logger)
    {
        _logger = logger;
    }

    public void UseDevice(string outputDeviceId)
    {
        _device?.Dispose();
        _device = _enumerator.GetDevice(outputDeviceId);
    }

    public IReadOnlyList<AudioAppInfo> GetRunningAudioApps()
    {
        var result = new List<AudioAppInfo>();

        if (_device is null)
        {
            _logger.LogWarning("GetRunningAudioApps llamado sin dispositivo seleccionado");
            return result;
        }

        try
        {
            var sessions = _device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                var pid = (int)session.GetProcessID;
                if (pid == 0) continue;

                Process process;
                try
                {
                    process = Process.GetProcessById(pid);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                var processName = process.ProcessName;

                // Para navegadores, subir al proceso raíz para que IncludeTargetProcessTree
                // capture todas las pestañas (procesos hijos renderer/audio/utility)
                int capturePid = pid;
                if (BrowserProcessNames.Contains(processName))
                {
                    capturePid = GetRootProcessId(pid, processName);
                    System.Diagnostics.Debug.WriteLine(
                        $"[AppAudio] Navegador {processName} — PID sesión: {pid} → PID raíz: {capturePid}");
                }

                var displayName = string.IsNullOrWhiteSpace(session.DisplayName)
                    ? processName
                    : session.DisplayName;

                result.Add(new AudioAppInfo
                {
                    ProcessId = capturePid,
                    ProcessName = processName,
                    DisplayName = displayName,
                    IsIncludedInMix = false
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerando sesiones de audio");
        }

        // Un mismo proceso puede tener varias sesiones; se muestra una sola entrada por proceso.
        return result
            .GroupBy(a => a.ProcessId)
            .Select(g => g.First())
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Sube por el árbol de procesos hasta encontrar el proceso raíz del mismo nombre.
    /// Brave/Chrome lanzan muchos procesos hijos con el mismo nombre — el audio sale del hijo
    /// pero hay que capturar desde el padre raíz con IncludeTargetProcessTree.
    /// </summary>
    private static int GetRootProcessId(int pid, string processName)
    {
        try
        {
            var current = pid;
            while (true)
            {
                var parentId = GetParentProcessId(current);
                if (parentId <= 0) break;

                Process parent;
                try { parent = Process.GetProcessById(parentId); }
                catch { break; }

                // Subir solo mientras el padre sea el mismo proceso (mismo nombre)
                if (!parent.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    break;

                current = parentId;
            }
            return current;
        }
        catch
        {
            return pid;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

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

    private static int GetParentProcessId(int pid)
    {
        try
        {
            var handle = Process.GetProcessById(pid).Handle;
            var info = new PROCESS_BASIC_INFORMATION();
            NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf(info), out _);
            return info.InheritedFromUniqueProcessId.ToInt32();
        }
        catch
        {
            return -1;
        }
    }

    public void Dispose()
    {
        _device?.Dispose();
        _enumerator.Dispose();
    }
}