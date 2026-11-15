using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IterVC.Audio.Buffers;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace IterVC.WasapiAB;

/// <summary>
/// Opt-in *hardware* probe for the exact production C# ring and the Rust C ABI.
/// No raw PCM is saved, forwarded, played, or transmitted. No changes to
/// IterVC's default device or routing. Not an end-to-end IterVC latency test.
/// </summary>
internal static class Program
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    private const int ReadPeriodMs = 10;
    private const int DefaultSeconds = 12;
    private const int DefaultRounds = 2;

    private sealed record Timing(double P50Us, double P95Us, double P99Us,
        double P999Us, double MaximumUs);
    private sealed record Trial(
        string Backend, int Round, string Device, string Format,
        int RequestedSeconds, int CaptureCallbacks, long CaptureBytes, int ReadCalls,
        int EmptyReads, int ShortReads, long MissingReadFrames, int LateReads,
        Timing CaptureWrite, Timing PlaybackRead,
        BufferCounters Ring, double ProcessCpuMilliseconds,
        long ProcessAllocatedBytes, int Gen0Collections, int Gen1Collections,
        int Gen2Collections);
    private sealed record Report(string Version, string Os, string Runtime,
        string Architecture, int LogicalProcessors, string TestMode,
        string TestProtocol, string Device, string Format, Trial[] Trials);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                ValidateSyntheticEquivalence();
                Console.WriteLine("PASS: standalone C# / Rust native ABI parity (no audio device).");
                return 0;
            }

            if (!args.Contains("--capture-mic", StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "Real microphone capture requires explicit --capture-mic. See --help.");

            var seconds = ReadInt(args, "--seconds", DefaultSeconds, 5, 180);
            var rounds = ReadInt(args, "--rounds", DefaultRounds, 1, 10);
            var output = ReadString(args, "--output", "wasapi-ab-results");
            var readFully = args.Contains("--read-fully", StringComparer.OrdinalIgnoreCase);

            // Match IterVC's production microphone capture format and default
            // communications device. Unsupported hardware is reported clearly.
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            Console.WriteLine($"Capture device: {device.FriendlyName}");
            Console.WriteLine("This voluntary diagnostic opens your microphone only for this benchmark.");
            Console.WriteLine("No audio content is retained, saved, played, sent over the network, or logged.");
            Console.WriteLine("Windows must allow desktop apps to access the microphone.");
            Console.WriteLine($"Format: {Format}; periods: {ReadPeriodMs}ms; seconds/phase: {seconds}; rounds: {rounds}.");
            Console.WriteLine("Order rotates across original C#, Rust and Span C# each round.");
            Console.WriteLine("WARNING: results measure the ring with real WASAPI callbacks, not the full IterVC mixer.");

            var records = new List<Trial>();
            for (int round = 0; round < rounds; round++)
            {
                // Latin-square rotation: with >=3 rounds, each backend runs
                // in every position, reducing warm-up and device drift bias.
                var order = round % 3 switch
                {
                    0 => new[] { 0, 1, 2 },
                    1 => new[] { 1, 2, 0 },
                    _ => new[] { 2, 0, 1 }
                };
                foreach (var candidate in order)
                {
                    using IBufferBackend backend = candidate switch
                    {
                        0 => new ManagedBuffer(Format, readFully),
                        1 => new RustBuffer(Format, readFully),
                        2 => new OptimizedManagedBuffer(Format, readFully),
                        _ => throw new InvalidOperationException("Unknown buffer candidate.")
                    };
                    Console.WriteLine($"Round {round + 1}/{rounds}: {backend.Name}...");
                    var trial = CaptureRealMicrophone(device, backend, seconds, round + 1);
                    records.Add(trial);
                    Console.WriteLine($"  capture p99={trial.CaptureWrite.P99Us:F2} us; " +
                        $"consumer p99={trial.PlaybackRead.P99Us:F2} us; " +
                        $"empty={trial.EmptyReads}; short={trial.ShortReads}; " +
                        $"missingFrames={trial.MissingReadFrames}; late={trial.LateReads}; " +
                        $"overflows={trial.Ring.Overflows}, partialUnderflows={trial.Ring.Underflows}");
                    Thread.Sleep(250); // Release driver resources between phases.
                }
            }

            var result = new Report("2", RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount, readFully ? "processed-readFully" : "raw-microphone",
                "Single real WASAPI microphone; Latin-square rotation of original C#, Rust " +
                "and Span-optimized C#. Capture callback writes while a separate consumer " +
                "reads every 10ms. No playback or file recording.",
                device.FriendlyName, Format.ToString(), records.ToArray());
            Directory.CreateDirectory(output);
            var json = Path.Combine(output, "results.json");
            File.WriteAllText(json, JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "summary.md"), RenderMarkdown(result));
            Console.WriteLine($"Report saved in {Path.GetFullPath(output)}.");
            Console.WriteLine("IMPORTANT: device names and host information appear in the report. " +
                "Review it before sharing.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("WASAPI A/B diagnostic failed: " + error);
            return 1;
        }
    }

    private static Trial CaptureRealMicrophone(
        MMDevice device, IBufferBackend backend, int seconds, int round)
    {
        using var capture = new WasapiCapture(device,
            useEventSync: true, audioBufferMillisecondsLength: 10)
        {
            WaveFormat = Format
        };
        var writeTimes = new long[seconds * 2_000];
        var readTimes = new long[seconds * 2_000];
        var callbackCount = 0;
        var readCount = 0;
        long captureBytes = 0;
        var emptyCount = 0;
        var shortReadCount = 0;
        long missingReadFrames = 0;
        var lateCount = 0;
        Exception? fault = null;
        var accepting = 1;
        var activeCallbacks = 0;
        using var cancellation = new CancellationTokenSource();
        using var finished = new ManualResetEventSlim(false);
        Exception? captureStoppedError = null;
        capture.RecordingStopped += (_, args) =>
        {
            captureStoppedError = args.Exception;
            finished.Set();
        };

        capture.DataAvailable += (_, args) =>
        {
            Interlocked.Increment(ref activeCallbacks);
            try
            {
                if (Volatile.Read(ref accepting) == 0 || args.BytesRecorded <= 0) return;
                var started = Stopwatch.GetTimestamp();
                backend.Write(args.Buffer, args.BytesRecorded);
                var elapsed = Stopwatch.GetTimestamp() - started;
                Interlocked.Add(ref captureBytes, args.BytesRecorded);
                var index = Interlocked.Increment(ref callbackCount) - 1;
                if ((uint)index < (uint)writeTimes.Length)
                    writeTimes[index] = elapsed;
            }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref fault, error, null);
            }
            finally
            {
                Interlocked.Decrement(ref activeCallbacks);
            }
        };

        var consumer = Task.CompletedTask;
        var startCpu = TimeSpan.Zero;
        var endCpu = TimeSpan.Zero;
        var initialBytes = 0L;
        var finalBytes = 0L;
        var gc0 = 0;
        var gc1 = 0;
        var gc2 = 0;

        try
        {
            startCpu = Process.GetCurrentProcess().TotalProcessorTime;
            initialBytes = GC.GetTotalAllocatedBytes(false);
            gc0 = GC.CollectionCount(0);
            gc1 = GC.CollectionCount(1);
            gc2 = GC.CollectionCount(2);

            capture.StartRecording();
            consumer = Task.Factory.StartNew(() =>
            {
                var count = Format.AverageBytesPerSecond * ReadPeriodMs / 1000;
                count -= count % Format.BlockAlign;
                var buffer = new byte[count];
                var period = Stopwatch.Frequency * ReadPeriodMs / 1_000;
                var next = Stopwatch.GetTimestamp() + period;
                while (!cancellation.IsCancellationRequested)
                {
                    // Avoid a tight spin for the entire period. The final
                    // ~2 ms use short spins to keep a useful jitter sample.
                    var current = Stopwatch.GetTimestamp();
                    while (current < next && !cancellation.IsCancellationRequested)
                    {
                        if (next - current > Stopwatch.Frequency / 500)
                            Thread.Sleep(1);
                        else
                            Thread.SpinWait(24);
                        current = Stopwatch.GetTimestamp();
                    }
                    if (cancellation.IsCancellationRequested) break;
                    if (current > next + period / 2)
                        Interlocked.Increment(ref lateCount);
                    var started = Stopwatch.GetTimestamp();
                    var bytes = backend.Read(buffer, buffer.Length);
                    var elapsed = Stopwatch.GetTimestamp() - started;
                    if (bytes == 0) Interlocked.Increment(ref emptyCount);
                    if (bytes < buffer.Length)
                    {
                        Interlocked.Increment(ref shortReadCount);
                        Interlocked.Add(ref missingReadFrames,
                            (buffer.Length - bytes) / Format.BlockAlign);
                    }
                    var index = Interlocked.Increment(ref readCount) - 1;
                    if ((uint)index < (uint)readTimes.Length)
                        readTimes[index] = elapsed;
                    next += period;
                    if (next < current - period) next = current + period;
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
        finally
        {
            // Stop accepting new callback work BEFORE disposal. Wait for the
            // final callback to exit, then release the consumer.
            Volatile.Write(ref accepting, 0);
            try { capture.StopRecording(); } catch (InvalidOperationException) { }
            finished.Wait(TimeSpan.FromSeconds(5));
            var stopped = SpinWait.SpinUntil(
                () => Volatile.Read(ref activeCallbacks) == 0, TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            if (!consumer.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("WASAPI test consumer did not terminate.");
            if (!stopped)
                throw new TimeoutException("WASAPI callback did not terminate.");
            finalBytes = GC.GetTotalAllocatedBytes(false);
            endCpu = Process.GetCurrentProcess().TotalProcessorTime;
        }

        if (captureStoppedError is not null)
            throw new InvalidOperationException("Real WASAPI capture was stopped by an error.", captureStoppedError);
        if (fault is not null)
            throw new InvalidOperationException("Audio-buffer operation failed in the real WASAPI callback.", fault);
        if (callbackCount < 10 || readCount < 10)
            throw new InvalidOperationException("No meaningful microphone callback/read traffic recorded.");

        return new Trial(backend.Name, round, device.FriendlyName, Format.ToString(),
            seconds, callbackCount, captureBytes, readCount, emptyCount,
            shortReadCount, missingReadFrames, lateCount,
            Analyze(writeTimes, Math.Min(callbackCount, writeTimes.Length)),
            Analyze(readTimes, Math.Min(readCount, readTimes.Length)),
            backend.Snapshot(), (endCpu - startCpu).TotalMilliseconds,
            finalBytes - initialBytes,
            GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1,
            GC.CollectionCount(2) - gc2);
    }

    private static Timing Analyze(long[] samples, int count)
    {
        if (count == 0) return new(0, 0, 0, 0, 0);
        var copy = new long[count];
        Array.Copy(samples, copy, count);
        Array.Sort(copy);
        double P(double p) => copy[Math.Clamp((int)Math.Ceiling(count * p) - 1, 0, count - 1)]
            * 1_000_000.0 / Stopwatch.Frequency;
        return new(P(0.5), P(0.95), P(0.99), P(0.999), P(1));
    }

    private static int ReadInt(string[] args, string key, int fallback, int min, int max)
    {
        var index = Array.FindIndex(args, x => string.Equals(x, key,
            StringComparison.OrdinalIgnoreCase));
        if (index < 0) return fallback;
        if (index + 1 == args.Length || !int.TryParse(args[index + 1], out var number) ||
            number < min || number > max)
            throw new ArgumentException($"{key} must be between {min} and {max}.");
        return number;
    }

    private static string ReadString(string[] args, string key, string fallback)
    {
        var index = Array.FindIndex(args, x => string.Equals(x, key,
            StringComparison.OrdinalIgnoreCase));
        if (index < 0) return fallback;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
            throw new ArgumentException($"Missing value after {key}.");
        return args[index + 1];
    }

    private static void ValidateSyntheticEquivalence()
    {
        // No Windows audio hardware required. Runs as part of GitHub Actions
        // and detects missing, wrong-architecture or incompatible Rust DLLs.
        foreach (var readFully in new[] { false, true })
        {
            using IBufferBackend cs = new ManagedBuffer(Format, readFully);
            using IBufferBackend rust = new RustBuffer(Format, readFully);
            using IBufferBackend span = new OptimizedManagedBuffer(Format, readFully);
            var source = new byte[8 * 8_000];
            new Random(57).NextBytes(source);
            var x = new byte[source.Length];
            var y = new byte[source.Length];
            var z = new byte[source.Length];
            var rng = new Random(17);
            for (var i = 0; i < 4_000; ++i)
            {
                if (i % 4 != 0)
                {
                    var amount = rng.Next(0, 7_000) / Format.BlockAlign * Format.BlockAlign;
                    cs.Write(source, amount);
                    rust.Write(source, amount);
                    span.Write(source, amount);
                }
                else
                {
                    var amount = rng.Next(0, 7_000) / Format.BlockAlign * Format.BlockAlign;
                    Array.Fill(x, (byte)91);
                    Array.Fill(y, (byte)91);
                    Array.Fill(z, (byte)91);
                    var n = cs.Read(x, amount);
                    var m = rust.Read(y, amount);
                    var o = span.Read(z, amount);
                    if (n != m || n != o ||
                        !x.AsSpan().SequenceEqual(y) ||
                        !x.AsSpan().SequenceEqual(z))
                        throw new InvalidOperationException($"PCM differs at {i} (readFully={readFully}).");
                }
                var a = cs.Snapshot();
                var b = rust.Snapshot();
                var c = span.Snapshot();
                if (a != c ||
                    a.WrittenFrames != b.WrittenFrames || a.ReadFrames != b.ReadFrames ||
                    a.Underflows != b.Underflows || a.Overflows != b.Overflows ||
                    a.Corrections != b.Corrections || a.DiscardedFrames != b.DiscardedFrames ||
                    Math.Abs(a.BufferedMs - b.BufferedMs) > 0.00001)
                    throw new InvalidOperationException($"Counters differ at operation {i}.");
            }
        }
    }

    private static string RenderMarkdown(Report report)
    {
        var result = new StringBuilder();
        result.AppendLine("# IterVC real WASAPI microphone buffer A/B diagnostic");
        result.AppendLine();
        result.AppendLine($"Platform: {report.Os}; runtime: {report.Runtime}; architecture: {report.Architecture}.");
        result.AppendLine($"Device: **{report.Device}**; format: {report.Format}; mode: {report.TestMode}.");
        result.AppendLine();
        result.AppendLine("| Round | Backend | Callback p50/p95/p99/p99.9 (us) | Read p50/p95/p99/p99.9 (us) | Partial underflows | Overflows | Empty reads | Short reads | Missing frames | Late reads | Process CPU ms | Allocated bytes |");
        result.AppendLine("|---:|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var t in report.Trials)
        {
            static string P(Timing v) =>
                $"{v.P50Us:F2}/{v.P95Us:F2}/{v.P99Us:F2}/{v.P999Us:F2}";
            result.AppendLine($"| {t.Round} | {t.Backend} | {P(t.CaptureWrite)} | {P(t.PlaybackRead)} | " +
                $"{t.Ring.Underflows} | {t.Ring.Overflows} | {t.EmptyReads} | " +
                $"{t.ShortReads} | {t.MissingReadFrames} | {t.LateReads} | " +
                $"{t.ProcessCpuMilliseconds:F0} | {t.ProcessAllocatedBytes:N0} |");
        }
        result.AppendLine();
        result.AppendLine("**Scope:** uses a real microphone capture driver and actual callback traffic, " +
            "feeding only the isolated ring buffer. It does **not** play or record raw PCM, " +
            "run the full IterVC mixer, mix apps or measure end-to-end audio latency.");
        result.AppendLine("Callback time includes the .NET-to-Rust P/Invoke cost; it excludes upstream NAudio processing.");
        result.AppendLine("P99.9 is unreliable when a phase contains fewer than 1000 callbacks.");
        result.AppendLine("In raw mode, partial underflows count only nonempty short reads; " +
            "empty reads are reported separately. The missing-frame count estimates " +
            "what the consumer requested but could not obtain; it is NOT verified " +
            "driver packet loss or audible dropout. In readFully mode, zero padding " +
            "prevents this count from measuring absent audio.");
        result.AppendLine("Late reads reflect Windows scheduling and the diagnostic's own " +
            "pacing; they are not direct evidence of audio-buffer faults. Process " +
            "CPU and allocated bytes include the entire diagnostic process, " +
            "WASAPI and the measurement loop, not only ring-buffer work.");
        result.AppendLine("Use multiple alternating rounds on the same device; no unconditional Rust speedup is assumed.");
        return result.ToString();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            IterVC WASAPI buffer A/B diagnostic (Windows x64, .NET 8).
            --self-test                   Verify C#/Rust PCM parity WITHOUT opening audio hardware.
            --capture-mic                 EXPLICITLY enable real microphone capture for A/B.
            --seconds N                   Duration of each backend phase: 5..180 (default 12).
            --rounds N                    Rotating groups of C#/Rust/Span C#: 1..10 (default 2).
                                         Use at least 3 rounds for balanced order.
            --read-fully                  Simulate processed output buffers instead of raw mic buffer.
            --output DIR                  JSON/Markdown output folder (default wasapi-ab-results).
            --help                        Show this help.

            Run from a console on your OWN Windows machine with the included Rust DLL.
            Nothing changes IterVC's routing; no raw PCM is stored, sent, or played.
            Device name, system metadata and buffer statistics ARE written to the report.
            """);
    }
}
