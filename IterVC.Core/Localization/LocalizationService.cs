namespace IterVC.Core.Localization;

/// <summary>
/// Implementación singleton del <see cref="ILocalizationService"/>.
/// Mantiene los pares clave/valor para cada idioma en memoria y elige el idioma
/// activo. Los diccionarios están escritos a mano aquí (no usamos archivos
/// .resx) para evitar recompilar el XAML al añadir entradas.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    /// <summary>Claves centralizadas para evitar typos. Usar siempre estas constantes en el código.</summary>
    public static class Keys
    {
        public const string AppTitle = "app.title";
        public const string AppFooter = "app.footer";
        public const string StatusRouting = "status.routing";
        public const string StatusInactive = "status.inactive";
        public const string ButtonStart = "button.start";
        public const string ButtonStop = "button.stop";
        public const string ButtonRefreshApps = "button.refresh_apps";
        public const string CardAppsTitle = "card.apps.title";
        public const string CardAppsHelp = "card.apps.help";
        public const string CardDevicesTitle = "card.devices.title";
        public const string DeviceReference = "device.reference";
        public const string DeviceVbCable = "device.vbcable";
        public const string DeviceMicrophone = "device.microphone";
        public const string DeviceNoneMic = "device.mic_none";
        public const string CardSpatialInfoTitle = "card.spatial.title";
        public const string CardSpatialInfoBody = "card.spatial.body";
        public const string CardSpatialInfoHelp = "card.spatial.help";
        public const string CardMixTitle = "card.mix.title";
        public const string MixAppsGain = "mix.apps_gain";
        public const string MixAppsBoost = "mix.apps_boost";
        public const string MixAppsBoostHelp = "mix.apps_boost.help";
        public const string MixAppsGainHelp = "mix.apps_gain.help";
        public const string MixMonitorMic = "mix.monitor_mic";
        public const string CardOscTitle = "card.osc.title";
        public const string OscEnableChatbox = "osc.enable_chatbox";
        public const string OscTemplateLabel = "osc.template_label";
        public const string OscTemplateWatermark = "osc.template_watermark";
        public const string OscTemplateTokens = "osc.template_tokens";
        public const string CardLanguageTitle = "card.language.title";
        public const string CardLanguageHelp = "card.language.help";
        public const string MediaPlayingStatus = "media.playing";
        public const string MediaPausedStatus = "media.paused";
        public const string MediaStoppedStatus = "media.stopped";
        public const string MediaNothingPlaying = "media.nothing_playing";

        // Appearance tab
        public const string CardAppearanceTitle = "card.appearance.title";
        public const string AppearanceBackground = "appearance.background";
        public const string AppearancePickImage = "appearance.pick_image";
        public const string AppearanceRemoveImage = "appearance.remove_image";
        public const string AppearanceAccentColor = "appearance.accent_color";
        public const string AppearanceCardColor = "appearance.card_color";
        public const string AppearanceAutoExtract = "appearance.auto_extract";
        public const string AppearanceResetDefaults = "appearance.reset_defaults";

        // Shortcuts tab
        public const string CardShortcutsTitle = "card.shortcuts.title";
        public const string ShortcutToggleLabel = "shortcut.toggle.label";
        public const string ShortcutToggleHelp = "shortcut.toggle.help";
        public const string ShortcutGlobalHint = "shortcut.global.hint";
        public const string ShortcutGlobalFailed = "shortcut.global.failed";

        // System tray
        public const string TrayShowWindow = "tray.show_window";
        public const string TrayExit = "tray.exit";
        public const string BehaviorMinimizeToTray = "behavior.minimize_to_tray";
    }

    private readonly Dictionary<string, string> _es = new()
    {
        [Keys.AppTitle] = "IterVC",
        [Keys.AppFooter] = "IterVC - 0.1-v15172926",
        [Keys.StatusRouting] = "ENRUTANDO",
        [Keys.StatusInactive] = "INACTIVO",
        [Keys.ButtonStart] = "Iniciar",
        [Keys.ButtonStop] = "Detener",
        [Keys.ButtonRefreshApps] = "Actualizar apps",
        [Keys.CardAppsTitle] = "APLICACIONES CON AUDIO",
        [Keys.CardAppsHelp] = "Marca las apps que quieres mezclar en el micrófono virtual.",
        [Keys.CardDevicesTitle] = "DISPOSITIVOS",
        [Keys.DeviceReference] = "Referencia (detectar apps)",
        [Keys.DeviceVbCable] = "Micrófono virtual (VB-Cable)",
        [Keys.DeviceMicrophone] = "Micrófono físico",
        [Keys.DeviceNoneMic] = "[Ninguno - Micrófono desactivado]",
        [Keys.CardSpatialInfoTitle] = "* Audio espacial del dispositivo de referencia",
        [Keys.CardSpatialInfoBody] = "Tu dispositivo puede tener Dolby Atmos for Headphones, Windows Sonic o DTS Sound Unbound activos. Esos efectos espaciales se aplican al audio de tus auriculares Y al audio que IterVC captura por proceso.",
        [Keys.CardSpatialInfoHelp] = "Para obtener señal estéreo limpia en VB-Cable: abre Configuración → Sistema → Sonido → tu dispositivo → Formato de audio espacial → Off.",
        [Keys.CardMixTitle] = "MEZCLA",
        [Keys.MixAppsGain] = "Ganancia de apps",
        [Keys.MixAppsGainHelp] = "Valores sobre 100% amplifican la señal.",
        [Keys.MixAppsBoost] = "Ganancia del micrófono",
        [Keys.MixAppsBoostHelp] = "Multiplicador extra aplicado al volumen del micro (1× = sin ganancia).",
        [Keys.MixMonitorMic] = "Monitorizar micrófono",
        [Keys.CardOscTitle] = "OSC - VRCHAT",
        [Keys.OscEnableChatbox] = "Enviar info al Chatbox",
        [Keys.OscTemplateLabel] = "Plantilla",
        [Keys.OscTemplateWatermark] = "{title} - {status}",
        [Keys.OscTemplateTokens] = "Tokens: {title}  {status}  {time}",
        [Keys.CardLanguageTitle] = "IDIOMA",
        [Keys.CardLanguageHelp] = "Cambia el idioma de la interfaz al vuelo.",
        [Keys.MediaPlayingStatus] = "Reproduciendo",
        [Keys.MediaPausedStatus] = "Pausado",
        [Keys.MediaStoppedStatus] = "Detenido",
        [Keys.MediaNothingPlaying] = "Nada reproduciéndose",
        [Keys.CardAppearanceTitle] = "APARIENCIA",
        [Keys.AppearanceBackground] = "Imagen de fondo",
        [Keys.AppearancePickImage] = "Elegir imagen...",
        [Keys.AppearanceRemoveImage] = "Eliminar",
        [Keys.AppearanceAccentColor] = "Color de acento",
        [Keys.AppearanceCardColor] = "Color de tarjeta",
        [Keys.AppearanceAutoExtract] = "Extraer colores de la imagen",
        [Keys.AppearanceResetDefaults] = "Restablecer valores predeterminados",
        [Keys.CardShortcutsTitle] = "ATAJOS",
        [Keys.ShortcutToggleLabel] = "Atajo para iniciar/detener",
        [Keys.ShortcutToggleHelp] = "Ej. F9, Ctrl+S, Espacio",
        [Keys.ShortcutGlobalHint] = "El atajo es global: funciona aunque la ventana esté en segundo plano.",
        [Keys.ShortcutGlobalFailed] = "No se pudo registrar el atajo global (puede estar en uso por otra aplicación).",
        [Keys.TrayShowWindow] = "Mostrar ventana",
        [Keys.TrayExit] = "Salir",
        [Keys.BehaviorMinimizeToTray] = "Minimizar a la bandeja al cerrar la ventana",
    };

    private readonly Dictionary<string, string> _en = new()
    {
        [Keys.AppTitle] = "IterVC",
        [Keys.AppFooter] = "IterVC - 0.1-v15172926",
        [Keys.StatusRouting] = "ROUTING",
        [Keys.StatusInactive] = "IDLE",
        [Keys.ButtonStart] = "Start",
        [Keys.ButtonStop] = "Stop",
        [Keys.ButtonRefreshApps] = "Refresh apps",
        [Keys.CardAppsTitle] = "APPS WITH AUDIO",
        [Keys.CardAppsHelp] = "Check the apps you want to mix into the virtual microphone.",
        [Keys.CardDevicesTitle] = "DEVICES",
        [Keys.DeviceReference] = "Reference (detect apps)",
        [Keys.DeviceVbCable] = "Virtual microphone (VB-Cable)",
        [Keys.DeviceMicrophone] = "Physical microphone",
        [Keys.DeviceNoneMic] = "[None - Microphone disabled]",
        [Keys.CardSpatialInfoTitle] = "* Reference device spatial audio",
        [Keys.CardSpatialInfoBody] = "Your device may have Dolby Atmos for Headphones, Windows Sonic or DTS Sound Unbound enabled. Those spatial effects are applied to your headphone audio AND to the audio IterVC captures per process.",
        [Keys.CardSpatialInfoHelp] = "To get clean stereo signal in VB-Cable: open Settings → System → Sound → your device → 'Spatial audio format' → 'Off'.",
        [Keys.CardMixTitle] = "MIX",
        [Keys.MixAppsGain] = "Apps gain",
        [Keys.MixAppsGainHelp] = "Values above 100% amplify the signal.",
        [Keys.MixAppsBoost] = "Microphone gain",
        [Keys.MixAppsBoostHelp] = "Extra multiplier applied on top of the mic volume (1× = no gain).",
        [Keys.MixMonitorMic] = "Monitor microphone",
        [Keys.CardOscTitle] = "OSC - VRCHAT",
        [Keys.OscEnableChatbox] = "Send info to Chatbox",
        [Keys.OscTemplateLabel] = "Template",
        [Keys.OscTemplateWatermark] = "{title} - {status}",
        [Keys.OscTemplateTokens] = "Tokens: {title}  {status}  {time}",
        [Keys.CardLanguageTitle] = "LANGUAGE",
        [Keys.CardLanguageHelp] = "Switches the interface language on the fly.",
        [Keys.MediaPlayingStatus] = "Playing",
        [Keys.MediaPausedStatus] = "Paused",
        [Keys.MediaStoppedStatus] = "Stopped",
        [Keys.MediaNothingPlaying] = "Nothing playing",
        [Keys.CardAppearanceTitle] = "APPEARANCE",
        [Keys.AppearanceBackground] = "Background image",
        [Keys.AppearancePickImage] = "Choose image...",
        [Keys.AppearanceRemoveImage] = "Remove",
        [Keys.AppearanceAccentColor] = "Accent color",
        [Keys.AppearanceCardColor] = "Card color",
        [Keys.AppearanceAutoExtract] = "Auto-extract colors from image",
        [Keys.AppearanceResetDefaults] = "Reset to defaults",
        [Keys.CardShortcutsTitle] = "SHORTCUTS",
        [Keys.ShortcutToggleLabel] = "Start/stop shortcut",
        [Keys.ShortcutToggleHelp] = "E.g. F9, Ctrl+S, Space",
        [Keys.ShortcutGlobalHint] = "The shortcut is global: it works even when the window is in the background.",
        [Keys.ShortcutGlobalFailed] = "Could not register the global hotkey (it may be in use by another application).",
        [Keys.TrayShowWindow] = "Show window",
        [Keys.TrayExit] = "Exit",
        [Keys.BehaviorMinimizeToTray] = "Minimize to tray when closing the window",
    };

    public string CurrentLanguage { get; private set; } = SupportedLanguages.Spanish;
    public event EventHandler? Changed;

    public string Get(string key)
    {
        var table = CurrentLanguage == SupportedLanguages.Spanish ? _es
                 : CurrentLanguage == SupportedLanguages.English ? _en
                 : _es;
        if (table.TryGetValue(key, out var value)) return value;
        if (_es.TryGetValue(key, out var fallbackEs)) return fallbackEs;
        if (_en.TryGetValue(key, out var fallbackEn)) return fallbackEn;
        return $"[{key}]";
    }

    public void SetLanguage(string lang)
    {
        if (lang != SupportedLanguages.Spanish && lang != SupportedLanguages.English)
            return;
        if (lang == CurrentLanguage) return;
        CurrentLanguage = lang;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
