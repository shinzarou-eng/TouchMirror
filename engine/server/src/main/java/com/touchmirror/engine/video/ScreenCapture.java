package com.touchmirror.engine.video;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.Options;
import com.touchmirror.engine.control.PositionMapper;
import com.touchmirror.engine.device.Device;
import com.touchmirror.engine.display.DisplayInfo;
import com.touchmirror.engine.display.DisplayMonitor;
import com.touchmirror.engine.display.DisplayProperties;
import com.touchmirror.engine.model.ConfigurationException;
import com.touchmirror.engine.model.Orientation;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.opengl.AffineOpenGLFilter;
import com.touchmirror.engine.opengl.OpenGLFilter;
import com.touchmirror.engine.opengl.OpenGLRunner;
import com.touchmirror.engine.util.AffineMatrix;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.LogUtils;
import com.touchmirror.engine.wrappers.ServiceManager;
import com.touchmirror.engine.wrappers.SurfaceControl;

import android.graphics.Rect;
import android.hardware.display.VirtualDisplay;
import android.os.Build;
import android.os.IBinder;
import android.view.Surface;

import java.io.IOException;
import java.util.Locale;

public class ScreenCapture extends SurfaceCapture {

    private final VirtualDisplayListener vdListener;
    private final int displayId;
    private final Rect crop;
    private Orientation.Lock captureOrientationLock;
    private Orientation captureOrientation;
    private final float angle;

    private VideoConstraints videoConstraints;

    private DisplayInfo displayInfo;
    private Size videoSize;

    private final DisplayMonitor displayMonitor = new DisplayMonitor();

    private IBinder display;
    private VirtualDisplay virtualDisplay;
    private Surface boundSurface;
    private Size boundInputSize;
    private volatile boolean captureSuspended;

    private AffineMatrix transform;
    private OpenGLRunner glRunner;

    public ScreenCapture(VirtualDisplayListener vdListener, Options options) {
        this.vdListener = vdListener;
        this.displayId = options.getDisplayId();
        assert displayId != Device.DISPLAY_ID_NONE;
        this.crop = options.getCrop();
        this.captureOrientationLock = options.getCaptureOrientationLock();
        this.captureOrientation = options.getCaptureOrientation();
        assert captureOrientationLock != null;
        assert captureOrientation != null;
        this.angle = options.getAngle();
    }

    @Override
    public void init(VideoConstraints videoConstraints) {
        this.videoConstraints = videoConstraints;
        displayMonitor.start(displayId, (props) -> getCaptureControl().reset(CaptureControl.RESET_REASON_DISPLAY_PROPERTIES_CHANGED));
    }

    @Override
    public void prepare() throws ConfigurationException {
        displayInfo = ServiceManager.getDisplayManager().getDisplayInfo(displayId);
        if (displayInfo == null) {
            Ln.e("Display " + displayId + " not found\n" + LogUtils.buildDisplayListMessage());
            throw new ConfigurationException("Unknown display id: " + displayId);
        }

        if ((displayInfo.getFlags() & DisplayInfo.FLAG_SUPPORTS_PROTECTED_BUFFERS) == 0) {
            Ln.w("Display doesn't have FLAG_SUPPORTS_PROTECTED_BUFFERS flag, mirroring can be restricted");
        }

        Size displaySize = displayInfo.getSize();
        int displayRotation = displayInfo.getRotation();
        displayMonitor.setSessionDisplayProperties(new DisplayProperties(displaySize, displayRotation));

        if (captureOrientationLock == Orientation.Lock.LockedInitial) {
            captureOrientationLock = Orientation.Lock.LockedValue;
            captureOrientation = Orientation.fromRotation(displayRotation);
        }

        VideoFilter filter = new VideoFilter(displaySize);

        if (crop != null) {
            filter.addCrop(crop, (displayRotation % 2) != 0);
        }

        filter.addOrientation(displayRotation, captureOrientationLock != Orientation.Lock.Unlocked, captureOrientation);
        filter.addAngle(angle);

        transform = filter.getInverseTransform();
        videoSize = filter.getOutputSize().constrain(videoConstraints);
    }

    @Override
    public void start(Surface surface) throws IOException {
        if (display != null) {
            SurfaceControl.destroyDisplay(display);
            display = null;
        }
        if (virtualDisplay != null) {
            virtualDisplay.release();
            virtualDisplay = null;
        }

        Size inputSize;
        if (transform != null) {
            inputSize = displayInfo.getSize();
            assert glRunner == null;
            OpenGLFilter glFilter = new AffineOpenGLFilter(transform);
            glRunner = new OpenGLRunner(glFilter);
            surface = glRunner.start(inputSize, videoSize, surface);
        } else {
            inputSize = videoSize;
        }

        boundSurface = surface;
        boundInputSize = inputSize;

        try {
            virtualDisplay = ServiceManager.getDisplayManager()
                    .createVirtualDisplay("touchmirror", inputSize.getWidth(), inputSize.getHeight(), displayId, surface);
            Ln.d("Display: using DisplayManager API");
        } catch (Exception displayManagerException) {
            if (Build.BRAND.equalsIgnoreCase("oculus") && Build.MODEL.toLowerCase(Locale.ROOT).startsWith("quest")) {
                try {
                    virtualDisplay = (VirtualDisplay) VirtualDisplay.class.getDeclaredConstructors()[0].newInstance(null, null, null, surface);
                } catch (ReflectiveOperationException e) {
                    Ln.e("Could not create VirtualDisplay", e);
                }
            } else {
                try {
                    display = createDisplay();

                    Size deviceSize = displayInfo.getSize();
                    setDisplaySurface(display, surface, deviceSize.toRect(), inputSize.toRect(), displayInfo.getLayerStack());
                    Ln.d("Display: using SurfaceControl API");
                } catch (Exception surfaceControlException) {
                    Ln.e("Could not create display using DisplayManager", displayManagerException);
                    Ln.e("Could not create display using SurfaceControl", surfaceControlException);
                    throw new AssertionError("Could not create display");
                }
            }
        }

        if (vdListener != null) {
            int virtualDisplayId;
            PositionMapper positionMapper;
            if (virtualDisplay == null || displayId == 0) {
                positionMapper = PositionMapper.create(videoSize, transform, displayInfo.getSize());
                virtualDisplayId = displayId;
            } else {
                positionMapper = PositionMapper.create(videoSize, transform, inputSize);
                virtualDisplayId = virtualDisplay.getDisplay().getDisplayId();
            }
            vdListener.onNewVirtualDisplay(virtualDisplayId, positionMapper);
        }

        if (captureSuspended) {
            setSuspended(true);
        }
    }

    @Override
    public synchronized void setSuspended(boolean suspended) {
        captureSuspended = suspended;
        try {
            if (virtualDisplay != null) {
                virtualDisplay.setSurface(suspended ? null : boundSurface);
            } else if (display != null && displayInfo != null) {
                if (suspended) {
                    SurfaceControl.openTransaction();
                    try {
                        SurfaceControl.setDisplaySurface(display, null);
                    } finally {
                        SurfaceControl.closeTransaction();
                    }
                } else if (boundSurface != null) {
                    setDisplaySurface(display, boundSurface, displayInfo.getSize().toRect(), boundInputSize.toRect(), displayInfo.getLayerStack());
                }
            }
        } catch (Throwable t) {
            Ln.w("Could not " + (suspended ? "suspend" : "resume") + " capture: " + t.getMessage());
        }
    }

    @Override
    public void stop() {
        if (glRunner != null) {
            glRunner.stopAndRelease();
            glRunner = null;
        }
    }

    @Override
    public void release() {
        displayMonitor.stopAndRelease();

        if (display != null) {
            SurfaceControl.destroyDisplay(display);
            display = null;
        }
        if (virtualDisplay != null) {
            virtualDisplay.release();
            virtualDisplay = null;
        }
    }

    @Override
    public Size getSize() {
        return videoSize;
    }

    @Override
    protected boolean applyNewVideoConstraints(VideoConstraints videoConstraints) {
        this.videoConstraints = videoConstraints;
        return true;
    }

    private static IBinder createDisplay() throws Exception {
        boolean secure = Build.VERSION.SDK_INT < AndroidVersions.API_30_ANDROID_11 || (Build.VERSION.SDK_INT == AndroidVersions.API_30_ANDROID_11
                && !"S".equals(Build.VERSION.CODENAME));
        return SurfaceControl.createDisplay("touchmirror", secure);
    }

    private static void setDisplaySurface(IBinder display, Surface surface, Rect deviceRect, Rect displayRect, int layerStack) {
        SurfaceControl.openTransaction();
        try {
            SurfaceControl.setDisplaySurface(display, surface);
            SurfaceControl.setDisplayProjection(display, 0, deviceRect, displayRect);
            SurfaceControl.setDisplayLayerStack(display, layerStack);
        } finally {
            SurfaceControl.closeTransaction();
        }
    }
}
