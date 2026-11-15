#!/usr/bin/env bash
# Codespaces postStartCommand: launch the private browser desktop and return.
set -Eeuo pipefail
cd "$(dirname "$0")/.."
state="$HOME/.local/state/itervc-browser"
mkdir -p "$state"

if curl --silent --fail --max-time 2 http://127.0.0.1:6080/vnc.html >/dev/null; then
  echo "[IterVC] Browser desktop already active (private Codespaces port 6080)."
  exit 0
fi
if [ ! -x "$HOME/.local/share/itervc-browser/IterVC.Linux" ]; then
  echo "[IterVC] Linux executable not found. Run: bash .devcontainer/build-linux.sh" >&2
  exit 1
fi

nohup bash .devcontainer/desktop-session.sh >>"$state/session.log" 2>&1 </dev/null &
echo "$!" >"$state/session.pid"

for _ in $(seq 1 20); do
  if curl --silent --fail --max-time 2 http://127.0.0.1:6080/vnc.html >/dev/null; then
    echo "[IterVC] Desktop ready. Open port 6080 (PRIVATE) in the Codespaces PORTS panel."
    echo "[IterVC] URL path: /vnc.html?autoconnect=1&resize=scale"
    exit 0
  fi
  if ! kill -0 "$(cat "$state/session.pid")" 2>/dev/null; then
    cat "$state/session.log" >&2
    exit 1
  fi
  sleep 1
done
echo "[IterVC] Browser desktop is not responding. Inspect: $state/session.log" >&2
exit 1
