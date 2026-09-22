package com.touchmirror.engine.control;

import com.touchmirror.engine.util.Ln;

import java.io.IOException;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.BlockingQueue;

public final class DeviceMessageSender {

    private final ControlChannel controlChannel;
    private final BlockingQueue<DeviceMessage> queue = new ArrayBlockingQueue<>(16);

    private Thread thread;

    public DeviceMessageSender(ControlChannel controlChannel) {
        this.controlChannel = controlChannel;
    }

    public void send(DeviceMessage msg) {
        if (!queue.offer(msg)) {
            Ln.w("Device message dropped: " + msg.getType());
        }
    }

    public void start() {
        thread = new Thread(() -> {
            try {
                while (!Thread.currentThread().isInterrupted()) {
                    controlChannel.send(queue.take());
                }
            } catch (IOException | InterruptedException e) {
            } finally {
                Ln.d("Device message sender stopped");
            }
        }, "control-send");
        thread.start();
    }

    public void stop() {
        if (thread != null) {
            thread.interrupt();
        }
    }

    public void join() throws InterruptedException {
        if (thread != null) {
            thread.join();
        }
    }
}
