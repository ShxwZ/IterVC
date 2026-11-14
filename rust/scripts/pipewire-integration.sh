#!/usr/bin/env bash
# Headless real-PipeWire integration; synthetic source, no physical hardware.
set -euo pipefail

app="./rust/target/release/itervc-linux-pipewire"
if [ "$#" -gt 0 ]; then app="$1"; fi
gui=""
if [ "$#" -gt 1 ]; then gui="$2"; fi
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
gui_pid=""
gui_autoplay_pid=""
pulse_app_pid=""
pulse_app_2_pid=""
test_sink_module=""
test_alt_sink_module=""
test_microphone_sink_module=""
test_source_module=""
output_capture_pid=""
mic_input_pid=""

cleanup() {
  if [ -n "$gui_autoplay_pid" ]; then
    kill -TERM "$gui_autoplay_pid" 2>/dev/null || true
    wait "$gui_autoplay_pid" 2>/dev/null || true
  fi
  if [ -n "$gui_pid" ]; then
    kill -TERM "$gui_pid" 2>/dev/null || true
    wait "$gui_pid" 2>/dev/null || true
  fi
  if [ -n "$mic_input_pid" ]; then
    kill -TERM "$mic_input_pid" 2>/dev/null || true
    wait "$mic_input_pid" 2>/dev/null || true
  fi
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
  if [ -n "$test_alt_sink_module" ]; then
    pactl unload-module "$test_alt_sink_module" >/dev/null 2>&1 || true
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
test_alt_sink_module="$(pactl load-module module-null-sink sink_name=itervc_ci_alt_audio_sink)"
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

# End-to-end GUI + Rust + PipeWire test: the actual Avalonia window must
# command the Rust worker through user-local IPC to create and remove a source.
if [ -n "$gui" ]; then
  # Preselect an app name that is deliberately not running yet, just as a
  # user may keep an application enabled between system reboots.
  export XDG_CONFIG_HOME="$test_dir/gui-settings"
  mkdir -p "$XDG_CONFIG_HOME/IterVC"
  python3 - "$XDG_CONFIG_HOME/IterVC/settings.json" <<'PY'
import json
import sys
with open(sys.argv[1], "w") as file:
    json.dump({
        "MicrophoneDeviceId": "itervc_ci_test_microphone",
        "IncludedProcessNames": ["PipeWireAutoTestPlayer"],
        "MicrophoneEnabled": True,
        "AppsVolume": 0.75
    }, file)
PY
  "$gui" >"$test_dir/gui-session.log" 2>&1 &
  gui_pid="$!"
  gui_window=""
  for _ in $(seq 1 90); do
    gui_window="$(xdotool search --onlyvisible --name 'IterVC.*Linux' 2>/dev/null | head -n 1 || true)"
    if [ -n "$gui_window" ]; then break; fi
    if ! kill -0 "$gui_pid" 2>/dev/null; then break; fi
    sleep 0.1
  done
  if [ -z "$gui_window" ]; then
    cat "$test_dir/gui-session.log" >&2
    echo "Avalonia GUI did not show a window under the PipeWire session" >&2
    exit 1
  fi
  # Allow the GUI's asynchronous device catalogue to load.
  sleep 1
  # A saved app can start while routing is idle and then exit just before
  # Start. The picker should see it, but Start must NOT pass its stale ID to
  # Rust (which would otherwise fail the whole microphone session).
  python3 - "$test_dir/gui-autoapp.pcm" <<'PY'
import struct
import sys
with open(sys.argv[1], "wb") as output:
    output.write(struct.pack("<ff", 0.35, -0.35) * (48_000 * 25))
PY
  gui_prestart_sink="$(pactl list short sinks | awk '$2=="itervc_ci_audio_sink" {print $1}')"
  pacat --playback --device=itervc_ci_audio_sink \
    --client-name=PipeWireAutoTestPlayer --stream-name=PipeWireAutoTestPlayer \
    --raw --format=float32le --rate=48000 --channels=2 \
    <"$test_dir/gui-autoapp.pcm" >"$test_dir/gui-before-start.log" 2>&1 &
  gui_autoplay_pid="$!"
  gui_stale_stream=""
  for _ in $(seq 1 60); do
    gui_stale_stream="$(pactl list short sink-inputs |
      awk -v sink="$gui_prestart_sink" '$2==sink {print $1; exit}')"
    if [ -n "$gui_stale_stream" ]; then break; fi
    sleep 0.1
  done
  if [ -z "$gui_stale_stream" ]; then
    cat "$test_dir/gui-before-start.log" >&2
    echo "Pre-start playback stream never appeared" >&2
    exit 1
  fi
  sleep 5
  kill -TERM "$gui_autoplay_pid" 2>/dev/null || true
  wait "$gui_autoplay_pid" 2>/dev/null || true
  gui_autoplay_pid=""
  # Wait until PulseAudio has removed the dead stream before starting.
  for _ in $(seq 1 50); do
    if ! pactl list short sink-inputs | awk -v id="$gui_stale_stream" '$1==id {found=1} END {exit !found}'; then
      break
    fi
    sleep 0.1
  done
  "$gui" --start-routing
  gui_source=0
  for _ in $(seq 1 80); do
    if pactl list short sources | grep -q "itervc_rust_microphone"; then
      gui_source=1
      break
    fi
    if ! kill -0 "$gui_pid" 2>/dev/null; then break; fi
    sleep 0.1
  done
  if [ "$gui_source" != 1 ]; then
    cat "$test_dir/gui-session.log" >&2
    echo "Linux GUI could not create a PipeWire virtual microphone through its shortcut API" >&2
    exit 1
  fi
  # The GUI started before this playback stream existed. Its 4-second
  # device poll must find the stored application name and add its new node via
  # the exact same Rust add-app IPC used in production.
  if ! pactl list short sinks | grep -q "itervc_rust_apps_sink"; then
    cat "$test_dir/gui-session.log" >&2
    echo "GUI did not prepare application capture for the saved, currently absent app" >&2
    exit 1
  fi
  pactl set-default-sink itervc_ci_audio_sink
  gui_original_sink="$(pactl list short sinks | awk '$2=="itervc_ci_audio_sink" {print $1}')"
  pacat --playback --device=itervc_ci_audio_sink \
    --client-name=PipeWireAutoTestPlayer --stream-name=PipeWireAutoTestPlayer \
    --raw --format=float32le --rate=48000 --channels=2 \
    <"$test_dir/gui-autoapp.pcm" >"$test_dir/gui-autoapp.log" 2>&1 &
  gui_autoplay_pid="$!"
  gui_playback_stream=""
  for _ in $(seq 1 80); do
    gui_playback_stream="$(pactl list short sink-inputs |
      awk -v sink="$gui_original_sink" '$2==sink {print $1; exit}')"
    if [ -n "$gui_playback_stream" ]; then break; fi
    sleep 0.1
  done
  if [ -z "$gui_playback_stream" ]; then
    cat "$test_dir/gui-autoapp.log" >&2
    echo "Delayed application playback never appeared on its original sink" >&2
    exit 1
  fi
  gui_audio_linked=0
  for _ in $(seq 1 120); do
    # This checks the physical stream and the extra tap co-exist, but an
    # audio recording below verifies the fan-out carries actual samples.
    if pw-link --links | grep -q "itervc_rust_apps_sink" &&
      pactl list short sink-inputs | awk -v id="$gui_playback_stream" -v sink="$gui_original_sink" '$1==id && $2==sink {found=1} END {exit !found}'; then
      # The first attempt may be during sink initialization. Give the GUI
      # polling loop enough time to send its Rust app-linked command.
      sleep 0.5
      gui_audio_linked=1
      break
    fi
    sleep 0.1
  done
  if [ "$gui_audio_linked" != 1 ]; then
    cat "$test_dir/gui-session.log" >&2
    pw-link --links >&2 || true
    echo "Delayed app was not linked to the GUI-managed virtual microphone" >&2
    exit 1
  fi
  pw-cat --record --raw --format=f32 --rate=48000 --channels=2 \
    --target=itervc_rust_microphone - >"$test_dir/gui-autoapp-output.pcm" \
    2>"$test_dir/gui-autoapp-record.log" &
  output_capture_pid="$!"
  sleep 2
  kill -TERM "$output_capture_pid" 2>/dev/null || true
  wait "$output_capture_pid" 2>/dev/null || true
  output_capture_pid=""
  python3 - "$test_dir/gui-autoapp-output.pcm" <<'PY'
from array import array
import os
import sys
path = sys.argv[1]
if not os.path.isfile(path) or os.path.getsize(path) < 8192:
    raise SystemExit("GUI auto-reattach produced no virtual microphone PCM")
samples = array("f")
with open(path, "rb") as recorded:
    samples.frombytes(recorded.read())
active = sum(abs(value) > 0.04 for value in samples)
if active < 1000:
    raise SystemExit("GUI failed to attach the delayed application to its virtual microphone")
print(f"GUI automatic late-app capture: {active} audible samples.")
PY
  if ! kill -0 "$gui_pid" 2>/dev/null; then
    cat "$test_dir/gui-session.log" >&2
    echo "GUI crashed during delayed application capture" >&2
    exit 1
  fi
  kill -TERM "$gui_autoplay_pid" 2>/dev/null || true
  wait "$gui_autoplay_pid" 2>/dev/null || true
  gui_autoplay_pid=""

  # Simulate the same selected application restarting under a new PipeWire
  # playback stream ID. The GUI must reattach it without restarting routing.
  pacat --playback --device=itervc_ci_audio_sink \
    --client-name=PipeWireAutoTestPlayer --stream-name=PipeWireAutoTestPlayer \
    --raw --format=float32le --rate=48000 --channels=2 \
    <"$test_dir/gui-autoapp.pcm" >"$test_dir/gui-autoapp-restarted.log" 2>&1 &
  gui_autoplay_pid="$!"
  restarted_stream=""
  for _ in $(seq 1 80); do
    restarted_stream="$(pactl list short sink-inputs |
      awk -v sink="$gui_original_sink" -v previous="$gui_playback_stream" \
        '$2==sink && $1!=previous {print $1; exit}')"
    if [ -n "$restarted_stream" ]; then break; fi
    sleep 0.1
  done
  if [ -z "$restarted_stream" ]; then
    echo "The restarted selected application had no new playback ID" >&2
    exit 1
  fi
  # Allow one GUI discovery interval; then verify the actual sound, not just
  # that a PipeWire monitor port happens to exist.
  sleep 5
  pw-cat --record --raw --format=f32 --rate=48000 --channels=2 \
    --target=itervc_rust_microphone - >"$test_dir/gui-restart-output.pcm" \
    2>"$test_dir/gui-restart-recorder.log" &
  output_capture_pid="$!"
  sleep 1.5
  kill -TERM "$output_capture_pid" 2>/dev/null || true
  wait "$output_capture_pid" 2>/dev/null || true
  output_capture_pid=""
  python3 - "$test_dir/gui-restart-output.pcm" <<'PY'
from array import array
import os
import sys

path = sys.argv[1]
if not os.path.isfile(path) or os.path.getsize(path) < 8192:
    raise SystemExit("Restarted application produced no virtual mic audio")
samples = array("f")
with open(path, "rb") as input:
    samples.frombytes(input.read())
active = sum(abs(sample) > 0.04 for sample in samples)
if active < 1000:
    raise SystemExit("Selected application's audio was not reconnected after restart")
print(f"GUI selected app restart: {active} nonzero samples.")
PY
  if ! kill -0 "$gui_pid" 2>/dev/null; then
    cat "$test_dir/gui-session.log" >&2
    echo "Linux GUI crashed during application restart capture" >&2
    exit 1
  fi
  kill -TERM "$gui_autoplay_pid" 2>/dev/null || true
  wait "$gui_autoplay_pid" 2>/dev/null || true
  gui_autoplay_pid=""

  "$gui" --stop-routing
  gui_stopped=0
  for _ in $(seq 1 80); do
    if ! pactl list short sources | grep -q "itervc_rust_microphone"; then
      gui_stopped=1
      break
    fi
    sleep 0.1
  done
  if [ "$gui_stopped" != 1 ]; then
    cat "$test_dir/gui-session.log" >&2
    echo "Linux GUI did not remove PipeWire virtual mic after stopping" >&2
    exit 1
  fi
  # Closing the running GUI must stop Rust and unload PipeWire through
  # MainWindow.OnClosing instead of just terminating the process.
  "$gui" --start-routing
  gui_restarted=0
  for _ in $(seq 1 80); do
    if pactl list short sources | grep -q "itervc_rust_microphone"; then
      gui_restarted=1
      break
    fi
    sleep 0.1
  done
  if [ "$gui_restarted" != 1 ]; then
    cat "$test_dir/gui-session.log" >&2
    echo "Linux GUI did not restart routing before shutdown test" >&2
    exit 1
  fi
  "$gui" --exit
  gui_exited=0
  for _ in $(seq 1 80); do
    if ! kill -0 "$gui_pid" 2>/dev/null; then
      gui_exited=1
      break
    fi
    sleep 0.1
  done
  if [ "$gui_exited" != 1 ]; then
    cat "$test_dir/gui-session.log" >&2
    echo "Linux GUI failed to close after exit shortcut command" >&2
    exit 1
  fi
  wait "$gui_pid" 2>/dev/null || true
  gui_pid=""
  if pactl list short sources | grep -q "itervc_rust_microphone"; then
    cat "$test_dir/gui-session.log" >&2
    echo "Linux GUI shutdown leaked the PipeWire virtual microphone" >&2
    exit 1
  fi
  echo "Real PipeWire + Avalonia GUI start/stop/shutdown via shortcut IPC: passed."
fi

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

# Enable app capture *before* the selected program starts. Its future
# playback stream must attach over stdin without restarting the microphone.
mkfifo "$test_dir/live-gain.fifo"
exec 9<>"$test_dir/live-gain.fifo"
"$app" --input itervc_ci_test_microphone --include-apps --app-volume 0.75 \
  --seconds 0 <"$test_dir/live-gain.fifo" >"$test_dir/app-session.log" 2>&1 &
app_pid="$!"
capture_ready=0
for _ in $(seq 1 80); do
  if pactl list short sinks | grep -q "itervc_rust_apps_sink"; then
    capture_ready=1
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ "$capture_ready" != 1 ]; then
  cat "$test_dir/app-session.log" >&2
  echo "Could not prepare empty app capture before launching an application" >&2
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
printf 'add-app %s\n' "$stream_id" >&9

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
# The second application is mono. IterVC must duplicate its sole
# output port to both channels of the shared stereo capture sink.
pacat --playback --device=itervc_ci_alt_audio_sink --raw --format=float32le \
  --rate=48000 --channels=1 </dev/zero >"$test_dir/pacat2.log" 2>&1 &
pulse_app_2_pid="$!"
alternate_sink_id="$(pactl list short sinks | awk '$2=="itervc_ci_alt_audio_sink" {print $1; exit}')"
if [ -z "$alternate_sink_id" ]; then
  echo "Missing independent second speaker sink" >&2
  exit 1
fi
stream_2_id=""
for _ in $(seq 1 50); do
  stream_2_id="$(pactl list short sink-inputs | awk -v sink="$alternate_sink_id" '$2==sink {print $1; exit}')"
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

# Begin with the first app, then attach the second while the session
# remains active. This exercises the exact hotplug command used by the GUI.
mkfifo "$test_dir/hotplug-app.fifo"
exec 7<>"$test_dir/hotplug-app.fifo"
"$app" --input itervc_ci_test_microphone \
  --app-stream "$stream_id" --app-volume 0.8 --seconds 0 \
  <"$test_dir/hotplug-app.fifo" >"$test_dir/multi-app.log" 2>&1 &
app_pid="$!"
sleep 0.6
printf 'add-app %s\n' "$stream_2_id" >&7

both_linked=0
for _ in $(seq 1 100); do
  current_1="$(pactl list short sink-inputs | awk -v id="$stream_id" '$1==id {print $2}')"
  current_2="$(pactl list short sink-inputs | awk -v id="$stream_2_id" '$1==id {print $2}')"
  links="$(pw-link --links || true)"
  if [ "$current_1" = "$original_sink_id" ] && [ "$current_2" = "$alternate_sink_id" ] \
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
exec 7>&-
exec 7<&-
final_first="$(pactl list short sink-inputs | awk -v stream="$stream_id" '$1==stream {print $2}')"
final_second="$(pactl list short sink-inputs | awk -v stream="$stream_2_id" '$1==stream {print $2}')"
if [ "$final_first" != "$original_sink_id" ] || [ "$final_second" != "$alternate_sink_id" ]; then
  cat "$test_dir/multi-app.log" >&2
  echo "One of the original app speaker outputs was changed" >&2
  exit 1
fi
if pactl list short sinks | grep -q "itervc_rust_apps_sink"; then
  echo "Temporary application capture sink was not removed" >&2
  exit 1
fi
cat "$test_dir/multi-app.log"

# Verify microphone gain is adjustable without restarting the selected
# capture source or the virtual microphone. The app playback sink is separate.
python3 - "$test_dir/microphone-signal.pcm" <<'PY'
import struct
import sys
with open(sys.argv[1], "wb") as stream:
    stream.write(struct.pack("<ff", 0.3, -0.3) * (48_000 * 35))
PY
pacat --playback --device=itervc_ci_microphone_source_sink --raw \
  --format=float32le --rate=48000 --channels=2 \
  <"$test_dir/microphone-signal.pcm" >"$test_dir/mic-playback.log" 2>&1 &
mic_input_pid="$!"

mkfifo "$test_dir/live-mic-gain.fifo"
exec 8<>"$test_dir/live-mic-gain.fifo"
"$app" --input itervc_ci_test_microphone --seconds 0 \
  <"$test_dir/live-mic-gain.fifo" >"$test_dir/mic-session.log" 2>&1 &
app_pid="$!"

mic_ready=0
for _ in $(seq 1 80); do
  if pactl list short sources | grep -q "itervc_rust_microphone"; then
    mic_ready=1
    break
  fi
  if ! kill -0 "$app_pid" 2>/dev/null; then break; fi
  sleep 0.1
done
if [ "$mic_ready" != 1 ]; then
  cat "$test_dir/mic-session.log" >&2
  echo "Live microphone mute test could not start routing" >&2
  exit 1
fi

capture_mic_virtual() {
  local path="$1"
  pw-cat --record --raw --format=f32 --rate=48000 --channels=2 \
    --target=itervc_rust_microphone - >"$path" 2>"$path.log" &
  output_capture_pid="$!"
  sleep 1.1
  kill -TERM "$output_capture_pid" 2>/dev/null || true
  wait "$output_capture_pid" 2>/dev/null || true
  output_capture_pid=""
}
check_virtual_level() {
  python3 - "$1" "$2" <<'PY'
from array import array
import os
import sys

path, expected = sys.argv[1:3]
if not os.path.isfile(path) or os.path.getsize(path) < 8192:
    raise SystemExit("Live mic test did not record enough PCM")
samples = array("f")
with open(path, "rb") as stream:
    samples.frombytes(stream.read())
active = sum(abs(x) > 0.04 for x in samples)
if expected == "sound" and active < 1000:
    raise SystemExit("Microphone audio was absent from the virtual source")
if expected == "silence" and active > 500:
    raise SystemExit("Mic gain zero did not silence the virtual microphone")
print(f"Live microphone gain: {expected} (active samples={active}).")
PY
}

sleep 0.4
capture_mic_virtual "$test_dir/mic-audible.pcm"
check_virtual_level "$test_dir/mic-audible.pcm" sound
printf 'mic-volume 0\n' >&8
sleep 0.5
capture_mic_virtual "$test_dir/mic-muted.pcm"
check_virtual_level "$test_dir/mic-muted.pcm" silence
if ! kill -0 "$app_pid" 2>/dev/null; then
  echo "Muting microphone unexpectedly terminated routing" >&2
  exit 1
fi
printf 'mic-volume 1\n' >&8
sleep 0.5
capture_mic_virtual "$test_dir/mic-restored.pcm"
check_virtual_level "$test_dir/mic-restored.pcm" sound

# The exact same virtual microphone must stay alive when gate state and
# its threshold/attack/release settings change through live Rust IPC.
# At 0.3 PCM amplitude, -10 dBFS keeps the gate closed; -40 dBFS opens it.
printf 'gate-attack 10\ngate-release 60\ngate-threshold -10\ngate-enabled 1\n' >&8
sleep 0.6
capture_mic_virtual "$test_dir/gate-closed.pcm"
check_virtual_level "$test_dir/gate-closed.pcm" silence
if ! kill -0 "$app_pid" 2>/dev/null; then
  echo "Changing live noise gate settings terminated routing" >&2
  exit 1
fi
printf 'gate-threshold -40\n' >&8
sleep 0.5
capture_mic_virtual "$test_dir/gate-open.pcm"
check_virtual_level "$test_dir/gate-open.pcm" sound
printf 'gate-enabled 0\n' >&8
sleep 0.5
capture_mic_virtual "$test_dir/gate-bypassed.pcm"
check_virtual_level "$test_dir/gate-bypassed.pcm" sound
if ! grep -q 'ITERVC_MIC_INPUT_LEVEL:' "$test_dir/mic-session.log" ||
   ! grep -q 'ITERVC_MIC_OUTPUT_LEVEL:' "$test_dir/mic-session.log"; then
  cat "$test_dir/mic-session.log" >&2
  echo "Separate real microphone input/output PCM meter telemetry was missing" >&2
  exit 1
fi

kill -TERM "$app_pid"
wait "$app_pid"
app_pid=""
exec 8>&-
exec 8<&-
kill -TERM "$mic_input_pid" 2>/dev/null || true
wait "$mic_input_pid" 2>/dev/null || true
mic_input_pid=""
if pactl list short sources | grep -q "itervc_rust_microphone"; then
  echo "Live mic test did not unload virtual source" >&2
  exit 1
fi
cat "$test_dir/mic-session.log"
echo "Real PipeWire virtual mic, non-invasive app taps, live gain/gate, and microphone meters passed."

