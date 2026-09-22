package com.touchmirror.engine.util;

import com.touchmirror.engine.model.Point;
import com.touchmirror.engine.model.Size;

public class AffineMatrix {

    public static final AffineMatrix IDENTITY = new AffineMatrix(1, 0, 0, 1, 0, 0);

    private final double a, b, c, d, e, f;

    public AffineMatrix(double a, double b, double c, double d, double e, double f) {
        this.a = a;
        this.b = b;
        this.c = c;
        this.d = d;
        this.e = e;
        this.f = f;
    }

    public static AffineMatrix ndcFromPixels(Size size) {
        return new AffineMatrix(1d / size.getWidth(), 0, 0, -1d / size.getHeight(), 0, 1);
    }

    public static AffineMatrix ndcToPixels(Size size) {
        return new AffineMatrix(size.getWidth(), 0, 0, -size.getHeight(), 0, size.getHeight());
    }

    public Point apply(Point point) {
        int x = (int) (a * point.getX() + c * point.getY() + e);
        int y = (int) (b * point.getX() + d * point.getY() + f);
        return new Point(x, y);
    }

    public AffineMatrix multiply(AffineMatrix rhs) {
        if (rhs == null) {
            return this;
        }
        return new AffineMatrix(
                a * rhs.a + c * rhs.b,
                b * rhs.a + d * rhs.b,
                a * rhs.c + c * rhs.d,
                b * rhs.c + d * rhs.d,
                a * rhs.e + c * rhs.f + e,
                b * rhs.e + d * rhs.f + f);
    }

    public static AffineMatrix multiplyAll(AffineMatrix... matrices) {
        AffineMatrix result = null;
        for (AffineMatrix matrix : matrices) {
            result = result == null ? matrix : result.multiply(matrix);
        }
        return result;
    }

    public AffineMatrix invert() {
        double det = a * d - c * b;
        if (det == 0) {
            return null;
        }
        return new AffineMatrix(
                d / det,
                -b / det,
                -c / det,
                a / det,
                (c * f - d * e) / det,
                (b * e - a * f) / det);
    }

    public AffineMatrix fromCenter() {
        return translate(0.5, 0.5).multiply(this).multiply(translate(-0.5, -0.5));
    }

    public AffineMatrix withAspectRatio(double aspectRatio) {
        return scale(1 / aspectRatio, 1).multiply(this).multiply(scale(aspectRatio, 1));
    }

    public AffineMatrix withAspectRatio(Size size) {
        return withAspectRatio((double) size.getWidth() / size.getHeight());
    }

    public static AffineMatrix translate(double x, double y) {
        return new AffineMatrix(1, 0, 0, 1, x, y);
    }

    public static AffineMatrix scale(double x, double y) {
        return new AffineMatrix(x, 0, 0, y, 0, 0);
    }

    public static AffineMatrix scale(Size from, Size to) {
        return scale((double) to.getWidth() / from.getWidth(), (double) to.getHeight() / from.getHeight());
    }

    public static AffineMatrix reframe(double x, double y, double w, double h) {
        if (w == 0 || h == 0) {
            throw new IllegalArgumentException("Cannot reframe to an empty area: " + w + "x" + h);
        }
        return scale(1 / w, 1 / h).multiply(translate(-x, -y));
    }

    public static AffineMatrix rotateOrtho(int ccwRotation) {
        switch (ccwRotation) {
            case 0:
                return IDENTITY;
            case 1:
                return new AffineMatrix(0, 1, -1, 0, 1, 0);
            case 2:
                return new AffineMatrix(-1, 0, 0, -1, 1, 1);
            case 3:
                return new AffineMatrix(0, -1, 1, 0, 0, 1);
            default:
                throw new IllegalArgumentException("Invalid rotation: " + ccwRotation);
        }
    }

    public static AffineMatrix hflip() {
        return new AffineMatrix(-1, 0, 0, 1, 1, 0);
    }

    public static AffineMatrix vflip() {
        return new AffineMatrix(1, 0, 0, -1, 0, 1);
    }

    public static AffineMatrix rotate(double ccwDegrees) {
        double radians = Math.toRadians(ccwDegrees);
        double cos = Math.cos(radians);
        double sin = Math.sin(radians);
        return new AffineMatrix(cos, sin, -sin, cos, 0, 0);
    }

    public void to4x4(float[] matrix) {
        matrix[0] = (float) a;
        matrix[1] = (float) b;
        matrix[2] = 0;
        matrix[3] = 0;
        matrix[4] = (float) c;
        matrix[5] = (float) d;
        matrix[6] = 0;
        matrix[7] = 0;
        matrix[8] = 0;
        matrix[9] = 0;
        matrix[10] = 1;
        matrix[11] = 0;
        matrix[12] = (float) e;
        matrix[13] = (float) f;
        matrix[14] = 0;
        matrix[15] = 1;
    }

    public float[] to4x4() {
        float[] matrix = new float[16];
        to4x4(matrix);
        return matrix;
    }

    @Override
    public String toString() {
        return "[" + a + ", " + c + ", " + e + "; " + b + ", " + d + ", " + f + "]";
    }
}
