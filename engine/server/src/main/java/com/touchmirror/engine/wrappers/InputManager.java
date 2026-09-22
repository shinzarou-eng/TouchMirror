package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.FakeContext;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.annotation.SuppressLint;
import android.view.InputEvent;
import android.view.MotionEvent;

import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;

@SuppressLint("PrivateApi,DiscouragedPrivateApi")
public final class InputManager {

    public static final int INJECT_INPUT_EVENT_MODE_ASYNC = 0;
    public static final int INJECT_INPUT_EVENT_MODE_WAIT_FOR_RESULT = 1;
    public static final int INJECT_INPUT_EVENT_MODE_WAIT_FOR_FINISH = 2;

    private static Method injectInputEventMethod;
    private static Method setDisplayIdMethod;
    private static Method setActionButtonMethod;

    private final android.hardware.input.InputManager manager;

    private long lastPermissionLogTime;

    static InputManager create() {
        android.hardware.input.InputManager manager =
                (android.hardware.input.InputManager) FakeContext.get().getSystemService(FakeContext.INPUT_SERVICE);
        return new InputManager(manager);
    }

    private InputManager(android.hardware.input.InputManager manager) {
        this.manager = manager;
    }

    private static Method resolveInjectInputEvent() throws NoSuchMethodException {
        if (injectInputEventMethod == null) {
            injectInputEventMethod = Reflect.lookupOrThrow(android.hardware.input.InputManager.class, "injectInputEvent", InputEvent.class,
                    int.class);
        }
        return injectInputEventMethod;
    }

    public boolean injectInputEvent(InputEvent event, int mode) {
        try {
            return (boolean) resolveInjectInputEvent().invoke(manager, event, mode);
        } catch (ReflectiveOperationException e) {
            if (e instanceof InvocationTargetException && e.getCause() instanceof SecurityException) {
                String message = e.getCause().getMessage();
                if (message != null && message.contains("INJECT_EVENTS permission")) {
                    long now = System.currentTimeMillis();
                    if (now - lastPermissionLogTime > 3000) {
                        Ln.e(message);
                        Ln.e("Make sure you have enabled \"USB debugging (Security Settings)\" and then rebooted your device.");
                        lastPermissionLogTime = now;
                    }
                    return false;
                }
            }
            Ln.e("Could not invoke method", e);
            return false;
        }
    }

    private static Method resolveSetDisplayId() throws NoSuchMethodException {
        if (setDisplayIdMethod == null) {
            setDisplayIdMethod = Reflect.lookupOrThrow(InputEvent.class, "setDisplayId", int.class);
        }
        return setDisplayIdMethod;
    }

    public static boolean setDisplayId(InputEvent event, int displayId) {
        try {
            resolveSetDisplayId().invoke(event, displayId);
            return true;
        } catch (ReflectiveOperationException e) {
            Ln.e("Cannot associate a display id to the input event", e);
            return false;
        }
    }

    private static Method resolveSetActionButton() throws NoSuchMethodException {
        if (setActionButtonMethod == null) {
            setActionButtonMethod = Reflect.lookupOrThrow(MotionEvent.class, "setActionButton", int.class);
        }
        return setActionButtonMethod;
    }

    public static boolean setActionButton(MotionEvent event, int actionButton) {
        try {
            resolveSetActionButton().invoke(event, actionButton);
            return true;
        } catch (ReflectiveOperationException e) {
            Ln.e("Cannot set action button on MotionEvent", e);
            return false;
        }
    }
}
