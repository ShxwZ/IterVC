#!/usr/bin/env python3
"""Same-runner Rust variant leaderboard, no invented absolute latency claims."""
import json
import math
import pathlib
import statistics
import sys

base = pathlib.Path(sys.argv[1])
variants = ["std", "std-fast", "parking", "parking-fast"]
rows = []
for variant in variants:
    path = base / variant / "results.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    if data["Candidate"] != variant:
        raise SystemExit(f"Wrong candidate label in {path}")
    comparisons = data["Comparisons"]
    if len(comparisons) != 3:
        raise SystemExit(f"Missing scenarios in {path}")
    geomean = statistics.geometric_mean(
        c["RustSpeedupVsCSharp"] for c in comparisons
    )
    thread = {r["Backend"]: r for r in data["Threaded"]}
    cs = thread["C# original"]
    rs = thread["Rust + P/Invoke"]
    rows.append(dict(
        candidate=variant,
        geo_speedup=round(geomean, 3),
        speedups={c["Scenario"]: round(c["RustSpeedupVsCSharp"], 3)
                  for c in comparisons},
        cs_read_p99_us=cs["ReadP99Us"],
        rust_read_p99_us=rs["ReadP99Us"],
        cs_write_p99_us=cs["WriteP99Us"],
        rust_write_p99_us=rs["WriteP99Us"],
        rust_thread_alloc_bytes_per_op=rs["WriterAllocBytesPerOperation"] +
                                           rs["ReaderAllocBytesPerOperation"],
        rust_thread_wall_ms=rs["WallMs"],
        cs_thread_wall_ms=cs["WallMs"],
    ))
rows.sort(key=lambda r: r["geo_speedup"], reverse=True)
lines = [
    "## Isolated Rust variants compared on this runner",
    "",
    "Each row compiles the SAME buffer algorithm with a different lock/copy combination.",
    "All candidates passed PCM and counter equivalence against the unmodified C# production code.",
    "Rust/C# ratios above 1 favor Rust *for the microbenchmark only*.",
    "",
    "| Rank | Variant | Geometric mean speedup | 1ms | 10ms | bursts | Writer p99 (Rust/C#, us) | Reader p99 (Rust/C#, us) |",
    "|---:|---|---:|---:|---:|---:|---:|---:|",
]
for rank, r in enumerate(rows, 1):
    sp = r["speedups"]
    lines.append(
        f"| {rank} | {r['candidate']} | {r['geo_speedup']:.2f}x | "
        f"{sp['steady-1ms']:.2f}x | {sp['steady-10ms']:.2f}x | {sp['bursty-10ms']:.2f}x | "
        f"{r['rust_write_p99_us']:.2f}/{r['cs_write_p99_us']:.2f} | "
        f"{r['rust_read_p99_us']:.2f}/{r['cs_read_p99_us']:.2f} |"
    )
lines += [
    "",
    "These p99 values come from a short producer/consumer contention stress test, not timed audio callbacks.",
    "They include Stopwatch instrumentation and shared-runner scheduling noise.",
    "**The fastest microbenchmark variant is not automatically the lowest-latency or safest product choice.**",
    "Repeat on real Windows hardware and under controlled contention before choosing a production implementation.",
    "",
]
(base / "leaderboard.md").write_text("\n".join(lines), encoding="utf-8")
(base / "leaderboard.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
print("\n".join(lines))
