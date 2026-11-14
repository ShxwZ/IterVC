//! Linux adapter for the **same** RNNoise native ABI that Windows uses.
//! Loaded only when opted in; the normal PipeWire path has no extra dynamic
//! library dependency. The DSP runs in the existing PCM worker, never in a
//! PipeWire callback. No unsafe pointers escape this module.

use std::ffi::{c_char, c_void, CStr, CString};
use std::path::PathBuf;

pub const FRAME_BYTES: usize = 480 * 2 * std::mem::size_of::<f32>();
const FRAME_SAMPLES: usize = 480 * 2;
const RTLD_NOW: i32 = 2;

#[link(name = "dl")]
unsafe extern "C" {
    fn dlopen(filename: *const c_char, flags: i32) -> *mut c_void;
    fn dlsym(handle: *mut c_void, symbol: *const c_char) -> *mut c_void;
    fn dlclose(handle: *mut c_void) -> i32;
    fn dlerror() -> *const c_char;
}

type Create = unsafe extern "C" fn() -> *mut c_void;
type Process = unsafe extern "C" fn(*mut c_void, *const f32, *mut f32) -> bool;
type Free = unsafe extern "C" fn(*mut c_void);

pub struct NoiseSuppressor {
    library: *mut c_void,
    runtime: *mut c_void,
    process: Process,
    free: Free,
    input: [f32; FRAME_SAMPLES],
    output: [f32; FRAME_SAMPLES],
}

impl NoiseSuppressor {
    pub fn new() -> Result<Self, String> {
        let path = std::env::var_os("ITERVC_RNNOISE_LIBRARY").map(PathBuf::from)
            .or_else(|| std::env::current_exe().ok().and_then(|p|
                p.parent().map(|dir| dir.join("libiter_vc_rnnoise.so"))))
            .ok_or("Could not locate RNNoise native library")?;
        let filename = CString::new(path.to_string_lossy().as_bytes())
            .map_err(|_| "RNNoise library path contains a NUL byte")?;

        unsafe {
            let library = dlopen(filename.as_ptr(), RTLD_NOW);
            if library.is_null() {
                let error = dlerror();
                let detail = if error.is_null() {
                    "unknown error".to_owned()
                } else {
                    CStr::from_ptr(error).to_string_lossy().into_owned()
                };
                return Err(format!("Could not load RNNoise native library: {detail}"));
            }
            let loaded = (|| {
                let create: Create = symbol(library, b"ivc_rnnoise_create\0")?;
                let process: Process = symbol(library, b"ivc_rnnoise_process_frame\0")?;
                let free: Free = symbol(library, b"ivc_rnnoise_free\0")?;
                let runtime = create();
                if runtime.is_null() {
                    return Err("Could not initialize native RNNoise processor".to_owned());
                }
                Ok(Self { library, runtime, process, free,
                    input: [0.0; FRAME_SAMPLES], output: [0.0; FRAME_SAMPLES] })
            })();
            if loaded.is_err() { dlclose(library); }
            loaded
        }
    }

    /// Exactly 10 ms, 48 kHz, interleaved stereo f32 PCM. The native library
    /// is identical to the one used by Windows, including its model.
    pub fn process_frame(&mut self, frame: &mut [u8]) -> Result<(), String> {
        if frame.len() != FRAME_BYTES {
            return Err("RNNoise requires exactly 480 stereo frames".into());
        }
        for (index, bytes) in frame.chunks_exact(4).enumerate() {
            self.input[index] = f32::from_le_bytes(bytes.try_into().unwrap());
        }
        if !unsafe { (self.process)(
            self.runtime, self.input.as_ptr(), self.output.as_mut_ptr()) } {
            return Err("Native RNNoise processing failed".into());
        }
        for (sample, bytes) in self.output.iter().zip(frame.chunks_exact_mut(4)) {
            bytes.copy_from_slice(&sample.to_le_bytes());
        }
        Ok(())
    }
}

unsafe fn symbol<T>(library: *mut c_void, name: &[u8]) -> Result<T, String> {
    let result = dlsym(library, name.as_ptr().cast());
    if result.is_null() {
        return Err(format!("Missing RNNoise symbol {}",
            String::from_utf8_lossy(&name[..name.len() - 1])));
    }
    Ok(std::mem::transmute_copy(&result))
}

impl Drop for NoiseSuppressor {
    fn drop(&mut self) {
        unsafe {
            if !self.runtime.is_null() { (self.free)(self.runtime); }
            if !self.library.is_null() { dlclose(self.library); }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn frame_size_is_exactly_ten_milliseconds_of_stereo_float_pcm() {
        assert_eq!(FRAME_BYTES, 3840);
        assert_eq!(FRAME_SAMPLES, 960);
    }
}
