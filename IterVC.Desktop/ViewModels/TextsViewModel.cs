using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Localization;

namespace IterVC.Desktop.ViewModels;

/// <summary>
/// Sub-VM con todas las cadenas traducibles de la UI.
/// Se regenera vía <see cref="RaiseAll"/> cuando el idioma cambia.
/// </summary>
public sealed class TextsViewModel : ObservableObject
{
    private string _appTitle = G(LK.AppTitle);
    private string _appFooter = FormatAppFooter();
    private string _statusRouting = G(LK.StatusRouting);
    private string _statusInactive = G(LK.StatusInactive);
    private string _statusMicrophoneActive = G(LK.StatusMicrophoneActive);
    private string _statusMicrophoneInactive = G(LK.StatusMicrophoneInactive);
    private string _buttonStart = G(LK.ButtonStart);
    private string _buttonStop = G(LK.ButtonStop);
    private string _buttonRefreshApps = G(LK.ButtonRefreshApps);
    private string _cardAppsTitle = G(LK.CardAppsTitle);
    private string _cardAppsHelp = G(LK.CardAppsHelp);
    private string _appsDetectedOne = G(LK.AppsDetectedOne);
    private string _appsDetectedMany = G(LK.AppsDetectedMany);
    private string _cardDevicesTitle = G(LK.CardDevicesTitle);
    private string _deviceReference = G(LK.DeviceReference);
    private string _deviceVbCable = G(LK.DeviceVbCable);
    private string _deviceMicrophone = G(LK.DeviceMicrophone);
    private string _deviceMicrophoneEnable = G(LK.DeviceMicrophoneEnable);
    private string _cardSpatialTitle = G(LK.CardSpatialInfoTitle);
    private string _cardSpatialBody = G(LK.CardSpatialInfoBody);
    private string _cardSpatialHelp = G(LK.CardSpatialInfoHelp);
    private string _cardMixTitle = G(LK.CardMixTitle);
    private string _mixAppsGain = G(LK.MixAppsGain);
    private string _mixAppsBoost = G(LK.MixAppsBoost);
    private string _mixAppsBoostHelp = G(LK.MixAppsBoostHelp);
    private string _mixAppsGainHelp = G(LK.MixAppsGainHelp);
    private string _mixMonitorMic = G(LK.MixMonitorMic);
    private string _noiseGateEnable = G(LK.NoiseGateEnable);
    private string _noiseGateThreshold = G(LK.NoiseGateThreshold);
    private string _noiseGateThresholdHelp = G(LK.NoiseGateThresholdHelp);
    private string _noiseGateAttack = G(LK.NoiseGateAttack);
    private string _noiseGateRelease = G(LK.NoiseGateRelease);
    private string _noiseGateTitle = G(LK.NoiseGateTitle);
    private string _noiseGateHelp = G(LK.NoiseGateHelp);
    private string _noiseGateOpen = G(LK.NoiseGateOpen);
    private string _noiseGateClosed = G(LK.NoiseGateClosed);
    private string _noiseGateCalibrate = G(LK.NoiseGateCalibrate);
    private string _noiseGateCalibrating = G(LK.NoiseGateCalibrating);
    private string _noiseGateAdvanced = G(LK.NoiseGateAdvanced);
    private string _noiseGateOutput = G(LK.NoiseGateOutput);
    private string _noiseGateAttackHelp = G(LK.NoiseGateAttackHelp);
    private string _noiseGateReleaseHelp = G(LK.NoiseGateReleaseHelp);
    private string _cardOscTitle = G(LK.CardOscTitle);
    private string _oscEnableChatbox = G(LK.OscEnableChatbox);
    private string _oscTemplateLabel = G(LK.OscTemplateLabel);
    private string _oscTemplateWatermark = G(LK.OscTemplateWatermark);
    private string _oscTemplateTokens = G(LK.OscTemplateTokens);
    private string _cardLanguageTitle = G(LK.CardLanguageTitle);
    private string _cardLanguageHelp = G(LK.CardLanguageHelp);
    private string _updateConsentTitle = G(LK.UpdateConsentTitle);
    private string _updateConsentBody = G(LK.UpdateConsentBody);
    private string _updateAllow = G(LK.UpdateAllow);
    private string _updateDecline = G(LK.UpdateDecline);
    private string _updateAvailable = G(LK.UpdateAvailable);
    private string _updateOpen = G(LK.UpdateOpen);
    private string _updateSettingsTitle = G(LK.UpdateSettingsTitle);
    private string _updateEnabled = G(LK.UpdateEnabled);
    private string _updateCheckNow = G(LK.UpdateCheckNow);
    private string _settingsTitle = G(LK.SettingsTitle);
    private string _updateDismiss = G(LK.UpdateDismiss);
    private string _hotkeyTitle = G(LK.HotkeyTitle);
    private string _hotkeyHelp = G(LK.HotkeyHelp);
    private string _hotkeyToggleRouting = G(LK.HotkeyToggleRouting);
    private string _hotkeyStartRouting = G(LK.HotkeyStartRouting);
    private string _hotkeyStopRouting = G(LK.HotkeyStopRouting);
    private string _hotkeyToggleMicrophone = G(LK.HotkeyToggleMicrophone);
    private string _hotkeyNotAssigned = G(LK.HotkeyNotAssigned);
    private string _hotkeyClear = G(LK.HotkeyClear);
    private string _hotkeyAssign = G(LK.HotkeyAssign);
    private string _hotkeyChange = G(LK.HotkeyChange);
    private string _hotkeyRecordingKeystrokes = G(LK.HotkeyRecordingKeystrokes);
    private string _hotkeyEscapeToCancel = G(LK.HotkeyEscapeToCancel);
    private string _hotkeyRoutingGroup = G(LK.HotkeyRoutingGroup);
    private string _hotkeyMicrophoneGroup = G(LK.HotkeyMicrophoneGroup);
    private string _microphoneEnable = G(LK.MicrophoneEnable);
    private string _microphoneDisable = G(LK.MicrophoneDisable);

    public string AppTitle { get => _appTitle; set => SetProperty(ref _appTitle, value); }
    public string AppFooter { get => _appFooter; set => SetProperty(ref _appFooter, value); }
    public string StatusRouting { get => _statusRouting; set => SetProperty(ref _statusRouting, value); }
    public string StatusInactive { get => _statusInactive; set => SetProperty(ref _statusInactive, value); }
    public string StatusMicrophoneActive { get => _statusMicrophoneActive; set => SetProperty(ref _statusMicrophoneActive, value); }
    public string StatusMicrophoneInactive { get => _statusMicrophoneInactive; set => SetProperty(ref _statusMicrophoneInactive, value); }
    public string ButtonStart { get => _buttonStart; set => SetProperty(ref _buttonStart, value); }
    public string ButtonStop { get => _buttonStop; set => SetProperty(ref _buttonStop, value); }
    public string ButtonRefreshApps { get => _buttonRefreshApps; set => SetProperty(ref _buttonRefreshApps, value); }
    public string CardAppsTitle { get => _cardAppsTitle; set => SetProperty(ref _cardAppsTitle, value); }
    public string CardAppsHelp { get => _cardAppsHelp; set => SetProperty(ref _cardAppsHelp, value); }
    public string AppsDetectedOne { get => _appsDetectedOne; set => SetProperty(ref _appsDetectedOne, value); }
    public string AppsDetectedMany { get => _appsDetectedMany; set => SetProperty(ref _appsDetectedMany, value); }
    public string CardDevicesTitle { get => _cardDevicesTitle; set => SetProperty(ref _cardDevicesTitle, value); }
    public string DeviceReference { get => _deviceReference; set => SetProperty(ref _deviceReference, value); }
    public string DeviceVbCable { get => _deviceVbCable; set => SetProperty(ref _deviceVbCable, value); }
    public string DeviceMicrophone { get => _deviceMicrophone; set => SetProperty(ref _deviceMicrophone, value); }
    public string DeviceMicrophoneEnable { get => _deviceMicrophoneEnable; set => SetProperty(ref _deviceMicrophoneEnable, value); }
    public string CardSpatialTitle { get => _cardSpatialTitle; set => SetProperty(ref _cardSpatialTitle, value); }
    public string CardSpatialBody { get => _cardSpatialBody; set => SetProperty(ref _cardSpatialBody, value); }
    public string CardSpatialHelp { get => _cardSpatialHelp; set => SetProperty(ref _cardSpatialHelp, value); }
    public string CardMixTitle { get => _cardMixTitle; set => SetProperty(ref _cardMixTitle, value); }
    public string MixAppsGain { get => _mixAppsGain; set => SetProperty(ref _mixAppsGain, value); }
    public string MixAppsBoost { get => _mixAppsBoost; set => SetProperty(ref _mixAppsBoost, value); }
    public string MixAppsBoostHelp { get => _mixAppsBoostHelp; set => SetProperty(ref _mixAppsBoostHelp, value); }
    public string MixAppsGainHelp { get => _mixAppsGainHelp; set => SetProperty(ref _mixAppsGainHelp, value); }
    public string MixMonitorMic { get => _mixMonitorMic; set => SetProperty(ref _mixMonitorMic, value); }
    public string NoiseGateEnable { get => _noiseGateEnable; set => SetProperty(ref _noiseGateEnable, value); }
    public string NoiseGateThreshold { get => _noiseGateThreshold; set => SetProperty(ref _noiseGateThreshold, value); }
    public string NoiseGateThresholdHelp { get => _noiseGateThresholdHelp; set => SetProperty(ref _noiseGateThresholdHelp, value); }
    public string NoiseGateAttack { get => _noiseGateAttack; set => SetProperty(ref _noiseGateAttack, value); }
    public string NoiseGateRelease { get => _noiseGateRelease; set => SetProperty(ref _noiseGateRelease, value); }
    public string NoiseGateTitle { get => _noiseGateTitle; set => SetProperty(ref _noiseGateTitle, value); }
    public string NoiseGateHelp { get => _noiseGateHelp; set => SetProperty(ref _noiseGateHelp, value); }
    public string NoiseGateOpen { get => _noiseGateOpen; set => SetProperty(ref _noiseGateOpen, value); }
    public string NoiseGateClosed { get => _noiseGateClosed; set => SetProperty(ref _noiseGateClosed, value); }
    public string NoiseGateCalibrate { get => _noiseGateCalibrate; set => SetProperty(ref _noiseGateCalibrate, value); }
    public string NoiseGateCalibrating { get => _noiseGateCalibrating; set => SetProperty(ref _noiseGateCalibrating, value); }
    public string NoiseGateAdvanced { get => _noiseGateAdvanced; set => SetProperty(ref _noiseGateAdvanced, value); }
    public string NoiseGateOutput { get => _noiseGateOutput; set => SetProperty(ref _noiseGateOutput, value); }
    public string NoiseGateAttackHelp { get => _noiseGateAttackHelp; set => SetProperty(ref _noiseGateAttackHelp, value); }
    public string NoiseGateReleaseHelp { get => _noiseGateReleaseHelp; set => SetProperty(ref _noiseGateReleaseHelp, value); }
    public string CardOscTitle { get => _cardOscTitle; set => SetProperty(ref _cardOscTitle, value); }
    public string OscEnableChatbox { get => _oscEnableChatbox; set => SetProperty(ref _oscEnableChatbox, value); }
    public string OscTemplateLabel { get => _oscTemplateLabel; set => SetProperty(ref _oscTemplateLabel, value); }
    public string OscTemplateWatermark { get => _oscTemplateWatermark; set => SetProperty(ref _oscTemplateWatermark, value); }
    public string OscTemplateTokens { get => _oscTemplateTokens; set => SetProperty(ref _oscTemplateTokens, value); }
    public string CardLanguageTitle { get => _cardLanguageTitle; set => SetProperty(ref _cardLanguageTitle, value); }
    public string CardLanguageHelp { get => _cardLanguageHelp; set => SetProperty(ref _cardLanguageHelp, value); }
    public string UpdateConsentTitle { get => _updateConsentTitle; set => SetProperty(ref _updateConsentTitle, value); }
    public string UpdateConsentBody { get => _updateConsentBody; set => SetProperty(ref _updateConsentBody, value); }
    public string UpdateAllow { get => _updateAllow; set => SetProperty(ref _updateAllow, value); }
    public string UpdateDecline { get => _updateDecline; set => SetProperty(ref _updateDecline, value); }
    public string UpdateAvailable { get => _updateAvailable; set => SetProperty(ref _updateAvailable, value); }
    public string UpdateOpen { get => _updateOpen; set => SetProperty(ref _updateOpen, value); }
    public string UpdateSettingsTitle { get => _updateSettingsTitle; set => SetProperty(ref _updateSettingsTitle, value); }
    public string UpdateEnabled { get => _updateEnabled; set => SetProperty(ref _updateEnabled, value); }
    public string UpdateCheckNow { get => _updateCheckNow; set => SetProperty(ref _updateCheckNow, value); }
    public string SettingsTitle { get => _settingsTitle; set => SetProperty(ref _settingsTitle, value); }
    public string UpdateDismiss { get => _updateDismiss; set => SetProperty(ref _updateDismiss, value); }
    public string HotkeyTitle { get => _hotkeyTitle; set => SetProperty(ref _hotkeyTitle, value); }
    public string HotkeyHelp { get => _hotkeyHelp; set => SetProperty(ref _hotkeyHelp, value); }
    public string HotkeyToggleRouting { get => _hotkeyToggleRouting; set => SetProperty(ref _hotkeyToggleRouting, value); }
    public string HotkeyStartRouting { get => _hotkeyStartRouting; set => SetProperty(ref _hotkeyStartRouting, value); }
    public string HotkeyStopRouting { get => _hotkeyStopRouting; set => SetProperty(ref _hotkeyStopRouting, value); }
    public string HotkeyToggleMicrophone { get => _hotkeyToggleMicrophone; set => SetProperty(ref _hotkeyToggleMicrophone, value); }
    public string HotkeyNotAssigned { get => _hotkeyNotAssigned; set => SetProperty(ref _hotkeyNotAssigned, value); }
    public string HotkeyClear { get => _hotkeyClear; set => SetProperty(ref _hotkeyClear, value); }
    public string HotkeyAssign { get => _hotkeyAssign; set => SetProperty(ref _hotkeyAssign, value); }
    public string HotkeyChange { get => _hotkeyChange; set => SetProperty(ref _hotkeyChange, value); }
    public string HotkeyRecordingKeystrokes { get => _hotkeyRecordingKeystrokes; set => SetProperty(ref _hotkeyRecordingKeystrokes, value); }
    public string HotkeyEscapeToCancel { get => _hotkeyEscapeToCancel; set => SetProperty(ref _hotkeyEscapeToCancel, value); }
    public string HotkeyRoutingGroup { get => _hotkeyRoutingGroup; set => SetProperty(ref _hotkeyRoutingGroup, value); }
    public string HotkeyMicrophoneGroup { get => _hotkeyMicrophoneGroup; set => SetProperty(ref _hotkeyMicrophoneGroup, value); }
    public string MicrophoneEnable { get => _microphoneEnable; set => SetProperty(ref _microphoneEnable, value); }
    public string MicrophoneDisable { get => _microphoneDisable; set => SetProperty(ref _microphoneDisable, value); }
    public string HotkeyConflict => G(LK.HotkeyConflict);
    public string HotkeyCancel => G(LK.HotkeyCancel);
    public string HotkeyRegistrationFailed => G(LK.HotkeyRegistrationFailed);
    public string UpdateChecking => G(LK.UpdateChecking);
    public string UpdateCurrent => G(LK.UpdateCurrent);
    public string UpdateCheckFailed => G(LK.UpdateCheckFailed);
    public string UpdateOpenFailed => G(LK.UpdateOpenFailed);

    public string AppCaptureError => G(LK.AppCaptureError);

    /// <summary>
    /// Llamado por el MainViewModel cuando cambia el idioma. Dispara PropertyChanged
    /// en cada propiedad para refrescar todos los bindings de la UI.
    /// </summary>
    public void RaiseAll()
    {
        AppTitle = G(LK.AppTitle);
        AppFooter = FormatAppFooter();
        StatusRouting = G(LK.StatusRouting);
        StatusInactive = G(LK.StatusInactive);
        StatusMicrophoneActive = G(LK.StatusMicrophoneActive);
        StatusMicrophoneInactive = G(LK.StatusMicrophoneInactive);
        ButtonStart = G(LK.ButtonStart);
        ButtonStop = G(LK.ButtonStop);
        ButtonRefreshApps = G(LK.ButtonRefreshApps);
        CardAppsTitle = G(LK.CardAppsTitle);
        CardAppsHelp = G(LK.CardAppsHelp);
        AppsDetectedOne = G(LK.AppsDetectedOne);
        AppsDetectedMany = G(LK.AppsDetectedMany);
        CardDevicesTitle = G(LK.CardDevicesTitle);
        DeviceReference = G(LK.DeviceReference);
        DeviceVbCable = G(LK.DeviceVbCable);
        DeviceMicrophone = G(LK.DeviceMicrophone);
        DeviceMicrophoneEnable = G(LK.DeviceMicrophoneEnable);
        CardSpatialTitle = G(LK.CardSpatialInfoTitle);
        CardSpatialBody = G(LK.CardSpatialInfoBody);
        CardSpatialHelp = G(LK.CardSpatialInfoHelp);
        CardMixTitle = G(LK.CardMixTitle);
        MixAppsGain = G(LK.MixAppsGain);
        MixAppsBoost = G(LK.MixAppsBoost);
        MixAppsBoostHelp = G(LK.MixAppsBoostHelp);
        MixAppsGainHelp = G(LK.MixAppsGainHelp);
        MixMonitorMic = G(LK.MixMonitorMic);
        NoiseGateEnable = G(LK.NoiseGateEnable);
        NoiseGateThreshold = G(LK.NoiseGateThreshold);
        NoiseGateThresholdHelp = G(LK.NoiseGateThresholdHelp);
        NoiseGateAttack = G(LK.NoiseGateAttack);
        NoiseGateRelease = G(LK.NoiseGateRelease);
        NoiseGateTitle = G(LK.NoiseGateTitle);
        NoiseGateHelp = G(LK.NoiseGateHelp);
        NoiseGateOpen = G(LK.NoiseGateOpen);
        NoiseGateClosed = G(LK.NoiseGateClosed);
        NoiseGateCalibrate = G(LK.NoiseGateCalibrate);
        NoiseGateCalibrating = G(LK.NoiseGateCalibrating);
        NoiseGateAdvanced = G(LK.NoiseGateAdvanced);
        NoiseGateOutput = G(LK.NoiseGateOutput);
        NoiseGateAttackHelp = G(LK.NoiseGateAttackHelp);
        NoiseGateReleaseHelp = G(LK.NoiseGateReleaseHelp);
        CardOscTitle = G(LK.CardOscTitle);
        OscEnableChatbox = G(LK.OscEnableChatbox);
        OscTemplateLabel = G(LK.OscTemplateLabel);
        OscTemplateWatermark = G(LK.OscTemplateWatermark);
        OscTemplateTokens = G(LK.OscTemplateTokens);
        CardLanguageTitle = G(LK.CardLanguageTitle);
        CardLanguageHelp = G(LK.CardLanguageHelp);
        UpdateConsentTitle = G(LK.UpdateConsentTitle);
        UpdateConsentBody = G(LK.UpdateConsentBody);
        UpdateAllow = G(LK.UpdateAllow);
        UpdateDecline = G(LK.UpdateDecline);
        UpdateAvailable = G(LK.UpdateAvailable);
        UpdateOpen = G(LK.UpdateOpen);
        UpdateSettingsTitle = G(LK.UpdateSettingsTitle);
        UpdateEnabled = G(LK.UpdateEnabled);
        UpdateCheckNow = G(LK.UpdateCheckNow);
        SettingsTitle = G(LK.SettingsTitle);
        UpdateDismiss = G(LK.UpdateDismiss);
        HotkeyTitle = G(LK.HotkeyTitle);
        HotkeyHelp = G(LK.HotkeyHelp);
        HotkeyToggleRouting = G(LK.HotkeyToggleRouting);
        HotkeyStartRouting = G(LK.HotkeyStartRouting);
        HotkeyStopRouting = G(LK.HotkeyStopRouting);
        HotkeyToggleMicrophone = G(LK.HotkeyToggleMicrophone);
        HotkeyNotAssigned = G(LK.HotkeyNotAssigned);
        HotkeyClear = G(LK.HotkeyClear);
        HotkeyAssign = G(LK.HotkeyAssign);
        HotkeyChange = G(LK.HotkeyChange);
        HotkeyRecordingKeystrokes = G(LK.HotkeyRecordingKeystrokes);
        HotkeyEscapeToCancel = G(LK.HotkeyEscapeToCancel);
        HotkeyRoutingGroup = G(LK.HotkeyRoutingGroup);
        HotkeyMicrophoneGroup = G(LK.HotkeyMicrophoneGroup);
        MicrophoneEnable = G(LK.MicrophoneEnable);
        MicrophoneDisable = G(LK.MicrophoneDisable);
        OnPropertyChanged(nameof(HotkeyConflict));
        OnPropertyChanged(nameof(HotkeyCancel));
        OnPropertyChanged(nameof(HotkeyRegistrationFailed));
        OnPropertyChanged(nameof(UpdateChecking));
        OnPropertyChanged(nameof(UpdateCurrent));
        OnPropertyChanged(nameof(UpdateCheckFailed));
        OnPropertyChanged(nameof(UpdateOpenFailed));
        OnPropertyChanged(nameof(AppCaptureError));
    }

    private static string G(string key) => LocalizationService.Instance.Get(key);
    private static string FormatAppFooter() => string.Format(G(LK.AppFooter), AppVersion.Display);
    private static class LK
    {
        internal const string AppTitle = LocalizationService.Keys.AppTitle;
        internal const string AppFooter = LocalizationService.Keys.AppFooter;
        internal const string StatusRouting = LocalizationService.Keys.StatusRouting; 
        internal const string StatusInactive = LocalizationService.Keys.StatusInactive; 
        internal const string StatusMicrophoneActive = LocalizationService.Keys.StatusMicrophoneActive;
        internal const string StatusMicrophoneInactive = LocalizationService.Keys.StatusMicrophoneInactive;
        internal const string ButtonStart = LocalizationService.Keys.ButtonStart; 
        internal const string ButtonStop = LocalizationService.Keys.ButtonStop; 
        internal const string ButtonRefreshApps = LocalizationService.Keys.ButtonRefreshApps; 
        internal const string CardAppsTitle = LocalizationService.Keys.CardAppsTitle; 
        internal const string CardAppsHelp = LocalizationService.Keys.CardAppsHelp; 
        internal const string AppsDetectedOne = LocalizationService.Keys.AppsDetectedOne;
        internal const string AppsDetectedMany = LocalizationService.Keys.AppsDetectedMany;
        internal const string AppCaptureError = LocalizationService.Keys.AppCaptureError;
        internal const string CardDevicesTitle = LocalizationService.Keys.CardDevicesTitle; 
        internal const string DeviceReference = LocalizationService.Keys.DeviceReference; 
        internal const string DeviceVbCable = LocalizationService.Keys.DeviceVbCable; 
        internal const string DeviceMicrophone = LocalizationService.Keys.DeviceMicrophone; 
        internal const string DeviceMicrophoneEnable = LocalizationService.Keys.DeviceMicrophoneEnable;
        internal const string CardSpatialInfoTitle = LocalizationService.Keys.CardSpatialInfoTitle; 
        internal const string CardSpatialInfoBody = LocalizationService.Keys.CardSpatialInfoBody; 
        internal const string CardSpatialInfoHelp = LocalizationService.Keys.CardSpatialInfoHelp; 
        internal const string CardMixTitle = LocalizationService.Keys.CardMixTitle; 
        internal const string MixAppsGain = LocalizationService.Keys.MixAppsGain; 
        internal const string MixAppsBoost = LocalizationService.Keys.MixAppsBoost; 
        internal const string MixAppsBoostHelp = LocalizationService.Keys.MixAppsBoostHelp; 
        internal const string MixAppsGainHelp = LocalizationService.Keys.MixAppsGainHelp; 
        internal const string MixMonitorMic = LocalizationService.Keys.MixMonitorMic; 
        internal const string NoiseGateEnable = LocalizationService.Keys.NoiseGateEnable;
        internal const string NoiseGateThreshold = LocalizationService.Keys.NoiseGateThreshold;
        internal const string NoiseGateThresholdHelp = LocalizationService.Keys.NoiseGateThresholdHelp;
        internal const string NoiseGateAttack = LocalizationService.Keys.NoiseGateAttack;
        internal const string NoiseGateRelease = LocalizationService.Keys.NoiseGateRelease;
        internal const string NoiseGateTitle = LocalizationService.Keys.NoiseGateTitle;
        internal const string NoiseGateHelp = LocalizationService.Keys.NoiseGateHelp;
        internal const string NoiseGateOpen = LocalizationService.Keys.NoiseGateOpen;
        internal const string NoiseGateClosed = LocalizationService.Keys.NoiseGateClosed;
        internal const string NoiseGateCalibrate = LocalizationService.Keys.NoiseGateCalibrate;
        internal const string NoiseGateCalibrating = LocalizationService.Keys.NoiseGateCalibrating;
        internal const string NoiseGateAdvanced = LocalizationService.Keys.NoiseGateAdvanced;
        internal const string NoiseGateOutput = LocalizationService.Keys.NoiseGateOutput;
        internal const string NoiseGateAttackHelp = LocalizationService.Keys.NoiseGateAttackHelp;
        internal const string NoiseGateReleaseHelp = LocalizationService.Keys.NoiseGateReleaseHelp;
        internal const string CardOscTitle = LocalizationService.Keys.CardOscTitle; 
        internal const string OscEnableChatbox = LocalizationService.Keys.OscEnableChatbox; 
        internal const string OscTemplateLabel = LocalizationService.Keys.OscTemplateLabel; 
        internal const string OscTemplateWatermark = LocalizationService.Keys.OscTemplateWatermark; 
        internal const string OscTemplateTokens = LocalizationService.Keys.OscTemplateTokens; 
        internal const string CardLanguageTitle = LocalizationService.Keys.CardLanguageTitle; 
        internal const string CardLanguageHelp = LocalizationService.Keys.CardLanguageHelp;
        internal const string UpdateConsentTitle = LocalizationService.Keys.UpdateConsentTitle;
        internal const string UpdateConsentBody = LocalizationService.Keys.UpdateConsentBody;
        internal const string UpdateAllow = LocalizationService.Keys.UpdateAllow;
        internal const string UpdateDecline = LocalizationService.Keys.UpdateDecline;
        internal const string UpdateAvailable = LocalizationService.Keys.UpdateAvailable;
        internal const string UpdateOpen = LocalizationService.Keys.UpdateOpen;
        internal const string UpdateSettingsTitle = LocalizationService.Keys.UpdateSettingsTitle;
        internal const string UpdateEnabled = LocalizationService.Keys.UpdateEnabled;
        internal const string UpdateCheckNow = LocalizationService.Keys.UpdateCheckNow;
        internal const string UpdateChecking = LocalizationService.Keys.UpdateChecking;
        internal const string UpdateCurrent = LocalizationService.Keys.UpdateCurrent;
        internal const string UpdateCheckFailed = LocalizationService.Keys.UpdateCheckFailed;
        internal const string UpdateOpenFailed = LocalizationService.Keys.UpdateOpenFailed;
        internal const string SettingsTitle = LocalizationService.Keys.SettingsTitle;
        internal const string UpdateDismiss = LocalizationService.Keys.UpdateDismiss;
        internal const string HotkeyTitle = LocalizationService.Keys.HotkeyTitle;
        internal const string HotkeyHelp = LocalizationService.Keys.HotkeyHelp;
        internal const string HotkeyToggleRouting = LocalizationService.Keys.HotkeyToggleRouting;
        internal const string HotkeyStartRouting = LocalizationService.Keys.HotkeyStartRouting;
        internal const string HotkeyStopRouting = LocalizationService.Keys.HotkeyStopRouting;
        internal const string HotkeyToggleMicrophone = LocalizationService.Keys.HotkeyToggleMicrophone;
        internal const string HotkeyRegistrationFailed = LocalizationService.Keys.HotkeyRegistrationFailed;
        internal const string HotkeyNotAssigned = LocalizationService.Keys.HotkeyNotAssigned;
        internal const string HotkeyClear = LocalizationService.Keys.HotkeyClear;
        internal const string HotkeyCancel = LocalizationService.Keys.HotkeyCancel;
        internal const string HotkeyAssign = LocalizationService.Keys.HotkeyAssign;
        internal const string HotkeyChange = LocalizationService.Keys.HotkeyChange;
        internal const string HotkeyRecordingKeystrokes = LocalizationService.Keys.HotkeyRecordingKeystrokes;
        internal const string HotkeyEscapeToCancel = LocalizationService.Keys.HotkeyEscapeToCancel;
        internal const string HotkeyConflict = LocalizationService.Keys.HotkeyConflict;
        internal const string HotkeyRoutingGroup = LocalizationService.Keys.HotkeyRoutingGroup;
        internal const string HotkeyMicrophoneGroup = LocalizationService.Keys.HotkeyMicrophoneGroup;
        internal const string MicrophoneEnable = LocalizationService.Keys.MicrophoneEnable;
        internal const string MicrophoneDisable = LocalizationService.Keys.MicrophoneDisable;
    }
}

