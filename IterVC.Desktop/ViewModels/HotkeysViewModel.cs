using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;

namespace IterVC.Desktop.ViewModels;

public sealed partial class HotkeysViewModel : ViewModelBase
{
    private readonly IGlobalHotkeyService _globalHotkeys;
    private readonly ISettingsService _settings;
    private readonly TextsViewModel _texts;

    public HotkeysViewModel(IGlobalHotkeyService globalHotkeys, ISettingsService settings, TextsViewModel texts)
    {
        _globalHotkeys = globalHotkeys;
        _settings = settings;
        _texts = texts;
        Rows = Enum.GetValues<HotkeyAction>().Select(action => new HotkeyBindingViewModel(action, this)).ToArray();
    }

    public IReadOnlyList<HotkeyBindingViewModel> Rows { get; }
    [ObservableProperty] private HotkeyAction? _recordingAction;
    [ObservableProperty] private string? _captureError;
    [ObservableProperty] private string? _registrationStatus;
    internal TextsViewModel Texts => _texts;

    partial void OnCaptureErrorChanged(string? value)
    {
        foreach (var row in Rows) row.RefreshSharedState();
    }

    public void Hydrate(AppSettings settings)
    {
        Set(HotkeyAction.ToggleRouting, settings.ToggleRoutingHotkeyEnabled, settings.ToggleRoutingHotkeyGesture);
        Set(HotkeyAction.StartRouting, settings.StartRoutingHotkeyEnabled, settings.StartRoutingHotkeyGesture);
        Set(HotkeyAction.StopRouting, settings.StopRoutingHotkeyEnabled, settings.StopRoutingHotkeyGesture);
        Set(HotkeyAction.ToggleMicrophone, settings.ToggleMicrophoneHotkeyEnabled, settings.ToggleMicrophoneHotkeyGesture);
    }

    public void BeginCapture(HotkeyAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action), action, null);
        var row = GetRow(action);
        CancelCapture();
        RecordingAction = action;
        row.IsRecording = true;
        CaptureError = null;
        RegistrationStatus = null;
    }

    public void CancelCapture()
    {
        foreach (var row in Rows) row.IsRecording = false;
        RecordingAction = null;
        CaptureError = null;
    }

    public async Task<bool> CompleteCaptureAsync(string gesture, CancellationToken cancellationToken = default)
    {
        if (RecordingAction is not { } action) return false;
        var normalized = gesture.Trim();
        var row = GetRow(action);
        if (string.Equals(row.Gesture, normalized, StringComparison.OrdinalIgnoreCase))
        {
            CancelCapture();
            return true;
        }
        if (!HotkeyGestureParser.TryParse(normalized, out var parsed)) return false;
        var conflict = Rows.FirstOrDefault(candidate => candidate.Action != action && candidate.IsAssigned
            && HotkeyGestureParser.TryParse(candidate.Gesture, out var other) && other == parsed);
        if (conflict is not null)
        {
            CaptureError = string.Format(_texts.HotkeyConflict, conflict.Label);
            return false;
        }

        var result = _globalHotkeys.Configure(BuildBindings(action, normalized));
        if (!result.Success)
        {
            CaptureError = result.Errors.TryGetValue(action, out var error) ? error : result.Errors.First().Value;
            return false;
        }

        row.Gesture = normalized;
        row.IsEnabled = true;
        await PersistAsync(cancellationToken);
        CancelCapture();
        return true;
    }

    public async Task<bool> ClearAsync(HotkeyAction action, CancellationToken cancellationToken = default)
    {
        var result = _globalHotkeys.Configure(BuildBindings(action, null));
        if (!result.Success) return false;
        var row = GetRow(action);
        row.IsEnabled = false;
        row.Gesture = string.Empty;
        await PersistAsync(cancellationToken);
        if (RecordingAction == action) CancelCapture();
        return true;
    }

    public IReadOnlyList<HotkeyBinding> BuildBindings(HotkeyAction? replacement = null, string? gesture = null) =>
        Rows.Select(row => new HotkeyBinding(row.Action,
            replacement == row.Action ? gesture is not null : row.IsEnabled,
            replacement == row.Action ? gesture ?? string.Empty : row.Gesture)).ToArray();

    public void RefreshLocalization()
    {
        foreach (var row in Rows) row.RefreshSharedState();
    }

    private HotkeyBindingViewModel GetRow(HotkeyAction action) =>
        Rows.Single(row => row.Action == action);

    public HotkeyBindingViewModel Row(HotkeyAction action) => GetRow(action);

    private void Set(HotkeyAction action, bool enabled, string? gesture)
    {
        var row = GetRow(action);
        row.IsEnabled = enabled;
        row.Gesture = gesture?.Trim() ?? string.Empty;
    }

    internal string GetLabel(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleRouting => _texts.HotkeyToggleRouting,
        HotkeyAction.StartRouting => _texts.HotkeyStartRouting,
        HotkeyAction.StopRouting => _texts.HotkeyStopRouting,
        HotkeyAction.ToggleMicrophone => _texts.HotkeyToggleMicrophone,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    private Task PersistAsync(CancellationToken cancellationToken) => _settings.UpdateAsync(settings =>
    {
        var toggle = GetRow(HotkeyAction.ToggleRouting);
        settings.ToggleRoutingHotkeyEnabled = toggle.IsEnabled;
        settings.ToggleRoutingHotkeyGesture = toggle.Gesture;
        var start = GetRow(HotkeyAction.StartRouting);
        settings.StartRoutingHotkeyEnabled = start.IsEnabled;
        settings.StartRoutingHotkeyGesture = start.Gesture;
        var stop = GetRow(HotkeyAction.StopRouting);
        settings.StopRoutingHotkeyEnabled = stop.IsEnabled;
        settings.StopRoutingHotkeyGesture = stop.Gesture;
        var microphone = GetRow(HotkeyAction.ToggleMicrophone);
        settings.ToggleMicrophoneHotkeyEnabled = microphone.IsEnabled;
        settings.ToggleMicrophoneHotkeyGesture = microphone.Gesture;
    }, cancellationToken);
}
