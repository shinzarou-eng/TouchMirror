package com.touchmirror.engine;

import com.touchmirror.engine.audio.AudioCapture;
import com.touchmirror.engine.audio.AudioCodec;
import com.touchmirror.engine.audio.AudioEncoder;
import com.touchmirror.engine.audio.AudioDirectCapture;
import com.touchmirror.engine.audio.AudioSource;
import com.touchmirror.engine.audio.AudioRawRecorder;
import com.touchmirror.engine.control.ControlChannel;
import com.touchmirror.engine.control.ControlMessage;
import com.touchmirror.engine.control.Controller;
import com.touchmirror.engine.device.DesktopConnection;
import com.touchmirror.engine.device.Device;
import com.touchmirror.engine.device.Streamer;
import com.touchmirror.engine.model.ConfigurationException;
import com.touchmirror.engine.model.NewDisplay;
import com.touchmirror.engine.opengl.OpenGLRunner;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.video.NewDisplayCapture;
import com.touchmirror.engine.video.ScreenCapture;
import com.touchmirror.engine.video.SurfaceCapture;
import com.touchmirror.engine.video.SurfaceEncoder;

import android.annotation.SuppressLint;
import android.os.Build;
import android.os.Looper;
import android.system.Os;

import java.io.File;
import java.io.IOException;
import java.lang.reflect.Field;
import java.util.ArrayList;
import java.util.List;

public final class Server {

    public static final String SERVER_PATH;

    static {
        String[] classPaths = System.getProperty("java.class.path").split(File.pathSeparator);
        SERVER_PATH = classPaths[0];
    }

    private static class Completion {
        private int running;
        private boolean fatalError;

        Completion(int running) {
            this.running = running;
        }

        synchronized void addCompleted(boolean fatalError) {
            --running;
            if (fatalError) {
                this.fatalError = true;
            }
            if (running == 0 || this.fatalError) {
                Looper.getMainLooper().quitSafely();
            }
        }
    }

    private Server() {
    }

    private static void mirror(Options options) throws IOException, ConfigurationException {
        int scid = options.getScid();
        boolean tunnelForward = options.isTunnelForward();

        Workarounds.apply();

        List<AsyncProcessor> asyncProcessors = new ArrayList<>();

        DesktopConnection connection = DesktopConnection.open(scid, tunnelForward, true);
        CleanUp cleanUp = null;
        try {
            int caps = Protocol.CAP_VIDEO | Protocol.CAP_AUDIO | Protocol.CAP_CONTROL | Protocol.CAP_CLIPBOARD
                    | Protocol.CAP_H265 | Protocol.CAP_AV1 | Protocol.CAP_VDISPLAY;
            if (com.touchmirror.engine.uhid.UhidDevice.isSupported()) {
                caps |= Protocol.CAP_UHID;
            }
            connection.sendHello(Device.getDeviceName(), caps);

            ControlChannel controlChannel = connection.getControlChannel();
            ControlMessage configMsg = controlChannel.recv();
            if (configMsg.getType() != ControlMessage.TYPE_CONFIG) {
                throw new IOException("Premier message attendu : CONFIG (reçu type=" + configMsg.getType() + ")");
            }
            options.applyConfig(configMsg.getConfig());
            Ln.initLogLevel(options.getLogLevel());

            if (Build.VERSION.SDK_INT < AndroidVersions.API_29_ANDROID_10 && options.getNewDisplay() != null) {
                Ln.e("New virtual display is not supported before Android 10");
                throw new ConfigurationException("New virtual display is not supported");
            }

            boolean control = options.getControl();
            boolean video = options.getVideo();
            boolean audio = options.getAudio();

            if (options.getCleanup()) {
                cleanUp = CleanUp.start(options);
            }

            String startApp = options.getStartApp();
            if (startApp != null) {
                int startAppDisplayId = options.getDisplayId() != Device.DISPLAY_ID_NONE
                        ? options.getDisplayId() : 0;
                new Thread(() -> Device.startApp(startApp, startAppDisplayId), "start-app").start();
            }

            Controller controller = null;

            if (control) {
                controller = new Controller(controlChannel, cleanUp, options);
                asyncProcessors.add(controller);
            }

            if (audio) {
                AudioCodec audioCodec = options.getAudioCodec();
                AudioCapture audioCapture = new AudioDirectCapture(AudioSource.OUTPUT);

                Streamer audioStreamer = new Streamer(connection.getMuxer(), Protocol.CHAN_AUDIO, audioCodec, options.getSendStreamMeta(), options.getSendFrameMeta());
                AsyncProcessor audioRecorder;
                if (audioCodec == AudioCodec.RAW) {
                    audioRecorder = new AudioRawRecorder(audioCapture, audioStreamer);
                } else {
                    audioRecorder = new AudioEncoder(audioCapture, audioStreamer, options);
                }
                asyncProcessors.add(audioRecorder);
            }

            if (video) {
                Streamer videoStreamer = new Streamer(connection.getMuxer(), Protocol.CHAN_VIDEO, options.getVideoCodec(), options.getSendStreamMeta(),
                        options.getSendFrameMeta());
                SurfaceCapture surfaceCapture;
                NewDisplay newDisplay = options.getNewDisplay();
                if (newDisplay != null) {
                    surfaceCapture = new NewDisplayCapture(controller, options);
                } else {
                    assert options.getDisplayId() != Device.DISPLAY_ID_NONE;
                    surfaceCapture = new ScreenCapture(controller, options);
                }
                SurfaceEncoder surfaceEncoder = new SurfaceEncoder(surfaceCapture, videoStreamer, options);
                asyncProcessors.add(surfaceEncoder);

                if (controller != null) {
                    controller.setSurfaceCapture(surfaceCapture);
                }
            }

            Completion completion = new Completion(asyncProcessors.size());
            for (AsyncProcessor asyncProcessor : asyncProcessors) {
                asyncProcessor.start((fatalError) -> {
                    completion.addCompleted(fatalError);
                });
            }

            Looper.loop();
        } finally {
            if (cleanUp != null) {
                cleanUp.interrupt();
            }
            for (AsyncProcessor asyncProcessor : asyncProcessors) {
                asyncProcessor.stop();
            }

            connection.shutdown();

            try {
                if (cleanUp != null) {
                    cleanUp.join();
                }
                for (AsyncProcessor asyncProcessor : asyncProcessors) {
                    asyncProcessor.join();
                }

                OpenGLRunner.shutdown();
            } catch (InterruptedException e) {
            }

            connection.close();
        }
    }

    private static void prepareMainLooper() {
        Looper.prepare();
        synchronized (Looper.class) {
            try {
                @SuppressLint("DiscouragedPrivateApi")
                Field field = Looper.class.getDeclaredField("sMainLooper");
                field.setAccessible(true);
                field.set(null, Looper.myLooper());
            } catch (ReflectiveOperationException e) {
                throw new AssertionError(e);
            }
        }
    }

    public static void main(String... args) {
        int status = 0;
        try {
            internalMain(args);
        } catch (Throwable t) {
            Ln.e(t.getMessage(), t);
            status = 1;
        } finally {
            System.exit(status);
        }
    }

    private static void internalMain(String... args) throws Exception {
        Thread.UncaughtExceptionHandler defaultHandler = Thread.getDefaultUncaughtExceptionHandler();
        Thread.setDefaultUncaughtExceptionHandler((t, e) -> {
            Ln.e("Exception on thread " + t, e);
            if (defaultHandler != null) {
                defaultHandler.uncaughtException(t, e);
            }
        });

        dropRootPrivileges();

        prepareMainLooper();

        Options options = Options.parse(args);

        Ln.disableSystemStreams();
        Ln.initLogLevel(options.getLogLevel());

        Ln.i("Device: [" + Build.MANUFACTURER + "] " + Build.BRAND + " " + Build.MODEL + " (Android " + Build.VERSION.RELEASE + ")");

        try {
            mirror(options);
        } catch (ConfigurationException e) {
        }
    }

    @SuppressWarnings("deprecation")
    private static void dropRootPrivileges() {
        try {
            if (Os.getuid() == 0) {
                Os.setuid(2000);
            }
        } catch (Exception e) {
            Ln.w("Cannot set UID", e);
        }
    }
}
