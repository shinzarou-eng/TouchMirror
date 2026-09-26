using System.Buffers.Binary;
using System.Windows.Input;

namespace TouchMirror.Engine;

public static class UhidDevices
{
    public const ushort MouseId = 1;
    public const ushort KeyboardId = 2;
    public const ushort VendorId = 0x544D;
    public const ushort ProductId = 0x0001;

    public static readonly byte[] MouseDescriptor =
    {
        0x05, 0x01,
        0x09, 0x02,
        0xA1, 0x01,
        0x09, 0x01,
        0xA1, 0x00,
        0x05, 0x09,
        0x19, 0x01,
        0x29, 0x05,
        0x95, 0x05,
        0x75, 0x01,
        0x25, 0x01,
        0x81, 0x02,
        0x75, 0x03,
        0x95, 0x01,
        0x81, 0x01,
        0x05, 0x01,
        0x09, 0x30,
        0x09, 0x31,
        0x16, 0x00, 0x00,
        0x26, 0xFF, 0x7F,
        0x75, 0x10,
        0x95, 0x02,
        0x81, 0x02,
        0x09, 0x38,
        0x15, 0x81,
        0x25, 0x7F,
        0x75, 0x08,
        0x95, 0x01,
        0x81, 0x06,
        0x05, 0x0C,
        0x0A, 0x38, 0x02,
        0x15, 0x81,
        0x25, 0x7F,
        0x75, 0x08,
        0x95, 0x01,
        0x81, 0x06,
        0xC0,
        0xC0,
    };

    public static readonly byte[] KeyboardDescriptor =
    {
        0x05, 0x01,
        0x09, 0x06,
        0xA1, 0x01,
        0x05, 0x07,
        0x19, 0xE0,
        0x29, 0xE7,
        0x15, 0x00,
        0x25, 0x01,
        0x75, 0x01,
        0x95, 0x08,
        0x81, 0x02,
        0x95, 0x01,
        0x75, 0x08,
        0x81, 0x01,
        0x95, 0x06,
        0x75, 0x08,
        0x15, 0x00,
        0x25, 0x65,
        0x05, 0x07,
        0x19, 0x00,
        0x29, 0x65,
        0x81, 0x00,
        0xC0,
    };

    public static byte[] MouseReport(byte buttons, ushort x, ushort y, sbyte wheel, sbyte pan)
    {
        var r = new byte[7];
        r[0] = buttons;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(1), x);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(3), y);
        r[5] = (byte)wheel;
        r[6] = (byte)pan;
        return r;
    }

    public static byte[] KeyboardReport(byte modifiers, byte[] keys)
    {
        var r = new byte[8];
        r[0] = modifiers;
        var n = Math.Min(keys.Length, 6);
        for (var i = 0; i < n; i++)
            r[2 + i] = keys[i];
        return r;
    }

    public static int HidUsage(Key key) => key switch
    {
        Key.Back => 0x2A,
        Key.Tab => 0x2B,
        Key.Enter => 0x28,
        Key.Escape => 0x29,
        Key.Space => 0x2C,
        Key.Delete => 0x4C,
        Key.Insert => 0x49,
        Key.Home => 0x4A,
        Key.End => 0x4D,
        Key.PageUp => 0x4B,
        Key.PageDown => 0x4E,
        Key.Up => 0x52,
        Key.Down => 0x51,
        Key.Left => 0x50,
        Key.Right => 0x4F,
        Key.CapsLock => 0x39,
        Key.PrintScreen => 0x46,
        Key.Scroll => 0x47,
        Key.Pause => 0x48,
        Key.NumLock => 0x53,
        Key.OemComma => 0x36,
        Key.OemPeriod => 0x37,
        Key.OemMinus => 0x2D,
        Key.OemPlus => 0x2E,
        Key.Oem1 => 0x33,
        Key.Oem3 => 0x35,
        Key.Oem5 => 0x31,
        Key.Oem6 => 0x30,
        Key.Oem7 => 0x34,
        Key.OemQuestion => 0x38,
        Key.Divide => 0x54,
        Key.Multiply => 0x55,
        Key.Subtract => 0x56,
        Key.Add => 0x57,
        Key.Decimal => 0x63,
        _ => KeyRangeUsage(key),
    };

    private static int KeyRangeUsage(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return 0x04 + (key - Key.A);
        if (key >= Key.D1 && key <= Key.D9)
            return 0x1E + (key - Key.D1);
        if (key == Key.D0)
            return 0x27;
        if (key >= Key.NumPad1 && key <= Key.NumPad9)
            return 0x59 + (key - Key.NumPad1);
        if (key == Key.NumPad0)
            return 0x62;
        if (key >= Key.F1 && key <= Key.F12)
            return 0x3A + (key - Key.F1);
        return -1;
    }

    public static int ModifierBit(Key key) => key switch
    {
        Key.LeftCtrl => 0x01,
        Key.LeftShift => 0x02,
        Key.LeftAlt => 0x04,
        Key.LWin => 0x08,
        Key.RightCtrl => 0x10,
        Key.RightShift => 0x20,
        Key.RightAlt => 0x40,
        Key.RWin => 0x80,
        _ => -1,
    };
}
