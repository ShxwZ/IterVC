using BuildSoft.VRChat.Osc.Chatbox;
using Microsoft.Extensions.Logging;
using IterVC.Core.Interfaces;

namespace IterVC.Audio;

public sealed class OscMediaService : IOscMediaService
{
    private readonly ILogger<OscMediaService> _logger;

    public OscMediaService(ILogger<OscMediaService> logger) => _logger = logger;

    public void SendMediaInfo(string? title, string? status, string template)
    {
        try
        {
            string message = template
                .Replace("{title}", title ?? "Desconocido")
                .Replace("{status}", status ?? "N/A")
                .Replace("\r\n", "\v")
                .Replace("\n", "\v");
            OscChatbox.SendMessage(message, direct: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando mensaje OSC al chatbox");
        }
    }

    public void ClearChatbox() => OscChatbox.SendMessage("", direct: true);
}