package com.touchmirror.engine.uhid;

import com.touchmirror.engine.util.Binary;
import com.touchmirror.engine.util.Ln;

import android.os.ParcelFileDescriptor;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

public final class UhidDevice {

    private static final int UHID_DESTROY = 1;
    private static final int UHID_START = 2;
    private static final int UHID_STOP = 3;
    private static final int UHID_OPEN = 4;
    private static final int UHID_CLOSE = 5;
    private static final int UHID_OUTPUT = 8;
    private static final int UHID_OUTPUT_EV = 9;
    private static final int UHID_GET_REPORT = 11;
    private static final int UHID_SET_REPORT = 12;
    private static final int UHID_CREATE2 = 13;
    private static final int UHID_INPUT2 = 14;
    private static final int UHID_SET_REPORT_REPLY = 15;
    private static final int UHID_GET_REPORT_REPLY = 16;

    private static final int BUS_USB = 0x03;
    private static final int MAX_REPORT_DATA = 4096;
    private static final int EVENT_BUF_SIZE = 4300;
    private static final int EIO = 5;

    public interface OutputListener {
        void onOutput(int id, byte[] data);
    }

    private final int id;
    private OutputListener listener;

    private ParcelFileDescriptor pfd;
    private FileInputStream in;
    private FileOutputStream out;
    private Thread readerThread;
    private volatile boolean closed;

    public UhidDevice(int id) {
        this.id = id;
    }

    public void setOutputListener(OutputListener listener) {
        this.listener = listener;
    }

    public synchronized void open(String name, byte[] reportDesc, int vendorId, int productId) throws IOException {
        pfd = ParcelFileDescriptor.open(new File("/dev/uhid"), ParcelFileDescriptor.MODE_READ_WRITE);
        try {
            in = new FileInputStream(pfd.getFileDescriptor());
            out = new FileOutputStream(pfd.getFileDescriptor());
            writeCreate2(name, reportDesc, vendorId, productId);
        } catch (IOException e) {
            closeFd();
            throw e;
        }
        readerThread = new Thread(this::readLoop, "uhid-read-" + id);
        readerThread.setDaemon(true);
        readerThread.start();
    }

    private void writeCreate2(String name, byte[] reportDesc, int vendorId, int productId) throws IOException {
        int rdSize = Math.min(reportDesc.length, MAX_REPORT_DATA);
        byte[] ev = new byte[280 + rdSize];
        Binary.writeInt32Le(ev, 0, UHID_CREATE2);
        putString(ev, 4, 128, name);
        putString(ev, 132, 64, "TouchMirror");
        Binary.writeInt16Le(ev, 260, rdSize);
        Binary.writeInt16Le(ev, 262, BUS_USB);
        Binary.writeInt32Le(ev, 264, vendorId);
        Binary.writeInt32Le(ev, 268, productId);
        Binary.writeInt32Le(ev, 272, 1);
        System.arraycopy(reportDesc, 0, ev, 280, rdSize);
        out.write(ev);
    }

    private static void putString(byte[] ev, int offset, int maxLen, String value) {
        if (value == null) {
            return;
        }
        byte[] raw = value.getBytes(StandardCharsets.UTF_8);
        int len = Math.min(raw.length, maxLen - 1);
        System.arraycopy(raw, 0, ev, offset, len);
    }

    public synchronized void sendInput(byte[] data) throws IOException {
        if (out == null || data.length > MAX_REPORT_DATA) {
            return;
        }
        byte[] ev = new byte[6 + data.length];
        Binary.writeInt32Le(ev, 0, UHID_INPUT2);
        Binary.writeInt16Le(ev, 4, data.length);
        System.arraycopy(data, 0, ev, 6, data.length);
        out.write(ev);
    }

    private void readLoop() {
        byte[] buf = new byte[EVENT_BUF_SIZE];
        FileInputStream stream = in;
        while (!closed && stream != null) {
            try {
                int n = stream.read(buf);
                if (n < 4) {
                    break;
                }
                int type = Binary.readInt32Le(buf, 0);
                switch (type) {
                    case UHID_OUTPUT:
                        int size = Math.min(Binary.readUInt16Le(buf, 4100), MAX_REPORT_DATA);
                        OutputListener l = listener;
                        if (l != null && size > 0) {
                            l.onOutput(id, Arrays.copyOfRange(buf, 4, 4 + size));
                        }
                        break;
                    case UHID_GET_REPORT:
                        sendGetReportReply(Binary.readInt64Le(buf, 4));
                        break;
                    case UHID_SET_REPORT:
                        sendSetReportReply(Binary.readInt64Le(buf, 4));
                        break;
                    case UHID_START:
                    case UHID_STOP:
                    case UHID_OPEN:
                    case UHID_CLOSE:
                    case UHID_OUTPUT_EV:
                        break;
                    default:
                        Ln.w("Unknown uhid event: " + type);
                        break;
                }
            } catch (IOException e) {
                break;
            }
        }
        Ln.d("uhid reader stopped (id=" + id + ")");
    }

    private synchronized void sendGetReportReply(long reqId) throws IOException {
        if (out == null) {
            return;
        }
        byte[] ev = new byte[16];
        Binary.writeInt32Le(ev, 0, UHID_GET_REPORT_REPLY);
        Binary.writeInt64Le(ev, 4, reqId);
        Binary.writeInt16Le(ev, 12, EIO);
        out.write(ev);
    }

    private synchronized void sendSetReportReply(long reqId) throws IOException {
        if (out == null) {
            return;
        }
        byte[] ev = new byte[14];
        Binary.writeInt32Le(ev, 0, UHID_SET_REPORT_REPLY);
        Binary.writeInt64Le(ev, 4, reqId);
        out.write(ev);
    }

    public synchronized void close() {
        if (closed) {
            return;
        }
        closed = true;
        if (out != null) {
            try {
                byte[] ev = new byte[4];
                Binary.writeInt32Le(ev, 0, UHID_DESTROY);
                out.write(ev);
            } catch (IOException e) {
            }
        }
        closeFd();
    }

    private void closeFd() {
        try {
            if (in != null) {
                in.close();
            }
        } catch (IOException e) {
        }
        try {
            if (out != null) {
                out.close();
            }
        } catch (IOException e) {
        }
        try {
            if (pfd != null) {
                pfd.close();
            }
        } catch (IOException e) {
        }
        in = null;
        out = null;
        pfd = null;
    }

    public static boolean isSupported() {
        try {
            ParcelFileDescriptor probe = ParcelFileDescriptor.open(new File("/dev/uhid"), ParcelFileDescriptor.MODE_READ_WRITE);
            probe.close();
            return true;
        } catch (Exception e) {
            return false;
        }
    }
}
