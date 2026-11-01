using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WidgetScreen;

// Notes and to-do (docs/design/notes.md): the store and its limits, the card's designs and actions, and typing saved
// when the dashboard closes. Everything runs in the test folder; the user's own notes are never touched.
internal static class NotesTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Click(Window window, string name) { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static string Settings(string directory, string name, Size size, double scale)
    {
        string folder = Path.Combine(directory, "notes-" + name); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.xml");
        if (File.Exists(NotesStore.PathFor(path))) File.Delete(NotesStore.PathFor(path));
        var settings = new AppSettings { TextScale = scale };
        settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Notes"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
        SettingsStore.Save(path, settings);
        return path;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Notes keep text and a to-do list, with limits, and save atomically", () => {
            string path = Path.Combine(directory, "notes-store", "notes.xml");
            if (File.Exists(path)) File.Delete(path);
            var store = new NotesStore(path);
            Assert(store.Data.Text == "" && store.Data.Items.Count == 0 && store.LoadWarning == null, "A new store isn't empty.");
            store.SetText("Shopping\r\nmilk"); var a = store.Add("  Call the\nbank  "); var b = store.Add("Pay rent"); var c = store.Add("Water plants");
            Assert(a.Text == "Call the bank" && store.Data.Items.Count == 3 && store.Data.Version == 4, "Adding didn't clean the line or count the version.");
            store.Toggle(b.Id); store.Toggle(c.Id, true); store.Move(c.Id, 0);
            Assert(store.Data.Items[0].Id == c.Id && store.Data.Items.Count(i => i.Done) == 2, "Toggling or moving failed.");
            Assert(store.ClearDone() == 2 && store.Data.Items.Single().Id == a.Id, "Clear done didn't remove only the done items.");
            store.Delete(a.Id);
            bool threw = false; try { store.Add("   "); } catch (ArgumentException) { threw = true; } Assert(threw, "An empty to-do was added.");
            threw = false; try { store.Toggle("missing"); } catch (InvalidOperationException) { threw = true; } Assert(threw, "A missing item didn't say so.");
            store.SetText(new string('x', NotesStore.MaxText + 50)); Assert(store.Data.Text.Length == NotesStore.MaxText, "The text limit isn't kept.");
            Assert(NotesStore.Line(new string('y', 500)).Length == NotesStore.MaxItem, "The item limit isn't kept.");
            store.SetText("Shopping\nmilk"); store.Add("Kept");
            var again = new NotesStore(path);
            Assert(again.Data.Text == "Shopping\nmilk" && again.Data.Items.Single().Text == "Kept" && !File.Exists(path + ".tmp"), "Notes weren't saved, or the temporary file was left.");
            File.WriteAllText(path, "<NotesData><Text>broken");
            var broken = new NotesStore(path);
            Assert(broken.LoadWarning != null && broken.Data.Items.Count == 0 && File.ReadAllText(path).StartsWith("<NotesData><Text>broken"), "A damaged file wasn't kept, or no warning was given.");
        });
        check("Notes start hidden and pick small, medium and large designs", () => {
            var settings = new AppSettings();
            Assert(WidgetLayout.Ids.Contains("Notes") && WidgetLayout.Title("Notes") == "Notes and to-do" && !WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Notes").Visible, "Notes are visible by default.");
            foreach (var canvas in new[] { new Size(1856, 576), new Size(1200, 640), new Size(656, 1712) })
                Assert(!WidgetLayout.DefaultFor(settings, canvas).First(p => p.Id == "Notes").Visible, "Notes show by default on " + canvas + ".");
            Assert(CardSizing.Notes(260, 200) == CardSizing.Design.Small && CardSizing.Notes(472, 310) == CardSizing.Design.Medium && CardSizing.Notes(699, 576) == CardSizing.Design.Medium && CardSizing.Notes(700, 300) == CardSizing.Design.Large && CardSizing.NotesListColumn(652) == 300 && CardSizing.NotesListColumn(1200) == 520, "Notes designs or sizes are wrong.");
        });
        check("Notes card adds, ticks, deletes and clears to-dos, keeping its text inside the card", () => {
            foreach (var size in new[] { new Size(260, 200), new Size(472, 310), new Size(1000, 400) })
            {
                string path = Settings(directory, size.Width + "x" + size.Height, size, 1.4);
                // A labelled test fixture: made-up notes.
                var fixture = new NotesStore(NotesStore.PathFor(path));
                fixture.SetText("TEST FIXTURE: shopping list and a long line that wraps across the scratchpad to check it stays inside the card");
                foreach (string line in new[] { "Buy milk", "A very long to-do that keeps going and going so it must end in an ellipsis", "Book the dentist" }) fixture.Add(line);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            Action layout = () => { dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout(); };
                            layout();
                            Assert(controller.NotesView.Current == CardSizing.Notes(size.Width, size.Height), "Notes picked the wrong design at " + size + ".");
                            Assert(((TextBox)window.FindName("NotesText")).Text.StartsWith("TEST FIXTURE") && ((TextBlock)window.FindName("NotesStatus")).Text == "3 TO DO", "The notes weren't shown at " + size + ".");
                            if (controller.NotesView.Current != CardSizing.Design.Large) { Click(window, "NotesTabTodo"); layout(); Assert(controller.NotesView.ShowingList && window.FindName("NotesText") == null, "The To-do tab didn't show the list."); }
                            var card = (FrameworkElement)window.FindName("NotesCard");
                            foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is Button || e is TextBox || e is CheckBox) && !(e.TemplatedParent is TextBox)))
                            {
                                var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                Assert(box.Left >= 23.5 && box.Right <= card.ActualWidth - 23.5 && box.Top >= 23.5, "Notes push a " + part.GetType().Name + " out of the " + size + " card: " + box);
                                Assert(part.ActualHeight >= 47.5 || part is TextBox, "A " + part.GetType().Name + " on the " + size + " notes card is shorter than 48: " + part.ActualHeight);
                            }
                            // Add, tick, delete and clear through the card itself.
                            ((TextBox)window.FindName("NotesAdd")).Text = "Fixture: new to-do"; Click(window, "NotesAddButton");
                            Assert(controller.Notes.Data.Items.Count == 4 && controller.Notes.Data.Items[3].Text == "Fixture: new to-do", "Add didn't add.");
                            var item = (CheckBox)window.FindName("NotesItem0"); item.IsChecked = true; item.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                            Assert(controller.Notes.Data.Items[0].Done, "Ticking didn't save.");
                            Click(window, "NotesDelete1"); Assert(controller.Notes.Data.Items.Count == 3 && !controller.Notes.Data.Items.Any(i => i.Text.StartsWith("A very long")), "Delete didn't delete.");
                            Click(window, "NotesClearDone"); Click(window, "ConfirmCancel"); Assert(controller.Notes.Data.Items.Count == 3, "Cancel cleared the done items.");
                            Click(window, "NotesClearDone"); Click(window, "ConfirmAction"); Assert(controller.Notes.Data.Items.Count == 2 && !controller.Notes.Data.Items.Any(i => i.Done), "Clear done didn't clear.");
                            Assert(new NotesStore(NotesStore.PathFor(path)).Data.Items.Count == 2, "The card's changes weren't saved.");
                            // A change from elsewhere (the phone) shows on the card.
                            controller.Notes.Add("From the phone"); layout();
                            Assert(window.FindName("NotesItem2") != null && (string)((TextBlock)((CheckBox)window.FindName("NotesItem2")).Content).Text == "From the phone", "A change from the phone didn't show.");
                            if (controller.NotesView.Current != CardSizing.Design.Large) { Click(window, "NotesTabNotes"); layout(); }
                            ((TextBox)window.FindName("NotesText")).Text = "Typed on the PC";
                        }
                        // Typing that hadn't been saved yet is saved when the dashboard closes.
                        Assert(new NotesStore(NotesStore.PathFor(path)).Data.Text == "Typed on the PC", "Typing wasn't saved on close at " + size + ".");
                    }
                    finally { window.Close(); }
                }
            }
        });
    }
}
