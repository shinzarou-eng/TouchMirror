using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace TouchMirror.Engine;

public enum ControlMsgType : byte
{
    Config = 0x01,
    InjectKeycode = 0x10,
    InjectText = 0x11,
    InjectTouchEvent = 0x12,
    InjectScrollEvent = 0x13,
    BackOrScreenOn = 0x20,
    ExpandNotificationPanel = 0x21,
    ExpandSettingsPanel = 0x22,
    CollapsePanels = 0x23,
    SetDisplayPower = 0x24,
    RotateDevice = 0x25,
    OpenHardKeyboardSettings = 0x26,
    StartApp = 0x27,
    ScanFile = 0x28,
    ResetVideo = 0x30,
    ResizeDisplay = 0x31,
    SetVideoParams = 0x32,
    GetClipboard = 0x40,
    SetClipboard = 0x41,
}

public enum DeviceMsgType : byte
{
    Clipboard = 0x50,
    AckClipboard = 0x51,
}

public static class AndroidMotionEvent
{
    public const byte ActionDown = 0;
    public const byte ActionUp = 1;
    public const byte ActionMove = 2;

    public const uint ButtonPrimary = 1;
    public const uint ButtonSecondary = 2;
    public const uint ButtonTertiary = 4;
    public const uint ButtonBack = 8;
    public const uint ButtonForward = 16;

    public const ulong PointerIdMouse = ulong.MaxValue;
    public const ulong PointerIdGenericFinger = ulong.MaxValue - 1;
    public const ulong PointerIdVirtualFinger = ulong.MaxValue - 2;

    public const byte ActionPointerDown = 5;
    public const byte ActionPointerUp = 6;
    public static byte PointerAction(byte baseAction, int index) => (byte)(baseAction | (index << 8));
}

public static class AndroidKeyEvent
{
    public const byte ActionDown = 0;
    public const byte ActionUp = 1;
}

public static class AndroidKeyCode
{
    public const int Home = 3;
    public const int Back = 4;
    public const int DpadUp = 19;
    public const int DpadDown = 20;
    public const int DpadLeft = 21;
    public const int DpadRight = 22;
    public const int VolumeUp = 24;
    public const int VolumeDown = 25;
    public const int Power = 26;
    public const int Camera = 27;
    public const int Clear = 28;
    public const int A = 29;
    public const int Z = 54;
    public const int Num0 = 7;
    public const int Tab = 61;
    public const int Space = 62;
    public const int Enter = 66;
    public const int Del = 67;
    public const int Escape = 111;
    public const int ForwardDel = 112;
    public const int MoveHome = 122;
    public const int MoveEnd = 123;
    public const int AppSwitch = 187;
}

public sealed class ControlChannel : IDisposable
{
    private const byte ChanControl = 3;

    private readonly Socket _socket;
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Threading.Channels.Channel<byte[]> _sendQueue =
        System.Threading.Channels.Channel.CreateBounded<byte[]>(1024);
    private readonly object _sendGate = new();
    private volatile bool _disposed;

    public event Action<string>? ClipboardReceived;
    public event Action? SendQueueFaulted;

    private int _faulted;

    public ControlChannel(Socket socket)
    {
        _socket = socket;
        Task.Run(SendLoop);
    }

    private void Send(ReadOnlySpan<byte> msg)
    {
        if (_disposed)
            return;
        var frame = new byte[5 + msg.Length];
        frame[0] = ChanControl;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), (uint)msg.Length);
        msg.CopyTo(frame.AsSpan(5));
        if (!_sendQueue.Writer.TryWrite(frame)
            && Interlocked.Exchange(ref _faulted, 1) == 0)
            SendQueueFaulted?.Invoke();
    }

    private async Task SendLoop()
    {
        try
        {
            await foreach (var frame in _sendQueue.Reader.ReadAllAsync(_cts.Token))
            {
                var off = 0;
                while (off < frame.Length)
                {
                    var sent = await _socket.SendAsync(frame.AsMemory(off), SocketFlags.None, _cts.Token);
                    if (sent == 0)
                        return;
                    off += sent;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally
        {
            lock (_sendGate)
            {
                _disposed = true;
                _sendQueue.Writer.TryComplete();
                _cts.Dispose();
            }
        }
    }

    public void Feed(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1)
            return;
        var type = (DeviceMsgType)payload[0];
        switch (type)
        {
            case DeviceMsgType.Clipboard:
                if (payload.Length < 5)
                    return;
                var len = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(1));
                if (len > (uint)(payload.Length - 5))
                    return;
                ClipboardReceived?.Invoke(Encoding.UTF8.GetString(payload.Slice(5, (int)len)));
                break;
            case DeviceMsgType.AckClipboard:
                break;
        }
    }

    public void SendConfig(EngineOptions o)
    {
        var cfg = new System.IO.MemoryStream(64);
        void Field(byte id, ReadOnlySpan<byte> v)
        {
            cfg.WriteByte(id);
            cfg.WriteByte((byte)v.Length);
            cfg.Write(v);
        }
        void U8(byte id, byte v) => Field(id, new[] { v });
        void U16(byte id, ushort v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, v);
            Field(id, b);
        }
        void U32(byte id, uint v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, v);
            Field(id, b);
        }

        U8(0x01, (byte)(o.Audio ? 1 : 0));
        U8(0x02, o.VideoCodec switch
        {
            "h264" => (byte)1,
            "h265" => (byte)2,
            "av1" => (byte)3,
            _ => (byte)0,
        });
        U8(0x03, 0);
        if (o.MaxSize > 0) U16(0x04, (ushort)Math.Min(o.MaxSize, 0xFFFF));
        if (o.MaxFps > 0) U8(0x05, (byte)Math.Min(o.MaxFps, 0xFF));
        if (o.VideoBitRate > 0) U32(0x06, (uint)o.VideoBitRate);
        var flags = (ushort)((o.StayAwake ? 2 : 0) | 4 | 8 | 16 | (o.ClipboardAutosync ? 32 : 0) | 1);
        U16(0x07, flags);
        if (o.NewDisplay != null && TryParseDisplaySpec(o.NewDisplay, out var dw, out var dh, out var dd))
        {
            Span<byte> nd = stackalloc byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(nd, dw);
            BinaryPrimitives.WriteUInt16BigEndian(nd.Slice(2), dh);
            BinaryPrimitives.WriteUInt16BigEndian(nd.Slice(4), dd);
            Field(0x08, nd);
        }
        if (!string.IsNullOrWhiteSpace(o.AutoLaunchPackage))
            Field(0x09, Encoding.UTF8.GetBytes(o.AutoLaunchPackage));
        U8(0x0A, 2);

        var tlv = cfg.ToArray();
        var msg = new byte[1 + tlv.Length];
        msg[0] = (byte)ControlMsgType.Config;
        tlv.CopyTo(msg, 1);
        Send(msg);
    }

    private static bool TryParseDisplaySpec(string spec, out ushort w, out ushort h, out ushort dpi)
    {
        w = h = dpi = 0;
        var slash = spec.Split('/');
        var dims = slash[0].Split('x');
        if (dims.Length != 2 || !ushort.TryParse(dims[0], out w) || !ushort.TryParse(dims[1], out h))
            return false;
        if (slash.Length > 1)
            ushort.TryParse(slash[1], out dpi);
        return w > 0 && h > 0;
    }

    public void InjectTouch(byte action, ulong pointerId, uint x, uint y, ushort w, ushort h,
        float pressure, uint actionButton, uint buttons)
    {
        Span<byte> buf = stackalloc byte[32];
        buf[0] = (byte)ControlMsgType.InjectTouchEvent;
        buf[1] = action;
        BinaryPrimitives.WriteUInt64BigEndian(buf.Slice(2, 8), pointerId);
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(10, 4), x);
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(14, 4), y);
        BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(18, 2), w);
        BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(20, 2), h);
        BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(22, 2), EncodePressure(pressure));
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(24, 4), actionButton);
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(28, 4), buttons);
        Send(buf);
    }

    public void InjectScroll(uint x, uint y, ushort w, ushort h, float hscroll, float vscroll, uint buttons)
    {
        Span<byte> buf = stackalloc byte[21];
        buf[0] = (byte)ControlMsgType.InjectScrollEvent;
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(1, 4), x);
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(5, 4), y);
        BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(9, 2), w);
        BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(11, 2), h);
        BinaryPrimitives.WriteInt16BigEndian(buf.Slice(13, 2), EncodeScroll(hscroll));
        BinaryPrimitives.WriteInt16BigEndian(buf.Slice(15, 2), EncodeScroll(vscroll));
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(17, 4), buttons);
        Send(buf);
    }

    public void InjectKey(byte action, int keycode, uint repeat = 0, uint metastate = 0)
    {
        Span<byte> buf = stackalloc byte[14];
        buf[0] = (byte)ControlMsgType.InjectKeycode;
        buf[1] = action;
        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(2, 4), keycode);
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(6, 4), repeat);
        BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(10, 4), metastate);
        Send(buf);
    }

    public void InjectKeyPress(int keycode, uint metastate = 0)
    {
        InjectKey(AndroidKeyEvent.ActionDown, keycode, 0, metastate);
        InjectKey(AndroidKeyEvent.ActionUp, keycode, 0, metastate);
    }

    public void InjectText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 300)
        {
            var end = 300;
            while (end > 0 && (bytes[end - 1] & 0xC0) == 0x80)
                end--;
            if (end > 0 && (bytes[end - 1] & 0x80) != 0)
                end--;
            bytes = bytes[..end];
        }
        var buf = new byte[5 + bytes.Length];
        buf[0] = (byte)ControlMsgType.InjectText;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(1, 4), (uint)bytes.Length);
        bytes.CopyTo(buf, 5);
        Send(buf);
    }

    public void Pinch(uint cx, uint cy, ushort w, ushort h, bool zoomIn)
    {
        var d0 = (uint)(h * 0.08);
        var d1 = (uint)(h * (zoomIn ? 0.30 : 0.02));
        if (!zoomIn)
            (d0, d1) = ((uint)(h * 0.30), (uint)(h * 0.08));

        var id1 = AndroidMotionEvent.PointerIdMouse;
        var id2 = AndroidMotionEvent.PointerIdVirtualFinger;

        InjectTouch(AndroidMotionEvent.ActionDown, id1,
            cx, (uint)Math.Max(0, (long)cy - d0), w, h, 1f, AndroidMotionEvent.ButtonPrimary, AndroidMotionEvent.ButtonPrimary);
        InjectTouch(AndroidMotionEvent.PointerAction(AndroidMotionEvent.ActionPointerDown, 1), id2,
            cx, Math.Min((uint)(h - 1), cy + d0), w, h, 1f, 0, AndroidMotionEvent.ButtonPrimary);

        const int steps = 6;
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            var d = (uint)(d0 + (d1 - d0) * t);
            InjectTouch(AndroidMotionEvent.ActionMove, id1,
                cx, (uint)Math.Max(0, (long)cy - d), w, h, 1f, 0, AndroidMotionEvent.ButtonPrimary);
            InjectTouch(AndroidMotionEvent.ActionMove, id2,
                cx, Math.Min((uint)(h - 1), cy + d), w, h, 1f, 0, AndroidMotionEvent.ButtonPrimary);
        }

        InjectTouch(AndroidMotionEvent.PointerAction(AndroidMotionEvent.ActionPointerUp, 1), id2,
            cx, Math.Min((uint)(h - 1), cy + d1), w, h, 1f, AndroidMotionEvent.ButtonPrimary, 0);
        InjectTouch(AndroidMotionEvent.ActionUp, id1,
            cx, (uint)Math.Max(0, (long)cy - d1), w, h, 0f, AndroidMotionEvent.ButtonPrimary, 0);
    }

    public void StartApp(string spec)
    {
        var bytes = Encoding.UTF8.GetBytes(spec);
        if (bytes.Length > 255)
            bytes = bytes[..255];
        var buf = new byte[2 + bytes.Length];
        buf[0] = (byte)ControlMsgType.StartApp;
        buf[1] = (byte)bytes.Length;
        bytes.CopyTo(buf, 2);
        Send(buf);
    }

    public void BackOrScreenOn(byte action)
    {
        Span<byte> buf = stackalloc byte[2] { (byte)ControlMsgType.BackOrScreenOn, action };
        Send(buf);
    }

    public void SendSimple(ControlMsgType type)
    {
        Span<byte> buf = stackalloc byte[1] { (byte)type };
        Send(buf);
    }

    public void SetDisplayPower(bool on)
    {
        Span<byte> buf = stackalloc byte[2] { (byte)ControlMsgType.SetDisplayPower, (byte)(on ? 1 : 0) };
        Send(buf);
    }

    public void SetVideoParams(int bitRate, bool suspend)
    {
        Span<byte> buf = stackalloc byte[6];
        buf[0] = (byte)ControlMsgType.SetVideoParams;
        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(1, 4), bitRate);
        buf[5] = (byte)(suspend ? 1 : 0);
        Send(buf);
    }

    public void SetClipboard(string text, bool paste)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var buf = new byte[14 + bytes.Length];
        buf[0] = (byte)ControlMsgType.SetClipboard;
        BinaryPrimitives.WriteUInt64BigEndian(buf.AsSpan(1, 8), (ulong)Random.Shared.NextInt64());
        buf[9] = (byte)(paste ? 1 : 0);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(10, 4), (uint)bytes.Length);
        bytes.CopyTo(buf, 14);
        Send(buf);
    }

    public void ScanFile(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        var buf = new byte[5 + bytes.Length];
        buf[0] = (byte)ControlMsgType.ScanFile;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(1, 4), (uint)bytes.Length);
        bytes.CopyTo(buf, 5);
        Send(buf);
    }

    private static ushort EncodePressure(float p)
        => (ushort)Math.Clamp((int)MathF.Round(p * 0xFFFF), 0, 0xFFFF);

    private static short EncodeScroll(float v)
        => (short)Math.Clamp((int)MathF.Round(Math.Clamp(v, -16f, 16f) * 2048f), short.MinValue, short.MaxValue);

    public void Dispose()
    {
        lock (_sendGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _sendQueue.Writer.TryComplete();
            _cts.Cancel();
        }
    }
}
