# Windows audio buffering: performance review and optimization proposal

**Scope:** static review of `master` at [9b7c0c7](https://github.com/ShxwZ/IterVC/commit/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e), .NET 8 x64 / NAudio 2.2.1. This is a design review, not an implementation or a benchmark. No runtime behavior, audio quality or public API is changed.

## Executive summary

IterVC already uses event-driven shared-mode WASAPI, bounded preallocated PCM rings, frame alignment, silence on underrun, and a separate RNNoise worker. Preserve those properties. The biggest readily identifiable opportunities are removing per-packet managed allocations in application capture, reducing the time a native packet is held, measuring actual device periods and queue ages, and making overflow/clock-drift handling explicit. Shrinking every buffer or replacing every lock is not a safe first optimization.

## 1. Current pipeline and latency budget

- Applications: Windows process-loopback packet → managed byte array → per-PID `RealtimeAudioBuffer` → format adaptation / downmix → application mixer → main mixer → output protection → shared event-driven `WasapiOut` (VB-Cable or selected endpoint).
- Microphone: shared event-driven `WasapiCapture` → optional pooled RNNoise queue / 10 ms processing frame → raw ring → worker (meter, gate, gain) → separate routed and monitor rings → independent outputs.
- The router's mix is 48 kHz, stereo, float32. Capture can be multichannel; keep the validated custom 5.1/7.1 downmix and compare against it, rather than assuming Windows stereo is equivalent.

| Layer | Current setting | Interpretation |
| --- | --- | --- |
| Process loopback | shared/event-driven, duration = 0 | Engine chooses buffer; packet size and cadence must be observed |
| Microphone capture | 10 ms preferred / 20 ms fallback | Requested NAudio buffer duration, not measured capture delay |
| Each PCM ring | 120 ms capacity, 20 ms target, 50 ms correction threshold | Target is a discard destination, not a startup prefill or guaranteed delay |
| RNNoise channel | 6 packets, plus a packet in flight and partial DSP frame | At 10/20 ms per callback, about 60/120 ms of queued audio in that example; callbacks can vary |
| Cable render | 20 ms preferred / 40 ms fallback | Requested, not actual endpoint latency |
| Monitor render | 30 ms preferred / 60 ms fallback | Separate endpoint and clock domain |

At 48 kHz float32, one frame is 4 × channel count bytes. The nominal payload sizes below exclude object headers and other buffers.

| Channels | 10 ms | 20 ms | 120 ms ring | New array payload/sec if every packet is allocated |
| --- | ---: | ---: | ---: | ---: |
| 2 | 3,840 B | 7,680 B | 46,080 B | 384,000 B/s |
| 6 | 11,520 B | 23,040 B | 138,240 B | 1,152,000 B/s |
| 8 | 15,360 B | 30,720 B | 184,320 B | 1,536,000 B/s |

Four 7.1 sources at this rate produce about 6.14 MB/s of transient array payload (about 400 arrays/s if packets are 10 ms), independently of the persistent ring allocations. This is an analytical estimate, not observed GC or CPU cost.

The end-to-end delay comprises capture/engine delay + actual queue residence + DSP/frame aggregation + render/endpoint delay + any virtual cable and receiving application's buffering. Do not add capacities or requested durations and present that as measured latency.

## 2. Findings and recommendations

### P1 — Eliminate recurring allocations and shorten the native packet lifetime

Evidence: [ProcessLoopbackCapture.CaptureLoop, lines 378–425](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/ProcessLoopbackCapture.cs#L378-L425) allocates a fresh array per packet, including silent packets, copies native PCM with Marshal.Copy, calculates channel levels, invokes the public event, then releases the WASAPI buffer in finally. The [router callback](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/AudioRouterService.cs#L197-L225) copies that array again into the ring. Subscriber work, logging and ring-lock contention all extend the native buffer lifetime.

Proposed incremental change:

1. First introduce an **internal synchronous packet sink** with a ReadOnlySpan<byte> / valid-length contract and an explicit silent-packet path. Keep the existing public EventHandler<byte[]> semantics: legacy subscribers may retain their array. Never publish a reused/rented array to those subscribers or silently change its length.
2. Safe initial implementation: allocate one capture-owned scratch array per stream at startup (query GetBufferSize where supported, otherwise use observed sizes and a bounded growth policy). Copy only valid bytes, clear only the valid range for silent packets, release the native packet on the same thread, then invoke the internal sink and diagnostics. Ensure cleanup on errors. Public event subscribers still receive independent exact-length arrays if present.
3. If profiling justifies it, implement an internal RealtimeAudioBuffer.Write(ReadOnlySpan<byte>) and WriteSilence(frameCount). With a synchronous native span, copy into the ring in one pass (up to two contiguous segments on wrap), never retain the span after ReleaseBuffer. Keep lock scope short, perform no logging/DSP/subscriber callbacks while the native pointer is borrowed, and verify HRESULTs. Compare the direct path against scratch-and-release: the latter has one more copy but avoids waiting on a ring lock with a native packet outstanding.

ArrayPool is an alternative for truly asynchronous ownership, not a replacement for lifetime management. A rented buffer can exceed requested length; clear valid bytes for silent data and return exactly once on success, exceptions, cancellation and shutdown. Do not globally pin buffers or introduce unmanaged rings without profiling.

Microsoft's [GetBuffer contract](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer) requires matching ReleaseBuffer on the same thread; pointer lifetime and timely release are correctness constraints. Keep native capture event-driven and drain all available packets.

### P1 — Fix the queue policy and accounting before tuning size

Evidence: [RealtimeAudioBuffer.Write/Read/Clear](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/Buffers/RealtimeAudioBuffer.cs#L42-L115).

- Correction checks occupancy **before** the incoming block (and after overflow discard). Example: 40 ms queued + 20 ms write → 60 ms queued with no correction; the next 10 ms write drops 40 ms of old data then leaves 30 ms, not the 20 ms target. This is existing hysteresis behavior, not an enforced ceiling. Large packets can exceed the threshold even after a correction.
- Oversized writes truncate their incoming prefix before the lock; the discarded prefix is absent from discard/overflow counters, and written frames count only accepted input. Clear drops queued data without recording that discard. Define separate accepted, received, dropped incoming, dropped queued, reset, read and missing frames; ensure accounting is unambiguous. For accepted data: accepted = read + discarded queued + reset + remaining (over a consistent interval).
- ReadFully fills missing frames with silence, but underflows count calls, not missing frames. A silent or idle process, absent microphone, or intentional stop should be classified separately from a deadline miss.
- Validate capacity, target and threshold (positive frame-aligned capacity, sensible target/threshold relation, overflow-safe arithmetic). Decide how to handle an incoming packet larger than target or capacity; keep freshest complete frames when latency is the objective.

Proposed policy: use occupancy projected after write, with separate normal target and emergency maximum; explicitly define whether the whole incoming block must be kept. Use per-route watermarks instead of a single default for microphone raw, routed, monitor and applications. Keep hard dropping for emergencies and use short transitions/crossfades or another tested concealment strategy when appropriate. Do not change the existing hysteresis tests implicitly: policy changes need explicit test updates and review.

Do not reduce physical ring capacity to the target. Capacity is cheap here and protects against bursts; actual occupancy determines queued delay. Keep the 120 ms allocation initially, collect cadence/jitter/occupancy, then A/B candidate target/threshold profiles (e.g. 10–20 / 30–50 ms as experiments, not defaults). A startup prefill, if introduced, adds latency; compare a short prefill and rebuffer strategy against today's immediate playback.

### P1 — Bound RNNoise backlog by age/frames, not just packet count

Evidence: [MicrophoneService.OnDataAvailable / StartSuppression](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/MicrophoneService.cs#L120-L165) uses a pooled copy and a bounded Channel with FullMode.Wait plus TryWrite. A full queue rejects the newest packet, preserving older queued audio. The code does not actually wait for queue capacity, but the callback holds the suppression lock across rent/copy/write.

For voice routing, stale queued audio may be less useful than fresh audio. Measure queue age, dropped frames, processing p95/p99 and timestamp gaps. Test a configurable frame/time bound and a drop-oldest policy with explicit ownership: if using Channel drop modes, use a supported drop callback or an explicit queue that returns the evicted pooled buffer exactly once. Never just switch FullMode to DropOldest and assume the consumer can release evicted arrays. Reset partial RNNoise frame accumulation on gaps/generation changes as appropriate, and validate recurrent DSP state handling. Consider a preallocated bounded slot pool and shorter synchronization only after verifying stop/fault/generation races. Avoid blocking the WASAPI callback and preserve the raw path.

### P2 — Reduce hot-path copies and scheduling overhead in the router

Evidence: [FeedMicrophoneSamples](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/AudioRouterService.cs#L73-L99) catches SemaphoreFullException for coalesced wake-ups; exception allocation is unnecessary on a normal backlog. Replace only with a race-tested coalescing signal (e.g. atomic pending flag + wake event / semaphore; clear before draining, recheck pending work so no wake-up is lost). Include cancellation and dispose tests. Keep the default worker until timing measurements establish whether ThreadPool wake-up jitter is material.

[ProcessMicrophoneAsync](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/AudioRouterService.cs#L307-L329) already reuses float and byte arrays. A synchronous internal helper taking ReadOnlySpan<float> and MemoryMarshal.AsBytes can write into the routed/monitor rings without the intermediate float-to-byte BlockCopy. Keep separate rings for independent consumers. Span locals must not cross awaits (C# 12).

The current ring serializes Read, Write, Clear and GetSnapshot using one lock per buffer. It is not a global lock. Measure contention, then consider moving silence fill outside the lock and skipping zero-length second copies. Avoid a naive lock-free SPSC rewrite: producer-side discard advances the read cursor, Clear can run from control threads, and diagnostics sample shared state. Raw capture versus suppression transitions also complicate single-producer assumptions. A lock-free design would need explicit cursor ownership, memory ordering, generation/reset handling and no overwritten data during reads. Prefer the existing lock until evidence justifies that complexity.

[AdaptToMixFormat](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/AudioRouterService.cs#L404-L422) correctly bypasses conversion for matching 48 kHz stereo. The multichannel downmixer grows scratch storage on demand; prewarm known block sizes if cold-start allocations matter. Benchmark downmix-before-resampling versus the current order for non-48 kHz multichannel only; preserve channel weights and check numerical/spectral equivalence before adopting.

### P2 — Real device timing and independent clocks

Evidence: [AudioLatencyPolicy](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/AudioLatencyPolicy.cs) chooses requested values and falls back on startup exceptions; successful initialization does not demonstrate stable runtime operation. Microphone startup may report asynchronously via RecordingStopped, so verify whether fallback handles those failures in NAudio 2.2.1 rather than assuming it does.

Record actual frame capacities, format, stream latency and device/engine period where supported. For standard endpoints, investigate IAudioClient3 GetSharedModeEnginePeriod / InitializeSharedAudioStream behind a capability gate; select supported periods, respect format restrictions, and fall back to the existing shared path. Do not assume IAudioClient3 is available for the virtual process-loopback client or that smaller requested buffers always reduce latency. Keep shared mode as default; exclusive mode is not a general solution for a multi-app router.

A fixed-rate resampler converts nominal sample rates, not clock drift. Independent capture/render/monitor endpoints may have different effective rates even when all report 48 kHz. At a hypothetical 100 ppm difference, occupancy changes by 4.8 frames/s or 6 ms/min. Persistent occupancy trends warrant adaptive per-source/per-consumer resampling (bounded ppm, smoothed control with deadband, reset on stream changes); sudden glitches need a separate emergency policy. This is a later-stage experiment, not evidence that drift is currently occurring.

The custom capture thread is event-driven but has no explicit MMCSS registration. Measure wake-up latency first; if needed, A/B an Audio/Pro Audio MMCSS registration on the dedicated capture thread, with paired reversion and no priority escalation on arbitrary ThreadPool threads. Maintain cancellation and shutdown behavior.

### P1 — Improve observability before claiming latency improvements

Evidence: [AudioBufferDiagnosticsReporter](https://github.com/ShxwZ/IterVC/blob/9b7c0c7c18d4ebbf558c1b72efc7ef6fdfef595e/IterVC.Audio/Diagnostics/AudioBufferDiagnosticsReporter.cs) logs cumulative min/max/mean and deltas for selected event counts. The mean is operation-weighted (read/write/clear), not a time-weighted queue delay. Router registration passes a fixed 10 ms requested period for application and raw microphone metrics; this can misstate deadline utilization. App processing measurement covers ring.Write, excluding native copy, allocation and channel diagnostics. Monitoring while disabled and reading inactive sources should not be conflated with glitches.

Add low-cost, interval-scoped counters and preallocated histograms, aggregated/logged away from the real-time thread:

- Native packet frames, valid byte count, callback interarrival/jitter, GetBuffer-to-ReleaseBuffer duration, wake-to-complete p50/p95/p99/max.
- Actual engine period and endpoint buffer size/padding, actual packet duration, callback deadline misses, HRESULT categories; observe AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY and TIMESTAMP_ERROR, device position/QPC when valid. Do not treat 200 ms WaitOne timeout as the engine period.
- Per-source occupancy/queue age percentiles, missing/dropped frames by reason, corrections, intentional silence, startup/stop and active consumer state; distinguish packet/worker/render processing.
- Allocation rate and GC pauses correlated with audio events (dotnet-counters/EventPipe; ETW/WPR/WPA for scheduling, DPC and audio glitches). Avoid per-packet logging and unbounded telemetry.

Additional compatibility note: [Microsoft's process-loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/) and activation API specify build 20348 as their supported floor; this repo targets 19041. Treat this as a separate compatibility check on actual supported Windows 10 builds, not a reason to raise the target framework here. Query capabilities and handle unsupported HRESULTs gracefully.

## 3. Implementation sequence (separate reviewable PRs)

| Step | Change | Acceptance gate |
| --- | --- | --- |
| 1 | Instrument real timing, active state and frame accounting | Baseline reproducible, no per-packet logs, no regression |
| 2 | Internal reusable capture scratch + early native release; preserve public event contract | No steady-state per-packet array allocation on internal-only route, identical PCM including silence |
| 3 | Projected occupancy and RNNoise queue-age policy | Bounded latency during overload, correct drop counters, no pool leaks, clean transitions |
| 4 | Span writes / coalesced wake-ups, minor lock optimizations | No lost wake-ups, frame corruption or measurable regression |
| 5 | Device-specific profiles, MMCSS and adaptive drift compensation if measurements justify | Lower p95/p99 latency with no increase in steady-state glitches |

Do not merge these into one broad rewrite. Keep NAudio 2.2.1 and Windows 10 compatibility for the initial changes. No exclusive mode, API break or global GC mode change is required.

## 4. Verification plan

**Automated** (future implementation): extend coverage, with maintainer approval, beyond existing RealtimeAudioBufferTests (alignment, overflow, wrap, silence, basic concurrency). Add deterministic numbered-frame tests for oversized writes, exact threshold boundaries, variable packet size, wrap plus discard, reset accounting, zero/silent packets after nonzero pooled data, logical length versus rented length, and producer/consumer/control interleavings. Verify legacy event array retention, native-release and pool-return on exception, RNNoise overload/generation/cancellation with a fake processor, and coalescing wake race. Simulate independent clocks and burst jitter with a deterministic producer/consumer schedule.

**Windows runtime A/B:** use same hardware, format, source audio, device/driver and OS build; Release x64, warm-up, then at least three 10-minute runs per representative case and a 60-minute drift soak. Start with a baseline, change one variable at a time.

- Windows 10 supported builds and Windows 11; integrated/USB microphones, VB-Cable and real speaker/headphone output; monitor on/off.
- 1/4/8 process sources, stereo/5.1/7.1, 44.1/48 kHz endpoints, silent/active transitions, RNNoise on/off; CPU load and device switching.
- Record end-to-end impulse/MLS cross-correlation with a common clock or calibrated external loopback; include receiving app and virtual cable delay if they are part of the test. Report median/p95/p99 and measurement setup. Test audio continuity and stereo/downmix balance with known signals.
- Capture allocation bytes/s, GC collections/pause times, CPU, context switches, queue-age/occupancy distribution, missing frames and drop reasons, native discontinuities. Compare normalized per active source and active minute.

Suggested gates (targets, not results): zero steady-state per-packet capture array allocations on the internal path, no new audible glitches or additional missing frames in clean steady state versus baseline, no memory/pool leaks on repeated start/stop, no increased p99 callback time, and demonstrably reduced p95/p99 end-to-end latency for any latency-profile change. Under deliberate overload, report the latency/drop tradeoff and recover to target occupancy after load subsides. Run dotnet restore, dotnet build IterVC.sln, dotnet test IterVC.sln on the Windows toolchain before implementation PRs are marked ready.

## References

- [WASAPI capture packet lifetime](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer)
- [IAudioClient and timing APIs](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient)
- [Windows low-latency audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- [IAudioClient3 shared period negotiation](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient3-getsharedmodeengineperiod)
- [Microsoft application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)

**Validation status:** static source review and arithmetic only. No Windows audio device or .NET SDK was available in the review environment; no build, test, ETW capture or benchmark was run. No measured improvement is claimed. This document is intentionally a proposal for human review, not production guidance that has been experimentally validated.
