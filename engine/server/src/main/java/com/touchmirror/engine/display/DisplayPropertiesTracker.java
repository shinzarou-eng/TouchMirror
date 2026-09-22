package com.touchmirror.engine.display;

import android.os.SystemClock;

import java.util.ArrayDeque;
import java.util.Deque;

public class DisplayPropertiesTracker {

    private static final long PENDING_CACHE_DURATION_MS = 3000;

    private static class PendingChange {
        private final DisplayProperties props;
        private final long timestamp;

        PendingChange(DisplayProperties props, long timestamp) {
            this.props = props;
            this.timestamp = timestamp;
        }
    }

    private final Deque<PendingChange> pending = new ArrayDeque<>();

    public synchronized void pushClientRequest(DisplayProperties props) {
        pending.addLast(new PendingChange(props, SystemClock.uptimeMillis()));
    }

    public synchronized boolean onChanged(DisplayProperties props) {
        expireOld();
        int index = indexOf(props);
        if (index == -1) {
            return false;
        }
        for (int i = 0; i <= index; ++i) {
            pending.pollFirst();
        }
        return true;
    }

    private int indexOf(DisplayProperties props) {
        int index = 0;
        for (PendingChange change : pending) {
            if (change.props.equals(props)) {
                return index;
            }
            ++index;
        }
        return -1;
    }

    private void expireOld() {
        long now = SystemClock.uptimeMillis();
        while (!pending.isEmpty() && pending.peekFirst().timestamp + PENDING_CACHE_DURATION_MS < now) {
            pending.pollFirst();
        }
    }
}
