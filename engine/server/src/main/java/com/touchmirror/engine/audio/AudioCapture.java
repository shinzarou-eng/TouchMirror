package com.touchmirror.engine.audio;

import android.media.MediaCodec;

import java.nio.ByteBuffer;

public interface AudioCapture {
    void checkCompatibility() throws AudioCaptureException;
    void start() throws AudioCaptureException;
    void stop();

    int read(ByteBuffer outDirectBuffer, MediaCodec.BufferInfo outBufferInfo);
}
