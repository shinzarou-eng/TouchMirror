package com.touchmirror.engine.opengl;

public interface OpenGLFilter {

    void init() throws OpenGLException;

    void draw(int textureId, float[] texMatrix);

    void release();
}
