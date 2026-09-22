package com.touchmirror.engine.display;

import com.touchmirror.engine.model.Size;

import java.util.Objects;

public final class DisplayProperties {

    private Size size;
    private int rotation;

    public DisplayProperties(Size size, int rotation) {
        assert size != null;
        assert rotation >= 0 && rotation < 4;
        this.size = size;
        this.rotation = rotation;
    }

    public Size getSize() {
        return size;
    }

    public void setSize(Size size) {
        this.size = size;
    }

    public int getRotation() {
        return rotation;
    }

    public void setRotation(int rotation) {
        this.rotation = rotation;
    }

    @Override
    public boolean equals(Object o) {
        if (!(o instanceof DisplayProperties)) {
            return false;
        }
        DisplayProperties other = (DisplayProperties) o;
        return rotation == other.rotation && Objects.equals(size, other.size);
    }

    @Override
    public int hashCode() {
        return Objects.hash(size, rotation);
    }

    @Override
    public String toString() {
        return size + " [rotation=" + rotation + "]";
    }
}
