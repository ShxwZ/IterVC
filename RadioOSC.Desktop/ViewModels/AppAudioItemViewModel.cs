using CommunityToolkit.Mvvm.ComponentModel;
using RadioOSC.Core.Models;

namespace RadioOSC.Desktop.ViewModels;

/// <summary>
/// Wrapper de presentación sobre un <see cref="AudioAppInfo"/>. El checkbox de la Vista
/// se enlaza a <see cref="IsIncludedInMix"/>; al cambiar, notifica al MainViewModel mediante
/// el callback recibido en el constructor (evita acoplar este ViewModel a los servicios!!).
/// </summary>
public sealed partial class AppAudioItemViewModel : ViewModelBase
{
    private readonly Action<AppAudioItemViewModel, bool> _onIncludedChanged;

    public int ProcessId { get; }
    public string ProcessName { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isIncludedInMix;

    [ObservableProperty] 
    private bool _isOscSource; 

    public AppAudioItemViewModel(AudioAppInfo info, Action<AppAudioItemViewModel, bool> onIncludedChanged)
    {
        ProcessId = info.ProcessId;
        ProcessName = info.ProcessName;
        DisplayName = info.DisplayName;
        _isIncludedInMix = info.IsIncludedInMix;
        _onIncludedChanged = onIncludedChanged;
    }

    partial void OnIsIncludedInMixChanged(bool value) => _onIncludedChanged(this, value);
}
