use std::panic::{catch_unwind, AssertUnwindSafe};
use std::slice;

use deep_filter::tract::{DfParams, DfTract, RuntimeParams};
use tract_core::ndarray::{ArrayView2, ArrayViewMut2};

pub struct DeepFilterRuntime {
    filter: DfTract,
    channels: usize,
    frame_length: usize,
    input_planar: Vec<f32>,
    output_planar: Vec<f32>,
}

#[no_mangle]
pub extern "C" fn ivc_dfn_create(channels: u32) -> *mut DeepFilterRuntime {
    let result = catch_unwind(AssertUnwindSafe(|| {
        let channels = match channels {
            1 | 2 => channels as usize,
            _ => return None,
        };

        let params = RuntimeParams::default_with_ch(channels)
            .with_atten_lim(35.0)
            .with_thresholds(-10.0, 30.0, 20.0)
            .with_post_filter(0.02)
            .with_mask_reduce(deep_filter::tract::ReduceMask::MEAN);

        let model = DfParams::default();
        let filter = DfTract::new(model, &params).ok()?;
        let frame_length = filter.hop_size;

        Some(Box::new(DeepFilterRuntime {
            filter,
            channels,
            frame_length,
            input_planar: vec![0.0; channels * frame_length],
            output_planar: vec![0.0; channels * frame_length],
        }))
    }));

    match result {
        Ok(Some(runtime)) => Box::into_raw(runtime),
        _ => std::ptr::null_mut(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn ivc_dfn_get_frame_length(runtime: *const DeepFilterRuntime) -> usize {
    if runtime.is_null() {
        return 0;
    }
    (*runtime).frame_length
}

#[no_mangle]
pub unsafe extern "C" fn ivc_dfn_reset(runtime: *mut DeepFilterRuntime) -> bool {
    if runtime.is_null() {
        return false;
    }

    catch_unwind(AssertUnwindSafe(|| (*runtime).filter.init().is_ok())).unwrap_or(false)
}

#[no_mangle]
pub unsafe extern "C" fn ivc_dfn_process_frame(
    runtime: *mut DeepFilterRuntime,
    interleaved_input: *const f32,
    interleaved_output: *mut f32,
) -> f32 {
    if runtime.is_null() || interleaved_input.is_null() || interleaved_output.is_null() {
        return f32::NAN;
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let state = &mut *runtime;
        let sample_count = state.channels * state.frame_length;
        let input = slice::from_raw_parts(interleaved_input, sample_count);

        for frame in 0..state.frame_length {
            for channel in 0..state.channels {
                state.input_planar[channel * state.frame_length + frame] =
                    input[frame * state.channels + channel];
            }
        }

        let noisy = ArrayView2::from_shape(
            (state.channels, state.frame_length),
            state.input_planar.as_slice(),
        )
        .map_err(|_| ())?;
        let enhanced = ArrayViewMut2::from_shape(
            (state.channels, state.frame_length),
            state.output_planar.as_mut_slice(),
        )
        .map_err(|_| ())?;

        let lsnr = state.filter.process(noisy, enhanced).map_err(|_| ())?;

        let output = slice::from_raw_parts_mut(interleaved_output, sample_count);
        for frame in 0..state.frame_length {
            for channel in 0..state.channels {
                output[frame * state.channels + channel] =
                    state.output_planar[channel * state.frame_length + frame];
            }
        }

        Ok::<f32, ()>(lsnr)
    }));

    result.ok().and_then(Result::ok).unwrap_or(f32::NAN)
}

#[no_mangle]
pub unsafe extern "C" fn ivc_dfn_set_attenuation_limit(
    runtime: *mut DeepFilterRuntime,
    attenuation_db: f32,
) {
    if runtime.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| {
        (*runtime).filter.set_atten_lim(attenuation_db);
    }));
}

#[no_mangle]
pub unsafe extern "C" fn ivc_dfn_set_post_filter_beta(
    runtime: *mut DeepFilterRuntime,
    beta: f32,
) {
    if runtime.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| {
        (*runtime).filter.set_pf_beta(beta);
    }));
}

#[no_mangle]
pub unsafe extern "C" fn ivc_dfn_free(runtime: *mut DeepFilterRuntime) {
    if runtime.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| {
        drop(Box::from_raw(runtime));
    }));
}
