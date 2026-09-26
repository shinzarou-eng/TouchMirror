using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Windows.Input;
using TouchMirror.Engine;
using Xunit;

namespace TouchMirror.Tests;

public sealed class UhidTests
{
    [Fact]
    public void MouseReport_Layout()
    {
        var r = UhidDevices.MouseReport(0b10101, 0x1234, 0xABCD, -3, 2);
        Assert.Equal(7, r.Length);
        Assert.Equal(0b10101, r[0]);
        Assert.Equal(new byte[] { 0x34, 0x12 }, r[1..3]);
        Assert.Equal(new byte[] { 0xCD, 0xAB }, r[3..5]);
        Assert.Equal(unchecked((byte)-3), r[5]);
        Assert.Equal(2, r[6]);
    }

    [Fact]
    public void KeyboardReport_ModsAndKeys()
    {
        var r = UhidDevices.KeyboardReport(0x22, new byte[] { 4, 5, 6 });
        Assert.Equal(8, r.Length);
        Assert.Equal(0x22, r[0]);
        Assert.Equal(0, r[1]);
        Assert.Equal(new byte[] { 4, 5, 6, 0, 0, 0 }, r[2..8]);
    }

    [Fact]
    public void KeyboardReport_MoreThanSixKeysTruncated()
    {
        var r = UhidDevices.KeyboardReport(0, new byte[] { 4, 5, 6, 7, 8, 9, 10, 11 });
        Assert.Equal(new byte[] { 4, 5, 6, 7, 8, 9 }, r[2..8]);
    }

    [Theory]
    [InlineData(Key.A, 0x04)]
    [InlineData(Key.Z, 0x1D)]
    [InlineData(Key.D1, 0x1E)]
    [InlineData(Key.D0, 0x27)]
    [InlineData(Key.Enter, 0x28)]
    [InlineData(Key.Escape, 0x29)]
    [InlineData(Key.Space, 0x2C)]
    [InlineData(Key.Up, 0x52)]
    [InlineData(Key.F1, 0x3A)]
    [InlineData(Key.F12, 0x45)]
    [InlineData(Key.NumPad0, 0x62)]
    [InlineData(Key.Delete, 0x4C)]
    public void HidUsage_Maps(Key key, int expected) => Assert.Equal(expected, UhidDevices.HidUsage(key));

    [Theory]
    [InlineData(Key.LeftCtrl, 0x01)]
    [InlineData(Key.RightAlt, 0x40)]
    [InlineData(Key.A, -1)]
    public void ModifierBit_Maps(Key key, int expected) => Assert.Equal(expected, UhidDevices.ModifierBit(key));

    [Fact]
    public void Descriptors_ParsePlausible()
    {
        Assert.Equal(0xC0, UhidDevices.MouseDescriptor[^1]);
        Assert.Equal(0xC0, UhidDevices.KeyboardDescriptor[^1]);
        Assert.True(UhidDevices.MouseDescriptor.Length < 4096);
        Assert.True(UhidDevices.KeyboardDescriptor.Length < 4096);
    }

    private static (Socket client, Socket server) SocketPair()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect((IPEndPoint)l.LocalEndpoint);
        var server = l.AcceptSocket();
        l.Stop();
        server.ReceiveTimeout = 4000;
        return (client, server);
    }

    private static byte[] ReadPayload(Socket s)
    {
        var head = new byte[5];
        var got = 0;
        while (got < 5)
            got += s.Receive(head, got, 5 - got, SocketFlags.None);
        var len = BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(1));
        var body = new byte[len];
        got = 0;
        while (got < len)
            got += s.Receive(body, got, len - got, SocketFlags.None);
        Assert.Equal(3, head[0]);
        return body;
    }

    [Fact]
    public void UhidCreate_WireFormat()
    {
        var (a, b) = SocketPair();
        using var c = new ControlChannel(a);
        c.UhidCreate(7, 0x544D, 0x0001, "Test Mouse", new byte[] { 0x05, 0x01 });
        var p = ReadPayload(b);
        Assert.Equal(0x50, p[0]);
        Assert.Equal(new byte[] { 0, 7 }, p[1..3]);
        Assert.Equal(new byte[] { 0x54, 0x4D }, p[3..5]);
        Assert.Equal(new byte[] { 0, 1 }, p[5..7]);
        Assert.Equal(10, p[7]);
        Assert.Equal("Test Mouse", System.Text.Encoding.UTF8.GetString(p[8..18]));
        Assert.Equal(new byte[] { 0, 2 }, p[18..20]);
        Assert.Equal(new byte[] { 0x05, 0x01 }, p[20..22]);
        b.Dispose();
    }

    [Fact]
    public void UhidInput_WireFormat()
    {
        var (a, b) = SocketPair();
        using var c = new ControlChannel(a);
        c.UhidInput(1, new byte[] { 1, 2, 3 });
        var p = ReadPayload(b);
        Assert.Equal(0x51, p[0]);
        Assert.Equal(new byte[] { 0, 1 }, p[1..3]);
        Assert.Equal(new byte[] { 0, 3 }, p[3..5]);
        Assert.Equal(new byte[] { 1, 2, 3 }, p[5..8]);
        b.Dispose();
    }

    [Fact]
    public void UhidDestroy_WireFormat()
    {
        var (a, b) = SocketPair();
        using var c = new ControlChannel(a);
        c.UhidDestroy(9);
        var p = ReadPayload(b);
        Assert.Equal(0x52, p[0]);
        Assert.Equal(new byte[] { 0, 9 }, p[1..3]);
        Assert.Equal(3, p.Length);
        b.Dispose();
    }

    [Fact]
    public void UhidInput_TooLarge_Dropped()
    {
        var (a, b) = SocketPair();
        b.ReceiveTimeout = 1500;
        using var c = new ControlChannel(a);
        c.UhidInput(1, new byte[5000]);
        Assert.Throws<SocketException>(() => ReadPayload(b));
        b.Dispose();
    }

    [Fact]
    public void UhidCreate_LongNameAndDesc_Truncated()
    {
        var (a, b) = SocketPair();
        using var c = new ControlChannel(a);
        c.UhidCreate(1, 0, 0, new string('x', 300), new byte[5000]);
        var p = ReadPayload(b);
        Assert.Equal(0x50, p[0]);
        Assert.Equal(127, p[7]);
        var descLen = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(8 + 127));
        Assert.Equal(4096, descLen);
        Assert.Equal(10 + 127 + 4096, p.Length);
        b.Dispose();
    }

    [Fact]
    public void Feed_UhidOutput_Dispatched()
    {
        var (a, b) = SocketPair();
        using var c = new ControlChannel(a);
        byte[]? got = null;
        ushort gotId = 0;
        c.UhidOutputReceived += (id, data) => { gotId = id; got = data; };
        var frame = new byte[] { 0x60, 0, 7, 0, 3, 9, 8, 7 };
        c.Feed(frame);
        Assert.Equal(7, gotId);
        Assert.Equal(new byte[] { 9, 8, 7 }, got);
        b.Dispose();
    }

    [Fact]
    public void Feed_UhidError_Dispatched()
    {
        var (a, b) = SocketPair();
        using var c = new ControlChannel(a);
        ushort gotId = 0;
        byte gotCode = 0;
        c.UhidErrorReceived += (id, code) => { gotId = id; gotCode = code; };
        c.Feed(new byte[] { 0x61, 0, 2, 1 });
        Assert.Equal(2, gotId);
        Assert.Equal(1, gotCode);
        b.Dispose();
    }

    [Theory]
    [InlineData(new byte[] { 0x60 })]
    [InlineData(new byte[] { 0x60, 0, 1, 0 })]
    [InlineData(new byte[] { 0x60, 0, 1, 0, 10, 1 })]
    [InlineData(new byte[] { 0x61, 0, 1 })]
    public void Feed_MalformedUhid_Ignored(byte[] frame)
    {
        var (a, b) = SocketPair();
        using var c = new ControlChannel(a);
        var fired = false;
        c.UhidOutputReceived += (_, _) => fired = true;
        c.UhidErrorReceived += (_, _) => fired = true;
        c.Feed(frame);
        Assert.False(fired);
        b.Dispose();
    }
}
