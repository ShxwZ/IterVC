using System.Text.Json;

namespace IterVC.Linux;

internal sealed record ApplicationStream(uint Id, string Name)
{
    public override string ToString() => $"{Name} (#{Id})";
}

/// <summary>
/// PipeWire PulseAudio compatibility reports active playback streams.
/// Stream IDs are temporary: always refresh before starting a session.
/// </summary>
internal static class ApplicationStreamCatalog
{
    public static ApplicationStream[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<ApplicationStream>();

        var result = new List<ApplicationStream>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("index", out var index) || !index.TryGetUInt32(out var id))
                continue;

            var label = $"Audio stream {id}";
            if (item.TryGetProperty("properties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "application.name", "media.name", "application.process.binary" })
                {
                    if (properties.TryGetProperty(propertyName, out var property) &&
                        property.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(property.GetString()))
                    {
                        label = property.GetString()!;
                        break;
                    }
                }
            }
            if (label.StartsWith("IterVC", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new ApplicationStream(id, label));
        }
        return result.ToArray();
    }
}
