package com.touchmirror.engine.video;

import com.touchmirror.engine.model.Orientation;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.util.AffineMatrix;

import android.graphics.Rect;

public class VideoFilter {

    private Size size;
    private AffineMatrix transform;

    public VideoFilter(Size inputSize) {
        this.size = inputSize;
    }

    public Size getOutputSize() {
        return size;
    }

    public AffineMatrix getTransform() {
        return transform;
    }

    public AffineMatrix getInverseTransform() {
        return transform != null ? transform.invert() : null;
    }

    public void addCrop(Rect crop, boolean transposed) {
        if (transposed) {
            crop = new Rect(crop.top, crop.left, crop.bottom, crop.right);
        }

        double inputWidth = size.getWidth();
        double inputHeight = size.getHeight();
        if (crop.left < 0 || crop.top < 0 || crop.right > inputWidth || crop.bottom > inputHeight) {
            throw new IllegalArgumentException("Crop " + crop + " exceeds the input area (" + size + ")");
        }

        transform = AffineMatrix.reframe(crop.left / inputWidth, 1 - crop.bottom / inputHeight, crop.width() / inputWidth,
                crop.height() / inputHeight).multiply(transform);
        size = new Size(crop.width(), crop.height());
    }

    public void addRotation(int ccwRotation) {
        if (ccwRotation == 0) {
            return;
        }

        transform = AffineMatrix.rotateOrtho(ccwRotation).multiply(transform);
        if (ccwRotation % 2 != 0) {
            size = size.rotate();
        }
    }

    public void addOrientation(Orientation captureOrientation) {
        if (captureOrientation.isFlipped()) {
            transform = AffineMatrix.hflip().multiply(transform);
        }
        addRotation((4 - captureOrientation.getRotation()) % 4);
    }

    public void addOrientation(int displayRotation, boolean locked, Orientation captureOrientation) {
        if (locked) {
            addRotation((4 - displayRotation) % 4);
        }
        addOrientation(captureOrientation);
    }

    public void addAngle(double cwAngle) {
        if (cwAngle == 0) {
            return;
        }
        transform = AffineMatrix.rotate(-cwAngle).withAspectRatio(size).fromCenter().multiply(transform);
    }

    public void addResize(Size targetSize) {
        if (size.equals(targetSize)) {
            return;
        }

        if (transform == null) {
            transform = AffineMatrix.IDENTITY;
        }
        size = targetSize;
    }
}
