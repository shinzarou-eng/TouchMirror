package com.touchmirror.engine.util;

import com.touchmirror.engine.AndroidVersions;

import android.os.Build;
import android.system.ErrnoException;
import android.system.Os;
import android.system.OsConstants;

import java.io.FileDescriptor;
import java.io.IOException;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.util.Scanner;

public final class IO {
    private IO() {
    }

    private static int writeRetryOnInterrupt(FileDescriptor fd, ByteBuffer buffer) throws IOException {
        while (true) {
            try {
                return Os.write(fd, buffer);
            } catch (ErrnoException e) {
                if (e.errno != OsConstants.EINTR) {
                    throw new IOException(e);
                }
            }
        }
    }

    public static void writeFully(FileDescriptor fd, ByteBuffer buffer) throws IOException {
        if (Build.VERSION.SDK_INT >= AndroidVersions.API_23_ANDROID_6_0) {
            while (buffer.hasRemaining()) {
                writeRetryOnInterrupt(fd, buffer);
            }
            return;
        }

        int position = buffer.position();
        int remaining = buffer.remaining();
        while (remaining > 0) {
            int written = writeRetryOnInterrupt(fd, buffer);
            remaining -= written;
            position += written;
            buffer.position(position);
        }
    }

    public static void writeFully(FileDescriptor fd, byte[] buffer, int offset, int len) throws IOException {
        writeFully(fd, ByteBuffer.wrap(buffer, offset, len));
    }

    public static String readAll(InputStream stream) {
        StringBuilder builder = new StringBuilder();
        Scanner scanner = new Scanner(stream);
        while (scanner.hasNextLine()) {
            builder.append(scanner.nextLine()).append('\n');
        }
        return builder.toString();
    }

    public static boolean isBrokenPipe(IOException e) {
        Throwable cause = e.getCause();
        return cause instanceof ErrnoException && ((ErrnoException) cause).errno == OsConstants.EPIPE;
    }

    public static boolean isBrokenPipe(Exception e) {
        return e instanceof IOException && isBrokenPipe((IOException) e);
    }
}
