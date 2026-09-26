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
        int v = toUnsigned(value);
        return v == 0xffff ? 1f : v / 65536f;
    }

    public static float i16FixedPointToFloat(short value) {
        return value == 0x7fff ? 1f : value / 32768f;
    }

    public static int readInt32Le(byte[] data, int offset) {
        return (data[offset] & 0xff)
                | ((data[offset + 1] & 0xff) << 8)
                | ((data[offset + 2] & 0xff) << 16)
                | ((data[offset + 3] & 0xff) << 24);
    }

    public static long readInt64Le(byte[] data, int offset) {
        return (readInt32Le(data, offset) & 0xffffffffL)
                | ((long) readInt32Le(data, offset + 4) << 32);
    }

    public static int readUInt16Le(byte[] data, int offset) {
        return (data[offset] & 0xff) | ((data[offset + 1] & 0xff) << 8);
    }

    public static void writeInt16Le(byte[] data, int offset, int value) {
        data[offset] = (byte) value;
        data[offset + 1] = (byte) (value >> 8);
    }

    public static void writeInt32Le(byte[] data, int offset, long value) {
        data[offset] = (byte) value;
        data[offset + 1] = (byte) (value >> 8);
        data[offset + 2] = (byte) (value >> 16);
        data[offset + 3] = (byte) (value >> 24);
    }

    public static void writeInt64Le(byte[] data, int offset, long value) {
        writeInt32Le(data, offset, value);
        writeInt32Le(data, offset + 4, value >> 32);
    }
}
