#!/usr/bin/env bash
# Session process: virtual X11 + openbox + private VNC over Codespaces HTTPS.
set -Eeuo pipefail

state="$HOME/.local/state/itervc-browser"
mkdir -p "$state"
export DISPLAY=:99
export XDG_RUNTIME_DIR="/tmp/itervc-browser-runtime-$(id -u)"
install -d -m 700 "$XDG_RUNTIME_DIR"

# The desktop has its own user D-Bus and PipeWire daemons; it has no physical
# sound card and cannot capture the visitor's Windows microphone.
eval "$(dbus-launch --sh-syntax)"
pids=()
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

Xvfb :99 -screen 0 1366x768x24 -nolisten tcp >"$state/xvfb.log" 2>&1 &
pids+=("$!")
ready=0
for _ in $(seq 1 40); do
  if xdpyinfo -display :99 >/dev/null 2>&1; then ready=1; break; fi
  sleep 0.25
done
if [ "$ready" != 1 ]; then
  cat "$state/xvfb.log" >&2
  exit 1
fi

openbox >"$state/openbox.log" 2>&1 &
pids+=("$!")

# No elevated privileges or host sound devices needed for a GUI preview.
# PipeWire is best-effort: a Codespace is not a real audio workstation.
pipewire >"$state/pipewire.log" 2>&1 &
pids+=("$!")
wireplumber >"$state/wireplumber.log" 2>&1 &
pids+=("$!")
pipewire-pulse >"$state/pipewire-pulse.log" 2>&1 &
pids+=("$!")

# VNC and its WebSocket proxy listen only on loopback; Codespaces forwards
# 6080 privately after GitHub authentication. Never mark it PUBLIC.
x11vnc -display :99 -localhost -nopw -forever -shared \
  -rfbport 5900 -noxdamage -quiet >"$state/x11vnc.log" 2>&1 &
pids+=("$!")
websockify --web=/usr/share/novnc \
  127.0.0.1:6080 127.0.0.1:5900 >"$state/websockify.log" 2>&1 &
proxy_pid="$!"
pids+=("$proxy_pid")

binary="$HOME/.local/share/itervc-browser/IterVC.Linux"
if [ -x "$binary" ]; then
  "$binary" >"$state/itervc.log" 2>&1 &
  pids+=("$!")
fi

# Keep the private preview available even if the user closes IterVC. The app
# can be relaunched from the Codespaces terminal with the command in README.
wait "$proxy_pid"
