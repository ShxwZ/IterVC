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
    private string _appFooter = G(LK.AppFooter);
    private string _statusRouting = G(LK.StatusRouting);
    private string _statusInactive = G(LK.StatusInactive);
    private string _buttonStart = G(LK.ButtonStart);
    private string _buttonStop = G(LK.ButtonStop);
    private string _buttonRefreshApps = G(LK.ButtonRefreshApps);
    private string _cardAppsTitle = G(LK.CardAppsTitle);
    private string _cardAppsHelp = G(LK.CardAppsHelp);
    private string _cardDevicesTitle = G(LK.CardDevicesTitle);
    private string _deviceReference = G(LK.DeviceReference);
    private string _deviceVbCable = G(LK.DeviceVbCable);
    private string _deviceMicrophone = G(LK.DeviceMicrophone);
    private string _cardSpatialTitle = G(LK.CardSpatialInfoTitle);
    private string _cardSpatialBody = G(LK.CardSpatialInfoBody);
    private string _cardSpatialHelp = G(LK.CardSpatialInfoHelp);
    private string _cardMixTitle = G(LK.CardMixTitle);
    private string _mixAppsGain = G(LK.MixAppsGain);
    private string _mixAppsBoost = G(LK.MixAppsBoost);
    private string _mixAppsBoostHelp = G(LK.MixAppsBoostHelp);
    private string _mixAppsGainHelp = G(LK.MixAppsGainHelp);
    private string _mixMonitorMic = G(LK.MixMonitorMic);
    private string _cardOscTitle = G(LK.CardOscTitle);
    private string _oscEnableChatbox = G(LK.OscEnableChatbox);
    private string _oscTemplateLabel = G(LK.OscTemplateLabel);
    private string _oscTemplateWatermark = G(LK.OscTemplateWatermark);
    private string _oscTemplateTokens = G(LK.OscTemplateTokens);
    private string _cardLanguageTitle = G(LK.CardLanguageTitle);
    private string _cardLanguageHelp = G(LK.CardLanguageHelp);
    private string _cardAppearanceTitle = G(LK.CardAppearanceTitle);
    private string _appearanceBackground = G(LK.AppearanceBackground);
    private string _appearancePickImage = G(LK.AppearancePickImage);
    private string _appearanceRemoveImage = G(LK.AppearanceRemoveImage);
    private string _appearanceAccentColor = G(LK.AppearanceAccentColor);
    private string _appearanceCardColor = G(LK.AppearanceCardColor);
    private string _appearanceAutoExtract = G(LK.AppearanceAutoExtract);
    private string _appearanceResetDefaults = G(LK.AppearanceResetDefaults);
    private string _cardShortcutsTitle = G(LK.CardShortcutsTitle);
    private string _shortcutToggleLabel = G(LK.ShortcutToggleLabel);
    private string _shortcutToggleHelp = G(LK.ShortcutToggleHelp);

    public string AppTitle { get => _appTitle; set => SetProperty(ref _appTitle, value); }
    public string AppFooter { get => _appFooter; set => SetProperty(ref _appFooter, value); }
    public string StatusRouting { get => _statusRouting; set => SetProperty(ref _statusRouting, value); }
    public string StatusInactive { get => _statusInactive; set => SetProperty(ref _statusInactive, value); }
    public string ButtonStart { get => _buttonStart; set => SetProperty(ref _buttonStart, value); }
    public string ButtonStop { get => _buttonStop; set => SetProperty(ref _buttonStop, value); }
    public string ButtonRefreshApps { get => _buttonRefreshApps; set => SetProperty(ref _buttonRefreshApps, value); }
    public string CardAppsTitle { get => _cardAppsTitle; set => SetProperty(ref _cardAppsTitle, value); }
    public string CardAppsHelp { get => _cardAppsHelp; set => SetProperty(ref _cardAppsHelp, value); }
    public string CardDevicesTitle { get => _cardDevicesTitle; set => SetProperty(ref _cardDevicesTitle, value); }
    public string DeviceReference { get => _deviceReference; set => SetProperty(ref _deviceReference, value); }
    public string DeviceVbCable { get => _deviceVbCable; set => SetProperty(ref _deviceVbCable, value); }
    public string DeviceMicrophone { get => _deviceMicrophone; set => SetProperty(ref _deviceMicrophone, value); }
    public string CardSpatialTitle { get => _cardSpatialTitle; set => SetProperty(ref _cardSpatialTitle, value); }
    public string CardSpatialBody { get => _cardSpatialBody; set => SetProperty(ref _cardSpatialBody, value); }
    public string CardSpatialHelp { get => _cardSpatialHelp; set => SetProperty(ref _cardSpatialHelp, value); }
    public string CardMixTitle { get => _cardMixTitle; set => SetProperty(ref _cardMixTitle, value); }
    public string MixAppsGain { get => _mixAppsGain; set => SetProperty(ref _mixAppsGain, value); }
    public string MixAppsBoost { get => _mixAppsBoost; set => SetProperty(ref _mixAppsBoost, value); }
    public string MixAppsBoostHelp { get => _mixAppsBoostHelp; set => SetProperty(ref _mixAppsBoostHelp, value); }
    public string MixAppsGainHelp { get => _mixAppsGainHelp; set => SetProperty(ref _mixAppsGainHelp, value); }
    public string MixMonitorMic { get => _mixMonitorMic; set => SetProperty(ref _mixMonitorMic, value); }
    public string CardOscTitle { get => _cardOscTitle; set => SetProperty(ref _cardOscTitle, value); }
    public string OscEnableChatbox { get => _oscEnableChatbox; set => SetProperty(ref _oscEnableChatbox, value); }
    public string OscTemplateLabel { get => _oscTemplateLabel; set => SetProperty(ref _oscTemplateLabel, value); }
    public string OscTemplateWatermark { get => _oscTemplateWatermark; set => SetProperty(ref _oscTemplateWatermark, value); }
    public string OscTemplateTokens { get => _oscTemplateTokens; set => SetProperty(ref _oscTemplateTokens, value); }
    public string CardLanguageTitle { get => _cardLanguageTitle; set => SetProperty(ref _cardLanguageTitle, value); }
    public string CardLanguageHelp { get => _cardLanguageHelp; set => SetProperty(ref _cardLanguageHelp, value); }
    public string CardAppearanceTitle { get => _cardAppearanceTitle; set => SetProperty(ref _cardAppearanceTitle, value); }
    public string AppearanceBackground { get => _appearanceBackground; set => SetProperty(ref _appearanceBackground, value); }
    public string AppearancePickImage { get => _appearancePickImage; set => SetProperty(ref _appearancePickImage, value); }
    public string AppearanceRemoveImage { get => _appearanceRemoveImage; set => SetProperty(ref _appearanceRemoveImage, value); }
    public string AppearanceAccentColor { get => _appearanceAccentColor; set => SetProperty(ref _appearanceAccentColor, value); }
    public string AppearanceCardColor { get => _appearanceCardColor; set => SetProperty(ref _appearanceCardColor, value); }
    public string AppearanceAutoExtract { get => _appearanceAutoExtract; set => SetProperty(ref _appearanceAutoExtract, value); }
    public string AppearanceResetDefaults { get => _appearanceResetDefaults; set => SetProperty(ref _appearanceResetDefaults, value); }
    public string CardShortcutsTitle { get => _cardShortcutsTitle; set => SetProperty(ref _cardShortcutsTitle, value); }
    public string ShortcutToggleLabel { get => _shortcutToggleLabel; set => SetProperty(ref _shortcutToggleLabel, value); }
    public string ShortcutToggleHelp { get => _shortcutToggleHelp; set => SetProperty(ref _shortcutToggleHelp, value); }

    /// <summary>Etiqueta del dispositivo "ninguno" en la lista de microfnos.</summary>
    public string MicrophoneNoneLabel => LocalizationService.Instance.Get(LK.DeviceNoneMic);

    /// <summary>Prefijo del mensaje "No se pudo capturar el proceso N".</summary>
    public string AppCaptureErrorPrefix => G(LK.ButtonStart) + ":";

    /// <summary>
    /// Llamado por el MainViewModel cuando cambia el idioma. Dispara PropertyChanged
    /// en cada propiedad para refrescar todos los bindings de la UI.
    /// </summary>
    public void RaiseAll()
    {
        AppTitle = G(LK.AppTitle);
        AppFooter = G(LK.AppFooter);
        StatusRouting = G(LK.StatusRouting);
        StatusInactive = G(LK.StatusInactive);
        ButtonStart = G(LK.ButtonStart);
        ButtonStop = G(LK.ButtonStop);
        ButtonRefreshApps = G(LK.ButtonRefreshApps);
        CardAppsTitle = G(LK.CardAppsTitle);
        CardAppsHelp = G(LK.CardAppsHelp);
        CardDevicesTitle = G(LK.CardDevicesTitle);
        DeviceReference = G(LK.DeviceReference);
        DeviceVbCable = G(LK.DeviceVbCable);
        DeviceMicrophone = G(LK.DeviceMicrophone);
        CardSpatialTitle = G(LK.CardSpatialInfoTitle);
        CardSpatialBody = G(LK.CardSpatialInfoBody);
        CardSpatialHelp = G(LK.CardSpatialInfoHelp);
        CardMixTitle = G(LK.CardMixTitle);
        MixAppsGain = G(LK.MixAppsGain);
        MixAppsBoost = G(LK.MixAppsBoost);
        MixAppsBoostHelp = G(LK.MixAppsBoostHelp);
        MixAppsGainHelp = G(LK.MixAppsGainHelp);
        MixMonitorMic = G(LK.MixMonitorMic);
        CardOscTitle = G(LK.CardOscTitle);
        OscEnableChatbox = G(LK.OscEnableChatbox);
        OscTemplateLabel = G(LK.OscTemplateLabel);
        OscTemplateWatermark = G(LK.OscTemplateWatermark);
        OscTemplateTokens = G(LK.OscTemplateTokens);
        CardLanguageTitle = G(LK.CardLanguageTitle);
        CardLanguageHelp = G(LK.CardLanguageHelp);
        CardAppearanceTitle = G(LK.CardAppearanceTitle);
        AppearanceBackground = G(LK.AppearanceBackground);
        AppearancePickImage = G(LK.AppearancePickImage);
        AppearanceRemoveImage = G(LK.AppearanceRemoveImage);
        AppearanceAccentColor = G(LK.AppearanceAccentColor);
        AppearanceCardColor = G(LK.AppearanceCardColor);
        AppearanceAutoExtract = G(LK.AppearanceAutoExtract);
        AppearanceResetDefaults = G(LK.AppearanceResetDefaults);
        CardShortcutsTitle = G(LK.CardShortcutsTitle);
        ShortcutToggleLabel = G(LK.ShortcutToggleLabel);
        ShortcutToggleHelp = G(LK.ShortcutToggleHelp);
        OnPropertyChanged(nameof(MicrophoneNoneLabel));
        OnPropertyChanged(nameof(AppCaptureErrorPrefix));
    }

    private static string G(string key) => LocalizationService.Instance.Get(key);
    private static class LK
    {
        internal const string AppTitle = LocalizationService.Keys.AppTitle;
        internal const string AppFooter = LocalizationService.Keys.AppFooter;
        internal const string StatusRouting = LocalizationService.Keys.StatusRouting; 
        internal const string StatusInactive = LocalizationService.Keys.StatusInactive; 
        internal const string ButtonStart = LocalizationService.Keys.ButtonStart; 
        internal const string ButtonStop = LocalizationService.Keys.ButtonStop; 
        internal const string ButtonRefreshApps = LocalizationService.Keys.ButtonRefreshApps; 
        internal const string CardAppsTitle = LocalizationService.Keys.CardAppsTitle; 
        internal const string CardAppsHelp = LocalizationService.Keys.CardAppsHelp; 
        internal const string CardDevicesTitle = LocalizationService.Keys.CardDevicesTitle; 
        internal const string DeviceReference = LocalizationService.Keys.DeviceReference; 
        internal const string DeviceVbCable = LocalizationService.Keys.DeviceVbCable; 
        internal const string DeviceMicrophone = LocalizationService.Keys.DeviceMicrophone; 
        internal const string DeviceNoneMic = LocalizationService.Keys.DeviceNoneMic; 
        internal const string CardSpatialInfoTitle = LocalizationService.Keys.CardSpatialInfoTitle; 
        internal const string CardSpatialInfoBody = LocalizationService.Keys.CardSpatialInfoBody; 
        internal const string CardSpatialInfoHelp = LocalizationService.Keys.CardSpatialInfoHelp; 
        internal const string CardMixTitle = LocalizationService.Keys.CardMixTitle; 
        internal const string MixAppsGain = LocalizationService.Keys.MixAppsGain; 
        internal const string MixAppsBoost = LocalizationService.Keys.MixAppsBoost; 
        internal const string MixAppsBoostHelp = LocalizationService.Keys.MixAppsBoostHelp; 
        internal const string MixAppsGainHelp = LocalizationService.Keys.MixAppsGainHelp; 
        internal const string MixMonitorMic = LocalizationService.Keys.MixMonitorMic; 
        internal const string CardOscTitle = LocalizationService.Keys.CardOscTitle; 
        internal const string OscEnableChatbox = LocalizationService.Keys.OscEnableChatbox; 
        internal const string OscTemplateLabel = LocalizationService.Keys.OscTemplateLabel; 
        internal const string OscTemplateWatermark = LocalizationService.Keys.OscTemplateWatermark; 
        internal const string OscTemplateTokens = LocalizationService.Keys.OscTemplateTokens; 
        internal const string CardLanguageTitle = LocalizationService.Keys.CardLanguageTitle; 
        internal const string CardLanguageHelp = LocalizationService.Keys.CardLanguageHelp;
        internal const string CardAppearanceTitle = LocalizationService.Keys.CardAppearanceTitle;
        internal const string AppearanceBackground = LocalizationService.Keys.AppearanceBackground;
        internal const string AppearancePickImage = LocalizationService.Keys.AppearancePickImage;
        internal const string AppearanceRemoveImage = LocalizationService.Keys.AppearanceRemoveImage;
        internal const string AppearanceAccentColor = LocalizationService.Keys.AppearanceAccentColor;
        internal const string AppearanceCardColor = LocalizationService.Keys.AppearanceCardColor;
        internal const string AppearanceAutoExtract = LocalizationService.Keys.AppearanceAutoExtract;
        internal const string AppearanceResetDefaults = LocalizationService.Keys.AppearanceResetDefaults;
        internal const string CardShortcutsTitle = LocalizationService.Keys.CardShortcutsTitle;
        internal const string ShortcutToggleLabel = LocalizationService.Keys.ShortcutToggleLabel;
        internal const string ShortcutToggleHelp = LocalizationService.Keys.ShortcutToggleHelp;
    }
}

