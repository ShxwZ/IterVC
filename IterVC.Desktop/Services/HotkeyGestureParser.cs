namespace IterVC.Desktop.Services;

internal readonly record struct ParsedHotkeyGesture(uint Modifiers, uint Key);

internal static class HotkeyGestureParser
{
    internal static bool TryParse(string? gesture, out ParsedHotkeyGesture parsed)
    {
        parsed = default;
        uint modifiers = 0;
        var parts = (gesture ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return false;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "ALT" => 0x02u, "CTRL" or "CONTROL" => 0x01u,
                "SHIFT" => 0x04u, "WIN" or "WINDOWS" => 0x08u, _ => 0u
            };
            if (modifier == 0 || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        var name = parts[^1].ToUpperInvariant();
        var key = name.Length == 1 && char.IsLetterOrDigit(name[0]) ? name[0]
            : name.StartsWith('F') && int.TryParse(name[1..], out var number) && number is >= 1 and <= 24
                ? (uint)(0x70 + number - 1) : name switch
                {
                    "BACKSPACE" => 0x08, "TAB" => 0x09, "ENTER" => 0x0D, "SPACE" => 0x20,
                    "PAGEUP" => 0x21, "PAGEDOWN" => 0x22, "END" => 0x23, "HOME" => 0x24,
                    "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
                    "INSERT" => 0x2D, "DELETE" => 0x2E, _ => 0
                };
        if (key == 0 || GetModifier((ushort)key) != 0) return false;
        parsed = new ParsedHotkeyGesture(modifiers, key);
        return true;
    }

    private static uint GetModifier(ushort key) => key switch
    {
        0x10 or 0xA0 or 0xA1 => 0x04, 0x11 or 0xA2 or 0xA3 => 0x01,
        0x12 or 0xA4 or 0xA5 => 0x02, 0x5B or 0x5C => 0x08, _ => 0
    };
}

internal static class HotkeyConfiguration
{
    internal static (Dictionary<HotkeyAction, ParsedHotkeyGesture> Bindings, Dictionary<HotkeyAction, string> Errors)
        Build(IReadOnlyList<HotkeyBinding> bindings, IReadOnlyDictionary<HotkeyAction, ParsedHotkeyGesture> current)
    {
        var errors = new Dictionary<HotkeyAction, string>();
        var candidate = new Dictionary<HotkeyAction, ParsedHotkeyGesture>();
        var gestures = new HashSet<ParsedHotkeyGesture>();
        foreach (var binding in bindings.Where(x => x.Enabled))
        {
            if (!HotkeyGestureParser.TryParse(binding.Gesture, out var parsed))
                errors[binding.Action] = "Invalid shortcut. Use zero or more modifiers plus one supported key.";
            else if (!gestures.Add(parsed))
                errors[binding.Action] = "This shortcut is already assigned to another action.";
            else
                candidate[binding.Action] = parsed;
        }
        return errors.Count == 0
            ? (candidate, errors)
            : (new Dictionary<HotkeyAction, ParsedHotkeyGesture>(current), errors);
    }
}

internal readonly record struct PhysicalHotkeyKey(IntPtr Device, ushort VirtualKey, ushort ScanCode, ushort Flags);

internal sealed class HotkeyKeyState
{
    private readonly HashSet<PhysicalHotkeyKey> _keysDown = [];
    private readonly Dictionary<PhysicalHotkeyKey, uint> _modifiersDown = [];

    internal uint? Process(IntPtr device, ushort virtualKey, ushort scanCode, ushort flags, bool released)
    {
        var key = new PhysicalHotkeyKey(device, virtualKey, scanCode, (ushort)(flags & 0x06));
        var modifier = virtualKey switch
        {
            0x10 or 0xA0 or 0xA1 => 0x04u, 0x11 or 0xA2 or 0xA3 => 0x01u,
            0x12 or 0xA4 or 0xA5 => 0x02u, 0x5B or 0x5C => 0x08u, _ => 0u
        };
        if (released)
        {
            _keysDown.Remove(key);
            _modifiersDown.Remove(key);
            return null;
        }
        if (!_keysDown.Add(key)) return null;
        if (modifier != 0)
        {
            _modifiersDown[key] = modifier;
            return null;
        }
        return _modifiersDown.Values.Aggregate(0u, (current, value) => current | value);
    }

    internal void Reset()
    {
        _keysDown.Clear();
        _modifiersDown.Clear();
    }
}
