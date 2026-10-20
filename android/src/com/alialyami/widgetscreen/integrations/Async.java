package com.alialyami.widgetscreen.integrations;

import android.os.Handler;
import android.os.Looper;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ThreadFactory;

// Background work with results delivered on the main thread. Network calls never run on the UI thread.
public final class Async {
    public interface Work<T> { T run() throws Exception; }
    public interface Done<T> { void ok(T value); }
    public interface Failed { void error(Exception error); }

    public static final Handler MAIN = new Handler(Looper.getMainLooper());
    public static final ExecutorService IO = Executors.newFixedThreadPool(3, threads("widget-io"));

    public static ThreadFactory threads(final String name) {
        return new ThreadFactory() {
            private int count;
            @Override public synchronized Thread newThread(Runnable runnable) {
                Thread thread = new Thread(runnable, name + "-" + (++count));
                thread.setDaemon(true);
                return thread;
            }
        };
    }

    public static <T> void run(ExecutorService pool, final Work<T> work, final Done<T> done, final Failed failed) {
        pool.execute(new Runnable() {
            @Override public void run() {
                try {
                    final T value = work.run();
                    MAIN.post(new Runnable() { @Override public void run() { if (done != null) done.ok(value); } });
                } catch (final Exception error) {
                    MAIN.post(new Runnable() { @Override public void run() { if (failed != null) failed.error(error); } });
                }
            }
        });
    }

    public static <T> void run(Work<T> work, Done<T> done, Failed failed) { run(IO, work, done, failed); }
}
