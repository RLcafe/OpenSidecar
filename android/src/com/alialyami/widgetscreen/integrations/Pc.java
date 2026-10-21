package com.alialyami.widgetscreen.integrations;

import android.content.Context;
import android.os.SystemClock;

import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
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
    private String host, token, name = "";
    private int port = PcLink.PORT;
    private int generation;
    private boolean polling;
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
            if (json != null) {
                Map<String, Object> saved = Json.obj(Json.parse(json));
                host = Json.field(saved, "host"); token = Json.field(saved, "token");
                name = Json.field(saved, "name") == null ? "" : Json.field(saved, "name");
                port = Json.has(saved, "port") ? (int) Json.number(saved, "port") : PcLink.PORT;
            }
        } catch (Exception e) { startupWarning = "The PC pairing could not be restored. Pair again in Settings → PC."; }
    }

    public boolean paired() { return host != null && token != null; }
    public String name() { return name.isEmpty() ? (host == null ? "your PC" : host) : name; }
    public String address() { return host == null ? "" : host + (port == PcLink.PORT ? "" : ":" + port); }
    public boolean online() { return paired() && problem == null && state != null; }
    private PcLink link() { return new PcLink(host, port, token); }

    // Pairs with the code the PC shows, then remembers the PC.
    public void pair(final String pcHost, final int pcPort, final String code, final String device, Async.Done<String> done, Async.Failed failed) {
        Async.run(sender, new Async.Work<String>() {
            @Override public String run() throws Exception {
                PcLink fresh = new PcLink(pcHost, pcPort, null);
                String pcName = Json.field(fresh.hello(), "name");
                String newToken = fresh.pair(code, device);
                SecureStore.write(file, Json.write(Json.map("host", pcHost, "port", pcPort, "token", newToken, "name", pcName == null ? "" : pcName)));
                return pcName == null ? pcHost : pcName;
            }
        }, new Async.Done<String>() {
            @Override public void ok(String pcName) {
                host = pcHost; port = pcPort; name = pcName; problem = null; unpaired = false; state = null; generation++;
                done.ok(pcName);
            }
        }, failed);
    }

    public void forget() throws Exception {
        SecureStore.write(file, "{}");
        host = null; token = null; name = ""; state = null; problem = null; unpaired = false; generation++;
    }

    public void discover(Async.Done<List<PcLink.Found>> done) {
        Async.run(new Async.Work<List<PcLink.Found>>() { @Override public List<PcLink.Found> run() { return PcLink.discover(1500); } }, done, null);
    }

    // One state reading at a time; a reading that was asked for while another runs is skipped.
    public void poll(final List<String> parts, final Runnable changed) {
        if (!paired() || polling) return;
        polling = true;
        final int version = generation;
        final PcLink current = link();
        Async.run(poller, new Async.Work<PcState>() {
            @Override public PcState run() throws Exception { return current.state(parts); }
        }, new Async.Done<PcState>() {
            @Override public void ok(PcState next) {
                polling = false;
                if (version != generation) return;
                state = next; stateAt = SystemClock.elapsedRealtime(); problem = null; unpaired = false;
                changed.run();
            }
        }, new Async.Failed() {
            @Override public void error(Exception error) {
                polling = false;
                if (version != generation) return;
                PcLink.Failure failure = error instanceof PcLink.Failure ? (PcLink.Failure) error : null;
                unpaired = failure != null && failure.unpaired();
                problem = failure != null ? failure.getMessage() : "Can't reach the PC.";
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
