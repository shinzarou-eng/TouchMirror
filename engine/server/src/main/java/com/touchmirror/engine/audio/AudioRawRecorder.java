package com.touchmirror.engine.audio;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.AsyncProcessor;
import com.touchmirror.engine.device.Streamer;
import com.touchmirror.engine.util.IO;
import com.touchmirror.engine.util.Ln;

import android.media.MediaCodec;
import android.os.Build;

import java.io.IOException;
import java.nio.ByteBuffer;

public final class AudioRawRecorder implements AsyncProcessor {

    private final AudioCapture capture;
    private final Streamer streamer;

    private Thread thread;

    public AudioRawRecorder(AudioCapture capture, Streamer streamer) {
        this.capture = capture;
        this.streamer = streamer;
    }

    private void record() throws IOException, AudioCaptureException {
        if (Build.VERSION.SDK_INT < AndroidVersions.API_30_ANDROID_11) {
            Ln.w("Audio disabled: it is not supported before Android 11");
            streamer.writeDisableStream(false);
            return;
        }

        ByteBuffer buffer = ByteBuffer.allocateDirect(AudioConfig.MAX_READ_SIZE);
        MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();

        try {
            try {
                capture.start();
            } catch (Throwable t) {
                streamer.writeDisableStream(false);
                throw t;
            }

            streamer.writeAudioHeader();
            while (!Thread.currentThread().isInterrupted()) {
                buffer.position(0);
                int size = capture.read(buffer, bufferInfo);
                if (size < 0) {
                    throw new IOException("Could not read audio: " + size);
                }
                buffer.limit(size);
                streamer.writePacket(buffer, bufferInfo);
            }
        } catch (IOException e) {
            if (!IO.isBrokenPipe(e)) {
                Ln.e("Audio capture error", e);
            }
        } finally {
            capture.stop();
        }
    }

    @Override
    public void start(TerminationListener listener) {
        thread = new Thread(() -> {
            boolean fatalError = false;
            try {
                record();
            } catch (AudioCaptureException e) {
            } catch (Throwable t) {
                Ln.e("Audio recording error", t);
                fatalError = true;
            } finally {
                Ln.d("Audio recorder stopped");
                listener.onTerminated(fatalError);
            }
        }, "audio-raw");
        thread.start();
    }

    @Override
    public void stop() {
        if (thread != null) {
            thread.interrupt();
        }
    }

    @Override
    public void join() throws InterruptedException {
        if (thread != null) {
            thread.join();
        }
    }
}
