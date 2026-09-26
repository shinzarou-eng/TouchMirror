package com.touchmirror.engine;

public final class Protocol {

    public static final byte[] MAGIC = {'T', 'M', 'I', 'R'};
    public static final short VERSION = 3;

    public static final int CHAN_SESSION = 0;
    public static final int CHAN_VIDEO = 1;
    public static final int CHAN_AUDIO = 2;
    public static final int CHAN_CONTROL = 3;
    public static final int CHAN_DEVICE = 4;

    public static final int FRAME_HEADER_LENGTH = 5;
    public static final int FRAME_MAX_PAYLOAD = 1 << 26;

    public static final int CAP_VIDEO = 1;
    public static final int CAP_AUDIO = 1 << 1;
    public static final int CAP_CONTROL = 1 << 2;
    public static final int CAP_CLIPBOARD = 1 << 3;
    public static final int CAP_H265 = 1 << 4;
    public static final int CAP_AV1 = 1 << 5;
    public static final int CAP_VDISPLAY = 1 << 6;
    public static final int CAP_UHID = 1 << 7;

    public static final int MAX_DEVICE_NAME_LENGTH = 63;

    public static final int KIND_CODEC = 0;
    public static final int KIND_PACKET = 1;
    public static final int KIND_SESSION = 2;
    public static final int KIND_END = 3;

    public static final int PACKET_FLAG_CONFIG = 1;
    public static final int PACKET_FLAG_KEY_FRAME = 1 << 1;

    public static final int MSG_CONFIG = 0x01;

    public static final int MSG_INJECT_KEYCODE = 0x10;
    public static final int MSG_INJECT_TEXT = 0x11;
    public static final int MSG_INJECT_TOUCH = 0x12;
    public static final int MSG_INJECT_SCROLL = 0x13;

    public static final int MSG_BACK_OR_SCREEN_ON = 0x20;
    public static final int MSG_EXPAND_NOTIFICATIONS = 0x21;
    public static final int MSG_EXPAND_SETTINGS = 0x22;
    public static final int MSG_COLLAPSE_PANELS = 0x23;
    public static final int MSG_SET_DISPLAY_POWER = 0x24;
    public static final int MSG_ROTATE_DEVICE = 0x25;
    public static final int MSG_HARD_KEYBOARD_SETTINGS = 0x26;
    public static final int MSG_START_APP = 0x27;
    public static final int MSG_SCAN_FILE = 0x28;

    public static final int MSG_RESET_VIDEO = 0x30;
    public static final int MSG_RESIZE_DISPLAY = 0x31;
    public static final int MSG_SET_VIDEO_PARAMS = 0x32;

    public static final int MSG_GET_CLIPBOARD = 0x40;
    public static final int MSG_SET_CLIPBOARD = 0x41;

    public static final int MSG_UHID_CREATE = 0x50;
    public static final int MSG_UHID_INPUT = 0x51;
    public static final int MSG_UHID_DESTROY = 0x52;

    public static final int DEVMSG_CLIPBOARD = 0x50;
    public static final int DEVMSG_ACK_CLIPBOARD = 0x51;
    public static final int DEVMSG_UHID_OUTPUT = 0x60;
    public static final int DEVMSG_UHID_ERROR = 0x61;

    public static final int CFG_AUDIO = 0x01;
    public static final int CFG_VIDEO_CODEC = 0x02;
    public static final int CFG_AUDIO_CODEC = 0x03;
    public static final int CFG_MAX_SIZE = 0x04;
    public static final int CFG_MAX_FPS = 0x05;
    public static final int CFG_VIDEO_BIT_RATE = 0x06;
    public static final int CFG_FLAGS = 0x07;
    public static final int CFG_NEW_DISPLAY = 0x08;
    public static final int CFG_START_APP = 0x09;
    public static final int CFG_LOG_LEVEL = 0x0A;

    public static final int FLAG_VIDEO = 1;
    public static final int FLAG_STAY_AWAKE = 1 << 1;
    public static final int FLAG_POWER_ON = 1 << 2;
    public static final int FLAG_CLEANUP = 1 << 3;
    public static final int FLAG_DOWNSIZE_ON_ERROR = 1 << 4;
    public static final int FLAG_CLIPBOARD_AUTOSYNC = 1 << 5;

    private Protocol() {
    }
}
