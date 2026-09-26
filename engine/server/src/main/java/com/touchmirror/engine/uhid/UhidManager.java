package com.touchmirror.engine.uhid;

import java.io.IOException;
import java.util.HashMap;
import java.util.Map;

public final class UhidManager {

    private final Map<Integer, UhidDevice> devices = new HashMap<>();
    private final UhidDevice.OutputListener outputListener;

    public UhidManager(UhidDevice.OutputListener outputListener) {
        this.outputListener = outputListener;
    }

    public synchronized void create(int id, int vendor, int product, String name, byte[] reportDesc) throws IOException {
        destroy(id);
        UhidDevice device = new UhidDevice(id);
        device.setOutputListener(outputListener);
        device.open(name, reportDesc, vendor, product);
        devices.put(id, device);
    }

    public synchronized void input(int id, byte[] data) throws IOException {
        UhidDevice device = devices.get(id);
        if (device == null) {
            throw new IOException("Unknown uhid device: " + id);
        }
        device.sendInput(data);
    }

    public synchronized boolean has(int id) {
        return devices.containsKey(id);
    }

    public synchronized void destroy(int id) {
        UhidDevice device = devices.remove(id);
        if (device != null) {
            device.close();
        }
    }

    public synchronized void destroyAll() {
        for (UhidDevice device : devices.values()) {
            device.close();
        }
        devices.clear();
    }
}
