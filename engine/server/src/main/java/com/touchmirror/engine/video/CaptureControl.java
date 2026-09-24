package com.touchmirror.engine.video;

import android.media.MediaCodec;
import android.os.Bundle;

import com.touchmirror.engine.util.Ln;

public class CaptureControl {

    public static final int RESET_REASON_TERMINATED = 1;
    public static final int RESET_REASON_DISPLAY_PROPERTIES_CHANGED = 1 << 1;
    public static final int RESET_REASON_CLIENT_RESET = 1 << 2;
    public static final int RESET_REASON_CLIENT_RESIZED = 1 << 3;

    private int reset = 0;

    private MediaCodec runningMediaCodec;

    public synchronized boolean isResetRequested() {
        return reset != 0;
    }

    public synchronized int consumeReset() {
        int value = reset;
        reset = 0;
        return value;
    }

    public synchronized void reset(int reason) {
        assert reason != 0;
        reset |= reason;
        if (runningMediaCodec != null) {
            try {
                runningMediaCodec.signalEndOfInputStream();
            } catch (IllegalStateException e) {
            }
        }
    }

    public synchronized void setRunningMediaCodec(MediaCodec runningMediaCodec) {
        this.runningMediaCodec = runningMediaCodec;
    }

    public synchronized void setVideoParams(int bitRate, boolean suspend) {
        MediaCodec codec = runningMediaCodec;
        if (codec == null) {
            return;
        }
        try {
            Bundle params = new Bundle();
            params.putInt(MediaCodec.PARAMETER_KEY_VIDEO_BITRATE, bitRate);
            params.putInt(MediaCodec.PARAMETER_KEY_SUSPEND, suspend ? 1 : 0);
            codec.setParameters(params);
            Ln.i("Video params: bitrate=" + bitRate + " suspend=" + suspend);
        } catch (IllegalStateException e) {
        }
    }
}
