package com.alialyami.widgetscreen.widgets;

import android.text.Editable;
import android.text.InputFilter;
import android.text.InputType;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.NotesText;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.CheckBox;

// The PC's notes and to-do list, in the same three designs as the Windows NotesCard (CardSizing.notes;
// docs/design/notes.md): tabs for the scratchpad and the list on small and medium cards, both side by side on large
// ones. The PC keeps the notes; every change here is sent to it, and typing is sent a moment after it stops. While the
// phone's text is on its way, the card keeps showing it instead of the PC's older copy.
public final class NotesWidget extends Widget {
    private TextView status, notesTab, todoTab;
    private EditText text, adding;
    private LinearLayout rows;
    private View clearDone;
    private boolean showList, applying;
    private String listed = null, builtFor = "";
    // Text typed here that the PC hasn't confirmed yet.
    private String pendingText;
    private int design;

    public NotesWidget(Dashboard dash) { super("Notes", dash); }

    @Override public String part() { return "notes"; }

    private PcState.Notes notes() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.notes; }

    @Override protected View create() {
        PcState.Notes n = notes();
        builtFor = n == null ? "none" : "notes"; listed = null;
        text = adding = null; rows = null; clearDone = null; notesTab = todoTab = null;
        design = CardSizing.notes(width, height);

        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("NOTES"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = ui.caption("");
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        double bodyWidth = Math.max(1, width - 48);
        View body;
        if (n == null) body = ui.caption("Waiting for the PC…");
        else if (design == CardSizing.LARGE) {
            LinearLayout row = ui.row(); row.setGravity(Gravity.FILL_VERTICAL);
            LinearLayout left = ui.column();
            left.addView(ui.label("SCRATCHPAD"), ui.ll(Ui.MATCH, Ui.WRAP));
            left.addView(scratchpad(), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
            row.addView(left, ui.ll(0, Ui.MATCH, 1));
            LinearLayout right = ui.column();
            right.addView(ui.label("TO-DO"), ui.ll(Ui.MATCH, Ui.WRAP));
            right.addView(list(), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
            row.addView(right, ui.margins(ui.ll(ui.px(CardSizing.notesListColumn(bodyWidth)), Ui.MATCH), 20, 0, 0, 0));
            body = row;
        } else {
            LinearLayout stack = ui.column();
            LinearLayout tabs = ui.row();
            notesTab = ui.button("Notes", new View.OnClickListener() { @Override public void onClick(View v) { setList(false); } });
            todoTab = ui.button("To-do", new View.OnClickListener() { @Override public void onClick(View v) { setList(true); } });
            for (TextView tab : new TextView[] { notesTab, todoTab }) { tab.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small())); tab.setPadding(ui.px(8), ui.px(4), ui.px(8), ui.px(4)); }
            tabs.addView(notesTab, ui.ll(0, ui.px(48), 1));
            tabs.addView(todoTab, ui.margins(ui.ll(0, ui.px(48), 1), 8, 0, 0, 0));
            stack.addView(tabs, ui.ll(Ui.MATCH, Ui.WRAP));
            stack.addView(showList ? list() : scratchpad(), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
            body = stack;
        }
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private void setList(boolean list) { if (showList != list) { sendText(); showList = list; dash.rebuildWidget(id); } }

    private EditText scratchpad() {
        text = ui.input(pendingText != null ? pendingText : notes() == null ? "" : notes().text, "Notes");
        text.setSingleLine(false);
        text.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE | InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        text.setGravity(Gravity.TOP | Gravity.START);
        text.setFilters(new InputFilter[] { new InputFilter.LengthFilter(NotesText.MAX_TEXT) });
        text.setContentDescription("Scratchpad");
        text.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) { }
            @Override public void afterTextChanged(Editable s) {
                if (applying) return;
                pendingText = s.toString();
                Async.MAIN.removeCallbacks(sendLater); Async.MAIN.postDelayed(sendLater, 1200);
            }
        });
        text.setOnFocusChangeListener(new View.OnFocusChangeListener() { @Override public void onFocusChange(View v, boolean focused) { if (!focused) sendText(); } });
        return text;
    }

    private final Runnable sendLater = new Runnable() { @Override public void run() { sendText(); } };

    // Sends the typed text; it stays shown here until the PC has it (or says why it couldn't).
    private void sendText() {
        Async.MAIN.removeCallbacks(sendLater);
        if (pendingText == null) return;
        final String sent = pendingText;
        dash.pc.command(Json.map("action", "notes.text", "text", sent), new Async.Done<String>() {
            @Override public void ok(String message) { if (sent.equals(pendingText)) pendingText = null; dash.refreshSoon(); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { dash.say(Pc.message(error)); Async.MAIN.postDelayed(sendLater, 3000); }
        });
    }

    // The list scrolls inside the card; the add row and Clear done stay under it.
    private View list() {
        LinearLayout column = ui.column();
        ScrollView scroll = new ScrollView(ui.context);
        scroll.setVerticalScrollBarEnabled(false);
        rows = ui.column();
        scroll.addView(rows, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        column.addView(scroll, ui.ll(Ui.MATCH, 0, 1));
        LinearLayout add = ui.row();
        adding = ui.input("", "New to-do");
        adding.setImeOptions(EditorInfo.IME_ACTION_DONE);
        adding.setFilters(new InputFilter[] { new InputFilter.LengthFilter(NotesText.MAX_ITEM) });
        adding.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            @Override public boolean onEditorAction(TextView v, int action, KeyEvent event) { add(); return true; }
        });
        add.addView(adding, ui.ll(0, ui.px(52), 1));
        add.addView(ui.button("Add", new View.OnClickListener() { @Override public void onClick(View v) { add(); } }), ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 8, 0, 0, 0));
        column.addView(add, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        TextView clear = ui.button("Clear done", new View.OnClickListener() { @Override public void onClick(View v) {
            dash.confirm("NOTES", "Clear the done items?", "They're removed from the list on the PC and this phone.", "Clear done", new Runnable() {
                @Override public void run() { dash.command(Json.map("action", "notes.clearDone")); }
            });
        } });
        clear.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
        clearDone = clear;
        LinearLayout.LayoutParams clearParams = ui.margins(ui.ll(Ui.WRAP, ui.px(48)), 0, 8, 0, 0);
        clearParams.gravity = Gravity.START;
        column.addView(clear, clearParams);
        return column;
    }

    private void add() {
        if (adding == null) return;
        String line = NotesText.line(adding.getText().toString());
        if (line.isEmpty()) return;
        adding.setText("");
        dash.command(Json.map("action", "notes.add", "text", line));
    }

    // A to-do row: a check box with the text and a delete button.
    private View row(final PcState.Todo item) {
        LinearLayout row = ui.row();
        CheckBox box = ui.check(item.text, item.done, new CheckBox.Listener() {
            @Override public void changed(CheckBox b, boolean checked) { dash.command(Json.map("action", "notes.toggle", "id", item.id, "done", checked)); }
        });
        box.setAlpha(item.done ? 0.6f : 1f);
        row.addView(box, ui.ll(0, Ui.WRAP, 1));
        row.addView(ui.iconButton("close", "Delete " + item.text, 16, false, new View.OnClickListener() {
            @Override public void onClick(View v) { dash.command(Json.map("action", "notes.delete", "id", item.id)); }
        }), ui.ll(ui.px(48), ui.px(48)));
        return row;
    }

    @Override public void render() {
        if (!built()) return;
        PcState.Notes n = notes();
        if (!(n == null ? "none" : "notes").equals(builtFor)) { dash.rebuildWidget(id); return; }
        if (n == null) { status.setText(!dash.online() ? "PC OFFLINE" : ""); return; }
        status.setText(NotesText.status(n.items));
        if (todoTab != null) {
            todoTab.setText(NotesText.todoTab(n.items));
            ui.select(notesTab, !showList); ui.select(todoTab, showList);
        }
        // The PC's text, unless this phone is typing or still sending its own.
        if (text != null && pendingText == null && !text.hasFocus() && !text.getText().toString().equals(n.text)) {
            applying = true;
            try { text.setText(n.text); } finally { applying = false; }
        }
        if (rows == null) return;
        StringBuilder key = new StringBuilder();
        for (PcState.Todo item : n.items) key.append(item.id).append('|').append(item.done).append('|').append(item.text).append('\n');
        if (!key.toString().equals(listed)) {
            listed = key.toString();
            rows.removeAllViews();
            for (PcState.Todo item : n.items) rows.addView(row(item), ui.ll(Ui.MATCH, Ui.WRAP));
        }
        clearDone.setVisibility(NotesText.anyDone(n.items) ? View.VISIBLE : View.GONE);
    }

    @Override public void release() { sendText(); super.release(); }
}
