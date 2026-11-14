#!/usr/bin/env bash
# Headless real-PipeWire integration; synthetic source, no physical hardware.
set -euo pipefail

app="./rust/target/release/itervc-linux-pipewire"
if [ "$#" -gt 0 ]; then app="$1"; fi
test_dir="$(mktemp -d)"
export XDG_RUNTIME_DIR="$test_dir/runtime"
export PIPEWIRE_RUNTIME_DIR="$XDG_RUNTIME_DIR"
export PULSE_SERVER="unix:$XDG_RUNTIME_DIR/pulse/native"
mkdir -p "$XDG_RUNTIME_DIR"
chmod 700 "$XDG_RUNTIME_DIR"

pipewire >"$test_dir/pipewire.log" 2>&1 &
pipewire_pid="$!"
pipewire-pulse >"$test_dir/pipewire-pulse.log" 2>&1 &
pulse_pid="$!"
app_pid=""
test_sink_module=""
test_source_module=""

cleanup() {
  if [ -n "$app_pid" ]; then
    kill -TERM "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  if [ -n "$test_source_module" ]; then
    pactl unload-module "$test_source_module" >/dev/null 2>&1 || true
  fi
  if [ -n "$test_sink_module" ]; then
    pactl unload-module "$test_sink_module" >/dev/null 2>&1 || true
  fi
  kill "$pulse_pid" "$pipewire_pid" 2>/dev/null || true
  wait "$pulse_pid" "$pipewire_pid" 2>/dev/null || true
  rm -rf "$test_dir"
}
trap cleanup EXIT

ready=0
for _ in $(seq 1 70); do
  if pactl info >/dev/null 2>&1; then ready=1; break; fi
  sleep 0.1
done
if [ "$ready" != 1 ]; then
  cat "$test_dir/pipewire.log" "$test_dir/pipewire-pulse.log" >&2
  echo "PipeWire test server could not start" >&2
  exit 1
fi

test_sink_module="$(pactl load-module module-null-sink sink_name=itervc_ci_audio_sink)"
test_source_module="$(pactl load-module module-remap-source master=itervc_ci_audio_sink.monitor source_name=itervc_ci_test_microphone)"

"$app" --check
"$app" --list-inputs | grep -Fx "itervc_ci_test_microphone"

"$app" --input itervc_ci_test_microphone --seconds 2 >"$test_dir/session.log" 2>&1 &
app_pid="$!"

virtual_ready=0
for _ in $(seq 1 40); do
  if pactl list short sources | grep -q "itervc_rust_microphone"; then
    virtual_ready=1
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ "$virtual_ready" != 1 ]; then
  cat "$test_dir/session.log" >&2
  echo "IterVC did not expose a PipeWire virtual microphone" >&2
  exit 1
fi

wait "$app_pid"
app_pid=""

if pactl list short sources | grep -q "itervc_rust_microphone"; then
  echo "IterVC failed to unload virtual microphone" >&2
  exit 1
fi
if pactl list short sinks | grep -q "itervc_rust_sink"; then
  echo "IterVC failed to unload virtual sink" >&2
  exit 1
fi

cat "$test_dir/session.log"
echo "Real headless PipeWire virtual microphone lifecycle test passed."
