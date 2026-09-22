package com.touchmirror.engine.device;

import com.touchmirror.engine.Protocol;
import com.touchmirror.engine.audio.AudioCodec;
import com.touchmirror.engine.model.Codec;

import android.media.MediaCodec;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

public final class Streamer {

    private final Muxer muxer;
    private final int channel;
    private final Codec codec;
    private final boolean sendStreamMeta;
    private final boolean sendFrameMeta;

    private final ByteBuffer headerBuffer = ByteBuffer.allocate(16);

    public Streamer(Muxer muxer, int channel, Codec codec, boolean sendCodecMeta, boolean sendFrameMeta) {
        this.muxer = muxer;
        this.channel = channel;
        this.codec = codec;
        this.sendStreamMeta = sendCodecMeta;
        this.sendFrameMeta = sendFrameMeta;
    }

    public Codec getCodec() {
        return codec;
    }

    public void writeAudioHeader() throws IOException {
        writeCodecHeader();
    }

    public void writeVideoHeader() throws IOException {
        writeCodecHeader();
    }

    private void writeCodecHeader() throws IOException {
        if (sendStreamMeta) {
            ByteBuffer buffer = ByteBuffer.allocate(5);
            buffer.put((byte) Protocol.KIND_CODEC);
            buffer.putInt(codec.getId());
            buffer.flip();
            muxer.write(channel, buffer);
        }
    }

    public void writeDisableStream(boolean error) throws IOException {
        byte[] code = new byte[2];
        code[0] = (byte) Protocol.KIND_END;
        if (error) {
            code[1] = 1;
        }
        muxer.write(channel, code);
    }

    public void writePacket(ByteBuffer buffer, long pts, boolean config, boolean keyFrame) throws IOException {
        if (config) {
            if (codec == AudioCodec.OPUS) {
                fixOpusConfigPacket(buffer);
            } else if (codec == AudioCodec.FLAC) {
                fixFlacConfigPacket(buffer);
            }
        }

        headerBuffer.clear();
        headerBuffer.put((byte) Protocol.KIND_PACKET);
        headerBuffer.putLong(pts);
        byte flags = 0;
        if (config) {
            flags |= Protocol.PACKET_FLAG_CONFIG;
        }
        if (keyFrame) {
            flags |= Protocol.PACKET_FLAG_KEY_FRAME;
        }
        headerBuffer.put(flags);
        headerBuffer.flip();
        muxer.write(channel, new ByteBuffer[]{headerBuffer, buffer});
    }

    public void writePacket(ByteBuffer codecBuffer, MediaCodec.BufferInfo bufferInfo) throws IOException {
        long pts = bufferInfo.presentationTimeUs;
        boolean config = (bufferInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0;
        boolean keyFrame = (bufferInfo.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0;
        writePacket(codecBuffer, pts, config, keyFrame);
    }

    public void writeSessionMeta(int width, int height, boolean isClientResize) throws IOException {
        if (sendStreamMeta) {
            headerBuffer.clear();
            headerBuffer.put((byte) Protocol.KIND_SESSION);
            headerBuffer.putInt(width);
            headerBuffer.putInt(height);
            headerBuffer.put((byte) (isClientResize ? 1 : 0));
            headerBuffer.flip();
            muxer.write(channel, headerBuffer);
        }
    }

    private static void fixOpusConfigPacket(ByteBuffer buffer) throws IOException {

        if (buffer.remaining() < 16) {
            throw new IOException("Not enough data in OPUS config packet");
        }

        final byte[] opusHeaderId = {'A', 'O', 'P', 'U', 'S', 'H', 'D', 'R'};
        byte[] idBuffer = new byte[8];
        buffer.get(idBuffer);
        if (!Arrays.equals(idBuffer, opusHeaderId)) {
            throw new IOException("OPUS header not found");
        }

        long sizeLong = buffer.getLong();
        if (sizeLong < 0 || sizeLong >= 0x7FFFFFFF) {
            throw new IOException("Invalid block size in OPUS header: " + sizeLong);
        }

        int size = (int) sizeLong;
        if (buffer.remaining() < size) {
            throw new IOException("Not enough data in OPUS header (invalid size: " + size + ")");
        }

        buffer.limit(buffer.position() + size);
    }

    private static void fixFlacConfigPacket(ByteBuffer buffer) throws IOException {

        if (buffer.remaining() < 8) {
            throw new IOException("Not enough data in FLAC config packet");
        }

        final byte[] flacHeaderId = {'f', 'L', 'a', 'C'};
        byte[] idBuffer = new byte[4];
        buffer.get(idBuffer);
        if (!Arrays.equals(idBuffer, flacHeaderId)) {
            throw new IOException("FLAC header not found");
        }

        buffer.order(ByteOrder.BIG_ENDIAN);

        int size = buffer.getInt();
        if (buffer.remaining() < size) {
            throw new IOException("Not enough data in FLAC header (invalid size: " + size + ")");
        }

        buffer.limit(buffer.position() + size);
    }
}
