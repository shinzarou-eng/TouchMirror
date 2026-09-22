package com.touchmirror.engine.model;

import com.touchmirror.engine.util.BinarySearch;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.video.VideoConstraints;

import android.graphics.Rect;
import android.media.MediaCodecInfo;
import android.util.Range;

import java.util.Objects;

public final class Size {

    private final int width;
    private final int height;

    public Size(int width, int height) {
        this.width = width;
        this.height = height;
    }

    public int getWidth() {
        return width;
    }

    public int getHeight() {
        return height;
    }

    public int getMax() {
        return Math.max(width, height);
    }

    public Size rotate() {
        return new Size(height, width);
    }

    public Size constrain(VideoConstraints constraints) {
        return constrain(constraints, true);
    }

    public Size constrain(VideoConstraints constraints, boolean preserveAspectRatio) {
        int maxSize = constraints.getMaxSize();
        int alignment = constraints.getAlignment();
        MediaCodecInfo.VideoCapabilities caps = constraints.getEncoderCapabilities();

        assert maxSize >= 0 : "Max size may not be negative";
        assert alignment > 0 : "Alignment must be positive";
        assert (alignment & (alignment - 1)) == 0 : "Alignment must be a power-of-two";

        if (caps == null) {
            return constrainWithoutCapabilities(maxSize, alignment, preserveAspectRatio);
        }

        boolean landscape = width >= height;
        int major = landscape ? width : height;
        int minor = landscape ? height : width;

        Range<Integer> majorRange = landscape ? caps.getSupportedWidths() : caps.getSupportedHeights();
        int maxMajor = Math.min(majorRange.getUpper(), major);
        if (maxSize > 0) {
            maxMajor = Math.min(maxMajor, maxSize);
        }

        int minBlock = (majorRange.getLower() + alignment - 1) / alignment;
        int maxBlock = maxMajor / alignment;

        int bestBlock = BinarySearch.findHighestTrue(minBlock, maxBlock, block -> {
            int pixels = block * alignment;
            return caps.isSizeSupported(align(width * pixels / major, alignment), align(height * pixels / major, alignment));
        });

        if (bestBlock < minBlock) {
            Ln.d("No matching size found, ignore encoder size validation");
            bestBlock = maxBlock;
        }

        int bestMajor = bestBlock * alignment;
        int bestMinor;

        if (preserveAspectRatio) {
            bestMinor = align(minor * bestMajor / major, alignment);
        } else {
            int maxMinor = landscape ? caps.getSupportedHeightsFor(bestMajor).getUpper() : caps.getSupportedWidthsFor(bestMajor).getUpper();
            maxMinor = Math.min(maxMinor, minor);
            if (maxSize > 0) {
                maxMinor = Math.min(maxMinor, maxSize);
            }
            bestMinor = align(maxMinor, alignment);

            maxMajor = landscape ? caps.getSupportedWidthsFor(bestMinor).getUpper() : caps.getSupportedHeightsFor(bestMinor).getUpper();
            maxMajor = Math.min(maxMajor, major);
            if (maxSize > 0) {
                maxMajor = Math.min(maxMajor, maxSize);
            }
            bestMajor = align(maxMajor, alignment);
        }

        bestMajor = Math.max(bestMajor, alignUp(majorRange.getLower(), alignment));

        int minMinor = landscape ? caps.getSupportedHeights().getLower() : caps.getSupportedWidths().getLower();
        bestMinor = Math.max(bestMinor, alignUp(minMinor, alignment));

        int w = landscape ? bestMajor : bestMinor;
        int h = landscape ? bestMinor : bestMajor;

        assert w % alignment == 0 : "The width must be a multiple of alignment";
        assert h % alignment == 0 : "The height must be a multiple of alignment";

        return new Size(w, h);
    }

    private Size constrainWithoutCapabilities(int maxSize, int alignment, boolean preserveAspectRatio) {
        int w = width;
        int h = height;
        if (maxSize > 0 && (width > maxSize || height > maxSize)) {
            if (preserveAspectRatio) {
                if (width > height) {
                    w = maxSize;
                    h = height * maxSize / width;
                } else {
                    w = width * maxSize / height;
                    h = maxSize;
                }
            } else {
                w = Math.min(width, maxSize);
                h = Math.min(height, maxSize);
            }
        }
        return new Size(Math.max(align(w, alignment), alignment), Math.max(align(h, alignment), alignment));
    }

    public Size align(int alignment) {
        int w = align(width, alignment);
        int h = align(height, alignment);
        return w == width && h == height ? this : new Size(w, h);
    }

    private static int align(int value, int alignment) {
        return value / alignment * alignment;
    }

    private static int alignUp(int value, int alignment) {
        return (value + alignment - 1) / alignment * alignment;
    }

    public Rect toRect() {
        return new Rect(0, 0, width, height);
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) {
            return true;
        }
        if (!(o instanceof Size)) {
            return false;
        }
        Size size = (Size) o;
        return width == size.width && height == size.height;
    }

    @Override
    public int hashCode() {
        return Objects.hash(width, height);
    }

    @Override
    public String toString() {
        return width + "x" + height;
    }
}
