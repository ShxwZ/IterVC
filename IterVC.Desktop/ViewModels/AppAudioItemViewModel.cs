using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Core.Models;

namespace IterVC.Desktop.ViewModels;

public sealed partial class AppAudioItemViewModel : ViewModelBase
{
    private readonly ApplicationsViewModel _owner;

    internal AppAudioItemViewModel(AudioAppInfo info, ApplicationsViewModel owner)
    {
        ProcessId = info.ProcessId;
        ProcessName = info.ProcessName;
        DisplayName = info.DisplayName;
        _isIncludedInMix = info.IsIncludedInMix;
        _owner = owner;
    }

    public int ProcessId { get; }
    public string ProcessName { get; }
    public string DisplayName { get; }
    [ObservableProperty] private bool _isIncludedInMix;

    [RelayCommand]
    private Task ToggleInclusionAsync() => _owner.SetIncludedAsync(this, !IsIncludedInMix);

    internal void SetIncluded(bool included) => IsIncludedInMix = included;
}
