using NAudio.Wave;

namespace IterVC.Audio.Buffers;

internal sealed class RealtimeAudioBuffer : IWaveProvider
{
    private readonly object _gate = new();
    private readonly byte[] _buffer;
    private readonly int _targetBytes;
    private readonly int _correctionBytes;
    private readonly bool _countUnderflows;
    private int _readPosition;
    private int _writePosition;
    private int _bufferedBytes;
    private long _writtenFrames;
    private long _readFrames;
    private long _underflows;
    private long _overflows;
    private long _corrections;
    private long _discardedFrames;
    private long _occupancySamples;
    private long _occupancyTotalBytes;
    private int _minimumBytes = int.MaxValue;
    private int _maximumBytes;

    public RealtimeAudioBuffer(
        WaveFormat waveFormat,
        bool readFully,
        bool countUnderflows = true,
        int capacityMilliseconds = 120,
        int targetMilliseconds = 20,
        int correctionThresholdMilliseconds = 50)
    {
        WaveFormat = waveFormat;
        ReadFully = readFully;
        _countUnderflows = countUnderflows;
        _buffer = new byte[Align(waveFormat.AverageBytesPerSecond * capacityMilliseconds / 1000)];
        _targetBytes = Align(waveFormat.AverageBytesPerSecond * targetMilliseconds / 1000);
        _correctionBytes = Align(waveFormat.AverageBytesPerSecond * correctionThresholdMilliseconds / 1000);
        TargetMilliseconds = targetMilliseconds;
    }

    public WaveFormat WaveFormat { get; }
    public bool ReadFully { get; }
    public double TargetMilliseconds { get; }

    public void Write(byte[] source, int offset, int count)
    {
        count -= count % WaveFormat.BlockAlign;
        if (count <= 0) return;
        if (count > _buffer.Length)
        {
            offset += count - _buffer.Length;
            count = _buffer.Length;
        }

        lock (_gate)
        {
            if (_bufferedBytes + count > _buffer.Length)
            {
                _overflows++;
                Discard(_bufferedBytes + count - _buffer.Length);
            }

            if (_bufferedBytes > _correctionBytes)
            {
                _corrections++;
                Discard(_bufferedBytes - _targetBytes);
            }

            CopyIntoRing(source, offset, count);
            _bufferedBytes += count;
            _writtenFrames += count / WaveFormat.BlockAlign;
            RecordOccupancy();
        }
    }

    public int Read(byte[] destination, int offset, int count)
    {
        count -= count % WaveFormat.BlockAlign;
        if (count <= 0) return 0;
        lock (_gate)
        {
            var available = Math.Min(count, _bufferedBytes);
            if (available == 0 && !ReadFully)
                return 0;
            CopyFromRing(destination, offset, available);
            _bufferedBytes -= available;
            _readFrames += available / WaveFormat.BlockAlign;

            if (available < count)
            {
                if (_countUnderflows)
                    _underflows++;
                if (ReadFully)
                {
                    Array.Clear(destination, offset + available, count - available);
                    available = count;
                }
            }

            RecordOccupancy();
            return available;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _readPosition = 0;
            _writePosition = 0;
            _bufferedBytes = 0;
            RecordOccupancy();
        }
    }

    public RealtimeAudioBufferSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var samples = Math.Max(1, _occupancySamples);
            return new(
                ToMilliseconds(_bufferedBytes),
                TargetMilliseconds,
                ToMilliseconds(_minimumBytes == int.MaxValue ? 0 : _minimumBytes),
                ToMilliseconds(_maximumBytes),
                ToMilliseconds((double)_occupancyTotalBytes / samples),
                _writtenFrames,
                _readFrames,
                _underflows,
                _overflows,
                _corrections,
                _discardedFrames,
                _discardedFrames * 1000d / WaveFormat.SampleRate);
        }
    }

    private int Align(int bytes) => Math.Max(WaveFormat.BlockAlign, bytes - bytes % WaveFormat.BlockAlign);

    private void Discard(int count)
    {
        count = Math.Min(_bufferedBytes, Align(count));
        _readPosition = (_readPosition + count) % _buffer.Length;
        _bufferedBytes -= count;
        _discardedFrames += count / WaveFormat.BlockAlign;
    }

    private void CopyIntoRing(byte[] source, int offset, int count)
    {
        var first = Math.Min(count, _buffer.Length - _writePosition);
        Buffer.BlockCopy(source, offset, _buffer, _writePosition, first);
        Buffer.BlockCopy(source, offset + first, _buffer, 0, count - first);
        _writePosition = (_writePosition + count) % _buffer.Length;
    }

    private void CopyFromRing(byte[] destination, int offset, int count)
    {
        var first = Math.Min(count, _buffer.Length - _readPosition);
        Buffer.BlockCopy(_buffer, _readPosition, destination, offset, first);
        Buffer.BlockCopy(_buffer, 0, destination, offset + first, count - first);
        _readPosition = (_readPosition + count) % _buffer.Length;
    }

    private void RecordOccupancy()
    {
        _minimumBytes = Math.Min(_minimumBytes, _bufferedBytes);
        _maximumBytes = Math.Max(_maximumBytes, _bufferedBytes);
        _occupancyTotalBytes += _bufferedBytes;
        _occupancySamples++;
    }

    private double ToMilliseconds(double bytes) => bytes * 1000d / WaveFormat.AverageBytesPerSecond;
}
