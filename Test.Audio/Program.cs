using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;


/// <summary>
/// Program para depuracion de sonido
/// </summary>
class Program
{
    [MTAThread]
    static void Main()
    {
        Console.WriteLine("Introduce el nombre del proceso (ej: brave, chrome, spotify):");
        var processName = Console.ReadLine()!.Trim();

        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            Console.WriteLine("No se encontraron procesos.");
            return;
        }

        Console.WriteLine($"\nEncontrados {processes.Length} procesos '{processName}':");

        foreach (var proc in processes)
        {
            Console.WriteLine($"--- Probando PID {proc.Id} ---");

            var loopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
            {
                TargetProcessId = (uint)proc.Id,
                ProcessLoopbackMode = 0 // IncludeTargetProcessTree
            };

            var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
            {
                ActivationType = 1, // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                ProcessLoopbackParams = loopbackParams
            };

            int paramsSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
            IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
            Marshal.StructureToPtr(activationParams, paramsPtr, false);

            var propvariant = new PROPVARIANT
            {
                vt = 65, // VT_BLOB
                blobSize = (uint)paramsSize,
                blobData = paramsPtr
            };

            var handler = new Handler();
            var iid = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"); // IID_IAudioClient

            // Usamos IntPtr para evitar que .NET intente castear el objeto de salida automáticamente
            int hr = ActivateAudioInterfaceAsync(
                @"VAD\Process_Loopback",
                ref iid,
                ref propvariant,
                handler,
                out IntPtr operationPtr);

            Console.WriteLine($"  HRESULT INICIAL : 0x{hr:X8} ({(hr == 0 ? "OK" : "ERROR")})");

            if (hr == 0)
            {
                // Esperamos un máximo de 5 segundos al callback asíncrono
                bool signaled = handler.WaitHandle.WaitOne(5000);

                if (signaled)
                {
                    Console.WriteLine($"  CALLBACK RESULT : {handler.ResultText}");
                    if (handler.Success)
                    {
                        Console.WriteLine($"\n✓ PID CORRECTO ENCONTRADO: {proc.Id} - ¡IAudioClient Listo!");

                        if (operationPtr != IntPtr.Zero) Marshal.Release(operationPtr);
                        Marshal.FreeHGlobal(paramsPtr);
                        break;
                    }
                }
                else
                {
                    Console.WriteLine("  RESULTADO: TIMEOUT — callback no llegó");
                }
            }

            if (operationPtr != IntPtr.Zero)
            {
                Marshal.Release(operationPtr);
            }

            GC.KeepAlive(handler);
            Marshal.FreeHGlobal(paramsPtr);
            Console.WriteLine();
        }

        Console.WriteLine("\nFin. Pulsa Enter para salir.");
        Console.ReadLine();
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        ref Guid iid,
        ref PROPVARIANT p,
        IActivateAudioInterfaceCompletionHandler h,
        out IntPtr op); // Cambiado a IntPtr para evitar excepciones de cast

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
    {
        public uint TargetProcessId;
        public uint ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct AUDIOCLIENT_ACTIVATION_PARAMS
    {
        public uint ActivationType;
        public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public uint blobSize;
        public IntPtr blobData;
    }

    // --- INTERFACES ---

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler
    {
        // Cambiado de la interfaz conflictiva a un IntPtr plano para esquivar el Marshaller de .NET
        void ActivateCompleted(IntPtr op);
    }

    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    // Firma del método GetActivateResult de la interfaz IActivateAudioInterfaceAsyncOperation
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetActivateResultDelegate(IntPtr self, out int hr, out IntPtr iface);

    class Handler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEvent WaitHandle { get; } = new ManualResetEvent(false);
        public bool Success { get; private set; }
        public string ResultText { get; private set; } = "";

        public void ActivateCompleted(IntPtr op)
        {
            try
            {
                if (op == IntPtr.Zero)
                {
                    ResultText = "Error: El puntero de operación asíncrona es nulo.";
                    return;
                }

                // --- TRUCO DE BAJO NIVEL (VTable Bypass) ---
                // 1. Leemos el puntero a la VTable (Tabla de Funciones Virtuales)
                IntPtr vtable = Marshal.ReadIntPtr(op);

                // 2. Obtenemos el 4º método (índice 3). 
                //    Los índices 0, 1 y 2 pertenecen a QueryInterface, AddRef y Release de IUnknown.
                //    El índice 3 es GetActivateResult.
                IntPtr getActivateResultPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);

                // 3. Convertimos el puntero de la función en un delegado llamable desde C#
                var getActivateResult = Marshal.GetDelegateForFunctionPointer<GetActivateResultDelegate>(getActivateResultPtr);

                // 4. Invocamos la función nativa pasando 'op' como el puntero 'this' (self)
                int comHr = getActivateResult(op, out var hr, out var iface);

                if (comHr == 0) // Invocación COM exitosa
                {
                    Success = hr == 0;
                    ResultText = $"hr=0x{hr:X8} ({(hr == 0 ? "IAudioClient OK" : "ERROR")})";

                    if (iface != IntPtr.Zero)
                    {
                        // Liberamos el IAudioClient devuelto si no lo vamos a usar en este test
                        Marshal.Release(iface);
                    }
                }
                else
                {
                    Success = false;
                    ResultText = $"Fallo al llamar a GetActivateResult nativo. HRESULT: 0x{comHr:X8}";
                }
            }
            catch (Exception ex)
            {
                Success = false;
                ResultText = $"Excepción en el callback: {ex.Message}";
            }
            finally
            {
                WaitHandle.Set();
            }
        }
    }
}