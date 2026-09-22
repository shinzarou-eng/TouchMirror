package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.FakeContext;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.SettingsException;

import android.annotation.SuppressLint;
import android.content.AttributionSource;
import android.os.Build;
import android.os.Bundle;
import android.os.IBinder;

import java.io.Closeable;
import java.lang.reflect.Method;

public final class ContentProvider implements Closeable {

    public static final String TABLE_SYSTEM = "system";
    public static final String TABLE_SECURE = "secure";
    public static final String TABLE_GLOBAL = "global";

    private static final String USER_KEY = "_user";
    private static final String VALUE_KEY = "value";

    private final ActivityManager manager;
    private final Object provider;
    private final String name;
    private final IBinder token;

    private Method callMethod;
    private int callVariant = -1;

    ContentProvider(ActivityManager manager, Object provider, String name, IBinder token) {
        this.manager = manager;
        this.provider = provider;
        this.name = name;
        this.token = token;
    }

    @SuppressLint("PrivateApi")
    private Method resolveCall() throws NoSuchMethodException {
        if (callMethod == null) {
            Class<?> cls = provider.getClass();
            if (Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12) {
                callMethod = cls.getMethod("call", AttributionSource.class, String.class, String.class, String.class, Bundle.class);
                callVariant = 0;
            } else {
                try {
                    callMethod = cls.getMethod("call", String.class, String.class, String.class, String.class, String.class, Bundle.class);
                    callVariant = 1;
                } catch (NoSuchMethodException e1) {
                    try {
                        callMethod = cls.getMethod("call", String.class, String.class, String.class, String.class, Bundle.class);
                        callVariant = 2;
                    } catch (NoSuchMethodException e2) {
                        callMethod = cls.getMethod("call", String.class, String.class, String.class, Bundle.class);
                        callVariant = 3;
                    }
                }
            }
        }
        return callMethod;
    }

    private Bundle call(String method, String key, Bundle extras) throws ReflectiveOperationException {
        try {
            Method resolved = resolveCall();
            Object[] args;
            switch (callVariant) {
                case 0:
                    args = new Object[]{FakeContext.get().getAttributionSource(), "settings", method, key, extras};
                    break;
                case 1:
                    args = new Object[]{FakeContext.PACKAGE_NAME, null, "settings", method, key, extras};
                    break;
                case 2:
                    args = new Object[]{FakeContext.PACKAGE_NAME, "settings", method, key, extras};
                    break;
                default:
                    args = new Object[]{FakeContext.PACKAGE_NAME, method, key, extras};
                    break;
            }
            return (Bundle) resolved.invoke(provider, args);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            throw e;
        }
    }

    @Override
    public void close() {
        manager.removeContentProviderExternal(name, token);
    }

    private static String getMethod(String table) {
        switch (table) {
            case TABLE_SECURE:
                return "GET_secure";
            case TABLE_SYSTEM:
                return "GET_system";
            case TABLE_GLOBAL:
                return "GET_global";
            default:
                throw new IllegalArgumentException("Invalid table: " + table);
        }
    }

    private static String putMethod(String table) {
        switch (table) {
            case TABLE_SECURE:
                return "PUT_secure";
            case TABLE_SYSTEM:
                return "PUT_system";
            case TABLE_GLOBAL:
                return "PUT_global";
            default:
                throw new IllegalArgumentException("Invalid table: " + table);
        }
    }

    public String getValue(String table, String key) throws SettingsException {
        Bundle extras = new Bundle();
        extras.putInt(USER_KEY, FakeContext.ROOT_UID);
        try {
            Bundle bundle = call(getMethod(table), key, extras);
            return bundle == null ? null : bundle.getString(VALUE_KEY);
        } catch (Exception e) {
            throw new SettingsException(table, "get", key, null, e);
        }
    }

    public void putValue(String table, String key, String value) throws SettingsException {
        Bundle extras = new Bundle();
        extras.putInt(USER_KEY, FakeContext.ROOT_UID);
        extras.putString(VALUE_KEY, value);
        try {
            call(putMethod(table), key, extras);
        } catch (Exception e) {
            throw new SettingsException(table, "put", key, value, e);
        }
    }
}
