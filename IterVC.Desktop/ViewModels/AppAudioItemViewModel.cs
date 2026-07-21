using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Models;

namespace IterVC.Desktop.ViewModels;

/// <summary>
/// Wrapper de presentación sobre un <see cref="AudioAppInfo"/>. El checkbox de la Vista
/// se enlaza a <see cref="IsIncludedInMix"/>; al cambiar, notifica al MainViewModel mediante
/// el callback recibido en el constructor (evita acoplar este ViewModel a los servicios!!).
/// </summary>
public sealed partial class AppAudioItemViewModel : ViewModelBase
{
    private readonly Action<AppAudioItemViewModel, bool> _onIncludedChanged;
    private readonly Action<AppAudioItemViewModel, float> _onVolumeChanged;

    public int ProcessId { get; }
    public string ProcessName { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isIncludedInMix;

    [ObservableProperty]
    private bool _isOscSource;

    /// <summary>Volumen individual de esta app (0.0 - 2.0, 1.0 = sin cambio).</summary>
    [ObservableProperty]
    private float _volume = 1.0f;

    /// <summary>Nivel de pico actual (0.0 - 1.0) para el medidor; lo alimenta el MainViewModel.</summary>
    [ObservableProperty]
    private float _level;

    public AppAudioItemViewModel(
        AudioAppInfo info,
        Action<AppAudioItemViewModel, bool> onIncludedChanged,
        Action<AppAudioItemViewModel, float> onVolumeChanged,
        float initialVolume = 1.0f)
    {
        ProcessId = info.ProcessId;
        ProcessName = info.ProcessName;
        DisplayName = info.DisplayName;
        _isIncludedInMix = info.IsIncludedInMix;
        _onIncludedChanged = onIncludedChanged;
        _onVolumeChanged = onVolumeChanged;
        _volume = initialVolume;
    }

    partial void OnIsIncludedInMixChanged(bool value) => _onIncludedChanged(this, value);

    partial void OnVolumeChanged(float value) => _onVolumeChanged(this, value);
}
