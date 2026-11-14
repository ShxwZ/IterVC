#!/usr/bin/env bash
# Exercise an actual Avalonia window under a virtual X11 display.
set -euo pipefail
binary="$1"
log="$(mktemp)"
"$binary" >"$log" 2>&1 &
gui_pid="$!"
cleanup() {
  kill -TERM "$gui_pid" 2>/dev/null || true
  wait "$gui_pid" 2>/dev/null || true
  cat "$log"
  rm -f "$log"
}
trap cleanup EXIT

window=""
for _ in $(seq 1 100); do
  window="$(xdotool search --onlyvisible --name 'IterVC.*Linux' 2>/dev/null | head -n 1 || true)"
  if [ -n "$window" ]; then break; fi
  if ! kill -0 "$gui_pid" 2>/dev/null; then
    echo "IterVC Linux exited before creating the Avalonia window" >&2
    exit 1
  fi
  sleep 0.1
done
if [ -z "$window" ]; then
  echo "Avalonia did not create a visible IterVC Linux X11 window" >&2
  exit 1
fi

# Xvfb deliberately has no window manager. windowfocus uses XSetInputFocus
# directly; windowactivate would require an EWMH-compliant window manager.
xdotool windowfocus --sync "$window"
xdotool key --clearmodifiers ctrl+shift+r
sleep 0.5
if ! kill -0 "$gui_pid" 2>/dev/null; then
  echo "IterVC Linux crashed after invoking its routing hotkey" >&2
  exit 1
fi
"$binary" --toggle-monitor
"$binary" --show-window
if ! kill -0 "$gui_pid" 2>/dev/null; then
  echo "IterVC Linux crashed after shortcut IPC" >&2
  exit 1
fi
echo "Avalonia X11 window, focused routing hotkey and IPC controls: passed."
