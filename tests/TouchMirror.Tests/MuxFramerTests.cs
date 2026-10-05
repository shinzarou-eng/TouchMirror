using System.Buffers.Binary;
using TouchMirror.Engine;
using Xunit;

namespace TouchMirror.Tests;

public sealed class MuxFramerTests
{
    private static byte[] Frame(byte channel, byte[] payload)
    {
        var f = new byte[9 + payload.Length];
        "TMIR"u8.CopyTo(f);
        f[4] = channel;
        BinaryPrimitives.WriteInt32BigEndian(f.AsSpan(5), payload.Length);
        payload.CopyTo(f.AsSpan(9));
        return f;
    }

    private static async Task<(byte Channel, byte[] Payload)?> ReadFrameAsync(MuxFramer framer)
    {
        var h = await framer.ReadHeaderAsync(CancellationToken.None);
        if (h is null)
            return null;
        var payload = new byte[h.Value.Size];
        Assert.True(await framer.FillExactAsync(payload, CancellationToken.None));
        return (h.Value.Channel, payload);
    }

    private sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _pos;

        public ChunkedStream(byte[] data, int chunk)
        {
            _data = data;
            _chunk = chunk;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }

        public override int Read(Span<byte> buffer)
        {
            var n = Math.Min(Math.Min(buffer.Length, _chunk), _data.Length - _pos);
            if (n <= 0)
                return 0;
            _data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ValidFrames_ParseInOrder()
    {
        var p1 = new byte[] { 1, 2, 3 };
        var p2 = new byte[1000];
        new Random(1).NextBytes(p2);
        var stream = new MemoryStream();
        stream.Write(Frame(1, p1));
        stream.Write(Frame(2, p2));
        stream.Write(Frame(4, new byte[] { 9 }));
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        var f1 = await ReadFrameAsync(framer);
        var f2 = await ReadFrameAsync(framer);
        var f3 = await ReadFrameAsync(framer);

        Assert.Equal((byte)1, f1!.Value.Channel);
        Assert.Equal(p1, f1.Value.Payload);
        Assert.Equal((byte)2, f2!.Value.Channel);
        Assert.Equal(p2, f2.Value.Payload);
        Assert.Equal((byte)4, f3!.Value.Channel);
        Assert.Null(await framer.ReadHeaderAsync(CancellationToken.None));
        Assert.Equal(0, framer.Resyncs);
    }

    [Fact]
    public async Task GarbageBeforeFrame_Resyncs()
    {
        var payload = new byte[] { 7, 7, 7 };
        var stream = new MemoryStream();
        stream.Write(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A });
        stream.Write(Frame(1, payload));
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        var f = await ReadFrameAsync(framer);

        Assert.Equal(payload, f!.Value.Payload);
        Assert.Equal(1, framer.Resyncs);
    }

    [Fact]
    public async Task FalseMagic_InvalidSize_SkipsToRealFrame()
    {
        var payload = new byte[] { 0xAA };
        var stream = new MemoryStream();
        var fake = new byte[14];
        "TMIR"u8.CopyTo(fake);
        fake[4] = 1;
        BinaryPrimitives.WriteInt32BigEndian(fake.AsSpan(5), int.MaxValue);
        stream.Write(fake);
        stream.Write(Frame(1, payload));
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        var f = await ReadFrameAsync(framer);

        Assert.Equal(payload, f!.Value.Payload);
        Assert.True(framer.Resyncs >= 1);
    }

    [Fact]
    public async Task MagicInsideGarbage_ResyncsToNextRealFrame()
    {
        var payload = new byte[] { 0x55 };
        var stream = new MemoryStream();
        stream.Write("xxTMIRyyyyTMIRzzz"u8.ToArray());
        stream.Write(Frame(3, payload));
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        var f = await ReadFrameAsync(framer);

        Assert.Equal((byte)3, f!.Value.Channel);
        Assert.Equal(payload, f.Value.Payload);
    }

    [Fact]
    public async Task MagicSplitAcrossSmallReads_StillFound()
    {
        var payload = new byte[] { 0x42 };
        var stream = new MemoryStream();
        stream.Write(new byte[] { 0x00 });
        stream.Write(Frame(1, payload));
        var chunked = new ChunkedStream(stream.ToArray(), 3);

        var framer = new MuxFramer(chunked);
        var f = await ReadFrameAsync(framer);

        Assert.Equal(payload, f!.Value.Payload);
    }

    [Fact]
    public async Task GarbageLargerThanBuffer_Resyncs()
    {
        var payload = new byte[] { 0x11 };
        var stream = new MemoryStream();
        var junk = new byte[40000];
        new Random(7).NextBytes(junk);
        stream.Write(junk);
        stream.Write(Frame(1, payload));
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        var f = await ReadFrameAsync(framer);

        Assert.Equal(payload, f!.Value.Payload);
        Assert.Equal(1, framer.Resyncs);
    }

    [Fact]
    public async Task EofMidFrame_ReturnsNullOnNextRead()
    {
        var stream = new MemoryStream();
        var partial = Frame(1, new byte[] { 1, 2, 3 });
        stream.Write(partial.AsSpan(0, 9 + 2).ToArray());
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        var h = await framer.ReadHeaderAsync(CancellationToken.None);
        Assert.NotNull(h);
        Assert.False(await framer.FillExactAsync(new byte[3], CancellationToken.None));
    }

    [Fact]
    public async Task Fuzz_RandomGarbage_AllFramesRecovered()
    {
        var rng = new Random(42);
        var stream = new MemoryStream();
        var expected = new List<byte[]>();
        for (var i = 0; i < 50; i++)
        {
            var junk = new byte[rng.Next(0, 500)];
            rng.NextBytes(junk);
            stream.Write(junk);
            var payload = new byte[rng.Next(1, 2000)];
            rng.NextBytes(payload);
            stream.Write(Frame((byte)(1 + rng.Next(4)), payload));
            expected.Add(payload);
        }
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        foreach (var want in expected)
        {
            var f = await ReadFrameAsync(framer);
            Assert.NotNull(f);
            Assert.Equal(want, f.Value.Payload);
        }
        Assert.Null(await framer.ReadHeaderAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TFalsePositive_ThenRealFrame()
    {
        var stream = new MemoryStream();
        stream.Write("TTTTMIR"u8.ToArray());
        var payload = new byte[] { 0x33 };
        stream.Write(Frame(1, payload));
        stream.Position = 0;

        var framer = new MuxFramer(stream);
        var f = await ReadFrameAsync(framer);
        Assert.Equal(payload, f!.Value.Payload);
    }
}
