package com.touchmirror.engine;

import com.touchmirror.engine.audio.AudioCodec;
import com.touchmirror.engine.device.Device;
import com.touchmirror.engine.model.NewDisplay;
import com.touchmirror.engine.model.Orientation;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.video.VideoCodec;

import android.graphics.Rect;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.List;

public class Options {

    private Ln.Level logLevel = Ln.Level.DEBUG;
    private int scid = -1;
    private boolean video = true;
    private boolean audio = true;
    private int maxSize;
    private int minSizeAlignment = 1;
    private VideoCodec videoCodec = VideoCodec.H264;
    private AudioCodec audioCodec = AudioCodec.OPUS;
    private int videoBitRate = 8000000;
    private int audioBitRate = 128000;
    private float maxFps;
    private float angle;
    private boolean tunnelForward;
    private Rect crop;
    private boolean control = true;
    private int displayId;
    private boolean showTouches;
    private boolean stayAwake;
    private int screenOffTimeout = -1;
    private int displayImePolicy = -1;

    private String videoEncoder;
    private String audioEncoder;
    private boolean powerOffScreenOnClose;
    private boolean clipboardAutosync = true;
    private boolean downsizeOnError = true;
    private boolean cleanup = true;
    private boolean powerOn = true;

    private NewDisplay newDisplay;
    private boolean vdDestroyContent = true;
    private boolean vdSystemDecorations = true;
    private boolean flexDisplay;

    private boolean keepActive;
    private boolean ignoreVideoEncoderConstraints;

    private Orientation.Lock captureOrientationLock = Orientation.Lock.Unlocked;
    private Orientation captureOrientation = Orientation.Orient0;

    private String startApp;

    private boolean sendDeviceMeta = true;
    private boolean sendFrameMeta = true;
    private boolean sendDummyByte = true;
    private boolean sendStreamMeta = true;

    public Ln.Level getLogLevel() {
        return logLevel;
    }

    public int getScid() {
        return scid;
    }

    public boolean getVideo() {
        return video;
    }

    public boolean getAudio() {
        return audio;
    }

    public int getMaxSize() {
        return maxSize;
    }

    public int getMinSizeAlignment() {
        return minSizeAlignment;
    }

    public VideoCodec getVideoCodec() {
        return videoCodec;
    }

    public AudioCodec getAudioCodec() {
        return audioCodec;
    }

    public int getVideoBitRate() {
        return videoBitRate;
    }

    public int getAudioBitRate() {
        return audioBitRate;
    }

    public float getMaxFps() {
        return maxFps;
    }

    public float getAngle() {
        return angle;
    }

    public boolean isTunnelForward() {
        return tunnelForward;
    }

    public Rect getCrop() {
        return crop;
    }

    public boolean getControl() {
        return control;
    }

    public int getDisplayId() {
        return displayId;
    }

    public boolean getShowTouches() {
        return showTouches;
    }

    public boolean getStayAwake() {
        return stayAwake;
    }

    public int getScreenOffTimeout() {
        return screenOffTimeout;
    }

    public int getDisplayImePolicy() {
        return displayImePolicy;
    }

    public String getVideoEncoder() {
        return videoEncoder;
    }

    public String getAudioEncoder() {
        return audioEncoder;
    }

    public boolean getPowerOffScreenOnClose() {
        return this.powerOffScreenOnClose;
    }

    public boolean getClipboardAutosync() {
        return clipboardAutosync;
    }

    public boolean getDownsizeOnError() {
        return downsizeOnError;
    }

    public boolean getCleanup() {
        return cleanup;
    }

    public boolean getPowerOn() {
        return powerOn;
    }

    public NewDisplay getNewDisplay() {
        return newDisplay;
    }

    public Orientation getCaptureOrientation() {
        return captureOrientation;
    }

    public Orientation.Lock getCaptureOrientationLock() {
        return captureOrientationLock;
    }

    public boolean getVDDestroyContent() {
        return vdDestroyContent;
    }

    public boolean getVDSystemDecorations() {
        return vdSystemDecorations;
    }

    public boolean getKeepActive() {
        return keepActive;
    }

    public boolean getFlexDisplay() {
        return flexDisplay;
    }

    public boolean getIgnoreVideoEncoderConstraints() {
        return ignoreVideoEncoderConstraints;
    }

    public String getStartApp() {
        return startApp;
    }

    public boolean getSendDeviceMeta() {
        return sendDeviceMeta;
    }

    public boolean getSendFrameMeta() {
        return sendFrameMeta;
    }

    public boolean getSendDummyByte() {
        return sendDummyByte;
    }

    public boolean getSendStreamMeta() {
        return sendStreamMeta;
    }

    public static Options parse(String... args) {
        if (args.length < 2) {
            throw new IllegalArgumentException("Missing client version or scid");
        }

        String clientVersion = args[0];
        if (!clientVersion.equals(BuildConfig.VERSION_NAME)) {
            throw new IllegalArgumentException(
                    "The server version (" + BuildConfig.VERSION_NAME + ") does not match the client " + "(" + clientVersion + ")");
        }

        Options options = new Options();
        options.scid = Integer.parseInt(args[1], 0x10);
        if (options.scid < -1) {
            throw new IllegalArgumentException("scid may not be negative (except -1 for 'none'): " + options.scid);
        }
        return options;
    }

    public void applyConfig(byte[] data) {
        ByteBuffer buf = ByteBuffer.wrap(data);
        while (buf.remaining() >= 2) {
            int field = buf.get() & 0xff;
            int len = buf.get() & 0xff;
            if (len > buf.remaining()) {
                throw new IllegalArgumentException("Truncated config field " + field);
            }
            int end = buf.position() + len;
            switch (field) {
                case Protocol.CFG_AUDIO:
                    audio = buf.get() != 0;
                    break;
                case Protocol.CFG_VIDEO_CODEC:
                    videoCodec = decodeVideoCodec(buf.get() & 0xff);
                    break;
                case Protocol.CFG_AUDIO_CODEC:
                    audioCodec = decodeAudioCodec(buf.get() & 0xff);
                    break;
                case Protocol.CFG_MAX_SIZE:
                    maxSize = buf.getShort() & 0xffff;
                    break;
                case Protocol.CFG_MAX_FPS:
                    maxFps = buf.get() & 0xff;
                    break;
                case Protocol.CFG_VIDEO_BIT_RATE:
                    videoBitRate = buf.getInt();
                    break;
                case Protocol.CFG_FLAGS: {
                    int f = buf.getShort() & 0xffff;
                    video = (f & Protocol.FLAG_VIDEO) != 0;
                    stayAwake = (f & Protocol.FLAG_STAY_AWAKE) != 0;
                    powerOn = (f & Protocol.FLAG_POWER_ON) != 0;
                    cleanup = (f & Protocol.FLAG_CLEANUP) != 0;
                    downsizeOnError = (f & Protocol.FLAG_DOWNSIZE_ON_ERROR) != 0;
                    clipboardAutosync = (f & Protocol.FLAG_CLIPBOARD_AUTOSYNC) != 0;
                    break;
                }
                case Protocol.CFG_NEW_DISPLAY:
                    newDisplay = new NewDisplay(new Size(buf.getShort() & 0xffff, buf.getShort() & 0xffff), buf.getShort() & 0xffff);
                    displayId = Device.DISPLAY_ID_NONE;
                    break;
                case Protocol.CFG_START_APP: {
                    byte[] utf = new byte[len];
                    buf.get(utf);
                    startApp = new String(utf, StandardCharsets.UTF_8);
                    break;
                }
                case Protocol.CFG_LOG_LEVEL:
                    logLevel = decodeLogLevel(buf.get() & 0xff);
                    break;
                default:
                    break;
            }
            buf.position(end);
        }
    }

    private static VideoCodec decodeVideoCodec(int id) {
        switch (id) {
            case 0:
                VideoCodec auto = VideoCodec.pickAuto();
                Ln.i("Video codec auto: " + auto.getName());
                return auto;
            case 1:
                return VideoCodec.H264;
            case 2:
                return VideoCodec.H265;
            case 3:
                return VideoCodec.AV1;
            default:
                throw new IllegalArgumentException("Video codec id " + id + " not supported");
        }
    }

    private static AudioCodec decodeAudioCodec(int id) {
        switch (id) {
            case 0:
                return AudioCodec.OPUS;
            case 1:
                return AudioCodec.AAC;
            case 2:
                return AudioCodec.FLAC;
            case 3:
                return AudioCodec.RAW;
            default:
                throw new IllegalArgumentException("Audio codec id " + id + " not supported");
        }
    }

    private static Ln.Level decodeLogLevel(int id) {
        Ln.Level[] levels = Ln.Level.values();
        if (id < 0 || id >= levels.length) {
            return Ln.Level.INFO;
        }
        return levels[id];
    }






}
