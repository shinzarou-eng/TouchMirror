package com.touchmirror.engine.util;

public final class StringUtils {
    private StringUtils() {
    }

    public static int getUtf8TruncationIndex(byte[] utf8, int maxLength) {
        if (utf8.length <= maxLength) {
            return utf8.length;
        }

        int len = maxLength;
        while ((utf8[len] & 0x80) != 0 && (utf8[len] & 0xc0) != 0xc0) {
            len--;
        }
        return len;
    }
}
