package com.touchmirror.engine.control;

import com.touchmirror.engine.Protocol;
import com.touchmirror.engine.device.Muxer;
import com.touchmirror.engine.util.StringUtils;

import java.io.ByteArrayOutputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

public class DeviceMessageWriter {

    private static final int MESSAGE_MAX_SIZE = 1 << 18;
    public static final int CLIPBOARD_TEXT_MAX_LENGTH = MESSAGE_MAX_SIZE - 5;

    private final Muxer muxer;

    public DeviceMessageWriter(Muxer muxer) {
        this.muxer = muxer;
    }

    public void write(DeviceMessage msg) throws IOException {
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        DataOutputStream dos = new DataOutputStream(bos);

        int type = msg.getType();
        dos.writeByte(type);
        switch (type) {
            case DeviceMessage.TYPE_CLIPBOARD:
                String text = msg.getText();
                byte[] raw = text.getBytes(StandardCharsets.UTF_8);
                int len = StringUtils.getUtf8TruncationIndex(raw, CLIPBOARD_TEXT_MAX_LENGTH);
                dos.writeInt(len);
                dos.write(raw, 0, len);
                break;
            case DeviceMessage.TYPE_ACK_CLIPBOARD:
                dos.writeLong(msg.getSequence());
                break;
            default:
                throw new ControlProtocolException("Unknown event type: " + type);
        }
        dos.flush();

        muxer.write(Protocol.CHAN_DEVICE, bos.toByteArray());
    }
}
