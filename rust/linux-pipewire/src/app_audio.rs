//! Copy opted-in PipeWire playback streams into the virtual microphone.
//! Native PipeWire links fan out each selected app's original audio to a
//! temporary capture sink, leaving the physical speaker connections intact.
//! This Rust stage adjusts only the copy destined for IterVC's microphone.

use std::io::{self, Read, Write};
use std::sync::atomic::{AtomicU32, Ordering};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SinkInput {
    pub id: u32,
    pub sink_id: u32,
}

pub fn parse_sink_inputs(text: &str) -> Vec<SinkInput> {
    text.lines()
        .filter_map(|line| {
            let mut columns = line.split_whitespace();
            Some(SinkInput {
                id: columns.next()?.parse().ok()?,
                sink_id: columns.next()?.parse().ok()?,
            })
        })
        .collect()
}

pub fn parse_sink_name(text: &str, id: u32) -> Option<String> {
    text.lines().find_map(|line| {
        let mut columns = line.split_whitespace();
        let this_id: u32 = columns.next()?.parse().ok()?;
        let name = columns.next()?;
        (this_id == id).then(|| name.to_owned())
    })
}

/// The PulseAudio sink-input index is its PipeWire object serial, not the
/// PipeWire node ID. Read the object.id property from that exact section
/// instead of relying on the non-unique node.name or an application PID.
pub fn parse_pactl_node_id(text: &str, sink_input_id: u32) -> Option<u32> {
    let section = format!("Sink Input #{sink_input_id}");
    let mut selected = false;
    for line in text.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with("Sink Input #") {
            selected = trimmed == section;
        } else if selected {
            if let Some(value) = trimmed.strip_prefix("object.id =") {
                return value.trim().trim_matches('"').parse().ok();
            }
        }
    }
    None
}

/// Identify the process behind a PulseAudio playback stream when available.
/// IterVC must not capture one of its own speaker-monitor subprocesses:
/// doing so would create an audio feedback loop.
pub fn parse_pactl_process_id(text: &str, sink_input_id: u32) -> Option<u32> {
    let section = format!("Sink Input #{sink_input_id}");
    let mut selected = false;
    for line in text.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with("Sink Input #") {
            selected = trimmed == section;
        } else if selected {
            if let Some(value) = trimmed.strip_prefix("application.process.id =") {
                return value.trim().trim_matches('"').parse().ok();
            }
        }
    }
    None
}

/// PipeWire's own Port registry exposes stable, unique per-node output port
/// IDs, even if several streams have the same display name (e.g. "pacat").
/// Stereo needs uniquely identifiable FL/FR ports. A genuine mono port may
/// feed *both* capture channels, yielding centered mono audio. Ambiguous
/// layouts still fail closed rather than capturing the wrong application.
pub fn find_output_port_ids(port_listing: &str, node_id: u32) -> Option<(u32, u32)> {
    #[derive(Default)]
    struct Port {
        id: Option<u32>,
        node_id: Option<u32>,
        direction_out: bool,
        name: String,
        channel: String,
    }
    fn finish(port: &Port, expected_node: u32, left: &mut Vec<u32>,
        right: &mut Vec<u32>, mono: &mut Vec<u32>) {
        if port.node_id != Some(expected_node) || !port.direction_out {
            return;
        }
        if let Some(id) = port.id {
            if port.name == "output_FL" || port.channel == "FL" {
                left.push(id);
            } else if port.name == "output_FR" || port.channel == "FR" {
                right.push(id);
            } else if port.name == "output_MONO" || port.channel == "MONO" {
                mono.push(id);
            }
        }
    }

    let mut current = Port::default();
    let mut left = Vec::new();
    let mut right = Vec::new();
    let mut mono = Vec::new();
    for line in port_listing.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with("id ") && trimmed.contains("PipeWire:Interface:Port") {
            finish(&current, node_id, &mut left, &mut right, &mut mono);
            current = Port::default();
            current.id = trimmed.strip_prefix("id ")
                .and_then(|value| value.split(',').next())
                .and_then(|value| value.trim().parse::<u32>().ok());
            continue;
        }
        let Some((key, value)) = trimmed.split_once('=') else { continue };
        let value = value.trim().trim_matches('"');
        match key.trim() {
            "node.id" => current.node_id = value.parse().ok(),
            "port.direction" => current.direction_out = value == "out",
            "port.name" => current.name = value.to_owned(),
            "audio.channel" => current.channel = value.to_owned(),
            _ => {}
        }
    }
    finish(&current, node_id, &mut left, &mut right, &mut mono);
    if left.len() == 1 && right.len() == 1 && left[0] != right[0] && mono.is_empty() {
        Some((left[0], right[0]))
    } else if mono.len() == 1 && left.is_empty() && right.is_empty() {
        Some((mono[0], mono[0]))
    } else {
        None
    }
}

/// Read raw 48 kHz stereo float32 PCM from the isolated app tap, apply
/// independent mix gain, and copy to the final virtual microphone. PipeWire
/// simultaneously preserves the application's original speaker connections.
#[cfg(test)]
pub fn forward_app(
    source: impl Read,
    virtual_output: impl Write,
    gain: f32,
) -> io::Result<u64> {
    if !gain.is_finite() || !(0.0..=2.0).contains(&gain) {
        return Err(io::Error::new(io::ErrorKind::InvalidInput, "Invalid app gain"));
    }
    let shared = AtomicU32::new(gain.to_bits());
    forward_app_live(source, virtual_output, &shared)
}

/// The selected app streams stay linked while their *virtual copy* gain
/// changes. The physical speaker connections never depend on this value.
pub fn forward_app_live(
    mut source: impl Read,
    mut virtual_output: impl Write,
    gain_bits: &AtomicU32,
) -> io::Result<u64> {
    const FRAME_BYTES: usize = 8;
    let mut data = [0u8; 4096];
    let mut altered = [0u8; 4096];
    let mut pending = Vec::with_capacity(4096 + FRAME_BYTES);
    let mut copied = 0u64;

    loop {
        let length = match source.read(&mut data) {
            Ok(0) => break,
            Ok(count) => count,
            Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
            Err(error) => return Err(error),
        };
        pending.extend_from_slice(&data[..length]);
        let count = pending.len() / FRAME_BYTES * FRAME_BYTES;
        if count == 0 {
            continue;
        }
        let gain = f32::from_bits(gain_bits.load(Ordering::Relaxed));
        if !gain.is_finite() || !(0.0..=2.0).contains(&gain) {
            return Err(io::Error::new(io::ErrorKind::InvalidInput, "Invalid live app gain"));
        }
        if gain == 0.0 {
            // Write canonical PCM silence, not negative zero (which is also
            // a valid IEEE float but breaks byte-exact mute checks).
            altered[..count].fill(0);
        } else {
            for (out, input) in altered[..count]
                .chunks_exact_mut(std::mem::size_of::<f32>())
                .zip(pending[..count].chunks_exact(std::mem::size_of::<f32>()))
            {
                let sample = f32::from_le_bytes(input.try_into().unwrap());
                let processed = if sample.is_finite() {
                    (sample * gain).clamp(-1.0, 1.0)
                } else {
                    0.0
                };
                out.copy_from_slice(&processed.to_le_bytes());
            }
        }

        virtual_output.write_all(&altered[..count])?;
        copied += count as u64;
        pending.drain(..count);
    }
    Ok(copied)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn app_list_parsing_handles_multiple_outputs_without_changing_them() {
        let sinks = "2\talsa_output.a\tPipeWire\n8\titervc_rust_apps_sink\tPipeWire\n";
        assert_eq!(parse_sink_name(sinks, 2).as_deref(), Some("alsa_output.a"));
        assert_eq!(parse_sink_inputs("17\t2\t48\tPipeWire\n20\t8\t49\tPipeWire\n"),
            vec![SinkInput { id: 17, sink_id: 2 }, SinkInput { id: 20, sink_id: 8 }]);
    }

    #[test]
    fn app_audio_is_gain_adjusted_only_in_virtual_microphone() {
        let samples = [0.5f32, -0.25, -0.5, 0.25];
        let input: Vec<u8> = samples.iter().flat_map(|value| value.to_le_bytes()).collect();
        let mut virtual_output = Vec::new();
        let count = forward_app(&input[..], &mut virtual_output, 0.5).unwrap();
        assert_eq!(count, input.len() as u64);
        let processed: Vec<f32> = virtual_output.chunks_exact(4)
            .map(|item| f32::from_le_bytes(item.try_into().unwrap())).collect();
        assert_eq!(processed, [0.25, -0.125, -0.25, 0.125]);
    }

    #[test]
    fn non_aligned_app_stream_is_framed_and_clipped() {
        struct Chunks(std::io::Cursor<Vec<u8>>);
        impl Read for Chunks {
            fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
                let limit = buffer.len().min(3);
                self.0.read(&mut buffer[..limit])
            }
        }
        let original: Vec<u8> = (0..48).map(|i| (i % 127) as u8).collect();
        let mut virtual_output = Vec::new();
        forward_app(Chunks(std::io::Cursor::new(original.clone())),
            &mut virtual_output, 0.0).unwrap();
        assert_eq!(virtual_output, vec![0u8; original.len()]);
    }

    #[test]
    fn duplicate_app_names_are_distinguished_by_pipewire_object_id() {
        let inputs = "Sink Input #119\n  Properties:\n    node.name = \"pacat\"\n\
                      object.id = \"60\"\n    object.serial = \"119\"\n\
                      Sink Input #212\n  Properties:\n    node.name = \"pacat\"\n\
                      object.id = \"67\"\n    object.serial = \"212\"\n";
        assert_eq!(parse_pactl_node_id(inputs, 119), Some(60));
        assert_eq!(parse_pactl_node_id(inputs, 212), Some(67));
        assert_eq!(parse_pactl_node_id(inputs, 999), None);
    }

    #[test]
    fn port_ids_map_to_unique_nodes_even_with_identical_names() {
        let ports = "id 64, type PipeWire:Interface:Port/3\n\
                         node.id = \"60\"\n    port.name = \"output_FR\"\n\
                         port.direction = \"out\"\n\
                     id 65, type PipeWire:Interface:Port/3\n\
                         node.id = \"60\"\n    port.name = \"output_FL\"\n\
                         port.direction = \"out\"\n\
                     id 72, type PipeWire:Interface:Port/3\n\
                         node.id = \"67\"\n    port.name = \"output_FL\"\n\
                         port.direction = \"out\"\n\
                     id 73, type PipeWire:Interface:Port/3\n\
                         node.id = \"67\"\n    port.name = \"output_FR\"\n\
                         port.direction = \"out\"\n";
        assert_eq!(find_output_port_ids(ports, 60), Some((65, 64)));
        assert_eq!(find_output_port_ids(ports, 67), Some((72, 73)));
        assert_eq!(find_output_port_ids(ports, 88), None);
    }

    #[test]
    fn process_lookup_is_scoped_to_selected_playback_stream() {
        let text = "Sink Input #45\n  Properties:\n    application.process.id = \"4242\"\n\
                    Sink Input #62\n  Properties:\n    application.process.id = \"8080\"\n";
        assert_eq!(parse_pactl_process_id(text, 45), Some(4242));
        assert_eq!(parse_pactl_process_id(text, 62), Some(8080));
        assert_eq!(parse_pactl_process_id(text, 999), None);
    }

    #[test]
    fn mono_application_port_is_copied_to_both_virtual_mix_channels() {
        let ports = "id 35, type PipeWire:Interface:Port/3\n\
                     node.id = \"42\"\n    port.name = \"output_MONO\"\n\
                     port.direction = \"out\"\n    audio.channel = \"MONO\"\n";
        assert_eq!(find_output_port_ids(ports, 42), Some((35, 35)));
        assert_eq!(find_output_port_ids(ports, 99), None);
    }

    #[test]
    fn ambiguous_or_mono_layouts_are_rejected() {
        let ports = "id 30, type PipeWire:Interface:Port/3\n\
                         node.id = \"7\"\n    port.name = \"output_FL\"\n\
                         port.direction = \"out\"\n\
                     id 31, type PipeWire:Interface:Port/3\n\
                         node.id = \"7\"\n    port.name = \"output_FL\"\n\
                         port.direction = \"out\"\n\
                     id 32, type PipeWire:Interface:Port/3\n\
                         node.id = \"7\"\n    port.name = \"output_FR\"\n\
                         port.direction = \"out\"\n";
        assert!(find_output_port_ids(ports, 7).is_none());
    }

    #[test]
    fn live_gain_mutes_only_virtual_copy_without_reconnecting_stream() {
        let source: Vec<u8> = [0.4f32, -0.4].iter()
            .flat_map(|x| x.to_le_bytes()).collect();
        let shared = AtomicU32::new(0.0f32.to_bits());
        let mut output = Vec::new();
        forward_app_live(source.as_slice(), &mut output, &shared).unwrap();
        assert_eq!(output, vec![0u8; source.len()]);
        shared.store(0.5f32.to_bits(), Ordering::Relaxed);
        output.clear();
        forward_app_live(source.as_slice(), &mut output, &shared).unwrap();
        let left = f32::from_le_bytes(output[..4].try_into().unwrap());
        assert!((left - 0.2).abs() < 1e-6);
        assert_eq!(source, [0.4f32, -0.4].iter()
            .flat_map(|x| x.to_le_bytes()).collect::<Vec<_>>());
    }

    #[test]
    fn refuses_invalid_gain() {
        assert!(forward_app(&[][..], Vec::new(), f32::NAN).is_err());
        assert!(forward_app(&[][..], Vec::new(), -0.5).is_err());
        assert!(forward_app(&[][..], Vec::new(), 2.1).is_err());
    }
}
