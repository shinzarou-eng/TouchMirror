package com.touchmirror.engine.audio;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.FakeContext;
import com.touchmirror.engine.Workarounds;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.wrappers.ServiceManager;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.content.ComponentName;
import android.content.Intent;
import android.media.AudioRecord;
import android.media.MediaCodec;
import android.os.Build;
import android.os.SystemClock;

import java.nio.ByteBuffer;

public class AudioDirectCapture implements AudioCapture {

    private static final int SAMPLE_RATE = AudioConfig.SAMPLE_RATE;
    private static final int CHANNEL_CONFIG = AudioConfig.CHANNEL_CONFIG;
    private static final int CHANNELS = AudioConfig.CHANNELS;
    private static final int CHANNEL_MASK = AudioConfig.CHANNEL_MASK;
    private static final int ENCODING = AudioConfig.ENCODING;

    private final int audioSource;

    private AudioRecord recorder;
    private AudioRecordReader reader;

    public AudioDirectCapture(AudioSource audioSource) {
        this.audioSource = audioSource.getDirectAudioSource();
    }

    @TargetApi(AndroidVersions.API_23_ANDROID_6_0)
    @SuppressLint({"WrongConstant", "MissingPermission"})
    private static AudioRecord createAudioRecord(int audioSource) {
        AudioRecord.Builder builder = new AudioRecord.Builder();
        if (Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12) {
            builder.setContext(FakeContext.get());
        }
        builder.setAudioSource(audioSource);
        builder.setAudioFormat(AudioConfig.createAudioFormat());
        int minBufferSize = AudioRecord.getMinBufferSize(SAMPLE_RATE, CHANNEL_CONFIG, ENCODING);
        if (minBufferSize > 0) {
            builder.setBufferSizeInBytes(8 * minBufferSize);
        }

        return builder.build();
    }

    private static void startWorkaroundAndroid11() {
        Intent intent = new Intent(Intent.ACTION_MAIN);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        intent.addCategory(Intent.CATEGORY_LAUNCHER);
        intent.setComponent(new ComponentName(FakeContext.PACKAGE_NAME, "com.android.shell.HeapDumpActivity"));
        ServiceManager.getActivityManager().startActivity(intent);
    }

    private static void stopWorkaroundAndroid11() {
        ServiceManager.getActivityManager().forceStopPackage(FakeContext.PACKAGE_NAME);
    }

    private void tryStartRecording(int attempts, int delayMs) throws AudioCaptureException {
        while (attempts-- > 0) {
            SystemClock.sleep(delayMs);
            try {
                startRecording();
                return;
            } catch (UnsupportedOperationException e) {
                if (attempts == 0) {
                    Ln.e("Failed to start audio capture");
                    Ln.e("On Android 11, audio capture must be started in the foreground, make sure that the device is unlocked when starting "
                            + "TouchMirror.");
                    throw new AudioCaptureException();
                } else {
                    Ln.d("Failed to start audio capture, retrying...");
                }
            }
        }
    }

    private void startRecording() throws AudioCaptureException {
        try {
            recorder = createAudioRecord(audioSource);
        } catch (NullPointerException e) {
            recorder = Workarounds.createAudioRecord(audioSource, SAMPLE_RATE, CHANNEL_CONFIG, CHANNELS, CHANNEL_MASK, ENCODING);
        }
        recorder.startRecording();
        reader = new AudioRecordReader(recorder);
    }

    @Override
    public void checkCompatibility() throws AudioCaptureException {
        if (Build.VERSION.SDK_INT < AndroidVersions.API_30_ANDROID_11) {
            Ln.w("Audio disabled: it is not supported before Android 11");
            throw new AudioCaptureException();
        }
    }

    @Override
    public void start() throws AudioCaptureException {
        if (Build.VERSION.SDK_INT == AndroidVersions.API_30_ANDROID_11) {
            startWorkaroundAndroid11();
            try {
                tryStartRecording(5, 100);
            } finally {
                stopWorkaroundAndroid11();
            }
        } else {
            startRecording();
        }
    }

    @Override
    public void stop() {
        if (recorder != null) {
            recorder.release();
        }
    }

    @Override
    @TargetApi(AndroidVersions.API_24_ANDROID_7_0)
    public int read(ByteBuffer outDirectBuffer, MediaCodec.BufferInfo outBufferInfo) {
        return reader.read(outDirectBuffer, outBufferInfo);
    }
}
