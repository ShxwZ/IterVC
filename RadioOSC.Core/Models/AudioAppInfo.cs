namespace RadioOSC.Core.Models;

/// <summary>
/// Representa una aplicación en ejecución con una sesión de audio activa en Windows.
/// </summary>
public sealed record AudioAppInfo
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// True si esta app se está capturando actualmente hacia el micrófono virtual
    /// (ver <see cref="IAudioRouterService.AddAppSourceAsync"/>). No implica nada sobre
    /// el volumen o silencio de la app en Windows — eso nunca se toca.
    /// </summary>
    public bool IsIncludedInMix { get; init; }
}
