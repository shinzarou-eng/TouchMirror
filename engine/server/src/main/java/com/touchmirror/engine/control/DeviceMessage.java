package com.touchmirror.engine.control;

public final class DeviceMessage {

    public static final int TYPE_CLIPBOARD = com.touchmirror.engine.Protocol.DEVMSG_CLIPBOARD;
    public static final int TYPE_ACK_CLIPBOARD = com.touchmirror.engine.Protocol.DEVMSG_ACK_CLIPBOARD;
    public static final int TYPE_UHID_OUTPUT = com.touchmirror.engine.Protocol.DEVMSG_UHID_OUTPUT;
    public static final int TYPE_UHID_ERROR = com.touchmirror.engine.Protocol.DEVMSG_UHID_ERROR;

    private int type;
    private String text;
    private long sequence;
    private int id;
    private int code;
    private byte[] data;

    private DeviceMessage() {
    }

    public static DeviceMessage createClipboard(String text) {
        DeviceMessage event = new DeviceMessage();
        event.type = TYPE_CLIPBOARD;
        event.text = text;
        return event;
    }

    public static DeviceMessage createAckClipboard(long sequence) {
        DeviceMessage event = new DeviceMessage();
        event.type = TYPE_ACK_CLIPBOARD;
        event.sequence = sequence;
        return event;
    }

    public static DeviceMessage createUhidOutput(int id, byte[] data) {
        DeviceMessage event = new DeviceMessage();
        event.type = TYPE_UHID_OUTPUT;
        event.id = id;
        event.data = data;
        return event;
    }

    public static DeviceMessage createUhidError(int id, int code) {
        DeviceMessage event = new DeviceMessage();
        event.type = TYPE_UHID_ERROR;
        event.id = id;
        event.code = code;
        return event;
    }

    public int getType() {
        return type;
    }

    public String getText() {
        return text;
    }

    public long getSequence() {
        return sequence;
    }

    public int getId() {
        return id;
    }

    public int getCode() {
        return code;
    }

    public byte[] getData() {
        return data;
    }
}
