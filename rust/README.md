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
- `linux-pipewire`: Linux backend with a Rust PCM buffer, optional
  microphone gate/gain and single-application capture. The final virtual
  microphone monitor and meter see the post-mix signal.

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

Requirements on Linux: a PipeWire session, `pw-cat`, `pipewire-pulse`,
`pactl` and running **WirePlumber**. `playerctl` is optional for OSC
music chatbox. VB-Cable is Windows-specific and **is not required**.
Mark the downloaded `IterVC.Linux` binary executable and run it.

The window offers microphone gain, boost and optional noise gate, plus
one explicitly selected active application's audio and independent app volume.
The **same final mixed output** feeds the virtual microphone, optional
speaker monitoring and the level meter. Monitoring is disabled by default.

Selected application audio is temporarily moved to a dedicated capture sink.
A copy is replayed to the application's original speakers, and a separate
volume-adjusted copy is mixed into the virtual microphone. The original route
is restored on normal exit, unless the user changed it in the meantime.
After SIGKILL or power loss you may need to restore the output manually.

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

These send short commands to a user-local Unix socket owned by the running GUI,
so shortcuts work without intercepting arbitrary keyboard input or requiring
root permissions. They must be registered in the desktop environment once,
not through the Wayland portal. An automatic consent-based portal integration
and customizable key combinations within IterVC remain future work.
The preview is not yet fully feature-equivalent to Windows: multi-app
dynamic stream discovery, RNNoise (separate PR #32), system tray,
Wayland desktop-portal shortcut registration and complete media controls
still need further development.

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

# Optional: capture a selected application alongside the microphone.
./rust/target/release/itervc-linux-pipewire --list-apps
./rust/target/release/itervc-linux-pipewire \
  --input alsa_input.your_actual_microphone_node \
  --app-stream YOUR_ACTIVE_STREAM_ID --app-volume 0.8 \
  --mic-volume 1 --noise-gate --gate-threshold -45 --seconds 30
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
feedback loops. Application capture currently supports one explicitly
selected active sink-input and relies on WirePlumber and pipewire-pulse.
There is no sound-quality or latency guarantee on unverified hardware.


## System tray and media controls (Linux)

IterVC's system tray icon uses the existing application asset and is only
displayed where the desktop supports AppIndicator/StatusNotifierItem. Use
**Minimize to system tray** to opt into hiding the window on minimize; this
is disabled by default. Recover the window through the tray menu or the
command `/path/to/IterVC.Linux --show-window`. Closing via the tray menu
stops the audio backend and restores selected application streams.

The Linux preview includes MPRIS play/pause, previous and next controls
using optional `playerctl`. Desktop X11/Wayland hotkey commands are
`--media-play-pause`, `--media-previous`, and `--media-next`, alongside
`--start-routing`, `--stop-routing`, `--toggle-microphone` and
`--toggle-applications-mute`.

Multiple application streams can be selected simultaneously when they
share the **same physical speaker output**. This restriction avoids
replaying a mixed stream to several different speaker devices. Streams
are restored individually when the routing session stops. If a selection
contains apps using different outputs, IterVC refuses to move them.

## Future integration gates

1. Run identical PCM streams through Rust and C# buffers; compare content,
   occupancy, overflows, underflows, CPU and tail latency under load.
2. Introduce a platform-neutral capture/routing layer in .NET; implement
   Windows with existing WASAPI and Linux with PipeWire nodes.
3. Integrate RNNoise on Linux once the separate PR #32 passes listening tests.
4. Expand single-app routing into multiple dynamic PipeWire streams, device
   hotplug handling, portal shortcuts and complete desktop/OSC parity.
5. Manually test real Linux sessions (X11/Wayland + WirePlumber) and Windows
   10/11 hardware, in addition to CI. No deployment before those checks.

This proof of concept tracks issue #36 and does not incorporate unfinished
RNNoise PR #32.

## CI with a real headless PipeWire server

Alongside Rust unit tests, Linux CI runs actual `pipewire`,
`pipewire-pulse` and `wireplumber` with a synthetic microphone and a
real PulseAudio playback stream. It verifies app redirection to the capture
sink, restoration to the original speakers and cleanup of temporary devices.

This still does **not** verify a real physical microphone, real speaker audio,
or the graphical UI under a Wayland/X11 desktop.
