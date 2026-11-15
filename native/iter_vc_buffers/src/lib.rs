//! Experimental Rust PCM ring buffer, isolated from Windows production audio.
//!
//! Deliberately follows the semantics of the exact existing
//! \`IterVC.Audio/Buffers/RealtimeAudioBuffer.cs\`: write alignment,
//! dropping oldest buffered frames on overflow, high-water latency
//! correction, optional read-to-silence, and occupancy counters.
//! All C ABI operations (including snapshots) serialize through the same
//! mutex, just as the C# reference uses a lock. This is *not* yet used by
//! the Windows/NAudio production path.
//!
//! FFI safety contract: callers supply valid non-overlapping buffers of
//! the given lengths, hold the handle alive for the duration of each call,
//! and must not call free concurrently with another operation.

use std::slice;
#[cfg(not(feature = "parking"))]
use std::sync::Mutex;
#[cfg(feature = "parking")]
use parking_lot::Mutex;

const MAX_CAPACITY_BYTES: usize = 64 * 1024 * 1024;

#[derive(Debug, Clone, Copy, Default, PartialEq)]
#[repr(C)]
pub struct BufferSnapshot {
    pub buffered_bytes: u64,
    pub target_bytes: u64,
    pub minimum_buffered_bytes: u64,
    pub maximum_buffered_bytes: u64,
    pub average_buffered_bytes: f64,
    pub written_frames: u64,
    pub read_frames: u64,
    pub underflows: u64,
    pub overflows: u64,
    pub corrections: u64,
    pub discarded_frames: u64,
}

#[derive(Debug)]
pub struct PcmBuffer {
    bytes: Vec<u8>,
    block_align: usize,
    target_bytes: usize,
    correction_bytes: usize,
    read_fully: bool,
    count_underflows: bool,
    read_position: usize,
    write_position: usize,
    buffered: usize,
    written_frames: u64,
    read_frames: u64,
    underflows: u64,
    overflows: u64,
    corrections: u64,
    discarded_frames: u64,
    occupancy_samples: u64,
    total_occupied: u64,
    minimum_occupied: usize,
    maximum_occupied: usize,
}

impl PcmBuffer {
    pub fn new(
        capacity_bytes: usize, block_align: usize, target_bytes: usize,
        correction_bytes: usize, read_fully: bool, count_underflows: bool,
    ) -> Option<Self> {
        if block_align == 0 || capacity_bytes == 0 || capacity_bytes > MAX_CAPACITY_BYTES
            || capacity_bytes % block_align != 0 || target_bytes == 0
            || target_bytes > capacity_bytes || correction_bytes == 0
            || correction_bytes > capacity_bytes
            || target_bytes % block_align != 0 || correction_bytes % block_align != 0
        {
            return None;
        }
        Some(Self {
            bytes: vec![0; capacity_bytes],
            block_align, target_bytes, correction_bytes, read_fully, count_underflows,
            read_position: 0, write_position: 0, buffered: 0,
            written_frames: 0, read_frames: 0, underflows: 0,
            overflows: 0, corrections: 0, discarded_frames: 0,
            occupancy_samples: 0, total_occupied: 0,
            minimum_occupied: usize::MAX, maximum_occupied: 0,
        })
    }

    fn align(&self, count: usize) -> usize {
        (count / self.block_align * self.block_align).max(self.block_align)
    }

    fn discard(&mut self, count: usize) {
        let discarded = self.buffered.min(self.align(count));
        self.read_position = (self.read_position + discarded) % self.bytes.len();
        self.buffered -= discarded;
        self.discarded_frames += (discarded / self.block_align) as u64;
    }

    fn record_occupancy(&mut self) {
        self.minimum_occupied = self.minimum_occupied.min(self.buffered);
        self.maximum_occupied = self.maximum_occupied.max(self.buffered);
        self.total_occupied = self.total_occupied.saturating_add(self.buffered as u64);
        self.occupancy_samples = self.occupancy_samples.saturating_add(1);
    }

    /// Returns aligned bytes accepted, excluding any leading bytes beyond
    /// capacity (the C# implementation also does not count these as discarded).
    pub fn write(&mut self, source: &[u8]) -> usize {
        let aligned = source.len() / self.block_align * self.block_align;
        if aligned == 0 {
            return 0;
        }
        let count = aligned.min(self.bytes.len());
        let from = aligned - count;

        if self.buffered + count > self.bytes.len() {
            self.overflows += 1;
            self.discard(self.buffered + count - self.bytes.len());
        }
        if self.buffered > self.correction_bytes {
            self.corrections += 1;
            self.discard(self.buffered.saturating_sub(self.target_bytes));
        }

        let first = count.min(self.bytes.len() - self.write_position);
        #[cfg(feature = "fastcopy")]
        {
            if count == first {
                self.bytes[self.write_position..self.write_position + count]
                    .copy_from_slice(&source[from..from + count]);
            } else {
                self.bytes[self.write_position..self.write_position + first]
                    .copy_from_slice(&source[from..from + first]);
                self.bytes[..count - first]
                    .copy_from_slice(&source[from + first..from + count]);
            }
        }
        #[cfg(not(feature = "fastcopy"))]
        {
            self.bytes[self.write_position..self.write_position + first]
                .copy_from_slice(&source[from..from + first]);
            self.bytes[..count - first]
                .copy_from_slice(&source[from + first..from + count]);
        }
        self.write_position = (self.write_position + count) % self.bytes.len();
        self.buffered += count;
        self.written_frames += (count / self.block_align) as u64;
        self.record_occupancy();
        count
    }

    /// Returns count bytes: actual data, or silence-padded count if read_fully.
    pub fn read(&mut self, output: &mut [u8]) -> usize {
        let count = output.len() / self.block_align * self.block_align;
        if count == 0 { return 0; }
        let taken = count.min(self.buffered);
        if taken == 0 && !self.read_fully { return 0; }

        let first = taken.min(self.bytes.len() - self.read_position);
        #[cfg(feature = "fastcopy")]
        {
            if first == taken {
                output[..first]
                    .copy_from_slice(&self.bytes[self.read_position..self.read_position + first]);
            } else {
                output[..first]
                    .copy_from_slice(&self.bytes[self.read_position..self.read_position + first]);
                output[first..taken].copy_from_slice(&self.bytes[..taken - first]);
            }
        }
        #[cfg(not(feature = "fastcopy"))]
        {
            output[..first]
                .copy_from_slice(&self.bytes[self.read_position..self.read_position + first]);
            output[first..taken].copy_from_slice(&self.bytes[..taken - first]);
        }
        self.read_position = (self.read_position + taken) % self.bytes.len();
        self.buffered -= taken;
        self.read_frames += (taken / self.block_align) as u64;

        if taken < count && self.count_underflows { self.underflows += 1; }
        let result = if self.read_fully {
            output[taken..count].fill(0);
            count
        } else {
            taken
        };

        self.record_occupancy();
        result
    }

    pub fn clear(&mut self) {
        self.read_position = 0;
        self.write_position = 0;
        self.buffered = 0;
        self.record_occupancy();
    }

    pub fn snapshot(&self) -> BufferSnapshot {
        BufferSnapshot {
            buffered_bytes: self.buffered as u64,
            target_bytes: self.target_bytes as u64,
            minimum_buffered_bytes: if self.minimum_occupied == usize::MAX {
                0
            } else { self.minimum_occupied as u64 },
            maximum_buffered_bytes: self.maximum_occupied as u64,
            average_buffered_bytes: self.total_occupied as f64
                / self.occupancy_samples.max(1) as f64,
            written_frames: self.written_frames,
            read_frames: self.read_frames,
            underflows: self.underflows,
            overflows: self.overflows,
            corrections: self.corrections,
            discarded_frames: self.discarded_frames,
        }
    }
}

/// An opaque, thread-safe mutex-protected handle for .NET P/Invoke.
pub struct BufferHandle(Mutex<PcmBuffer>);

#[no_mangle]
pub extern "C" fn ivc_buffer_create(
    capacity_bytes: usize, block_align: usize, target_bytes: usize,
    correction_bytes: usize, read_fully: i32, count_underflows: i32,
) -> *mut BufferHandle {
    PcmBuffer::new(capacity_bytes, block_align, target_bytes,
        correction_bytes, read_fully != 0, count_underflows != 0)
        .map(|buffer| Box::into_raw(Box::new(BufferHandle(Mutex::new(buffer)))))
        .unwrap_or(std::ptr::null_mut())
}

unsafe fn with_handle<R>(
    handle: *mut BufferHandle, action: impl FnOnce(&mut PcmBuffer) -> R,
) -> Option<R> {
    let owner = handle.as_ref()?;
    #[cfg(feature = "parking")]
    let mut guard = owner.0.lock();
    #[cfg(not(feature = "parking"))]
    let mut guard = owner.0.lock().unwrap_or_else(|poison| poison.into_inner());
    Some(action(&mut guard))
}

/// # Safety
/// \`handle\` must be valid and live; \`source\` must be valid for \`length\`
/// bytes and must not alias the buffer handle.
#[no_mangle]
pub unsafe extern "C" fn ivc_buffer_write(
    handle: *mut BufferHandle, source: *const u8, length: usize,
) -> usize {
    if source.is_null() || length == 0 { return 0; }
    with_handle(handle, |b| b.write(slice::from_raw_parts(source, length)))
        .unwrap_or(0)
}

/// # Safety
/// \`destination\` must be valid for \`length\` writable bytes, and the handle
/// must be valid/live.
#[no_mangle]
pub unsafe extern "C" fn ivc_buffer_read(
    handle: *mut BufferHandle, destination: *mut u8, length: usize,
) -> usize {
    if destination.is_null() || length == 0 { return 0; }
    with_handle(handle, |b| b.read(slice::from_raw_parts_mut(destination, length)))
        .unwrap_or(0)
}

/// # Safety
/// \`handle\` and \`snapshot\` must be valid, with a writable snapshot pointer.
#[no_mangle]
pub unsafe extern "C" fn ivc_buffer_snapshot(
    handle: *mut BufferHandle, snapshot: *mut BufferSnapshot,
) -> i32 {
    if snapshot.is_null() { return 0; }
    if let Some(value) = with_handle(handle, |b| b.snapshot()) {
        *snapshot = value;
        1
    } else { 0 }
}

/// # Safety
/// The caller owns the handle and must not free it while another thread is
/// operating on it. This operation is idempotent for null, not for a freed ptr.
#[no_mangle]
pub unsafe extern "C" fn ivc_buffer_free(handle: *mut BufferHandle) {
    if !handle.is_null() { drop(Box::from_raw(handle)); }
}

/// # Safety
/// \`handle\` must still be live.
#[no_mangle]
pub unsafe extern "C" fn ivc_buffer_clear(handle: *mut BufferHandle) {
    let _ = with_handle(handle, |b| b.clear());
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ring(capacity: usize, target: usize, correction: usize,
            read_fully: bool) -> PcmBuffer {
        PcmBuffer::new(capacity, 1, target, correction, read_fully, true).unwrap()
    }

    #[test]
    fn reject_invalid_capacity_and_alignment() {
        assert!(PcmBuffer::new(0, 4, 4, 4, false, true).is_none());
        assert!(PcmBuffer::new(10, 4, 4, 4, false, true).is_none());
        assert!(PcmBuffer::new(16, 0, 4, 4, false, true).is_none());
        assert!(PcmBuffer::new(16, 4, 5, 8, false, true).is_none());
        assert!(PcmBuffer::new(16, 4, 4, 17, false, true).is_none());
        assert!(PcmBuffer::new(MAX_CAPACITY_BYTES + 4, 4, 4, 4, false, true).is_none());
    }

    #[test]
    fn partial_read_alignment_and_fifo() {
        let mut b = PcmBuffer::new(16, 4, 4, 12, false, true).unwrap();
        assert_eq!(b.write(&[1, 2, 3, 4, 5, 6, 7, 8, 99]), 8);
        let mut out = [42u8; 9];
        assert_eq!(b.read(&mut out), 8);
        assert_eq!(out, [1, 2, 3, 4, 5, 6, 7, 8, 42]);
        assert_eq!(b.read(&mut out), 0);
        assert_eq!(b.snapshot().underflows, 0); // C# returns early if empty.
    }

    #[test]
    fn wraparound_preserves_bytes() {
        let mut b = ring(8, 2, 6, false);
        b.write(&[1, 2, 3, 4, 5, 6]);
        assert_eq!(b.read(&mut [0; 4]), 4);
        b.write(&[7, 8, 9, 10]);
        let mut out = [0; 6];
        assert_eq!(b.read(&mut out), 6);
        assert_eq!(out, [5, 6, 7, 8, 9, 10]);
    }

    #[test]
    fn overflow_discards_oldest_and_counts_once() {
        let mut b = ring(8, 2, 6, false);
        b.write(&[1, 2, 3, 4, 5, 6]);
        b.write(&[7, 8, 9, 10, 11]);
        let mut out = [0; 8];
        assert_eq!(b.read(&mut out), 8);
        assert_eq!(out, [4, 5, 6, 7, 8, 9, 10, 11]);
        assert_eq!(b.snapshot().discarded_frames, 3);
        assert_eq!(b.snapshot().overflows, 1);
    }

    #[test]
    fn oversized_input_keeps_latest_without_reporting_incoming_discard() {
        let mut b = ring(8, 2, 6, false);
        b.write(&(0u8..20).collect::<Vec<u8>>());
        let mut out = [0; 8];
        assert_eq!(b.read(&mut out), 8);
        assert_eq!(out, [12, 13, 14, 15, 16, 17, 18, 19]);
        assert_eq!(b.snapshot().discarded_frames, 0);
        assert_eq!(b.snapshot().written_frames, 8);
    }

    #[test]
    fn latency_correction_matches_csharp_trigger_before_write() {
        let mut b = ring(12, 2, 4, false);
        b.write(&[0, 1, 2, 3, 4, 5]);
        b.write(&[6]);
        let mut out = [0; 3];
        assert_eq!(b.read(&mut out), 3);
        assert_eq!(out, [4, 5, 6]);
        assert_eq!(b.snapshot().corrections, 1);
        assert_eq!(b.snapshot().discarded_frames, 4);
    }

    #[test]
    fn pad_zeros_and_count_underflow_only_if_requested() {
        let mut b = ring(16, 2, 8, true);
        b.write(&[4, 5]);
        let mut out = [99; 5];
        assert_eq!(b.read(&mut out), 5);
        assert_eq!(out, [4, 5, 0, 0, 0]);
        assert_eq!(b.snapshot().underflows, 1);
        assert_eq!(b.snapshot().read_frames, 2);
        let mut quiet = PcmBuffer::new(16, 1, 2, 8, true, false).unwrap();
        assert_eq!(quiet.read(&mut [8u8; 4]), 4);
        assert_eq!(quiet.snapshot().underflows, 0);
    }

    #[test]
    fn clear_keeps_counters_and_records_empty_occupancy() {
        let mut b = ring(16, 2, 8, false);
        b.write(&[1, 2, 3]);
        b.clear();
        assert_eq!(b.snapshot().written_frames, 3);
        assert_eq!(b.snapshot().buffered_bytes, 0);
        assert_eq!(b.snapshot().minimum_buffered_bytes, 0);
    }

    #[test]
    fn concurrent_producer_consumer_preserves_invariants() {
        use std::sync::{Arc, Barrier};
        const ROUNDS: usize = 20_000;
        let handle = Arc::new(BufferHandle(Mutex::new(
            PcmBuffer::new(8192, 8, 2048, 4096, true, true).unwrap())));
        let ready = Arc::new(Barrier::new(3));
        let producer_handle = Arc::clone(&handle);
        let producer_ready = Arc::clone(&ready);
        let producer = std::thread::spawn(move || {
            let input = [0xA5u8; 80];
            producer_ready.wait();
            for i in 0..ROUNDS {
                #[cfg(feature = "parking")]
                let mut guard = producer_handle.0.lock();
                #[cfg(not(feature = "parking"))]
                let mut guard = producer_handle.0.lock().unwrap();
                assert_eq!(guard.write(&input), 80);
                if i % 64 == 0 { std::thread::yield_now(); }
            }
        });
        let consumer_handle = Arc::clone(&handle);
        let consumer_ready = Arc::clone(&ready);
        let consumer = std::thread::spawn(move || {
            let mut output = [0xB1u8; 80];
            consumer_ready.wait();
            for i in 0..ROUNDS {
                #[cfg(feature = "parking")]
                let mut guard = consumer_handle.0.lock();
                #[cfg(not(feature = "parking"))]
                let mut guard = consumer_handle.0.lock().unwrap();
                assert_eq!(guard.read(&mut output), 80);
                if i % 64 == 0 { std::thread::yield_now(); }
            }
        });
        ready.wait();
        producer.join().unwrap();
        consumer.join().unwrap();
        #[cfg(feature = "parking")]
        let guard = handle.0.lock();
        #[cfg(not(feature = "parking"))]
        let guard = handle.0.lock().unwrap();
        let snapshot = guard.snapshot();
        assert!(snapshot.buffered_bytes <= 8192);
        assert_eq!(snapshot.written_frames, (ROUNDS * 10) as u64);
        assert!(snapshot.read_frames <= snapshot.written_frames);
        assert!(snapshot.discarded_frames <= snapshot.written_frames);
        assert!(snapshot.minimum_buffered_bytes <= snapshot.maximum_buffered_bytes);
    }

    #[test]
    fn ffi_roundtrip_and_rejects_null() {
        unsafe {
            let handle = ivc_buffer_create(32, 4, 8, 16, 1, 1);
            assert!(!handle.is_null());
            let input = [1u8, 2, 3, 4, 5, 6, 7, 8];
            assert_eq!(ivc_buffer_write(handle, input.as_ptr(), input.len()), 8);
            let mut out = [0; 12];
            assert_eq!(ivc_buffer_read(handle, out.as_mut_ptr(), out.len()), 12);
            assert_eq!(&out[..8], &input);
            assert_eq!(&out[8..], &[0; 4]);
            let mut s = BufferSnapshot::default();
            assert_eq!(ivc_buffer_snapshot(handle, &mut s), 1);
            assert_eq!(s.written_frames, 2);
            assert_eq!(s.read_frames, 2);
            assert_eq!(s.underflows, 1);
            assert_eq!(ivc_buffer_write(std::ptr::null_mut(), input.as_ptr(), 8), 0);
            ivc_buffer_clear(handle);
            ivc_buffer_free(handle);
            ivc_buffer_free(std::ptr::null_mut());
        }
    }
}
