package com.touchmirror.engine.util;

import android.util.Log;

import java.io.FileDescriptor;
import java.io.FileOutputStream;
import java.io.OutputStream;
import java.io.PrintStream;

public final class Ln {

    private static final String TAG = "touchmirror";
    private static final String PREFIX = "[server] ";

    private static final PrintStream OUT = new PrintStream(new FileOutputStream(FileDescriptor.out));
    private static final PrintStream ERR = new PrintStream(new FileOutputStream(FileDescriptor.err));

    public enum Level {
        VERBOSE, DEBUG, INFO, WARN, ERROR
    }

    private static volatile Level threshold = Level.INFO;

    private Ln() {
    }

    public static void initLogLevel(Level level) {
        threshold = level;
    }

    public static void disableSystemStreams() {
        PrintStream sink = new PrintStream(new OutputStream() {
            @Override
            public void write(int b) {
            }

            @Override
            public void write(byte[] b) {
            }

            @Override
            public void write(byte[] b, int off, int len) {
            }
        });
        System.setOut(sink);
        System.setErr(sink);
    }

    public static boolean isEnabled(Level level) {
        return level.ordinal() >= threshold.ordinal();
    }

    private static void out(Level level, String tag, String message) {
        Log.println(level.ordinal() + Log.VERBOSE, TAG, message);
        OUT.print(PREFIX + tag + ": " + message + '\n');
    }

    public static void v(String message) {
        if (isEnabled(Level.VERBOSE)) {
            out(Level.VERBOSE, "VERBOSE", message);
        }
    }

    public static void d(String message) {
        if (isEnabled(Level.DEBUG)) {
            out(Level.DEBUG, "DEBUG", message);
        }
    }

    public static void i(String message) {
        if (isEnabled(Level.INFO)) {
            out(Level.INFO, "INFO", message);
        }
    }

    public static void w(String message, Throwable throwable) {
        if (isEnabled(Level.WARN)) {
            err(Level.WARN, "WARN", message, throwable);
        }
    }

    public static void w(String message) {
        w(message, null);
    }

    public static void e(String message, Throwable throwable) {
        if (isEnabled(Level.ERROR)) {
            err(Level.ERROR, "ERROR", message, throwable);
        }
    }

    public static void e(String message) {
        e(message, null);
    }

    private static void err(Level level, String tag, String message, Throwable throwable) {
        if (level == Level.WARN) {
            Log.w(TAG, message, throwable);
        } else {
            Log.e(TAG, message, throwable);
        }
        synchronized (ERR) {
            ERR.print(PREFIX + tag + ": " + message + '\n');
            if (throwable != null) {
                throwable.printStackTrace(ERR);
            }
        }
    }
}
