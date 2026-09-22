package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.os.Build;
import android.os.IInterface;
import android.os.SystemClock;

import java.lang.reflect.Method;

public final class PowerManager {

    private static final int USER_ACTIVITY_EVENT_OTHER = 0;

    private final IInterface manager;

    private Method isScreenOnMethod;
    private Method userActivityMethod;

    static PowerManager create() {
        return new PowerManager(ServiceManager.getService("power", "android.os.IPowerManager"));
    }

    private PowerManager(IInterface manager) {
        this.manager = manager;
    }

    private Method resolveIsScreenOn() throws NoSuchMethodException {
        if (isScreenOnMethod == null) {
            isScreenOnMethod = Build.VERSION.SDK_INT >= AndroidVersions.API_34_ANDROID_14
                    ? Reflect.lookupOrThrow(manager.getClass(), "isDisplayInteractive", int.class)
                    : Reflect.lookupOrThrow(manager.getClass(), "isInteractive");
        }
        return isScreenOnMethod;
    }

    public boolean isScreenOn(int displayId) {
        try {
            Method method = resolveIsScreenOn();
            Object result = Build.VERSION.SDK_INT >= AndroidVersions.API_34_ANDROID_14
                    ? method.invoke(manager, displayId)
                    : method.invoke(manager);
            return result != null && (boolean) result;
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return false;
        }
    }

    private Method resolveUserActivity() throws NoSuchMethodException {
        if (userActivityMethod == null) {
            userActivityMethod = Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12
                    ? Reflect.lookupOrThrow(manager.getClass(), "userActivity", int.class, long.class, int.class, int.class)
                    : Reflect.lookupOrThrow(manager.getClass(), "userActivity", long.class, int.class, int.class);
        }
        return userActivityMethod;
    }

    public void userActivity(int displayId) {
        try {
            Method method = resolveUserActivity();
            long time = SystemClock.uptimeMillis();
            if (Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12) {
                method.invoke(manager, displayId, time, USER_ACTIVITY_EVENT_OTHER, 0);
            } else {
                method.invoke(manager, time, USER_ACTIVITY_EVENT_OTHER, 0);
            }
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }
}
