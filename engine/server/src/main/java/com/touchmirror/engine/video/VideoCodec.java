package com.touchmirror.engine.video;

import com.touchmirror.engine.model.Codec;

import android.annotation.SuppressLint;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.os.Build;

public enum VideoCodec implements Codec {
    H264(0x68_32_36_34, "h264", MediaFormat.MIMETYPE_VIDEO_AVC),
    H265(0x68_32_36_35, "h265", MediaFormat.MIMETYPE_VIDEO_HEVC),
    @SuppressLint("InlinedApi")
    AV1(0x00_61_76_31, "av1", MediaFormat.MIMETYPE_VIDEO_AV1),
    VP8(0x00_76_70_38, "vp8", MediaFormat.MIMETYPE_VIDEO_VP8),
    VP9(0x00_76_70_39, "vp9", MediaFormat.MIMETYPE_VIDEO_VP9);

    private final int id;
    private final String name;
    private final String mimeType;

    VideoCodec(int id, String name, String mimeType) {
        this.id = id;
        this.name = name;
        this.mimeType = mimeType;
    }

    @Override
    public Type getType() {
        return Type.VIDEO;
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

    public static VideoCodec findByName(String name) {
        for (VideoCodec codec : values()) {
            if (codec.name.equals(name)) {
                return codec;
            }
        }
        return null;
    }

    public static VideoCodec pickAuto() {
        if (hasHardwareEncoder(MediaFormat.MIMETYPE_VIDEO_AV1)) {
            return AV1;
        }
        if (hasHardwareEncoder(MediaFormat.MIMETYPE_VIDEO_HEVC)) {
            return H265;
        }
        return H264;
    }

    private static boolean hasHardwareEncoder(String mimeType) {
        try {
            for (MediaCodecInfo info : new MediaCodecList(MediaCodecList.REGULAR_CODECS).getCodecInfos()) {
                if (!info.isEncoder()) {
                    continue;
                }
                for (String type : info.getSupportedTypes()) {
                    if (mimeType.equalsIgnoreCase(type) && isHardware(info)) {
                        return true;
                    }
                }
            }
        } catch (Throwable t) {
        }
        return false;
    }

    private static boolean isHardware(MediaCodecInfo info) {
        if (Build.VERSION.SDK_INT >= 29) {
            return info.isHardwareAccelerated();
        }
        String name = info.getName();
        return !(name.startsWith("OMX.google.") || name.startsWith("c2.android."));
    }
}
