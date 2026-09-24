package com.touchmirror.engine.util;

import com.touchmirror.engine.wrappers.ContentProvider;
import com.touchmirror.engine.wrappers.ServiceManager;

public final class Settings {

    public static final String TABLE_SYSTEM = ContentProvider.TABLE_SYSTEM;
    public static final String TABLE_SECURE = ContentProvider.TABLE_SECURE;
    public static final String TABLE_GLOBAL = ContentProvider.TABLE_GLOBAL;

    private Settings() {
    }

    public static String getValue(String table, String key) throws SettingsException {
        try (ContentProvider provider = ServiceManager.getActivityManager().createSettingsProvider()) {
            return provider.getValue(table, key);
        }
    }

    public static void putValue(String table, String key, String value) throws SettingsException {
        try (ContentProvider provider = ServiceManager.getActivityManager().createSettingsProvider()) {
            provider.putValue(table, key, value);
        }

    }

    public static String getAndPutValue(String table, String key, String value) throws SettingsException {
        try (ContentProvider provider = ServiceManager.getActivityManager().createSettingsProvider()) {
            String oldValue = provider.getValue(table, key);
            if (!value.equals(oldValue)) {
                provider.putValue(table, key, value);
            }
            return oldValue;
        }
    }
}
