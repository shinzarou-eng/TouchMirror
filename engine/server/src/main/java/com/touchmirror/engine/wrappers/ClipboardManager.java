package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.FakeContext;

import android.content.ClipData;
import android.content.Context;

public final class ClipboardManager {

    private final android.content.ClipboardManager manager;

    static ClipboardManager create() {
        android.content.ClipboardManager manager =
                (android.content.ClipboardManager) FakeContext.get().getSystemService(Context.CLIPBOARD_SERVICE);
        return manager == null ? null : new ClipboardManager(manager);
    }

    private ClipboardManager(android.content.ClipboardManager manager) {
        this.manager = manager;
    }

    public CharSequence getText() {
        ClipData clip = manager.getPrimaryClip();
        if (clip == null || clip.getItemCount() == 0) {
            return null;
        }
        return clip.getItemAt(0).getText();
    }

    public boolean setText(CharSequence text) {
        manager.setPrimaryClip(ClipData.newPlainText(null, text));
        return true;
    }

    public void addPrimaryClipChangedListener(android.content.ClipboardManager.OnPrimaryClipChangedListener listener) {
        manager.addPrimaryClipChangedListener(listener);
    }
}
