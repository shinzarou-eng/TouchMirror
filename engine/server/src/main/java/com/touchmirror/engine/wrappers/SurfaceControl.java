package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.annotation.SuppressLint;
import android.graphics.Rect;
import android.os.Build;
import android.os.IBinder;
import android.view.Surface;

import java.lang.reflect.Method;

@SuppressLint("PrivateApi")
public final class SurfaceControl {

    public static final int POWER_MODE_OFF = 0;
    public static final int POWER_MODE_NORMAL = 2;

    private static final Class<?> CLASS = loadClass();

    private static Method getBuiltInDisplayMethod;
    private static Method setDisplayPowerModeMethod;
    private static Method getPhysicalDisplayTokenMethod;
    private static Method getPhysicalDisplayIdsMethod;

    private SurfaceControl() {
    }

    private static Class<?> loadClass() {
        try {
            return Class.forName("android.view.SurfaceControl");
        } catch (ClassNotFoundException e) {
            throw new AssertionError(e);
        }
    }

    private static Object callStatic(String name, Class<?>[] paramTypes, Object... args) {
        try {
            return CLASS.getMethod(name, paramTypes).invoke(null, args);
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    public static void openTransaction() {
        callStatic("openTransaction", new Class<?>[0]);
    }

    public static void closeTransaction() {
        callStatic("closeTransaction", new Class<?>[0]);
    }

    public static void setDisplayProjection(IBinder displayToken, int orientation, Rect layerStackRect, Rect displayRect) {
        callStatic("setDisplayProjection", new Class<?>[]{IBinder.class, int.class, Rect.class, Rect.class}, displayToken, orientation,
                layerStackRect, displayRect);
    }

    public static void setDisplayLayerStack(IBinder displayToken, int layerStack) {
        callStatic("setDisplayLayerStack", new Class<?>[]{IBinder.class, int.class}, displayToken, layerStack);
    }

    public static void setDisplaySurface(IBinder displayToken, Surface surface) {
        callStatic("setDisplaySurface", new Class<?>[]{IBinder.class, Surface.class}, displayToken, surface);
    }

    public static IBinder createDisplay(String name, boolean secure) throws Exception {
        return (IBinder) CLASS.getMethod("createDisplay", String.class, boolean.class).invoke(null, name, secure);
    }

    public static void destroyDisplay(IBinder displayToken) {
        callStatic("destroyDisplay", new Class<?>[]{IBinder.class}, displayToken);
    }

    private static Method resolveGetBuiltInDisplay() throws NoSuchMethodException {
        if (getBuiltInDisplayMethod == null) {
            getBuiltInDisplayMethod = Build.VERSION.SDK_INT < AndroidVersions.API_29_ANDROID_10
                    ? Reflect.lookupOrThrow(CLASS, "getBuiltInDisplay", int.class)
                    : Reflect.lookupOrThrow(CLASS, "getInternalDisplayToken");
        }
        return getBuiltInDisplayMethod;
    }

    public static boolean hasGetBuildInDisplayMethod() {
        try {
            resolveGetBuiltInDisplay();
            return true;
        } catch (NoSuchMethodException e) {
            return false;
        }
    }

    public static IBinder getBuiltInDisplay() {
        try {
            Method method = resolveGetBuiltInDisplay();
            Object result = Build.VERSION.SDK_INT < AndroidVersions.API_29_ANDROID_10 ? method.invoke(null, 0) : method.invoke(null);
            return (IBinder) result;
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return null;
        }
    }

    private static Method resolveGetPhysicalDisplayToken() throws NoSuchMethodException {
        if (getPhysicalDisplayTokenMethod == null) {
            getPhysicalDisplayTokenMethod = Reflect.lookupOrThrow(CLASS, "getPhysicalDisplayToken", long.class);
        }
        return getPhysicalDisplayTokenMethod;
    }

    public static IBinder getPhysicalDisplayToken(long physicalDisplayId) {
        try {
            return (IBinder) resolveGetPhysicalDisplayToken().invoke(null, physicalDisplayId);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return null;
        }
    }

    private static Method resolveGetPhysicalDisplayIds() throws NoSuchMethodException {
        if (getPhysicalDisplayIdsMethod == null) {
            getPhysicalDisplayIdsMethod = Reflect.lookupOrThrow(CLASS, "getPhysicalDisplayIds");
        }
        return getPhysicalDisplayIdsMethod;
    }

    public static boolean hasGetPhysicalDisplayIdsMethod() {
        try {
            resolveGetPhysicalDisplayIds();
            return true;
        } catch (NoSuchMethodException e) {
            return false;
        }
    }

    public static long[] getPhysicalDisplayIds() {
        try {
            return (long[]) resolveGetPhysicalDisplayIds().invoke(null);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return null;
        }
    }

    public static boolean setDisplayPowerMode(IBinder displayToken, int mode) {
        try {
            if (setDisplayPowerModeMethod == null) {
                setDisplayPowerModeMethod = Reflect.lookupOrThrow(CLASS, "setDisplayPowerMode", IBinder.class, int.class);
            }
            setDisplayPowerModeMethod.invoke(null, displayToken, mode);
            return true;
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return false;
        }
    }
}
