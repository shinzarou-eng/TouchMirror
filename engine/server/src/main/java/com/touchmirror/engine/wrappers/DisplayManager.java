package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.FakeContext;
import com.touchmirror.engine.display.DisplayInfo;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.util.Command;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.content.Context;
import android.hardware.display.VirtualDisplay;
import android.os.Handler;
import android.view.Display;
import android.view.Surface;

import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

@SuppressLint("PrivateApi,DiscouragedPrivateApi")
public final class DisplayManager {

    public static final long EVENT_FLAG_DISPLAY_CHANGED = 1L << 2;

    public interface DisplayListener {
        void onDisplayChanged(int displayId);
    }

    public static final class DisplayListenerHandle {
        private final Object proxy;

        private DisplayListenerHandle(Object proxy) {
            this.proxy = proxy;
        }
    }

    private final Object manager;

    private Method getDisplayInfoMethod;
    private Method createVirtualDisplayMethod;
    private Method requestDisplayPowerMethod;

    static DisplayManager create() {
        try {
            Object global = Class.forName("android.hardware.display.DisplayManagerGlobal").getDeclaredMethod("getInstance").invoke(null);
            return new DisplayManager(global);
        } catch (ReflectiveOperationException e) {
            throw new AssertionError(e);
        }
    }

    private DisplayManager(Object manager) {
        this.manager = manager;
    }

    public static DisplayInfo parseDisplayInfo(String dumpsysDisplayOutput, int displayId) {
        Pattern pattern = Pattern.compile(
                "^    mOverrideDisplayInfo=DisplayInfo\\{\".*?, displayId " + displayId + ".*?(, FLAG_.*)?, real ([0-9]+) x ([0-9]+).*?, "
                        + "rotation ([0-9]+).*?, density ([0-9]+).*?, layerStack ([0-9]+)",
                Pattern.MULTILINE);
        Matcher matcher = pattern.matcher(dumpsysDisplayOutput);
        if (!matcher.find()) {
            return null;
        }

        int flags = parseDisplayFlags(matcher.group(1));
        int width = Integer.parseInt(matcher.group(2));
        int height = Integer.parseInt(matcher.group(3));
        int rotation = Integer.parseInt(matcher.group(4));
        int density = Integer.parseInt(matcher.group(5));
        int layerStack = Integer.parseInt(matcher.group(6));

        return new DisplayInfo(displayId, new Size(width, height), rotation, layerStack, flags, density, null);
    }

    private static DisplayInfo getDisplayInfoFromDumpsys(int displayId) {
        try {
            return parseDisplayInfo(Command.execReadOutput("dumpsys", "display"), displayId);
        } catch (Exception e) {
            Ln.e("Could not get display info from \"dumpsys display\" output", e);
            return null;
        }
    }

    private static int parseDisplayFlags(String text) {
        if (text == null) {
            return 0;
        }

        int flags = 0;
        Matcher matcher = Pattern.compile("FLAG_[A-Z_]+").matcher(text);
        while (matcher.find()) {
            try {
                Field field = Display.class.getDeclaredField(matcher.group());
                flags |= field.getInt(null);
            } catch (ReflectiveOperationException e) {
            }
        }
        return flags;
    }

    public DisplayInfo getDisplayInfo(int displayId) {
        try {
            if (getDisplayInfoMethod == null) {
                getDisplayInfoMethod = Reflect.lookupOrThrow(manager.getClass(), "getDisplayInfo", int.class);
            }
            Object displayInfo = getDisplayInfoMethod.invoke(manager, displayId);
            if (displayInfo == null) {
                return getDisplayInfoFromDumpsys(displayId);
            }

            Class<?> cls = displayInfo.getClass();
            int width = cls.getDeclaredField("logicalWidth").getInt(displayInfo);
            int height = cls.getDeclaredField("logicalHeight").getInt(displayInfo);
            int rotation = cls.getDeclaredField("rotation").getInt(displayInfo);
            int layerStack = cls.getDeclaredField("layerStack").getInt(displayInfo);
            int flags = cls.getDeclaredField("flags").getInt(displayInfo);
            int dpi = cls.getDeclaredField("logicalDensityDpi").getInt(displayInfo);
            String uniqueId;
            try {
                uniqueId = (String) cls.getDeclaredField("uniqueId").get(displayInfo);
            } catch (NoSuchFieldException e) {
                uniqueId = null;
            }
            return new DisplayInfo(displayId, new Size(width, height), rotation, layerStack, flags, dpi, uniqueId);
        } catch (ReflectiveOperationException e) {
            throw new AssertionError(e);
        }
    }

    public int[] getDisplayIds() {
        try {
            return (int[]) manager.getClass().getMethod("getDisplayIds").invoke(manager);
        } catch (ReflectiveOperationException e) {
            throw new AssertionError(e);
        }
    }

    public VirtualDisplay createVirtualDisplay(String name, int width, int height, int displayIdToMirror, Surface surface) throws Exception {
        if (createVirtualDisplayMethod == null) {
            createVirtualDisplayMethod = Reflect.lookupOrThrow(android.hardware.display.DisplayManager.class, "createVirtualDisplay",
                    String.class, int.class, int.class, int.class, Surface.class);
        }
        return (VirtualDisplay) createVirtualDisplayMethod.invoke(null, name, width, height, displayIdToMirror, surface);
    }

    public VirtualDisplay createNewVirtualDisplay(String name, int width, int height, int dpi, Surface surface, int flags) throws Exception {
        Constructor<android.hardware.display.DisplayManager> ctor =
                android.hardware.display.DisplayManager.class.getDeclaredConstructor(Context.class);
        ctor.setAccessible(true);
        return ctor.newInstance(FakeContext.get()).createVirtualDisplay(name, width, height, dpi, surface, flags);
    }

    @TargetApi(AndroidVersions.API_35_ANDROID_15)
    public boolean requestDisplayPower(int displayId, boolean on) {
        try {
            if (requestDisplayPowerMethod == null) {
                requestDisplayPowerMethod = Reflect.lookupOrThrow(manager.getClass(), "requestDisplayPower", int.class, boolean.class);
            }
            Object result = requestDisplayPowerMethod.invoke(manager, displayId, on);
            return result != null && (boolean) result;
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return false;
        }
    }

    public DisplayListenerHandle registerDisplayListener(DisplayListener listener, Handler handler) {
        try {
            Class<?> listenerClass = Class.forName("android.hardware.display.DisplayManager$DisplayListener");
            Object proxy = Proxy.newProxyInstance(ClassLoader.getSystemClassLoader(), new Class<?>[]{listenerClass}, (p, method, args) -> {
                if ("onDisplayChanged".equals(method.getName())) {
                    listener.onDisplayChanged((int) args[0]);
                }
                if ("toString".equals(method.getName())) {
                    return "DisplayListener";
                }
                return null;
            });

            Class<?> cls = manager.getClass();
            Method register = Reflect.lookup(cls, "registerDisplayListener", listenerClass, Handler.class, long.class, String.class);
            if (register != null) {
                register.invoke(manager, proxy, handler, EVENT_FLAG_DISPLAY_CHANGED, FakeContext.PACKAGE_NAME);
            } else if ((register = Reflect.lookup(cls, "registerDisplayListener", listenerClass, Handler.class, long.class)) != null) {
                register.invoke(manager, proxy, handler, EVENT_FLAG_DISPLAY_CHANGED);
            } else {
                Reflect.lookupOrThrow(cls, "registerDisplayListener", listenerClass, Handler.class).invoke(manager, proxy, handler);
            }

            return new DisplayListenerHandle(proxy);
        } catch (Exception e) {
            Ln.e("Could not register display listener", e);
            return null;
        }
    }

    public void unregisterDisplayListener(DisplayListenerHandle handle) {
        try {
            Class<?> listenerClass = Class.forName("android.hardware.display.DisplayManager$DisplayListener");
            manager.getClass().getMethod("unregisterDisplayListener", listenerClass).invoke(manager, handle.proxy);
        } catch (Exception e) {
            Ln.e("Could not unregister display listener", e);
        }
    }
}
