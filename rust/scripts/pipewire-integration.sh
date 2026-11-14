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
# WirePlumber is required to connect PulseAudio playback streams to sinks.
wireplumber >"$test_dir/wireplumber.log" 2>&1 &
session_manager_pid="$!"
app_pid=""
pulse_app_pid=""
pulse_app_2_pid=""
test_sink_module=""
test_source_module=""

cleanup() {
  if [ -n "$app_pid" ]; then
    kill -TERM "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
  if [ -n "$pulse_app_2_pid" ]; then
    kill -TERM "$pulse_app_2_pid" 2>/dev/null || true
    wait "$pulse_app_2_pid" 2>/dev/null || true
  fi
  if [ -n "$pulse_app_pid" ]; then
    kill -TERM "$pulse_app_pid" 2>/dev/null || true
    wait "$pulse_app_pid" 2>/dev/null || true
  fi
  if [ -n "$test_source_module" ]; then
    pactl unload-module "$test_source_module" >/dev/null 2>&1 || true
  fi
  if [ -n "$test_sink_module" ]; then
    pactl unload-module "$test_sink_module" >/dev/null 2>&1 || true
  fi
  kill "$session_manager_pid" "$pulse_pid" "$pipewire_pid" 2>/dev/null || true
  wait "$session_manager_pid" "$pulse_pid" "$pipewire_pid" 2>/dev/null || true
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

# Exercise a genuine PulseAudio playback stream through pipewire-pulse.
# IterVC must capture the specifically selected sink-input, publish it to
# the virtual microphone, keep speaker playback alive and restore the
# original application output once it exits.
if ! command -v pacat >/dev/null 2>&1; then
  echo "pacat is required for the app routing integration test" >&2
  exit 1
fi
original_sink_id="$(pactl list short sinks | awk '$2=="itervc_ci_audio_sink" {print $1}')"
if [ -z "$original_sink_id" ]; then
  echo "Missing original application speaker sink" >&2
  exit 1
fi

pacat --playback --raw --format=float32le --rate=48000 --channels=2 \
  --device=itervc_ci_audio_sink </dev/zero >"$test_dir/pacat.log" 2>&1 &
pulse_app_pid="$!"

stream_id=""
for _ in $(seq 1 50); do
  stream_id="$(pactl list short sink-inputs | awk -v sink="$original_sink_id" '$2==sink {print $1; exit}')"
  if [ -n "$stream_id" ]; then break; fi
  if ! kill -0 "$pulse_app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ -z "$stream_id" ]; then
  cat "$test_dir/pacat.log" >&2
  cat "$test_dir/wireplumber.log" >&2
  echo "-- PipeWire sinks --" >&2
  pactl list short sinks >&2 || true
  echo "-- PulseAudio playback streams --" >&2
  pactl list short sink-inputs >&2 || true
  echo "-- PulseAudio clients --" >&2
  pactl list short clients >&2 || true
  echo "-- pacat process --" >&2
  ps -p "$pulse_app_pid" -o pid,stat,cmd >&2 || true
  echo "Could not find synthetic PulseAudio application playback stream" >&2
  exit 1
fi

"$app" --list-apps | grep -q "\"index\""
"$app" --input itervc_ci_test_microphone --app-stream "$stream_id" --app-volume 0.75 \
  --seconds 0 >"$test_dir/app-session.log" 2>&1 &
app_pid="$!"

moved=0
for _ in $(seq 1 90); do
  capture_sink="$(pactl list short sinks | awk '$2=="itervc_rust_apps_sink" {print $1}')"
  current_sink="$(pactl list short sink-inputs | awk -v id="$stream_id" '$1==id {print $2}')"
  if [ -n "$capture_sink" ] && [ "$current_sink" = "$capture_sink" ]; then
    moved=1
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ "$moved" != 1 ]; then
  cat "$test_dir/app-session.log" >&2
  echo "Selected app was not captured by the isolated PipeWire sink" >&2
  exit 1
fi

kill -TERM "$app_pid"
wait "$app_pid"
app_pid=""

restored_sink="$(pactl list short sink-inputs | awk -v id="$stream_id" '$1==id {print $2}')"
if [ "$restored_sink" != "$original_sink_id" ]; then
  cat "$test_dir/app-session.log" >&2
  echo "Selected application was not returned to its original output" >&2
  exit 1
fi
if pactl list short sinks | grep -q "itervc_rust_apps_sink"; then
  echo "Temporary application capture sink was not removed" >&2
  exit 1
fi
cat "$test_dir/app-session.log"
# Verify two independent application sink-inputs can be selected together
# without duplicating playback through their shared original speaker.
pacat --playback --raw --format=float32le --rate=48000 --channels=2 \
  --device=itervc_ci_audio_sink </dev/zero >"$test_dir/pacat2.log" 2>&1 &
pulse_app_2_pid="$!"
stream_2_id=""
for _ in $(seq 1 50); do
  stream_2_id="$(pactl list short sink-inputs | awk -v sink="$original_sink_id" -v first="$stream_id" '$2==sink && $1!=first {print $1; exit}')"
  if [ -n "$stream_2_id" ]; then break; fi
  if ! kill -0 "$pulse_app_2_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ -z "$stream_2_id" ]; then
  cat "$test_dir/pacat2.log" >&2
  pactl list short sink-inputs >&2
  echo "Second application stream was not created" >&2
  exit 1
fi

"$app" --input itervc_ci_test_microphone \
  --app-stream "$stream_id" --app-stream "$stream_2_id" \
  --app-volume 0.8 --seconds 0 >"$test_dir/multi-app.log" 2>&1 &
app_pid="$!"

both_moved=0
for _ in $(seq 1 90); do
  capture_sink="$(pactl list short sinks | awk '$2=="itervc_rust_apps_sink" {print $1}')"
  current_1="$(pactl list short sink-inputs | awk -v id="$stream_id" '$1==id {print $2}')"
  current_2="$(pactl list short sink-inputs | awk -v id="$stream_2_id" '$1==id {print $2}')"
  if [ -n "$capture_sink" ] && [ "$current_1" = "$capture_sink" ] && [ "$current_2" = "$capture_sink" ]; then
    both_moved=1
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ "$both_moved" != 1 ]; then
  cat "$test_dir/multi-app.log" >&2
  pactl list short sink-inputs >&2
  echo "Not all selected application streams were captured" >&2
  exit 1
fi
kill -TERM "$app_pid"
wait "$app_pid"
app_pid=""
for id in "$stream_id" "$stream_2_id"; do
  final_sink="$(pactl list short sink-inputs | awk -v stream="$id" '$1==stream {print $2}')"
  if [ "$final_sink" != "$original_sink_id" ]; then
    cat "$test_dir/multi-app.log" >&2
    echo "Application $id was not restored to its original speaker" >&2
    exit 1
  fi
done
cat "$test_dir/multi-app.log"
echo "Real PipeWire virtual mic and multi-app capture/restore tests passed."

