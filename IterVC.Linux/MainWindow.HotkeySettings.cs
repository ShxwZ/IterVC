using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using IterVC.Core.Settings;

namespace IterVC.Linux;

/// <summary>
/// Focused-window hotkeys use the same settings names and gesture syntax as
/// Windows. Wayland deliberately remains compositor-controlled: global keys
/// can be bound to the secure local IPC commands in desktop settings.
/// </summary>
internal static class LinuxHotkeyParser
{
    internal readonly record struct Gesture(Key Key, KeyModifiers Modifiers);

    internal static bool TryParse(string? value, out Gesture result)
    {
        result = default;
        var parts = (value ?? "").Split('+',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 4) return false;
        var modifiers = KeyModifiers.None;
        foreach (var token in parts[..^1])
        {
            var next = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Control,
                "ALT" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                _ => KeyModifiers.None
            };
            if (next == KeyModifiers.None || (modifiers & next) != 0) return false;
            modifiers |= next;
        }
        var text = parts[^1].Length == 1 && char.IsAsciiDigit(parts[^1][0])
            ? "D" + parts[^1] : parts[^1];
        if (!Enum.TryParse<Key>(text, true, out var key) ||
            !Enum.IsDefined(key) || key == Key.None) return false;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;
        result = new Gesture(key, modifiers);
        return true;
    }

    internal static bool IsMatch(string? gesture, Key key, KeyModifiers modifiers) =>
        TryParse(gesture, out var parsed) && parsed.Key == key &&
        parsed.Modifiers == modifiers;
}

internal sealed partial class MainWindow
{
    private readonly TextBlock _hotkeyStatus = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly List<(Func<bool> Enabled, Func<string> Gesture)> _hotkeys = new();

    private Control HotkeySettingsControls()
    {
        var rows = new StackPanel { Spacing = 8 };
        _hotkeyStatus.FontSize = 10;
        _hotkeyStatus.Foreground = C("WarningTextBrush");

        void Add(string title, string hint, Func<bool> enabled, Action<bool> setEnabled,
                 Func<string> gesture, Action<string> setGesture)
        {
            var check = new CheckBox { Content = title, IsChecked = enabled() };
            var text = new TextBox { Text = gesture(), Width = 168,
                Watermark = "Ctrl+Shift+R" };
            var instructions = Label(hint, 10, "TextSubtleBrush");
            var item = new StackPanel { Orientation = Orientation.Horizontal,
                Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(check);
            item.Children.Add(text);
            var panel = Vertical(item, instructions);
            panel.Spacing = 3;
            rows.Children.Add(panel);
            _hotkeys.Add((enabled, gesture));

            check.IsCheckedChanged += (_, _) => {
                setEnabled(check.IsChecked == true);
                SavePreferences();
                ValidateHotkeySettings();
            };
            text.TextChanged += (_, _) => {
                setGesture(text.Text ?? "");
                SavePreferences();
                ValidateHotkeySettings();
            };
        }

        Add(L("Iniciar/detener enrutamiento", "Toggle routing"),
            "toggle-routing",
            () => _settings.ToggleRoutingHotkeyEnabled,
            x => _settings.ToggleRoutingHotkeyEnabled = x,
            () => _settings.ToggleRoutingHotkeyGesture,
            x => _settings.ToggleRoutingHotkeyGesture = x);
        Add(L("Iniciar enrutamiento", "Start routing"),
            "start-routing",
            () => _settings.StartRoutingHotkeyEnabled,
            x => _settings.StartRoutingHotkeyEnabled = x,
            () => _settings.StartRoutingHotkeyGesture,
            x => _settings.StartRoutingHotkeyGesture = x);
        Add(L("Detener enrutamiento", "Stop routing"),
            "stop-routing",
            () => _settings.StopRoutingHotkeyEnabled,
            x => _settings.StopRoutingHotkeyEnabled = x,
            () => _settings.StopRoutingHotkeyGesture,
            x => _settings.StopRoutingHotkeyGesture = x);
        Add(L("Silenciar micrófono", "Toggle microphone mute"),
            "toggle-microphone",
            () => _settings.ToggleMicrophoneHotkeyEnabled,
            x => _settings.ToggleMicrophoneHotkeyEnabled = x,
            () => _settings.ToggleMicrophoneHotkeyGesture,
            x => _settings.ToggleMicrophoneHotkeyGesture = x);
        Add(L("Silenciar aplicaciones", "Toggle application mute"),
            "toggle-applications-mute",
            () => _settings.ToggleApplicationsMuteHotkeyEnabled,
            x => _settings.ToggleApplicationsMuteHotkeyEnabled = x,
            () => _settings.ToggleApplicationsMuteHotkeyGesture,
            x => _settings.ToggleApplicationsMuteHotkeyGesture = x);

        rows.Children.Add(_hotkeyStatus);
        ValidateHotkeySettings();
        return rows;
    }

    private void ValidateHotkeySettings()
    {
        var seen = new HashSet<LinuxHotkeyParser.Gesture>();
        var invalid = false;
        var duplicate = false;
        foreach (var (enabled, gesture) in _hotkeys)
        {
            if (!enabled()) continue;
            if (!LinuxHotkeyParser.TryParse(gesture(), out var parsed))
                invalid = true;
            else if (!seen.Add(parsed))
                duplicate = true;
        }
        _hotkeyStatus.Text = duplicate
            ? L("Hay atajos duplicados. Solo se aplicará el primero.",
                "Duplicate shortcuts. Only the first will apply.")
            : invalid
            ? L("Hay un atajo inválido. Usa Ctrl, Shift o Alt y una tecla, por ejemplo Ctrl+Shift+R.",
                "Invalid shortcut. Use Ctrl, Shift or Alt plus a key, such as Ctrl+Shift+R.")
            : "";
    }
}
