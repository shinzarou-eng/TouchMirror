package com.touchmirror.engine.wrappers;

import com.touchmirror.engine.util.Ln;

import android.content.res.Configuration;
import android.os.Parcel;
import android.os.RemoteException;
import android.view.IDisplayWindowListener;

public class DisplayWindowListener extends IDisplayWindowListener.Stub {
    @Override
    public void onDisplayAdded(int displayId) {
    }

    @Override
    public void onDisplayConfigurationChanged(int displayId, Configuration newConfig) {
    }

    @Override
    public void onDisplayRemoved(int displayId) {
    }

    @Override
    public boolean onTransact(int code, Parcel data, Parcel reply, int flags) throws RemoteException {
        try {
            return super.onTransact(code, data, reply, flags);
        } catch (AbstractMethodError e) {
            Ln.v("Ignoring AbstractMethodError: " + e.getMessage());
            reply.writeNoException();
            return true;
        }
    }
}
