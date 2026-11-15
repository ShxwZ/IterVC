# Prueba A/B con WASAPI y un micrófono real (Windows)

Herramienta independiente de la [PR #38](https://github.com/ShxwZ/IterVC/pull/38). No sustituye el buffer de producción, no modifica la configuración de IterVC ni instala drivers.

Usa el formato original de IterVC (48 kHz, estéreo float32, captura WASAPI con eventos, buffers 120/20/50 ms). El callback del micrófono escribe en el buffer C# original o en Rust, mientras un hilo distinto lee cada 10 ms. Prueba tres opciones — **C# original, Rust + P/Invoke y C# con `Span.CopyTo`** — rotando su orden cada tres rondas para evitar sesgo por calentamiento o por la posición de la prueba.

## Descargar y usar

En [GitHub Actions](https://github.com/ShxwZ/IterVC/actions/workflows/rust-pcm-buffers-benchmarks.yml) abre la ejecución más reciente y descarga el artefacto **itervc-wasapi-ab-hardware-win-x64**. Extrae el ZIP y abre PowerShell en esa carpeta.

Primero comprueba la biblioteca sin abrir el micrófono:

~~~powershell
.\IterVC.WasapiAB.exe --self-test
~~~

Después, para capturar voluntariamente el micrófono de comunicaciones predeterminado (sin guardar audio):

~~~powershell
.\IterVC.WasapiAB.exe --capture-mic --seconds 20 --rounds 3 --output .\mis-resultados
~~~

Cada ronda hace **tres fases** (C# original, Rust y C# optimizado), rotando el orden. Con 3 rondas y 20 segundos son **nueve fases de 20 segundos**. La opción **--read-fully** permite evaluar también la semántica de buffers de salida procesada; por defecto se utiliza la del micrófono de entrada.

**Privacidad:** no se guarda, reproduce ni transmite ninguna muestra de audio PCM. Solo se escriben estadísticas, el nombre del dispositivo y datos del entorno en **mis-resultados/results.json** y **mis-resultados/summary.md**. Revisa esos datos antes de compartirlos.

El ejecutable publicado incluye .NET 8 y la biblioteca Rust, y es para Windows x64. Windows necesita conceder acceso al micrófono a las aplicaciones de escritorio. Si el dispositivo no acepta el formato de captura exacto de IterVC, el programa mostrará un error en lugar de hacer una conversión que falsee la comparación.

## Métricas

Se registran los percentiles p50/p95/p99/p99,9 y el máximo de escritura en el callback real WASAPI y lectura de un hilo separado, además de underflows parciales, overflows, descartes, correcciones, lecturas vacías, lecturas incompletas, frames que faltaron en lecturas solicitadas, lecturas tardías, CPU global del proceso, asignaciones globales del proceso y recolecciones GC.

**Limitación esencial:** esto utiliza micrófono y callbacks de hardware reales, pero NO ejecuta el mixer completo, RNNoise, VB-Cable ni los streams de aplicaciones. Tampoco mide latencia extremo a extremo ni cortes audibles. CPU y asignaciones incluyen la aplicación de diagnóstico completa y la instrumentación, no solo el ring buffer.

## Qué comprueba GitHub Actions

Actions publica un ejecutable autocontenido de Windows y ejecuta **--self-test** para probar DLL nativa, equivalencia PCM/counters y arquitectura sin abrir hardware. Esto NO demuestra que se haya probado físicamente un micrófono en CI. La validación completa requiere ejecutar el comando de captura en un PC Windows real.

El Rust empaquetado utiliza **std::sync::Mutex** sin las variantes experimentales, elegida por equilibrio entre velocidad, resultados de colas y complejidad. No hacer merge ni sustituir el buffer C# solo porque un microbenchmark sea más rápido.


## Resultado del primer ensayo físico y corrección de métricas

En una primera captura voluntaria de Windows (3 fases por backend, 20 s por fase), **el C# original superó a Rust en p99 de escritura en las tres rondas**: mediana **1,10 µs C# / 1,80 µs Rust**. En lectura, las medianas p99 fueron **2,90 µs C# / 7,80 µs Rust**, aunque una ronda de Rust sí fue más rápida que su pareja C#. Los tiempos p50 fueron **0,50 vs 1,00 µs** (escritura) y **0,90 vs 1,10 µs** (lectura). La mediana de tiempo CPU del **proceso completo** fue igual, **265,6 ms**, con dispersión elevada: no demuestra ahorro energético ni de CPU atribuible a Rust.

**El informe original tenía dos trampas de interpretación** que se corrigen en la versión 2 del diagnóstico:
- El modo de micrófono sin procesar usaba `countUnderflows: false`; por tanto, los valores de underflow **siempre eran cero** y no podían utilizarse para concluir ausencia de fallos. Ahora se cuentan los underflows parciales en ambas implementaciones, **pero una lectura totalmente vacía sigue contándose aparte** por el contrato del buffer original. La herramienta también muestra explícitamente las lecturas incompletas y los frames no entregados. En modo `--read-fully` el relleno de silencio requiere otra interpretación.
- La prueba original registró aproximadamente 1.100 lecturas tardías por fase (de ~2.000), tanto en C# como en Rust, debido al ritmo del hilo de prueba y al planificador de Windows. **No equivalen a interrupciones audibles ni prueban que un buffer sea mejor.**

La conclusión, con ese hardware, es **mantener C# como implementación de producción**. Rust había ganado los microbenchmarks sintéticos, pero no mostró menores tiempos del callback WASAPI. Esta revisión permite volver a comparar la alternativa C# con `Span.CopyTo` sin traer una nueva DLL ni atribuir a Rust optimizaciones que pueden hacerse en C#.

Solo se publican en el repositorio **cifras agregadas sin nombre de dispositivo ni identificadores del equipo**. Los informes individuales compartidos por quien realiza el test permanecen fuera del repositorio.
