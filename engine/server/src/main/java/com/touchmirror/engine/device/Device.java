package com.touchmirror.engine.device;

import com.touchmirror.engine.AndroidVersions;
import com.touchmirror.engine.FakeContext;
import com.touchmirror.engine.display.DisplayInfo;
import com.touchmirror.engine.model.DeviceApp;
import com.touchmirror.engine.util.IO;
import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.LogUtils;
import com.touchmirror.engine.wrappers.ActivityManager;
import com.touchmirror.engine.wrappers.ClipboardManager;
import com.touchmirror.engine.wrappers.DisplayControl;
import com.touchmirror.engine.wrappers.InputManager;
import com.touchmirror.engine.wrappers.ServiceManager;
import com.touchmirror.engine.wrappers.SurfaceControl;
import com.touchmirror.engine.wrappers.WindowManager;

import android.annotation.SuppressLint;
import android.app.ActivityOptions;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.IBinder;
import android.os.SystemClock;
import android.view.InputDevice;
import android.view.InputEvent;
import android.view.KeyCharacterMap;
import android.view.KeyEvent;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class Device {

    public static final int DISPLAY_ID_NONE = -1;

    public static final int POWER_MODE_OFF = SurfaceControl.POWER_MODE_OFF;
    public static final int POWER_MODE_NORMAL = SurfaceControl.POWER_MODE_NORMAL;

    public static final int INJECT_MODE_ASYNC = InputManager.INJECT_INPUT_EVENT_MODE_ASYNC;
    public static final int INJECT_MODE_WAIT_FOR_RESULT = InputManager.INJECT_INPUT_EVENT_MODE_WAIT_FOR_RESULT;
    public static final int INJECT_MODE_WAIT_FOR_FINISH = InputManager.INJECT_INPUT_EVENT_MODE_WAIT_FOR_FINISH;

    private static final boolean USE_ANDROID_15_DISPLAY_POWER = false;

    private Device() {
    }

    public static String getDeviceName() {
        return Build.MODEL;
    }

    public static boolean supportsInputEvents(int displayId) {
        return displayId == 0 || Build.VERSION.SDK_INT >= AndroidVersions.API_29_ANDROID_10;
    }

    public static boolean injectEvent(InputEvent event, int displayId, int injectMode) {
        if (!supportsInputEvents(displayId)) {
            throw new AssertionError("Could not inject input event if !supportsInputEvents()");
        }

        if (displayId != 0 && !InputManager.setDisplayId(event, displayId)) {
            return false;
        }

        return ServiceManager.getInputManager().injectInputEvent(event, injectMode);
    }

    public static boolean injectKeyEvent(int action, int keyCode, int repeat, int metaState, int displayId, int injectMode) {
        long now = SystemClock.uptimeMillis();
        KeyEvent event = new KeyEvent(now, now, action, keyCode, repeat, metaState, KeyCharacterMap.VIRTUAL_KEYBOARD, 0, 0,
                InputDevice.SOURCE_KEYBOARD);
        return injectEvent(event, displayId, injectMode);
    }

    public static boolean pressReleaseKeycode(int keyCode, int displayId, int injectMode) {
        return injectKeyEvent(KeyEvent.ACTION_DOWN, keyCode, 0, 0, displayId, injectMode)
                && injectKeyEvent(KeyEvent.ACTION_UP, keyCode, 0, 0, displayId, injectMode);
    }

    public static boolean isScreenOn(int displayId) {
        assert displayId != DISPLAY_ID_NONE;
        return ServiceManager.getPowerManager().isScreenOn(displayId);
    }

    public static void keepActive(int displayId) {
        assert displayId != DISPLAY_ID_NONE;
        ServiceManager.getPowerManager().userActivity(displayId);
    }

    public static void expandNotificationPanel() {
        ServiceManager.getStatusBarManager().expandNotificationsPanel();
    }

    public static void expandSettingsPanel() {
        ServiceManager.getStatusBarManager().expandSettingsPanel();
    }

    public static void collapsePanels() {
        ServiceManager.getStatusBarManager().collapsePanels();
    }

    public static String getClipboardText() {
        ClipboardManager clipboard = ServiceManager.getClipboardManager();
        if (clipboard == null) {
            return null;
        }
        CharSequence text = clipboard.getText();
        return text == null ? null : text.toString();
    }

    public static boolean setClipboardText(String text) {
        ClipboardManager clipboard = ServiceManager.getClipboardManager();
        if (clipboard == null) {
            return false;
        }

        String current = getClipboardText();
        if (current != null && current.equals(text)) {
            return false;
        }

        return clipboard.setText(text);
    }

    public static boolean setDisplayPower(int displayId, boolean on) {
        assert displayId != DISPLAY_ID_NONE;

        if (USE_ANDROID_15_DISPLAY_POWER && Build.VERSION.SDK_INT >= AndroidVersions.API_35_ANDROID_15) {
            return ServiceManager.getDisplayManager().requestDisplayPower(displayId, on);
        }

        boolean multiDisplay = Build.VERSION.SDK_INT >= AndroidVersions.API_29_ANDROID_10;
        if (multiDisplay && Build.VERSION.SDK_INT >= AndroidVersions.API_34_ANDROID_14 && Build.BRAND.equalsIgnoreCase("honor")
                && SurfaceControl.hasGetBuildInDisplayMethod()) {
            multiDisplay = false;
        }

        int mode = on ? POWER_MODE_NORMAL : POWER_MODE_OFF;
        if (multiDisplay) {
            boolean useDisplayControl =
                    Build.VERSION.SDK_INT >= AndroidVersions.API_34_ANDROID_14 && !SurfaceControl.hasGetPhysicalDisplayIdsMethod();

            long[] displayIds = useDisplayControl ? DisplayControl.getPhysicalDisplayIds() : SurfaceControl.getPhysicalDisplayIds();
            if (displayIds == null) {
                Ln.e("Could not get physical display ids");
                return false;
            }

            boolean allOk = true;
            for (long physicalDisplayId : displayIds) {
                IBinder token = useDisplayControl
                        ? DisplayControl.getPhysicalDisplayToken(physicalDisplayId)
                        : SurfaceControl.getPhysicalDisplayToken(physicalDisplayId);
                allOk &= SurfaceControl.setDisplayPowerMode(token, mode);
            }
            return allOk;
        }

        IBinder token = SurfaceControl.getBuiltInDisplay();
        if (token == null) {
            Ln.e("Could not get built-in display");
            return false;
        }
        return SurfaceControl.setDisplayPowerMode(token, mode);
    }

    public static boolean powerOffScreen(int displayId) {
        assert displayId != DISPLAY_ID_NONE;
        return !isScreenOn(displayId) || pressReleaseKeycode(KeyEvent.KEYCODE_POWER, displayId, INJECT_MODE_ASYNC);
    }

    public static void rotateDevice(int displayId) {
        assert displayId != DISPLAY_ID_NONE;

        WindowManager wm = ServiceManager.getWindowManager();
        boolean accelerometerRotation = !wm.isRotationFrozen(displayId);

        int newRotation = (getCurrentRotation(displayId) & 1) ^ 1;
        Ln.i("Device rotation requested: " + (newRotation == 0 ? "portrait" : "landscape"));
        wm.freezeRotation(displayId, newRotation);

        if (accelerometerRotation) {
            wm.thawRotation(displayId);
        }
    }

    private static int getCurrentRotation(int displayId) {
        assert displayId != DISPLAY_ID_NONE;

        if (displayId == 0) {
            return ServiceManager.getWindowManager().getRotation();
        }

        return ServiceManager.getDisplayManager().getDisplayInfo(displayId).getRotation();
    }

    public static List<DeviceApp> listApps() {
        PackageManager pm = FakeContext.get().getPackageManager();
        List<DeviceApp> apps = new ArrayList<>();
        for (ApplicationInfo appInfo : getLaunchableApps(pm)) {
            apps.add(toApp(pm, appInfo));
        }
        return apps;
    }

    @SuppressLint("QueryPermissionsNeeded")
    private static List<ApplicationInfo> getLaunchableApps(PackageManager pm) {
        List<ApplicationInfo> apps = new ArrayList<>();
        for (ApplicationInfo appInfo : pm.getInstalledApplications(PackageManager.GET_META_DATA)) {
            if (appInfo.enabled && getLaunchIntent(pm, appInfo.packageName) != null) {
                apps.add(appInfo);
            }
        }
        return apps;
    }

    public static Intent getLaunchIntent(PackageManager pm, String packageName) {
        Intent intent = pm.getLaunchIntentForPackage(packageName);
        return intent != null ? intent : pm.getLeanbackLaunchIntentForPackage(packageName);
    }

    private static DeviceApp toApp(PackageManager pm, ApplicationInfo appInfo) {
        return new DeviceApp(appInfo.packageName, pm.getApplicationLabel(appInfo).toString(),
                (appInfo.flags & ApplicationInfo.FLAG_SYSTEM) != 0);
    }

    @SuppressLint("QueryPermissionsNeeded")
    public static DeviceApp findByPackageName(String packageName) {
        PackageManager pm = FakeContext.get().getPackageManager();
        for (ApplicationInfo appInfo : pm.getInstalledApplications(PackageManager.GET_META_DATA)) {
            if (packageName.equals(appInfo.packageName)) {
                return toApp(pm, appInfo);
            }
        }
        return null;
    }

    @SuppressLint("QueryPermissionsNeeded")
    public static List<DeviceApp> findByName(String searchName) {
        String search = searchName.toLowerCase(Locale.getDefault());
        PackageManager pm = FakeContext.get().getPackageManager();
        List<DeviceApp> apps = new ArrayList<>();
        for (ApplicationInfo appInfo : getLaunchableApps(pm)) {
            String name = pm.getApplicationLabel(appInfo).toString();
            if (name.toLowerCase(Locale.getDefault()).startsWith(search)) {
                apps.add(toApp(pm, appInfo));
            }
        }
        return apps;
    }

    public static void startApp(String spec, int displayId) {
        boolean forceStop = spec.startsWith("+");
        if (forceStop) {
            spec = spec.substring(1);
        }

        int userId = -1;
        int at = spec.indexOf('@');
        if (at > 0) {
            try {
                userId = Integer.parseInt(spec.substring(at + 1));
            } catch (NumberFormatException e) {
                Ln.w("Invalid user id in app spec \"" + spec + "\"");
                return;
            }
            spec = spec.substring(0, at);
        }

        if (userId >= 0) {
            startAppAsUser(spec, displayId, userId, forceStop);
            return;
        }

        DeviceApp app;
        if (spec.startsWith("?")) {
            String search = spec.substring(1);
            List<DeviceApp> apps = findByName(search);
            if (apps.isEmpty()) {
                Ln.w("No app found for name \"" + search + "\"");
                return;
            }
            if (apps.size() > 1) {
                Ln.w(LogUtils.buildAppListMessage("No unique app found for name \"" + search + "\":", apps));
                return;
            }
            app = apps.get(0);
        } else {
            app = findByPackageName(spec);
            if (app == null) {
                Ln.w("No app found for package \"" + spec + "\"");
                return;
            }
        }

        Ln.i("Starting app \"" + app.getName() + "\" [" + app.getPackageName() + "] on display " + displayId + "...");
        startApp(app.getPackageName(), displayId, forceStop);
    }

    public static void startApp(String packageName, int displayId, boolean forceStop) {
        PackageManager pm = FakeContext.get().getPackageManager();
        Intent launchIntent = getLaunchIntent(pm, packageName);
        if (launchIntent == null) {
            Ln.w("Cannot create launch intent for app " + packageName);
            return;
        }

        launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);

        Bundle options = null;
        if (Build.VERSION.SDK_INT >= AndroidVersions.API_26_ANDROID_8_0) {
            ActivityOptions launchOptions = ActivityOptions.makeBasic();
            launchOptions.setLaunchDisplayId(displayId);
            options = launchOptions.toBundle();
        }

        ActivityManager am = ServiceManager.getActivityManager();
        if (forceStop) {
            am.forceStopPackage(packageName);
        }
        am.startActivity(launchIntent, options);
    }

    private static void startAppAsUser(String packageName, int displayId, int userId, boolean forceStop) {
        String component = resolveLauncherComponent(packageName, userId);
        if (component == null) {
            Ln.w("No launcher activity for \"" + packageName + "\" on user " + userId);
            return;
        }

        if (forceStop) {
            exec("am", "force-stop", "--user", String.valueOf(userId), packageName);
        }

        Ln.i("Starting app [" + packageName + "] on display " + displayId + " as user " + userId + "...");
        exec("am", "start", "--user", String.valueOf(userId), "--display", String.valueOf(displayId), "-n", component);
    }

    private static String resolveLauncherComponent(String packageName, int userId) {
        String output = exec("cmd", "package", "resolve-activity", "--brief", "--user", String.valueOf(userId), "-a", Intent.ACTION_MAIN,
                "-c", Intent.CATEGORY_LAUNCHER, packageName);
        if (output == null) {
            return null;
        }
        for (String line : output.split("\\R")) {
            String trimmed = line.trim();
            if (trimmed.contains("/")) {
                return trimmed;
            }
        }
        return null;
    }

    private static String exec(String... command) {
        try {
            Process process = new ProcessBuilder(command).redirectErrorStream(true).start();
            String output = IO.readAll(process.getInputStream());
            process.waitFor();
            return output;
        } catch (Exception e) {
            Ln.e("exec failed: " + String.join(" ", command), e);
            return null;
        }
    }

    public static void sendBroadcast(Intent intent) {
        ServiceManager.getActivityManager().sendBroadcast(intent);
    }
}
