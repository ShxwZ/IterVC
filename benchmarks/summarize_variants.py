#!/usr/bin/env python3
"""Rank same-runner Rust candidates against original AND optimized C#.

Each candidate runs twice, once forward and once in reverse. Never infer
a hardware/audio latency win from these short virtualized tests.
"""
import json
import pathlib
import statistics
import sys

base = pathlib.Path(sys.argv[1])
variants = ["std", "std-fast", "parking", "parking-fast"]
passes = ["pass-0", "pass-1"]
rows = []

for variant in variants:
    reports = []
    for round_dir in passes:
        path = base / round_dir / variant / "results.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        if data["Candidate"] != variant:
            raise SystemExit(f"Variant label mismatch: {path}")
        if len(data["Comparisons"]) != 3:
            raise SystemExit(f"Missing benchmark scenarios: {path}")
        if len(data["Threaded"]) != 2 or len(data["Paced"]) != 3:
            raise SystemExit(f"Missing producer/consumer or paced tests: {path}")
        reports.append(data)

    original = []
    optimized = []
    for data in reports:
        original.extend(c["RustSpeedupVsCSharp"] for c in data["Comparisons"])
        optimized.extend(c["RustSpeedupVsOptimizedCSharp"] for c in data["Comparisons"])

    def med(metric, key="Rust + P/Invoke", field="Threaded"):
        return statistics.median(
            next(x[metric] for x in report[field] if x["Backend"] == key)
            for report in reports
        )

    rows.append(dict(
        candidate=variant,
        geo_vs_original=round(statistics.geometric_mean(original), 4),
        geo_vs_span_csharp=round(statistics.geometric_mean(optimized), 4),
        min_vs_span_csharp=round(min(optimized), 4),
        rust_thread_write_p99_us=round(med("WriteP99Us"), 3),
        rust_thread_read_p99_us=round(med("ReadP99Us"), 3),
        cs_thread_write_p99_us=round(med("WriteP99Us", "C# original"), 3),
        cs_thread_read_p99_us=round(med("ReadP99Us", "C# original"), 3),
        rust_paced_write_p99_us=round(med("WriteP99Us", field="Paced"), 3),
        rust_paced_read_p99_us=round(med("ReadP99Us", field="Paced"), 3),
        cs_paced_write_p99_us=round(med("WriteP99Us", "C# original", "Paced"), 3),
        cs_paced_read_p99_us=round(med("ReadP99Us", "C# original", "Paced"), 3),
        span_paced_read_p99_us=round(med("ReadP99Us", "Span-optimized C#", "Paced"), 3),
        rust_writer_alloc_bytes_per_op=round(med("WriterAllocBytesPerOperation"), 3),
        rust_reader_alloc_bytes_per_op=round(med("ReaderAllocBytesPerOperation"), 3),
    ))

rows.sort(key=lambda x: x["geo_vs_span_csharp"], reverse=True)
winner = rows[0]
# Differences <=5% on hosted CI are not defensible evidence for an extra
# dependency. Prefer the std lock if it is close to the fastest result.
standard = next(r for r in rows if r["candidate"] == "std")
recommended = (standard if standard["geo_vs_span_csharp"] >=
               winner["geo_vs_span_csharp"] * 0.95 else winner)
lines = [
    "## Isolated Rust candidates — original C# and optimized Span C#",
    "",
    "4 variants × 2 opposite orders × 5 measured trials × 3 scenarios. "
    "All runs passed exact PCM, snapshot and stress-test invariants.",
    "",
    "| Rank | Rust variant | vs original C# | vs Span C# | Contention writer p99 (µs) | Contention reader p99 (µs) | Paced writer p99 (µs) | Paced reader p99 (µs) |",
    "|---:|---|---:|---:|---:|---:|---:|---:|",
]
for i, r in enumerate(rows, 1):
    lines.append(
        f"| {i} | {r['candidate']} | {r['geo_vs_original']:.2f}× | "
        f"{r['geo_vs_span_csharp']:.2f}× | "
        f"{r['rust_thread_write_p99_us']:.2f} | {r['rust_thread_read_p99_us']:.2f} | "
        f"{r['rust_paced_write_p99_us']:.2f} | {r['rust_paced_read_p99_us']:.2f} |"
    )
lines += [
    "",
    f"**Exploratory recommendation on this runner: {recommended['candidate']}.** "
    "When within 5% of the fastest microbenchmark candidate, the standard "
    "mutex is preferred for less complexity. This is NOT a production sign-off.",
    "",
    "**Caveats:** Virtual machines, CPU scheduling and per-callback measurement "
    "overhead dominate extreme percentiles. A synthetic 10ms packet is sent "
    "every 1ms during the paced test (10× accelerated simulation), without "
    "physical microphone, speakers or actual WASAPI callbacks.",
    "Short contention runs have many underflows; interpret p99 as a stress "
    "indicator, not a real audio deadline guarantee.",
    "Compare p99 and allocations in leaderboard.json; never select solely by average throughput.",
    "",
]

(base / "leaderboard.md").write_text("\n".join(lines), encoding="utf-8")
(base / "leaderboard.json").write_text(json.dumps(
    {"recommended_on_this_runner": recommended["candidate"],
     "ranking": rows, "passes": passes}, indent=2), encoding="utf-8")
print("\n".join(lines))
