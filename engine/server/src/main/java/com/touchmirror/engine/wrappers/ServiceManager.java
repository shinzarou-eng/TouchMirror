package com.touchmirror.engine.wrappers;

import android.annotation.SuppressLint;
import android.os.IBinder;
import android.os.IInterface;

import java.lang.reflect.Method;

@SuppressLint("PrivateApi,DiscouragedPrivateApi")
public final class ServiceManager {

    private static final Method GET_SERVICE_METHOD = resolveGetServiceMethod();

    private static WindowManager windowManager;
    private static DisplayManager displayManager;
    private static InputManager inputManager;
    private static PowerManager powerManager;
    private static StatusBarManager statusBarManager;
    private static ClipboardManager clipboardManager;
    private static ActivityManager activityManager;

    private ServiceManager() {
    }

    private static Method resolveGetServiceMethod() {
        try {
            return Class.forName("android.os.ServiceManager").getDeclaredMethod("getService", String.class);
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    static IInterface getService(String name, String type) {
        try {
            IBinder binder = (IBinder) GET_SERVICE_METHOD.invoke(null, name);
            Method asInterface = Class.forName(type + "$Stub").getMethod("asInterface", IBinder.class);
            return (IInterface) asInterface.invoke(null, binder);
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    public static WindowManager getWindowManager() {
        if (windowManager == null) {
            windowManager = WindowManager.create();
        }
        return windowManager;
    }

    public static synchronized DisplayManager getDisplayManager() {
        if (displayManager == null) {
            displayManager = DisplayManager.create();
        }
        return displayManager;
    }

    public static InputManager getInputManager() {
        if (inputManager == null) {
            inputManager = InputManager.create();
        }
        return inputManager;
    }

    public static PowerManager getPowerManager() {
        if (powerManager == null) {
            powerManager = PowerManager.create();
        }
        return powerManager;
    }

    public static StatusBarManager getStatusBarManager() {
        if (statusBarManager == null) {
            statusBarManager = StatusBarManager.create();
        }
        return statusBarManager;
    }

    public static ClipboardManager getClipboardManager() {
        if (clipboardManager == null) {
            clipboardManager = ClipboardManager.create();
        }
        return clipboardManager;
    }

    public static ActivityManager getActivityManager() {
        if (activityManager == null) {
            activityManager = ActivityManager.create();
        }
        return activityManager;
    }
}
