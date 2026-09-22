package com.touchmirror.engine.video;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.Options;
import com.touchmirror.engine.control.PositionMapper;
import com.touchmirror.engine.display.DisplayInfo;
import com.touchmirror.engine.display.DisplayMonitor;
import com.touchmirror.engine.display.DisplayProperties;
import com.touchmirror.engine.display.DisplayPropertiesTracker;
import com.touchmirror.engine.display.DisplayResizeDebouncer;
import com.touchmirror.engine.model.NewDisplay;
import com.touchmirror.engine.model.Orientation;
import com.touchmirror.engine.model.Size;
import com.touchmirror.engine.opengl.AffineOpenGLFilter;
import com.touchmirror.engine.opengl.OpenGLFilter;
import com.touchmirror.engine.opengl.OpenGLRunner;
import com.touchmirror.engine.util.AffineMatrix;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.wrappers.ServiceManager;

import android.graphics.Rect;
import android.hardware.display.VirtualDisplay;
import android.os.Build;
import android.view.Surface;

import java.io.IOException;

public class NewDisplayCapture extends SurfaceCapture {

    private static final int VIRTUAL_DISPLAY_FLAG_PUBLIC = android.hardware.display.DisplayManager.VIRTUAL_DISPLAY_FLAG_PUBLIC;
    private static final int VIRTUAL_DISPLAY_FLAG_PRESENTATION = android.hardware.display.DisplayManager.VIRTUAL_DISPLAY_FLAG_PRESENTATION;
    private static final int VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY = android.hardware.display.DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY;
    private static final int VIRTUAL_DISPLAY_FLAG_SUPPORTS_TOUCH = 1 << 6;
    private static final int VIRTUAL_DISPLAY_FLAG_ROTATES_WITH_CONTENT = 1 << 7;
    private static final int VIRTUAL_DISPLAY_FLAG_DESTROY_CONTENT_ON_REMOVAL = 1 << 8;
    private static final int VIRTUAL_DISPLAY_FLAG_SHOULD_SHOW_SYSTEM_DECORATIONS = 1 << 9;
    private static final int VIRTUAL_DISPLAY_FLAG_TRUSTED = 1 << 10;
    private static final int VIRTUAL_DISPLAY_FLAG_OWN_DISPLAY_GROUP = 1 << 11;
    private static final int VIRTUAL_DISPLAY_FLAG_ALWAYS_UNLOCKED = 1 << 12;
    private static final int VIRTUAL_DISPLAY_FLAG_TOUCH_FEEDBACK_DISABLED = 1 << 13;
    private static final int VIRTUAL_DISPLAY_FLAG_OWN_FOCUS = 1 << 14;
    private static final int VIRTUAL_DISPLAY_FLAG_DEVICE_DISPLAY_GROUP = 1 << 15;

    private final VirtualDisplayListener vdListener;
    private final NewDisplay newDisplay;

    private final DisplayMonitor displayMonitor = new DisplayMonitor();

    private AffineMatrix displayTransform;
    private AffineMatrix eventTransform;
    private OpenGLRunner glRunner;

    private Size mainDisplaySize;
    private int mainDisplayDpi;
    private final int displayImePolicy;
    private final Rect crop;
    private final boolean captureOrientationLocked;
    private final Orientation captureOrientation;
    private final float angle;
    private final boolean vdDestroyContent;
    private final boolean vdSystemDecorations;
    private final boolean flexDisplay;

    private VideoConstraints videoConstraints;

    private VirtualDisplay virtualDisplay;
    private Surface boundSurface;
    private volatile boolean captureSuspended;
    private Size videoSize;
    private Size displaySize;
    private Size physicalSize;

    private DisplayPropertiesTracker tracker;
    private DisplayResizeDebouncer debouncer;

    private int dpi;

    public NewDisplayCapture(VirtualDisplayListener vdListener, Options options) {
        this.vdListener = vdListener;
        this.newDisplay = options.getNewDisplay();
        assert newDisplay != null;
        this.displayImePolicy = options.getDisplayImePolicy();
        this.crop = options.getCrop();
        this.captureOrientationLocked = options.getCaptureOrientationLock() != Orientation.Lock.Unlocked;
        this.captureOrientation = options.getCaptureOrientation();
        assert captureOrientation != null;
        this.angle = options.getAngle();
        this.vdDestroyContent = options.getVDDestroyContent();
        this.vdSystemDecorations = options.getVDSystemDecorations();
        this.flexDisplay = options.getFlexDisplay();
    }

    @Override
    protected void init(VideoConstraints videoConstraints) {
        setVideoConstraints(videoConstraints);

        displaySize = newDisplay.getSize();
        dpi = newDisplay.getDpi();
        if (flexDisplay) {
            if (crop != null) {
                throw new IllegalArgumentException("Flex display does not support cropping");
            }

            tracker = new DisplayPropertiesTracker();
            debouncer = new DisplayResizeDebouncer(this::triggerResize);
            debouncer.start();

            if (displaySize == null) {
                displaySize = new Size(1280, 960);
            }
            if (dpi == 0) {
                dpi = 160;
            }
        } else if (displaySize == null || dpi == 0) {
            DisplayInfo displayInfo = ServiceManager.getDisplayManager().getDisplayInfo(0);
            if (displayInfo != null) {
                mainDisplaySize = displayInfo.getSize();
                if ((displayInfo.getRotation() % 2) != 0) {
                    mainDisplaySize = mainDisplaySize.rotate();
                }
                mainDisplayDpi = displayInfo.getDpi();
            } else {
                Ln.w("Main display not found, fallback to 1920x1080 240dpi");
                mainDisplaySize = new Size(1920, 1080);
                mainDisplayDpi = 240;
            }
        }
    }

    @Override
    public void prepare() {
        int displayRotation;
        if (virtualDisplay == null) {
            if (flexDisplay) {
                assert displaySize != null;
                displaySize = displaySize.constrain(videoConstraints, false);
            } else {
                if (displaySize == null) {
                    displaySize = mainDisplaySize;
                }
                displaySize = displaySize.align(videoConstraints.getAlignment());
            }

            if (dpi == 0) {
                dpi = scaleDpi(mainDisplaySize, mainDisplayDpi, displaySize);
            }

            displayRotation = 0;
            displayMonitor.setSessionDisplayProperties(new DisplayProperties(displaySize, displayRotation));
        } else {
            DisplayInfo displayInfo = ServiceManager.getDisplayManager().getDisplayInfo(virtualDisplay.getDisplay().getDisplayId());
            dpi = displayInfo.getDpi();
            displayRotation = displayInfo.getRotation();
            displaySize = displayInfo.getSize();
            displaySize = flexDisplay ? displaySize.constrain(videoConstraints, false) : displaySize.align(videoConstraints.getAlignment());
        }

        VideoFilter filter = new VideoFilter(displaySize);

        if (crop != null) {
            filter.addCrop(crop, (displayRotation % 2) != 0);
        }

        filter.addOrientation(displayRotation, captureOrientationLocked, captureOrientation);
        filter.addAngle(angle);

        if (!flexDisplay) {
            Size outputSize = filter.getOutputSize();
            Size filteredSize = outputSize.constrain(videoConstraints);
            if (!filteredSize.equals(outputSize)) {
                filter.addResize(filteredSize);
            }
        }

        eventTransform = filter.getInverseTransform();
        videoSize = filter.getOutputSize();

        physicalSize = (displayRotation % 2) == 0 ? displaySize : displaySize.rotate();
        VideoFilter displayFilter = new VideoFilter(physicalSize);
        displayFilter.addRotation(displayRotation);
        displayTransform = AffineMatrix.multiplyAll(displayFilter.getInverseTransform(), eventTransform);
    }

    private void startNew(Surface surface) {
        try {
            int flags = VIRTUAL_DISPLAY_FLAG_PUBLIC
                    | VIRTUAL_DISPLAY_FLAG_PRESENTATION
                    | VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY
                    | VIRTUAL_DISPLAY_FLAG_SUPPORTS_TOUCH
                    | VIRTUAL_DISPLAY_FLAG_ROTATES_WITH_CONTENT;
            if (vdDestroyContent) {
                flags |= VIRTUAL_DISPLAY_FLAG_DESTROY_CONTENT_ON_REMOVAL;
            }
            if (vdSystemDecorations) {
                flags |= VIRTUAL_DISPLAY_FLAG_SHOULD_SHOW_SYSTEM_DECORATIONS;
            }
            if (Build.VERSION.SDK_INT >= AndroidVersions.API_33_ANDROID_13) {
                flags |= VIRTUAL_DISPLAY_FLAG_TRUSTED
                        | VIRTUAL_DISPLAY_FLAG_OWN_DISPLAY_GROUP
                        | VIRTUAL_DISPLAY_FLAG_ALWAYS_UNLOCKED
                        | VIRTUAL_DISPLAY_FLAG_TOUCH_FEEDBACK_DISABLED;
                if (Build.VERSION.SDK_INT >= AndroidVersions.API_34_ANDROID_14) {
                    flags |= VIRTUAL_DISPLAY_FLAG_OWN_FOCUS
                            | VIRTUAL_DISPLAY_FLAG_DEVICE_DISPLAY_GROUP;
                }
            }
            VirtualDisplay vd = ServiceManager.getDisplayManager()
                    .createNewVirtualDisplay("touchmirror", displaySize.getWidth(), displaySize.getHeight(), dpi, surface, flags);
            setCurrentVirtualDisplay(vd);
            int virtualDisplayId = vd.getDisplay().getDisplayId();
            Ln.i("New display: " + displaySize.getWidth() + "x" + displaySize.getHeight() + "/" + dpi + " (id=" + virtualDisplayId + ")");

            if (displayImePolicy != -1) {
                ServiceManager.getWindowManager().setDisplayImePolicy(virtualDisplayId, displayImePolicy);
            }

            displayMonitor.start(virtualDisplayId, (props) -> {
                int reason;
                if (flexDisplay) {
                    if (tracker.onChanged(props)) {
                        reason = CaptureControl.RESET_REASON_CLIENT_RESIZED;
                    } else {
                        reason = CaptureControl.RESET_REASON_DISPLAY_PROPERTIES_CHANGED;
                        debouncer.cancelResize();
                    }
                } else {
                    reason = CaptureControl.RESET_REASON_DISPLAY_PROPERTIES_CHANGED;
                }
                getCaptureControl().reset(reason);
            });
        } catch (Exception e) {
            Ln.e("Could not create display", e);
            throw new AssertionError("Could not create display");
        }
    }

    @Override
    public void start(Surface surface) throws IOException {
        if (displayTransform != null) {
            assert glRunner == null;
            OpenGLFilter glFilter = new AffineOpenGLFilter(displayTransform);
            glRunner = new OpenGLRunner(glFilter);
            surface = glRunner.start(physicalSize, videoSize, surface);
        }

        boundSurface = surface;

        if (virtualDisplay == null) {
            startNew(surface);
        } else {
            virtualDisplay.setSurface(surface);
        }

        if (captureSuspended) {
            setSuspended(true);
        }

        if (vdListener != null) {
            PositionMapper positionMapper = PositionMapper.create(videoSize, eventTransform, displaySize);
            vdListener.onNewVirtualDisplay(virtualDisplay.getDisplay().getDisplayId(), positionMapper);
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
    public synchronized void setSuspended(boolean suspended) {
        captureSuspended = suspended;
        VirtualDisplay vd = virtualDisplay;
        if (vd == null || (boundSurface == null && !suspended)) {
            return;
        }
        try {
            vd.setSurface(suspended ? null : boundSurface);
        } catch (Exception e) {
            Ln.w("Could not " + (suspended ? "suspend" : "resume") + " capture: " + e.getMessage());
        }
    }

    @Override
    public void release() {
        displayMonitor.stopAndRelease();

        if (debouncer != null) {
            debouncer.stop();
        }

        if (virtualDisplay != null) {
            synchronized (this) {
                virtualDisplay.release();
                setCurrentVirtualDisplay(null);
            }
        }
    }

    @Override
    public Size getSize() {
        return videoSize;
    }

    @Override
    protected boolean applyNewVideoConstraints(VideoConstraints videoConstraints) {
        setVideoConstraints(videoConstraints);
        return true;
    }

    private static int scaleDpi(Size initialSize, int initialDpi, Size size) {
        return initialDpi * size.getMax() / initialSize.getMax();
    }

    public void requestResize(int width, int height) {
        if (!flexDisplay) {
            throw new IllegalStateException("Cannot resize a non-flex display");
        }

        Size newSize = new Size(width, height).constrain(getVideoConstraints(), false);
        if (Ln.isEnabled(Ln.Level.VERBOSE)) {
            Ln.v(getClass().getSimpleName() + ": requestResize(" + width + ", " + height + ")");
            Ln.v(getClass().getSimpleName() + ": constrained size = " + newSize);
        }

        debouncer.requestResize(newSize);
    }

    private synchronized void setCurrentVirtualDisplay(VirtualDisplay virtualDisplay) {
        this.virtualDisplay = virtualDisplay;
    }

    private synchronized VideoConstraints getVideoConstraints() {
        return videoConstraints;
    }

    private synchronized void setVideoConstraints(VideoConstraints videoConstraints) {
        this.videoConstraints = videoConstraints;
    }

    private synchronized void triggerResize(Size size) {
        if (virtualDisplay != null) {
            size = size.constrain(videoConstraints, false);
            int displayId = virtualDisplay.getDisplay().getDisplayId();
            DisplayInfo displayInfo = ServiceManager.getDisplayManager().getDisplayInfo(displayId);
            int dpi = displayInfo.getDpi();
            int displayRotation = displayInfo.getRotation();
            if (captureOrientation.isSwap()) {
                size = size.rotate();
            }
            tracker.pushClientRequest(new DisplayProperties(size, displayRotation));

            Size vdSize = (displayRotation % 2) == 0 ? size : size.rotate();
            virtualDisplay.resize(vdSize.getWidth(), vdSize.getHeight(), dpi);
        }
    }
}
