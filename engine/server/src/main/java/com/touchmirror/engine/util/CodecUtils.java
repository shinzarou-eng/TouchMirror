package com.touchmirror.engine.util;

import android.media.MediaCodecInfo;
import android.media.MediaCodecList;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

public final class CodecUtils {
    private CodecUtils() {
    }

    public static MediaCodecInfo[] getEncoders(MediaCodecList codecs, String mimeType) {
        List<MediaCodecInfo> encoders = new ArrayList<>();
        for (MediaCodecInfo info : codecs.getCodecInfos()) {
            if (info.isEncoder() && Arrays.asList(info.getSupportedTypes()).contains(mimeType)) {
                encoders.add(info);
            }
        }
        return encoders.toArray(new MediaCodecInfo[0]);
    }
}
