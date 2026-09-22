package com.touchmirror.engine.control;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.AsyncProcessor;
import com.touchmirror.engine.CleanUp;
import com.touchmirror.engine.Options;
import com.touchmirror.engine.device.Device;
import com.touchmirror.engine.model.Point;
import com.touchmirror.engine.model.Position;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.video.CaptureControl;
import com.touchmirror.engine.video.NewDisplayCapture;
import com.touchmirror.engine.video.SurfaceCapture;
import com.touchmirror.engine.video.VirtualDisplayListener;
import com.touchmirror.engine.wrappers.ClipboardManager;
import com.touchmirror.engine.wrappers.InputManager;
import com.touchmirror.engine.wrappers.ServiceManager;

import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.SystemClock;
import android.util.Pair;
import android.view.InputDevice;
import android.view.KeyCharacterMap;
import android.view.KeyEvent;
import android.view.MotionEvent;

import java.io.File;
import java.io.IOException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

public class Controller implements AsyncProcessor, VirtualDisplayListener {

    private static final class DisplayData {
        private final int virtualDisplayId;
        private final PositionMapper positionMapper;

        private DisplayData(int virtualDisplayId, PositionMapper positionMapper) {
            this.virtualDisplayId = virtualDisplayId;
            this.positionMapper = positionMapper;
        }
    }

    private static final int DEFAULT_DEVICE_ID = 0;
    private static final int POINTER_ID_MOUSE = -1;
    private static final long KEEP_ACTIVE_INTERVAL_MS = 4000;

    private static final ScheduledExecutorService EXECUTOR = Executors.newSingleThreadScheduledExecutor();

    private final int displayId;
    private final boolean supportsInputEvents;
    private final ControlChannel controlChannel;
    private final CleanUp cleanUp;
    private final DeviceMessageSender sender;
    private final boolean clipboardAutosync;
    private final boolean powerOn;
    private final boolean keepActive;

    private final KeyCharacterMap charMap = KeyCharacterMap.load(KeyCharacterMap.VIRTUAL_KEYBOARD);
    private final AtomicBoolean isSettingClipboard = new AtomicBoolean();
    private final AtomicReference<DisplayData> displayData = new AtomicReference<>();
    private final Object displayDataAvailable = new Object();

    private final PointersState pointersState = new PointersState();
    private final MotionEvent.PointerProperties[] pointerProperties = new MotionEvent.PointerProperties[PointersState.MAX_POINTERS];
    private final MotionEvent.PointerCoords[] pointerCoords = new MotionEvent.PointerCoords[PointersState.MAX_POINTERS];

    private Thread thread;
    private Thread keepActiveThread;
    private ExecutorService startAppExecutor;

    private long lastTouchDown;
    private boolean keepDisplayPowerOff;
    private SurfaceCapture surfaceCapture;

    public Controller(ControlChannel controlChannel, CleanUp cleanUp, Options options) {
        this.controlChannel = controlChannel;
        this.cleanUp = cleanUp;
        this.displayId = options.getDisplayId();
        this.clipboardAutosync = options.getClipboardAutosync();
        this.powerOn = options.getPowerOn();
        this.keepActive = options.getKeepActive();

        initPointers();
        sender = new DeviceMessageSender(controlChannel);

        supportsInputEvents = Device.supportsInputEvents(displayId);
        if (!supportsInputEvents) {
            Ln.w("Input events are not supported for secondary displays before Android 10");
        }

        ClipboardManager clipboardManager = ServiceManager.getClipboardManager();
        if (clipboardAutosync) {
            if (clipboardManager != null) {
                clipboardManager.addPrimaryClipChangedListener(() -> {
                    if (isSettingClipboard.get()) {
                        return;
                    }
                    String text = Device.getClipboardText();
                    if (text != null) {
                        sender.send(DeviceMessage.createClipboard(text));
                    }
                });
            } else {
                Ln.w("No clipboard manager, copy-paste between device and computer will not work");
            }
        }
    }

    @Override
    public void onNewVirtualDisplay(int virtualDisplayId, PositionMapper positionMapper) {
        DisplayData old = displayData.getAndSet(new DisplayData(virtualDisplayId, positionMapper));
        if (old == null) {
            synchronized (displayDataAvailable) {
                displayDataAvailable.notify();
            }
        }
    }

    public void setSurfaceCapture(SurfaceCapture surfaceCapture) {
        this.surfaceCapture = surfaceCapture;
    }

    private void initPointers() {
        for (int i = 0; i < PointersState.MAX_POINTERS; ++i) {
            MotionEvent.PointerProperties props = new MotionEvent.PointerProperties();
            props.toolType = MotionEvent.TOOL_TYPE_FINGER;
            pointerProperties[i] = props;

            MotionEvent.PointerCoords coords = new MotionEvent.PointerCoords();
            coords.orientation = 0;
            coords.size = 0;
            pointerCoords[i] = coords;
        }
    }

    @Override
    public void start(TerminationListener listener) {
        if (keepActive) {
            startKeepActiveThread();
        }

        thread = new Thread(() -> {
            try {
                control();
            } catch (IOException e) {
                Ln.e("Controller error", e);
            } finally {
                Ln.d("Controller stopped");
                listener.onTerminated(true);
            }
        }, "control-recv");
        thread.start();
        sender.start();
    }

    @Override
    public void stop() {
        if (keepActiveThread != null) {
            keepActiveThread.interrupt();
        }
        if (thread != null) {
            thread.interrupt();
        }
        sender.stop();
    }

    @Override
    public void join() throws InterruptedException {
        if (thread != null) {
            thread.join();
        }
        sender.join();
    }

    private void control() throws IOException {
        if (powerOn && displayId == 0 && !Device.isScreenOn(displayId)) {
            Device.pressReleaseKeycode(KeyEvent.KEYCODE_POWER, displayId, Device.INJECT_MODE_ASYNC);
            SystemClock.sleep(500);
        }

        boolean alive = true;
        while (!Thread.currentThread().isInterrupted() && alive) {
            alive = handleEvent();
        }
    }

    private void startKeepActiveThread() {
        keepActiveThread = new Thread(() -> {
            try {
                while (true) {
                    Thread.sleep(KEEP_ACTIVE_INTERVAL_MS);
                    int actionDisplayId = getActionDisplayId();
                    if (actionDisplayId != Device.DISPLAY_ID_NONE) {
                        Device.keepActive(actionDisplayId);
                    }
                }
            } catch (InterruptedException e) {
            } catch (Throwable e) {
                Ln.e("Keep active error", e);
            } finally {
                Ln.d("Keep active thread stopped");
            }
        }, "keep-active");
        keepActiveThread.setDaemon(true);
        keepActiveThread.start();
    }

    private boolean handleEvent() throws IOException {
        ControlMessage msg;
        try {
            msg = controlChannel.recv();
        } catch (ControlProtocolException e) {
            Ln.e("Control protocol error", e);
            return false;
        } catch (IOException e) {
            return false;
        }

        switch (msg.getType()) {
            case ControlMessage.TYPE_INJECT_KEYCODE:
                if (supportsInputEvents) {
                    injectKeycode(msg.getAction(), msg.getKeycode(), msg.getRepeat(), msg.getMetaState());
                }
                return true;
            case ControlMessage.TYPE_INJECT_TEXT:
                if (supportsInputEvents) {
                    injectText(msg.getText());
                }
                return true;
            case ControlMessage.TYPE_INJECT_TOUCH_EVENT:
                if (supportsInputEvents) {
                    injectTouch(msg.getAction(), msg.getPointerId(), msg.getPosition(), msg.getPressure(), msg.getActionButton(),
                            msg.getButtons());
                }
                return true;
            case ControlMessage.TYPE_INJECT_SCROLL_EVENT:
                if (supportsInputEvents) {
                    injectScroll(msg.getPosition(), msg.getHScroll(), msg.getVScroll(), msg.getButtons());
                }
                return true;
            case ControlMessage.TYPE_BACK_OR_SCREEN_ON:
                if (supportsInputEvents) {
                    pressBackOrTurnScreenOn(msg.getAction());
                }
                return true;
            case ControlMessage.TYPE_EXPAND_NOTIFICATION_PANEL:
                Device.expandNotificationPanel();
                return true;
            case ControlMessage.TYPE_EXPAND_SETTINGS_PANEL:
                Device.expandSettingsPanel();
                return true;
            case ControlMessage.TYPE_COLLAPSE_PANELS:
                Device.collapsePanels();
                return true;
            case ControlMessage.TYPE_GET_CLIPBOARD:
                getClipboard(msg.getCopyKey());
                return true;
            case ControlMessage.TYPE_SET_CLIPBOARD:
                setClipboard(msg.getText(), msg.getPaste(), msg.getSequence());
                return true;
            case ControlMessage.TYPE_SET_DISPLAY_POWER:
                if (supportsInputEvents) {
                    setDisplayPower(msg.getOn());
                }
                return true;
            case ControlMessage.TYPE_ROTATE_DEVICE:
                int actionDisplayId = getActionDisplayId();
                if (actionDisplayId != Device.DISPLAY_ID_NONE) {
                    Device.rotateDevice(actionDisplayId);
                }
                return true;
            case ControlMessage.TYPE_OPEN_HARD_KEYBOARD_SETTINGS:
                openHardKeyboardSettings();
                return true;
            case ControlMessage.TYPE_START_APP:
                startAppAsync(msg.getText());
                return true;
            case ControlMessage.TYPE_RESET_VIDEO:
                resetVideo();
                return true;
            case ControlMessage.TYPE_RESIZE_DISPLAY:
                resizeDisplay(msg.getWidth(), msg.getHeight());
                return true;
            case ControlMessage.TYPE_SCAN_FILE:
                scanFile(msg.getText());
                return true;
            case ControlMessage.TYPE_SET_VIDEO_PARAMS:
                setVideoParams(msg.getBitRate(), msg.isSuspend());
                return true;
            default:
                throw new AssertionError("Unexpected message type: " + msg.getType());
        }
    }

    private boolean injectKeycode(int action, int keycode, int repeat, int metaState) {
        if (keepDisplayPowerOff && action == KeyEvent.ACTION_UP && (keycode == KeyEvent.KEYCODE_POWER || keycode == KeyEvent.KEYCODE_WAKEUP)) {
            assert displayId != Device.DISPLAY_ID_NONE;
            scheduleDisplayPowerOff(displayId);
        }
        return injectKeyEvent(action, keycode, repeat, metaState, Device.INJECT_MODE_ASYNC);
    }

    private boolean injectChar(char c) {
        String decomposed = KeyComposition.decompose(c);
        KeyEvent[] events = charMap.getEvents(decomposed != null ? decomposed.toCharArray() : new char[]{c});
        if (events == null) {
            return false;
        }

        int actionDisplayId = getActionDisplayId();
        if (actionDisplayId == Device.DISPLAY_ID_NONE) {
            return true;
        }
        for (KeyEvent event : events) {
            if (!Device.injectEvent(event, actionDisplayId, Device.INJECT_MODE_ASYNC)) {
                return false;
            }
        }
        return true;
    }

    private int injectText(String text) {
        int injected = 0;
        for (char c : text.toCharArray()) {
            if (injectChar(c)) {
                injected++;
            } else {
                Ln.w("Could not inject char u+" + String.format("%04x", (int) c));
            }
        }
        return injected;
    }

    private Pair<Point, Integer> getEventPointAndDisplayId(Position position) {
        DisplayData data = displayData.get();
        assert data != null || displayId != Device.DISPLAY_ID_NONE : "Cannot receive a positional event without a display";

        Point point;
        int targetDisplayId;
        if (data != null) {
            point = data.positionMapper.map(position);
            if (point == null) {
                if (Ln.isEnabled(Ln.Level.VERBOSE)) {
                    Ln.v("Ignore positional event generated for size " + position.getScreenSize() + " (current size is "
                            + data.positionMapper.getVideoSize() + ")");
                }
                return null;
            }
            targetDisplayId = data.virtualDisplayId;
        } else {
            point = position.getPoint();
            targetDisplayId = displayId;
        }

        return Pair.create(point, targetDisplayId);
    }

    private boolean injectTouch(int action, long pointerId, Position position, float pressure, int actionButton, int buttons) {
        long now = SystemClock.uptimeMillis();

        Pair<Point, Integer> target = getEventPointAndDisplayId(position);
        if (target == null) {
            return false;
        }

        int pointerIndex = pointersState.getPointerIndex(pointerId);
        if (pointerIndex == -1) {
            Ln.w("Too many pointers for touch event");
            return false;
        }
        Pointer pointer = pointersState.get(pointerIndex);
        pointer.setPoint(target.first);
        pointer.setPressure(pressure);

        int source;
        boolean secondaryButtons = ((actionButton | buttons) & ~MotionEvent.BUTTON_PRIMARY) != 0;
        if (pointerId == POINTER_ID_MOUSE && (action == MotionEvent.ACTION_HOVER_MOVE || secondaryButtons)) {
            pointerProperties[pointerIndex].toolType = MotionEvent.TOOL_TYPE_MOUSE;
            source = InputDevice.SOURCE_MOUSE;
            pointer.setUp(buttons == 0);
        } else {
            pointerProperties[pointerIndex].toolType = MotionEvent.TOOL_TYPE_FINGER;
            source = InputDevice.SOURCE_TOUCHSCREEN;
            buttons = 0;
            pointer.setUp(action == MotionEvent.ACTION_UP);
        }

        int pointerCount = pointersState.update(pointerProperties, pointerCoords);
        if (pointerCount == 1) {
            if (action == MotionEvent.ACTION_DOWN) {
                lastTouchDown = now;
            }
        } else if (action == MotionEvent.ACTION_UP) {
            action = MotionEvent.ACTION_POINTER_UP | (pointerIndex << MotionEvent.ACTION_POINTER_INDEX_SHIFT);
        } else if (action == MotionEvent.ACTION_DOWN) {
            action = MotionEvent.ACTION_POINTER_DOWN | (pointerIndex << MotionEvent.ACTION_POINTER_INDEX_SHIFT);
        }

        if (Build.VERSION.SDK_INT >= AndroidVersions.API_23_ANDROID_6_0 && source == InputDevice.SOURCE_MOUSE) {
            if (action == MotionEvent.ACTION_DOWN) {
                if (actionButton == buttons) {
                    MotionEvent downEvent = MotionEvent.obtain(lastTouchDown, now, MotionEvent.ACTION_DOWN, pointerCount, pointerProperties,
                            pointerCoords, 0, buttons, 1f, 1f, DEFAULT_DEVICE_ID, 0, source, 0);
                    if (!Device.injectEvent(downEvent, target.second, Device.INJECT_MODE_ASYNC)) {
                        return false;
                    }
                }

                MotionEvent pressEvent = MotionEvent.obtain(lastTouchDown, now, MotionEvent.ACTION_BUTTON_PRESS, pointerCount,
                        pointerProperties, pointerCoords, 0, buttons, 1f, 1f, DEFAULT_DEVICE_ID, 0, source, 0);
                if (!InputManager.setActionButton(pressEvent, actionButton)) {
                    return false;
                }
                return Device.injectEvent(pressEvent, target.second, Device.INJECT_MODE_ASYNC);
            }

            if (action == MotionEvent.ACTION_UP) {
                MotionEvent releaseEvent = MotionEvent.obtain(lastTouchDown, now, MotionEvent.ACTION_BUTTON_RELEASE, pointerCount,
                        pointerProperties, pointerCoords, 0, buttons, 1f, 1f, DEFAULT_DEVICE_ID, 0, source, 0);
                if (!InputManager.setActionButton(releaseEvent, actionButton)) {
                    return false;
                }
                if (!Device.injectEvent(releaseEvent, target.second, Device.INJECT_MODE_ASYNC)) {
                    return false;
                }

                if (buttons == 0) {
                    MotionEvent upEvent = MotionEvent.obtain(lastTouchDown, now, MotionEvent.ACTION_UP, pointerCount, pointerProperties,
                            pointerCoords, 0, buttons, 1f, 1f, DEFAULT_DEVICE_ID, 0, source, 0);
                    return Device.injectEvent(upEvent, target.second, Device.INJECT_MODE_ASYNC);
                }
                return true;
            }
        }

        MotionEvent event = MotionEvent.obtain(lastTouchDown, now, action, pointerCount, pointerProperties, pointerCoords, 0, buttons, 1f,
                1f, DEFAULT_DEVICE_ID, 0, source, 0);
        return Device.injectEvent(event, target.second, Device.INJECT_MODE_ASYNC);
    }

    private boolean injectScroll(Position position, float hScroll, float vScroll, int buttons) {
        long now = SystemClock.uptimeMillis();

        Pair<Point, Integer> target = getEventPointAndDisplayId(position);
        if (target == null) {
            return false;
        }

        pointerProperties[0].id = 0;
        pointerCoords[0].x = target.first.getX();
        pointerCoords[0].y = target.first.getY();
        pointerCoords[0].setAxisValue(MotionEvent.AXIS_HSCROLL, hScroll);
        pointerCoords[0].setAxisValue(MotionEvent.AXIS_VSCROLL, vScroll);

        MotionEvent event = MotionEvent.obtain(lastTouchDown, now, MotionEvent.ACTION_SCROLL, 1, pointerProperties, pointerCoords, 0,
                buttons, 1f, 1f, DEFAULT_DEVICE_ID, 0, InputDevice.SOURCE_MOUSE, 0);
        return Device.injectEvent(event, target.second, Device.INJECT_MODE_ASYNC);
    }

    private static void scheduleDisplayPowerOff(int displayId) {
        EXECUTOR.schedule(() -> {
            Ln.i("Forcing display off");
            Device.setDisplayPower(displayId, false);
        }, 200, TimeUnit.MILLISECONDS);
    }

    private boolean pressBackOrTurnScreenOn(int action) {
        boolean injectBack;
        if (Build.VERSION.SDK_INT >= AndroidVersions.API_34_ANDROID_14) {
            int actionDisplayId = getActionDisplayId();
            injectBack = actionDisplayId == Device.DISPLAY_ID_NONE || Device.isScreenOn(actionDisplayId);
        } else {
            injectBack = displayId != 0 || Device.isScreenOn(0);
        }
        if (injectBack) {
            return injectKeyEvent(action, KeyEvent.KEYCODE_BACK, 0, 0, Device.INJECT_MODE_ASYNC);
        }

        if (action != KeyEvent.ACTION_DOWN) {
            return true;
        }

        if (keepDisplayPowerOff) {
            assert displayId != Device.DISPLAY_ID_NONE;
            scheduleDisplayPowerOff(displayId);
        }
        return pressReleaseKeycode(KeyEvent.KEYCODE_POWER, Device.INJECT_MODE_ASYNC);
    }

    private void getClipboard(int copyKey) {
        if (copyKey != ControlMessage.COPY_KEY_NONE && Build.VERSION.SDK_INT >= AndroidVersions.API_24_ANDROID_7_0 && supportsInputEvents) {
            int key = copyKey == ControlMessage.COPY_KEY_COPY ? KeyEvent.KEYCODE_COPY : KeyEvent.KEYCODE_CUT;
            pressReleaseKeycode(key, Device.INJECT_MODE_WAIT_FOR_FINISH);
        }

        if (!clipboardAutosync) {
            String text = Device.getClipboardText();
            if (text != null) {
                sender.send(DeviceMessage.createClipboard(text));
            }
        }
    }

    private boolean setClipboard(String text, boolean paste, long sequence) {
        isSettingClipboard.set(true);
        boolean ok = Device.setClipboardText(text);
        isSettingClipboard.set(false);
        if (ok) {
            Ln.i("Device clipboard set");
        }

        if (paste && Build.VERSION.SDK_INT >= AndroidVersions.API_24_ANDROID_7_0 && supportsInputEvents) {
            pressReleaseKeycode(KeyEvent.KEYCODE_PASTE, Device.INJECT_MODE_ASYNC);
        }

        if (sequence != ControlMessage.SEQUENCE_INVALID) {
            sender.send(DeviceMessage.createAckClipboard(sequence));
        }

        return ok;
    }

    private void openHardKeyboardSettings() {
        ServiceManager.getActivityManager().startActivity(new Intent("android.settings.HARD_KEYBOARD_SETTINGS"));
    }

    private boolean injectKeyEvent(int action, int keyCode, int repeat, int metaState, int injectMode) {
        int actionDisplayId = getActionDisplayId();
        return actionDisplayId != Device.DISPLAY_ID_NONE
                && Device.injectKeyEvent(action, keyCode, repeat, metaState, actionDisplayId, injectMode);
    }

    private boolean pressReleaseKeycode(int keyCode, int injectMode) {
        int actionDisplayId = getActionDisplayId();
        return actionDisplayId != Device.DISPLAY_ID_NONE && Device.pressReleaseKeycode(keyCode, actionDisplayId, injectMode);
    }

    private int getActionDisplayId() {
        if (displayId != Device.DISPLAY_ID_NONE) {
            return displayId;
        }
        DisplayData data = displayData.get();
        return data != null ? data.virtualDisplayId : Device.DISPLAY_ID_NONE;
    }

    public void startAppAsync(String name) {
        if (startAppExecutor == null) {
            startAppExecutor = Executors.newSingleThreadExecutor();
        }
        startAppExecutor.submit(() -> startApp(name));
    }

    private void startApp(String name) {
        int startAppDisplayId = getStartAppDisplayId();
        if (startAppDisplayId == Device.DISPLAY_ID_NONE) {
            Ln.e("No known display id to start app \"" + name + "\"");
            return;
        }
        Device.startApp(name, startAppDisplayId);
    }

    private int getStartAppDisplayId() {
        if (displayId != Device.DISPLAY_ID_NONE) {
            return displayId;
        }

        try {
            DisplayData data = waitDisplayData(1000);
            if (data != null) {
                return data.virtualDisplayId;
            }
        } catch (InterruptedException e) {
        }

        return Device.DISPLAY_ID_NONE;
    }

    private DisplayData waitDisplayData(long timeoutMillis) throws InterruptedException {
        long deadline = System.currentTimeMillis() + timeoutMillis;
        synchronized (displayDataAvailable) {
            DisplayData data = displayData.get();
            while (data == null) {
                long timeout = deadline - System.currentTimeMillis();
                if (timeout < 0) {
                    return null;
                }
                if (timeout > 0) {
                    displayDataAvailable.wait(timeout);
                }
                data = displayData.get();
            }
            return data;
        }
    }

    private void setDisplayPower(boolean on) {
        int targetDisplayId = displayId != Device.DISPLAY_ID_NONE ? displayId : 0;
        if (Device.setDisplayPower(targetDisplayId, on)) {
            keepDisplayPowerOff = displayId != Device.DISPLAY_ID_NONE && !on;
            Ln.i("Device display turned " + (on ? "on" : "off"));
            if (cleanUp != null) {
                cleanUp.setRestoreDisplayPower(!on);
            }
        }
    }

    private void resetVideo() {
        if (surfaceCapture != null) {
            Ln.i("Video capture reset");
            surfaceCapture.getCaptureControl().reset(CaptureControl.RESET_REASON_CLIENT_RESET);
        }
    }

    private void setVideoParams(int bitRate, boolean suspend) {
        if (surfaceCapture != null) {
            surfaceCapture.setSuspended(suspend);
            surfaceCapture.getCaptureControl().setVideoParams(bitRate, suspend);
        }
    }

    private void resizeDisplay(int width, int height) {
        ((NewDisplayCapture) surfaceCapture).requestResize(width, height);
    }

    private void scanFile(String path) {
        try {
            @SuppressWarnings("deprecation")
            Intent intent = new Intent(Intent.ACTION_MEDIA_SCANNER_SCAN_FILE, Uri.fromFile(new File(path)));
            Device.sendBroadcast(intent);
        } catch (Throwable t) {
            Ln.e("MediaStore scan failed for " + path, t);
        }
    }
}
