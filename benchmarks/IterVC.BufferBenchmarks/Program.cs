using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IterVC.Audio.Buffers;
using Microsoft.Win32.SafeHandles;
using NAudio.Wave;

namespace IterVC.BufferBenchmarks;

/// <summary>
/// Tests AND measures the exact Windows C# source against a Rust C ABI backend.
/// No dependency on microphone hardware, UI or the experimental Linux branch.
/// Ratio > 1 means Rust was faster in this particular shared CI environment.
/// </summary>
internal static class Program
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private const int CapacityMs = 120;
    private const int TargetMs = 20;
    private const int CorrectionMs = 50;

    private record Scenario(string Name, int BlockBytes, bool ReadFully, bool Bursty);
    private record Measurement(string Scenario, string Backend, double[] SamplesMs,
        double MedianMs, double RequestedMiBPerSecond, long Checksum);
    private record Comparison(string Scenario, double CSharpMedianMs, double RustMedianMs,
        double RustSpeedupVsCSharp, double CSharpMiBPerSecond, double RustMiBPerSecond);
    private record Report(string Machine, string Os, string Framework, string Architecture,
        int LogicalProcessors, int IterationsPerTrial, int Trials, Comparison[] Comparisons,
        Measurement[] Measurements);

    private static int Main(string[] args)
    {
        try
        {
            var output = args.Length > 0 ? args[0] : "bench-results";
            var iterations = args.Length > 1 ? int.Parse(args[1]) : 40_000;
            if (iterations < 1_000 || iterations > 2_000_000)
                throw new ArgumentOutOfRangeException(nameof(iterations));

            Console.WriteLine("Checking exact PCM parity (original production C# vs Rust C ABI)...");
            VerifyParity(readFully: true, countUnderflows: true);
            VerifyParity(readFully: false, countUnderflows: true);
            VerifyParity(readFully: true, countUnderflows: false);
            VerifyParity(readFully: false, countUnderflows: false);
            Console.WriteLine("PASS: PCM, latency corrections and all snapshot counters.");

            var scenarios = new[]
            {
                new Scenario("steady-1ms", Format.AverageBytesPerSecond / 1000, true, false),
                new Scenario("steady-10ms", Format.AverageBytesPerSecond / 100, true, false),
                new Scenario("bursty-10ms", Format.AverageBytesPerSecond / 100, false, true),
            };
            const int trials = 5;
            var measurements = new List<Measurement>();
            var comparisons = new List<Comparison>();

            foreach (var scenario in scenarios)
            {
                // Warm both runtimes including JIT, FFI and cache accesses.
                RunManaged(scenario, 5_000);
                RunRust(scenario, 5_000);
                var cs = new double[trials];
                var rust = new double[trials];
                var csChecksum = 0L;
                var rustChecksum = 0L;

                // Alternate order to reduce bias from runner thermal/load drift.
                for (var trial = 0; trial < trials; trial++)
                {
                    if (trial % 2 == 0)
                    {
                        (cs[trial], csChecksum) = RunManaged(scenario, iterations);
                        (rust[trial], rustChecksum) = RunRust(scenario, iterations);
                    }
                    else
                    {
                        (rust[trial], rustChecksum) = RunRust(scenario, iterations);
                        (cs[trial], csChecksum) = RunManaged(scenario, iterations);
                    }
                }
                if (csChecksum != rustChecksum)
                    throw new InvalidOperationException(
                        $"Different output in {scenario.Name}: C#={csChecksum} Rust={rustChecksum}");

                var bytesRequested = (double)iterations * scenario.BlockBytes;
                // Each steady iteration requests one write AND one read; burst
                // requests only one operation in a given iteration.
                if (!scenario.Bursty) bytesRequested *= 2;
                var c = cs.Order().ElementAt(trials / 2);
                var r = rust.Order().ElementAt(trials / 2);
                var csMiB = bytesRequested / (1024 * 1024) / (c / 1000);
                var rustMiB = bytesRequested / (1024 * 1024) / (r / 1000);
                measurements.Add(new(scenario.Name, "production C#",
                    cs, c, csMiB, csChecksum));
                measurements.Add(new(scenario.Name, "Rust + P/Invoke",
                    rust, r, rustMiB, rustChecksum));
                comparisons.Add(new(scenario.Name, c, r, c / r, csMiB, rustMiB));
                Console.WriteLine($"{scenario.Name}: C# {c:F3} ms / Rust+PInvoke {r:F3} ms " +
                                  $"(relative speed {c / r:F2}x)");
            }

            Directory.CreateDirectory(output);
            var report = new Report(Environment.MachineName,
                RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
                RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                iterations, trials, comparisons.ToArray(), measurements.ToArray());
            File.WriteAllText(Path.Combine(output, "results.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            var summary = RenderMarkdown(report);
            File.WriteAllText(Path.Combine(output, "summary.md"), summary);
            Console.WriteLine(summary);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static (double Milliseconds, long Checksum) RunManaged(Scenario scenario, int count)
    {
        var buffer = new RealtimeAudioBuffer(Format, scenario.ReadFully);
        var source = Data(scenario.BlockBytes);
        var target = new byte[scenario.BlockBytes];
        long checksum = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < count; ++i)
        {
            if (!scenario.Bursty)
            {
                buffer.Write(source, 0, source.Length);
                var read = buffer.Read(target, 0, target.Length);
                checksum += read + target[0];
            }
            else if (i % 16 < 8)
                buffer.Write(source, 0, source.Length);
            else
            {
                var read = buffer.Read(target, 0, target.Length);
                checksum += read + target[0];
            }
        }
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds, checksum);
    }

    private static (double Milliseconds, long Checksum) RunRust(Scenario scenario, int count)
    {
        using var buffer = new RustBuffer(scenario.ReadFully, countUnderflows: true);
        var source = Data(scenario.BlockBytes);
        var target = new byte[scenario.BlockBytes];
        long checksum = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < count; ++i)
        {
            if (!scenario.Bursty)
            {
                buffer.Write(source, source.Length);
                var read = buffer.Read(target, target.Length);
                checksum += read + target[0];
            }
            else if (i % 16 < 8)
                buffer.Write(source, source.Length);
            else
            {
                var read = buffer.Read(target, target.Length);
                checksum += read + target[0];
            }
        }
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds, checksum);
    }

    private static byte[] Data(int count)
    {
        var result = new byte[count];
        for (var i = 0; i < count; i++)
            result[i] = (byte)(i * 37 + 11);
        return result;
    }

    private static void VerifyParity(bool readFully, bool countUnderflows)
    {
        var managed = new RealtimeAudioBuffer(Format, readFully, countUnderflows);
        using var native = new RustBuffer(readFully, countUnderflows);
        var input = Data(Format.AverageBytesPerSecond / 1000 * 260 + 7);
        var left = new byte[input.Length];
        var right = new byte[input.Length];
        var random = new Random(0x13579 + (readFully ? 1 : 0) +
            (countUnderflows ? 2 : 0));

        for (var i = 0; i < 2_000; ++i)
        {
            if (i % 113 == 0)
            {
                managed.Clear();
                native.Clear();
            }
            else if (i % 4 != 0)
            {
                // Include short, unaligned, oversized, and bursty chunks.
                var n = i % 71 == 0 ? input.Length
                    : random.Next(0, Format.AverageBytesPerSecond / 12 + 15);
                managed.Write(input, 0, n);
                native.Write(input, n);
            }
            else
            {
                var n = random.Next(0, Format.AverageBytesPerSecond / 12 + 15);
                Array.Fill(left, (byte)0xA5);
                Array.Fill(right, (byte)0xA5);
                var leftCount = managed.Read(left, 0, n);
                var rightCount = native.Read(right, n);
                if (leftCount != rightCount ||
                    !left.AsSpan(0, n + 1).SequenceEqual(right.AsSpan(0, n + 1)))
                    throw new InvalidOperationException(
                        $"PCM mismatch at step {i} (readFully={readFully}, underflow={countUnderflows})");
            }
            if (i % 3 == 0 || i == 1_999)
                AssertSnapshot(managed.GetSnapshot(), native.Snapshot(), i);
        }
    }

    private static void AssertSnapshot(RealtimeAudioBufferSnapshot c, NativeSnapshot r, int step)
    {
        const double toleranceBytes = 0.01;
        var bytesPerMs = Format.AverageBytesPerSecond / 1000d;
        void EqualBytes(double actualMs, double expectedBytes, string field)
        {
            if (Math.Abs(actualMs * bytesPerMs - expectedBytes) > toleranceBytes)
                throw new InvalidOperationException(
                    $"{field} mismatch at operation {step}: C# {actualMs} ms vs Rust {expectedBytes} B");
        }
        void Equal(long actual, ulong expected, string field)
        {
            if (actual != checked((long)expected))
                throw new InvalidOperationException(
                    $"{field} mismatch at operation {step}: C# {actual} vs Rust {expected}");
        }
        EqualBytes(c.BufferedMilliseconds, r.BufferedBytes, "buffered");
        EqualBytes(c.TargetMilliseconds, r.TargetBytes, "target");
        EqualBytes(c.MinimumBufferedMilliseconds, r.MinimumBufferedBytes, "min");
        EqualBytes(c.MaximumBufferedMilliseconds, r.MaximumBufferedBytes, "max");
        EqualBytes(c.AverageBufferedMilliseconds, r.AverageBufferedBytes, "avg");
        Equal(c.TotalFramesWritten, r.WrittenFrames, "written frames");
        Equal(c.TotalFramesRead, r.ReadFrames, "read frames");
        Equal(c.UnderflowBlocks, r.Underflows, "underflows");
        Equal(c.OverflowEvents, r.Overflows, "overflows");
        Equal(c.LatencyCorrections, r.Corrections, "latency corrections");
        Equal(c.DiscardedFrames, r.DiscardedFrames, "discarded frames");
    }

    private static string RenderMarkdown(Report report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## IterVC buffer benchmarks — original C# versus Rust C ABI");
        sb.AppendLine();
        sb.AppendLine($"Platform: **{report.Os}**; .NET: **{report.Framework}**; " +
                      $"architecture: **{report.Architecture}**; CPUs: **{report.LogicalProcessors}**.");
        sb.AppendLine($"Each scenario: {report.Trials} trials, {report.IterationsPerTrial:N0} " +
                      "iterations/trial; warm-up 5,000 iterations; alternating engine order.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Original C# median (ms) | Rust + P/Invoke median (ms) | Relative Rust speed | C# MiB/s | Rust MiB/s |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var row in report.Comparisons)
        {
            sb.AppendLine($"| {row.Scenario} | {row.CSharpMedianMs:F2} | {row.RustMedianMs:F2} | " +
                $"{row.RustSpeedupVsCSharp:F2}× | {row.CSharpMiBPerSecond:F1} | {row.RustMiBPerSecond:F1} |");
        }
        sb.AppendLine();
        sb.AppendLine("**Method:** exact `IterVC.Audio/Buffers/RealtimeAudioBuffer.cs` " +
            "compiled unchanged; Rust C ABI, its mutex and .NET P/Invoke overhead included. " +
            "Each test verifies PCM output and all counters first. MiB/s is **requested** " +
            "read+write payload, not actual audio hardware throughput.");
        sb.AppendLine();
        sb.AppendLine("**Interpretation:** >1× means Rust faster on this particular runner; <1× means " +
            "original C# faster. GitHub-hosted runners vary. Results are descriptive, " +
            "not a performance gate or proof of lower end-to-end WASAPI latency. " +
            "Do not replace the production buffer until repeated local hardware " +
            "benchmarks establish a meaningful win without regressions.");
        return sb.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSnapshot
    {
        public ulong BufferedBytes, TargetBytes, MinimumBufferedBytes, MaximumBufferedBytes;
        public double AverageBufferedBytes;
        public ulong WrittenFrames, ReadFrames, Underflows, Overflows, Corrections, DiscardedFrames;
    }

    internal sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public NativeHandle() : base(true) { }
        protected override bool ReleaseHandle()
        {
            NativeMethods.Free(handle);
            return true;
        }
    }

    internal static class NativeMethods
    {
        private const string Library = "iter_vc_buffers";

        [DllImport(Library, EntryPoint = "ivc_buffer_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeHandle Create(nuint capacityBytes, nuint blockAlign,
            nuint targetBytes, nuint correctionBytes, int readFully, int countUnderflows);

        [DllImport(Library, EntryPoint = "ivc_buffer_write", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nuint Write(NativeHandle handle, [In] byte[] bytes, nuint count);

        [DllImport(Library, EntryPoint = "ivc_buffer_read", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nuint Read(NativeHandle handle, [Out] byte[] bytes, nuint count);

        [DllImport(Library, EntryPoint = "ivc_buffer_clear", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Clear(NativeHandle handle);

        [DllImport(Library, EntryPoint = "ivc_buffer_snapshot", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Snapshot(NativeHandle handle, out NativeSnapshot value);

        [DllImport(Library, EntryPoint = "ivc_buffer_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Free(nint handle);
    }

    internal sealed class RustBuffer : IDisposable
    {
        private readonly NativeHandle _handle;
        public RustBuffer(bool readFully, bool countUnderflows)
        {
            nuint bytesPerSecond = checked((nuint)Format.AverageBytesPerSecond);
            _handle = NativeMethods.Create(
                bytesPerSecond * CapacityMs / 1000, checked((nuint)Format.BlockAlign),
                bytesPerSecond * TargetMs / 1000,
                bytesPerSecond * CorrectionMs / 1000,
                readFully ? 1 : 0, countUnderflows ? 1 : 0);
            if (_handle.IsInvalid)
                throw new InvalidOperationException("Rust buffer creation failed.");
        }
        public void Write(byte[] bytes, int length) =>
            NativeMethods.Write(_handle, bytes, checked((nuint)length));
        public int Read(byte[] bytes, int length) =>
            checked((int)NativeMethods.Read(_handle, bytes, checked((nuint)length)));
        public void Clear() => NativeMethods.Clear(_handle);
        public NativeSnapshot Snapshot()
        {
            if (NativeMethods.Snapshot(_handle, out var snapshot) != 1)
                throw new InvalidOperationException("Could not snapshot Rust buffer.");
            return snapshot;
        }
        public void Dispose() => _handle.Dispose();
    }
}
