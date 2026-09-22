package com.touchmirror.engine.util;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.audio.AudioCodec;
import com.touchmirror.engine.device.Device;
import com.touchmirror.engine.display.DisplayInfo;
import com.touchmirror.engine.model.Codec;
import com.touchmirror.engine.model.DeviceApp;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.video.VideoCodec;
import com.touchmirror.engine.wrappers.DisplayManager;
import com.touchmirror.engine.wrappers.ServiceManager;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.os.Build;

import java.util.Collections;
import java.util.List;
import java.util.Objects;

public final class LogUtils {

    private static final int OPTION_COLUMN = 70;
    private static final int APP_NAME_COLUMN = 30;

    private LogUtils() {
    }

    private static String buildEncoderListMessage(String type, Codec[] codecs) {
        StringBuilder builder = new StringBuilder("List of ").append(type).append(" encoders:");
        MediaCodecList codecList = new MediaCodecList(MediaCodecList.REGULAR_CODECS);
        for (Codec codec : codecs) {
            for (MediaCodecInfo info : CodecUtils.getEncoders(codecList, codec.getMimeType())) {
                int lineStart = builder.length();
                builder.append("\n    --").append(type).append("-codec=").append(codec.getName());
                builder.append(" --").append(type).append("-encoder=").append(info.getName());
                if (Build.VERSION.SDK_INT >= AndroidVersions.API_29_ANDROID_10) {
                    int padding = OPTION_COLUMN - (builder.length() - lineStart);
                    if (padding > 0) {
                        builder.append(String.format("%" + padding + "s", " "));
                    }
                    builder.append(" (").append(getHwCodecType(info)).append(')');
                    if (info.isVendor()) {
                        builder.append(" [vendor]");
                    }
                    if (info.isAlias()) {
                        builder.append(" (alias for ").append(info.getCanonicalName()).append(')');
                    }
                }
            }
        }
        return builder.toString();
    }

    public static String buildVideoEncoderListMessage() {
        return buildEncoderListMessage("video", VideoCodec.values());
    }

    public static String buildAudioEncoderListMessage() {
        return buildEncoderListMessage("audio", AudioCodec.values());
    }

    @TargetApi(AndroidVersions.API_29_ANDROID_10)
    private static String getHwCodecType(MediaCodecInfo info) {
        if (info.isSoftwareOnly()) {
            return "sw";
        }
        return info.isHardwareAccelerated() ? "hw" : "hybrid";
    }

    public static String buildDisplayListMessage() {
        StringBuilder builder = new StringBuilder("List of displays:");
        DisplayManager displayManager = ServiceManager.getDisplayManager();
        int[] displayIds = displayManager.getDisplayIds();
        if (displayIds == null || displayIds.length == 0) {
            return builder.append("\n    (none)").toString();
        }

        for (int id : displayIds) {
            builder.append("\n    --display-id=").append(id).append("    (");
            DisplayInfo displayInfo = displayManager.getDisplayInfo(id);
            if (displayInfo != null) {
                Size size = displayInfo.getSize();
                builder.append(size.getWidth()).append('x').append(size.getHeight());
            } else {
                builder.append("size unknown");
            }
            builder.append(')');
        }
        return builder.toString();
    }

    public static String buildAppListMessage() {
        return buildAppListMessage("List of apps:", Device.listApps());
    }

    @SuppressLint("QueryPermissionsNeeded")
    public static String buildAppListMessage(String title, List<DeviceApp> apps) {
        StringBuilder builder = new StringBuilder(title);

        Collections.sort(apps, (first, second) -> {
            int cmp = -Boolean.compare(first.isSystem(), second.isSystem());
            if (cmp == 0) {
                cmp = Objects.compare(first.getName(), second.getName(), String::compareTo);
            }
            if (cmp == 0) {
                cmp = Objects.compare(first.getPackageName(), second.getPackageName(), String::compareTo);
            }
            return cmp;
        });

        for (DeviceApp app : apps) {
            String name = app.getName();
            builder.append("\n ").append(app.isSystem() ? "* " : "- ").append(name);
            int padding = APP_NAME_COLUMN - name.length();
            if (padding > 0) {
                builder.append(String.format("%" + padding + "s", " "));
            } else {
                builder.append("\n   ").append(String.format("%" + APP_NAME_COLUMN + "s", " "));
            }
            builder.append(' ').append(app.getPackageName());
        }
        return builder.toString();
    }
}
