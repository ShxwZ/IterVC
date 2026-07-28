namespace IterVC.Core.Models;

public sealed class AudioDataEventArgs(byte[] buffer, int count) : EventArgs
{
    public byte[] Buffer { get; } = buffer;
    public int Count { get; } = count;
}
