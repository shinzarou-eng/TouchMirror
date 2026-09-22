package com.touchmirror.engine.audio;

import android.annotation.SuppressLint;
import android.media.MediaRecorder;

@SuppressLint("InlinedApi")
public enum AudioSource {

    OUTPUT(MediaRecorder.AudioSource.REMOTE_SUBMIX);

    private final int directAudioSource;

    AudioSource(int directAudioSource) {
        this.directAudioSource = directAudioSource;
    }

    public int getDirectAudioSource() {
        return directAudioSource;
    }
}
