package com.touchmirror.engine.control;

import com.touchmirror.engine.device.Muxer;

import java.io.ByteArrayInputStream;
import java.io.EOFException;
import java.io.IOException;

public final class ControlChannel {

    private final Muxer muxer;
    private final DeviceMessageWriter writer;

    public ControlChannel(Muxer muxer) throws IOException {
        this.muxer = muxer;
        this.writer = new DeviceMessageWriter(muxer);
    }

    public ControlMessage recv() throws IOException {
        byte[] frame = muxer.takeControlFrame();
        if (frame == null || frame.length == 0) {
            throw new EOFException("control channel closed");
        }
        return new ControlMessageReader(new ByteArrayInputStream(frame)).read();
    }

    public void send(DeviceMessage msg) throws IOException {
        writer.write(msg);
    }
}
