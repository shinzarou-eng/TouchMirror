package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.util.Ln;
import com.touchmirror.engine.util.Reflect;

import android.os.IInterface;

import java.lang.reflect.Method;

public final class StatusBarManager {

    private final IInterface manager;

    private Method expandNotificationsPanelMethod;
    private boolean expandNotificationsPanelTakesInt;
    private Method expandSettingsPanelMethod;
    private boolean expandSettingsPanelTakesString;
    private Method collapsePanelsMethod;

    static StatusBarManager create() {
        return new StatusBarManager(ServiceManager.getService("statusbar", "com.android.internal.statusbar.IStatusBarService"));
    }

    private StatusBarManager(IInterface manager) {
        this.manager = manager;
    }

    private Method resolveExpandNotificationsPanel() throws NoSuchMethodException {
        if (expandNotificationsPanelMethod == null) {
            expandNotificationsPanelMethod = Reflect.lookup(manager.getClass(), "expandNotificationsPanel");
            if (expandNotificationsPanelMethod == null) {
                expandNotificationsPanelMethod = Reflect.lookupOrThrow(manager.getClass(), "expandNotificationsPanel", int.class);
                expandNotificationsPanelTakesInt = true;
            }
        }
        return expandNotificationsPanelMethod;
    }

    private Method resolveExpandSettingsPanel() throws NoSuchMethodException {
        if (expandSettingsPanelMethod == null) {
            expandSettingsPanelMethod = Reflect.lookup(manager.getClass(), "expandSettingsPanel", String.class);
            if (expandSettingsPanelMethod == null) {
                expandSettingsPanelMethod = Reflect.lookupOrThrow(manager.getClass(), "expandSettingsPanel");
            } else {
                expandSettingsPanelTakesString = true;
            }
        }
        return expandSettingsPanelMethod;
    }

    public void expandNotificationsPanel() {
        try {
            Method method = resolveExpandNotificationsPanel();
            if (expandNotificationsPanelTakesInt) {
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
            Method method = resolveExpandSettingsPanel();
            if (expandSettingsPanelTakesString) {
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
            if (collapsePanelsMethod == null) {
                collapsePanelsMethod = Reflect.lookupOrThrow(manager.getClass(), "collapsePanels");
            }
            collapsePanelsMethod.invoke(manager);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
        }
    }
}
