using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace IterVC.Audio;

internal static class AudioLatencyPolicy
{
    internal const int PreferredCableLatencyMilliseconds = 20;
    internal const int FallbackCableLatencyMilliseconds = 40;
    internal const int PreferredMonitorLatencyMilliseconds = 30;
    internal const int FallbackMonitorLatencyMilliseconds = 60;
    internal const int PreferredMicrophoneBufferMilliseconds = 10;
    internal const int FallbackMicrophoneBufferMilliseconds = 20;

    internal static IWavePlayer StartOutput(
        Func<int, IWavePlayer> create,
        IWaveProvider source,
        string role,
        string device,
        int preferredLatency,
        int fallbackLatency,
        ILogger logger)
    {
        Exception? preferredFailure = null;
        foreach (var latency in new[] { preferredLatency, fallbackLatency })
        {
            IWavePlayer? output = null;
            try
            {
                output = create(latency);
                output.Init(source);
                output.Play();
                logger.LogInformation(
                    "Audio output initialized: Role={Role} Device={Device} Format={Format} PreferredLatencyMs={PreferredLatencyMs} SelectedLatencyMs={SelectedLatencyMs} FallbackUsed={FallbackUsed}",
                    role, device, source.WaveFormat, preferredLatency, latency, latency != preferredLatency);
                return output;
            }
            catch (Exception exception)
            {
                try { output?.Stop(); } catch { }
                output?.Dispose();
                if (latency == preferredLatency)
                {
                    preferredFailure = exception;
                    continue;
                }

                throw new AggregateException(
                    $"Could not initialize {role} output at {preferredLatency} ms or {fallbackLatency} ms.",
                    preferredFailure!, exception);
            }
        }

        throw new InvalidOperationException("Audio output initialization did not run.");
    }
}
