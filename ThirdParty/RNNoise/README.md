# RNNoise-compatible microphone suppression

IterVC uses the `nnnoiseless` 0.5.2 Rust port of Xiph RNNoise:
https://github.com/jneem/nnnoiseless

Upstream RNNoise: https://github.com/xiph/rnnoise

License: BSD-3-Clause (full upstream notice in `COPYING`).
The default RNNoise model is included in the Rust crate and compiled into
`iter_vc_rnnoise.dll`. No model download, Python or Rust installation is
required on end-user machines.

The RNNoise bridge consumes 480-frame stereo blocks of 48 kHz normalized
floating point audio, using one independent model state per channel. The
bridge performs the PCM scaling required by RNNoise.

The DLL is bundled in the .NET single-file executable (and extracted
automatically by the .NET runtime). The noise suppression stage is opt-in,
runs off the WASAPI callback and falls back to unmodified microphone audio
if the native backend fails. Both microphone monitoring and VB-Cable routing
receive the same downstream post-gate/post-gain signal.
