namespace IterVC.Core.Models;

public enum AudioDeviceKind
{
    Output,
    Input
}

/// <summary>
/// Representación agnóstica-de-driver de un dispositivo de audio,
/// para no filtrar tipos de NAudio hacia la capa de UI.
/// </summary>
public sealed class AudioDeviceInfo
{
    /// <summary>Identificador estable del dispositivo (Device ID de WASAPI/MME).</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public AudioDeviceKind Kind { get; init; }

    public bool IsDefault { get; init; }

    public override string ToString() => Name;
}
