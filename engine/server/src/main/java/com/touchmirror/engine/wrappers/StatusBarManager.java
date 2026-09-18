package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.util.Ln;

import android.os.IInterface;

import java.lang.reflect.Method;

public final class StatusBarManager {

    private final IInterface manager;
    private Method expandNotificationsPanelMethod;
    private boolean expandNotificationPanelMethodCustomVersion;
    private Method expandSettingsPanelMethod;
    private boolean expandSettingsPanelMethodNewVersion = true;
    private Method collapsePanelsMethod;

    static StatusBarManager create() {
        IInterface manager = ServiceManager.getService("statusbar", "com.android.internal.statusbar.IStatusBarService");
        return new StatusBarManager(manager);
    }

    private StatusBarManager(IInterface manager) {
        this.manager = manager;
    }

    private Method getExpandNotificationsPanelMethod() throws NoSuchMethodException {
        if (expandNotificationsPanelMethod == null) {
            try {
                expandNotificationsPanelMethod = manager.getClass().getMethod("expandNotificationsPanel");
            } catch (NoSuchMethodException e) {
                expandNotificationsPanelMethod = manager.getClass().getMethod("expandNotificationsPanel", int.class);
                expandNotificationPanelMethodCustomVersion = true;
            }
        }
        return expandNotificationsPanelMethod;
    }

    private Method getExpandSettingsPanel() throws NoSuchMethodException {
        if (expandSettingsPanelMethod == null) {
            try {
                expandSettingsPanelMethod = manager.getClass().getMethod("expandSettingsPanel", String.class);
            } catch (NoSuchMethodException e) {
                expandSettingsPanelMethod = manager.getClass().getMethod("expandSettingsPanel");
                expandSettingsPanelMethodNewVersion = false;
            }
        }
        return expandSettingsPanelMethod;
    }

    private Method getCollapsePanelsMethod() throws NoSuchMethodException {
        if (collapsePanelsMethod == null) {
            collapsePanelsMethod = manager.getClass().getMethod("collapsePanels");
        }
        return collapsePanelsMethod;
    }

    public void expandNotificationsPanel() {
        try {
            Method method = getExpandNotificationsPanelMethod();
            if (expandNotificationPanelMethodCustomVersion) {
                method.invoke(manager, 0);
            } else {
                method.invoke(manager);
            }
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }

    public void expandSettingsPanel() {
        try {
            Method method = getExpandSettingsPanel();
            if (expandSettingsPanelMethodNewVersion) {
                method.invoke(manager, (Object) null);
            } else {
                method.invoke(manager);
            }
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }

    public void collapsePanels() {
        try {
            Method method = getCollapsePanelsMethod();
            method.invoke(manager);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }
}
