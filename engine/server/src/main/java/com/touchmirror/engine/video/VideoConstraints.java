package com.touchmirror.engine.video;

import android.media.MediaCodecInfo;

public class VideoConstraints {
    private final int maxSize;
    private final int alignment;
    private final MediaCodecInfo.VideoCapabilities caps;

    public VideoConstraints(int maxSize, int alignment, MediaCodecInfo.VideoCapabilities caps) {
        assert maxSize >= 0 : "Max size must not be negative";
        this.maxSize = maxSize;

        assert alignment > 0 : "Alignment must be positive";
        assert (alignment & (alignment - 1)) == 0 : "Alignment must be a power-of-two";
        this.alignment = alignment;

        this.caps = caps;
    }

    public int getMaxSize() {
        return maxSize;
    }

    public int getAlignment() {
        return alignment;
    }

    public MediaCodecInfo.VideoCapabilities getEncoderCapabilities() {
        return caps;
    }

    public VideoConstraints withMaxSize(int maxSize) {
        return new VideoConstraints(maxSize, alignment, caps);
    }

    public VideoConstraints withCapabilities(MediaCodecInfo.VideoCapabilities caps) {
        return new VideoConstraints(maxSize, alignment, caps);
    }
}
