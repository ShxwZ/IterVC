using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Desktop.Services;

namespace IterVC.Desktop.ViewModels;

public sealed partial class HotkeyBindingViewModel : ViewModelBase
{
    private readonly HotkeysViewModel _owner;

    internal HotkeyBindingViewModel(HotkeyAction action, HotkeysViewModel owner)
    {
        Action = action;
        _owner = owner;
    }

    public HotkeyAction Action { get; }
    public string Label => _owner.GetLabel(Action);
    public string AssignText => _owner.Texts.HotkeyAssign;
    public string ChangeText => _owner.Texts.HotkeyChange;
    public string ClearText => _owner.Texts.HotkeyClear;
    public string CancelText => _owner.Texts.HotkeyCancel;
    public string NotAssignedText => _owner.Texts.HotkeyNotAssigned;
    public string RecordingText => _owner.Texts.HotkeyRecordingKeystrokes;
    public string EscapeToCancelText => _owner.Texts.HotkeyEscapeToCancel;
    public string? CaptureError => IsRecording ? _owner.CaptureError : null;
    public bool ShowRoutingGroupHeader => Action == HotkeyAction.ToggleRouting;
    public bool ShowMicrophoneGroupHeader => Action == HotkeyAction.ToggleMicrophone;
    public string RoutingGroupText => _owner.Texts.HotkeyRoutingGroup;
    public string MicrophoneGroupText => _owner.Texts.HotkeyMicrophoneGroup;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _gesture = string.Empty;
    [ObservableProperty] private bool _isRecording;
    public bool IsAssigned => IsEnabled && !string.IsNullOrWhiteSpace(Gesture);
    public IReadOnlyList<string> GestureParts => Gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(IsAssigned));
    partial void OnGestureChanged(string value)
    {
        OnPropertyChanged(nameof(IsAssigned));
        OnPropertyChanged(nameof(GestureParts));
    }
    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(CaptureError));

    [RelayCommand]
    private void BeginCapture() => _owner.BeginCapture(Action);

    [RelayCommand]
    private Task ClearAsync() => _owner.ClearAsync(Action);

    [RelayCommand]
    private void CancelCapture() => _owner.CancelCapture();

    internal void RefreshSharedState()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(AssignText));
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(ClearText));
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(NotAssignedText));
        OnPropertyChanged(nameof(RecordingText));
        OnPropertyChanged(nameof(EscapeToCancelText));
        OnPropertyChanged(nameof(CaptureError));
        OnPropertyChanged(nameof(RoutingGroupText));
        OnPropertyChanged(nameof(MicrophoneGroupText));
    }
}
