# Rust PCM ring buffers: equivalencia y rendimiento, sin tocar producción

**Rama aislada:** `perf/rust-pcm-buffers-benchmarks`, creada directamente desde `master`. **No incluye Linux, PipeWire ni cambios en la GUI.** El buffer usado por Windows (`IterVC.Audio/Buffers/RealtimeAudioBuffer.cs`) sigue intacto y continúa siendo la implementación real.

## Qué se compara

- **C# real:** se compilan directamente, **sin copiarlos ni reescribirlos**, `RealtimeAudioBuffer.cs` y `RealtimeAudioBufferSnapshot.cs` desde `IterVC.Audio/Buffers`.
- **Rust experimental:** crate independiente `native/iter_vc_buffers`, buffer PCM de capacidad fija y `Mutex` (como el `lock` de C#), con una ABI C mínima: crear, escribir, leer, limpiar, consultar contadores y liberar. Se invoca desde .NET mediante **P/Invoke y SafeHandle**; por tanto, el coste de FFI forma parte de los resultados.
- **Formato y parámetros idénticos:** 48 kHz, 2 canales, float32, capacidad 120 ms, objetivo 20 ms y corrección tras 50 ms de buffer. Casos de `readFully` y cómputo de underflows activados/desactivados.
- **Equivalencia obligatoria antes de medir:** PCM byte a byte, parcialidades, overflow, underflow, corrección de latencia, `Clear`, estadísticas (incluyendo descartes y ocupación) con operaciones reproducibles y semillas fijas. La prueba se repite para los cuatro modos de lectura.
- **Tres escenarios:** operaciones de 1 ms, de 10 ms y escrituras/lecturas por ráfagas de 10 ms. 5.000 iteraciones de precalentamiento; 5 ensayos de 40.000 iteraciones cada uno por plataforma; orden alterno de C# y Rust para reducir sesgos. Se informa la **mediana** y los tiempos individuales.

## GitHub Actions

Workflow: [Rust PCM buffers — C# equivalence and comparative benchmarks](../.github/workflows/rust-pcm-buffers-benchmarks.yml).

Se ejecuta en **windows-latest** (la plataforma real de la app) y **ubuntu-latest** (para contrastar el coste portable de Rust/PInvoke), y publica dos artefactos con `results.json` (muestras) y `summary.md` (tabla). También coloca la tabla en el resumen de la ejecución de GitHub Actions.

> **No existe un umbral CI de «Rust debe ser más rápido».** Los runners compartidos no son laboratorios estables. Una ratio superior a **1.00×** significa que Rust fue más rápido en *esa ejecución*; inferior a **1.00×** significa que C# fue más rápido. El benchmark mide tráfico de memoria y operaciones del buffer, **no** latencia WASAPI, jitter del driver, end-to-end ni calidad del audio. Los MiB/s son bytes solicitados, no throughput de un micrófono físico.

## Reproducir manualmente

Requiere Rust estable y .NET SDK 8 en Windows x64 o Linux x64, y acceso a NuGet:

```bash
cargo test --manifest-path native/iter_vc_buffers/Cargo.toml
cargo build --manifest-path native/iter_vc_buffers/Cargo.toml --release
dotnet build benchmarks/IterVC.BufferBenchmarks/IterVC.BufferBenchmarks.csproj -c Release
```

Copia la biblioteca Rust de Release al directorio de salida `benchmarks/IterVC.BufferBenchmarks/bin/Release/net8.0/`:

- **Windows:** `native/iter_vc_buffers/target/release/iter_vc_buffers.dll`
- **Linux:** `native/iter_vc_buffers/target/release/libiter_vc_buffers.so`

Y ejecuta:

```bash
dotnet run --project benchmarks/IterVC.BufferBenchmarks/IterVC.BufferBenchmarks.csproj -c Release --no-build -- bench-results 40000
```

El programa no entrega ratios si falla cualquier verificación de equivalencia. Resultados en `bench-results/summary.md` y `bench-results/results.json`.

## Decisión posterior

Solo tras repetir estas pruebas **en el mismo PC Windows**, con carga/afinidad controlada y perfiles representativos, se debería abrir una PR distinta para integrar opcionalmente el buffer Rust en producción. Esa PR debe medir p50/p95/p99 de bloqueo/callback, CPU, asignaciones, memoria, interop, latencia y comportamiento ante saturación, además de permitir reversión a C# en caso de regresiones. **Esta rama no modifica el motor audio Windows.**
