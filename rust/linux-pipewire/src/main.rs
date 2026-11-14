//! Experimental Linux microphone -> PipeWire virtual microphone path.
//!
//! Uses pw-cat and pipewire-pulse's pactl compatibility layer for the first
//! integration milestone. This CLI is NOT yet a Linux port of the Avalonia UI,
//! a per-application capture backend, or a replacement for Windows WASAPI.

mod app_audio;
mod mic_dsp;
use mic_dsp::{MicrophoneConfig, MicrophoneProcessor};

use itervc_audio_buffers::PcmRingBuffer;
use app_audio::{StreamRoute, parse_sink_inputs, parse_sink_name, find_sink_id, should_restore};
use std::env;
use std::io::{self, Read, Write};
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};

const RATE: usize = 48_000;
const CHANNELS: usize = 2;
const FRAME_BYTES: usize = CHANNELS * std::mem::size_of::<f32>();
const RING_MS: usize = 200;
const SINK_NAME: &str = "itervc_rust_sink";
const MIC_NAME: &str = "itervc_rust_microphone";
const APP_SINK_NAME: &str = "itervc_rust_apps_sink";
const STOP_POLL_INTERVAL: Duration = Duration::from_millis(20);

static STOP: AtomicBool = AtomicBool::new(false);

// A signal handler only flips a lock-free flag; cleanup occurs on main thread.
#[cfg(target_os = "linux")]
mod shutdown {
    use super::{Ordering, STOP};

    extern "C" {
        fn signal(number: i32, handler: extern "C" fn(i32)) -> usize;
    }

    extern "C" fn stop_signal(_: i32) {
        STOP.store(true, Ordering::Relaxed);
    }

    pub fn install() {
        unsafe {
            signal(2, stop_signal);  // SIGINT
            signal(15, stop_signal); // SIGTERM
        }
    }
}

#[derive(Debug, PartialEq)]
enum Mode {
    Help,
    ListInputs,
    ListOutputs,
    ListApps,
    Check,
    Run { input: String, seconds: u64, monitor_output: Option<String>,
        app_stream: Option<u32>, app_volume: f32, mic_volume: f32,
        gate_enabled: bool, gate_threshold_db: f32 },
}

fn parse_args(args: impl IntoIterator<Item = String>) -> Result<Mode, String> {
    let mut mode = None;
    let mut input = None;
    let mut seconds = 30u64;
    let mut monitor_output = None;
    let mut app_stream = None;
    let mut app_volume = 1.0f32;
    let mut mic_volume = 1.0f32;
    let mut gate_enabled = false;
    let mut gate_threshold_db = -45.0f32;
    let mut args = args.into_iter();

    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--help" | "-h" => mode = Some(Mode::Help),
            "--list-inputs" => mode = Some(Mode::ListInputs),
            "--list-outputs" => mode = Some(Mode::ListOutputs),
            "--list-apps" => mode = Some(Mode::ListApps),
            "--check" => mode = Some(Mode::Check),
            "--input" => {
                input = Some(args.next().ok_or("Missing device after --input")?);
            }
            "--app-stream" => {
                let raw = args.next().ok_or("Missing stream id after --app-stream")?;
                app_stream = Some(raw.parse::<u32>().map_err(|_| "Invalid app stream id")?);
            }
            "--mic-volume" => {
                let raw = args.next().ok_or("Missing value after --mic-volume")?;
                mic_volume = raw.parse::<f32>().map_err(|_| "Invalid mic volume")?;
                if !mic_volume.is_finite() || !(0.0..=3.0).contains(&mic_volume) {
                    return Err("Mic volume must be in 0..3".into());
                }
            }
            "--noise-gate" => gate_enabled = true,
            "--gate-threshold" => {
                let raw = args.next().ok_or("Missing dB after --gate-threshold")?;
                gate_threshold_db = raw.parse::<f32>().map_err(|_| "Invalid gate threshold")?;
                if !gate_threshold_db.is_finite() || !(-100.0..=0.0).contains(&gate_threshold_db) {
                    return Err("Gate threshold must be between -100 and 0".into());
                }
            }
            "--app-volume" => {
                let raw = args.next().ok_or("Missing volume after --app-volume")?;
                app_volume = raw.parse::<f32>().map_err(|_| "Invalid app volume")?;
                if !app_volume.is_finite() || !(0.0..=2.0).contains(&app_volume) {
                    return Err("Application volume must be between 0.0 and 2.0".into());
                }
            }
            "--monitor-output" => {
                monitor_output = Some(args.next().ok_or("Missing sink after --monitor-output")?);
            }
            "--seconds" => {
                let value = args.next().ok_or("Missing value after --seconds")?;
                seconds = value.parse().map_err(|_| "Invalid --seconds value")?;
                if seconds > 3600 {
                    return Err("--seconds must be between 0 (continuous) and 3600".into());
                }
            }
            unknown => return Err(format!("Unknown argument: {unknown}")),
        }
    }

    if let Some(mode) = mode {
        if input.is_some() || monitor_output.is_some() || app_stream.is_some() ||
            app_volume != 1.0 || mic_volume != 1.0 || gate_enabled || gate_threshold_db != -45.0 {
            return Err("--input cannot be combined with a query option".into());
        }
        return Ok(mode);
    }

    let input = input.ok_or("Use --input NODE_NAME (see --list-inputs)")?;
    if !is_physical_input(&input) {
        return Err("Specify a physical PipeWire input, not the virtual microphone".into());
    }
    if monitor_output.as_deref().is_some_and(|name| !is_physical_output(name)) {
        return Err("Select a real speaker output, not the virtual sink".into());
    }
    if app_stream.is_none() && app_volume != 1.0 {
        return Err("--app-volume needs --app-stream".into());
    }
    Ok(Mode::Run { input, seconds, monitor_output, app_stream, app_volume,
        mic_volume, gate_enabled, gate_threshold_db })
}

fn print_help() {
    println!(
        "IterVC experimental Linux PipeWire prototype\n\
         Usage:\n\
           itervc-linux-pipewire --list-inputs\n\
           itervc-linux-pipewire --list-outputs\n\
           itervc-linux-pipewire --list-apps\n\
           itervc-linux-pipewire --check\n\
           itervc-linux-pipewire --input <physical-node-name> [--seconds 30] [--monitor-output SINK]\n\n\
         Requirements: a user PipeWire session, pipewire-pulse, pactl and pw-cat.\n\
         A temporary IterVC virtual microphone is created and removed after exit.\n\
         An optional app stream is temporarily redirected to a capture sink\n\
         and restored when the routing session exits.\n\
         --seconds 0 runs until stopped. --monitor-output is optional.\n\
         --app-stream ID selects one running application; --app-volume 0..2.\n\
         --mic-volume 0..3, --noise-gate and --gate-threshold -100..0."
    );
}

fn command_output(exe: &str, args: &[&str]) -> Result<String, String> {
    let output = Command::new(exe)
        .args(args)
        .output()
        .map_err(|error| format!("Could not launch {exe}: {error}"))?;
    if !output.status.success() {
        return Err(format!(
            "{exe} {} failed ({}): {}",
            args.first().unwrap_or(&""),
            output.status,
            String::from_utf8_lossy(&output.stderr).trim()
        ));
    }
    Ok(String::from_utf8_lossy(&output.stdout).trim().to_owned())
}

/// Prevent feedback by routing only explicit physical sources.
fn is_physical_input(name: &str) -> bool {
    !name.trim().is_empty()
        && !name.contains('\0')
        && !name.ends_with(".monitor")
        && !name.starts_with("itervc_rust_")
}

fn available_inputs() -> Result<Vec<String>, String> {
    let output = command_output("pactl", &["list", "short", "sources"])?;
    Ok(output
        .lines()
        .filter_map(|line| line.split_whitespace().nth(1))
        .filter(|name| is_physical_input(name))
        .map(str::to_owned)
        .collect())
}

fn is_physical_output(name: &str) -> bool {
    !name.trim().is_empty() && !name.contains('\0') && !name.starts_with("itervc_rust_")
}

fn available_outputs() -> Result<Vec<String>, String> {
    let output = command_output("pactl", &["list", "short", "sinks"])?;
    Ok(output.lines().filter_map(|line| line.split_whitespace().nth(1))
        .filter(|name| is_physical_output(name)).map(str::to_owned).collect())
}

fn validate_output(name: &str) -> Result<(), String> {
    if !is_physical_output(name) || !available_outputs()?.iter().any(|item| item == name) {
        return Err(format!("Speaker output '{name}' is unavailable. Use --list-outputs."));
    }
    Ok(())
}

fn validate_input(name: &str) -> Result<(), String> {
    if !is_physical_input(name) {
        return Err("Select a physical microphone, not a monitor or virtual source".into());
    }
    if !available_inputs()?.iter().any(|available| available == name) {
        return Err(format!("Microphone '{name}' was not found. Use --list-inputs."));
    }
    Ok(())
}

fn check_dependencies() -> Result<(), String> {
    command_output("pactl", &["info"])?;
    command_output("pw-cat", &["--version"])?;
    Ok(())
}

/// Automatically unloads only the modules created by this process.
struct PipeWireSession {
    modules: Vec<u32>,
    recorder: Option<Child>,
    playback: Option<Child>,
    monitor_playback: Option<Child>,
    final_recorder: Option<Child>,
    app_recorder: Option<Child>,
    app_playback: Option<Child>,
    app_speaker_playback: Option<Child>,
    app_route: Option<StreamRoute>,
}

impl PipeWireSession {
    fn new() -> Self {
        Self {
            modules: Vec::new(),
            recorder: None,
            playback: None,
            monitor_playback: None,
            final_recorder: None,
            app_recorder: None,
            app_playback: None,
            app_speaker_playback: None,
            app_route: None,
        }
    }

    fn load_module(&mut self, args: &[&str]) -> Result<(), String> {
        let output = command_output("pactl", args)?;
        let id = output
            .parse::<u32>()
            .map_err(|_| format!("Unexpected pactl module id: {output}"))?;
        self.modules.push(id);
        Ok(())
    }

    fn create_virtual_microphone(&mut self) -> Result<(), String> {
        let sink_arg = format!("sink_name={SINK_NAME}");
        let monitor_arg = format!("master={SINK_NAME}.monitor");
        let source_arg = format!("source_name={MIC_NAME}");
        self.load_module(&[
            "load-module",
            "module-null-sink",
            sink_arg.as_str(),
            "sink_properties=device.description=IterVC_Rust_Virtual_Output",
        ])?;
        self.load_module(&[
            "load-module",
            "module-remap-source",
            monitor_arg.as_str(),
            source_arg.as_str(),
            "source_properties=device.description=IterVC_Rust_Microphone",
        ])?;
        Ok(())
    }

    fn start_streams(&mut self, input: &str, monitor_output: Option<&str>)
        -> Result<(ChildStdout, ChildStdin, ChildStdout, Option<ChildStdin>), String> {
        let target = format!("--target={input}");
        let mut recorder = Command::new("pw-cat")
            .args([
                "--record",
                "--raw",
                "--format=f32",
                "--rate=48000",
                "--channels=2",
                "--latency=20ms",
                target.as_str(),
                "-",
            ])
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .map_err(|error| format!("Could not start PipeWire microphone capture: {error}"))?;

        let audio_input = recorder.stdout.take().ok_or("Missing capture stdout")?;
        self.recorder = Some(recorder);

        let sink_target = format!("--target={SINK_NAME}");
        let mut playback = Command::new("pw-cat")
            .args([
                "--playback",
                "--raw",
                "--format=f32",
                "--rate=48000",
                "--channels=2",
                "--latency=20ms",
                sink_target.as_str(),
                "-",
            ])
            .stdin(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .map_err(|error| format!("Could not start PipeWire virtual microphone output: {error}"))?;

        let audio_output = playback.stdin.take().ok_or("Missing playback stdin")?;
        self.playback = Some(playback);

        // Capture the *actual mixed sink monitor*, not just the microphone
        // input. Additional application streams share the exact same sink.
        let final_source = format!("--target={SINK_NAME}.monitor");
        let mut final_recorder = Command::new("pw-cat")
            .args(["--record", "--raw", "--format=f32", "--rate=48000",
                "--channels=2", "--latency=20ms", final_source.as_str(), "-"])
            .stdout(Stdio::piped()).stderr(Stdio::inherit()).spawn()
            .map_err(|error| format!("Could not capture the final mixed output: {error}"))?;
        let final_input = final_recorder.stdout.take().ok_or("Missing final sink monitor stdout")?;
        self.final_recorder = Some(final_recorder);

        let monitor_pipe = if let Some(output) = monitor_output {
            let target = format!("--target={output}");
            let mut monitor = Command::new("pw-cat")
                .args(["--playback", "--raw", "--format=f32", "--rate=48000",
                    "--channels=2", "--latency=20ms", target.as_str(), "-"])
                .stdin(Stdio::piped()).stderr(Stdio::inherit()).spawn()
                .map_err(|error| format!("Could not start speaker monitoring: {error}"))?;
            let pipe = monitor.stdin.take().ok_or("No speaker monitor stdin")?;
            self.monitor_playback = Some(monitor);
            Some(pipe)
        } else {
            None
        };
        Ok((audio_input, audio_output, final_input, monitor_pipe))
    }
    /// Capture a single opted-in app stream and retain its original speaker
    /// output. Each stream is addressed by sink-input ID, not an unreliable PID.
    fn start_application(&mut self, stream_id: u32)
        -> Result<(ChildStdout, ChildStdin, ChildStdin), String>
    {
        let current_inputs = command_output("pactl", &["list", "short", "sink-inputs"])?;
        let stream = parse_sink_inputs(&current_inputs)
            .into_iter().find(|item| item.id == stream_id)
            .ok_or_else(|| format!("App stream {stream_id} is no longer available"))?;

        let current_sinks = command_output("pactl", &["list", "short", "sinks"])?;
        let original_name = parse_sink_name(&current_sinks, stream.sink_id)
            .ok_or_else(|| format!("Original app output {} is unavailable", stream.sink_id))?;
        if original_name.starts_with("itervc_rust_") {
            return Err("Refusing to capture IterVC's own audio output".into());
        }

        let sink_arg = format!("sink_name={APP_SINK_NAME}");
        self.load_module(&["load-module", "module-null-sink", sink_arg.as_str(),
            "sink_properties=device.description=IterVC_App_Capture"])?;

        let sinks_after = command_output("pactl", &["list", "short", "sinks"])?;
        let capture_sink_id = find_sink_id(&sinks_after, APP_SINK_NAME)
            .ok_or("Could not locate the isolated application capture sink")?;

        let target = format!("--target={APP_SINK_NAME}.monitor");
        let mut recorder = Command::new("pw-cat")
            .args(["--record", "--raw", "--format=f32", "--rate=48000",
                "--channels=2", "--latency=20ms", target.as_str(), "-"])
            .stdout(Stdio::piped()).stderr(Stdio::inherit()).spawn()
            .map_err(|error| format!("Could not capture application audio: {error}"))?;
        let app_source = recorder.stdout.take().ok_or("Missing application capture stdout")?;
        self.app_recorder = Some(recorder);

        let target_virtual = format!("--target={SINK_NAME}");
        let mut virtual_player = Command::new("pw-cat")
            .args(["--playback", "--raw", "--format=f32", "--rate=48000",
                "--channels=2", "--latency=20ms", target_virtual.as_str(), "-"])
            .stdin(Stdio::piped()).stderr(Stdio::inherit()).spawn()
            .map_err(|error| format!("Could not start app mixer playback: {error}"))?;
        let virtual_pipe = virtual_player.stdin.take().ok_or("No app mixer stdin")?;
        self.app_playback = Some(virtual_player);

        let target_speaker = format!("--target={original_name}");
        let mut speaker_player = Command::new("pw-cat")
            .args(["--playback", "--raw", "--format=f32", "--rate=48000",
                "--channels=2", "--latency=20ms", target_speaker.as_str(), "-"])
            .stdin(Stdio::piped()).stderr(Stdio::inherit()).spawn()
            .map_err(|error| format!("Could not restore application speaker playback: {error}"))?;
        let speaker_pipe = speaker_player.stdin.take().ok_or("No app speaker stdin")?;
        self.app_speaker_playback = Some(speaker_player);

        // No application is moved before every playback and capture pipe
        // has been created. All routing changes are strictly opt-in.
        command_output("pactl", &["move-sink-input", &stream_id.to_string(), APP_SINK_NAME])?;
        self.app_route = Some(StreamRoute {
            stream_id,
            original_sink_id: stream.sink_id,
            original_sink_name: original_name,
            capture_sink_id,
        });
        eprintln!("Capturing application stream {stream_id}; original device {}",
            stream.sink_id);
        Ok((app_source, virtual_pipe, speaker_pipe))
    }

    // Stop both subprocesses to unblock any pending pipe I/O on cancellation.
    fn stop_streams(&mut self) {
        for child in [&mut self.recorder, &mut self.playback, &mut self.monitor_playback, &mut self.final_recorder,
            &mut self.app_recorder, &mut self.app_playback, &mut self.app_speaker_playback].into_iter().flatten() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }
}

impl Drop for PipeWireSession {
    fn drop(&mut self) {
        if let Some(recorder) = self.recorder.as_mut() {
            let _ = recorder.kill();
            let _ = recorder.wait();
        }
        if let Some(playback) = self.playback.as_mut() {
            // Give pw-cat a short chance to drain the pipe after stdin closes;
            // killing immediately can truncate the last PCM frames.
            let deadline = Instant::now() + Duration::from_millis(500);
            loop {
                if matches!(playback.try_wait(), Ok(Some(_))) {
                    break;
                }
                if Instant::now() >= deadline {
                    let _ = playback.kill();
                    let _ = playback.wait();
                    break;
                }
                std::thread::sleep(Duration::from_millis(10));
            }
        }
        if let Some(monitor) = self.monitor_playback.as_mut() {
            let _ = monitor.kill();
            let _ = monitor.wait();
        }
        if let Some(recorder) = self.final_recorder.as_mut() {
            let _ = recorder.kill();
            let _ = recorder.wait();
        }
        // Restore the user's original application destination only if the
        // stream still belongs to our capture sink. Never override a new
        // destination selected by the user while IterVC was running.
        if let Some(route) = self.app_route.take() {
            if let Ok(current) = command_output("pactl", &["list", "short", "sink-inputs"]) {
                if should_restore(&current, &route) {
                    if let Err(error) = command_output("pactl", &["move-sink-input",
                        &route.stream_id.to_string(), &route.original_sink_id.to_string()]) {
                        eprintln!("Could not restore app stream {}: {error}", route.stream_id);
                    }
                }
            }
        }
        for child in [&mut self.app_recorder, &mut self.app_playback, &mut self.app_speaker_playback].into_iter().flatten() {
            let _ = child.kill();
            let _ = child.wait();
        }
        for id in self.modules.drain(..).rev() {
            let _ = Command::new("pactl")
                .args(["unload-module", &id.to_string()])
                .status();
        }
    }
}

/// This stream comes from the final PipeWire sink monitor, after every
/// playback stream has been mixed. The speaker monitor and output meters
/// therefore see the exact signal presented to the virtual microphone.
fn monitor_final_stream(mut capture: impl Read, mut speaker: Option<impl Write>) -> io::Result<()> {
    let mut bytes = [0u8; 4096];
    let mut pending = Vec::with_capacity(4096 + FRAME_BYTES);
    let mut samples = 0usize;
    let mut power = 0.0f64;
    let mut last_report = Instant::now();

    loop {
        let received = match capture.read(&mut bytes) {
            Ok(0) => break,
            Ok(n) => n,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
            Err(error) => return Err(error),
        };
        pending.extend_from_slice(&bytes[..received]);
        let length = pending.len() / FRAME_BYTES * FRAME_BYTES;
        if length == 0 {
            continue;
        }
        if let Some(playback) = speaker.as_mut() {
            if let Err(error) = playback.write_all(&pending[..length]) {
                eprintln!("IterVC speaker monitoring stopped: {error}");
                speaker = None; // Virtual microphone stays active.
            }
        }
        for chunk in pending[..length].chunks_exact(std::mem::size_of::<f32>()) {
            let value = f32::from_le_bytes(chunk.try_into().unwrap()) as f64;
            if value.is_finite() {
                power += value * value;
                samples += 1;
            }
        }
        pending.drain(..length);
        if last_report.elapsed() >= Duration::from_millis(100) && samples > 0 {
            eprintln!("ITERVC_LEVEL:{:.5}", (power / samples as f64).sqrt());
            power = 0.0;
            samples = 0;
            last_report = Instant::now();
        }
    }
    Ok(())
}

fn process_audio(source: impl Read, destination: impl Write, duration: Duration) -> io::Result<u64> {
    process_audio_with_monitor(source, destination, None, duration)
}

fn process_audio_with_monitor(
    source: impl Read, destination: impl Write,
    monitor: Option<&mut dyn Write>, duration: Duration,
) -> io::Result<u64> {
    process_audio_configured(source, destination, monitor, duration, MicrophoneConfig::default())
}

fn process_audio_configured(
    mut source: impl Read, mut destination: impl Write,
    mut monitor: Option<&mut dyn Write>, duration: Duration,
    settings: MicrophoneConfig,
) -> io::Result<u64> {
    // 48 kHz, 2 channels, float32. One bounded ring for the Linux backend.
    let mut ring = PcmRingBuffer::new(RATE * RING_MS / 1000, FRAME_BYTES)
        .expect("Fixed ring configuration is valid");
    let mut processor = MicrophoneProcessor::new(settings)?;
    let mut incoming = [0u8; 4096];
    let mut outbound = [0u8; 4096];
    let mut pending = Vec::with_capacity(incoming.len() + FRAME_BYTES);
    let mut forwarded_bytes = 0u64;
    let start = Instant::now();
    while !STOP.load(Ordering::Relaxed) && start.elapsed() < duration {
        let read = match source.read(&mut incoming) {
            Ok(0) => break,
            Ok(count) => count,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
            Err(error) => return Err(error),
        };
        pending.extend_from_slice(&incoming[..read]);
        let aligned = pending.len() / FRAME_BYTES * FRAME_BYTES;
        if aligned == 0 {
            continue;
        }
        ring.write(&pending[..aligned]);
        pending.drain(..aligned);
        loop {
            let available = ring.available_frames() * FRAME_BYTES;
            if available == 0 {
                break;
            }
            let count = available.min(outbound.len());
            let written = ring.read(&mut outbound[..count]);
            processor.apply(&mut outbound[..written]);
            destination.write_all(&outbound[..written])?;
            if let Some(speaker) = monitor.as_deref_mut() {
                if let Err(error) = speaker.write_all(&outbound[..written]) {
                    eprintln!("IterVC monitoring disabled: {error}");
                    monitor = None;
                }
            }
            forwarded_bytes += written as u64;
        }
    }

    let stats = ring.stats();
    eprintln!(
        "IterVC Linux buffer: forwarded {} frames, dropped {} frames, overflows {}, underflows {}",
        forwarded_bytes / FRAME_BYTES as u64,
        stats.discarded_frames,
        stats.overflow_events,
        stats.underflow_events
    );
    Ok(forwarded_bytes)
}

fn run(input: &str, seconds: u64, monitor_output: Option<&str>,
    app_stream: Option<u32>, app_volume: f32,
    mic_volume: f32, gate_enabled: bool, gate_threshold_db: f32) -> Result<(), String> {
    check_dependencies()?;
    validate_input(input)?;
    if let Some(output) = monitor_output {
        validate_output(output)?;
    }
    #[cfg(target_os = "linux")]
    shutdown::install();

    let mut session = PipeWireSession::new();
    session.create_virtual_microphone()?;
    let (source, destination, mixed_monitor, monitor_pipe) =
        session.start_streams(input, monitor_output)?;
    let app_pipes = if let Some(stream_id) = app_stream {
        Some(session.start_application(stream_id)?)
    } else {
        None
    };

    eprintln!(
        "IterVC Rust prototype: '{}' -> '{}'; active for up to {} seconds.",
        input, MIC_NAME, seconds
    );
    eprintln!("Select 'IterVC Rust Microphone' as the input in your target app.");

    // A silent or disconnected microphone may leave capture blocked in read().
    // Supervise it independently so timeout and shutdown still clean up.
    let duration = if seconds == 0 { Duration::MAX } else { Duration::from_secs(seconds) };
    let deadline = (seconds != 0).then(|| Instant::now() + duration);
    let monitor_worker = std::thread::spawn(move || monitor_final_stream(mixed_monitor, monitor_pipe));
    let app_worker = app_pipes.map(|(source, virtual_output, speaker_output)|
        std::thread::spawn(move || app_audio::forward_app(
            source, virtual_output, speaker_output, app_volume)));
    let mic_settings = MicrophoneConfig { volume: mic_volume, gate_enabled,
        gate_threshold_db, ..MicrophoneConfig::default() };
    let worker = std::thread::spawn(move ||
        process_audio_configured(source, destination, None, duration, mic_settings));
    while !worker.is_finished()
        && !STOP.load(Ordering::Relaxed)
        && !deadline.is_some_and(|cutoff| Instant::now() >= cutoff)
    {
        std::thread::sleep(STOP_POLL_INTERVAL);
    }
    let interrupted = STOP.load(Ordering::Relaxed)
        || deadline.is_some_and(|cutoff| Instant::now() >= cutoff);
    if !worker.is_finished() {
        session.stop_streams();
    }
    let result = worker.join().map_err(|_| "PipeWire audio worker panicked".to_owned())?;
    session.stop_streams(); // Unblock the final sink monitor reader on shutdown.
    let monitor_result = monitor_worker.join()
        .map_err(|_| "Final sink monitor worker panicked".to_owned())?;
    let app_result = if let Some(worker) = app_worker {
        Some(worker.join().map_err(|_| "App audio worker panicked".to_owned())?)
    } else { None };
    if !interrupted {
        result.map_err(|error| format!("PipeWire audio transport error: {error}"))?;
        monitor_result.map_err(|error| format!("PipeWire output monitor error: {error}"))?;
        if let Some(result) = app_result {
            result.map_err(|error| format!("PipeWire application routing error: {error}"))?;
        }
    }
    Ok(())
}

fn main() {
    let result = match parse_args(env::args().skip(1)) {
        Ok(Mode::Help) => {
            print_help();
            Ok(())
        }
        Ok(Mode::Check) => check_dependencies().map(|_| println!("PipeWire tools and session ready.")),
        Ok(Mode::ListApps) => command_output("pactl", &["--format=json", "list", "sink-inputs"])
            .map(|streams| println!("{streams}")),
        Ok(Mode::ListOutputs) => available_outputs().map(|outputs| {
            if outputs.is_empty() { println!("No speaker outputs were found."); }
            else { for output in outputs { println!("{output}"); } }
        }),
        Ok(Mode::ListInputs) => available_inputs().map(|sources| {
            if sources.is_empty() {
                println!("No physical microphones were found.");
            } else {
                for source in sources {
                    println!("{source}");
                }
            }
        }),
        Ok(Mode::Run { input, seconds, monitor_output, app_stream, app_volume,
            mic_volume, gate_enabled, gate_threshold_db }) =>
            run(&input, seconds, monitor_output.as_deref(), app_stream, app_volume,
                mic_volume, gate_enabled, gate_threshold_db),
        Err(error) => Err(error),
    };
    if let Err(error) = result {
        eprintln!("Error: {error}");
        eprintln!("Use --help for usage details.");
        std::process::exit(1);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parse(args: &[&str]) -> Result<Mode, String> {
        parse_args(args.iter().map(|arg| (*arg).to_string()))
    }

    #[test]
    fn refuses_unspecified_source_to_avoid_feedback() {
        assert!(parse(&[]).is_err());
        assert!(parse(&["--input", MIC_NAME]).is_err());
        assert!(parse(&["--input", ""]).is_err());
    }

    #[test]
    fn parses_physical_source_and_duration() {
        assert_eq!(
            parse(&["--input", "alsa_input.usb-microphone", "--seconds", "5"]).unwrap(),
            Mode::Run {
                input: "alsa_input.usb-microphone".into(),
                seconds: 5,
                monitor_output: None,
                app_stream: None,
                app_volume: 1.0,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0
            }
        );
        assert!(parse(&["--input", "mic", "--seconds", "0"]).is_ok());
        assert!(parse(&["--input", "mic", "--seconds", "3601"]).is_err());
    }

    #[test]
    fn app_capture_is_opt_in_and_gain_is_validated() {
        assert_eq!(parse(&["--input", "mic", "--app-stream", "42", "--app-volume", "0.75"]).unwrap(),
            Mode::Run { input: "mic".into(), seconds: 30, monitor_output: None,
                app_stream: Some(42), app_volume: 0.75,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0 });
        assert!(parse(&["--input", "mic", "--app-stream", "unknown"]).is_err());
        assert!(parse(&["--input", "mic", "--app-volume", "0.2"]).is_err());
        assert!(parse(&["--input", "mic", "--app-stream", "2", "--app-volume", "nan"]).is_err());
        assert_eq!(parse(&["--list-apps"]).unwrap(), Mode::ListApps);
    }

    #[test]
    fn microphone_dsp_flags_are_opt_in() {
        assert_eq!(parse(&["--input", "mic", "--mic-volume", "1.25",
            "--noise-gate", "--gate-threshold", "-38"]).unwrap(),
            Mode::Run { input: "mic".into(), seconds: 30, monitor_output: None,
                app_stream: None, app_volume: 1.0,
                mic_volume: 1.25, gate_enabled: true, gate_threshold_db: -38.0 });
        assert!(parse(&["--input", "mic", "--mic-volume", "5"]).is_err());
        assert!(parse(&["--input", "mic", "--gate-threshold", "nan"]).is_err());
    }

    #[test]
    fn offline_end_to_end_pcm_is_identical() {
        STOP.store(false, Ordering::Relaxed);
        // Deliberately use a reader returning partial, unaligned chunks.
        struct SmallChunks {
            inner: std::io::Cursor<Vec<u8>>,
        }
        impl Read for SmallChunks {
            fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
                let count = buf.len().min(13);
                self.inner.read(&mut buf[..count])
            }
        }
        let frames: Vec<u8> = (0..8_000u32).flat_map(|frame| {
            let left = (frame as f32 / 8_000.0).to_le_bytes();
            let right = (-((frame as f32) / 8_000.0)).to_le_bytes();
            left.into_iter().chain(right)
        }).collect();
        let reader = SmallChunks {
            inner: std::io::Cursor::new(frames.clone())
        };
        let mut result = Vec::new();
        let bytes = process_audio(reader, &mut result, Duration::from_secs(10)).unwrap();
        assert_eq!(bytes as usize, frames.len());
        assert_eq!(result, frames);
    }

    #[test]
    fn rejects_monitor_and_own_virtual_sources() {
        assert!(!is_physical_input("alsa_output.monitor"));
        assert!(!is_physical_input("itervc_rust_microphone"));
        assert!(!is_physical_input("itervc_rust_sink.monitor"));
        assert!(!is_physical_input(" "));
        assert!(is_physical_input("alsa_input.usb_microphone"));
    }

    #[test]
    fn monitor_selection_is_optional_and_checked() {
        assert!(parse(&["--input", "physical", "--monitor-output", "itervc_rust_sink"]).is_err());
        assert_eq!(parse(&["--input", "physical", "--monitor-output", "alsa_output.speakers"]).unwrap(),
            Mode::Run { input: "physical".into(), seconds: 30,
                monitor_output: Some("alsa_output.speakers".into()),
                app_stream: None, app_volume: 1.0,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0 });
    }

    #[test]
    fn offline_monitor_matches_virtual_output() {
        let data: Vec<u8> = (0..8192).map(|i| (i % 251) as u8).collect();
        let mut virtual_output = Vec::new();
        let mut speaker_output = Vec::new();
        let count = process_audio_with_monitor(&data[..], &mut virtual_output,
            Some(&mut speaker_output), Duration::from_secs(3)).unwrap();
        assert_eq!(count, data.len() as u64);
        assert_eq!(virtual_output, data);
        assert_eq!(speaker_output, data);
    }

    #[test]
    fn final_mixed_sink_monitor_and_speaker_receive_same_pcm() {
        // Raw audio is supplied by PipeWire's final sink monitor. We must
        // neither alter it nor accidentally use the raw microphone buffer.
        let samples: Vec<u8> = (0..2400u32)
            .flat_map(|sample| ((sample as f32 / 2400.0) - 0.5).to_le_bytes())
            .collect();
        let mut local_monitor = Vec::new();
        monitor_final_stream(&samples[..], Some(&mut local_monitor)).unwrap();
        assert_eq!(local_monitor, samples);
    }

    #[test]
    fn help_and_list_do_not_require_capture_device() {
        assert_eq!(parse(&["--help"]).unwrap(), Mode::Help);
        assert_eq!(parse(&["--list-inputs"]).unwrap(), Mode::ListInputs);
    }
}
