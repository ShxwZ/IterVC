#![cfg(target_os = "linux")]

use std::fs;
use std::os::unix::fs::PermissionsExt;
use std::process::Command;
use std::time::{SystemTime, UNIX_EPOCH};

/// Real child process and virtual-mic lifecycle test with mock PipeWire tools.
/// It needs no desktop audio server or physical microphone in GitHub Actions.
#[test]
fn virtual_mic_session_forwards_pcm_and_unloads_only_its_modules() {
    let id = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
    let dir = std::env::temp_dir().join(format!("itervc-pipewire-test-{}-{id}", std::process::id()));
    fs::create_dir_all(&dir).unwrap();

    let pactl = dir.join("pactl");
    let pwcat = dir.join("pw-cat");
    let data = dir.join("input.pcm");
    let output = dir.join("output.pcm");
    let module_log = dir.join("module.log");

    fs::write(
        &pactl,
        "#!/bin/sh\n\
         case \"$1\" in\n\
           info) exit 0 ;;\n\
           list) printf '42\\talsa_input.test\\tPipeWire\\n'; exit 0 ;;\n\
           load-module)\n\
             echo \"load:$2\" >> \"$ITERVC_TEST_LOG\"\n\
             if [ \"$2\" = module-null-sink ]; then echo 101; else echo 102; fi\n\
             exit 0 ;;\n\
           unload-module) echo \"unload:$2\" >> \"$ITERVC_TEST_LOG\"; exit 0 ;;\n\
         esac\n\
         exit 1\n",
    ).unwrap();

    fs::write(
        &pwcat,
        "#!/bin/sh\n\
         case \"$1\" in\n\
           --version) echo 'pw-cat test fake'; exit 0 ;;\n\
           --record)\n\
             for arg in \"$@\"; do [ \"$arg\" = --raw ] && RAW=1; done\n\
             [ \"$RAW\" = 1 ] || exit 71\n\
             cat \"$ITERVC_TEST_PCM\"; exit 0 ;;\n\
           --playback)\n\
             for arg in \"$@\"; do [ \"$arg\" = --raw ] && RAW=1; done\n\
             [ \"$RAW\" = 1 ] || exit 72\n\
             cat > \"$ITERVC_TEST_OUTPUT\"; exit 0 ;;\n\
         esac\n\
         exit 1\n",
    ).unwrap();
    fs::set_permissions(&pactl, fs::Permissions::from_mode(0o755)).unwrap();
    fs::set_permissions(&pwcat, fs::Permissions::from_mode(0o755)).unwrap();

    let original: Vec<u8> = (0..16000).map(|i| (i % 251) as u8).collect();
    assert_eq!(original.len() % 8, 0);
    fs::write(&data, &original).unwrap();

    let previous_path = std::env::var("PATH").unwrap_or_default();
    let path = format!("{}:{previous_path}", dir.display());
    let status = Command::new(env!("CARGO_BIN_EXE_itervc-linux-pipewire"))
        .args(["--input", "alsa_input.test", "--seconds", "2"])
        .env("PATH", path)
        .env("ITERVC_TEST_LOG", &module_log)
        .env("ITERVC_TEST_PCM", &data)
        .env("ITERVC_TEST_OUTPUT", &output)
        .status()
        .unwrap();
    assert!(status.success(), "PipeWire mock session returned failure");

    assert_eq!(fs::read(&output).unwrap(), original);
    
    // An unknown source must fail BEFORE allocating any virtual modules.
    let rejected = Command::new(env!("CARGO_BIN_EXE_itervc-linux-pipewire"))
        .args(["--input", "not-a-device", "--seconds", "1"])
        .env("PATH", format!("{}:{}", dir.display(), std::env::var("PATH").unwrap_or_default()))
        .env("ITERVC_TEST_LOG", &module_log)
        .output()
        .unwrap();
    assert!(!rejected.status.success());
    assert!(String::from_utf8_lossy(&rejected.stderr).contains("was not found"));

    let log = fs::read_to_string(&module_log).unwrap();
    assert_eq!(
        log.lines().collect::<Vec<_>>(),
        vec![
            "load:module-null-sink",
            "load:module-remap-source",
            "unload:102",
            "unload:101",
        ],
        "Temporary modules must be unloaded in reverse creation order"
    );

    fs::remove_dir_all(&dir).unwrap();
}
