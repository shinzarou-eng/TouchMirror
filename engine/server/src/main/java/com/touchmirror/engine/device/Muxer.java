package com.touchmirror.engine.device;

import com.touchmirror.engine.Protocol;
import com.touchmirror.engine.util.Ln;

import android.net.LocalSocket;

import java.io.Closeable;
import java.io.IOException;

import java.io.InterruptedIOException;
import java.io.OutputStream;
import java.io.PushbackInputStream;
import java.nio.ByteBuffer;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;

public final class Muxer implements Closeable {

    private final LocalSocket socket;
    private final OutputStream out;
    private final PushbackInputStream in;

    private final BlockingQueue<byte[]> inbound = new LinkedBlockingQueue<>(256);
    private volatile boolean running = true;
    private final Thread demuxThread;

    public Muxer(LocalSocket socket) throws IOException {
        this.socket = socket;
        this.out = socket.getOutputStream();
        this.in = new PushbackInputStream(socket.getInputStream(), Protocol.FRAME_HEADER_LENGTH);
        this.demuxThread = new Thread(this::demuxLoop, "mux-demux");
        demuxThread.start();
    }

    public void write(int channel, ByteBuffer[] parts) throws IOException {
        int total = 0;
        for (ByteBuffer part : parts) {
            total += part.remaining();
        }
        byte[] frame = new byte[Protocol.FRAME_HEADER_LENGTH + total];
        System.arraycopy(Protocol.MAGIC, 0, frame, 0, 4);
        frame[4] = (byte) channel;
        frame[5] = (byte) (total >> 24);
        frame[6] = (byte) (total >> 16);
        frame[7] = (byte) (total >> 8);
        frame[8] = (byte) total;
        int off = Protocol.FRAME_HEADER_LENGTH;
        for (ByteBuffer part : parts) {
            int n = part.remaining();
            part.get(frame, off, n);
            off += n;
        }
        synchronized (out) {
            out.write(frame);
        }
    }

    public void write(int channel, ByteBuffer part) throws IOException {
        write(channel, new ByteBuffer[]{part});
    }

    public void write(int channel, byte[] payload) throws IOException {
        byte[] frame = new byte[Protocol.FRAME_HEADER_LENGTH + payload.length];
        System.arraycopy(Protocol.MAGIC, 0, frame, 0, 4);
        frame[4] = (byte) channel;
        frame[5] = (byte) (payload.length >> 24);
        frame[6] = (byte) (payload.length >> 16);
        frame[7] = (byte) (payload.length >> 8);
        frame[8] = (byte) payload.length;
        System.arraycopy(payload, 0, frame, Protocol.FRAME_HEADER_LENGTH, payload.length);
        synchronized (out) {
            out.write(frame);
        }
    }

    public byte[] takeControlFrame() throws IOException {
        try {
            return inbound.take();
        } catch (InterruptedException e) {
            throw new InterruptedIOException();
        }
    }

    private void demuxLoop() {
        byte[] header = new byte[Protocol.FRAME_HEADER_LENGTH];
        try {
            while (running) {
                if (!readFully(header)) {
                    break;
                }
                if (isValidHeader(header)) {
                    if (!dispatchFrame(header)) {
                        break;
                    }
                } else {
                    Ln.w("mux: stream desynced, resyncing");
                    if (!resync(header)) {
                        break;
                    }
                }
            }
        } catch (IOException e) {
        }
        offerInbound(new byte[0]);
    }

    private boolean isValidHeader(byte[] header) {
        for (int i = 0; i < 4; i++) {
            if (header[i] != Protocol.MAGIC[i]) {
                return false;
            }
        }
        int channel = header[4] & 0xff;
        int len = readInt32(header, 5);
        return channel <= Protocol.CHAN_DEVICE && len > 0 && len <= Protocol.FRAME_MAX_PAYLOAD;
    }

    private boolean dispatchFrame(byte[] header) throws IOException {
        int channel = header[4] & 0xff;
        int len = readInt32(header, 5);
        byte[] payload = new byte[len];
        if (!readFully(payload)) {
            return false;
        }
        if (channel == Protocol.CHAN_CONTROL) {
            offerInbound(payload);
        }
        return true;
    }

    private boolean resync(byte[] seed) throws IOException {
        in.unread(seed);
        byte[] hdr = new byte[5];
        int match = 0;
        while (running) {
            if (match < 4) {
                int b = in.read();
                if (b < 0) {
                    return false;
                }
                match = (b == (Protocol.MAGIC[match] & 0xff)) ? match + 1 : (b == (Protocol.MAGIC[0] & 0xff) ? 1 : 0);
                continue;
            }
            match = 0;
            if (!readFully(hdr)) {
                return false;
            }
            int channel = hdr[0] & 0xff;
            int len = readInt32(hdr, 1);
            if (channel <= Protocol.CHAN_DEVICE && len > 0 && len <= Protocol.FRAME_MAX_PAYLOAD) {
                byte[] joined = new byte[Protocol.FRAME_HEADER_LENGTH];
                System.arraycopy(Protocol.MAGIC, 0, joined, 0, 4);
                System.arraycopy(hdr, 0, joined, 4, 5);
                return dispatchFrame(joined);
            }
            in.unread(hdr);
        }
        return false;
    }

    private static int readInt32(byte[] data, int offset) {
        return ((data[offset] & 0xff) << 24) | ((data[offset + 1] & 0xff) << 16) | ((data[offset + 2] & 0xff) << 8) | (data[offset + 3] & 0xff);
    }

    private void offerInbound(byte[] payload) {
        if (!inbound.offer(payload)) {
            inbound.poll();
            inbound.offer(payload);
        }
    }

    private boolean readFully(byte[] buffer) throws IOException {
        int total = 0;
        while (total < buffer.length) {
            int n = in.read(buffer, total, buffer.length - total);
            if (n < 0) {
                return false;
            }
            total += n;
        }
        return true;
    }

    @Override
    public void close() throws IOException {
        running = false;
        demuxThread.interrupt();
        socket.close();
    }
}
