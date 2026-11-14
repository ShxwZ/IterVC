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

# The session manager needs both PipeWire and a D-Bus session already active.
# Starting them simultaneously is racy on hosted CI VMs.
pipewire_ready=0
for _ in $(seq 1 90); do
  if pw-cli info 0 >/dev/null 2>&1; then pipewire_ready=1; break; fi
  sleep 0.1
done
if [ "$pipewire_ready" != 1 ]; then
  cat "$test_dir/pipewire.log" >&2
  echo "PipeWire core did not initialize" >&2
  exit 1
fi

wireplumber >"$test_dir/wireplumber.log" 2>&1 &
session_manager_pid="$!"
pipewire-pulse >"$test_dir/pipewire-pulse.log" 2>&1 &
pulse_pid="$!"
app_pid=""
pulse_app_pid=""
pulse_app_2_pid=""
test_sink_module=""
test_microphone_sink_module=""
test_source_module=""
output_capture_pid=""

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
  if [ -n "$output_capture_pid" ]; then
    kill -TERM "$output_capture_pid" 2>/dev/null || true
    wait "$output_capture_pid" 2>/dev/null || true
  fi
  if [ -n "$test_source_module" ]; then
    pactl unload-module "$test_source_module" >/dev/null 2>&1 || true
  fi
  if [ -n "$test_microphone_sink_module" ]; then
    pactl unload-module "$test_microphone_sink_module" >/dev/null 2>&1 || true
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

# Keep the synthetic microphone separate from application playback so we can
# prove that app audio reaches the *mixed* virtual microphone, not the mic input.
test_sink_module="$(pactl load-module module-null-sink sink_name=itervc_ci_audio_sink)"
test_microphone_sink_module="$(pactl load-module module-null-sink sink_name=itervc_ci_microphone_source_sink)"
test_source_module="$(pactl load-module module-remap-source master=itervc_ci_microphone_source_sink.monitor source_name=itervc_ci_test_microphone)"

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
# IterVC taps selected sink-input output ports without moving the
# original physical speaker connections.
if ! command -v pacat >/dev/null 2>&1; then
  echo "pacat is required for the app routing integration test" >&2
  exit 1
fi
pactl set-default-sink itervc_ci_audio_sink
original_sink_id="$(pactl list short sinks | awk '$2=="itervc_ci_audio_sink" {print $1}')"
if [ -z "$original_sink_id" ]; then
  echo "Missing original application speaker sink" >&2
  exit 1
fi

# Play a deterministic non-zero stereo PCM signal to the original speaker sink.
# The separate synthetic microphone source produces silence.
python3 - "$test_dir/app-constant.pcm" <<'PY'
import struct
import sys
with open(sys.argv[1], "wb") as stream:
    stream.write(struct.pack("<ff", 0.4, -0.4) * (48_000 * 45))
PY
pacat --playback --raw --format=float32le --rate=48000 --channels=2 \
  <"$test_dir/app-constant.pcm" >"$test_dir/pacat.log" 2>&1 &
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
# Keep a writable stdin channel for gain changes without restarting PipeWire.
mkfifo "$test_dir/live-gain.fifo"
exec 9<>"$test_dir/live-gain.fifo"
"$app" --input itervc_ci_test_microphone --app-stream "$stream_id" --app-volume 0.75 \
  --seconds 0 <"$test_dir/live-gain.fifo" >"$test_dir/app-session.log" 2>&1 &
app_pid="$!"

linked=0
for _ in $(seq 1 100); do
  current_sink="$(pactl list short sink-inputs | awk -v id="$stream_id" '$1==id {print $2}')"
  links="$(pw-link --links || true)"
  if [ "$current_sink" = "$original_sink_id" ] \
    && printf '%s\n' "$links" | grep -Fq "itervc_rust_apps_sink"; then
    linked=1
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ "$linked" != 1 ]; then
  cat "$test_dir/app-session.log" >&2
  echo "App output ports:" >&2
  pw-link --output >&2 || true
  echo "Capture input ports:" >&2
  pw-link --input >&2 || true
  echo "Current PipeWire links:" >&2
  pw-link --links >&2 || true
  echo "Selected app did not connect while preserving its original output" >&2
  exit 1
fi

# Record the *actual* virtual microphone while the selected app is running.
# This fails if the links exist but no audio makes it to the mixed output.
pw-cat --record --raw --format=f32 --rate=48000 --channels=2 \
  --target=itervc_rust_microphone - >"$test_dir/mixed-output.pcm" \
  2>"$test_dir/mixed-record.log" &
output_capture_pid="$!"
sleep 2
kill -TERM "$output_capture_pid" 2>/dev/null || true
wait "$output_capture_pid" 2>/dev/null || true
output_capture_pid=""
if [ ! -s "$test_dir/mixed-output.pcm" ]; then
  echo "Virtual microphone returned no bytes; PipeWire diagnostics follow" >&2
  cat "$test_dir/mixed-record.log" >&2 || true
  cat "$test_dir/app-session.log" >&2 || true
  echo "--- Sources ---" >&2
  pactl list short sources >&2 || true
  echo "--- Playback streams ---" >&2
  pactl list short sink-inputs >&2 || true
  echo "--- Native port links ---" >&2
  pw-link --links >&2 || true
  exit 1
fi
python3 - "$test_dir/mixed-output.pcm" <<'PY'
from array import array
import os
import sys

path = sys.argv[1]
if not os.path.isfile(path) or os.path.getsize(path) < 8192:
    raise SystemExit("PipeWire virtual microphone yielded no PCM frames")
with open(path, "rb") as file:
    samples = array("f")
    samples.frombytes(file.read())
# The physical microphone is silent in this test, therefore a non-zero
# signal must have originated from the selected application tap.
nonzero = sum(abs(sample) > 0.04 for sample in samples)
if nonzero < 1000:
    raise SystemExit("Selected application's nonzero audio was not mixed into the virtual microphone")
print(f"Application PCM audibility check: {nonzero} nonzero samples from virtual source.")
PY

# Mute the selected application in the virtual microphone *without*
# restarting the Rust process or touching its original speaker connection.
printf 'app-volume 0\n' >&9
sleep 0.5
pw-cat --record --raw --format=f32 --rate=48000 --channels=2 \
  --target=itervc_rust_microphone - >"$test_dir/muted-output.pcm" 2>"$test_dir/muted.log" &
output_capture_pid="$!"
sleep 1.1
kill -TERM "$output_capture_pid" 2>/dev/null || true
wait "$output_capture_pid" 2>/dev/null || true
output_capture_pid=""
python3 - "$test_dir/muted-output.pcm" <<'PY'
from array import array
import os
import sys
path = sys.argv[1]
if os.path.getsize(path) < 8192:
    raise SystemExit("Live app-mute test did not record enough virtual microphone PCM")
samples = array("f")
with open(path, "rb") as stream:
    samples.frombytes(stream.read())
if sum(abs(value) > 0.04 for value in samples) > 500:
    raise SystemExit("Application mute did not silence the mixed virtual microphone")
print("Live application mute: verified virtual microphone silence.")
PY
if ! kill -0 "$app_pid" 2>/dev/null; then
  echo "Live gain command unexpectedly terminated microphone routing" >&2
  exit 1
fi
printf 'app-volume 0.75\n' >&9
sleep 0.5
pw-cat --record --raw --format=f32 --rate=48000 --channels=2 \
  --target=itervc_rust_microphone - >"$test_dir/restored-output.pcm" 2>"$test_dir/restored.log" &
output_capture_pid="$!"
sleep 1.1
kill -TERM "$output_capture_pid" 2>/dev/null || true
wait "$output_capture_pid" 2>/dev/null || true
output_capture_pid=""
python3 - "$test_dir/restored-output.pcm" <<'PY'
from array import array
import os
import sys
path = sys.argv[1]
if os.path.getsize(path) < 8192:
    raise SystemExit("Live app-unmute test did not record enough PCM")
samples = array("f")
with open(path, "rb") as stream:
    samples.frombytes(stream.read())
if sum(abs(value) > 0.04 for value in samples) < 1000:
    raise SystemExit("Application audio did not resume after live unmute")
print("Live application unmute: nonzero audio restored without restarting PipeWire.")
PY

kill -TERM "$app_pid"
wait "$app_pid"
app_pid=""
exec 9>&-
exec 9<&-

restored_sink="$(pactl list short sink-inputs | awk -v id="$stream_id" '$1==id {print $2}')"
if [ "$restored_sink" != "$original_sink_id" ]; then
  cat "$test_dir/app-session.log" >&2
  echo "Selected application's original speaker connection was modified" >&2
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
  </dev/zero >"$test_dir/pacat2.log" 2>&1 &
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

both_linked=0
for _ in $(seq 1 100); do
  current_1="$(pactl list short sink-inputs | awk -v id="$stream_id" '$1==id {print $2}')"
  current_2="$(pactl list short sink-inputs | awk -v id="$stream_2_id" '$1==id {print $2}')"
  links="$(pw-link --links || true)"
  if [ "$current_1" = "$original_sink_id" ] && [ "$current_2" = "$original_sink_id" ] \
    && printf '%s\n' "$links" | grep -Fq "itervc_rust_apps_sink"; then
    both_linked=1
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ "$both_linked" != 1 ]; then
  cat "$test_dir/multi-app.log" >&2
  echo "Detailed PulseAudio sink-inputs:" >&2
  pactl list sink-inputs >&2 || true
  echo "PipeWire global output port IDs:" >&2
  pw-link --id --output >&2 || true
  echo "PipeWire global port properties:" >&2
  pw-cli ls Port >&2 || true
  echo "Available output ports:" >&2
  pw-link --output >&2 || true
  echo "Available input ports:" >&2
  pw-link --input >&2 || true
  echo "Current links:" >&2
  pw-link --links >&2 || true
  echo "Detailed PulseAudio sink-input properties:" >&2
  pactl list sink-inputs >&2 || true
  echo "PipeWire global output port IDs:" >&2
  pw-link --id --output >&2 || true
  echo "PipeWire port node associations:" >&2
  pw-cli ls Port >&2 || true
  echo "Selected apps did not connect to the capture sink" >&2
  exit 1
fi
kill -TERM "$app_pid"
wait "$app_pid"
app_pid=""
for id in "$stream_id" "$stream_2_id"; do
  final_sink="$(pactl list short sink-inputs | awk -v stream="$id" '$1==stream {print $2}')"
  if [ "$final_sink" != "$original_sink_id" ]; then
    cat "$test_dir/multi-app.log" >&2
    echo "Application $id speaker destination was modified" >&2
    exit 1
  fi
done
if pactl list short sinks | grep -q "itervc_rust_apps_sink"; then
  echo "Temporary application capture sink was not removed" >&2
  exit 1
fi
cat "$test_dir/multi-app.log"
echo "Real PipeWire virtual mic and non-invasive multi-app tap tests passed."

