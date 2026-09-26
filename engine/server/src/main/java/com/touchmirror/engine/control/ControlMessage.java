package com.touchmirror.engine.control;

import com.touchmirror.engine.Protocol;
import com.touchmirror.engine.model.Position;

public final class ControlMessage {

    public static final int TYPE_CONFIG = Protocol.MSG_CONFIG;
    public static final int TYPE_INJECT_KEYCODE = Protocol.MSG_INJECT_KEYCODE;
    public static final int TYPE_INJECT_TEXT = Protocol.MSG_INJECT_TEXT;
    public static final int TYPE_INJECT_TOUCH_EVENT = Protocol.MSG_INJECT_TOUCH;
    public static final int TYPE_INJECT_SCROLL_EVENT = Protocol.MSG_INJECT_SCROLL;
    public static final int TYPE_BACK_OR_SCREEN_ON = Protocol.MSG_BACK_OR_SCREEN_ON;
    public static final int TYPE_EXPAND_NOTIFICATION_PANEL = Protocol.MSG_EXPAND_NOTIFICATIONS;
    public static final int TYPE_EXPAND_SETTINGS_PANEL = Protocol.MSG_EXPAND_SETTINGS;
    public static final int TYPE_COLLAPSE_PANELS = Protocol.MSG_COLLAPSE_PANELS;
    public static final int TYPE_GET_CLIPBOARD = Protocol.MSG_GET_CLIPBOARD;
    public static final int TYPE_SET_CLIPBOARD = Protocol.MSG_SET_CLIPBOARD;
    public static final int TYPE_SET_DISPLAY_POWER = Protocol.MSG_SET_DISPLAY_POWER;
    public static final int TYPE_ROTATE_DEVICE = Protocol.MSG_ROTATE_DEVICE;
    public static final int TYPE_OPEN_HARD_KEYBOARD_SETTINGS = Protocol.MSG_HARD_KEYBOARD_SETTINGS;
    public static final int TYPE_START_APP = Protocol.MSG_START_APP;
    public static final int TYPE_RESET_VIDEO = Protocol.MSG_RESET_VIDEO;
    public static final int TYPE_RESIZE_DISPLAY = Protocol.MSG_RESIZE_DISPLAY;
    public static final int TYPE_SCAN_FILE = Protocol.MSG_SCAN_FILE;
    public static final int TYPE_SET_VIDEO_PARAMS = Protocol.MSG_SET_VIDEO_PARAMS;
    public static final int TYPE_UHID_CREATE = Protocol.MSG_UHID_CREATE;
    public static final int TYPE_UHID_INPUT = Protocol.MSG_UHID_INPUT;
    public static final int TYPE_UHID_DESTROY = Protocol.MSG_UHID_DESTROY;

    public static final long SEQUENCE_INVALID = 0;

    public static final int COPY_KEY_NONE = 0;
    public static final int COPY_KEY_COPY = 1;
    public static final int COPY_KEY_CUT = 2;

    private int type;
    private String text;
    private int metaState;
    private int action;
    private int keycode;
    private int actionButton;
    private int buttons;
    private long pointerId;
    private float pressure;
    private Position position;
    private float hScroll;
    private float vScroll;
    private int copyKey;
    private boolean paste;
    private int repeat;
    private long sequence;
    private boolean on;
    private int width;
    private int height;
    private int bitRate;
    private boolean suspend;
    private byte[] config;
    private int id;
    private int vendor;
    private int product;
    private byte[] data;

    private ControlMessage() {
    }

    public static ControlMessage createConfig(byte[] data) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_CONFIG;
        msg.config = data;
        return msg;
    }

    public static ControlMessage createInjectKeycode(int action, int keycode, int repeat, int metaState) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_INJECT_KEYCODE;
        msg.action = action;
        msg.keycode = keycode;
        msg.repeat = repeat;
        msg.metaState = metaState;
        return msg;
    }

    public static ControlMessage createInjectText(String text) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_INJECT_TEXT;
        msg.text = text;
        return msg;
    }

    public static ControlMessage createInjectTouchEvent(int action, long pointerId, Position position, float pressure, int actionButton,
            int buttons) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_INJECT_TOUCH_EVENT;
        msg.action = action;
        msg.pointerId = pointerId;
        msg.pressure = pressure;
        msg.position = position;
        msg.actionButton = actionButton;
        msg.buttons = buttons;
        return msg;
    }

    public static ControlMessage createInjectScrollEvent(Position position, float hScroll, float vScroll, int buttons) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_INJECT_SCROLL_EVENT;
        msg.position = position;
        msg.hScroll = hScroll;
        msg.vScroll = vScroll;
        msg.buttons = buttons;
        return msg;
    }

    public static ControlMessage createBackOrScreenOn(int action) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_BACK_OR_SCREEN_ON;
        msg.action = action;
        return msg;
    }

    public static ControlMessage createGetClipboard(int copyKey) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_GET_CLIPBOARD;
        msg.copyKey = copyKey;
        return msg;
    }

    public static ControlMessage createSetClipboard(long sequence, String text, boolean paste) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_SET_CLIPBOARD;
        msg.sequence = sequence;
        msg.text = text;
        msg.paste = paste;
        return msg;
    }

    public static ControlMessage createSetDisplayPower(boolean on) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_SET_DISPLAY_POWER;
        msg.on = on;
        return msg;
    }

    public static ControlMessage createEmpty(int type) {
        ControlMessage msg = new ControlMessage();
        msg.type = type;
        return msg;
    }

    public static ControlMessage createStartApp(String name) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_START_APP;
        msg.text = name;
        return msg;
    }

    public static ControlMessage createResizeDisplay(int width, int height) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_RESIZE_DISPLAY;
        msg.width = width;
        msg.height = height;
        return msg;
    }

    public static ControlMessage createScanFile(String path) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_SCAN_FILE;
        msg.text = path;
        return msg;
    }

    public static ControlMessage createSetVideoParams(int bitRate, boolean suspend) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_SET_VIDEO_PARAMS;
        msg.bitRate = bitRate;
        msg.suspend = suspend;
        return msg;
    }

    public static ControlMessage createUhidCreate(int id, int vendor, int product, String name, byte[] reportDesc) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_UHID_CREATE;
        msg.id = id;
        msg.vendor = vendor;
        msg.product = product;
        msg.text = name;
        msg.data = reportDesc;
        return msg;
    }

    public static ControlMessage createUhidInput(int id, byte[] data) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_UHID_INPUT;
        msg.id = id;
        msg.data = data;
        return msg;
    }

    public static ControlMessage createUhidDestroy(int id) {
        ControlMessage msg = new ControlMessage();
        msg.type = TYPE_UHID_DESTROY;
        msg.id = id;
        return msg;
    }

    public int getType() {
        return type;
    }

    public String getText() {
        return text;
    }

    public int getMetaState() {
        return metaState;
    }

    public int getAction() {
        return action;
    }

    public int getKeycode() {
        return keycode;
    }

    public int getActionButton() {
        return actionButton;
    }

    public int getButtons() {
        return buttons;
    }

    public long getPointerId() {
        return pointerId;
    }

    public float getPressure() {
        return pressure;
    }

    public Position getPosition() {
        return position;
    }

    public float getHScroll() {
        return hScroll;
    }

    public float getVScroll() {
        return vScroll;
    }

    public int getCopyKey() {
        return copyKey;
    }

    public boolean getPaste() {
        return paste;
    }

    public int getRepeat() {
        return repeat;
    }

    public long getSequence() {
        return sequence;
    }

    public boolean getOn() {
        return on;
    }

    public int getWidth() {
        return width;
    }

    public int getHeight() {
        return height;
    }

    public int getBitRate() {
        return bitRate;
    }

    public boolean isSuspend() {
        return suspend;
    }

    public byte[] getConfig() {
        return config;
    }

    public int getId() {
        return id;
    }

    public int getVendor() {
        return vendor;
    }

    public int getProduct() {
        return product;
    }

    public byte[] getData() {
        return data;
    }
}
