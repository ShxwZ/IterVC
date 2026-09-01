# DeepFilterNet3

IterVC uses the upstream DeepFilterNet3 runtime from:

- Project: https://github.com/Rikorose/DeepFilterNet
- Pinned source revision: `d375b2d8309e0935d165700c91da9de862a99c31`
- License: MIT OR Apache-2.0
- Native model: the upstream DFN3 model is embedded by the `default-model` feature of `libDF`.

The native bridge in `native/iter_vc_deep_filter` links the pinned upstream `deep_filter` crate and exposes a small C ABI for the IterVC audio service.

The corresponding upstream license texts are included next to this file as `LICENSE-MIT.txt` and `LICENSE-APACHE.txt`.

DeepFilterNet is used locally; IterVC does not send microphone audio to a remote service.
