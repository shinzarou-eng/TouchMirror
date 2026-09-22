package com.touchmirror.engine.model;

public enum Orientation {

    Orient0,
    Orient90,
    Orient180,
    Orient270,
    Flip0,
    Flip90,
    Flip180,
    Flip270;

    public enum Lock {
        Unlocked, LockedInitial, LockedValue
    }

    public static Orientation fromRotation(int ccwRotation) {
        assert ccwRotation >= 0 && ccwRotation < 4;
        return values()[(4 - ccwRotation) % 4];
    }

    public boolean isFlipped() {
        return (ordinal() & 4) != 0;
    }

    public int getRotation() {
        return ordinal() & 3;
    }

    public boolean isSwap() {
        return (ordinal() & 1) != 0;
    }
}
