package com.touchmirror.engine.display;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.device.Device;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.wrappers.DisplayManager;
import com.touchmirror.engine.wrappers.DisplayWindowListener;
import com.touchmirror.engine.wrappers.ServiceManager;

import android.content.res.Configuration;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.view.Display;
import android.view.IDisplayWindowListener;

public class DisplayMonitor {

    public interface Listener {
        void onDisplayPropertiesChanged(DisplayProperties props);
    }

    private static final boolean USE_LEGACY_LISTENER = Build.VERSION.SDK_INT < AndroidVersions.API_34_ANDROID_14;
    private static final long POLL_INTERVAL_MS = 500;

    private int displayId = Device.DISPLAY_ID_NONE;
    private DisplayProperties props;
    private Boolean screenOn;
    private Listener listener;

    private DisplayManager.DisplayListenerHandle displayListenerHandle;
    private HandlerThread listenerThread;
    private IDisplayWindowListener displayWindowListener;

    private HandlerThread pollThread;
    private Handler pollHandler;
    private final Runnable pollRunnable = new Runnable() {
        @Override
        public void run() {
            try {
                checkDisplayPropertiesChanged();
            } catch (Throwable e) {
                Ln.e("DisplayMonitor error", e);
            }
            Handler handler = pollHandler;
            if (handler != null) {
                handler.postDelayed(this, POLL_INTERVAL_MS);
            }
        }
    };

    public void start(int displayId, Listener listener) {
        assert listener != null;
        assert this.displayId == Device.DISPLAY_ID_NONE;

        this.listener = listener;
        this.displayId = displayId;
        this.screenOn = null;

        if (USE_LEGACY_LISTENER) {
            listenerThread = new HandlerThread("DisplayListener");
            listenerThread.start();
            displayListenerHandle = ServiceManager.getDisplayManager().registerDisplayListener(eventDisplayId -> {
                if (Ln.isEnabled(Ln.Level.VERBOSE)) {
                    Ln.v("DisplayMonitor: onDisplayChanged(" + eventDisplayId + ")");
                }
                if (eventDisplayId == displayId) {
                    checkDisplayPropertiesChanged();
                }
            }, new Handler(listenerThread.getLooper()));
        } else {
            displayWindowListener = new DisplayWindowListener() {
                @Override
                public void onDisplayConfigurationChanged(int eventDisplayId, Configuration newConfig) {
                    if (Ln.isEnabled(Ln.Level.VERBOSE)) {
                        Ln.v("DisplayMonitor: onDisplayConfigurationChanged(" + eventDisplayId + ")");
                    }
                    if (eventDisplayId == displayId) {
                        checkDisplayPropertiesChanged();
                    }
                }
            };
            ServiceManager.getWindowManager().registerDisplayWindowListener(displayWindowListener);
        }

        pollThread = new HandlerThread("DisplayMonitorPoll");
        pollThread.start();
        pollHandler = new Handler(pollThread.getLooper());
        pollHandler.postDelayed(pollRunnable, POLL_INTERVAL_MS);
    }

    public void stopAndRelease() {
        if (USE_LEGACY_LISTENER) {
            if (displayListenerHandle != null) {
                ServiceManager.getDisplayManager().unregisterDisplayListener(displayListenerHandle);
                displayListenerHandle = null;
            }
            if (listenerThread != null) {
                listenerThread.quitSafely();
            }
        } else if (displayWindowListener != null) {
            ServiceManager.getWindowManager().unregisterDisplayWindowListener(displayWindowListener);
        }

        if (pollThread != null) {
            pollHandler = null;
            pollThread.quitSafely();
            pollThread = null;
        }
    }

    private synchronized DisplayProperties swapDisplayProperties(DisplayProperties newProps) {
        DisplayProperties old = props;
        props = newProps;
        return old;
    }

    public synchronized void setSessionDisplayProperties(DisplayProperties props) {
        this.props = props;
    }

    private synchronized void checkDisplayPropertiesChanged() {
        DisplayInfo displayInfo = ServiceManager.getDisplayManager().getDisplayInfo(displayId);
        if (displayInfo == null) {
            Ln.w("DisplayInfo for " + displayId + " cannot be retrieved");
            screenOn = null;
            DisplayProperties old = swapDisplayProperties(null);
            if (Ln.isEnabled(Ln.Level.VERBOSE)) {
                Ln.v("DisplayMonitor: " + old + " -> (unknown)");
            }
            listener.onDisplayPropertiesChanged(null);
            return;
        }

        DisplayProperties newProps = new DisplayProperties(displayInfo.getSize(), displayInfo.getRotation());
        DisplayProperties old = swapDisplayProperties(newProps);
        if (Ln.isEnabled(Ln.Level.VERBOSE)) {
            Ln.v("DisplayMonitor: " + old + " -> " + newProps + (newProps.equals(old) ? " (unchanged)" : ""));
        }

        boolean on = displayInfo.getState() == Display.STATE_ON;
        Boolean previousOn = screenOn;
        screenOn = on;
        boolean wokeUp = previousOn != null && !previousOn && on;
        if (previousOn == null || previousOn != on) {
            Ln.i("DisplayMonitor: display " + displayId + " state=" + displayInfo.getState() + " on=" + on);
        }

        if (!newProps.equals(old) || wokeUp) {
            listener.onDisplayPropertiesChanged(newProps);
        }
    }
}
