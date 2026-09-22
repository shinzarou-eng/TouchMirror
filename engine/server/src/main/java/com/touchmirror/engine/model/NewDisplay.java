package com.touchmirror.engine.model;

public final class NewDisplay {

    private final Size size;
    private final int dpi;

    public NewDisplay(Size size, int dpi) {
        this.size = size;
        this.dpi = dpi;
    }

    public Size getSize() {
        return size;
    }

    public int getDpi() {
        return dpi;
    }

    public boolean hasExplicitSize() {
        return size != null;
    }

    public boolean hasExplicitDpi() {
        return dpi != 0;
    }
}
