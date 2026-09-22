package com.touchmirror.engine.util;

import java.io.IOException;
import java.util.Arrays;

public final class Command {
    private Command() {
    }

    public static String execReadOutput(String... cmd) throws IOException, InterruptedException {
        Process process = Runtime.getRuntime().exec(cmd);
        String output = IO.readAll(process.getInputStream());
        int exitCode = process.waitFor();
        if (exitCode != 0) {
            throw new IOException("Command " + Arrays.toString(cmd) + " exited with code " + exitCode);
        }
        return output;
    }
}
