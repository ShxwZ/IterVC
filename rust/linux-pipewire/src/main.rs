//! Experimental Linux microphone -> PipeWire virtual microphone path.
//!
//! Uses pw-cat and PipeWire's native pw-link graph plus pipewire-pulse's pactl
//! layer to expose microphone and application sound without changing existing
//! speaker destinations. Windows retains its own WASAPI implementation.

mod app_audio;
mod mic_dsp;
#[cfg(target_os = "linux")]
mod rnnoise;
use mic_dsp::{MicrophoneConfig, MicrophoneProcessor};

use itervc_audio_buffers::PcmRingBuffer;
use app_audio::{parse_sink_inputs, parse_sink_name, parse_pactl_node_id, find_output_port_ids};
use std::env;
use std::io::{self, BufRead, Read, Write};
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use std::sync::{mpsc, Arc};
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
        app_streams: Vec<u32>, include_apps: bool, app_volume: f32, mic_volume: f32,
        gate_enabled: bool, gate_threshold_db: f32, gate_attack_ms: f32,
        gate_release_ms: f32 },
}

fn parse_args(args: impl IntoIterator<Item = String>) -> Result<Mode, String> {
    let mut mode = None;
    let mut input = None;
    let mut seconds = 30u64;
    let mut monitor_output = None;
    let mut app_streams: Vec<u32> = Vec::new();
    let mut include_apps = false;
    let mut app_volume = 1.0f32;
    let mut mic_volume = 1.0f32;
    let mut gate_enabled = false;
    let mut gate_threshold_db = -45.0f32;
    let mut gate_attack_ms = 10.0f32;
    let mut gate_release_ms = 150.0f32;
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
            "--include-apps" => include_apps = true,
            "--app-stream" => {
                include_apps = true;
                let raw = args.next().ok_or("Missing stream id after --app-stream")?;
                let id = raw.parse::<u32>().map_err(|_| "Invalid app stream id")?;
                if !app_streams.contains(&id) { app_streams.push(id); }
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
            "--gate-attack" => {
                let raw = args.next().ok_or("Missing milliseconds after --gate-attack")?;
                gate_attack_ms = raw.parse::<f32>().map_err(|_| "Invalid gate attack")?;
                if !gate_attack_ms.is_finite() || !(1.0..=1000.0).contains(&gate_attack_ms) {
                    return Err("Gate attack must be between 1 and 1000 ms".into());
                }
            }
            "--gate-release" => {
                let raw = args.next().ok_or("Missing milliseconds after --gate-release")?;
                gate_release_ms = raw.parse::<f32>().map_err(|_| "Invalid gate release")?;
                if !gate_release_ms.is_finite() || !(1.0..=2000.0).contains(&gate_release_ms) {
                    return Err("Gate release must be between 1 and 2000 ms".into());
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
        if input.is_some() || monitor_output.is_some() || include_apps ||
            app_volume != 1.0 || mic_volume != 1.0 || gate_enabled || gate_threshold_db != -45.0 ||
            gate_attack_ms != 10.0 || gate_release_ms != 150.0 {
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
    if !include_apps && app_volume != 1.0 {
        return Err("--app-volume needs --include-apps or --app-stream".into());
    }
    Ok(Mode::Run { input, seconds, monitor_output, app_streams, include_apps, app_volume,
        mic_volume, gate_enabled, gate_threshold_db, gate_attack_ms, gate_release_ms })
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
         Selected apps are tapped via PipeWire links; their speaker routing remains unchanged.\n\
         --seconds 0 runs until stopped. --monitor-output is optional.\n\
         --app-stream ID selects an app (repeatable); --include-apps waits for future streams.\n\
          --app-volume 0..2.\n\
         --mic-volume 0..3, --noise-gate and --gate-threshold -100..0."
    );
}

fn command_output(exe: &str, args: &[&str]) -> Result<String, String> {
    let output = Command::new(exe)
        .args(args)
        .env("LC_ALL", "C")
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
    app_links: Vec<(String, String)>,
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
            app_links: Vec::new(),
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
    /// Tap explicitly selected playback streams using PipeWire's native graph.
    /// The original physical-speaker links stay connected throughout.
    fn start_application(&mut self, stream_ids: &[u32])
        -> Result<(ChildStdout, ChildStdin), String>
    {
        let available = parse_sink_inputs(
            &command_output("pactl", &["list", "short", "sink-inputs"])?);
        let sinks = command_output("pactl", &["list", "short", "sinks"])?;
        let detailed_inputs = command_output("pactl", &["list", "sink-inputs"])?;
        let mut node_ids = Vec::new();
        for id in stream_ids {
            let stream = available.iter().find(|entry| entry.id == *id)
                .ok_or_else(|| format!("App stream {id} is not available"))?;
            let original_name = parse_sink_name(&sinks, stream.sink_id)
                .ok_or_else(|| format!("App stream {id} has no valid physical speaker output"))?;
            if original_name.starts_with("itervc_rust_") {
                return Err("Refusing to capture IterVC's own audio stream".into());
            }
            // Ignore our own speaker/monitor playback to avoid re-capturing
            // the virtual microphone output into itself through the speakers.
            let our_pids: Vec<u32> = [
                self.playback.as_ref(),
                self.monitor_playback.as_ref(),
                self.app_playback.as_ref()
            ].into_iter().flatten().map(|child| child.id()).collect();
            if app_audio::parse_pactl_process_id(&detailed_inputs, *id)
                .is_some_and(|pid| our_pids.contains(&pid))
            {
                return Err(format!("Refusing to capture IterVC's own playback stream {id}"));
            }
            let node_id = parse_pactl_node_id(&detailed_inputs, *id)
                .ok_or_else(|| format!("PipeWire object.id for app stream {id} was not found"))?;
            if node_ids.contains(&node_id) {
                return Err(format!("App stream {id} resolves to an already selected PipeWire node"));
            }
            node_ids.push(node_id);
        }

        // Only the selected applications' output ports are connected to this
        // null sink. No change is made to their existing physical outputs.
        let sink_arg = format!("sink_name={APP_SINK_NAME}");
        self.load_module(&["load-module", "module-null-sink", sink_arg.as_str(),
            "sink_properties=device.description=IterVC_App_Capture"])?;

        let monitor_target = format!("--target={APP_SINK_NAME}.monitor");
        let mut recorder = Command::new("pw-cat")
            .args(["--record", "--raw", "--format=f32", "--rate=48000",
                "--channels=2", "--latency=20ms", monitor_target.as_str(), "-"])
            .stdout(Stdio::piped()).stderr(Stdio::inherit()).spawn()
            .map_err(|error| format!("Could not capture tapped application audio: {error}"))?;
        let app_source = recorder.stdout.take().ok_or("Missing app capture stdout")?;
        self.app_recorder = Some(recorder);

        let mixer_target = format!("--target={SINK_NAME}");
        let mut mixer = Command::new("pw-cat")
            .args(["--playback", "--raw", "--format=f32", "--rate=48000",
                "--channels=2", "--latency=20ms", mixer_target.as_str(), "-"])
            .stdin(Stdio::piped()).stderr(Stdio::inherit()).spawn()
            .map_err(|error| format!("Could not start app mix playback: {error}"))?;
        let virtual_pipe = mixer.stdin.take().ok_or("No app mix stdin")?;
        self.app_playback = Some(mixer);

        // Match the object.id from pactl to pw-cli Port's node.id, obtaining
        // unique numeric port IDs. Duplicate display names are common: two
        // playback streams may both be called "pacat" or "Spotify".
        let mut ports = None;
        for _ in 0..25 {
            let listing = command_output("pw-cli", &["ls", "Port"])?;
            let inputs = command_output("pw-link", &["--input"])?;
            let capture_left = format!("{APP_SINK_NAME}:playback_FL");
            let capture_right = format!("{APP_SINK_NAME}:playback_FR");
            if !inputs.lines().any(|line| line.trim() == capture_left) ||
                !inputs.lines().any(|line| line.trim() == capture_right) {
                std::thread::sleep(Duration::from_millis(50));
                continue;
            }
            let mut links = Vec::new();
            let mut all_found = true;
            for node_id in &node_ids {
                if let Some((left, right)) = find_output_port_ids(&listing, *node_id) {
                    links.push(vec![
                        (left.to_string(), capture_left.clone()),
                        (right.to_string(), capture_right.clone()),
                    ]);
                } else {
                    all_found = false;
                    break;
                }
            }
            if all_found {
                ports = Some(links);
                break;
            }
            std::thread::sleep(Duration::from_millis(50));
        }
        let ports = ports.ok_or(
            "Could not identify unique stereo PipeWire port IDs for selected apps; existing speaker routes were not changed")?;

        for (source, destination) in ports.into_iter().flatten() {
            command_output("pw-link", &[&source, &destination])?;
            self.app_links.push((source, destination));
        }

        eprintln!("Tapping {} app streams while preserving existing speaker routes",
            stream_ids.len());
        Ok((app_source, virtual_pipe))
    }

    /// Adds a newly launched instance of an opted-in application to an
    /// existing PipeWire capture graph. Only extra fan-out links are created;
    /// the application's original physical speaker links are untouched.
    fn add_application_stream(&mut self, stream_id: u32) -> Result<(), String> {
        if self.app_recorder.is_none() || self.app_playback.is_none() {
            return Err("Application capture is not active; enable it before starting routing".into());
        }

        let available = parse_sink_inputs(
            &command_output("pactl", &["list", "short", "sink-inputs"])?);
        let stream = available.iter().find(|entry| entry.id == stream_id)
            .ok_or_else(|| format!("App stream {stream_id} is no longer available"))?;
        let sinks = command_output("pactl", &["list", "short", "sinks"])?;
        let original_name = parse_sink_name(&sinks, stream.sink_id)
            .ok_or_else(|| format!("App stream {stream_id} has no valid original speaker output"))?;
        if original_name.starts_with("itervc_rust_") {
            return Err("Refusing to capture IterVC's own audio sink".into());
        }
        let details = command_output("pactl", &["list", "sink-inputs"])?;
        let our_pids: Vec<u32> = [
            self.playback.as_ref(), self.monitor_playback.as_ref(), self.app_playback.as_ref()
        ].into_iter().flatten().map(|child| child.id()).collect();
        if app_audio::parse_pactl_process_id(&details, stream_id)
            .is_some_and(|pid| our_pids.contains(&pid))
        {
            return Err(format!("Refusing to capture IterVC's own playback stream {stream_id}"));
        }
        let node_id = parse_pactl_node_id(&details, stream_id)
            .ok_or_else(|| format!("PipeWire object ID missing for app stream {stream_id}"))?;

        let capture_left = format!("{APP_SINK_NAME}:playback_FL");
        let capture_right = format!("{APP_SINK_NAME}:playback_FR");
        for _ in 0..25 {
            let listing = command_output("pw-cli", &["ls", "Port"])?;
            let Some((left_id, right_id)) = find_output_port_ids(&listing, node_id)
                else {
                    std::thread::sleep(Duration::from_millis(50));
                    continue;
                };
            let links = [
                (left_id.to_string(), capture_left.clone()),
                (right_id.to_string(), capture_right.clone()),
            ];
            for (source, target) in links {
                if self.app_links.iter().any(|entry| entry.0 == source && entry.1 == target) {
                    continue;
                }
                command_output("pw-link", &[source.as_str(), target.as_str()])?;
                self.app_links.push((source, target));
            }
            eprintln!("Dynamically linked application stream {stream_id} while preserving its speaker output");
            return Ok(());
        }
        Err(format!("Stereo PipeWire ports for app stream {stream_id} were not found"))
    }

    // Stop both subprocesses to unblock any pending pipe I/O on cancellation.
    fn stop_streams(&mut self) {
        for child in [&mut self.recorder, &mut self.playback, &mut self.monitor_playback, &mut self.final_recorder,
            &mut self.app_recorder, &mut self.app_playback].into_iter().flatten() {
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
        // Disconnect ONLY the extra taps created by IterVC. The selected apps'
        // speaker links have never been moved and remain intact.
        for (source, destination) in self.app_links.drain(..).rev() {
            if let Err(error) = command_output("pw-link", &["--disconnect", &source, &destination]) {
                eprintln!("Could not remove PipeWire app tap {source} -> {destination}: {error}");
            }
        }
        for child in [&mut self.app_recorder, &mut self.app_playback].into_iter().flatten() {
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

#[cfg(test)]
fn process_audio(source: impl Read, destination: impl Write, duration: Duration) -> io::Result<u64> {
    process_audio_with_monitor(source, destination, None, duration)
}

#[cfg(test)]
fn process_audio_with_monitor(
    source: impl Read, destination: impl Write,
    monitor: Option<&mut dyn Write>, duration: Duration,
) -> io::Result<u64> {
    process_audio_configured(source, destination, monitor, duration, MicrophoneConfig::default())
}

#[cfg(test)]
fn process_audio_configured(
    source: impl Read, destination: impl Write,
    monitor: Option<&mut dyn Write>, duration: Duration,
    settings: MicrophoneConfig,
) -> io::Result<u64> {
    let gain = AtomicU32::new(settings.volume.to_bits());
    let gate = LiveGateSettings::new(settings);
    process_audio_live(source, destination, monitor, duration, settings, &gain, &gate)
}

/// Aggregate finite stereo PCM power without allocating per audio callback.
/// The input and processed microphone meters are kept separate from the
/// final PipeWire mix meter (which can also contain selected applications).
fn accumulate_pcm_power(data: &[u8], power: &mut f64, count: &mut u64) {
    for sample in data.chunks_exact(std::mem::size_of::<f32>()) {
        let value = f32::from_le_bytes(sample.try_into().unwrap()) as f64;
        if value.is_finite() {
            *power += value * value;
            *count += 1;
        }
    }
}

fn process_audio_live(
    mut source: impl Read, mut destination: impl Write,
    mut monitor: Option<&mut dyn Write>, duration: Duration,
    settings: MicrophoneConfig, gain_bits: &AtomicU32, gate: &LiveGateSettings,
) -> io::Result<u64> {
    // 48 kHz, 2 channels, float32. One bounded ring for the Linux backend.
    let mut ring = PcmRingBuffer::new(RATE * RING_MS / 1000, FRAME_BYTES)
        .expect("Fixed ring configuration is valid");
    let mut processor = MicrophoneProcessor::new(settings)?;
    #[cfg(target_os = "linux")]
    let mut suppressor: Option<rnnoise::NoiseSuppressor> = None;
    #[cfg(target_os = "linux")]
    let mut suppressor_failed = false;
    let mut incoming = [0u8; 4096];
    // Fixed 10ms chunks allow lossless RNNoise processing without allocating
    // or blocking the PipeWire capture callback. Non-suppressed audio is
    // forwarded identically (only the write chunk size changes).
    let mut outbound = [0u8; 3840];
    let mut pending = Vec::with_capacity(incoming.len() + FRAME_BYTES);
    let mut forwarded_bytes = 0u64;
    let mut mic_input_power = 0.0f64;
    let mut mic_output_power = 0.0f64;
    let mut mic_sample_count = 0u64;
    let mut mic_output_count = 0u64;
    let mut mic_last_report = Instant::now();
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
            #[cfg(target_os = "linux")]
            {
                let requested = gate.noise_suppression.load(Ordering::Relaxed);
                if !requested {
                    if suppressor.is_some() { eprintln!("ITERVC_RNNOISE_DISABLED"); }
                    suppressor = None;
                    suppressor_failed = false;
                } else if suppressor.is_none() && !suppressor_failed {
                    match rnnoise::NoiseSuppressor::new() {
                        Ok(runtime) => {
                            suppressor = Some(runtime);
                            eprintln!("ITERVC_RNNOISE_ACTIVE");
                        }
                        Err(error) => {
                            suppressor_failed = true;
                            eprintln!("ITERVC_RNNOISE_ERROR:{error}; falling back to original microphone audio");
                        }
                    }
                }
                if suppressor.is_some() && available < rnnoise::FRAME_BYTES {
                    break; // Wait for a complete 10 ms stereo frame.
                }
            }
            let count = available.min(outbound.len());
            let written = ring.read(&mut outbound[..count]);
            accumulate_pcm_power(&outbound[..written], &mut mic_input_power, &mut mic_sample_count);
            #[cfg(target_os = "linux")]
            if let Some(runtime) = suppressor.as_mut() {
                if let Err(error) = runtime.process_frame(&mut outbound[..written]) {
                    eprintln!("ITERVC_RNNOISE_ERROR:{error}; falling back to original microphone audio");
                    suppressor = None;
                    suppressor_failed = true;
                }
            }
            processor.set_volume(f32::from_bits(gain_bits.load(Ordering::Relaxed)))?;
            processor.set_gate(
                gate.enabled.load(Ordering::Relaxed),
                f32::from_bits(gate.threshold_db.load(Ordering::Relaxed)),
                f32::from_bits(gate.attack_ms.load(Ordering::Relaxed)),
                f32::from_bits(gate.release_ms.load(Ordering::Relaxed)),
            )?;
            processor.apply(&mut outbound[..written]);
            accumulate_pcm_power(&outbound[..written], &mut mic_output_power, &mut mic_output_count);
            if mic_last_report.elapsed() >= Duration::from_millis(100) && mic_sample_count > 0 {
                eprintln!("ITERVC_MIC_INPUT_LEVEL:{:.5}",
                    (mic_input_power / mic_sample_count as f64).sqrt());
                eprintln!("ITERVC_MIC_OUTPUT_LEVEL:{:.5}",
                    (mic_output_power / mic_output_count.max(1) as f64).sqrt());
                mic_input_power = 0.0;
                mic_output_power = 0.0;
                mic_sample_count = 0;
                mic_output_count = 0;
                mic_last_report = Instant::now();
            }
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

    // Release any sub-10ms tail as unprocessed PCM instead of silently
    // truncating the end of a source when suppression is active.
    while ring.available_frames() > 0 {
        let available = ring.available_frames() * FRAME_BYTES;
        let count = available.min(outbound.len());
        let written = ring.read(&mut outbound[..count]);
        processor.set_volume(f32::from_bits(gain_bits.load(Ordering::Relaxed)))?;
        processor.apply(&mut outbound[..written]);
        destination.write_all(&outbound[..written])?;
        if let Some(speaker) = monitor.as_deref_mut() {
            if speaker.write_all(&outbound[..written]).is_err() { monitor = None; }
        }
        forwarded_bytes += written as u64;
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

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum GainTarget { Microphone, Applications }

/// Only bounded, finite gain values are accepted from the Linux GUI.
fn parse_live_command(command: &str) -> Option<(GainTarget, f32)> {
    let mut parts = command.split_whitespace();
    let target = match parts.next()? {
        "mic-volume" => GainTarget::Microphone,
        "app-volume" => GainTarget::Applications,
        _ => return None,
    };
    let value = parts.next()?.parse::<f32>().ok()?;
    if parts.next().is_some() || !value.is_finite() {
        return None;
    }
    let maximum = if target == GainTarget::Microphone { 3.0 } else { 2.0 };
    (0.0..=maximum).contains(&value).then_some((target, value))
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum GateTarget { Enabled, Threshold, Attack, Release, NoiseSuppression }

struct LiveGateSettings {
    enabled: AtomicBool,
    noise_suppression: AtomicBool,
    threshold_db: AtomicU32,
    attack_ms: AtomicU32,
    release_ms: AtomicU32,
}

impl LiveGateSettings {
    fn new(config: MicrophoneConfig) -> Self {
        Self {
            enabled: AtomicBool::new(config.gate_enabled),
            noise_suppression: AtomicBool::new(false),
            threshold_db: AtomicU32::new(config.gate_threshold_db.to_bits()),
            attack_ms: AtomicU32::new(config.attack_ms.to_bits()),
            release_ms: AtomicU32::new(config.release_ms.to_bits()),
        }
    }

    fn update(&self, target: GateTarget, value: f32) {
        match target {
            GateTarget::Enabled => self.enabled.store(value == 1.0, Ordering::Relaxed),
            GateTarget::NoiseSuppression => self.noise_suppression.store(value == 1.0, Ordering::Relaxed),
            GateTarget::Threshold => self.threshold_db.store(value.to_bits(), Ordering::Relaxed),
            GateTarget::Attack => self.attack_ms.store(value.to_bits(), Ordering::Relaxed),
            GateTarget::Release => self.release_ms.store(value.to_bits(), Ordering::Relaxed),
        }
    }
}

fn parse_gate_command(command: &str) -> Option<(GateTarget, f32)> {
    let mut parts = command.split_whitespace();
    let target = match parts.next()? {
        "gate-enabled" => GateTarget::Enabled,
        "noise-suppression" => GateTarget::NoiseSuppression,
        "gate-threshold" => GateTarget::Threshold,
        "gate-attack" => GateTarget::Attack,
        "gate-release" => GateTarget::Release,
        _ => return None,
    };
    let value = parts.next()?.parse::<f32>().ok()?;
    if parts.next().is_some() || !value.is_finite() { return None; }
    let valid = match target {
        GateTarget::Enabled | GateTarget::NoiseSuppression => value == 0.0 || value == 1.0,
        GateTarget::Threshold => (-100.0..=0.0).contains(&value),
        GateTarget::Attack => (1.0..=1000.0).contains(&value),
        GateTarget::Release => (1.0..=2000.0).contains(&value),
    };
    valid.then_some((target, value))
}

/// App indices change when programs restart; a valid zero-based ID is sent
/// to the main thread, which owns all PipeWire link creation and cleanup.
fn parse_add_app_command(command: &str) -> Option<u32> {
    let mut parts = command.split_whitespace();
    if parts.next()? != "add-app" {
        return None;
    }
    let id = parts.next()?.parse::<u32>().ok()?;
    parts.next().is_none().then_some(id)
}

fn watch_live_controls(microphone: Arc<AtomicU32>, applications: Arc<AtomicU32>,
    gate: Arc<LiveGateSettings>, new_apps: mpsc::Sender<u32>) {
    // Stdin is owned by IterVC's GUI when launched from the portable binary.
    // It is never read by the PipeWire worker or a WASAPI callback.
    std::thread::spawn(move || {
        for line in io::stdin().lock().lines() {
            if STOP.load(Ordering::Relaxed) { break; }
            let Ok(line) = line else { break };
            match parse_live_command(&line) {
                Some((GainTarget::Microphone, value)) =>
                    microphone.store(value.to_bits(), Ordering::Relaxed),
                Some((GainTarget::Applications, value)) =>
                    applications.store(value.to_bits(), Ordering::Relaxed),
                None => {
                    if let Some((target, value)) = parse_gate_command(&line) {
                        gate.update(target, value);
                    } else if let Some(id) = parse_add_app_command(&line) {
                        if new_apps.send(id).is_err() { break; }
                    } else {
                        eprintln!("Ignoring invalid live audio control");
                    }
                },
            }
        }
    });
}

fn run(input: &str, seconds: u64, monitor_output: Option<&str>,
    app_streams: &[u32], include_apps: bool, app_volume: f32,
    mic_volume: f32, gate_enabled: bool, gate_threshold_db: f32,
    gate_attack_ms: f32, gate_release_ms: f32) -> Result<(), String> {
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
    let app_pipes = if include_apps {
        Some(session.start_application(app_streams)?)
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
    let mic_gain = Arc::new(AtomicU32::new(mic_volume.to_bits()));
    let app_gain = Arc::new(AtomicU32::new(app_volume.to_bits()));
    let app_gain_worker = Arc::clone(&app_gain);
    let app_worker = app_pipes.map(|(source, virtual_output)|
        std::thread::spawn(move || app_audio::forward_app_live(
            source, virtual_output, app_gain_worker.as_ref())));
    let mic_settings = MicrophoneConfig { volume: mic_volume, gate_enabled,
        gate_threshold_db, attack_ms: gate_attack_ms, release_ms: gate_release_ms };
    let live_gate = Arc::new(LiveGateSettings::new(mic_settings));
    let gate_worker = Arc::clone(&live_gate);
    let mic_gain_worker = Arc::clone(&mic_gain);
    let worker = std::thread::spawn(move ||
        process_audio_live(source, destination, None, duration, mic_settings,
            mic_gain_worker.as_ref(), gate_worker.as_ref()));
    let (new_app_tx, new_app_rx) = mpsc::channel();
    watch_live_controls(mic_gain, app_gain, live_gate, new_app_tx);
    let mut app_worker_warning_sent = false;
    let mut mixed_monitor_warning_sent = false;
    while !worker.is_finished()
        && !STOP.load(Ordering::Relaxed)
        && !deadline.is_some_and(|cutoff| Instant::now() >= cutoff)
    {
        // Add newly started opted-in app streams without restarting the
        // physical microphone, the virtual sink or existing app taps.
        for stream_id in new_app_rx.try_iter() {
            match session.add_application_stream(stream_id) {
                Ok(()) => eprintln!("ITERVC_APP_LINKED:{stream_id}"),
                Err(error) => eprintln!("ITERVC_APP_FAILED:{stream_id}:{error}"),
            }
        }
        // Keep the physical mic alive even if one application closes.
        // Report partial failures immediately instead of showing "routing"
        // while the app audio / final meter worker has already stopped.
        if !app_worker_warning_sent && app_worker.as_ref().is_some_and(|job| job.is_finished()) {
            eprintln!("IterVC application capture stream stopped; microphone routing continues.");
            app_worker_warning_sent = true;
        }
        if !mixed_monitor_warning_sent && monitor_worker.is_finished() {
            eprintln!("IterVC mixed output monitor stopped; level meter and speaker monitoring are unavailable.");
            mixed_monitor_warning_sent = true;
        }
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
        Ok(Mode::Run { input, seconds, monitor_output, app_streams, include_apps, app_volume,
            mic_volume, gate_enabled, gate_threshold_db, gate_attack_ms, gate_release_ms }) =>
            run(&input, seconds, monitor_output.as_deref(), &app_streams, include_apps, app_volume,
                mic_volume, gate_enabled, gate_threshold_db, gate_attack_ms, gate_release_ms),
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
    fn microphone_meter_ignores_non_finite_pcm_and_reports_raw_power() {
        let bytes: Vec<u8> = [0.25f32, -0.5, f32::NAN, f32::INFINITY]
            .iter().flat_map(|sample| sample.to_le_bytes()).collect();
        let (mut power, mut count) = (0.0, 0);
        accumulate_pcm_power(&bytes, &mut power, &mut count);
        assert_eq!(count, 2);
        assert!((power - 0.3125).abs() < 0.00001);
        let (mut silence_power, mut silence_count) = (0.0, 0);
        accumulate_pcm_power(&[0u8; 16], &mut silence_power, &mut silence_count);
        assert_eq!(silence_power, 0.0);
        assert_eq!(silence_count, 4);
    }

    #[test]
    fn live_gate_ipc_validates_settings_before_publication() {
        assert_eq!(parse_gate_command("gate-enabled 1"), Some((GateTarget::Enabled, 1.0)));
        assert_eq!(parse_gate_command("gate-enabled 0"), Some((GateTarget::Enabled, 0.0)));
        assert_eq!(parse_gate_command("noise-suppression 1"), Some((GateTarget::NoiseSuppression, 1.0)));
        assert_eq!(parse_gate_command("noise-suppression 0"), Some((GateTarget::NoiseSuppression, 0.0)));
        assert_eq!(parse_gate_command("gate-threshold -31"), Some((GateTarget::Threshold, -31.0)));
        assert_eq!(parse_gate_command("gate-attack 12"), Some((GateTarget::Attack, 12.0)));
        assert_eq!(parse_gate_command("gate-release 500"), Some((GateTarget::Release, 500.0)));
        for invalid in ["gate-enabled 2", "gate-enabled -1",
            "gate-threshold nan", "gate-threshold -101", "gate-threshold 1",
            "gate-attack 0", "gate-attack inf", "gate-attack 1001",
            "gate-release 0", "gate-release 2001", "noise-suppression 2", "gate-enabled 1 0",
            "noise-gate 1", "gate-enabled", "gate-threshold NaN"] {
            assert_eq!(parse_gate_command(invalid), None, "{invalid}");
        }
        let settings = LiveGateSettings::new(MicrophoneConfig::default());
        settings.update(GateTarget::Enabled, 1.0);
        settings.update(GateTarget::Threshold, -34.0);
        assert!(settings.enabled.load(Ordering::Relaxed));
        assert_eq!(f32::from_bits(settings.threshold_db.load(Ordering::Relaxed)), -34.0);
    }

    #[test]
    fn app_hotplug_command_is_strict_and_cannot_modify_other_controls() {
        assert_eq!(parse_add_app_command("add-app 0"), Some(0));
        assert_eq!(parse_add_app_command("add-app 42"), Some(42));
        assert_eq!(parse_add_app_command("add-app 400000"), Some(400000));
        for bad in ["add-app", "add-app -1", "add-app NaN",
            "add-app 23 24", "mic-volume 1", "add-app 9999999999999999"] {
            assert_eq!(parse_add_app_command(bad), None, "{bad}");
        }
    }

    #[test]
    fn live_controls_reject_bad_values_without_touching_audio() {
        assert_eq!(parse_live_command("mic-volume 0.5"), Some((GainTarget::Microphone, 0.5)));
        assert_eq!(parse_live_command("app-volume 0"), Some((GainTarget::Applications, 0.0)));
        for invalid in ["mic-volume -1", "mic-volume 3.01", "app-volume 2.01",
            "app-volume NaN", "app-volume infinity", "unknown 1", "mic-volume", "mic-volume 1 2"] {
            assert_eq!(parse_live_command(invalid), None, "{invalid}");
        }
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
                app_streams: vec![], include_apps: false,
                app_volume: 1.0,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0, gate_attack_ms: 10.0, gate_release_ms: 150.0
            }
        );
        assert!(parse(&["--input", "mic", "--seconds", "0"]).is_ok());
        assert!(parse(&["--input", "mic", "--seconds", "3601"]).is_err());
    }

    #[test]
    fn app_capture_is_opt_in_and_gain_is_validated() {
        assert_eq!(parse(&["--input", "mic", "--app-stream", "42", "--app-volume", "0.75"]).unwrap(),
            Mode::Run { input: "mic".into(), seconds: 30, monitor_output: None,
                app_streams: vec![42], include_apps: true, app_volume: 0.75,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0, gate_attack_ms: 10.0, gate_release_ms: 150.0 });
        assert!(parse(&["--input", "mic", "--app-stream", "unknown"]).is_err());
        assert!(parse(&["--input", "mic", "--app-volume", "0.2"]).is_err());
        assert!(parse(&["--input", "mic", "--app-stream", "2", "--app-volume", "nan"]).is_err());
        assert_eq!(parse(&["--list-apps"]).unwrap(), Mode::ListApps);
        assert_eq!(parse(&["--input", "mic", "--app-stream", "14", "--app-stream", "7", "--app-stream", "14"]).unwrap(),
            Mode::Run { input: "mic".into(), seconds: 30, monitor_output: None,
                app_streams: vec![14, 7], include_apps: true, app_volume: 1.0,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0, gate_attack_ms: 10.0, gate_release_ms: 150.0 });
    }

    #[test]
    fn application_capture_can_wait_for_selected_programs() {
        assert_eq!(parse(&["--input", "mic", "--include-apps", "--app-volume", "0.5"]).unwrap(),
            Mode::Run { input: "mic".into(), seconds: 30, monitor_output: None,
                app_streams: vec![], include_apps: true, app_volume: 0.5,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0, gate_attack_ms: 10.0, gate_release_ms: 150.0 });
        assert!(parse(&["--list-apps", "--include-apps"]).is_err());
        assert!(parse(&["--input", "mic", "--app-stream", "0"]).is_ok());
    }

    #[test]
    fn microphone_dsp_flags_are_opt_in() {
        assert_eq!(parse(&["--input", "mic", "--mic-volume", "1.25",
            "--noise-gate", "--gate-threshold", "-38"]).unwrap(),
            Mode::Run { input: "mic".into(), seconds: 30, monitor_output: None,
                app_streams: vec![], include_apps: false, app_volume: 1.0,
                mic_volume: 1.25, gate_enabled: true, gate_threshold_db: -38.0, gate_attack_ms: 10.0, gate_release_ms: 150.0 });
        assert!(parse(&["--input", "mic", "--mic-volume", "5"]).is_err());
        assert!(parse(&["--input", "mic", "--gate-threshold", "nan"]).is_err());
        assert_eq!(parse(&["--input", "mic", "--noise-gate", "--gate-attack", "25",
            "--gate-release", "450"]).unwrap(),
            Mode::Run { input: "mic".into(), seconds: 30, monitor_output: None,
                app_streams: vec![], include_apps: false, app_volume: 1.0,
                mic_volume: 1.0, gate_enabled: true, gate_threshold_db: -45.0,
                gate_attack_ms: 25.0, gate_release_ms: 450.0 });
        for bad in [
            ["--gate-attack", "0"], ["--gate-attack", "NaN"],
            ["--gate-attack", "1001"], ["--gate-release", "0"],
            ["--gate-release", "inf"], ["--gate-release", "2001"],
        ] {
            assert!(parse(&["--input", "mic", bad[0], bad[1]]).is_err());
        }
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
                app_streams: vec![], include_apps: false, app_volume: 1.0,
                mic_volume: 1.0, gate_enabled: false, gate_threshold_db: -45.0, gate_attack_ms: 10.0, gate_release_ms: 150.0 });
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
