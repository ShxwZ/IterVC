# Rust buffers and experimental Linux PipeWire audio

This directory is a **staged migration**, not a Windows audio engine rewrite.

## Why a hybrid architecture?

IterVC's existing WASAPI/NAudio path, application loopback, noise gate, gain,
meters and VB-Cable monitoring remain unchanged on Windows. Rewriting the
working audio mixer before comparing latency, throughput and output parity
would risk regressions. The Rust component is optional until those measurements
justify adopting it in the existing .NET runtime.

- \`audio-buffers\`: platform-independent bounded PCM ring, with an opaque C
  ABI for a **future** managed wrapper. The C ABI requires externally serialized
  access. One frame is \`channels * bytes_per_sample\`; only full frames are
  stored. On overflow the **oldest** frames are discarded.
- \`linux-pipewire\`: small **Linux command-line proof of concept**. It uses
  the Rust ring and PipeWire's tools to capture a selected microphone and
  publish a temporary virtual microphone. No sound processing/gating yet.

## Build and test (Linux)

\`\`\`sh
cargo test --locked --manifest-path rust/Cargo.toml --workspace --all-targets
cargo build --locked --manifest-path rust/Cargo.toml -p itervc-linux-pipewire --release
./rust/target/release/itervc-linux-pipewire --help
\`\`\`

## Manual PipeWire demonstration (Linux desktop)

Requires a **running per-user PipeWire session**, \`pipewire-pulse\`, \`pw-cat\`
and \`pactl\` (from \`pulseaudio-utils\` on Debian/Ubuntu). These tools are
runtime dependencies only for the prototype, not for Windows.

\`\`\`sh
./rust/target/release/itervc-linux-pipewire --check
./rust/target/release/itervc-linux-pipewire --list-inputs
./rust/target/release/itervc-linux-pipewire \
  --input alsa_input.your_actual_microphone_node --seconds 30
\`\`\`

The command creates \`itervc_rust_sink\` (a null sink) plus a remapped source
named \`itervc_rust_microphone\`. While running, select **IterVC Rust
Microphone** as your microphone in another application. Samples go through
the Rust PCM buffer and are written to that virtual source. The two
\`pactl\` modules are automatically unloaded on normal exit or a caught
SIGINT/SIGTERM. If the process is forcibly killed, remove any leftover
modules with \`pactl list short modules\` and \`pactl unload-module ID\`.

**Important:** specify a *physical* input, not the virtual source, to avoid
feedback loops. This experiment routes only the physical microphone. It
does not yet capture application streams, preserve the full IterVC mixed
output, apply the existing noise gate or RNNoise, monitor locally, or provide
an Avalonia UI on Linux. There is no sound-quality or latency guarantee.

## Future integration gates

1. Run identical PCM streams through Rust and C# buffers; compare content,
   occupancy, overflows, underflows, CPU and tail latency under load.
2. Introduce a platform-neutral capture/routing layer in .NET; implement
   Windows with existing WASAPI and Linux with PipeWire nodes.
3. Publish *one* post-processing microphone stream to both Linux virtual
   microphone and local monitoring, keeping the meters at the same point.
4. Support app sources per PipeWire node/stream (a Linux PID is not equivalent
   to Windows' process-loopback semantics), manage device changes, and
   integrate existing preferences/OSC/hotkeys/GUI.
5. Manually test real Linux sessions (X11/Wayland + WirePlumber) and Windows
   10/11 hardware, in addition to CI. No deployment before those checks.

This proof of concept tracks issue #36 and does not incorporate unfinished
RNNoise PR #32.
