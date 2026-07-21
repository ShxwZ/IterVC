using BuildSoft.VRChat.Osc.Chatbox;
using Microsoft.Extensions.Logging;
using IterVC.Core.Interfaces;
using Windows.UI.Text;

namespace IterVC.Audio;
public sealed class OscMediaService : IOscMediaService
{
    private readonly ILogger<OscMediaService> _logger;
    public double GetWeight(char c)
    {
        if ("WM@#%".Contains(c))
        return 1.45;

        if ("ABCDEFGHNOQRSUVXYZ".Contains(c))
           return 1.0;

        if ("abcdefghknopquvxyz".Contains(c))
            return 0.8;

        if ("iljrftI1".Contains(c))
            return 0.5;

        if (".,:;'|!".Contains(c))
            return 0.4;

        if (c == ' ')
            return 0.45;

        return 0.9;
    }
    private readonly double limit=24;
    public string Animation(string line)
    {
        if (line.Length <= 1)
            return line;
        char[] result =line.ToCharArray();
        char c = result[0];
        for (int i = 0; i < result.Length - 1; i++)
        result[i] = result[i + 1];
        result[result.Length - 1] = c;
        return new string(result);
    }

    public bool[] GetLinesAnimation(string message)
    {
        // supposing that all \r\n and \n are \v
        string[] lines = message.Split('\v');
        double Weight = 0;
        bool[] animatedlines = new bool[lines.Length];
        int counter = 0;
        foreach(string line in lines)
        {
            Weight = 0;
            foreach (char c in line)
            {
                Weight += GetWeight(c);
            }
            if(Weight > limit)
            {
                animatedlines[counter] = true;
            }
            counter++;
        }
        return animatedlines;
    }

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