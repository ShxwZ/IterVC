//! Cross-platform PCM ring buffer, written in Rust.
//!
//! A buffer instance has one owner at a time. The C ABI is intentionally not
//! thread-safe: callers must serialize writes, reads and destruction.
//! The existing .NET/Windows buffer remains the production implementation
//! until parity, latency and concurrency benchmarks justify switching.

use std::slice;

const MAX_CAPACITY_BYTES: usize = 64 * 1024 * 1024;

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct BufferStats {
    pub written_frames: u64,
    pub read_frames: u64,
    pub discarded_frames: u64,
    pub overflow_events: u64,
    pub underflow_events: u64,
    pub occupancy_frames: usize,
}

#[derive(Debug)]
pub struct PcmRingBuffer {
    data: Vec<u8>,
    frame_bytes: usize,
    head: usize,
    len: usize,
    stats: BufferStats,
}

impl PcmRingBuffer {
    /// \`capacity_frames\` is the maximum number of interleaved PCM frames.
    /// \`frame_bytes\` is channels * bytes per sample; all reads/writes are
    /// frame-aligned. Oldest frames are discarded on overflow.
    pub fn new(capacity_frames: usize, frame_bytes: usize) -> Option<Self> {
        let capacity = capacity_frames.checked_mul(frame_bytes)?;
        if capacity == 0 || frame_bytes == 0 || capacity > MAX_CAPACITY_BYTES {
            return None;
        }
        Some(Self {
            data: vec![0; capacity],
            frame_bytes,
            head: 0,
            len: 0,
            stats: BufferStats::default(),
        })
    }

    pub fn capacity_frames(&self) -> usize {
        self.data.len() / self.frame_bytes
    }

    pub fn available_frames(&self) -> usize {
        self.len / self.frame_bytes
    }

    pub fn stats(&self) -> BufferStats {
        let mut snapshot = self.stats;
        snapshot.occupancy_frames = self.available_frames();
        snapshot
    }

    /// Returns the number of input *frames* retained, excluding any trimmed
    /// frames when a write exceeds capacity. Incomplete frames are ignored.
    pub fn write(&mut self, input: &[u8]) -> usize {
        let aligned = input.len() / self.frame_bytes * self.frame_bytes;
        if aligned == 0 {
            return 0;
        }
        let capacity = self.data.len();
        let retained = aligned.min(capacity);
        let incoming_discarded = aligned - retained;
        let previous_discarded = (self.len + retained).saturating_sub(capacity);
        let discarded = incoming_discarded + previous_discarded;

        if discarded > 0 {
            self.stats.discarded_frames += (discarded / self.frame_bytes) as u64;
            self.stats.overflow_events += 1;
        }
        if previous_discarded > 0 {
            self.head = (self.head + previous_discarded) % capacity;
            self.len -= previous_discarded;
        }

        let source = &input[aligned - retained..aligned];
        let tail = (self.head + self.len) % capacity;
        let first = retained.min(capacity - tail);
        self.data[tail..tail + first].copy_from_slice(&source[..first]);
        self.data[..retained - first].copy_from_slice(&source[first..]);

        self.len += retained;
        self.stats.written_frames += (retained / self.frame_bytes) as u64;
        retained / self.frame_bytes
    }

    /// Reads up to \`output.len()\` bytes, rounded down to full PCM frames.
    /// Returns bytes actually copied, without filling silence.
    pub fn read(&mut self, output: &mut [u8]) -> usize {
        let requested = output.len() / self.frame_bytes * self.frame_bytes;
        if requested == 0 {
            return 0;
        }
        let take = requested.min(self.len);
        if take < requested {
            self.stats.underflow_events += 1;
        }
        if take == 0 {
            return 0;
        }
        let first = take.min(self.data.len() - self.head);
        output[..first].copy_from_slice(&self.data[self.head..self.head + first]);
        output[first..take].copy_from_slice(&self.data[..take - first]);
        self.head = (self.head + take) % self.data.len();
        self.len -= take;
        self.stats.read_frames += (take / self.frame_bytes) as u64;
        take
    }

    pub fn clear(&mut self) {
        self.head = 0;
        self.len = 0;
    }
}

/// Opaque handle for eventual .NET integration, not used by Windows by default.
/// Caller must serialize all access and must not free an active handle.
#[no_mangle]
pub extern "C" fn ivc_pcm_ring_create(
    capacity_frames: usize,
    frame_bytes: usize,
) -> *mut PcmRingBuffer {
    PcmRingBuffer::new(capacity_frames, frame_bytes)
        .map(|ring| Box::into_raw(Box::new(ring)))
        .unwrap_or(std::ptr::null_mut())
}

#[no_mangle]
pub unsafe extern "C" fn ivc_pcm_ring_write(
    handle: *mut PcmRingBuffer,
    input: *const u8,
    length: usize,
) -> usize {
    if handle.is_null() || input.is_null() {
        return 0;
    }
    (*handle).write(slice::from_raw_parts(input, length))
}

#[no_mangle]
pub unsafe extern "C" fn ivc_pcm_ring_read(
    handle: *mut PcmRingBuffer,
    output: *mut u8,
    capacity: usize,
) -> usize {
    if handle.is_null() || output.is_null() {
        return 0;
    }
    (*handle).read(slice::from_raw_parts_mut(output, capacity))
}

#[no_mangle]
pub unsafe extern "C" fn ivc_pcm_ring_available_frames(handle: *const PcmRingBuffer) -> usize {
    if handle.is_null() {
        0
    } else {
        (*handle).available_frames()
    }
}

#[no_mangle]
pub unsafe extern "C" fn ivc_pcm_ring_free(handle: *mut PcmRingBuffer) {
    if !handle.is_null() {
        drop(Box::from_raw(handle));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::VecDeque;

    #[test]
    fn rejects_invalid_allocations() {
        assert!(PcmRingBuffer::new(0, 8).is_none());
        assert!(PcmRingBuffer::new(10, 0).is_none());
        assert!(PcmRingBuffer::new(usize::MAX, 8).is_none());
        assert!(PcmRingBuffer::new(MAX_CAPACITY_BYTES + 1, 1).is_none());
    }

    #[test]
    fn preserves_interleaved_frames_and_partial_reads() {
        let mut ring = PcmRingBuffer::new(4, 4).unwrap();
        assert_eq!(ring.write(&[1, 2, 3, 4, 5, 6, 7, 8]), 2);
        assert_eq!(ring.available_frames(), 2);
        let mut output = [0u8; 4];
        assert_eq!(ring.read(&mut output), 4);
        assert_eq!(output, [1, 2, 3, 4]);
        assert_eq!(ring.read(&mut output), 4);
        assert_eq!(output, [5, 6, 7, 8]);
        assert_eq!(ring.read(&mut output), 0);
        assert_eq!(ring.stats().underflow_events, 1);
    }

    #[test]
    fn handles_wraparound_and_frame_alignment() {
        let mut ring = PcmRingBuffer::new(3, 2).unwrap();
        assert_eq!(ring.write(&[1, 2, 3, 4]), 2);
        assert_eq!(ring.read(&mut [0u8; 2]), 2);
        assert_eq!(ring.write(&[5, 6, 7, 8, 9]), 2);
        let mut output = [0u8; 7];
        assert_eq!(ring.read(&mut output), 6);
        assert_eq!(&output[..6], &[3, 4, 5, 6, 7, 8]);
        assert_eq!(ring.available_frames(), 0);
    }

    #[test]
    fn discards_oldest_frames_on_overflow() {
        let mut ring = PcmRingBuffer::new(3, 1).unwrap();
        ring.write(&[1, 2, 3]);
        ring.write(&[4, 5]);
        let mut output = [0; 3];
        assert_eq!(ring.read(&mut output), 3);
        assert_eq!(output, [3, 4, 5]);
        assert_eq!(ring.stats().discarded_frames, 2);
        assert_eq!(ring.stats().overflow_events, 1);
    }

    #[test]
    fn large_write_keeps_only_latest_frames() {
        let mut ring = PcmRingBuffer::new(3, 1).unwrap();
        assert_eq!(ring.write(&[1, 2, 3, 4, 5, 6]), 3);
        let mut output = [0; 3];
        assert_eq!(ring.read(&mut output), 3);
        assert_eq!(output, [4, 5, 6]);
        assert_eq!(ring.stats().discarded_frames, 3);
    }

    #[test]
    fn random_operations_match_reference_queue() {
        let mut ring = PcmRingBuffer::new(17, 1).unwrap();
        let mut reference = VecDeque::new();
        let mut seed = 42u64;
        for _ in 0..10_000 {
            seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1);
            let count = ((seed >> 32) % 25) as usize;
            if seed & 1 == 0 {
                let data: Vec<u8> = (0..count).map(|i| (i as u8) ^ seed as u8).collect();
                ring.write(&data);
                for b in data {
                    if reference.len() == 17 {
                        reference.pop_front();
                    }
                    reference.push_back(b);
                }
            } else {
                let mut actual = vec![0; count];
                let got = ring.read(&mut actual);
                let mut expected = Vec::new();
                for _ in 0..count.min(reference.len()) {
                    expected.push(reference.pop_front().unwrap());
                }
                assert_eq!(&actual[..got], expected.as_slice());
                assert_eq!(got, expected.len());
            }
            assert_eq!(ring.available_frames(), reference.len());
        }
    }

    #[test]
    fn c_abi_round_trip_and_null_checks() {
        unsafe {
            let handle = ivc_pcm_ring_create(4, 2);
            assert!(!handle.is_null());
            let input = [10u8, 11, 12, 13];
            assert_eq!(ivc_pcm_ring_write(handle, input.as_ptr(), 4), 2);
            assert_eq!(ivc_pcm_ring_available_frames(handle), 2);
            let mut output = [0u8; 4];
            assert_eq!(ivc_pcm_ring_read(handle, output.as_mut_ptr(), 4), 4);
            assert_eq!(output, input);
            assert_eq!(ivc_pcm_ring_write(std::ptr::null_mut(), input.as_ptr(), 4), 0);
            assert_eq!(ivc_pcm_ring_available_frames(std::ptr::null()), 0);
            ivc_pcm_ring_free(handle);
            ivc_pcm_ring_free(std::ptr::null_mut());
        }
    }

    #[test]
    fn clear_preserves_stats_but_discards_queued_audio() {
        let mut ring = PcmRingBuffer::new(3, 1).unwrap();
        ring.write(&[1, 2]);
        ring.clear();
        assert_eq!(ring.available_frames(), 0);
        assert_eq!(ring.stats().written_frames, 2);
        assert_eq!(ring.read(&mut [0u8; 1]), 0);
    }
}
