package com.touchmirror.engine.audio;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.util.Ln;

import android.annotation.TargetApi;
import android.media.AudioRecord;
import android.media.AudioTimestamp;
import android.media.MediaCodec;

import java.nio.ByteBuffer;

public class AudioRecordReader {

    private static final long ONE_SAMPLE_US = (1000000 + AudioConfig.SAMPLE_RATE - 1) / AudioConfig.SAMPLE_RATE;
    private static final long US_PER_BYTE = 1000000L / (AudioConfig.CHANNELS * AudioConfig.BYTES_PER_SAMPLE);

    private final AudioRecord recorder;
    private final AudioTimestamp timestamp = new AudioTimestamp();

    private long previousRecorderTimestamp = -1;
    private long previousPts;
    private long nextPts;

    public AudioRecordReader(AudioRecord recorder) {
        this.recorder = recorder;
    }

    @TargetApi(AndroidVersions.API_24_ANDROID_7_0)
    public int read(ByteBuffer outDirectBuffer, MediaCodec.BufferInfo outBufferInfo) {
        int size = recorder.read(outDirectBuffer, AudioConfig.MAX_READ_SIZE);
        if (size <= 0) {
            return size;
        }

        long pts;
        if (recorder.getTimestamp(timestamp, AudioTimestamp.TIMEBASE_MONOTONIC) == AudioRecord.SUCCESS
                && timestamp.nanoTime != previousRecorderTimestamp) {
            pts = timestamp.nanoTime / 1000;
            previousRecorderTimestamp = timestamp.nanoTime;
        } else {
            if (nextPts == 0) {
                Ln.w("Could not get initial audio timestamp");
                nextPts = System.nanoTime() / 1000;
            }
            pts = nextPts;
        }

        nextPts = pts + size * US_PER_BYTE / AudioConfig.SAMPLE_RATE;

        if (previousPts != 0 && pts < previousPts + ONE_SAMPLE_US) {
            pts = previousPts + ONE_SAMPLE_US;
        }
        previousPts = pts;

        outBufferInfo.set(0, size, pts, 0);
        return size;
    }
}
