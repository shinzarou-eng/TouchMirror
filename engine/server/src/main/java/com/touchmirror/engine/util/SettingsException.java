package com.touchmirror.engine.util;

public class SettingsException extends Exception {

    public SettingsException(String method, String table, String key, String value, Throwable cause) {
        super("Could not access settings: " + method + " " + table + " " + key + (value != null ? " " + value : ""), cause);
    }
}
