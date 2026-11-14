# Rust buffers and experimental Linux PipeWire audio

This directory is a **staged migration**, not a Windows audio engine rewrite.

## Why a hybrid architecture?

IterVC's existing WASAPI/NAudio path, application loopback, noise gate, gain,
meters and VB-Cable monitoring remain unchanged on Windows. Rewriting the
working audio mixer before comparing latency, throughput and output parity
would risk regressions. The Rust component is optional until those measurements
justify adopting it in the existing .NET runtime.

- `audio-buffers`: platform-independent bounded PCM ring, with an opaque C
  ABI for a **future** managed wrapper. The C ABI requires externally serialized
  access. One frame is `channels * bytes_per_sample`; only full frames are
  stored. On overflow the **oldest** frames are discarded.
- `linux-pipewire`: Linux backend with Rust PCM buffers, optional
  microphone gate/gain and multiple explicitly selected application taps.
  The final virtual microphone monitor and meter see the post-mix signal.

## Build and test (Linux)

```sh
cargo test --locked --manifest-path rust/Cargo.toml --workspace --all-targets
cargo build --locked --manifest-path rust/Cargo.toml -p itervc-linux-pipewire --release
./rust/target/release/itervc-linux-pipewire --help
```

## Linux desktop preview (portable, single executable)

The Linux GitHub Actions workflow publishes **IterVC-Linux-Desktop-portable-x64**,
a self-contained Avalonia GUI containing the compiled Rust audio backend as
an embedded resource. You do not need the .NET SDK or Rust to launch it.

Requirements on Linux: a PipeWire session, `pw-cat`, `pw-cli`, `pw-link`,
`pipewire-pulse`, `pactl` and running **WirePlumber**. `playerctl` is optional for OSC
music chatbox. VB-Cable is Windows-specific and **is not required**.
For a native Linux distribution, prefer **IterVC-Linux-Desktop-portable-tarball-x64**
from GitHub Actions → Artifacts. It contains a `.tar.gz` archive that preserves
Linux executable permissions (the GitHub Actions download itself is wrapped in
a ZIP). Extract the tarball and launch `IterVC.Linux` without installing .NET
or Rust. The runner verifies extraction, executable permissions and `--self-test`.
Alternatively, download **IterVC-Linux-Desktop-portable-x64**, manually mark
the extracted executable as executable (`chmod +x IterVC.Linux`) and run it.

The window offers microphone gain, boost and optional noise gate, plus
multiple explicitly selected active applications and independent app-mix volume.
The **same final mixed output** feeds the virtual microphone, optional
speaker monitoring and the level meter. Monitoring is disabled by default.

PipeWire application capture uses native `pw-link` fan-out, not
`pactl move-sink-input`. For each selected playback stream, IterVC maps the
unique PipeWire `object.id` and stereo port IDs to a temporary capture sink.
A volume-adjusted copy is mixed into the virtual microphone. **The original
physical speaker connections stay in place** throughout the session; IterVC
does not move or restore application destinations. The additional links
are removed when routing stops. After SIGKILL or power loss, the temporary
sink may require manual removal using `pactl list short modules` and
`pactl unload-module ID`.

Optional VRChat OSC chatbox music status uses MPRIS metadata via `playerctl`
and UDP port 9000 on localhost.

**Shortcuts:** Ctrl+Shift+R toggles routing and Ctrl+Shift+M toggles
monitoring when the window has focus. For **system-wide shortcuts on both
X11 and Wayland**, configure two custom keyboard commands in your desktop's
shortcut preferences, pointing to the downloaded executable:

```sh
/path/to/IterVC.Linux --toggle-routing
/path/to/IterVC.Linux --toggle-monitor
```

When monitoring is toggled during an active session, the app briefly restarts
the microphone stream with the new monitoring option.

**Live volume and mute.** While routing, microphone
volume, boost, application volume and the mute/unmute hotkeys send validated
gain commands to the running Rust process over its stdin. They do **not**
restart the PipeWire stream, disconnect selected applications or recreate the
virtual microphone. For interactive CLI debugging, type `mic-volume 0.8`,
`mic-volume 0`, `app-volume 0.5` or `app-volume 0` while running with
`--seconds 0`. Invalid and non-finite values are ignored. The gate's
activation and threshold still require restarting the session.


These send short commands to a user-local Unix socket owned by the running GUI,
so shortcuts work without intercepting arbitrary keyboard input or requiring
root permissions. They must be registered in the desktop environment once,
not through the Wayland portal. An automatic consent-based portal integration
and customizable key combinations within IterVC remain future work.
Previously selected applications can now rejoin the capture automatically
if they restart and expose a new PipeWire playback stream. IterVC prepares the
capture sink even if those applications are not running when routing starts;
the GUI polls at intervals and only marks a new stream connected when Rust
confirms its port links. Transient attachment failures are retried.

The Linux preview is not yet fully feature-equivalent to Windows: RNNoise
(separate PR #32), automatic native Wayland desktop-portal shortcut registration,
complete physical-device hotplug recovery, and listening tests on real hardware
are still pending.

The desktop binary can be smoke-tested without a graphical session using
`IterVC.Linux --self-test`; this checks that the embedded Rust backend
extracts and executes. GUI and real PipeWire audio require a Linux desktop.

## Manual PipeWire demonstration (Linux desktop)

Requires a **running per-user PipeWire session**, `pipewire-pulse`, `pw-cat`
and `pactl` (from `pulseaudio-utils` on Debian/Ubuntu). These tools are
runtime dependencies only for the prototype, not for Windows.

```sh
./rust/target/release/itervc-linux-pipewire --check
./rust/target/release/itervc-linux-pipewire --list-inputs
./rust/target/release/itervc-linux-pipewire \
  --input alsa_input.your_actual_microphone_node --seconds 30

# Optional: capture one or more active applications alongside the microphone.
./rust/target/release/itervc-linux-pipewire --list-apps
./rust/target/release/itervc-linux-pipewire \
  --input alsa_input.your_actual_microphone_node \
  --app-stream APP_ID_1 --app-stream APP_ID_2 --app-volume 0.8 \
  --mic-volume 1 --noise-gate --gate-threshold -45 --seconds 30

# Or prepare application capture before a selected program starts.
./rust/target/release/itervc-linux-pipewire \
  --input alsa_input.your_actual_microphone_node \
  --include-apps --app-volume 0.8 --seconds 0
# While it runs, submit 'add-app APP_ID' on stdin to attach a new stream.
```

The microphone source must appear under `--list-inputs`; monitor and
IterVC virtual sources are filtered out to avoid feedback. Capture and
playback explicitly use PipeWire `--raw` float32 mode to avoid sending
an audio file header through the PCM buffer. Shutdown and session timeout
are supervised even when capture blocks waiting for microphone input.

The command creates `itervc_rust_sink` (a null sink) plus a remapped source
named `itervc_rust_microphone`. While running, select **IterVC Rust
Microphone** as your microphone in another application. Samples go through
the Rust PCM buffer and are written to that virtual source. The two
`pactl` modules are automatically unloaded on normal exit or a caught
SIGINT/SIGTERM. If the process is forcibly killed, remove any leftover
modules with `pactl list short modules` and `pactl unload-module ID`.

**Important:** specify a *physical* input, not the virtual source, to avoid
feedback loops. Selected playback streams are identified by unique PipeWire
node and port IDs, **not** by display name alone or process ID. Only stereo
streams with identifiable FL/FR ports are tapped; unsupported layouts fail
without changing the original audio connections. There is no guarantee about
sound quality or latency on unverified hardware.


## System tray and media controls (Linux)

IterVC's system tray icon uses the existing application asset and is only
displayed where the desktop supports AppIndicator/StatusNotifierItem. Use
**Minimize to system tray** to opt into hiding the window on minimize; this
is disabled by default. Recover the window through the tray menu or the
command `/path/to/IterVC.Linux --show-window`. Closing via the tray menu
stops the audio backend and removes additional PipeWire application taps.

The Linux preview includes MPRIS play/pause, previous and next controls
using optional `playerctl`. Desktop X11/Wayland hotkey commands are
`--media-play-pause`, `--media-previous`, and `--media-next`, alongside
`--start-routing`, `--stop-routing`, `--toggle-microphone`,
`--toggle-applications-mute` and `--exit` (gracefully close the GUI).

Multiple application streams can be selected simultaneously, including
streams using different physical outputs. Each stream keeps its own original
speaker connection.
The CI captures nonzero application PCM from the actual virtual microphone,
then verifies both application and microphone mute/unmute produce sound,
silence and restored sound while the same routing process remains active.
Additional tests prepare an empty application capture sink, dynamically attach
a late-starting app and verify both applications keep their original outputs.
The Rust backend adds **only** the selected application port links and removes
those links on shutdown.

## Future integration gates

1. Run identical PCM streams through Rust and C# buffers; compare content,
   occupancy, overflows, underflows, CPU and tail latency under load.
2. Introduce a platform-neutral capture/routing layer in .NET; implement
   Windows with existing WASAPI and Linux with PipeWire nodes.
3. Integrate RNNoise on Linux once the separate PR #32 passes listening tests.
4. Extend the current selected-application auto-reconnection into robust
   device hotplug handling and consent-based portal shortcuts.
5. Manually test real Linux sessions (X11/Wayland + WirePlumber) and Windows
   10/11 hardware, in addition to CI. No deployment before those checks.

This proof of concept tracks issue #36 and does not incorporate unfinished
RNNoise PR #32.

## CI with a real headless PipeWire server

Alongside Rust unit tests, Linux CI runs actual `pipewire`,
`pipewire-pulse` and `wireplumber` with a synthetic microphone and a
multiple real PulseAudio playback streams. It checks that the selected app
ports are linked to the temporary capture sink while **their original
speaker destinations remain unchanged**, and verifies cleanup on exit.

This still does **not** verify a real physical microphone, real speaker audio,
or the graphical UI under a Wayland/X11 desktop.
