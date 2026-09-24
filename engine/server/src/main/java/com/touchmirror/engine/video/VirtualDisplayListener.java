package com.touchmirror.engine.video;

import com.touchmirror.engine.control.PositionMapper;

public interface VirtualDisplayListener {
    void onNewVirtualDisplay(int displayId, PositionMapper positionMapper);
}
