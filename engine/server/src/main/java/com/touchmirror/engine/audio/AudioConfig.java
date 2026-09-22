package com.touchmirror.engine.audio;

import android.media.AudioFormat;

public final class AudioConfig {

    public static final int SAMPLE_RATE = 48000;
    public static final int CHANNEL_CONFIG = AudioFormat.CHANNEL_IN_STEREO;
    public static final int CHANNELS = 2;
    public static final int CHANNEL_MASK = AudioFormat.CHANNEL_IN_LEFT | AudioFormat.CHANNEL_IN_RIGHT;
    public static final int ENCODING = AudioFormat.ENCODING_PCM_16BIT;
    public static final int BYTES_PER_SAMPLE = 2;

    public static final int MAX_READ_SIZE = 1024 * CHANNELS * BYTES_PER_SAMPLE;

    private AudioConfig() {
    }

    public static AudioFormat createAudioFormat() {
        return new AudioFormat.Builder()
                .setEncoding(ENCODING)
                .setSampleRate(SAMPLE_RATE)
                .setChannelMask(CHANNEL_CONFIG)
                .build();
    }
}
