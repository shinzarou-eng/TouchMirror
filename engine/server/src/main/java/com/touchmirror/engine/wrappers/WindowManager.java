package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.annotation.TargetApi;
import android.os.Build;
import android.os.IInterface;
import android.view.IDisplayWindowListener;

import java.lang.reflect.Method;

public final class WindowManager {

    public static final int DISPLAY_IME_POLICY_LOCAL = 0;
    public static final int DISPLAY_IME_POLICY_FALLBACK_DISPLAY = 1;
    public static final int DISPLAY_IME_POLICY_HIDE = 2;

    private final IInterface manager;

    private Method getRotationMethod;
    private Method freezeDisplayRotationMethod;
    private int freezeDisplayRotationVariant;
    private Method isDisplayRotationFrozenMethod;
    private int isDisplayRotationFrozenVariant;
    private Method thawDisplayRotationMethod;
    private int thawDisplayRotationVariant;
    private Method getDisplayImePolicyMethod;
    private Method setDisplayImePolicyMethod;

    static WindowManager create() {
        return new WindowManager(ServiceManager.getService("window", "android.view.IWindowManager"));
    }

    private WindowManager(IInterface manager) {
        this.manager = manager;
    }

    private Method resolveGetRotation() throws NoSuchMethodException {
        if (getRotationMethod == null) {
            getRotationMethod = Reflect.lookup(manager.getClass(), "getDefaultDisplayRotation");
            if (getRotationMethod == null) {
                getRotationMethod = Reflect.lookupOrThrow(manager.getClass(), "getRotation");
            }
        }
        return getRotationMethod;
    }

    private Method resolveFreezeDisplayRotation() throws NoSuchMethodException {
        if (freezeDisplayRotationMethod == null) {
            Class<?> cls = manager.getClass();
            freezeDisplayRotationMethod = Reflect.lookup(cls, "freezeDisplayRotation", int.class, int.class, String.class);
            if (freezeDisplayRotationMethod == null) {
                freezeDisplayRotationMethod = Reflect.lookup(cls, "freezeDisplayRotation", int.class, int.class);
                freezeDisplayRotationVariant = 1;
            }
            if (freezeDisplayRotationMethod == null) {
                freezeDisplayRotationMethod = Reflect.lookupOrThrow(cls, "freezeRotation", int.class);
                freezeDisplayRotationVariant = 2;
            }
        }
        return freezeDisplayRotationMethod;
    }

    private Method resolveIsDisplayRotationFrozen() throws NoSuchMethodException {
        if (isDisplayRotationFrozenMethod == null) {
            Class<?> cls = manager.getClass();
            isDisplayRotationFrozenMethod = Reflect.lookup(cls, "isDisplayRotationFrozen", int.class);
            if (isDisplayRotationFrozenMethod == null) {
                isDisplayRotationFrozenMethod = Reflect.lookupOrThrow(cls, "isRotationFrozen");
                isDisplayRotationFrozenVariant = 1;
            }
        }
        return isDisplayRotationFrozenMethod;
    }

    private Method resolveThawDisplayRotation() throws NoSuchMethodException {
        if (thawDisplayRotationMethod == null) {
            Class<?> cls = manager.getClass();
            thawDisplayRotationMethod = Reflect.lookup(cls, "thawDisplayRotation", int.class, String.class);
            if (thawDisplayRotationMethod == null) {
                thawDisplayRotationMethod = Reflect.lookup(cls, "thawDisplayRotation", int.class);
                thawDisplayRotationVariant = 1;
            }
            if (thawDisplayRotationMethod == null) {
                thawDisplayRotationMethod = Reflect.lookupOrThrow(cls, "thawRotation");
                thawDisplayRotationVariant = 2;
            }
        }
        return thawDisplayRotationMethod;
    }

    public int getRotation() {
        try {
            Object result = resolveGetRotation().invoke(manager);
            return result == null ? 0 : (int) result;
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return 0;
        }
    }

    public void freezeRotation(int displayId, int rotation) {
        try {
            Method method = resolveFreezeDisplayRotation();
            switch (freezeDisplayRotationVariant) {
                case 0:
                    method.invoke(manager, displayId, rotation, "touchmirror#freezeRotation");
                    break;
                case 1:
                    method.invoke(manager, displayId, rotation);
                    break;
                default:
                    if (displayId != 0) {
                        Ln.e("Secondary display rotation not supported on this device");
                        return;
                    }
                    method.invoke(manager, rotation);
                    break;
            }
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }

    public boolean isRotationFrozen(int displayId) {
        try {
            Method method = resolveIsDisplayRotationFrozen();
            Object result;
            if (isDisplayRotationFrozenVariant == 0) {
                result = method.invoke(manager, displayId);
            } else {
                if (displayId != 0) {
                    Ln.e("Secondary display rotation not supported on this device");
                    return false;
                }
                result = method.invoke(manager);
            }
            return result != null && (boolean) result;
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return false;
        }
    }

    public void thawRotation(int displayId) {
        try {
            Method method = resolveThawDisplayRotation();
            switch (thawDisplayRotationVariant) {
                case 0:
                    method.invoke(manager, displayId, "touchmirror#thawRotation");
                    break;
                case 1:
                    method.invoke(manager, displayId);
                    break;
                default:
                    if (displayId != 0) {
                        Ln.e("Secondary display rotation not supported on this device");
                        return;
                    }
                    method.invoke(manager);
                    break;
            }
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }

    @TargetApi(AndroidVersions.API_30_ANDROID_11)
    public int[] registerDisplayWindowListener(IDisplayWindowListener listener) {
        try {
            return (int[]) manager.getClass().getMethod("registerDisplayWindowListener", IDisplayWindowListener.class)
                    .invoke(manager, listener);
        } catch (Exception e) {
            Ln.e("Could not register display window listener", e);
            return null;
        }
    }

    @TargetApi(AndroidVersions.API_30_ANDROID_11)
    public void unregisterDisplayWindowListener(IDisplayWindowListener listener) {
        try {
            manager.getClass().getMethod("unregisterDisplayWindowListener", IDisplayWindowListener.class).invoke(manager, listener);
        } catch (Exception e) {
            Ln.e("Could not unregister display window listener", e);
        }
    }

    @TargetApi(AndroidVersions.API_29_ANDROID_10)
    private Method resolveGetDisplayImePolicy() throws NoSuchMethodException {
        if (getDisplayImePolicyMethod == null) {
            getDisplayImePolicyMethod = Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12
                    ? Reflect.lookupOrThrow(manager.getClass(), "getDisplayImePolicy", int.class)
                    : Reflect.lookupOrThrow(manager.getClass(), "shouldShowIme", int.class);
        }
        return getDisplayImePolicyMethod;
    }

    @TargetApi(AndroidVersions.API_29_ANDROID_10)
    public int getDisplayImePolicy(int displayId) {
        try {
            Method method = resolveGetDisplayImePolicy();
            if (Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12) {
                Object result = method.invoke(manager, displayId);
                return result == null ? -1 : (int) result;
            }
            Object shouldShowIme = method.invoke(manager, displayId);
            return shouldShowIme != null && (boolean) shouldShowIme ? DISPLAY_IME_POLICY_LOCAL : DISPLAY_IME_POLICY_FALLBACK_DISPLAY;
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return -1;
        }
    }

    @TargetApi(AndroidVersions.API_29_ANDROID_10)
    private Method resolveSetDisplayImePolicy() throws NoSuchMethodException {
        if (setDisplayImePolicyMethod == null) {
            setDisplayImePolicyMethod = Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12
                    ? Reflect.lookupOrThrow(manager.getClass(), "setDisplayImePolicy", int.class, int.class)
                    : Reflect.lookupOrThrow(manager.getClass(), "setShouldShowIme", int.class, boolean.class);
        }
        return setDisplayImePolicyMethod;
    }

    @TargetApi(AndroidVersions.API_29_ANDROID_10)
    public void setDisplayImePolicy(int displayId, int displayImePolicy) {
        try {
            Method method = resolveSetDisplayImePolicy();
            if (Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12) {
                method.invoke(manager, displayId, displayImePolicy);
            } else if (displayImePolicy != DISPLAY_IME_POLICY_HIDE) {
                method.invoke(manager, displayId, displayImePolicy == DISPLAY_IME_POLICY_LOCAL);
            } else {
                Ln.w("DISPLAY_IME_POLICY_HIDE is not supported before Android 12");
            }
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }
}
