#![cfg(target_os = "linux")]

use std::fs;
use std::os::unix::fs::PermissionsExt;
use std::process::{Command, Stdio};
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

/// A stopped/silent capture must not leave the virtual microphone behind
/// after --seconds expires. Uses fake pw-cat but real child pipes and signals.
#[test]
fn silent_microphone_stops_on_deadline_and_cleans_up() {
    let seed = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
    let dir = std::env::temp_dir().join(format!("itervc-idle-test-{}-{seed}", std::process::id()));
    fs::create_dir_all(&dir).unwrap();

    let pactl = dir.join("pactl");
    let pwcat = dir.join("pw-cat");
    let log = dir.join("modules.log");
    fs::write(
        &pactl,
        "#!/bin/sh\n\
         case \"$1\" in\n\
           info) exit 0 ;;\n\
           list) printf '42\\talsa_input.test\\tPipeWire\\n'; exit 0 ;;\n\
           load-module)\n\
             echo \"load:$2\" >> \"$ITERVC_TEST_LOG\"\n\
             if [ \"$2\" = module-null-sink ]; then echo 301; else echo 302; fi\n\
             exit 0 ;;\n\
           unload-module) echo \"unload:$2\" >> \"$ITERVC_TEST_LOG\"; exit 0 ;;\n\
         esac\n\
         exit 1\n",
    )
    .unwrap();
    fs::write(
        &pwcat,
        "#!/bin/sh\n\
         case \"$1\" in\n\
           --version) echo 'pw-cat mock'; exit 0 ;;\n\
           --record) exec sleep 30 ;;\n\
           --playback) cat > /dev/null; exit 0 ;;\n\
         esac\n\
         exit 1\n",
    )
    .unwrap();
    for file in [&pactl, &pwcat] {
        fs::set_permissions(file, fs::Permissions::from_mode(0o755)).unwrap();
    }

    let path = format!("{}:{}", dir.display(), std::env::var("PATH").unwrap_or_default());
    let mut child = Command::new(env!("CARGO_BIN_EXE_itervc-linux-pipewire"))
        .args(["--input", "alsa_input.test", "--seconds", "1"])
        .env("PATH", path)
        .env("ITERVC_TEST_LOG", &log)
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();

    // Bound the test itself so a regression cannot hang GitHub Actions.
    let deadline = Instant::now() + Duration::from_secs(6);
    let exit = loop {
        if let Some(status) = child.try_wait().unwrap() {
            break Some(status);
        }
        if Instant::now() >= deadline {
            let _ = child.kill();
            let _ = child.wait();
            break None;
        }
        thread::sleep(Duration::from_millis(50));
    };

    assert_eq!(exit.map(|status| status.success()), Some(true),
        "The prototype did not exit cleanly when the source stayed silent");
    assert_eq!(
        fs::read_to_string(&log).unwrap().lines().collect::<Vec<_>>(),
        vec!["load:module-null-sink", "load:module-remap-source", "unload:302", "unload:301"],
    );

    fs::remove_dir_all(&dir).unwrap();
}
