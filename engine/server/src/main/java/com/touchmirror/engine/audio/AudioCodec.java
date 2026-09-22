package com.touchmirror.engine.audio;

import com.touchmirror.engine.model.Codec;

import android.media.MediaFormat;

public enum AudioCodec implements Codec {

    OPUS(0x6f707573, "opus", MediaFormat.MIMETYPE_AUDIO_OPUS),
    AAC(0x00616163, "aac", MediaFormat.MIMETYPE_AUDIO_AAC),
    FLAC(0x666c6163, "flac", MediaFormat.MIMETYPE_AUDIO_FLAC),
    RAW(0x00726177, "raw", MediaFormat.MIMETYPE_AUDIO_RAW);

    private final int id;
    private final String name;
    private final String mimeType;

    AudioCodec(int id, String name, String mimeType) {
        this.id = id;
        this.name = name;
        this.mimeType = mimeType;
    }

    @Override
    public Type getType() {
        return Type.AUDIO;
    }

    @Override
    public int getId() {
        return id;
    }

    @Override
    public String getName() {
        return name;
    }

    @Override
    public String getMimeType() {
        return mimeType;
    }
}
