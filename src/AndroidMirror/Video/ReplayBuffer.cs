using System.IO;

namespace TouchMirror.Video;

public sealed class ReplayBuffer
{
    private readonly struct Entry
    {
        public byte[] Data { get; init; }
        public long Pts { get; init; }
        public bool IsKey { get; init; }
    }

    private readonly List<Entry> _items = new();
    private byte[]? _config;
    private long _bytes;
    private readonly long _windowUs;
    private readonly long _maxBytes;

    public ReplayBuffer(int windowSeconds = 30, long maxBytes = 192L << 20)
    {
        _windowUs = windowSeconds * 1_000_000L;
        _maxBytes = maxBytes;
    }

    public void Push(byte[] annexb, int len, long pts, bool isConfig, bool isKey)
    {
        lock (_items)
        {
            if (isConfig)
            {
                var cfg = new byte[len];
                Array.Copy(annexb, cfg, len);
                _config = cfg;
                return;
            }
            var data = new byte[len];
            Array.Copy(annexb, data, len);
            _items.Add(new Entry { Data = data, Pts = pts, IsKey = isKey });
            _bytes += len;
            while (_items.Count > 1)
            {
                var head = _items[0];
                if (pts - head.Pts <= _windowUs && _bytes <= _maxBytes)
                    break;
                _bytes -= head.Data.Length;
                _items.RemoveAt(0);
            }
        }
    }

    public string? Dump(string path, int width, int height, string codecId)
    {
        List<Entry> span;
        byte[] cfg;
        lock (_items)
        {
            if (_config == null || _items.Count == 0)
                return null;
            var ki = -1;
            for (var i = _items.Count - 1; i >= 0; i--)
                if (_items[i].IsKey) { ki = i; break; }
            if (ki < 0)
                return null;
            cfg = _config;
            span = _items.GetRange(ki, _items.Count - ki);
        }
        try
        {
            using var rec = new Mp4Recorder(path, width, height, codecId);
            rec.WriteConfig(cfg, cfg.Length);
            foreach (var e in span)
                rec.WritePacket(e.Data, e.Data.Length, e.Pts, e.IsKey);
            return rec.HeaderWritten ? path : null;
        }
        catch (Exception ex)
        {
            Services.AppLogger.Write($"replay: dump échoué — {ex.Message}");
            try { File.Delete(path); } catch { }
            return null;
        }
    }
}
