package com.alialyami.widgetscreen.integrations;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;

import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.Spotify;

import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

// The network half of the Windows SpotifyService. All Web API traffic runs on one thread: tokens refresh
// once, Retry-After pauses every view, and a disconnect invalidates results still in flight.
public final class SpotifyService {
    public static final class NoDeviceException extends IllegalStateException {
        public NoDeviceException() { super("No active Spotify device. Start a song in Spotify, then try again."); }
    }
    public static final class Cancelled extends IllegalStateException {
        public Cancelled() { super("Spotify request cancelled."); }
    }

    private static final long SIGN_IN_LIMIT_MS = 10 * 60000L;
    private final ExecutorService gate = Executors.newSingleThreadExecutor(Async.threads("spotify"));
    private final SecureRandom random = new SecureRandom();
    private final File file;
    private final SharedPreferences pending;
    private final Object lock = new Object();
    private volatile Spotify.Session session;
    private volatile int generation;
    private volatile long retryAtMs;
    public volatile String startupWarning;

    public SpotifyService(Context context) {
        file = new File(context.getFilesDir(), "spotify-session.dat");
        pending = context.getSharedPreferences("spotify-sign-in", Context.MODE_PRIVATE);
        Spotify.Session loaded = null;
        try { String json = SecureStore.read(file); loaded = json == null ? null : Spotify.Session.fromJson(json); }
        catch (Exception e) { startupWarning = "Spotify connection could not be restored. Connect again."; }
        session = loaded == null ? new Spotify.Session() : loaded;
    }

    public String clientId() { return session.clientId == null ? "" : session.clientId; }
    public boolean connected() { return session.connected(); }
    public boolean canControl() { return session.hasScope("user-modify-playback-state"); }
    public boolean canReadRecent() { return session.hasScope("user-read-recently-played"); }
    public int generation() { return generation; }

    public static String message(Exception error) {
        if (error instanceof Cancelled) return "Spotify request cancelled or timed out. Try again.";
        if (error instanceof IllegalStateException || error instanceof IllegalArgumentException) return error.getMessage();
        return "Could not reach Spotify. Check your connection and try again.";
    }

    // Sign-in, step 1: remember the PKCE values (they survive the app being closed while the browser is
    // in front) and return the address to open.
    public String beginSignIn(String clientId) {
        if (!Spotify.validClientId(clientId)) throw new IllegalStateException("Paste the 32-character Client ID from your Spotify Developer app.");
        String verifier = Spotify.randomValue(random), state = Spotify.randomValue(random);
        pending.edit().putString("clientId", clientId).putString("verifier", verifier).putString("state", state).putLong("started", System.currentTimeMillis()).commit();
        return Spotify.authorizeUrl(clientId, state, verifier);
    }

    public boolean signingIn() {
        long started = pending.getLong("started", 0);
        return pending.getString("state", null) != null && System.currentTimeMillis() - started < SIGN_IN_LIMIT_MS;
    }

    public void cancelSignIn() { pending.edit().clear().commit(); }

    // Sign-in, step 2: Spotify sent the browser back to widgetscreen://spotify-callback.
    public void completeSignIn(final String callback, Async.Done<Void> done, Async.Failed failed) {
        final String clientId = pending.getString("clientId", null), verifier = pending.getString("verifier", null), state = pending.getString("state", null);
        final boolean fresh = signingIn();
        Async.run(gate, new Async.Work<Void>() {
            @Override public Void run() throws Exception {
                if (state == null || !fresh) throw new IllegalStateException("This sign-in has expired. Tap Connect Spotify to try again.");
                String[] result = Spotify.readCallback(callback, state);
                if (result == null) throw new IllegalStateException("Unrecognized callback. Tap Connect Spotify to try again.");
                cancelSignIn();
                if (result[1] != null) throw new IllegalStateException("Spotify sign-in was declined. You can connect again when ready.");
                int version = generation;
                Map<String, String> form = new LinkedHashMap<String, String>();
                form.put("grant_type", "authorization_code"); form.put("code", result[0]); form.put("redirect_uri", Spotify.REDIRECT_URI);
                form.put("client_id", clientId); form.put("code_verifier", verifier);
                Map<String, Object> token = token(form);
                Spotify.Session connected = sessionFrom(token, clientId, null);
                if (Json.field(token, "scope") == null) connected.scope = Spotify.SCOPES;
                synchronized (lock) {
                    if (version != generation) throw new Cancelled();
                    try { SecureStore.write(file, connected.toJson()); }
                    catch (Exception storageError) { throw new IllegalStateException("Spotify approved, but the login could not be saved on this device. Reconnect to try again."); }
                    session = connected; startupWarning = null;
                }
                return null;
            }
        }, done, failed);
    }

    public void disconnect() throws Exception {
        synchronized (lock) {
            Spotify.Session cleared = Spotify.Session.cleared(clientId());
            SecureStore.write(file, cleared.toJson());
            generation++; session = cleared; retryAtMs = 0;
        }
    }

    private Map<String, Object> token(Map<String, String> values) throws Exception {
        Http.Response response;
        try { response = Http.send("POST", "https://accounts.spotify.com/api/token", null, Spotify.form(values).getBytes(StandardCharsets.UTF_8), "application/x-www-form-urlencoded", 15000, 1024 * 1024); }
        catch (java.net.SocketTimeoutException e) { throw new IllegalStateException("Spotify's sign-in server did not respond in time. Check your connection and try again."); }
        catch (IOException e) { throw new IllegalStateException("Could not contact Spotify to finish sign-in. Check your internet connection and try again."); }
        if (!response.ok()) {
            String error = response.text();
            if (error.contains("invalid_grant")) throw new IllegalStateException("Spotify authorization expired. Connect again.");
            if (response.code == 400 || response.code == 401) throw new IllegalStateException("Spotify rejected the app details. Check the Client ID and redirect address in Settings. Connect again.");
            throw new IllegalStateException("Spotify sign-in is temporarily unavailable. Try again later.");
        }
        return Json.obj(Json.parse(response.text()));
    }

    private static Spotify.Session sessionFrom(Map<String, Object> result, String clientId, String refresh) {
        Spotify.Session value = new Spotify.Session();
        value.clientId = clientId; value.accessToken = Json.field(result, "access_token");
        value.refreshToken = Json.field(result, "refresh_token") == null ? refresh : Json.field(result, "refresh_token");
        value.scope = Json.field(result, "scope");
        value.expiresMs = System.currentTimeMillis() + (long) (Json.number(result, "expires_in") * 1000);
        if (value.accessToken == null || value.accessToken.isEmpty() || value.refreshToken == null || value.refreshToken.isEmpty())
            throw new IllegalStateException("Spotify did not return a usable connection. Connect again.");
        return value;
    }

    private void forget(int version) throws Exception {
        synchronized (lock) {
            if (version != generation) throw new Cancelled();
            session = Spotify.Session.cleared(clientId());
            SecureStore.write(file, session.toJson());
        }
    }

    private static String clock(long ms) { return new SimpleDateFormat("HH:mm", Locale.US).format(new Date(ms)); }

    // Runs on the Spotify thread only.
    private Object request(String method, String path, String body) throws Exception {
        int version = generation;
        if (!connected()) throw new IllegalStateException("Connect Spotify to use your library.");
        if (System.currentTimeMillis() < retryAtMs) throw new IllegalStateException("Spotify is limiting requests. Try again after " + clock(retryAtMs) + ".");
        for (int attempt = 0; attempt < 2; attempt++) {
            if (session.expiresMs <= System.currentTimeMillis() + 60000) {
                Map<String, String> form = new LinkedHashMap<String, String>();
                form.put("grant_type", "refresh_token"); form.put("refresh_token", session.refreshToken); form.put("client_id", clientId());
                Map<String, Object> result;
                try { result = token(form); }
                catch (IllegalStateException e) {
                    if (e.getMessage() != null && e.getMessage().startsWith("Spotify authorization expired")) { try { forget(version); } catch (Cancelled ignored) { } }
                    throw e;
                }
                synchronized (lock) {
                    if (version != generation) throw new Cancelled();
                    Spotify.Session refreshed = sessionFrom(result, clientId(), session.refreshToken);
                    if (refreshed.scope == null) refreshed.scope = session.scope;
                    SecureStore.write(file, refreshed.toJson());
                    session = refreshed;
                }
            }
            Map<String, String> headers = new HashMap<String, String>();
            headers.put("Authorization", "Bearer " + session.accessToken);
            Http.Response response = Http.send(method, "https://api.spotify.com/v1/" + path, headers, body == null ? null : body.getBytes(StandardCharsets.UTF_8), body == null ? null : "application/json", 15000, 4 * 1024 * 1024);
            if (version != generation) throw new Cancelled();
            if (response.code == 401 && attempt == 0) { session.expiresMs = 0; continue; }
            if (response.code == 429) {
                long delay = 60000;
                try { String retry = response.header("Retry-After"); if (retry != null) delay = Long.parseLong(retry.trim()) * 1000; } catch (NumberFormatException ignored) { }
                retryAtMs = System.currentTimeMillis() + delay;
                if (response.text().contains("QUOTA_EXCEEDED")) { retryAtMs = System.currentTimeMillis() + 3600000L; throw new IllegalStateException("Spotify's developer quota is exhausted. Requests paused for an hour; try later."); }
                throw new IllegalStateException("Spotify is limiting requests. Requests paused until " + clock(retryAtMs) + ".");
            }
            if (response.code == 403) throw new IllegalStateException("Spotify denied access. Check the app owner's Premium, user allowlist and permissions, then reconnect.");
            if (response.code == 404 && path.startsWith("me/player/")) throw new NoDeviceException();
            if (response.code == 401) { forget(version); throw new IllegalStateException("Spotify authorization expired. Connect again."); }
            if (!response.ok()) throw new IllegalStateException("Spotify is unavailable (" + response.code + "). Try again later.");
            String data = response.text();
            return data.trim().isEmpty() ? null : Json.parse(data);
        }
        throw new IllegalStateException("Spotify authorization expired. Connect again.");
    }

    public <T> void call(Async.Work<T> work, Async.Done<T> done, Async.Failed failed) { Async.run(gate, work, done, failed); }

    private void requirePlayback() { if (!canControl()) throw new IllegalStateException("Reconnect in Settings → Spotify to enable playback. Spotify Premium is required."); }

    public Spotify.Playback current() throws Exception { return Spotify.parsePlayback(request("GET", "me/player/currently-playing?additional_types=episode", null)); }

    public Spotify.Page browse(String kind, String query, int offset) throws Exception {
        boolean recent = canReadRecent();
        return Spotify.parseBrowse(kind, request("GET", Spotify.browsePath(kind, query, offset, recent), null), offset, recent);
    }

    public Spotify.Page tracks(Spotify.Item collection, int offset) throws Exception { return Spotify.parseTracks(collection, request("GET", Spotify.tracksPath(collection, offset), null), offset); }

    public boolean isLiked(String uri) throws Exception {
        Object value = request("GET", "me/library/contains?uris=" + Spotify.encode(uri), null);
        if (!(value instanceof List) || ((List<?>) value).size() != 1 || !(((List<?>) value).get(0) instanceof Boolean)) throw new IllegalStateException("Spotify did not return the track's saved state.");
        return (Boolean) ((List<?>) value).get(0);
    }

    public void setLiked(String uri, boolean liked) throws Exception {
        if (Spotify.webUrl(uri) == null || !uri.startsWith("spotify:track:")) throw new IllegalStateException("Only Spotify songs can be liked here.");
        request(liked ? "PUT" : "DELETE", "me/library?uris=" + Spotify.encode(uri), null);
    }

    public void playInCollection(String collectionUri, int position) throws Exception { requirePlayback(); request("PUT", "me/player/play", Spotify.playInCollectionBody(collectionUri, position)); }
    public void playTrack(String trackUri) throws Exception { requirePlayback(); request("PUT", "me/player/play", Spotify.playTrackBody(trackUri)); }

    public static void openInBrowser(Activity activity, String url) { Launcher.openUrl(activity, url); }
}
