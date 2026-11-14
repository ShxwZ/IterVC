//! Experimental Linux microphone -> PipeWire virtual microphone path.
//!
//! Uses pw-cat and pipewire-pulse's pactl compatibility layer for the first
//! integration milestone. This CLI is NOT yet a Linux port of the Avalonia UI,
//! a per-application capture backend, or a replacement for Windows WASAPI.

use itervc_audio_buffers::PcmRingBuffer;
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

#[derive(Debug, PartialEq, Eq)]
enum Mode {
    Help,
    ListInputs,
    ListOutputs,
    Check,
    Run { input: String, seconds: u64, monitor_output: Option<String> },
}

fn parse_args(args: impl IntoIterator<Item = String>) -> Result<Mode, String> {
    let mut mode = None;
    let mut input = None;
    let mut seconds = 30u64;
    let mut monitor_output = None;
    let mut args = args.into_iter();

    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--help" | "-h" => mode = Some(Mode::Help),
            "--list-inputs" => mode = Some(Mode::ListInputs),
            "--list-outputs" => mode = Some(Mode::ListOutputs),
            "--check" => mode = Some(Mode::Check),
            "--input" => {
                input = Some(args.next().ok_or("Missing device after --input")?);
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
        if input.is_some() || monitor_output.is_some() {
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
    Ok(Mode::Run { input, seconds, monitor_output })
}

fn print_help() {
    println!(
        "IterVC experimental Linux PipeWire prototype\n\
         Usage:\n\
           itervc-linux-pipewire --list-inputs\n\
           itervc-linux-pipewire --list-outputs\n\
           itervc-linux-pipewire --check\n\
           itervc-linux-pipewire --input <physical-node-name> [--seconds 30] [--monitor-output SINK]\n\n\
         Requirements: a user PipeWire session, pipewire-pulse, pactl and pw-cat.\n\
         A temporary IterVC virtual microphone is created and removed after exit.\n\
         This routes only the selected physical microphone, not application audio.\n\
         --seconds 0 runs until stopped. --monitor-output is optional."
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
}

impl PipeWireSession {
    fn new() -> Self {
        Self {
            modules: Vec::new(),
            recorder: None,
            playback: None,
            monitor_playback: None,
            final_recorder: None,
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
    // Stop both subprocesses to unblock any pending pipe I/O on cancellation.
    fn stop_streams(&mut self) {
        for child in [&mut self.recorder, &mut self.playback, &mut self.monitor_playback, &mut self.final_recorder].into_iter().flatten() {
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
    mut source: impl Read,
    mut destination: impl Write,
    mut monitor: Option<&mut dyn Write>,
    duration: Duration,
) -> io::Result<u64> {
    // 48 kHz, 2 channels, float32. One bounded ring for the Linux backend.
    let mut ring = PcmRingBuffer::new(RATE * RING_MS / 1000, FRAME_BYTES)
        .expect("Fixed ring configuration is valid");
    let mut incoming = [0u8; 4096];
    let mut outbound = [0u8; 4096];
    let mut pending = Vec::with_capacity(incoming.len() + FRAME_BYTES);
    let mut forwarded_bytes = 0u64;
    let start = Instant::now();
    let mut last_meter = Instant::now();
    let mut accumulated_power = 0.0f64;
    let mut accumulated_samples = 0usize;

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
            destination.write_all(&outbound[..written])?;
            if let Some(speaker) = monitor.as_deref_mut() {
                if let Err(error) = speaker.write_all(&outbound[..written]) {
                    eprintln!("IterVC monitoring disabled: {error}");
                    monitor = None;
                }
            }
            for sample in outbound[..written].chunks_exact(std::mem::size_of::<f32>()) {
                let value = f32::from_le_bytes(sample.try_into().unwrap()) as f64;
                if value.is_finite() {
                    accumulated_power += value * value;
                    accumulated_samples += 1;
                }
            }
            if last_meter.elapsed() >= Duration::from_millis(100) && accumulated_samples > 0 {
                let rms = (accumulated_power / accumulated_samples as f64).sqrt();
                eprintln!("ITERVC_LEVEL:{rms:.5}");
                last_meter = Instant::now();
                accumulated_power = 0.0;
                accumulated_samples = 0;
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

fn run(input: &str, seconds: u64, monitor_output: Option<&str>) -> Result<(), String> {
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
    let worker = std::thread::spawn(move || process_audio(source, destination, duration));
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
    if !interrupted {
        result.map_err(|error| format!("PipeWire audio transport error: {error}"))?;
        monitor_result.map_err(|error| format!("PipeWire output monitor error: {error}"))?;
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
        Ok(Mode::Run { input, seconds, monitor_output }) =>
            run(&input, seconds, monitor_output.as_deref()),
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
                monitor_output: None
            }
        );
        assert!(parse(&["--input", "mic", "--seconds", "0"]).is_ok());
        assert!(parse(&["--input", "mic", "--seconds", "3601"]).is_err());
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
                monitor_output: Some("alsa_output.speakers".into()) });
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
    fn help_and_list_do_not_require_capture_device() {
        assert_eq!(parse(&["--help"]).unwrap(), Mode::Help);
        assert_eq!(parse(&["--list-inputs"]).unwrap(), Mode::ListInputs);
    }
}
