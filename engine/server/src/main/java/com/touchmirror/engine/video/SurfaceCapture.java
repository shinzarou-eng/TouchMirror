package com.touchmirror.engine.video;

import com.touchmirror.engine.model.ConfigurationException;
import com.touchmirror.engine.model.Size;

import android.view.Surface;

import java.io.IOException;

public abstract class SurfaceCapture {

    private CaptureControl captureControl;

    public final void init(CaptureControl captureControl, VideoConstraints videoConstraints) throws ConfigurationException, IOException {
        this.captureControl = captureControl;
        init(videoConstraints);
    }

    public CaptureControl getCaptureControl() {
        return captureControl;
    }

    protected abstract void init(VideoConstraints videoConstraints) throws ConfigurationException, IOException;

    public abstract void release();

    public void prepare() throws ConfigurationException, IOException {
    }

    public abstract void start(Surface surface) throws IOException;

    public void stop() {
    }

    public void setSuspended(boolean suspended) {
    }

    public abstract Size getSize();

    public boolean isClosed() {
        return false;
    }

    protected abstract boolean applyNewVideoConstraints(VideoConstraints videoConstraints);
}
