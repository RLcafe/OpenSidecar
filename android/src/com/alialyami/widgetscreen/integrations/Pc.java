package com.alialyami.widgetscreen.integrations;

import android.content.Context;
import android.os.SystemClock;

import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcPairing;
import com.alialyami.widgetscreen.core.PcState;

import java.io.File;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

// The PC this phone controls: the saved pairing (encrypted with the Android Keystore in pc-link.dat), the last
// state the PC sent, and the threads that talk to it. Polling and commands use separate threads, so a tap never
// waits behind a slow reading.
public final class Pc {
    private final File file;
    private final ExecutorService poller = Executors.newSingleThreadExecutor(Async.threads("pc-state"));
    private final ExecutorService sender = Executors.newSingleThreadExecutor(Async.threads("pc-command"));
    private PcPairing pairing = PcPairing.none();
    private int generation;
    private boolean polling;
    // Readings that failed in a row, and when the next unforced reading may go (PcLink.retryDelayMs).
    private int failures;
    private long retryAt;
    public PcState state;
    // When the last state arrived (elapsedRealtime), so the focus countdown keeps moving between readings.
    public long stateAt;
    // Null while the PC answers; otherwise why it doesn't.
    public String problem;
    public boolean unpaired;
    public String startupWarning;

    public Pc(Context context) {
        file = new File(context.getFilesDir(), "pc-link.dat");
        try {
            String json = SecureStore.read(file);
            if (json != null) pairing = PcPairing.fromJson(json);
        } catch (Exception e) { startupWarning = "The PC pairing could not be restored. Pair again in Settings → PC."; }
    }

    public boolean paired() { return pairing.paired(); }
    public String name() { return !pairing.name.isEmpty() ? pairing.name : pairing.host == null ? "your PC" : pairing.host; }
    public String address() { return pairing.host == null ? "" : pairing.host + (pairing.port == PcLink.PORT ? "" : ":" + pairing.port); }
    public boolean online() { return paired() && problem == null && state != null; }
    private PcLink link() { return pairing.link(); }

    // Pairs with the code the PC shows, then remembers the PC.
    public void pair(final String pcHost, final int pcPort, final String code, final String device, final Async.Done<String> done, Async.Failed failed) {
        Async.run(sender, new Async.Work<PcPairing>() {
            @Override public PcPairing run() throws Exception {
                PcPairing paired = PcPairing.pair(pcHost, pcPort, code, device);
                SecureStore.write(file, paired.toJson());
                return paired;
            }
        }, new Async.Done<PcPairing>() {
            @Override public void ok(PcPairing paired) {
                pairing = paired; problem = null; unpaired = false; state = null; polling = false; generation++; failures = 0; retryAt = 0;
                done.ok(paired.name);
            }
        }, failed);
    }

    public void forget() throws Exception {
        SecureStore.write(file, "{}");
        pairing = PcPairing.none(); state = null; problem = null; unpaired = false; generation++; failures = 0; retryAt = 0;
    }

    public int failures() { return failures; }

    public void discover(Async.Done<List<PcLink.Found>> done) {
        Async.run(new Async.Work<List<PcLink.Found>>() { @Override public List<PcLink.Found> run() { return PcLink.discover(1500); } }, done, null);
    }

    // One state reading at a time; a reading that was asked for while another runs is skipped. While the PC is
    // away, unforced readings back off (5 s, 15 s, then a minute); a forced one (a tap, a settings change) goes now.
    public void poll(final List<String> parts, final Runnable changed) { poll(parts, changed, false); }
    public void poll(final List<String> parts, final Runnable changed, boolean force) {
        if (!paired() || polling) return;
        if (!force && SystemClock.elapsedRealtime() < retryAt) return;
        polling = true;
        final int version = generation;
        final PcLink current = link();
        Async.run(poller, new Async.Work<PcState>() {
            @Override public PcState run() throws Exception { return current.state(parts); }
        }, new Async.Done<PcState>() {
            @Override public void ok(PcState next) {
                polling = false;
                if (version != generation) return;
                state = next; stateAt = SystemClock.elapsedRealtime(); problem = null; unpaired = false; failures = 0; retryAt = 0;
                changed.run();
            }
        }, new Async.Failed() {
            @Override public void error(Exception error) {
                polling = false;
                if (version != generation) return;
                PcLink.Failure failure = error instanceof PcLink.Failure ? (PcLink.Failure) error : null;
                unpaired = failure != null && failure.unpaired();
                problem = failure != null ? failure.getMessage() : "Can't reach the PC.";
                failures++; retryAt = SystemClock.elapsedRealtime() + PcLink.retryDelayMs(failures);
                changed.run();
            }
        });
    }

    public interface Call<T> { T run(PcLink link) throws Exception; }

    // Commands, the Spotify proxy and artwork, in order on their own thread.
    public <T> void send(final Call<T> call, Async.Done<T> done, Async.Failed failed) {
        if (!paired()) { if (failed != null) failed.error(new PcLink.Failure(401, "Pair this phone with your PC in Settings → PC.", false)); return; }
        final PcLink current = link();
        Async.run(sender, new Async.Work<T>() { @Override public T run() throws Exception { return call.run(current); } }, done, failed);
    }

    public void command(final Map<String, Object> body, Async.Done<String> done, Async.Failed failed) {
        send(new Call<String>() { @Override public String run(PcLink link) throws Exception { return link.command(body); } }, done, failed);
    }

    public static String message(Exception error) {
        if (error instanceof PcLink.Failure || error instanceof IllegalStateException || error instanceof IllegalArgumentException) return error.getMessage();
        return "Something went wrong talking to the PC. Try again.";
    }
}
