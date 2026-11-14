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
    Check,
    Run { input: String, seconds: u64 },
}

fn parse_args(args: impl IntoIterator<Item = String>) -> Result<Mode, String> {
    let mut mode = None;
    let mut input = None;
    let mut seconds = 30u64;
    let mut args = args.into_iter();

    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--help" | "-h" => mode = Some(Mode::Help),
            "--list-inputs" => mode = Some(Mode::ListInputs),
            "--check" => mode = Some(Mode::Check),
            "--input" => {
                input = Some(args.next().ok_or("Missing device after --input")?);
            }
            "--seconds" => {
                let value = args.next().ok_or("Missing value after --seconds")?;
                seconds = value.parse().map_err(|_| "Invalid --seconds value")?;
                if !(1..=3600).contains(&seconds) {
                    return Err("--seconds must be between 1 and 3600".into());
                }
            }
            unknown => return Err(format!("Unknown argument: {unknown}")),
        }
    }

    if let Some(mode) = mode {
        if input.is_some() {
            return Err("--input cannot be combined with a query option".into());
        }
        return Ok(mode);
    }

    let input = input.ok_or("Use --input NODE_NAME (see --list-inputs)")?;
    if input.is_empty() || input == MIC_NAME || input.contains('\0') {
        return Err("Specify a physical PipeWire input, not the virtual microphone".into());
    }
    Ok(Mode::Run { input, seconds })
}

fn print_help() {
    println!(
        "IterVC experimental Linux PipeWire prototype\n\
         Usage:\n\
           itervc-linux-pipewire --list-inputs\n\
           itervc-linux-pipewire --check\n\
           itervc-linux-pipewire --input <physical-node-name> [--seconds 30]\n\n\
         Requirements: a user PipeWire session, pipewire-pulse, pactl and pw-cat.\n\
         A temporary IterVC virtual microphone is created and removed after exit.\n\
         This routes only the selected physical microphone, not application audio."
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
}

impl PipeWireSession {
    fn new() -> Self {
        Self {
            modules: Vec::new(),
            recorder: None,
            playback: None,
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
        self.load_module(&[
            "load-module",
            "module-null-sink",
            "sink_name=itervc_rust_sink",
            "sink_properties=device.description=IterVC_Rust_Virtual_Output",
        ])?;
        self.load_module(&[
            "load-module",
            "module-remap-source",
            "master=itervc_rust_sink.monitor",
            "source_name=itervc_rust_microphone",
            "source_properties=device.description=IterVC_Rust_Microphone",
        ])?;
        Ok(())
    }

    fn start_streams(&mut self, input: &str) -> Result<(ChildStdout, ChildStdin), String> {
        let target = format!("--target={input}");
        let mut recorder = Command::new("pw-cat")
            .args([
                "--record",
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

        let mut playback = Command::new("pw-cat")
            .args([
                "--playback",
                "--format=f32",
                "--rate=48000",
                "--channels=2",
                "--latency=20ms",
                "--target=itervc_rust_sink",
                "-",
            ])
            .stdin(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .map_err(|error| format!("Could not start PipeWire virtual microphone output: {error}"))?;

        let audio_output = playback.stdin.take().ok_or("Missing playback stdin")?;
        self.playback = Some(playback);
        Ok((audio_input, audio_output))
    }
}

impl Drop for PipeWireSession {
    fn drop(&mut self) {
        for child in [&mut self.recorder, &mut self.playback].into_iter().flatten() {
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

fn process_audio(
    mut source: impl Read,
    mut destination: impl Write,
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
            let written = ring.read(&mut outbound);
            if written == 0 {
                break;
            }
            destination.write_all(&outbound[..written])?;
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

fn run(input: &str, seconds: u64) -> Result<(), String> {
    check_dependencies()?;
    #[cfg(target_os = "linux")]
    shutdown::install();

    let mut session = PipeWireSession::new();
    session.create_virtual_microphone()?;
    let (source, destination) = session.start_streams(input)?;

    eprintln!(
        "IterVC Rust prototype: '{}' -> '{}'; active for up to {} seconds.",
        input, MIC_NAME, seconds
    );
    eprintln!("Select 'IterVC Rust Microphone' as the input in your target app.");

    process_audio(source, destination, Duration::from_secs(seconds))
        .map_err(|error| format!("PipeWire audio transport error: {error}"))?;
    Ok(())
}

fn main() {
    let result = match parse_args(env::args().skip(1)) {
        Ok(Mode::Help) => {
            print_help();
            Ok(())
        }
        Ok(Mode::Check) => check_dependencies().map(|_| println!("PipeWire tools and session ready.")),
        Ok(Mode::ListInputs) => command_output("pactl", &["list", "short", "sources"])
            .map(|sources| println!("{sources}")),
        Ok(Mode::Run { input, seconds }) => run(&input, seconds),
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
                seconds: 5
            }
        );
        assert!(parse(&["--input", "mic", "--seconds", "0"]).is_err());
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
    fn help_and_list_do_not_require_capture_device() {
        assert_eq!(parse(&["--help"]).unwrap(), Mode::Help);
        assert_eq!(parse(&["--list-inputs"]).unwrap(), Mode::ListInputs);
    }
}
