namespace IterVC.Core.Settings;

public enum CloseBehavior
{
    Ask,
    MinimizeToTray,
    Exit
}

/// <summary>
/// Configuración persistida de la aplicación (settings.json).
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 4;
    public string? OutputDeviceId { get; set; }
    public string? VbCableDeviceId { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public bool MicrophoneEnabled { get; set; } = true;
    public float AppsVolume { get; set; } = 1.0f;
    public float MicrophoneVolume { get; set; } = 1.0f;
    public float MicrophoneBoost { get; set; } = 1.0f;
    public bool MonitorMicrophone { get; set; }

    /// <summary>Whether DeepFilterNet3 microphone noise suppression is enabled.</summary>
    public bool NoiseSuppressionEnabled { get; set; } = true;

    public bool NoiseGateEnabled { get; set; }
    public float NoiseGateThresholdDb { get; set; } = -45f;
    public float NoiseGateAttackMilliseconds { get; set; } = 10f;
    public float NoiseGateReleaseMilliseconds { get; set; } = 150f;
    public List<string> IncludedProcessNames { get; set; } = new();
    public string Language { get; set; } = "Español";
    public string OscTemplate { get; set; } = "{title} - {status}";
    public bool EnableOscChatbox { get; set; }
    public bool? CheckForUpdates { get; set; }
    public DateTimeOffset? LastSuccessfulUpdateCheckUtc { get; set; }
    public string? CachedLatestVersion { get; set; }
    public string? CachedReleaseUrl { get; set; }
    public string? DismissedUpdateVersion { get; set; }
    public bool ToggleRoutingHotkeyEnabled { get; set; } = true;
    public string ToggleRoutingHotkeyGesture { get; set; } = "Ctrl+Shift+R";
    public bool StartRoutingHotkeyEnabled { get; set; }
    public string StartRoutingHotkeyGesture { get; set; } = "";
    public bool StopRoutingHotkeyEnabled { get; set; }
    public string StopRoutingHotkeyGesture { get; set; } = "";
    public bool ToggleApplicationsMuteHotkeyEnabled { get; set; }
    public string ToggleApplicationsMuteHotkeyGesture { get; set; } = "";
    public bool ToggleMicrophoneHotkeyEnabled { get; set; }
    public string ToggleMicrophoneHotkeyGesture { get; set; } = "";
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Ask;
    public bool MinimizeToTrayWhenMinimized { get; set; }
    public bool TrayHideNotificationSuppressed { get; set; }
    public bool StartHiddenOnWindowsStartup { get; set; } = true;
    public bool StartupRegistrationNoticeAcknowledged { get; set; }
}
