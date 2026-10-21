package com.alialyami.widgetscreen.widgets;

import android.graphics.Bitmap;
import android.os.SystemClock;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Spotify;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Images;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.GlyphView;

// Port of the Windows SpotifyWidget: recent albums, playlists and search with cover thumbnails, plus a
// now-playing strip with cover, song, artist and Like. Tapping a song plays it in its album or playlist.
public final class SpotifyWidget extends Widget {
    private LinearLayout tabs, library, results, searchRow, collectionRow, paging, strip, emptyState;
    private TextView account, collectionTitle, pageLabel, pagePrevious, pageNext, songTitle, songArtist, like, status;
    private final TextView[] tabButtons = new TextView[3];
    private static final String[] KINDS = { "Albums", "Playlists", "Search" };
    private EditText query;
    private ScrollView scroll;
    private ImageView art;
    private GlyphView likeGlyph;

    private Spotify.Playback playback;
    private Spotify.Item collection;
    private Spotify.Page page;
    private String kind = "Albums", searchQuery = "", likedUri, artUrl, statusText = "", pageError;
    private Boolean liked;
    private Bitmap artBitmap;
    private boolean busy, likeBusy, commandBusy, libraryPending = true, loading;
    private int generation, browseGeneration, offset;
    private long nextPoll, lastLikeCheck;

    public SpotifyWidget(Dashboard dash) { super("Spotify", dash); }

    // The PC's Spotify connection, reached through the phone link, with the same calls as the Windows SpotifyService.
    private final class Proxy {
        private PcLink link;
        private PcState.SpotifyFlags flags() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.spotify; }
        boolean connected() { PcState.SpotifyFlags f = flags(); return f != null && f.connected; }
        boolean canControl() { PcState.SpotifyFlags f = flags(); return f != null && f.canControl; }
        boolean canReadRecent() { PcState.SpotifyFlags f = flags(); return f != null && f.canReadRecent; }
        // Work runs on the link's command thread, one call at a time, so the link field belongs to it.
        <T> void call(final Async.Work<T> work, Async.Done<T> done, Async.Failed failed) {
            dash.pc.send(new Pc.Call<T>() { @Override public T run(PcLink current) throws Exception { link = current; return work.run(); } }, done, failed);
        }
        Spotify.Page browse(String kind, String query, int offset) throws Exception { return PcState.page(link.spotify(Json.map("op", "browse", "kind", kind, "query", query, "offset", offset))); }
        Spotify.Page tracks(Spotify.Item collection, int offset) throws Exception { return PcState.page(link.spotify(Json.map("op", "tracks", "uri", collection.uri, "offset", offset))); }
        Spotify.Playback current() throws Exception { return PcState.playback(link.spotify(Json.map("op", "current"))); }
        boolean isLiked(String uri) throws Exception { return Boolean.TRUE.equals(Json.value(link.spotify(Json.map("op", "liked", "uri", uri)), "liked")); }
        void setLiked(String uri, boolean liked) throws Exception { link.spotify(Json.map("op", "like", "uri", uri, "liked", liked)); }
        void playTrack(String uri) throws Exception { link.spotify(Json.map("op", "playTrack", "uri", uri)); }
        void playInCollection(String uri, int position) throws Exception { link.spotify(Json.map("op", "playCollection", "uri", uri, "position", position)); }
        void openOnPc() throws Exception { link.spotify(Json.map("op", "open")); }
    }
    private final Proxy proxy = new Proxy();
    private Proxy service() { return proxy; }
    private static boolean noDevice(Exception error) { return error instanceof PcLink.Failure && ((PcLink.Failure) error).noDevice; }
    private boolean wasConnected;

    @Override public String part() { return "spotify"; }

    // Readings arrive every couple of seconds; only a change in the PC's Spotify connection matters here.
    @Override public void stateChanged() {
        boolean connected = service().connected();
        if (connected != wasConnected) { wasConnected = connected; accountChanged(); }
    }

    @Override protected View create() {
        FrameLayout root = new FrameLayout(ui.context);
        LinearLayout column = ui.column();
        LinearLayout header = ui.row();
        header.addView(ui.label("SPOTIFY"), ui.ll(0, Ui.WRAP, 1));
        account = ui.button("Account", new View.OnClickListener() { @Override public void onClick(View v) { dash.say("Spotify is connected on the PC. Manage it there in Settings → Spotify."); } });
        account.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
        account.setPadding(ui.px(12), ui.px(4), ui.px(12), ui.px(4));
        account.setContentDescription("Spotify account and setup in Settings");
        header.addView(account, ui.ll(Ui.WRAP, Ui.WRAP));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(48)));

        tabs = ui.row();
        for (int i = 0; i < KINDS.length; i++) {
            final String tab = KINDS[i];
            tabButtons[i] = ui.button(tab, new View.OnClickListener() { @Override public void onClick(View v) { select(tab, true); } });
            tabButtons[i].setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
            tabButtons[i].setPadding(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
            tabs.addView(tabButtons[i], ui.margins(ui.ll(0, Ui.MATCH, 1), 0, 0, i < 2 ? 4 : 0, 0));
        }
        column.addView(tabs, ui.margins(ui.ll(Ui.MATCH, ui.px(48)), 0, 4, 0, 4));

        library = ui.column();
        searchRow = ui.row();
        query = ui.input(searchQuery, "Song or artist");
        query.setImeOptions(EditorInfo.IME_ACTION_SEARCH);
        query.setContentDescription("Song or artist to search");
        query.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            @Override public boolean onEditorAction(TextView v, int action, KeyEvent event) {
                if (action == EditorInfo.IME_ACTION_SEARCH || (event != null && event.getKeyCode() == KeyEvent.KEYCODE_ENTER && event.getAction() == KeyEvent.ACTION_DOWN)) { search(); return true; }
                return false;
            }
        });
        searchRow.addView(query, ui.ll(0, Ui.WRAP, 1));
        searchRow.addView(ui.button("Find", new View.OnClickListener() { @Override public void onClick(View v) { search(); } }), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0));
        library.addView(searchRow, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6));

        collectionRow = ui.row();
        collectionRow.addView(ui.button("Back", new View.OnClickListener() { @Override public void onClick(View v) { select(kind, true); } }), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        collectionTitle = ui.smallText("");
        collectionRow.addView(collectionTitle, ui.margins(ui.ll(0, Ui.WRAP, 1), 4, 0, 4, 0));
        TextView open = ui.button("Open Spotify", new View.OnClickListener() { @Override public void onClick(View v) { open(collection == null ? null : collection.webUrl()); } });
        open.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
        open.setPadding(ui.px(8), ui.px(4), ui.px(8), ui.px(4));
        collectionRow.addView(open, ui.ll(Ui.WRAP, Ui.WRAP));
        library.addView(collectionRow, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6));

        scroll = new ScrollView(ui.context);
        scroll.setVerticalScrollBarEnabled(false);
        // Inside the phone list, the inner list scrolls first and hands the rest to the page.
        scroll.setNestedScrollingEnabled(true);
        results = ui.column();
        scroll.addView(results);
        library.addView(scroll, ui.ll(Ui.MATCH, 0, 1));

        paging = ui.row();
        pagePrevious = ui.button("Previous", new View.OnClickListener() { @Override public void onClick(View v) { offset = Math.max(0, offset - 10); loadPage(); } });
        paging.addView(pagePrevious, ui.ll(Ui.WRAP, Ui.WRAP));
        pageLabel = ui.smallText("");
        pageLabel.setGravity(Gravity.CENTER);
        paging.addView(pageLabel, ui.ll(0, Ui.WRAP, 1));
        pageNext = ui.button("Next", new View.OnClickListener() { @Override public void onClick(View v) { offset += 10; loadPage(); } });
        paging.addView(pageNext, ui.ll(Ui.WRAP, Ui.WRAP));
        library.addView(paging, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        column.addView(library, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 4));

        strip = ui.row();
        strip.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        strip.setPadding(ui.px(8), ui.px(8), ui.px(8), ui.px(8));
        FrameLayout cover = new FrameLayout(ui.context);
        cover.setBackground(ui.shape(ui.theme.clockSurface, 0, 0, 10));
        cover.setClipToOutline(true);
        cover.addView(new GlyphView(ui.context, "music", ui.theme.accent, ui.fpx(26)), new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        art = new ImageView(ui.context);
        art.setScaleType(ImageView.ScaleType.CENTER_CROP);
        art.setContentDescription("Current song cover");
        cover.addView(art, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        strip.addView(cover, ui.ll(ui.px(64), ui.px(64)));
        LinearLayout song = ui.column();
        songTitle = ui.body("");
        songTitle.setTypeface(Ui.MEDIUM);
        song.addView(songTitle, ui.ll(Ui.MATCH, Ui.WRAP));
        songArtist = ui.smallText("");
        song.addView(songArtist, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        strip.addView(song, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 8, 0));
        LinearLayout likeButton = ui.row();
        likeButton.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
        likeButton.setPadding(ui.px(12), ui.px(4), ui.px(12), ui.px(4));
        likeButton.setMinimumHeight(ui.px(48));
        likeGlyph = new GlyphView(ui.context, "heart-outline", ui.theme.text, ui.fpx(18));
        likeButton.addView(likeGlyph, ui.ll(ui.px(20), ui.px(20)));
        like = ui.text("Like", ui.small(), ui.theme.text);
        likeButton.addView(like, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 6, 0, 0, 0));
        likeButton.setClickable(true);
        likeButton.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { toggleLike(); } });
        strip.addView(likeButton, ui.ll(Ui.WRAP, Ui.WRAP));
        column.addView(strip, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));

        status = ui.smallText("");
        column.addView(status, ui.ll(Ui.MATCH, ui.px(36)));
        status.setGravity(Gravity.CENTER_VERTICAL);
        root.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));

        // Shown instead of the tabs, list and now-playing strip until an account is connected.
        emptyState = ui.column();
        emptyState.setGravity(Gravity.CENTER_HORIZONTAL);
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.clockSurface, 0, 0, 20));
        tile.addView(new GlyphView(ui.context, "music", ui.theme.accent, ui.fpx(38)), new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        emptyState.addView(tile, ui.ll(ui.px(88), ui.px(88)));
        TextView heading = ui.heading("Connect Spotify");
        heading.setTypeface(Ui.MEDIUM);
        emptyState.addView(heading, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 20, 0, 0));
        TextView blurb = Ui.wrap(ui.smallText("Connect Spotify on the PC to browse your recent albums and playlists here, and like the song that's playing."), 3);
        blurb.setGravity(Gravity.CENTER);
        emptyState.addView(blurb, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 22));
        TextView setup = ui.accentButton("How to connect", new View.OnClickListener() { @Override public void onClick(View v) { dash.say("On the PC, open Settings → Spotify and connect your account. The phone uses the PC's connection."); } });
        setup.setMinWidth(ui.px(200));
        emptyState.addView(setup, ui.ll(Ui.WRAP, Ui.WRAP));
        FrameLayout.LayoutParams emptyParams = new FrameLayout.LayoutParams(Math.min(ui.px(360), Ui.MATCH), Ui.WRAP, Gravity.CENTER);
        emptyParams.width = ui.px(Math.min(360, Math.max(200, width - 48)));
        root.addView(emptyState, emptyParams);
        return ui.card(root, 24);
    }

    private void setStatus(String text) { statusText = text == null ? "" : text; if (built()) status.setText(statusText); }

    private void open(String url) {
        if (url == null) return;
        try { Async.MAIN.post(new OpenLink(url)); } catch (Exception e) { setStatus("Could not open Spotify."); }
    }

    private final class OpenLink implements Runnable {
        private final String url;
        OpenLink(String url) { this.url = url; }
        @Override public void run() { try { dash.openUrl(url); } catch (Exception e) { setStatus("Could not open Spotify."); } }
    }

    // A connection change (connect, disconnect, expiry) resets everything that came from the old account.
    public void accountChanged() {
        generation++; browseGeneration++; playback = null; likedUri = artUrl = null; liked = null; artBitmap = null; nextPoll = 0;
        page = null; collection = null; offset = 0; pageError = null;
        libraryPending = service().connected();
        setStatus(service().connected() ? "" : "Connect Spotify on the PC to use your library.");
        render();
    }

    @Override public void render() {
        if (!built()) return;
        boolean connected = service().connected();
        int shown = connected ? View.VISIBLE : View.GONE;
        tabs.setVisibility(shown); library.setVisibility(shown); strip.setVisibility(shown); status.setVisibility(shown); account.setVisibility(shown);
        emptyState.setVisibility(connected ? View.GONE : View.VISIBLE);
        for (int i = 0; i < KINDS.length; i++) {
            boolean on = KINDS[i].equals(kind);
            ui.select(tabButtons[i], on);
            tabButtons[i].setContentDescription(KINDS[i] + (on ? ", selected" : ""));
        }
        searchRow.setVisibility(kind.equals("Search") && collection == null ? View.VISIBLE : View.GONE);
        collectionRow.setVisibility(collection != null ? View.VISIBLE : View.GONE);
        collectionTitle.setText(collection == null ? "" : collection.name);
        if (!query.getText().toString().equals(searchQuery) && !query.hasFocus()) query.setText(searchQuery);
        renderPage();
        renderNow();
        status.setText(statusText);
    }

    private void renderPage() {
        results.removeAllViews();
        boolean hasPrevious = offset > 0 && !loading, hasNext = page != null && page.hasMore && !loading;
        paging.setVisibility(offset > 0 || (page != null && page.hasMore) ? View.VISIBLE : View.GONE);
        Ui.enable(pagePrevious, hasPrevious); Ui.enable(pageNext, hasNext);
        pageLabel.setText(page == null || page.items.isEmpty() ? "" : (offset + 1) + "–" + (offset + page.items.size()));
        if (page == null) return;
        // Rows side by side on wide cards (CardSizing, as on the PC).
        int columns = CardSizing.spotifyColumns(width);
        LinearLayout line = null;
        for (int i = 0; i < page.items.size(); i++) {
            if (columns == 1) { results.addView(row(page.items.get(i)), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6)); continue; }
            if (i % columns == 0) { line = ui.row(); results.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6)); }
            line.addView(row(page.items.get(i)), ui.margins(ui.ll(0, Ui.WRAP, 1), i % columns == 0 ? 0 : 3, 0, i % columns == columns - 1 ? 0 : 3, 0));
        }
        if (line != null) for (int i = page.items.size() % columns; i > 0 && i < columns; i++) line.addView(new View(ui.context), ui.ll(0, 1, 1));
    }

    private void renderNow() {
        Spotify.Item item = playback == null ? null : playback.item;
        if (!service().connected()) { songTitle.setText(""); songArtist.setText(""); }
        else if (playback == null && pageError == null && busy) { songTitle.setText("Loading…"); songArtist.setText("Checking what's playing."); }
        else if (pageError != null) { songTitle.setText("Can't reach Spotify"); songArtist.setText("Retrying in a minute."); }
        else if (playback == null) { songTitle.setText("Loading…"); songArtist.setText("Checking what's playing."); }
        else { songTitle.setText(item == null ? "Nothing playing" : item.name); songArtist.setText(item == null ? "Play something in Spotify on any device." : item.subtitle); }
        art.setImageBitmap(artBitmap);
        boolean canLike = item != null && item.isTrack() && liked != null && !likeBusy;
        View likeButton = (View) like.getParent();
        Ui.enable(likeButton, canLike);
        like.setText(Boolean.TRUE.equals(liked) ? "Liked" : "Like");
        likeGlyph.setGlyph(Boolean.TRUE.equals(liked) ? "heart" : "heart-outline");
        likeGlyph.setColor(Boolean.TRUE.equals(liked) ? ui.theme.accent : ui.theme.text);
        likeButton.setContentDescription(Boolean.TRUE.equals(liked) ? "Remove current song from Liked Songs" : "Save current song to Liked Songs");
    }

    private void select(String selected, boolean load) {
        browseGeneration++; kind = selected; offset = 0; collection = null; libraryPending = false; page = null; loading = false;
        if (!service().connected()) { setStatus("Connect Spotify on the PC to use your library."); render(); return; }
        if (selected.equals("Search")) {
            setStatus("Type a song or artist, then tap Find.");
            render();
            if (built()) query.requestFocus();
        } else if (load) { render(); loadPage(); }
        else { setStatus("Loading from Spotify…"); render(); }
    }

    private void search() {
        searchQuery = query.getText().toString().trim();
        kind = "Search"; offset = 0; collection = null;
        InputMethodManager keyboard = (InputMethodManager) ui.context.getSystemService(android.content.Context.INPUT_METHOD_SERVICE);
        if (keyboard != null) keyboard.hideSoftInputFromWindow(query.getWindowToken(), 0);
        query.clearFocus();
        loadPage();
    }

    private void loadPage() {
        final int version = ++browseGeneration;
        page = null;
        if (!service().connected()) { setStatus("Connect Spotify on the PC to use your library."); render(); return; }
        if (kind.equals("Search") && collection == null && searchQuery.isEmpty()) { setStatus("Type a song or artist to search for."); render(); return; }
        loading = true;
        setStatus("Loading from Spotify…");
        render();
        final Spotify.Item parent = collection;
        final String browseKind = kind, browseQuery = searchQuery;
        final int browseOffset = offset;
        service().call(new Async.Work<Spotify.Page>() {
            @Override public Spotify.Page run() throws Exception { return parent == null ? service().browse(browseKind, browseQuery, browseOffset) : service().tracks(parent, browseOffset); }
        }, new Async.Done<Spotify.Page>() {
            @Override public void ok(Spotify.Page result) {
                if (version != browseGeneration) return;
                loading = false; page = result;
                setStatus(Spotify.listStatus(result.items.size(), kind, collection, service().canReadRecent(), service().canControl()));
                render();
                if (built()) scroll.scrollTo(0, 0);
            }
        }, new Async.Failed() {
            @Override public void error(Exception error) {
                if (version != browseGeneration) return;
                loading = false; page = new Spotify.Page(new java.util.ArrayList<Spotify.Item>(), false);
                setStatus(Pc.message(error));
                if (!service().connected()) accountChanged(); else render();
            }
        });
    }

    private View row(final Spotify.Item item) {
        LinearLayout row = ui.row();
        row.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
        row.setPadding(ui.px(10), ui.px(8), ui.px(12), ui.px(8));
        row.setMinimumHeight(ui.px(70));
        FrameLayout cover = new FrameLayout(ui.context);
        cover.setBackground(ui.shape(ui.theme.clockSurface, 0, 0, 8));
        cover.setClipToOutline(true);
        cover.addView(new GlyphView(ui.context, item.uri.startsWith("spotify:playlist:") ? "playlist" : item.isTrack() || item.isEpisode() ? "music" : "album", ui.theme.accent, ui.fpx(20)), new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        final ImageView image = new ImageView(ui.context);
        image.setScaleType(ImageView.ScaleType.CENTER_CROP);
        cover.addView(image, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        row.addView(cover, ui.ll(ui.px(52), ui.px(52)));
        LinearLayout label = ui.column();
        TextView name = ui.body(item.name);
        name.setTypeface(Ui.MEDIUM);
        label.addView(name, ui.ll(Ui.MATCH, Ui.WRAP));
        label.addView(ui.smallText(item.subtitle == null ? "" : item.subtitle), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 3, 0, 0));
        row.addView(label, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
        final Spotify.Item parent = collection;
        final boolean playsInCollection = parent != null && item.isTrack() && item.position >= 0 && service().canControl();
        final boolean playsSearchResult = parent == null && item.isTrack() && service().canControl();
        row.setClickable(true);
        row.setContentDescription(playsInCollection ? "Play " + item.name + " in " + parent.name : playsSearchResult ? "Play " + item.name : item.isTrack() ? "Open " + item.name + " in Spotify" : "Browse " + item.name);
        row.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (playsInCollection) play(parent, item);
                else if (playsSearchResult) play(null, item);
                else if (item.isTrack() || item.isEpisode()) open(item.webUrl());
                else { collection = item; offset = 0; loadPage(); }
            }
        });
        if (Spotify.isImageUrl(item.imageUrl)) {
            final int version = browseGeneration;
            dash.images.load(item.imageUrl, 104, new Images.Loaded() { @Override public void loaded(Bitmap bitmap) { if (version == browseGeneration) image.setImageBitmap(bitmap); } });
        }
        return row;
    }

    @Override public void tick(int ticks, boolean quiet) {
        if (!active()) return;
        if (libraryPending && service().connected()) { libraryPending = false; if (!kind.equals("Search")) loadPage(); }
        refresh(quiet);
    }

    private void refresh(final boolean quiet) {
        if (!service().connected() || busy || commandBusy || SystemClock.elapsedRealtime() < nextPoll) return;
        busy = true;
        final int version = generation;
        nextPoll = SystemClock.elapsedRealtime() + (quiet ? 30000 : 10000);
        service().call(new Async.Work<Object[]>() {
            @Override public Object[] run() throws Exception {
                Spotify.Playback snapshot = service().current();
                Boolean saved = null; Exception likeError = null;
                Spotify.Item item = snapshot.item;
                boolean sameSong = item != null && item.uri.equals(likedUri);
                if (item != null && item.isTrack() && !likeBusy && (!sameSong || liked == null || SystemClock.elapsedRealtime() - lastLikeCheck >= 60000)) {
                    try { saved = service().isLiked(item.uri); } catch (Exception e) { likeError = e; }
                }
                return new Object[] { snapshot, saved, likeError };
            }
        }, new Async.Done<Object[]>() {
            @Override public void ok(Object[] result) {
                busy = false;
                if (version != generation || !service().connected()) return;
                playback = (Spotify.Playback) result[0]; pageError = null;
                Spotify.Item item = playback.item;
                String uri = item == null ? null : item.uri;
                if (uri == null ? likedUri != null : !uri.equals(likedUri)) { likedUri = uri; liked = null; }
                if (result[1] != null) { liked = (Boolean) result[1]; lastLikeCheck = SystemClock.elapsedRealtime(); }
                if (result[2] != null) setStatus(Pc.message((Exception) result[2]));
                final String requested = item == null ? null : item.imageUrl;
                if (requested == null ? artUrl != null : !requested.equals(artUrl)) {
                    artUrl = requested; artBitmap = null;
                    if (Spotify.isImageUrl(requested)) dash.images.load(requested, 128, new Images.Loaded() {
                        @Override public void loaded(Bitmap bitmap) { if (version == generation && requested.equals(artUrl)) { artBitmap = bitmap; if (built()) renderNow(); } }
                    });
                }
                if (built()) renderNow();
            }
        }, new Async.Failed() {
            @Override public void error(Exception error) {
                busy = false;
                if (version != generation) return;
                nextPoll = SystemClock.elapsedRealtime() + 60000; playback = null; likedUri = artUrl = null; liked = null; artBitmap = null;
                pageError = Pc.message(error);
                setStatus(pageError);
                if (!service().connected()) accountChanged(); else if (built()) renderNow();
            }
        });
    }

    private void toggleLike() {
        if (likeBusy || playback == null || playback.item == null || liked == null) return;
        final int version = generation;
        final String uri = playback.item.uri;
        final boolean target = !liked;
        likeBusy = true; renderNow();
        service().call(new Async.Work<Void>() {
            @Override public Void run() throws Exception { service().setLiked(uri, target); return null; }
        }, new Async.Done<Void>() {
            @Override public void ok(Void value) {
                likeBusy = false;
                if (version == generation && uri.equals(likedUri)) { liked = target; lastLikeCheck = SystemClock.elapsedRealtime(); setStatus(target ? "Saved to your Liked Songs." : "Removed from your Liked Songs."); }
                if (built()) renderNow();
            }
        }, new Async.Failed() {
            @Override public void error(Exception error) { likeBusy = false; if (version == generation) setStatus(Pc.message(error)); if (built()) renderNow(); }
        });
    }

    // Plays the song inside its album or playlist, or on its own when parent is null (search results).
    // With no active Spotify device, it opens the Spotify app and retries while it starts.
    private void play(final Spotify.Item parent, final Spotify.Item song) {
        if (commandBusy) return;
        commandBusy = true;
        final int version = generation;
        setStatus("Starting " + song.name + "…");
        attempt(parent, song, version, 0);
    }

    private void attempt(final Spotify.Item parent, final Spotify.Item song, final int version, final int tries) {
        service().call(new Async.Work<Void>() {
            @Override public Void run() throws Exception {
                if (parent == null) service().playTrack(song.uri); else service().playInCollection(parent.uri, song.position);
                return null;
            }
        }, new Async.Done<Void>() {
            @Override public void ok(Void value) {
                commandBusy = false;
                if (version != generation) return;
                playback = null; nextPoll = SystemClock.elapsedRealtime() + 1000;
                setStatus("Playing " + song.name + (parent == null ? "." : " from " + parent.name + "."));
            }
        }, new Async.Failed() {
            @Override public void error(Exception error) {
                if (version != generation) { commandBusy = false; return; }
                if (noDevice(error) && tries < 6) {
                    if (tries == 0) {
                        // No device is active: open Spotify on the PC and retry briefly while it starts.
                        service().call(new Async.Work<Void>() { @Override public Void run() throws Exception { service().openOnPc(); return null; } }, null, null);
                        setStatus("Opening Spotify on the PC to play " + song.name + "…");
                    }
                    Async.MAIN.postDelayed(new Runnable() { @Override public void run() { attempt(parent, song, version, tries + 1); } }, 2000);
                    return;
                }
                commandBusy = false;
                setStatus(noDevice(error) ? "Spotify opened but isn't ready yet. Play anything in it once, then tap the song again." : Pc.message(error));
            }
        });
    }
}
