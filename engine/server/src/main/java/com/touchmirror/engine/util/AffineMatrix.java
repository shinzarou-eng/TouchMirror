package com.touchmirror.engine.util;

import com.touchmirror.engine.model.Point;
import com.touchmirror.engine.model.Size;

public class AffineMatrix {

    private final double a, b, c, d, e, f;

    public static final AffineMatrix IDENTITY = new AffineMatrix(1, 0, 0, 1, 0, 0);

    public AffineMatrix(double a, double b, double c, double d, double e, double f) {
        this.a = a;
        this.b = b;
        this.c = c;
        this.d = d;
        this.e = e;
        this.f = f;
    }

    @Override
    public String toString() {
        return "[" + a + ", " + c + ", " + e + "; " + b + ", " + d + ", " + f + "]";
    }

    public static AffineMatrix ndcFromPixels(Size size) {
        double w = size.getWidth();
        double h = size.getHeight();
        return new AffineMatrix(1 / w, 0, 0, -1 / h, 0, 1);
    }

    public static AffineMatrix ndcToPixels(Size size) {
        double w = size.getWidth();
        double h = size.getHeight();
        return new AffineMatrix(w, 0, 0, -h, 0, h);
    }

    public Point apply(Point point) {
        int x = point.getX();
        int y = point.getY();
        int xx = (int) (a * x + c * y + e);
        int yy = (int) (b * x + d * y + f);
        return new Point(xx, yy);
    }

    public AffineMatrix multiply(AffineMatrix rhs) {
        if (rhs == null) {
            return this;
        }

        double aa = this.a * rhs.a + this.c * rhs.b;
        double bb = this.b * rhs.a + this.d * rhs.b;
        double cc = this.a * rhs.c + this.c * rhs.d;
        double dd = this.b * rhs.c + this.d * rhs.d;
        double ee = this.a * rhs.e + this.c * rhs.f + this.e;
        double ff = this.b * rhs.e + this.d * rhs.f + this.f;
        return new AffineMatrix(aa, bb, cc, dd, ee, ff);
    }

    public static AffineMatrix multiplyAll(AffineMatrix... matrices) {
        AffineMatrix result = null;
        for (AffineMatrix matrix : matrices) {
            if (result == null) {
                result = matrix;
            } else {
                result = result.multiply(matrix);
            }
        }
        return result;
    }

    public AffineMatrix invert() {

        double det = a * d - c * b;
        if (det == 0) {
            return null;
        }

        double aa = d / det;
        double bb = -b / det;
        double cc = -c / det;
        double dd = a / det;
        double ee = (c * f - d * e) / det;
        double ff = (b * e - a * f) / det;

        return new AffineMatrix(aa, bb, cc, dd, ee, ff);
    }

    public AffineMatrix fromCenter() {
        return translate(0.5, 0.5).multiply(this).multiply(translate(-0.5, -0.5));
    }

    public AffineMatrix withAspectRatio(double ar) {
        return scale(1 / ar, 1).multiply(this).multiply(scale(ar, 1));
    }

    public AffineMatrix withAspectRatio(Size size) {
        double ar = (double) size.getWidth() / size.getHeight();
        return withAspectRatio(ar);
    }

    public static AffineMatrix translate(double x, double y) {
        return new AffineMatrix(1, 0, 0, 1, x, y);
    }

    public static AffineMatrix scale(double x, double y) {
        return new AffineMatrix(x, 0, 0, y, 0, 0);
    }

    public static AffineMatrix scale(Size from, Size to) {
        double scaleX = (double) to.getWidth() / from.getWidth();
        double scaleY = (double) to.getHeight() / from.getHeight();
        return scale(scaleX, scaleY);
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
}
