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
    @SuppressLint("InlinedApi") // introduced in API 29
    AV1(0x00_61_76_31, "av1", MediaFormat.MIMETYPE_VIDEO_AV1),
    VP8(0x00_76_70_38, "vp8", MediaFormat.MIMETYPE_VIDEO_VP8),
    VP9(0x00_76_70_39, "vp9", MediaFormat.MIMETYPE_VIDEO_VP9);

    private final int id; // 4-byte ASCII representation of the name
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

    /**
     * « auto » : préfère H.265 (même qualité à ~moitié débit) quand un encodeur
     * HEVC matériel existe, sinon H.264. Un encodeur HEVC logiciel serait bien
     * trop lent sur téléphone, d'où le filtre matériel.
     */
    public static VideoCodec pickAuto() {
        try {
            for (MediaCodecInfo info : new MediaCodecList(MediaCodecList.REGULAR_CODECS).getCodecInfos()) {
                if (!info.isEncoder()) {
                    continue;
                }
                for (String type : info.getSupportedTypes()) {
                    if (!MediaFormat.MIMETYPE_VIDEO_HEVC.equalsIgnoreCase(type)) {
                        continue;
                    }
                    if (isHardware(info)) {
                        return H265;
                    }
                }
            }
        } catch (Throwable t) {
            // MediaCodecList indisponible : repli H.264
        }
        return H264;
    }

    private static boolean isHardware(MediaCodecInfo info) {
        if (Build.VERSION.SDK_INT >= 29) {
            return info.isHardwareAccelerated();
        }
        // API < 29 : les codecs logiciels AOSP portent des noms conventionnels
        String name = info.getName();
        return !(name.startsWith("OMX.google.") || name.startsWith("c2.android."));
    }
}
