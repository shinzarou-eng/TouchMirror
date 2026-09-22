package com.touchmirror.engine.device;

import com.touchmirror.engine.Protocol;
import com.touchmirror.engine.control.ControlChannel;
import com.touchmirror.engine.util.StringUtils;

import android.net.LocalServerSocket;
import android.net.LocalSocket;
import android.net.LocalSocketAddress;

import java.io.Closeable;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

public final class DesktopConnection implements Closeable {

    private static final String SOCKET_NAME_PREFIX = "touchmirror";

    private final LocalSocket socket;
    private final Muxer muxer;
    private final ControlChannel controlChannel;

    private DesktopConnection(LocalSocket socket, boolean control) throws IOException {
        this.socket = socket;
        this.muxer = new Muxer(socket);
        this.controlChannel = control ? new ControlChannel(muxer) : null;
    }

    private static LocalSocket connect(String abstractName) throws IOException {
        LocalSocket localSocket = new LocalSocket();
        localSocket.connect(new LocalSocketAddress(abstractName));
        return localSocket;
    }

    private static String getSocketName(int scid) {
        if (scid == -1) {
            return SOCKET_NAME_PREFIX;
        }

        return SOCKET_NAME_PREFIX + String.format("_%08x", scid);
    }

    public static DesktopConnection open(int scid, boolean tunnelForward, boolean control) throws IOException {
        String socketName = getSocketName(scid);

        LocalSocket socket;
        if (tunnelForward) {
            try (LocalServerSocket localServerSocket = new LocalServerSocket(socketName)) {
                socket = localServerSocket.accept();
            }
        } else {
            socket = connect(socketName);
        }

        return new DesktopConnection(socket, control);
    }

    public void shutdown() throws IOException {
        socket.shutdownInput();
        socket.shutdownOutput();
    }

    public void close() throws IOException {
        muxer.close();
    }

    public void sendHello(String deviceName, int caps) throws IOException {
        byte[] deviceNameBytes = deviceName.getBytes(StandardCharsets.UTF_8);
        int len = StringUtils.getUtf8TruncationIndex(deviceNameBytes, Protocol.MAX_DEVICE_NAME_LENGTH);

        byte[] hello = new byte[11 + len];
        System.arraycopy(Protocol.MAGIC, 0, hello, 0, 4);
        hello[4] = (byte) (Protocol.VERSION >> 8);
        hello[5] = (byte) Protocol.VERSION;
        hello[6] = (byte) (caps >> 24);
        hello[7] = (byte) (caps >> 16);
        hello[8] = (byte) (caps >> 8);
        hello[9] = (byte) caps;
        hello[10] = (byte) len;
        System.arraycopy(deviceNameBytes, 0, hello, 11, len);

        muxer.write(Protocol.CHAN_SESSION, hello);
    }

    public Muxer getMuxer() {
        return muxer;
    }

    public ControlChannel getControlChannel() {
        return controlChannel;
    }
}
