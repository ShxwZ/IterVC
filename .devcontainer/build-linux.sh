#!/usr/bin/env bash
# Build the Linux browser preview from THIS branch, not from a stale artifact.
set -Eeuo pipefail
cd "$(dirname "$0")/.."

command -v cargo >/dev/null || { echo "Missing Rust toolchain" >&2; exit 1; }
command -v dotnet >/dev/null || { echo "Missing .NET SDK 8" >&2; exit 1; }

echo "[IterVC] Building Linux Rust/PipeWire worker..."
cargo build --locked --manifest-path rust/Cargo.toml -p itervc-linux-pipewire --release

echo "[IterVC] Building the SAME RNNoise model/engine as Windows..."
cargo build --manifest-path native/iter_vc_rnnoise/Cargo.toml --release
install -m 644 native/iter_vc_rnnoise/target/release/libiter_vc_rnnoise.so \
  rust/target/release/libiter_vc_rnnoise.so

output="$HOME/.local/share/itervc-browser"
mkdir -p "$output"
echo "[IterVC] Publishing self-contained Linux Avalonia UI..."
dotnet publish IterVC.Linux/IterVC.Linux.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugSymbols=false -p:DebugType=None -o "$output"

test -x "$output/IterVC.Linux" || chmod u+x "$output/IterVC.Linux"
"$output/IterVC.Linux" --self-test
echo "[IterVC] Ready: $output/IterVC.Linux"
