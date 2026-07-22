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
        public const string AppTitle           = "app.title";
        public const string AppFooter          = "app.footer";
        public const string StatusRouting      = "status.routing";
        public const string StatusInactive     = "status.inactive";
        public const string StatusMicrophoneActive = "status.microphone_active";
        public const string StatusMicrophoneInactive = "status.microphone_inactive";
        public const string ButtonStart        = "button.start";
        public const string ButtonStop         = "button.stop";
        public const string ButtonRefreshApps  = "button.refresh_apps";
        public const string CardAppsTitle      = "card.apps.title";
        public const string CardAppsHelp       = "card.apps.help";
        public const string AppsDetectedOne    = "apps.detected_one";
        public const string AppsDetectedMany   = "apps.detected_many";
        public const string CardDevicesTitle   = "card.devices.title";
        public const string DeviceReference    = "device.reference";
        public const string DeviceVbCable      = "device.vbcable";
        public const string DeviceMicrophone   = "device.microphone";
        public const string DeviceMicrophoneEnable = "device.microphone_enable";
        public const string CardSpatialInfoTitle = "card.spatial.title";
        public const string CardSpatialInfoBody  = "card.spatial.body";
        public const string CardSpatialInfoHelp  = "card.spatial.help";
        public const string CardMixTitle       = "card.mix.title";
        public const string MixAppsGain        = "mix.apps_gain";
        public const string MixAppsBoost       = "mix.apps_boost";
        public const string MixAppsBoostHelp   = "mix.apps_boost.help";
        public const string MixAppsGainHelp    = "mix.apps_gain.help";
        public const string MixMonitorMic      = "mix.monitor_mic";
        public const string NoiseGateEnable    = "noise_gate.enable";
        public const string NoiseGateThreshold = "noise_gate.threshold";
        public const string NoiseGateThresholdHelp = "noise_gate.threshold.help";
        public const string NoiseGateAttack    = "noise_gate.attack";
        public const string NoiseGateRelease   = "noise_gate.release";
        public const string NoiseGateTitle     = "noise_gate.title";
        public const string NoiseGateHelp      = "noise_gate.help";
        public const string NoiseGateOpen      = "noise_gate.open";
        public const string NoiseGateClosed    = "noise_gate.closed";
        public const string NoiseGateCalibrate = "noise_gate.calibrate";
        public const string NoiseGateCalibrating = "noise_gate.calibrating";
        public const string NoiseGateAdvanced  = "noise_gate.advanced";
        public const string NoiseGateOutput    = "noise_gate.output";
        public const string NoiseGateAttackHelp = "noise_gate.attack.help";
        public const string NoiseGateReleaseHelp = "noise_gate.release.help";
        public const string CardOscTitle       = "card.osc.title";
        public const string OscEnableChatbox   = "osc.enable_chatbox";
        public const string OscTemplateLabel   = "osc.template_label";
        public const string OscTemplateWatermark = "osc.template_watermark";
        public const string OscTemplateTokens  = "osc.template_tokens";
        public const string CardLanguageTitle  = "card.language.title";
        public const string CardLanguageHelp   = "card.language.help";
        public const string MediaPlayingStatus = "media.playing";
        public const string MediaPausedStatus  = "media.paused";
        public const string MediaStoppedStatus = "media.stopped";
        public const string UpdateConsentTitle = "update.consent.title";
        public const string UpdateConsentBody = "update.consent.body";
        public const string UpdateAllow = "update.allow";
        public const string UpdateDecline = "update.decline";
        public const string UpdateAvailable = "update.available";
        public const string UpdateOpen = "update.open";
        public const string UpdateSettingsTitle = "update.settings.title";
        public const string UpdateEnabled = "update.enabled";
        public const string UpdateCheckNow = "update.check_now";
        public const string UpdateChecking = "update.checking";
        public const string UpdateCurrent = "update.current";
        public const string UpdateCheckFailed = "update.check_failed";
        public const string UpdateOpenFailed = "update.open_failed";
        public const string SettingsTitle = "settings.title";
        public const string UpdateDismiss = "update.dismiss";
        public const string HotkeyTitle = "hotkey.title";
        public const string HotkeyToggleRouting = "hotkey.toggle_routing";
        public const string HotkeyStartRouting = "hotkey.start_routing";
        public const string HotkeyStopRouting = "hotkey.stop_routing";
        public const string HotkeyToggleMicrophone = "hotkey.toggle_microphone";
        public const string HotkeyHelp = "hotkey.help";
        public const string HotkeyRegistrationFailed = "hotkey.registration_failed";
        public const string HotkeyNotAssigned = "hotkey.not_assigned";
        public const string HotkeyClear = "hotkey.clear";
        public const string HotkeyCancel = "hotkey.cancel";
        public const string HotkeyAssign = "hotkey.assign";
        public const string HotkeyChange = "hotkey.change";
        public const string HotkeyRecordingKeystrokes = "hotkey.recording_keystrokes";
        public const string HotkeyEscapeToCancel = "hotkey.escape_to_cancel";
        public const string HotkeyConflict = "hotkey.conflict";
        public const string HotkeyRoutingGroup = "hotkey.routing_group";
        public const string HotkeyMicrophoneGroup = "hotkey.microphone_group";
        public const string MicrophoneEnable = "microphone.enable";
        public const string MicrophoneDisable = "microphone.disable";
    }

    private readonly Dictionary<string, string> _es = new()
    {
        [Keys.AppTitle]             = "IterVC",
        [Keys.AppFooter]            = "IterVC - {0}",
        [Keys.StatusRouting]        = "ENRUTAMIENTO ACTIVO",
        [Keys.StatusInactive]       = "ENRUTAMIENTO DETENIDO",
        [Keys.StatusMicrophoneActive] = "MICRÓFONO ACTIVO",
        [Keys.StatusMicrophoneInactive] = "MICRÓFONO SILENCIADO",
        [Keys.ButtonStart]          = "Iniciar",
        [Keys.ButtonStop]           = "Detener",
        [Keys.ButtonRefreshApps]    = "Actualizar apps",
        [Keys.CardAppsTitle]        = "APLICACIONES CON AUDIO",
        [Keys.CardAppsHelp]         = "Marca las apps que quieres mezclar en el micrófono virtual.",
        [Keys.AppsDetectedOne]      = "1 aplicación con audio detectada.",
        [Keys.AppsDetectedMany]     = "{0} aplicaciones con audio detectadas.",
        [Keys.CardDevicesTitle]     = "DISPOSITIVOS",
        [Keys.DeviceReference]      = "Referencia (detectar apps)",
        [Keys.DeviceVbCable]        = "Micrófono virtual (VB-Cable)",
        [Keys.DeviceMicrophone]     = "Micrófono físico",
        [Keys.DeviceMicrophoneEnable] = "Usar micrófono",
        [Keys.CardSpatialInfoTitle] = "* Audio espacial del dispositivo de referencia",
        [Keys.CardSpatialInfoBody]  = "Tu dispositivo puede tener Dolby Atmos for Headphones, Windows Sonic o DTS Sound Unbound activos. Esos efectos espaciales se aplican al audio de tus auriculares Y al audio que IterVC captura por proceso.",
        [Keys.CardSpatialInfoHelp]  = "Para obtener señal estéreo limpia en VB-Cable: abre Configuración → Sistema → Sonido → tu dispositivo → Formato de audio espacial → Off.",
        [Keys.CardMixTitle]         = "MEZCLA",
        [Keys.MixAppsGain]          = "Ganancia de apps",
        [Keys.MixAppsGainHelp]      = "Valores sobre 100% amplifican la señal.",
        [Keys.MixAppsBoost]         = "Ganancia del micrófono",
        [Keys.MixAppsBoostHelp]     = "Multiplicador extra aplicado al volumen del micro (1× = sin ganancia).",
        [Keys.MixMonitorMic]        = "Monitorizar micrófono",
        [Keys.NoiseGateEnable]      = "Activar puerta de ruido",
        [Keys.NoiseGateThreshold]   = "Umbral",
        [Keys.NoiseGateThresholdHelp] = "Nivel mínimo que debe superar tu voz para que el micrófono se abra.",
        [Keys.NoiseGateAttack]      = "Ataque",
        [Keys.NoiseGateRelease]     = "Liberación",
        [Keys.NoiseGateTitle]       = "PUERTA DE RUIDO",
        [Keys.NoiseGateHelp]        = "Quédate en silencio y coloca el umbral por encima del ruido ambiente. Tu voz debe superar la marca.",
        [Keys.NoiseGateOpen]        = "ABIERTA · transmitiendo",
        [Keys.NoiseGateClosed]      = "CERRADA · silenciado",
        [Keys.NoiseGateCalibrate]   = "Calibrar ruido ambiente",
        [Keys.NoiseGateCalibrating] = "Escuchando el ambiente…",
        [Keys.NoiseGateAdvanced]    = "Ajustes avanzados",
        [Keys.NoiseGateOutput]      = "SALIDA DE LA PUERTA",
        [Keys.NoiseGateAttackHelp]  = "Cuánto tarda en dejar pasar tu voz al empezar a hablar.",
        [Keys.NoiseGateReleaseHelp] = "Cuánto tarda en volver a silenciar después de hablar.",
        [Keys.CardOscTitle]         = "OSC - VRCHAT",
        [Keys.OscEnableChatbox]     = "Enviar info al Chatbox",
        [Keys.OscTemplateLabel]     = "Plantilla",
        [Keys.OscTemplateWatermark] = "{title} - {status}",
        [Keys.OscTemplateTokens]    = "Tokens: {title}  {status}  {time}",
        [Keys.CardLanguageTitle]    = "IDIOMA",
        [Keys.CardLanguageHelp]     = "Cambia el idioma de la interfaz al vuelo.",
        [Keys.MediaPlayingStatus]   = "Reproduciendo",
        [Keys.MediaPausedStatus]    = "Pausado",
        [Keys.MediaStoppedStatus]   = "Detenido",
        [Keys.UpdateConsentTitle]   = "COMPROBAR ACTUALIZACIONES",
        [Keys.UpdateConsentBody]    = "¿Quieres que IterVC consulte GitHub Releases al iniciarse? Solo se envía una solicitud a GitHub y puedes desactivarla cuando quieras.",
        [Keys.UpdateAllow]          = "Permitir",
        [Keys.UpdateDecline]        = "Ahora no",
        [Keys.UpdateAvailable]      = "Hay una nueva versión disponible.",
        [Keys.UpdateOpen]           = "Ver versión",
        [Keys.UpdateSettingsTitle]  = "ACTUALIZACIONES",
        [Keys.UpdateEnabled]        = "Comprobar automáticamente al iniciar",
        [Keys.UpdateCheckNow]       = "Comprobar ahora",
        [Keys.UpdateChecking]       = "Buscando actualizaciones…",
        [Keys.UpdateCurrent]        = "Ya tienes la versión más reciente.",
        [Keys.UpdateCheckFailed]    = "No se pudo comprobar. Revisa tu conexión e inténtalo de nuevo.",
        [Keys.UpdateOpenFailed]     = "No se pudo abrir la página de la versión.",
        [Keys.SettingsTitle]        = "CONFIGURACIÓN",
        [Keys.UpdateDismiss]        = "Descartar",
        [Keys.HotkeyTitle]          = "ATAJOS GLOBALES",
        [Keys.HotkeyToggleRouting]  = "Alternar enrutamiento",
        [Keys.HotkeyStartRouting]   = "Iniciar enrutamiento",
        [Keys.HotkeyStopRouting]    = "Detener enrutamiento",
        [Keys.HotkeyToggleMicrophone] = "Alternar micrófono",
        [Keys.HotkeyHelp]           = "Los atajos funcionan aunque la aplicaci\u00f3n no tenga el foco y no bloquean su uso en otras aplicaciones.",
        [Keys.HotkeyRegistrationFailed] = "No se pudieron aplicar los atajos observados: {0}",
        [Keys.HotkeyNotAssigned]    = "Sin asignar",
        [Keys.HotkeyClear]          = "Borrar",
        [Keys.HotkeyCancel]           = "Cancelar",
        [Keys.HotkeyAssign]           = "Asignar",
        [Keys.HotkeyChange]           = "Cambiar",
        [Keys.HotkeyRecordingKeystrokes] = "Grabando pulsaciones…",
        [Keys.HotkeyEscapeToCancel]   = "Esc para cancelar",
        [Keys.HotkeyConflict]         = "Esta combinación ya está asignada a {0}.",
        [Keys.HotkeyRoutingGroup]     = "ENRUTAMIENTO",
        [Keys.HotkeyMicrophoneGroup]  = "MICRÓFONO",
        [Keys.MicrophoneEnable]       = "Activar micrófono",
        [Keys.MicrophoneDisable]      = "Silenciar micrófono",
    };

    private readonly Dictionary<string, string> _en = new()
    {
        [Keys.AppTitle]             = "IterVC",
        [Keys.AppFooter]            = "IterVC - {0}",
        [Keys.StatusRouting]        = "ROUTING ACTIVE",
        [Keys.StatusInactive]       = "ROUTING STOPPED",
        [Keys.StatusMicrophoneActive] = "MICROPHONE ON",
        [Keys.StatusMicrophoneInactive] = "MICROPHONE MUTED",
        [Keys.ButtonStart]          = "Start",
        [Keys.ButtonStop]           = "Stop",
        [Keys.ButtonRefreshApps]    = "Refresh apps",
        [Keys.CardAppsTitle]        = "APPS WITH AUDIO",
        [Keys.CardAppsHelp]         = "Check the apps you want to mix into the virtual microphone.",
        [Keys.AppsDetectedOne]      = "1 application with audio detected.",
        [Keys.AppsDetectedMany]     = "{0} applications with audio detected.",
        [Keys.CardDevicesTitle]     = "DEVICES",
        [Keys.DeviceReference]      = "Reference (detect apps)",
        [Keys.DeviceVbCable]        = "Virtual microphone (VB-Cable)",
        [Keys.DeviceMicrophone]     = "Physical microphone",
        [Keys.DeviceMicrophoneEnable] = "Use microphone",
        [Keys.CardSpatialInfoTitle] = "* Reference device spatial audio",
        [Keys.CardSpatialInfoBody]  = "Your device may have Dolby Atmos for Headphones, Windows Sonic or DTS Sound Unbound enabled. Those spatial effects are applied to your headphone audio AND to the audio IterVC captures per process.",
        [Keys.CardSpatialInfoHelp]  = "To get clean stereo signal in VB-Cable: open Settings → System → Sound → your device → 'Spatial audio format' → 'Off'.",
        [Keys.CardMixTitle]         = "MIX",
        [Keys.MixAppsGain]          = "Apps gain",
        [Keys.MixAppsGainHelp]      = "Values above 100% amplify the signal.",
        [Keys.MixAppsBoost]         = "Microphone gain",
        [Keys.MixAppsBoostHelp]     = "Extra multiplier applied on top of the mic volume (1× = no gain).",
        [Keys.MixMonitorMic]        = "Monitor microphone",
        [Keys.NoiseGateEnable]      = "Enable noise gate",
        [Keys.NoiseGateThreshold]   = "Threshold",
        [Keys.NoiseGateThresholdHelp] = "Minimum level your voice must exceed before the microphone opens.",
        [Keys.NoiseGateAttack]      = "Attack",
        [Keys.NoiseGateRelease]     = "Release",
        [Keys.NoiseGateTitle]       = "NOISE GATE",
        [Keys.NoiseGateHelp]        = "Stay quiet and place the threshold above the ambient noise. Your voice should cross the marker.",
        [Keys.NoiseGateOpen]        = "OPEN · transmitting",
        [Keys.NoiseGateClosed]      = "CLOSED · muted",
        [Keys.NoiseGateCalibrate]   = "Calibrate ambient noise",
        [Keys.NoiseGateCalibrating] = "Listening to the room…",
        [Keys.NoiseGateAdvanced]    = "Advanced settings",
        [Keys.NoiseGateOutput]      = "GATE OUTPUT",
        [Keys.NoiseGateAttackHelp]  = "How long it takes to let your voice through after you start speaking.",
        [Keys.NoiseGateReleaseHelp] = "How long it waits before muting again after you stop speaking.",
        [Keys.CardOscTitle]         = "OSC - VRCHAT",
        [Keys.OscEnableChatbox]     = "Send info to Chatbox",
        [Keys.OscTemplateLabel]     = "Template",
        [Keys.OscTemplateWatermark] = "{title} - {status}",
        [Keys.OscTemplateTokens]    = "Tokens: {title}  {status}  {time}",
        [Keys.CardLanguageTitle]    = "LANGUAGE",
        [Keys.CardLanguageHelp]     = "Switches the interface language on the fly.",
        [Keys.MediaPlayingStatus]   = "Playing",
        [Keys.MediaPausedStatus]    = "Paused",
        [Keys.MediaStoppedStatus]   = "Stopped",
        [Keys.UpdateConsentTitle]   = "CHECK FOR UPDATES",
        [Keys.UpdateConsentBody]    = "Allow IterVC to check GitHub Releases at startup? This only sends a request to GitHub and can be disabled at any time.",
        [Keys.UpdateAllow]          = "Allow",
        [Keys.UpdateDecline]        = "Not now",
        [Keys.UpdateAvailable]      = "A new version is available.",
        [Keys.UpdateOpen]           = "View release",
        [Keys.UpdateSettingsTitle]  = "UPDATES",
        [Keys.UpdateEnabled]        = "Check automatically at startup",
        [Keys.UpdateCheckNow]       = "Check now",
        [Keys.UpdateChecking]       = "Checking for updates…",
        [Keys.UpdateCurrent]        = "You already have the latest version.",
        [Keys.UpdateCheckFailed]    = "Could not check. Verify your connection and try again.",
        [Keys.UpdateOpenFailed]     = "Could not open the release page.",
        [Keys.SettingsTitle]        = "SETTINGS",
        [Keys.UpdateDismiss]        = "Dismiss",
        [Keys.HotkeyTitle]          = "GLOBAL HOTKEYS",
        [Keys.HotkeyToggleRouting]  = "Toggle routing",
        [Keys.HotkeyStartRouting]   = "Start routing",
        [Keys.HotkeyStopRouting]    = "Stop routing",
        [Keys.HotkeyToggleMicrophone] = "Toggle microphone",
        [Keys.HotkeyHelp]           = "Shortcuts work while the app is unfocused without blocking their use in other applications.",
        [Keys.HotkeyRegistrationFailed] = "Could not apply observed shortcuts: {0}",
        [Keys.HotkeyNotAssigned]    = "Not assigned",
        [Keys.HotkeyClear]          = "Clear",
        [Keys.HotkeyCancel]           = "Cancel",
        [Keys.HotkeyAssign]           = "Assign",
        [Keys.HotkeyChange]           = "Change",
        [Keys.HotkeyRecordingKeystrokes] = "Recording keystrokes…",
        [Keys.HotkeyEscapeToCancel]   = "Esc to cancel",
        [Keys.HotkeyConflict]         = "This shortcut is already assigned to {0}.",
        [Keys.HotkeyRoutingGroup]     = "ROUTING",
        [Keys.HotkeyMicrophoneGroup]  = "MICROPHONE",
        [Keys.MicrophoneEnable]       = "Enable microphone",
        [Keys.MicrophoneDisable]      = "Mute microphone",
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
