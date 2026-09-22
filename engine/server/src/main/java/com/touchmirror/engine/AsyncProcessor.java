package com.touchmirror.engine;

public interface AsyncProcessor {

    interface TerminationListener {
        void onTerminated(boolean fatalError);
    }

    void start(TerminationListener listener);

    void stop();

    void join() throws InterruptedException;
}
