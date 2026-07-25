using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IterVC.Desktop.ViewModels;

namespace IterVC.Desktop.Views;

public sealed class HotkeyCaptureControl : Control
{
    public static readonly StyledProperty<HotkeysViewModel?> HotkeysProperty =
        AvaloniaProperty.Register<HotkeyCaptureControl, HotkeysViewModel?>(nameof(Hotkeys));

    private TopLevel? _captureRoot;

    public HotkeysViewModel? Hotkeys
    {
        get => GetValue(HotkeysProperty);
        set => SetValue(HotkeysProperty, value);
    }

    public void CancelCapture() => Hotkeys?.CancelCapture();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_captureRoot is not null) return;
        _captureRoot = TopLevel.GetTopLevel(this);
        _captureRoot?.AddHandler(KeyDownEvent, CaptureShortcut, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (_captureRoot is Window window) window.Deactivated += CancelCaptureOnDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_captureRoot is Window window) window.Deactivated -= CancelCaptureOnDeactivated;
        _captureRoot?.RemoveHandler(KeyDownEvent, CaptureShortcut);
        _captureRoot = null;
        CancelCapture();
        base.OnDetachedFromVisualTree(e);
    }

    private void CancelCaptureOnDeactivated(object? sender, EventArgs e) => CancelCapture();

    private async void CaptureShortcut(object? sender, KeyEventArgs e)
    {
        if (Hotkeys is not { RecordingAction: not null } hotkeys) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            hotkeys.CancelCapture();
            return;
        }
        if (IsModifier(e.Key)) return;

        var key = FormatKey(e.Key);
        if (key is null) return;
        var parts = new List<string>(5);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(key);
        await hotkeys.CompleteCaptureAsync(string.Join('+', parts));
    }

    internal static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    internal static string? FormatKey(Key key)
    {
        var name = key.ToString();
        if (name.Length == 1 && char.IsLetter(name[0])) return name.ToUpperInvariant();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) return name[1].ToString();
        if (name.StartsWith('F') && int.TryParse(name[1..], out var function) && function is >= 1 and <= 24) return name;
        return key switch
        {
            Key.Space => "Space", Key.Tab => "Tab", Key.Enter => "Enter", Key.Back => "Backspace",
            Key.Insert => "Insert", Key.Delete => "Delete", Key.Home => "Home", Key.End => "End",
            Key.PageUp => "PageUp", Key.PageDown => "PageDown", Key.Left => "Left", Key.Right => "Right",
            Key.Up => "Up", Key.Down => "Down", _ => null
        };
    }
}
