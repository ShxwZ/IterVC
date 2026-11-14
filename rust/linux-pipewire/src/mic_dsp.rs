//! Platform-independent microphone gain and noise gate processing.
//! This stage applies only to the microphone path; application audio is mixed
//! later by PipeWire. Windows retains its existing NAudio gate implementation.

use std::io;

const SAMPLE_RATE: f32 = 48_000.0;
const FRAME_BYTES: usize = 8;

#[derive(Clone, Copy, Debug)]
pub struct MicrophoneConfig {
    pub volume: f32,
    pub gate_enabled: bool,
    pub gate_threshold_db: f32,
    pub attack_ms: f32,
    pub release_ms: f32,
}

impl Default for MicrophoneConfig {
    fn default() -> Self {
        Self {
            volume: 1.0,
            gate_enabled: false,
            gate_threshold_db: -45.0,
            attack_ms: 10.0,
            release_ms: 150.0,
        }
    }
}

impl MicrophoneConfig {
    pub fn validate(self) -> io::Result<Self> {
        if !self.volume.is_finite() || !(0.0..=3.0).contains(&self.volume) {
            return Err(io::Error::new(io::ErrorKind::InvalidInput,
                "Microphone volume must be between 0.0 and 3.0"));
        }
        if !self.gate_threshold_db.is_finite()
            || !(-100.0..=0.0).contains(&self.gate_threshold_db) {
            return Err(io::Error::new(io::ErrorKind::InvalidInput,
                "Gate threshold must be between -100 and 0 dBFS"));
        }
        if !self.attack_ms.is_finite() || !(1.0..=1000.0).contains(&self.attack_ms)
            || !self.release_ms.is_finite() || !(1.0..=2000.0).contains(&self.release_ms) {
            return Err(io::Error::new(io::ErrorKind::InvalidInput,
                "Invalid gate attack or release time"));
        }
        Ok(self)
    }
}

pub struct MicrophoneProcessor {
    settings: MicrophoneConfig,
    threshold: f32,
    gate_gain: f32,
    attack_rate: f32,
    release_rate: f32,
}

impl MicrophoneProcessor {
    pub fn new(settings: MicrophoneConfig) -> io::Result<Self> {
        let settings = settings.validate()?;
        let threshold = 10f32.powf(settings.gate_threshold_db / 20.0);
        Ok(Self {
            settings,
            threshold,
            gate_gain: if settings.gate_enabled { 0.0 } else { 1.0 },
            attack_rate: 1.0 / (SAMPLE_RATE * settings.attack_ms / 1000.0),
            release_rate: 1.0 / (SAMPLE_RATE * settings.release_ms / 1000.0),
        })
    }

    pub fn set_volume(&mut self, volume: f32) -> io::Result<()> {
        if !volume.is_finite() || !(0.0..=3.0).contains(&volume) {
            return Err(io::Error::new(io::ErrorKind::InvalidInput, "Invalid live mic gain"));
        }
        self.settings.volume = volume;
        Ok(())
    }

    /// Update the full gate state between PCM buffers, without restarting
    /// PipeWire or discarding microphone buffers. Settings are validated as
    /// a single unit so invalid live controls cannot corrupt DSP state.
    pub fn set_gate(&mut self, enabled: bool, threshold_db: f32,
                    attack_ms: f32, release_ms: f32) -> io::Result<()> {
        let next = MicrophoneConfig {
            volume: self.settings.volume,
            gate_enabled: enabled,
            gate_threshold_db: threshold_db,
            attack_ms,
            release_ms,
        }.validate()?;
        self.settings = next;
        self.threshold = 10f32.powf(threshold_db / 20.0);
        self.attack_rate = 1.0 / (SAMPLE_RATE * attack_ms / 1000.0);
        self.release_rate = 1.0 / (SAMPLE_RATE * release_ms / 1000.0);
        Ok(())
    }

    /// Process interleaved 48 kHz stereo float32 little-endian frames.
    /// Bypass is byte-for-byte transparent to avoid changing audio when
    /// the user has not enabled gate or gain.
    pub fn apply(&mut self, data: &mut [u8]) {
        if self.settings.volume == 1.0 && !self.settings.gate_enabled {
            return;
        }
        for frame in data.chunks_exact_mut(FRAME_BYTES) {
            let left = f32::from_le_bytes(frame[..4].try_into().unwrap());
            let right = f32::from_le_bytes(frame[4..8].try_into().unwrap());
            let level = left.abs().max(right.abs());
            let target = if !self.settings.gate_enabled
                || (level.is_finite() && level >= self.threshold) { 1.0 } else { 0.0 };
            let step = if target > self.gate_gain {
                self.attack_rate
            } else {
                self.release_rate
            };
            if self.gate_gain < target {
                self.gate_gain = (self.gate_gain + step).min(target);
            } else if self.gate_gain > target {
                self.gate_gain = (self.gate_gain - step).max(target);
            }

            let amp = self.gate_gain * self.settings.volume;
            for (offset, sample) in [(0, left), (4, right)] {
                let result = if !sample.is_finite() || amp == 0.0 {
                    // Canonical zero even for negative input samples while muted.
                    0.0
                } else {
                    (sample * amp).clamp(-1.0, 1.0)
                };
                frame[offset..offset + 4].copy_from_slice(&result.to_le_bytes());
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn stereo(value: f32, frames: usize) -> Vec<u8> {
        (0..frames).flat_map(|_| value.to_le_bytes().into_iter().chain(value.to_le_bytes()))
            .collect()
    }
    fn first(bytes: &[u8]) -> f32 {
        f32::from_le_bytes(bytes[..4].try_into().unwrap())
    }

    #[test]
    fn disabled_processing_is_byte_identical() {
        let mut input = stereo(0.21, 100);
        let original = input.clone();
        MicrophoneProcessor::new(MicrophoneConfig::default()).unwrap().apply(&mut input);
        assert_eq!(input, original);
    }

    #[test]
    fn volume_and_clipping_are_safe() {
        let mut config = MicrophoneConfig::default();
        config.volume = 2.0;
        let mut pcm = stereo(0.75, 24);
        MicrophoneProcessor::new(config).unwrap().apply(&mut pcm);
        assert_eq!(first(&pcm), 1.0);

        config.volume = 0.5;
        let mut pcm = stereo(0.6, 8);
        MicrophoneProcessor::new(config).unwrap().apply(&mut pcm);
        assert!((first(&pcm) - 0.3).abs() < 1e-6);
    }

    #[test]
    fn live_gate_controls_are_validated_and_apply_without_restart() {
        let mut processor = MicrophoneProcessor::new(MicrophoneConfig::default()).unwrap();
        let mut pcm = stereo(0.02, 600);
        processor.apply(&mut pcm);
        assert!((first(&pcm) - 0.02).abs() < 1e-6);

        processor.set_gate(true, -10.0, 2.0, 30.0).unwrap();
        let mut quiet = stereo(0.02, 3000);
        processor.apply(&mut quiet);
        let tail = f32::from_le_bytes(
            quiet[quiet.len() - 8..quiet.len() - 4].try_into().unwrap());
        assert_eq!(tail, 0.0);

        // Change the threshold while running: the same input now opens it.
        processor.set_gate(true, -45.0, 2.0, 30.0).unwrap();
        let mut loud = stereo(0.02, 500);
        processor.apply(&mut loud);
        let tail = f32::from_le_bytes(
            loud[loud.len() - 8..loud.len() - 4].try_into().unwrap());
        assert!((tail - 0.02).abs() < 0.001);

        assert!(processor.set_gate(true, f32::NAN, 2.0, 30.0).is_err());
        assert!(processor.set_gate(true, -20.0, 0.0, 30.0).is_err());
        assert!(processor.set_gate(true, -20.0, 2.0, f32::INFINITY).is_err());
        processor.set_gate(false, -45.0, 2.0, 30.0).unwrap();
        let mut dry = stereo(0.001, 700);
        processor.apply(&mut dry);
        assert!((f32::from_le_bytes(
            dry[dry.len() - 8..dry.len() - 4].try_into().unwrap()) - 0.001).abs() < 0.0001);
    }

    #[test]
    fn gate_closes_when_quiet_and_opens_smoothly() {
        let config = MicrophoneConfig { gate_enabled: true, ..MicrophoneConfig::default() };
        let mut gate = MicrophoneProcessor::new(config).unwrap();
        let mut quiet = stereo(0.001, 5000);
        gate.apply(&mut quiet);
        assert_eq!(first(&quiet), 0.0);

        let mut loud = stereo(0.4, 2000);
        gate.apply(&mut loud);
        assert!(first(&loud) > 0.0 && first(&loud) < 0.4);
        let last = f32::from_le_bytes(loud[loud.len() - 8..loud.len() - 4].try_into().unwrap());
        assert!((last - 0.4).abs() < 0.005);

        let mut quiet = stereo(0.001, 12000);
        gate.apply(&mut quiet);
        let last = f32::from_le_bytes(quiet[quiet.len() - 8..quiet.len() - 4].try_into().unwrap());
        assert!(last < 0.001);
    }

    #[test]
    fn live_gain_mutes_and_restores_microphone_without_resetting_gate() {
        let mut processor = MicrophoneProcessor::new(MicrophoneConfig::default()).unwrap();
        processor.set_volume(0.0).unwrap();
        let mut muted = stereo(0.5, 512);
        processor.apply(&mut muted);
        assert!(muted.iter().all(|byte| *byte == 0));
        let mut negative = stereo(-0.5, 512);
        processor.apply(&mut negative);
        assert!(negative.iter().all(|byte| *byte == 0));
        processor.set_volume(1.0).unwrap();
        let mut restored = stereo(0.5, 512);
        processor.apply(&mut restored);
        assert!((first(&restored) - 0.5).abs() < 1e-6);
        assert!(processor.set_volume(f32::NAN).is_err());
    }

    #[test]
    fn rejects_nonfinite_or_invalid_configuration() {
        let mut settings = MicrophoneConfig::default();
        settings.volume = f32::NAN;
        assert!(MicrophoneProcessor::new(settings).is_err());
        settings.volume = 1.0;
        settings.gate_threshold_db = -125.0;
        assert!(MicrophoneProcessor::new(settings).is_err());
        settings.gate_threshold_db = -40.0;
        settings.release_ms = 0.0;
        assert!(MicrophoneProcessor::new(settings).is_err());
    }
}
