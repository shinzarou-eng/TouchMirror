package com.touchmirror.engine.control;

import com.touchmirror.engine.model.Point;
import com.touchmirror.engine.model.Position;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.util.AffineMatrix;

public final class PositionMapper {

    private final Size videoSize;
    private final AffineMatrix videoToDeviceMatrix;

    public PositionMapper(Size videoSize, AffineMatrix videoToDeviceMatrix) {
        this.videoSize = videoSize;
        this.videoToDeviceMatrix = videoToDeviceMatrix;
    }

    public static PositionMapper create(Size videoSize, AffineMatrix filterTransform, Size targetSize) {
        AffineMatrix transform = filterTransform;
        if (!videoSize.equals(targetSize) || filterTransform != null) {
            transform = AffineMatrix.ndcToPixels(targetSize).multiply(transform).multiply(AffineMatrix.ndcFromPixels(videoSize));
        }
        return new PositionMapper(videoSize, transform);
    }

    public Size getVideoSize() {
        return videoSize;
    }

    public Point map(Position position) {
        if (!videoSize.equals(position.getScreenSize())) {
            return null;
        }

        Point point = position.getPoint();
        return videoToDeviceMatrix != null ? videoToDeviceMatrix.apply(point) : point;
    }
}
