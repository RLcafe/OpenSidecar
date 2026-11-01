using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Notes widget (docs/design/notes.md): a scratchpad and a to-do list that this PC keeps in notes.xml. Typing
    // is saved a second after it stops (on the existing heartbeat) or when the box loses focus; list changes save at
    // once. A phone reads the same notes and changes them through the link.
    public sealed partial class DashboardController
    {
        private NotesStore notes;
        private NotesCard notesCard;
        // Typed text not saved yet, and the heartbeat it was last typed on.
        private string notesDraft;
        private int notesTypedAt;
        // For the checks (tests/Notes/NotesTests.cs).
        public NotesStore Notes { get { return notes; } }
        public NotesCard NotesView { get { return notesCard; } }

        private void InitializeNotes()
        {
            notes = new NotesStore(NotesStore.PathFor(settingsPath));
            if (notes.LoadWarning != null && pendingMessage == null) pendingMessage = notes.LoadWarning;
            notesCard = new NotesCard(window, Control<Grid>("NotesBody"), Control<TextBlock>("NotesStatus"));
            notesCard.TextEdited += text => { notesDraft = text; notesTypedAt = ticks; };
            notesCard.TextLeft += SaveNotesDraft;
            notesCard.Added += text => NotesChange(() => notes.Add(text));
            notesCard.Toggled += (id, done) => NotesChange(() => notes.Toggle(id, done));
            notesCard.Deleted += id => NotesChange(() => notes.Delete(id));
            notesCard.ClearDoneAsked += async () => {
                int count = notes.Data.Items.Count(i => i.Done);
                if (count == 0) return;
                bool clear = await confirm.AskAsync("NOTES", count == 1 ? "Clear 1 done item?" : "Clear " + count + " done items?", "They're removed from the list on this PC and your phone.", "Clear done");
                if (clear && !disposed) NotesChange(() => notes.ClearDone());
            };
            notes.Changed += () => notesCard.Show(notes.Data);
            notesCard.Show(notes.Data);
        }

        // A change from the card; a failed save keeps the list as it was and says so.
        private void NotesChange(Action change)
        {
            try { change(); }
            catch (ArgumentException) { }
            catch (Exception e) { Notify("Couldn't save your notes: " + e.GetBaseException().Message); notesCard.Show(notes.Data); }
        }

        private void SaveNotesDraft()
        {
            if (notesDraft == null) return;
            string draft = notesDraft; notesDraft = null;
            NotesChange(() => notes.SetText(draft));
        }
        // Typing is saved once a second has passed without a key.
        private void TickNotes() { if (notesDraft != null && ticks - notesTypedAt >= 1) SaveNotesDraft(); }

        // ----- The phone -----

        private Dictionary<string, object> RemoteNotes()
        {
            var data = notes.Data;
            return D("version", data.Version, "text", notesDraft ?? data.Text, "items", data.Items.Select(i => (object)D("id", i.Id, "text", i.Text, "done", i.Done)).ToArray());
        }

        private RemoteResponse RemoteNotesCommand(string action, Dictionary<string, object> body)
        {
            try
            {
                switch (action)
                {
                    // The phone's text replaces the PC's, unless the PC's box is being typed in right now.
                    case "notes.text":
                        if (notesCard.Typing && notesDraft != null) throw new InvalidOperationException("Someone is typing in the notes on the PC. Try again in a moment.");
                        notes.SetText(Str(body, "text") ?? ""); break;
                    case "notes.add": notes.Add(Str(body, "text")); break;
                    case "notes.toggle": notes.Toggle(Str(body, "id"), body.ContainsKey("done") ? (bool?)Flag(body, "done") : null); break;
                    case "notes.delete": notes.Delete(Str(body, "id")); break;
                    case "notes.clearDone": notes.ClearDone(); break;
                    case "notes.move": notes.Move(Str(body, "id"), (int)Required(body, "to")); break;
                    default: throw new ArgumentException("Unknown notes request. Update Widget Screen on the phone or the PC.");
                }
            }
            catch (System.IO.IOException e) { throw new InvalidOperationException("The PC couldn't save the notes: " + e.Message); }
            catch (UnauthorizedAccessException e) { throw new InvalidOperationException("The PC couldn't save the notes: " + e.Message); }
            return RemoteResponse.Json(200, D("ok", true, "notes", RemoteNotes()));
        }
    }
}
