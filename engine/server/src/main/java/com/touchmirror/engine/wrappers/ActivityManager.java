package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.FakeContext;
import com.touchmirror.engine.util.Ln;

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

    private final IInterface manager;
    private Method getContentProviderExternalMethod;
    private boolean getContentProviderExternalMethodNewVersion = true;
    private Method removeContentProviderExternalMethod;
    private Method startActivityAsUserMethod;
    private Method forceStopPackageMethod;
    private Method broadcastIntentMethod;

    static ActivityManager create() {
        try {
            Class<?> cls = Class.forName("android.app.ActivityManagerNative");
            Method getDefaultMethod = cls.getDeclaredMethod("getDefault");
            IInterface am = (IInterface) getDefaultMethod.invoke(null);
            return new ActivityManager(am);
        } catch (ReflectiveOperationException e) {
            throw new AssertionError(e);
        }
    }

    private ActivityManager(IInterface manager) {
        this.manager = manager;
    }

    private Method getGetContentProviderExternalMethod() throws NoSuchMethodException {
        if (getContentProviderExternalMethod == null) {
            try {
                getContentProviderExternalMethod = manager.getClass()
                        .getMethod("getContentProviderExternal", String.class, int.class, IBinder.class, String.class);
            } catch (NoSuchMethodException e) {
                getContentProviderExternalMethod = manager.getClass().getMethod("getContentProviderExternal", String.class, int.class, IBinder.class);
                getContentProviderExternalMethodNewVersion = false;
            }
        }
        return getContentProviderExternalMethod;
    }

    private Method getRemoveContentProviderExternalMethod() throws NoSuchMethodException {
        if (removeContentProviderExternalMethod == null) {
            removeContentProviderExternalMethod = manager.getClass().getMethod("removeContentProviderExternal", String.class, IBinder.class);
        }
        return removeContentProviderExternalMethod;
    }

    @TargetApi(AndroidVersions.API_29_ANDROID_10)
    public IContentProvider getContentProviderExternal(String name, IBinder token) {
        try {
            Method method = getGetContentProviderExternalMethod();
            Object[] args;
            if (getContentProviderExternalMethodNewVersion) {
                args = new Object[]{name, FakeContext.ROOT_UID, token, null};
            } else {
                args = new Object[]{name, FakeContext.ROOT_UID, token};
            }
            Object providerHolder = method.invoke(manager, args);
            if (providerHolder == null) {
                return null;
            }
            Field providerField = providerHolder.getClass().getDeclaredField("provider");
            providerField.setAccessible(true);
            return (IContentProvider) providerField.get(providerHolder);
        } catch (ReflectiveOperationException | ClassCastException e) {
            Ln.e("Could not invoke method", e);
            return null;
        }
    }

    void removeContentProviderExternal(String name, IBinder token) {
        try {
            Method method = getRemoveContentProviderExternalMethod();
            method.invoke(manager, name, token);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }

    public ContentProvider createSettingsProvider() {
        IBinder token = new Binder();
        IContentProvider provider = getContentProviderExternal("settings", token);
        if (provider == null) {
            return null;
        }
        return new ContentProvider(this, provider, "settings", token);
    }

    private Method getStartActivityAsUserMethod() throws NoSuchMethodException, ClassNotFoundException {
        if (startActivityAsUserMethod == null) {
            Class<?> iApplicationThreadClass = Class.forName("android.app.IApplicationThread");
            Class<?> profilerInfo = Class.forName("android.app.ProfilerInfo");
            startActivityAsUserMethod = manager.getClass()
                    .getMethod("startActivityAsUser", iApplicationThreadClass, String.class, Intent.class, String.class, IBinder.class, String.class,
                            int.class, int.class, profilerInfo, Bundle.class, int.class);
        }
        return startActivityAsUserMethod;
    }

    public int startActivity(Intent intent) {
        return startActivity(intent, null);
    }

    @SuppressWarnings("ConstantConditions")
    public int startActivity(Intent intent, Bundle options) {
        try {
            Method method = getStartActivityAsUserMethod();
            return (int) method.invoke(
                     manager,
                     null,
                     FakeContext.PACKAGE_NAME,
                     intent,
                     null,
                     null,
                     null,
                     0,
                     0,
                     null,
                     options,
                      -2);
        } catch (Throwable e) {
            Ln.e("Could not invoke method", e);
            return 0;
        }
    }

    private Method getForceStopPackageMethod() throws NoSuchMethodException {
        if (forceStopPackageMethod == null) {
            forceStopPackageMethod = manager.getClass().getMethod("forceStopPackage", String.class, int.class);
        }
        return forceStopPackageMethod;
    }

    public void forceStopPackage(String packageName) {
        try {
            Method method = getForceStopPackageMethod();
            method.invoke(manager, packageName,   -2);
        } catch (Throwable e) {
            Ln.e("Could not invoke method", e);
        }
    }

    private Method getBroadcastIntentMethod() throws NoSuchMethodException {
        if (broadcastIntentMethod == null) {
            try {
                Class<?> iApplicationThreadClass = Class.forName("android.app.IApplicationThread");
                Class<?> iIntentReceiverClass = Class.forName("android.content.IIntentReceiver");
                broadcastIntentMethod = manager.getClass()
                        .getMethod("broadcastIntent", iApplicationThreadClass, Intent.class, String.class, iIntentReceiverClass, int.class,
                                String.class, Bundle.class, String[].class, int.class, Bundle.class, boolean.class, boolean.class, int.class);
            } catch (ClassNotFoundException e) {
                throw new AssertionError(e);
            }
        }
        return broadcastIntentMethod;
    }

    public void sendBroadcast(Intent intent) {
        try {
            Method method = getBroadcastIntentMethod();
            method.invoke(manager, null, intent, null, null, 0, null, null, null, -1, null, true, false,  -2);
        } catch (Throwable e) {
            Ln.e("Could not invoke method", e);
        }
    }
}
