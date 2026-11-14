//! Optional, explicitly selected application-stream capture on PipeWire.
//!
//! A single PulseAudio-compatible playback stream is temporarily redirected
//! into an isolated sink. Its monitor is copied into IterVC's output mixer and
//! simultaneously replayed to the original physical sink. The session owner
//! must restore the application's original sink before unloading the module.
//! Never change any sink-input that is not explicitly selected.

use std::io::{self, Read, Write};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StreamRoute {
    pub stream_id: u32,
    pub original_sink_id: u32,
    pub original_sink_name: String,
    pub capture_sink_id: u32,
}

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

pub fn find_sink_id(text: &str, name: &str) -> Option<u32> {
    text.lines().find_map(|line| {
        let mut columns = line.split_whitespace();
        let this_id: u32 = columns.next()?.parse().ok()?;
        (columns.next()? == name).then_some(this_id)
    })
}

/// Decides whether a moved application still belongs to our capture sink.
/// We must not overwrite an output changed by the user during the session.
pub fn should_restore(current_inputs: &str, route: &StreamRoute) -> bool {
    parse_sink_inputs(current_inputs)
        .iter()
        .any(|entry| entry.id == route.stream_id && entry.sink_id == route.capture_sink_id)
}

/// Extract a selected playback stream's PipeWire node name from pactl's
/// human-readable listing. The command is run with LC_ALL=C by the caller.
/// We never guess a node from an application's display name or PID.
pub fn parse_pactl_node_name(text: &str, sink_input_id: u32) -> Option<String> {
    let section = format!("Sink Input #{sink_input_id}");
    let mut selected = false;
    for line in text.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with("Sink Input #") {
            selected = trimmed == section;
        } else if selected {
            if let Some(value) = trimmed.strip_prefix("node.name =") {
                let node = value.trim().trim_matches('"');
                if !node.is_empty() {
                    return Some(node.to_owned());
                }
            }
        }
    }
    None
}

/// Find matching 48 kHz stereo output ports without routing the original
/// playback away from its current speakers. pw-link supports fan-out to
/// multiple sinks. Refuse unknown layouts rather than guessing a connection.
pub fn capture_ports(node_name: &str, output_ports: &str, input_ports: &str,
    capture_sink_name: &str) -> Option<Vec<(String, String)>>
{
    let output: Vec<&str> = output_ports.lines().map(str::trim).collect();
    let inputs: Vec<&str> = input_ports.lines().map(str::trim).collect();
    let mut pairs = Vec::new();
    for (channel, accepted) in [
        ("playback_FL", ["output_FL", "output_AUX0"]),
        ("playback_FR", ["output_FR", "output_AUX1"]),
    ] {
        let destination = format!("{capture_sink_name}:{channel}");
        if !inputs.contains(&destination.as_str()) {
            return None;
        }
        let source = accepted.iter().map(|suffix| format!("{node_name}:{suffix}"))
            .find(|candidate| output.contains(&candidate.as_str()))?;
        pairs.push((source, destination));
    }
    Some(pairs)
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

/// PipeWire's own Port registry exposes stable, unique per-node output port
/// IDs, even if several streams have the same display name (e.g. "pacat").
/// A channel must be present exactly once; missing/ambiguous layouts fail
/// closed rather than tapping the wrong application.
pub fn find_output_port_ids(port_listing: &str, node_id: u32) -> Option<(u32, u32)> {
    #[derive(Default)]
    struct Port {
        id: Option<u32>,
        node_id: Option<u32>,
        direction_out: bool,
        name: String,
        channel: String,
    }
    fn finish(port: &Port, expected_node: u32, left: &mut Vec<u32>, right: &mut Vec<u32>) {
        if port.node_id != Some(expected_node) || !port.direction_out {
            return;
        }
        if let Some(id) = port.id {
            if port.name == "output_FL" || port.channel == "FL" {
                left.push(id);
            } else if port.name == "output_FR" || port.channel == "FR" {
                right.push(id);
            }
        }
    }

    let mut current = Port::default();
    let mut left = Vec::new();
    let mut right = Vec::new();
    for line in port_listing.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with("id ") && trimmed.contains("PipeWire:Interface:Port") {
            finish(&current, node_id, &mut left, &mut right);
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
    finish(&current, node_id, &mut left, &mut right);
    if left.len() == 1 && right.len() == 1 && left[0] != right[0] {
        Some((left[0], right[0]))
    } else {
        None
    }
}

/// Read raw 48 kHz stereo float32 PCM from the app monitor. Send the
/// gain-adjusted audio to IterVC's virtual microphone and the original audio
/// to its previous output device. An unavailable speaker must not interrupt
/// routing to the virtual microphone.
pub fn forward_app(
    mut source: impl Read,
    mut virtual_output: impl Write,
    mut original_output: impl Write,
    gain: f32,
) -> io::Result<u64> {
    if !gain.is_finite() || !(0.0..=2.0).contains(&gain) {
        return Err(io::Error::new(io::ErrorKind::InvalidInput, "Invalid app gain"));
    }
    const FRAME_BYTES: usize = 8;
    let mut data = [0u8; 4096];
    let mut altered = [0u8; 4096];
    let mut pending = Vec::with_capacity(4096 + FRAME_BYTES);
    let mut copied = 0u64;
    let mut speaker_available = true;

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

        virtual_output.write_all(&altered[..count])?;
        if speaker_available {
            if let Err(error) = original_output.write_all(&pending[..count]) {
                eprintln!("Application speaker passthrough stopped: {error}");
                speaker_available = false;
            }
        }
        copied += count as u64;
        pending.drain(..count);
    }
    Ok(copied)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sink_list_parsing_and_restore_only_while_owned() {
        let sinks = "2\talsa_output.a\tPipeWire\n8\titervc_rust_apps_sink\tPipeWire\n";
        assert_eq!(parse_sink_name(sinks, 2).as_deref(), Some("alsa_output.a"));
        assert_eq!(find_sink_id(sinks, "itervc_rust_apps_sink"), Some(8));
        assert_eq!(parse_sink_inputs("17\t2\t48\tPipeWire\n20\t8\t49\tPipeWire\n").len(), 2);
        let route = StreamRoute {
            stream_id: 20,
            original_sink_id: 2,
            original_sink_name: "alsa_output.a".into(),
            capture_sink_id: 8,
        };
        assert!(should_restore("20\t8\t49\tPipeWire\n", &route));
        assert!(!should_restore("20\t2\t49\tPipeWire\n", &route));
        assert!(!should_restore("19\t8\t49\tPipeWire\n", &route));
    }

    #[test]
    fn app_audio_reaches_both_outputs_with_independent_volume() {
        let samples = [0.5f32, -0.25, -0.5, 0.25];
        let input: Vec<u8> = samples.iter().flat_map(|value| value.to_le_bytes()).collect();
        let mut virtual_output = Vec::new();
        let mut original_output = Vec::new();
        let count = forward_app(&input[..], &mut virtual_output, &mut original_output, 0.5).unwrap();
        assert_eq!(count, input.len() as u64);
        assert_eq!(original_output, input);
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
        let mut speaker = Vec::new();
        forward_app(Chunks(std::io::Cursor::new(original.clone())),
            &mut virtual_output, &mut speaker, 0.0).unwrap();
        assert_eq!(speaker, original);
        assert_eq!(virtual_output, vec![0u8; original.len()]);
    }

    #[test]
    fn port_selection_keeps_physical_routes_untouched() {
        let pactl = "Sink Input #11\n  Properties:\n    node.name = \"Music App\"\n\
                     Sink Input #22\n  Properties:\n    node.name = \"Chat App\"\n";
        assert_eq!(parse_pactl_node_name(pactl, 11).as_deref(), Some("Music App"));
        assert_eq!(parse_pactl_node_name(pactl, 22).as_deref(), Some("Chat App"));
        assert_eq!(parse_pactl_node_name(pactl, 23), None);
        let outputs = "Music App:output_FL\nMusic App:output_FR\nChat App:output_FL\n";
        let inputs = "itervc_rust_apps_sink:playback_FL\nitervc_rust_apps_sink:playback_FR\n";
        assert_eq!(capture_ports("Music App", outputs, inputs, "itervc_rust_apps_sink").unwrap(),
            vec![
                ("Music App:output_FL".into(), "itervc_rust_apps_sink:playback_FL".into()),
                ("Music App:output_FR".into(), "itervc_rust_apps_sink:playback_FR".into())
            ]);
        assert!(capture_ports("Chat App", outputs, inputs, "itervc_rust_apps_sink").is_none());
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
    fn refuses_invalid_gain() {
        assert!(forward_app(&[][..], Vec::new(), Vec::new(), f32::NAN).is_err());
        assert!(forward_app(&[][..], Vec::new(), Vec::new(), -0.5).is_err());
        assert!(forward_app(&[][..], Vec::new(), Vec::new(), 2.1).is_err());
    }
}
