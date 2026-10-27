using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Selene
{
    // Where one game's screenshots and Steam art come from, when the automatic match is wrong (an older or emulated game that
    // shares its name with another one on Steam). Chosen in Game options > Metadata source; kept per game in
    // media-sources.json in Selene Core's data folder.
    public class MediaChoice
    {
        public string Source { get; set; }    // "auto", "steam" or "igdb"
        public string SteamId { get; set; }   // a Steam AppID (the number in the store page's address)
        public string IgdbId { get; set; }    // an IGDB game id
        public MediaChoice() { Source = "auto"; }
    }

    public static class MediaSource
    {
        private static readonly object gate = new object();
        private static Dictionary<string, MediaChoice> map;
        private static string FileName { get { return Path.Combine(SelenePaths.DataFolder, "media-sources.json"); } }

        private static Dictionary<string, MediaChoice> Map()
        {
            if (map != null) return map;
            try
            {
                if (File.Exists(FileName)) map = new JavaScriptSerializer().Deserialize<Dictionary<string, MediaChoice>>(File.ReadAllText(FileName));
            }
            catch (Exception ex) { HomeNav.Log("media sources: " + ex.Message); }
            return map ?? (map = new Dictionary<string, MediaChoice>());
        }

        public static MediaChoice Get(Guid id)
        {
            lock (gate)
            {
                MediaChoice c;
                return Map().TryGetValue(id.ToString(), out c) && c != null ? new MediaChoice { Source = c.Source ?? "auto", SteamId = c.SteamId, IgdbId = c.IgdbId } : new MediaChoice();
            }
        }

        // the game's own choice, or null while it is automatic (or the switch is off)
        public static MediaChoice Override(Guid id)
        {
            if (!Features.On("metasource")) return null;
            var c = Get(id);
            return c.Source == "auto" && string.IsNullOrEmpty(c.SteamId) && string.IsNullOrEmpty(c.IgdbId) ? null : c;
        }

        public static void Set(Guid id, MediaChoice choice)
        {
            lock (gate)
            {
                var m = Map();
                if (choice.Source == "auto" && string.IsNullOrEmpty(choice.SteamId) && string.IsNullOrEmpty(choice.IgdbId)) m.Remove(id.ToString());
                else m[id.ToString()] = choice;
                try
                {
                    Directory.CreateDirectory(SelenePaths.DataFolder);
                    File.WriteAllText(FileName, new JavaScriptSerializer().Serialize(m), Encoding.UTF8);
                }
                catch (Exception ex) { HomeNav.Log("media sources: can't save: " + ex.Message); }
            }
        }

        public static string SourceName(Guid id)
        {
            var s = Get(id).Source;
            return s == "steam" ? "Steam" : s == "igdb" ? "IGDB" : "Automatic";
        }

        public static void CycleSource(Guid id)
        {
            var c = Get(id);
            c.Source = c.Source == "auto" ? "steam" : c.Source == "steam" ? "igdb" : "auto";
            Set(id, c);
        }
    }

    // Typing a number (a Steam AppID or an IGDB id): the keypad with digits, Save keeps it, empty clears it back to automatic.
    public class MediaIdScreen : Screen
    {
        private readonly Func<Screen> back;
        private readonly Action<string> saved;
        private readonly KeyPad pad;
        private readonly TextBlock value = Ui.Text("", 30);
        private readonly TextBlock note;
        private string text;
        public override bool WantsText { get { return true; } }
        public override bool MenuOnStart { get { return false; } }

        public MediaIdScreen(string title, string help, string current, Action<string> saved, Func<Screen> back)
        {
            this.back = back; this.saved = saved;
            text = current ?? "";
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 900 };
            body.Children.Add(Ui.Title(title));
            note = Ui.Sub(help);
            note.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(note);
            var box = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(24, 14, 24, 16), Margin = new Thickness(0, 26, 0, 0), Background = Ink.Brush("SeleneGlassColor", 0.8), BorderBrush = Ink.Brush("SeleneFocusColor"), BorderThickness = new Thickness(2), Child = value, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 420 };
            body.Children.Add(box);
            pad = new KeyPad("Save", 62, 9, new[] { "1234567890" }, false);
            pad.Typed = Type; pad.Erased = Erase; pad.Done = Save;
            pad.Root.Margin = new Thickness(0, 30, 0, 0);
            pad.Root.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(pad.Root);
            Root.Children.Add(body);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Show();
        }

        private void Show() { value.Text = text.Length == 0 ? "Automatic" : text; value.Opacity = text.Length == 0 ? 0.5 : 1; }
        public override void Shown() { pad.Focus(true); }

        public override void Type(string typed)
        {
            foreach (var c in typed) if (char.IsDigit(c) && text.Length < 12) text += c;
            Show();
            pad.After(text);
        }

        public override void Erase() { if (text.Length > 0) text = text.Substring(0, text.Length - 1); Show(); pad.After(text); }

        private void Save()
        {
            Ui.Chime();
            saved(text.Trim());
            LoginScene.Show(back(), -1);
        }

        public override void Handle(string button)
        {
            if (button == "B") { Ui.Back(); LoginScene.Show(back(), -1); return; }
            if (button.StartsWith("DPad")) { pad.Move(button); return; }
            pad.Press(button);
        }
    }
}
