package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.FakeContext;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.content.IContentProvider;
import android.content.Intent;
import android.os.Binder;
import android.os.Bundle;
import android.os.IBinder;
import android.os.IInterface;

import java.lang.reflect.Field;
import java.lang.reflect.Method;

@SuppressLint("PrivateApi,DiscouragedPrivateApi")
public final class ActivityManager {

    private static final int USER_CURRENT = -2;

    private final IInterface manager;

    private Method getContentProviderExternalMethod;
    private boolean getContentProviderExternalTakesCaller;
    private Method removeContentProviderExternalMethod;
    private Method startActivityAsUserMethod;
    private Method forceStopPackageMethod;
    private Method broadcastIntentMethod;

    static ActivityManager create() {
        try {
            Method getDefault = Class.forName("android.app.ActivityManagerNative").getDeclaredMethod("getDefault");
            return new ActivityManager((IInterface) getDefault.invoke(null));
        } catch (ReflectiveOperationException e) {
            throw new AssertionError(e);
        }
    }

    private ActivityManager(IInterface manager) {
        this.manager = manager;
    }

    private Method resolveGetContentProviderExternal() throws NoSuchMethodException {
        if (getContentProviderExternalMethod == null) {
            getContentProviderExternalMethod =
                    Reflect.lookup(manager.getClass(), "getContentProviderExternal", String.class, int.class, IBinder.class, String.class);
            if (getContentProviderExternalMethod == null) {
                getContentProviderExternalMethod =
                        Reflect.lookupOrThrow(manager.getClass(), "getContentProviderExternal", String.class, int.class, IBinder.class);
            } else {
                getContentProviderExternalTakesCaller = true;
            }
        }
        return getContentProviderExternalMethod;
    }

    private Method resolveRemoveContentProviderExternal() throws NoSuchMethodException {
        if (removeContentProviderExternalMethod == null) {
            removeContentProviderExternalMethod =
                    Reflect.lookupOrThrow(manager.getClass(), "removeContentProviderExternal", String.class, IBinder.class);
        }
        return removeContentProviderExternalMethod;
    }

    @TargetApi(AndroidVersions.API_29_ANDROID_10)
    public IContentProvider getContentProviderExternal(String name, IBinder token) {
        try {
            Method method = resolveGetContentProviderExternal();
            Object[] args = getContentProviderExternalTakesCaller
                    ? new Object[]{name, FakeContext.ROOT_UID, token, null}
                    : new Object[]{name, FakeContext.ROOT_UID, token};
            Object holder = method.invoke(manager, args);
            if (holder == null) {
                return null;
            }
            Field providerField = holder.getClass().getDeclaredField("provider");
            providerField.setAccessible(true);
            return (IContentProvider) providerField.get(holder);
        } catch (ReflectiveOperationException | ClassCastException e) {
            Ln.e("Could not invoke method", e);
            return null;
        }
    }

    void removeContentProviderExternal(String name, IBinder token) {
        try {
            resolveRemoveContentProviderExternal().invoke(manager, name, token);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }

    public ContentProvider createSettingsProvider() {
        IBinder token = new Binder();
        IContentProvider provider = getContentProviderExternal("settings", token);
        return provider == null ? null : new ContentProvider(this, provider, "settings", token);
    }

    private Method resolveStartActivityAsUser() throws NoSuchMethodException, ClassNotFoundException {
        if (startActivityAsUserMethod == null) {
            startActivityAsUserMethod = manager.getClass().getMethod("startActivityAsUser",
                    Class.forName("android.app.IApplicationThread"), String.class, Intent.class, String.class, IBinder.class, String.class,
                    int.class, int.class, Class.forName("android.app.ProfilerInfo"), Bundle.class, int.class);
        }
        return startActivityAsUserMethod;
    }

    public int startActivity(Intent intent) {
        return startActivity(intent, null);
    }

    public int startActivity(Intent intent, Bundle options) {
        try {
            Object result = resolveStartActivityAsUser().invoke(manager, null, FakeContext.PACKAGE_NAME, intent, null, null, null, 0, 0,
                    null, options, USER_CURRENT);
            return result == null ? 0 : (int) result;
        } catch (Throwable e) {
            Ln.e("Could not invoke method", e);
            return 0;
        }
    }

    public void forceStopPackage(String packageName) {
        try {
            if (forceStopPackageMethod == null) {
                forceStopPackageMethod = Reflect.lookupOrThrow(manager.getClass(), "forceStopPackage", String.class, int.class);
            }
            forceStopPackageMethod.invoke(manager, packageName, USER_CURRENT);
        } catch (Throwable e) {
            Ln.e("Could not invoke method", e);
        }
    }

    private Method resolveBroadcastIntent() throws NoSuchMethodException {
        if (broadcastIntentMethod == null) {
            try {
                broadcastIntentMethod = manager.getClass().getMethod("broadcastIntent",
                        Class.forName("android.app.IApplicationThread"), Intent.class, String.class,
                        Class.forName("android.content.IIntentReceiver"), int.class, String.class, Bundle.class, String[].class, int.class,
                        Bundle.class, boolean.class, boolean.class, int.class);
            } catch (ClassNotFoundException e) {
                throw new AssertionError(e);
            }
        }
        return broadcastIntentMethod;
    }

    public void sendBroadcast(Intent intent) {
        try {
            resolveBroadcastIntent().invoke(manager, null, intent, null, null, 0, null, null, null, -1, null, true, false, USER_CURRENT);
        } catch (Throwable e) {
            Ln.e("Could not invoke method", e);
        }
    }
}
