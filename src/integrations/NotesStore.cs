using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace WidgetScreen
{
    // One line of the to-do list.
    public sealed class TodoItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
        public TodoItem Copy() { return (TodoItem)MemberwiseClone(); }
    }

    // The Notes widget's scratchpad and to-do list (docs/design/notes.md). The PC owns them; a phone edits them
    // through the link. Version goes up with every change, so the phone can tell its copy is old.
    public sealed class NotesData
    {
        public string Text { get; set; }
        public List<TodoItem> Items { get; set; }
        [XmlIgnore] public int Version { get; set; }
        public NotesData() { Text = ""; Items = new List<TodoItem>(); }
        public NotesData Copy() { return new NotesData { Text = Text, Items = Items.Select(i => i.Copy()).ToList(), Version = Version }; }
    }

    // notes.xml next to settings.xml, written atomically like the settings. Limits keep a phone request under the
    // link's 64 KB and the list quick to draw.
    public sealed class NotesStore
    {
        public const int MaxText = 16000, MaxItems = 200, MaxItem = 200;
        private readonly string path;
        public NotesData Data { get; private set; }
        public string LoadWarning { get; private set; }
        public event Action Changed;

        public static string PathFor(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "notes.xml"); }

        public NotesStore(string path)
        {
            this.path = path; Data = new NotesData();
            if (!File.Exists(path)) return;
            try { using (var file = File.OpenRead(path)) Data = Clean((NotesData)new XmlSerializer(typeof(NotesData)).Deserialize(file)); }
            catch (Exception e)
            {
                if (!(e is IOException) && !(e is InvalidOperationException) && !(e is UnauthorizedAccessException)) throw;
                LoadWarning = "Your notes couldn't be read. The file is kept until you change a note.";
            }
        }

        public static NotesData Clean(NotesData data)
        {
            data = data ?? new NotesData();
            data.Text = Limit(data.Text ?? "", MaxText);
            data.Items = (data.Items ?? new List<TodoItem>()).Where(i => i != null && !String.IsNullOrWhiteSpace(i.Text)).Take(MaxItems).ToList();
            var ids = new HashSet<string>();
            foreach (var item in data.Items)
            {
                item.Text = Line(item.Text);
                if (String.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) { item.Id = NewId(); ids.Add(item.Id); }
            }
            return data;
        }
        private static string Limit(string text, int length) { return text.Length > length ? text.Substring(0, length) : text; }
        // A to-do is one line: line breaks become spaces, and it's trimmed to 200 characters.
        public static string Line(string text) { return Limit(String.Join(" ", (text ?? "").Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim(), MaxItem); }
        private static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        private void Save()
        {
            Data.Version++;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            using (var file = File.Create(temporary)) new XmlSerializer(typeof(NotesData)).Serialize(file, Data);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            LoadWarning = null;
            if (Changed != null) Changed();
        }

        // Each change saves at once; a failed save puts the notes back and throws, so nothing pretends to be saved.
        private void Change(Action change)
        {
            var before = Data.Copy();
            change();
            try { Save(); }
            catch (Exception) { Data = before; throw; }
        }

        public void SetText(string text) { text = Limit(text ?? "", MaxText); if (text == Data.Text) return; Change(() => Data.Text = text); }
        public TodoItem Add(string text)
        {
            string line = Line(text);
            if (line.Length == 0) throw new ArgumentException("Type something to add.");
            if (Data.Items.Count >= MaxItems) throw new InvalidOperationException("The list is full. Clear done items first.");
            var item = new TodoItem { Id = NewId(), Text = line };
            Change(() => Data.Items.Add(item));
            return item;
        }
        private TodoItem Find(string id)
        {
            var item = Data.Items.FirstOrDefault(i => i.Id == id);
            if (item == null) throw new InvalidOperationException("That item isn't on the list anymore.");
            return item;
        }
        public void Toggle(string id, bool? done = null) { var item = Find(id); bool next = done ?? !item.Done; Change(() => Data.Items.First(i => i.Id == id).Done = next); }
        public void Delete(string id) { Find(id); Change(() => Data.Items.RemoveAll(i => i.Id == id)); }
        public int ClearDone() { int count = Data.Items.Count(i => i.Done); if (count > 0) Change(() => Data.Items.RemoveAll(i => i.Done)); return count; }
        public void Move(string id, int to)
        {
            var item = Find(id); to = Math.Max(0, Math.Min(Data.Items.Count - 1, to));
            if (Data.Items.IndexOf(item) == to) return;
            Change(() => { var moved = Data.Items.First(i => i.Id == id); Data.Items.Remove(moved); Data.Items.Insert(to, moved); });
        }
    }
}
