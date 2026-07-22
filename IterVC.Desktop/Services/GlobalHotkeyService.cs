using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IterVC.Desktop.Services;

internal enum HotkeyAction { ToggleRouting = 1, StartRouting, StopRouting, ToggleMicrophone }
internal readonly record struct HotkeyBinding(HotkeyAction Action, bool Enabled, string Gesture);

/// <summary>
/// Observes keyboard input globally without consuming it. The foreground application still
/// receives every key; IterVC only reacts to configured chords seen through Raw Input.
/// </summary>
internal sealed class GlobalHotkeyService : IDisposable
{
    internal const string WorkerUnavailableError = "hotkey-worker-unavailable";
    private const uint WmInput = 0x00FF;
    private const uint WmInputDeviceChange = 0x00FE;
    private const uint WmQuit = 0x0012;
    private const uint WmAppConfigure = 0x8001;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevDevNotify = 0x00002000;
    private const ushort RiKeyBreak = 0x0001;
    private const uint Ctrl = 0x01;
    private const uint Alt = 0x02;
    private const uint Shift = 0x04;
    private const uint Win = 0x08;

    private readonly Thread _thread;
    private readonly AutoResetEvent _ready = new(false);
    private readonly AutoResetEvent _configured = new(false);
    private readonly object _sync = new();
    private readonly WndProc _windowProcedure;
    private IReadOnlyList<HotkeyBinding> _pending = [];
    private Dictionary<HotkeyAction, string> _errors = [];
    private Dictionary<HotkeyAction, ParsedGesture> _bindings = [];
    private readonly HotkeyKeyState _keyState = new();
    private uint _threadId;
    private IntPtr _window;
    private string? _initializationError;
    private volatile bool _stopRequested;
    private bool _initialized;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _windowProcedure = WindowProcedure;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "IterVC global shortcut observer" };
        _thread.Start();
        if (!_ready.WaitOne(TimeSpan.FromSeconds(2)))
        {
            _initializationError = WorkerUnavailableError;
            _stopRequested = true;
            if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    public event EventHandler<HotkeyAction>? Pressed;
    public event EventHandler<string>? Failed;

    public IReadOnlyDictionary<HotkeyAction, string> Configure(IReadOnlyList<HotkeyBinding> bindings)
    {
        if (_initializationError is not null)
            return bindings.ToDictionary(x => x.Action, _ => _initializationError);
        if (_disposed || !_thread.IsAlive)
            return bindings.ToDictionary(x => x.Action, _ => WorkerUnavailableError);
        lock (_sync) _pending = bindings.ToArray();
        _configured.WaitOne(0); // Discard a late signal from an earlier timed-out request.
        if (!PostThreadMessage(_threadId, WmAppConfigure, IntPtr.Zero, IntPtr.Zero))
            return bindings.ToDictionary(x => x.Action, _ => new Win32Exception(Marshal.GetLastWin32Error()).Message);
        if (!_configured.WaitOne(TimeSpan.FromSeconds(2)))
            return bindings.ToDictionary(x => x.Action, _ => WorkerUnavailableError);
        lock (_sync) return new Dictionary<HotkeyAction, string>(_errors);
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        var className = $"IterVC.RawInput.{_threadId}";
        var instance = GetModuleHandle(null);
        try
        {
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProcedure = _windowProcedure,
                Instance = instance,
                ClassName = className
            };
            if (RegisterClassEx(ref windowClass) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _window = CreateWindowEx(0, className, "IterVC shortcut observer", 0, 0, 0, 0, 0,
                new IntPtr(-3), IntPtr.Zero, instance, IntPtr.Zero); // HWND_MESSAGE
            if (_window == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var device = new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = RidevInputSink | RidevDevNotify,
                Target = _window
            };
            if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _ready.Set();
            _initialized = true;
            if (_stopRequested) return;
            while (true)
            {
                var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result == 0) break;
                if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (message.Id == WmAppConfigure)
                    ApplyPendingConfiguration();
                else
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
        }
        catch (Exception ex)
        {
            _initializationError = ex.Message;
            _ready.Set();
            if (_initialized) Failed?.Invoke(this, ex.Message);
        }
        finally
        {
            _keyState.Reset();
            if (_window != IntPtr.Zero) DestroyWindow(_window);
            UnregisterClass(className, instance);
        }
    }

    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == WmInput)
                ProcessRawInput(lParam);
            else if (message == WmInputDeviceChange)
                ResetPressedState();
        }
        catch (Exception ex)
        {
            ResetPressedState();
            Failed?.Invoke(this, ex.Message);
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ProcessRawInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        var queryResult = GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize);
        if (queryResult == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (queryResult != 0 || size == 0)
            return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var readResult = GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize);
            if (readResult == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (readResult != size) return;
            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != 1) return; // RIM_TYPEKEYBOARD
            var keyboard = Marshal.PtrToStructure<RawKeyboard>(buffer + Marshal.SizeOf<RawInputHeader>());
            if (keyboard.VirtualKey == 0xFF) return;
            ProcessKey(header.Device, keyboard.VirtualKey, keyboard.MakeCode, keyboard.Flags,
                (keyboard.Flags & RiKeyBreak) != 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void ResetPressedState()
    {
        _keyState.Reset();
    }

    private void ProcessKey(IntPtr device, ushort virtualKey, ushort scanCode, ushort flags, bool released)
    {
        var activeModifiers = _keyState.Process(device, virtualKey, scanCode, flags, released);
        if (activeModifiers is null) return;
        foreach (var binding in _bindings)
        {
            if (binding.Value.Key == virtualKey && binding.Value.Modifiers == activeModifiers.Value)
                Pressed?.Invoke(this, binding.Key);
        }
    }

    private void ApplyPendingConfiguration()
    {
        var errors = new Dictionary<HotkeyAction, string>();
        try
        {
            IReadOnlyList<HotkeyBinding> bindings;
            lock (_sync) bindings = _pending;
            var current = _bindings.ToDictionary(x => x.Key,
                x => new ParsedHotkeyGesture(x.Value.Modifiers, x.Value.Key));
            var result = HotkeyConfiguration.Build(bindings, current);
            errors = result.Errors;
            _bindings = result.Bindings.ToDictionary(x => x.Key,
                x => new ParsedGesture(x.Value.Modifiers, x.Value.Key));
        }
        catch (Exception ex)
        {
            foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>()) errors[action] = ex.Message;
        }
        finally
        {
            lock (_sync) _errors = errors;
            _configured.Set();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopRequested = true;
        PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        if (!_thread.IsAlive)
        {
            _ready.Dispose();
            _configured.Dispose();
        }
    }

    private readonly record struct ParsedGesture(uint Modifiers, uint Key);
    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public WndProc WindowProcedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader { public uint Type, Size; public IntPtr Device, Parameter; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard { public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public IntPtr Window; public uint Id; public IntPtr WParam, LParam; public uint Time; public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
