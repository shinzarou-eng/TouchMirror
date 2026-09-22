package com.touchmirror.engine.device;

import com.touchmirror.engine.Protocol;

import android.net.LocalSocket;

import java.io.Closeable;
import java.io.IOException;
import java.io.InputStream;

import java.io.InterruptedIOException;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;

public final class Muxer implements Closeable {

    private final LocalSocket socket;
    private final OutputStream out;
    private final InputStream in;

    private final BlockingQueue<byte[]> inbound = new LinkedBlockingQueue<>();
    private volatile boolean running = true;
    private final Thread demuxThread;

    public Muxer(LocalSocket socket) throws IOException {
        this.socket = socket;
        this.out = socket.getOutputStream();
        this.in = socket.getInputStream();
        this.demuxThread = new Thread(this::demuxLoop, "mux-demux");
        demuxThread.start();
    }

    public void write(int channel, ByteBuffer[] parts) throws IOException {
        int total = 0;
        for (ByteBuffer part : parts) {
            total += part.remaining();
        }
        byte[] frame = new byte[Protocol.FRAME_HEADER_LENGTH + total];
        frame[0] = (byte) channel;
        frame[1] = (byte) (total >> 24);
        frame[2] = (byte) (total >> 16);
        frame[3] = (byte) (total >> 8);
        frame[4] = (byte) total;
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
        frame[0] = (byte) channel;
        frame[1] = (byte) (payload.length >> 24);
        frame[2] = (byte) (payload.length >> 16);
        frame[3] = (byte) (payload.length >> 8);
        frame[4] = (byte) payload.length;
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
                int channel = header[0] & 0xff;
                int len = ((header[1] & 0xff) << 24) | ((header[2] & 0xff) << 16) | ((header[3] & 0xff) << 8) | (header[4] & 0xff);
                if (len <= 0 || len > Protocol.FRAME_MAX_PAYLOAD) {
                    break;
                }
                byte[] payload = new byte[len];
                if (!readFully(payload)) {
                    break;
                }
                if (channel == Protocol.CHAN_CONTROL) {
                    inbound.offer(payload);
                }
            }
        } catch (IOException e) {
        }
        inbound.offer(new byte[0]);
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
