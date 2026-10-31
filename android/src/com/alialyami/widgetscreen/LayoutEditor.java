package com.alialyami.widgetscreen;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.core.WidgetLayout.Box;
import com.alialyami.widgetscreen.core.WidgetLayout.Placement;

import java.util.ArrayList;
import java.util.List;

// Port of the Windows LayoutEditor's model: the current and saved arrangement, gestures, undo and redo (50),
// show and hide, Reset, Save and Cancel. WidgetCanvas draws it and feeds it touches.
public final class LayoutEditor {
    public interface Listener { void changed(boolean structure); }

    private final List<Placement[]> undo = new ArrayList<Placement[]>(), redo = new ArrayList<Placement[]>();
    private Placement[] saved, current, gestureBefore;
    private AppSettings preferences;
    // The canvas the dashboard has on this screen (DashboardFit); layouts are kept per canvas size.
    private double canvasWidth = WidgetLayout.REFERENCE_WIDTH, canvasHeight = WidgetLayout.REFERENCE_HEIGHT;
    private String gestureId, gestureCorner, selected = "Focus";
    private Box gestureBounds;
    private int gestureLayer;
    private boolean editing, snap, gestureMoved;
    private Listener listener;
    // Alignment guides shown while a drag sits on another widget's edge (WidgetLayout.align); WidgetCanvas draws them.
    private final List<WidgetLayout.Guide> guides = new ArrayList<WidgetLayout.Guide>();

    public void setListener(Listener listener) { this.listener = listener; }
    public boolean editing() { return editing; }
    public boolean snap() { return snap; }
    public String selected() { return selected; }
    public boolean canUndo() { return !undo.isEmpty(); }
    public boolean canRedo() { return !redo.isEmpty(); }
    public boolean gestureActive() { return gestureId != null; }
    public Placement[] placements() { return current; }
    public List<WidgetLayout.Guide> guides() { return guides; }
    public double canvasWidth() { return canvasWidth; }
    public double canvasHeight() { return canvasHeight; }

    private void changed(boolean structure) { if (listener != null) listener.changed(structure); }

    public void apply(AppSettings settings) {
        preferences = settings;
        if (editing) return;
        snap = settings.snapWidgets;
        current = settings.layoutFor(canvasWidth, canvasHeight);
        saved = WidgetLayout.copy(current);
    }

    // A new screen shape ends an edit in progress (its changes belonged to the old canvas) and loads the layout for
    // the new canvas. Returns true when an edit was discarded.
    public boolean setCanvas(double width, double height) {
        width = WidgetLayout.key(width); height = WidgetLayout.key(height);
        if (width == canvasWidth && height == canvasHeight && current != null) return false;
        boolean discarded = editing;
        if (editing) { gestureId = null; editing = false; undo.clear(); redo.clear(); }
        canvasWidth = width; canvasHeight = height;
        if (preferences != null) apply(preferences);
        return discarded;
    }

    public void begin() {
        if (editing) return;
        editing = true; saved = WidgetLayout.copy(current); undo.clear(); redo.clear();
        selected = "Focus";
        for (Placement p : current) if (isVisible(p.id)) { selected = p.id; break; }
        changed(true);
    }

    public void cancel() {
        gestureId = null;
        current = WidgetLayout.copy(saved); editing = false; snap = preferences.snapWidgets; undo.clear(); redo.clear();
        changed(true);
    }

    public Placement[] export() { return WidgetLayout.copy(current); }

    public void accept() {
        gestureId = null;
        saved = WidgetLayout.copy(current); editing = false; undo.clear(); redo.clear();
        changed(true);
    }

    public void setSnap(boolean value) { snap = value; changed(false); }

    public void select(String id) {
        if (WidgetLayout.find(current, id) == null) return;
        selected = id; changed(false);
    }

    public boolean isVisible(String id) {
        if (current == null) return true;
        Placement p = WidgetLayout.find(current, id);
        return p != null && p.visible;
    }

    public boolean isEnabled(String id) { Placement p = WidgetLayout.find(current, id); return p != null && p.visible; }

    public Box bounds(String id) { return WidgetLayout.find(current, id).bounds(); }

    public int layer(String id) { return WidgetLayout.find(current, id).layer; }

    public boolean hasOverlaps() {
        List<Placement> visible = new ArrayList<Placement>();
        for (Placement p : current) if (isVisible(p.id)) visible.add(p);
        for (int i = 0; i < visible.size(); i++)
            for (int j = i + 1; j < visible.size(); j++) {
                Box a = visible.get(i).bounds(), b = visible.get(j).bounds();
                double width = Math.min(a.right(), b.right()) - Math.max(a.x, b.x), height = Math.min(a.bottom(), b.bottom()) - Math.max(a.y, b.y);
                if (width > 1 && height > 1) return true;
            }
        return false;
    }

    private void remember(Placement[] before) {
        undo.add(before);
        if (undo.size() > 50) undo.remove(0);
        redo.clear();
    }

    public void setVisible(String id, boolean visible) {
        if (!editing || gestureActive()) return;
        Placement p = WidgetLayout.find(current, id);
        if (p == null || p.visible == visible) return;
        remember(export()); p.visible = visible;
        if (visible) selected = id;
        changed(true);
    }

    public void reset() {
        if (!editing || gestureActive()) return;
        remember(export()); current = WidgetLayout.defaults(canvasWidth, canvasHeight);
        changed(true);
    }

    // Lines up the shown widgets neatly in their current order, filling the canvas (WidgetLayout.arrange). One undo
    // step; returns false when they are already arranged that way.
    public boolean arrange() {
        if (!editing || gestureActive()) return false;
        List<Placement> visible = new ArrayList<Placement>();
        for (Placement p : current) if (isVisible(p.id)) visible.add(p);
        List<String> shown = WidgetLayout.arrangeOrder(visible, canvasWidth, canvasHeight);
        if (shown.isEmpty()) return false;
        Placement[] next = WidgetLayout.tidy(current, shown, canvasWidth, canvasHeight);
        boolean same = true;
        for (int i = 0; i < next.length; i++) if (!next[i].bounds().equals(current[i].bounds()) || next[i].layer != current[i].layer) same = false;
        if (same) return false;
        remember(export()); current = next;
        changed(true);
        return true;
    }

    public void undo() {
        if (!editing || !canUndo() || gestureActive()) return;
        redo.add(export()); current = undo.remove(undo.size() - 1);
        changed(true);
    }

    public void redo() {
        if (!editing || !canRedo() || gestureActive()) return;
        undo.add(export()); current = redo.remove(redo.size() - 1);
        changed(true);
    }

    private int topLayer() { int top = 0; for (Placement p : current) top = Math.max(top, p.layer); return top; }

    // Moves (or, with resize, grows from the bottom-right corner) the selected card by whole units: the Windows
    // editor's arrow keys. Each nudge is one undo step.
    public void nudgeSelected(double dx, double dy, boolean resize) {
        if (!editing || gestureActive()) return;
        Placement p = WidgetLayout.find(current, selected);
        if (p == null) return;
        Box next = WidgetLayout.transform(p.id, p.bounds(), resize ? "SE" : "", dx, dy, false, canvasWidth, canvasHeight);
        if (next.equals(p.bounds())) return;
        remember(export()); p.layer = topLayer() + 1; p.setBounds(next);
        changed(resize);
    }

    public void startGesture(String id, String corner) {
        if (!editing || gestureActive()) return;
        gestureBefore = export(); gestureId = id; gestureCorner = corner; gestureMoved = false;
        gestureBounds = bounds(id); gestureLayer = topLayer() + 1;
        selected = id;
        changed(false);
    }

    // Movement since the gesture started, in canvas units.
    public void continueGesture(double dx, double dy) {
        if (gestureId == null) return;
        // A tap selects a widget without snapping or changing its saved stacking order.
        if (!gestureMoved && Math.abs(dx) < 2 && Math.abs(dy) < 2) return;
        gestureMoved = true;
        Placement p = WidgetLayout.find(current, gestureId);
        List<Box> others = new ArrayList<Box>();
        for (Placement o : current) if (!o.id.equals(p.id) && isVisible(o.id)) others.add(o.bounds());
        Box next;
        if (snap) next = WidgetLayout.align(p.id, gestureBounds, gestureCorner, dx, dy, others, canvasWidth, canvasHeight, guides);
        else { guides.clear(); next = WidgetLayout.transform(p.id, gestureBounds, gestureCorner, dx, dy, false, canvasWidth, canvasHeight); }
        p.layer = next.equals(gestureBounds) ? WidgetLayout.find(gestureBefore, p.id).layer : gestureLayer;
        p.setBounds(next);
        changed(false);
    }

    public void endGesture() {
        if (gestureId == null) return;
        String id = gestureId;
        boolean moved = !bounds(id).equals(gestureBounds);
        if (moved) remember(gestureBefore);
        gestureId = null; gestureBefore = null; guides.clear();
        // The card's content picks its compact or tall arrangement again for its new size.
        changed(moved);
    }
}
