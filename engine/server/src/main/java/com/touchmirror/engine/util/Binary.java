package com.touchmirror.engine.util;

public final class Binary {
    private Binary() {
    }

    public static int toUnsigned(short value) {
        return value & 0xffff;
    }

    public static int toUnsigned(byte value) {
        return value & 0xff;
    }

    public static float u16FixedPointToFloat(short value) {
        int unsignedShort = Binary.toUnsigned(value);
        return unsignedShort == 0xffff ? 1f : (unsignedShort / 0x1p16f);
    }

    public static float i16FixedPointToFloat(short value) {
        return value == 0x7fff ? 1f : (value / 0x1p15f);
    }
}
