using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;

namespace IterVC.Desktop.Services;

/// <summary>
/// Registra un atajo de teclado global (a nivel de sistema) mediante RegisterHotKey de Win32,
/// de modo que funcione aunque la ventana no tenga el foco o esté oculta en la bandeja.
/// El WM_HOTKEY llega al WndProc de la ventana principal, interceptado con
/// <see cref="Win32Properties.AddWndProcHookCallback"/>.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4956; // "IV"
    private const uint WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly TopLevel _topLevel;
    private readonly IntPtr _hwnd;
    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private bool _registered;

    /// <summary>Se dispara en el hilo de UI cuando el usuario pulsa el atajo registrado.</summary>
    public event Action? Pressed;

    public GlobalHotkeyService(TopLevel topLevel, IntPtr hwnd)
    {
        _topLevel = topLevel;
        _hwnd = hwnd;
        _hook = WndProcHook;
        Win32Properties.AddWndProcHookCallback(topLevel, _hook);
    }

    /// <summary>Se invoca para cada mensaje de la ventana; solo intercepta nuestro WM_HOTKEY.</summary>
    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Intenta registrar el gesto como hotkey global. Devuelve false si Windows lo rechaza
    /// (p. ej. otro programa ya lo usa) o si la tecla no tiene equivalente virtual-key.
    /// Debe llamarse desde el hilo de UI (dueño de la ventana).
    /// </summary>
    public bool TryRegister(KeyGesture gesture)
    {
        Unregister();

        var vk = ToVirtualKey(gesture.Key);
        if (vk == 0) return false;

        var modifiers = MOD_NOREPEAT;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= MOD_CONTROL;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= MOD_ALT;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= MOD_SHIFT;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= MOD_WIN;

        _registered = RegisterHotKey(_hwnd, HotkeyId, modifiers, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        Win32Properties.RemoveWndProcHookCallback(_topLevel, _hook);
    }

    /// <summary>Traduce una tecla de Avalonia a su virtual-key code de Win32 (0 si no hay mapeo).</summary>
    private static uint ToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return (uint)(0x41 + (key - Key.A));
        if (key >= Key.D0 && key <= Key.D9) return (uint)(0x30 + (key - Key.D0));
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return (uint)(0x60 + (key - Key.NumPad0));
        if (key >= Key.F1 && key <= Key.F24) return (uint)(0x70 + (key - Key.F1));

        return key switch
        {
            Key.Space => 0x20,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Back => 0x08,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Pause => 0x13,
            Key.Scroll => 0x91,
            Key.Snapshot => 0x2C,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            Key.OemBackslash => 0xE2,
            _ => 0
        };
    }
}
