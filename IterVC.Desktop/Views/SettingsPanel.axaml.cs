using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IterVC.Desktop.ViewModels;

namespace IterVC.Desktop.Views;

public sealed partial class SettingsPanel : UserControl
{
    private TopLevel? _keyCaptureRoot;

    public SettingsPanel()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_keyCaptureRoot is not null) return;
        _keyCaptureRoot = TopLevel.GetTopLevel(this);
        _keyCaptureRoot?.AddHandler(KeyDownEvent, CaptureShortcut, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (_keyCaptureRoot is Window window) window.Deactivated += CancelCaptureOnDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_keyCaptureRoot is Window window) window.Deactivated -= CancelCaptureOnDeactivated;
        _keyCaptureRoot?.RemoveHandler(KeyDownEvent, CaptureShortcut);
        _keyCaptureRoot = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void CancelCaptureOnDeactivated(object? sender, EventArgs e) => ViewModel?.CancelShortcutCapture();

    private void CancelCaptureOutsideSettingsTab(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedItem: var selected }
            && !ReferenceEquals(selected, ShortcutsSettingsTab)
            && ViewModel is { RecordingShortcutAction: not null } vm)
            vm.CancelShortcutCapture();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void RecordShortcut(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } button || ViewModel is not { } vm) return;
        if (vm.RecordingShortcutAction == action)
            vm.CancelShortcutCapture();
        else
            vm.BeginShortcutCapture(action);
        button.Focus();
        e.Handled = true;
    }

    private void ClearShortcut(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action } && ViewModel is { } vm)
            vm.ClearHotkey(action);
        e.Handled = true;
    }

    private void CancelShortcut(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelShortcutCapture();
        e.Handled = true;
    }

    private void CaptureShortcut(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { RecordingShortcutAction: not null } vm) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            vm.CancelShortcutCapture();
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
        vm.CompleteShortcutCapture(string.Join('+', parts));
    }

    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string? FormatKey(Key key)
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
