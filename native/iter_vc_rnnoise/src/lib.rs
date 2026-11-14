use std::panic::{catch_unwind, AssertUnwindSafe};
use std::slice;

use nnnoiseless::DenoiseState;

const FRAME: usize = DenoiseState::FRAME_SIZE;
const CHANNELS: usize = 2;
const SAMPLES: usize = FRAME * CHANNELS;
const PCM_SCALE: f32 = 32768.0;

pub struct RnNoiseRuntime {
    states: [Box<DenoiseState<'static>>; CHANNELS],
    input: [[f32; FRAME]; CHANNELS],
    output: [[f32; FRAME]; CHANNELS],
}

#[no_mangle]
pub extern "C" fn ivc_rnnoise_create() -> *mut RnNoiseRuntime {
    catch_unwind(AssertUnwindSafe(|| {
        Box::into_raw(Box::new(RnNoiseRuntime {
            states: [DenoiseState::new(), DenoiseState::new()],
            input: [[0.0; FRAME]; CHANNELS],
            output: [[0.0; FRAME]; CHANNELS],
        }))
    }))
    .unwrap_or(std::ptr::null_mut())
}

/// Expects precisely 480 interleaved float32 samples per stereo channel at 48 kHz.
/// Floats on the C ABI boundary are normalized to [-1, 1].
#[no_mangle]
pub unsafe extern "C" fn ivc_rnnoise_process_frame(
    runtime: *mut RnNoiseRuntime,
    input: *const f32,
    output: *mut f32,
) -> bool {
    if runtime.is_null() || input.is_null() || output.is_null() {
        return false;
    }

    catch_unwind(AssertUnwindSafe(|| {
        let state = &mut *runtime;
        let source = slice::from_raw_parts(input, SAMPLES);
        let destination = slice::from_raw_parts_mut(output, SAMPLES);

        for sample in 0..FRAME {
            for channel in 0..CHANNELS {
                let value = source[sample * CHANNELS + channel];
                state.input[channel][sample] =
                    if value.is_finite() { value.clamp(-1.0, 1.0) * PCM_SCALE } else { 0.0 };
            }
        }

        for channel in 0..CHANNELS {
            state.states[channel].process_frame(
                &mut state.output[channel],
                &state.input[channel],
            );
        }

        for sample in 0..FRAME {
            for channel in 0..CHANNELS {
                let value = state.output[channel][sample] / PCM_SCALE;
                destination[sample * CHANNELS + channel] =
                    if value.is_finite() { value.clamp(-1.0, 1.0) } else { 0.0 };
            }
        }
        true
    }))
    .unwrap_or(false)
}

#[no_mangle]
pub unsafe extern "C" fn ivc_rnnoise_free(runtime: *mut RnNoiseRuntime) {
    if !runtime.is_null() {
        let _ = catch_unwind(AssertUnwindSafe(|| drop(Box::from_raw(runtime))));
    }
}
