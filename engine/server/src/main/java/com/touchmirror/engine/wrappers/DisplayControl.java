package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.os.IBinder;
import android.system.Os;

import java.lang.reflect.Method;

@SuppressLint({"PrivateApi", "SoonBlockedPrivateApi", "BlockedPrivateApi"})
@TargetApi(AndroidVersions.API_34_ANDROID_14)
public final class DisplayControl {

    private static final Class<?> CLASS = loadDisplayControlClass();

    private static Method getPhysicalDisplayTokenMethod;
    private static Method getPhysicalDisplayIdsMethod;

    private DisplayControl() {
    }

    private static Class<?> loadDisplayControlClass() {
        try {
            Class<?> factoryClass = Class.forName("com.android.internal.os.ClassLoaderFactory");
            Method createClassLoader = factoryClass.getDeclaredMethod("createClassLoader", String.class, String.class, String.class,
                    ClassLoader.class, int.class, boolean.class, String.class);

            ClassLoader classLoader = (ClassLoader) createClassLoader.invoke(null, Os.getenv("SYSTEMSERVERCLASSPATH"), null, null,
                    ClassLoader.getSystemClassLoader(), 0, true, null);

            Class<?> displayControlClass = classLoader.loadClass("com.android.server.display.DisplayControl");

            Method loadLibrary = Runtime.class.getDeclaredMethod("loadLibrary0", Class.class, String.class);
            loadLibrary.setAccessible(true);
            loadLibrary.invoke(Runtime.getRuntime(), displayControlClass, "android_servers");

            return displayControlClass;
        } catch (Throwable e) {
            Ln.e("Could not initialize DisplayControl", e);
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

    public static long[] getPhysicalDisplayIds() {
        try {
            return (long[]) resolveGetPhysicalDisplayIds().invoke(null);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return null;
        }
    }
}
