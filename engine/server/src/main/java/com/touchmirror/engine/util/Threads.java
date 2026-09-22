package com.touchmirror.engine.util;

import android.os.Handler;

import java.util.concurrent.Callable;
import java.util.concurrent.Semaphore;

public final class Threads {
    private Threads() {
    }

    public static <T> T executeSynchronouslyOn(Handler handler, Callable<T> callable) throws Throwable {
        Semaphore done = new Semaphore(0);
        @SuppressWarnings("unchecked")
        T[] result = (T[]) new Object[1];
        Throwable[] failure = new Throwable[1];

        handler.post(() -> {
            try {
                result[0] = callable.call();
            } catch (Throwable t) {
                failure[0] = t;
            } finally {
                done.release();
            }
        });

        try {
            done.acquire();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }

        if (failure[0] != null) {
            throw failure[0];
        }
        return result[0];
    }
}
