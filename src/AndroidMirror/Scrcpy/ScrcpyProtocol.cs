using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace TouchMirror.Scrcpy;

public enum ControlMsgType : byte
{
    InjectKeycode = 0,
    InjectText = 1,
    InjectTouchEvent = 2,
    InjectScrollEvent = 3,
    BackOrScreenOn = 4,
    ExpandNotificationPanel = 5,
    ExpandSettingsPanel = 6,
    CollapsePanels = 7,
    GetClipboard = 8,
    SetClipboard = 9,
    SetDisplayPower = 10,
    RotateDevice = 11,
    UhidCreate = 12,
    UhidInput = 13,
    UhidDestroy = 14,
    OpenHardKeyboardSettings = 15,
    StartApp = 16,
    ResetVideo = 17,
    CameraSetTorch = 18,
    CameraZoomIn = 19,
    CameraZoomOut = 20,
    ResizeDisplay = 21,
    ScanFile = 22,
}

public enum DeviceMsgType : byte
{
    Clipboard = 0,
    AckClipboard = 1,
    UhidOutput = 2,
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
    private readonly Socket _socket;
    private readonly object _writeLock = new();
    private readonly CancellationTokenSource _cts = new();

    public event Action<string>? ClipboardReceived;

    public ControlChannel(Socket socket)
    {
        _socket = socket;
        Task.Run(ReadLoopAsync);
    }

    private void Send(ReadOnlySpan<byte> msg)
    {
        lock (_writeLock)
        {
            _socket.Send(msg);
        }
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
            // Troncature sur une limite UTF-8 : pas de séquence coupée en deux.
            var end = 300;
            while (end > 0 && (bytes[end - 1] & 0xC0) == 0x80)
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

    /// <summary>Lance une app sur l'appareil (« pkg », « +pkg » force-stop, « pkg@user » profil).</summary>
    public void StartApp(string spec)
    {
        var bytes = Encoding.UTF8.GetBytes(spec);
        if (bytes.Length > 255)
            bytes = bytes[..255];
        var buf = new byte[2 + bytes.Length];
        buf[0] = (byte)ControlMsgType.StartApp;
        buf[1] = (byte)bytes.Length; // longueur sur 1 octet (parseString(1) côté serveur)
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

    private static ushort EncodePressure(float p)
        => (ushort)Math.Clamp((int)MathF.Round(p * 0xFFFF), 0, 0xFFFF);

    private static short EncodeScroll(float v)
        => (short)Math.Clamp((int)MathF.Round(Math.Clamp(v, -16f, 16f) * 2048f), short.MinValue, short.MaxValue);

    private async Task ReadLoopAsync()
    {
        var header = new byte[16];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!await ReadExactAsync(header.AsMemory(0, 1)))
                    break;
                var type = (DeviceMsgType)header[0];
                switch (type)
                {
                    case DeviceMsgType.Clipboard:
                        if (!await ReadExactAsync(header.AsMemory(0, 4))) return;
                        var len = BinaryPrimitives.ReadUInt32BigEndian(header);
                        var textBuf = new byte[len];
                        if (!await ReadExactAsync(textBuf)) return;
                        ClipboardReceived?.Invoke(Encoding.UTF8.GetString(textBuf));
                        break;
                    case DeviceMsgType.AckClipboard:
                        if (!await ReadExactAsync(header.AsMemory(0, 8))) return;
                        break;
                    case DeviceMsgType.UhidOutput:
                        if (!await ReadExactAsync(header.AsMemory(0, 4))) return;
                        var size = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                        var skip = new byte[size];
                        if (!await ReadExactAsync(skip)) return;
                        break;
                    default:
                        return;
                }
            }
        }
        catch { }
    }

    private async Task<bool> ReadExactAsync(Memory<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await _socket.ReceiveAsync(buffer.Slice(total), SocketFlags.None, _cts.Token);
            if (n == 0) return false;
            total += n;
        }
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _socket.Dispose(); } catch { }
        _cts.Dispose();
    }
}