namespace IterVC.Desktop.Services;

public static class HotkeyErrors
{
    public const string WorkerUnavailable = "hotkey-worker-unavailable";
}

public enum HotkeyAction { ToggleRouting = 1, StartRouting, StopRouting, ToggleMicrophone }

public readonly record struct HotkeyBinding(HotkeyAction Action, bool Enabled, string Gesture);

public readonly record struct HotkeyRegistrationResult(IReadOnlyDictionary<HotkeyAction, string> Errors)
{
    public bool Success => Errors.Count == 0;
}

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<HotkeyAction>? Pressed;
    event EventHandler<string>? Failed;
    HotkeyRegistrationResult Configure(IReadOnlyList<HotkeyBinding> bindings);
}
