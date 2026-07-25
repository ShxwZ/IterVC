using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace IterVC.Audio;

internal readonly record struct ProcessRenderFormat(
    int SampleRate,
    ushort Channels,
    string DeviceName)
{
    public static ProcessRenderFormat StereoFallback => new(48000, 2, "process-loopback default");
}

/// <summary>
/// Finds the render endpoint containing a session owned by the target process tree.
/// Process-loopback itself is endpoint-independent, so this lookup is used only to
/// request a capture format that avoids an opaque Windows 7.1-to-stereo downmix.
/// </summary>
internal static class ProcessRenderFormatDetector
{
    public static ProcessRenderFormat Detect(
        int targetProcessId,
        bool includeProcessTree,
        ILogger? logger)
    {
        var best = ProcessRenderFormat.StereoFallback;
        var foundMatchingSession = false;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                var device = devices[deviceIndex];
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    var ownsSession = false;

                    for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                    {
                        var sessionProcessId = (int)sessions[sessionIndex].GetProcessID;
                        if (sessionProcessId == 0)
                            continue;

                        ownsSession = sessionProcessId == targetProcessId ||
                            (includeProcessTree && IsDescendantOf(sessionProcessId, targetProcessId));

                        if (ownsSession)
                            break;
                    }

                    if (!ownsSession)
                        continue;

                    var mixFormat = device.AudioClient.MixFormat;
                    var supportedChannels = mixFormat.Channels is 6 or 8
                        ? (ushort)mixFormat.Channels
                        : (ushort)2;

                    var candidate = new ProcessRenderFormat(
                        mixFormat.SampleRate,
                        supportedChannels,
                        device.FriendlyName);

                    if (!foundMatchingSession || candidate.Channels > best.Channels)
                    {
                        best = candidate;
                        foundMatchingSession = true;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Could not inspect render endpoint '{Device}'", device.FriendlyName);
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not detect the render format for process {ProcessId}", targetProcessId);
        }

        logger?.LogInformation(
            "Detected render format for process {ProcessId}: {SampleRate} Hz, {Channels} channels from '{Device}'",
            targetProcessId,
            best.SampleRate,
            best.Channels,
            best.DeviceName);

        return best;
    }

    private static bool IsDescendantOf(int candidateProcessId, int targetProcessId)
    {
        var currentProcessId = candidateProcessId;

        for (var depth = 0; depth < 64 && currentProcessId > 0; depth++)
        {
            if (currentProcessId == targetProcessId)
                return true;

            var parentProcessId = GetParentProcessId(currentProcessId);
            if (parentProcessId <= 0 || parentProcessId == currentProcessId)
                return false;

            currentProcessId = parentProcessId;
        }

        return false;
    }

    private static int GetParentProcessId(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var info = new PROCESS_BASIC_INFORMATION();
            var status = NtQueryInformationProcess(
                process.Handle,
                0,
                ref info,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(),
                out _);

            return status == 0
                ? info.InheritedFromUniqueProcessId.ToInt32()
                : -1;
        }
        catch
        {
            return -1;
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
}
