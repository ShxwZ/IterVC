# Prueba A/B con WASAPI y un micrófono real (Windows)

Herramienta independiente de la [PR #38](https://github.com/ShxwZ/IterVC/pull/38). No sustituye el buffer de producción, no modifica la configuración de IterVC ni instala drivers.

Usa el formato original de IterVC (48 kHz, estéreo float32, captura WASAPI con eventos, buffers 120/20/50 ms). El callback del micrófono escribe en el buffer C# original o en Rust, mientras un hilo distinto lee cada 10 ms. Prueba alternando C# → Rust y Rust → C# con el mismo micrófono.

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

Cada ronda hace dos fases, alternando el orden. Con 3 rondas y 20 segundos son seis fases de 20 segundos. La opción **--read-fully** permite evaluar también la semántica de buffers de salida procesada; por defecto se utiliza la del micrófono de entrada.

**Privacidad:** no se guarda, reproduce ni transmite ninguna muestra de audio PCM. Solo se escriben estadísticas, el nombre del dispositivo y datos del entorno en **mis-resultados/results.json** y **mis-resultados/summary.md**. Revisa esos datos antes de compartirlos.

El ejecutable publicado incluye .NET 8 y la biblioteca Rust, y es para Windows x64. Windows necesita conceder acceso al micrófono a las aplicaciones de escritorio. Si el dispositivo no acepta el formato de captura exacto de IterVC, el programa mostrará un error en lugar de hacer una conversión que falsee la comparación.

## Métricas

Se registran los percentiles p50/p95/p99/p99,9 y el máximo de escritura en el callback real WASAPI y lectura de un hilo separado, además de underflows, overflows, descartes, correcciones, lecturas vacías, lecturas tardías, CPU global del proceso, asignaciones globales del proceso y recolecciones GC.

**Limitación esencial:** esto utiliza micrófono y callbacks de hardware reales, pero NO ejecuta el mixer completo, RNNoise, VB-Cable ni los streams de aplicaciones. Tampoco mide latencia extremo a extremo ni cortes audibles. CPU y asignaciones incluyen la aplicación de diagnóstico completa y la instrumentación, no solo el ring buffer.

## Qué comprueba GitHub Actions

Actions publica un ejecutable autocontenido de Windows y ejecuta **--self-test** para probar DLL nativa, equivalencia PCM/counters y arquitectura sin abrir hardware. Esto NO demuestra que se haya probado físicamente un micrófono en CI. La validación completa requiere ejecutar el comando de captura en un PC Windows real.

El Rust empaquetado utiliza **std::sync::Mutex** sin las variantes experimentales, elegida por equilibrio entre velocidad, resultados de colas y complejidad. No hacer merge ni sustituir el buffer C# solo porque un microbenchmark sea más rápido.
