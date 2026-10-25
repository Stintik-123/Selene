using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // Report a problem, no account needed, in three steps: what went wrong, where you were, anything to add (on-screen keys
    // or a keyboard). The versions and the logs go along (names, folders and emails taken out) to Selene's report service,
    // which opens a GitHub issue; a code to scan then follows it. If sending fails, the code opens GitHub's own form.
    public class ReportScreen : Screen
    {
        private const string Service = "https://selene-report.mattxslv.workers.dev/report";
        private const string Form = "https://github.com/mattxslv/Selene/issues/new?template=bug_report.yml";

        private static readonly string[] Kinds = { "Something doesn't work", "Something looks wrong", "Selene froze or closed", "Controller or navigation", "Sound or music", "Other" };
        private static readonly int[] KindIcons = { 0xe868 /* bug */, 0xe3ad /* broken image */, 0xeb3b /* frozen */, 0xea28 /* controller */, 0xe050 /* sound */, 0xe5d3 /* more */ };
        private static readonly string[] Places = { "Starting Playnite", "Spotlight", "Collection", "A game's page", "Store", "Captures", "Settings or menu", "Starting or playing a game", "Quitting", "Somewhere else" };
        private static readonly int[] PlaceIcons = { 0xeb9b, 0xe88a, 0xe8f1, 0xe88e, 0xea12, 0xe412, 0xe8b8, 0xe037, 0xe879, 0xe5d3 };

        private readonly Func<Screen> back;
        private readonly Grid stage = new Grid();
        private readonly TextBlock stepLine;
        private readonly FrameworkElement header;
        private int step;          // 0 what, 1 where, 2 note, 3 result
        private int kind, place;
        private Grid stepRoot;
        private List<Tile> tiles = new List<Tile>();
        private int columns;
        private KeyPad pad;
        private TextBlock noteText;
        private Border noteBox;
        private string note = "";
        private bool sending;

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Ink.Brush("SeleneScrimColor", 0.94); } }
        public override bool WantsText { get { return step == 2 && !sending; } }

        public ReportScreen(Func<Screen> back)
        {
            this.back = back;
            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(120, 90, 0, 0) };
            head.Children.Add(new TextBlock { Text = "Report a problem", FontSize = 42, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor") });
            stepLine = new TextBlock { FontSize = 20, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.6), Margin = new Thickness(2, 6, 0, 0) };
            head.Children.Add(stepLine);
            header = head;
            Root.Children.Add(head);
            Root.Children.Add(stage);
            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
        }

        public override void Shown()
        {
            Ui.Rise(header, 40, 14, 300);
            Ui.Play("OpenPanel.wav", 0.8);
            Go(0, 0);
        }

        // ---- the steps ----
        private void Go(int to, int dir)
        {
            step = to;
            var old = stepRoot;
            stepRoot = to == 0 ? Choices(Kinds, KindIcons, 3, 300, 150, kind)
                     : to == 1 ? Choices(Places, PlaceIcons, 5, 236, 132, place)
                     : to == 2 ? NotePage()
                     : stepRoot;
            stepLine.Text = to == 0 ? "Step 1 of 3  \u00B7  What went wrong?" : to == 1 ? "Step 2 of 3  \u00B7  Where were you?" : to == 2 ? "Step 3 of 3  \u00B7  Anything to add?" : "";
            if (ReferenceEquals(old, stepRoot)) return;
            stage.Children.Add(stepRoot);
            if (old != null) { var leaving = old; Ui.Slide(leaving, 0, -dir * 90, 1, 0, 220, () => stage.Children.Remove(leaving)); }
            Ui.Slide(stepRoot, dir * 90, 0, 0, 1, 300);
            ShowFocus();
        }

        private Grid Choices(string[] labels, int[] icons, int cols, double w, double h, int at)
        {
            columns = cols;
            tiles = new List<Tile>();
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
            var wrap = new WrapPanel { Width = cols * (w + 24) };
            for (int i = 0; i < labels.Length; i++)
            {
                var t = new Tile(w, h, 18);
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(new TextBlock { Text = Ui.Icon(icons[i]), FontFamily = Ui.Icons, FontSize = 40, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center });
                content.Children.Add(new TextBlock { Text = labels[i], FontSize = 19, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 12, 14, 0) });
                t.Face.Child = content;
                t.Root.Margin = new Thickness(12);
                wrap.Children.Add(t.Root);
                tiles.Add(t);
                Ui.Rise(t.Root, 60 + i * 40, 12, 280);
            }
            grid.Children.Add(wrap);
            return grid;
        }

        private Grid NotePage()
        {
            tiles = new List<Tile>();
            var page = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 70, 0, 0) };
            var col = new StackPanel { Width = 960 };
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            chips.Children.Add(Chip(KindIcons[kind], Kinds[kind]));
            chips.Children.Add(Chip(PlaceIcons[place], Places[place]));
            col.Children.Add(chips);
            noteText = new TextBlock { FontSize = 21, FontFamily = Ui.Body, TextWrapping = TextWrapping.Wrap };
            noteBox = new Border { Child = noteText, MinHeight = 96, CornerRadius = new CornerRadius(18), Padding = new Thickness(24, 18, 24, 18), Background = Ink.Brush("SeleneInkColor", 0.08), BorderBrush = Ink.Brush("SeleneFocusColor", 0.7), BorderThickness = new Thickness(2) };
            col.Children.Add(noteBox);
            pad = new KeyPad("Send");
            pad.Root.Margin = new Thickness(0, 26, 0, 0);
            pad.Typed = Type;
            pad.Erased = Erase;
            pad.Done = Send;
            col.Children.Add(pad.Root);
            col.Children.Add(new TextBlock
            {
                Text = "Sent with it: your Selene, Playnite and Windows versions and Selene's recent logs, with names, folders and emails taken out. It shows up publicly on GitHub. No account needed.",
                FontSize = 15, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.5), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(40, 18, 40, 0)
            });
            page.Children.Add(col);
            ShowNote();
            return page;
        }

        private static Border Chip(int icon, string text)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = Ui.Icon(icon), FontFamily = Ui.Icons, FontSize = 20, Foreground = Ink.Brush("SeleneInkColor", 0.85), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            line.Children.Add(new TextBlock { Text = text, FontSize = 17, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.85), VerticalAlignment = VerticalAlignment.Center });
            return new Border { Child = line, CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 7, 16, 8), Margin = new Thickness(0, 0, 10, 0), Background = Ink.Brush("SeleneInkColor", 0.1) };
        }

        private void ShowNote()
        {
            bool empty = note.Length == 0;
            noteText.Text = empty ? "What happened? A few words help a lot (optional)." : note + "\u258F";
            noteText.Foreground = Ink.Brush("SeleneInkColor", empty ? 0.45 : 0.95);
            if (pad != null) pad.After(note);
        }

        private void ShowFocus()
        {
            int at = step == 0 ? kind : place;
            for (int i = 0; i < tiles.Count; i++) tiles[i].Focus(i == at);
            if (step == 2 && pad != null) pad.Focus(true);
        }

        public override void Type(string text)
        {
            if (step != 2 || sending) return;
            if (note.Length + text.Length > 500) { Ui.Shake(noteBox); return; }
            note += text;
            Ui.Key();
            ShowNote();
        }

        public override void Erase()
        {
            if (step != 2 || sending) return;
            if (note.Length == 0) { Ui.Shake(noteBox); return; }
            note = note.Substring(0, note.Length - 1);
            Ui.Key();
            ShowNote();
        }

        public override void Handle(string b)
        {
            if (sending) return;
            if (step == 3) { if (b == "A" || b == "B") { Ui.Back(); LoginScene.Show(back(), -1); } return; }
            if (b == "B")
            {
                Ui.Back();
                if (step == 0) LoginScene.Show(back(), -1); else Go(step - 1, -1);
                return;
            }
            if (step == 2)
            {
                if (b.StartsWith("DPad")) { if (!pad.Move(b)) Ui.Shake(noteBox); return; }
                pad.Press(b);
                return;
            }
            int at = step == 0 ? kind : place;
            if (b.StartsWith("DPad"))
            {
                int next = at + (b == "DPadRight" ? 1 : b == "DPadLeft" ? -1 : b == "DPadDown" ? columns : -columns);
                bool sameRow = b == "DPadUp" || b == "DPadDown" || next / columns == at / columns;
                if (next < 0 || next >= tiles.Count || !sameRow) { Ui.Shake(tiles[at].Root); return; }
                if (step == 0) kind = next; else place = next;
                Ui.Tick();
                ShowFocus();
                return;
            }
            if (b == "A") { Ui.Confirm(); Go(step + 1, 1); }
        }

        // ---- sending ----
        private void Send()
        {
            if (sending) return;
            sending = true;
            Ui.Confirm();
            pad.DoneText = "Sending...";
            var report = new Dictionary<string, object>
            {
                { "category", Kinds[kind] }, { "place", Places[place] }, { "note", Scrub(note.Trim()) },
                { "selene", Brand.Version }, { "playnite", PlayniteVersion() }, { "windows", WindowsName() },
            };
            var dispatcher = Root.Dispatcher;
            Task.Factory.StartNew(() =>
            {
                string url = null, error = null;
                try
                {
                    var seleneLog = ReadLines(Path.Combine(Path.GetTempPath(), "selene.log"));
                    var playniteLog = ReadLines(HomeNav.Api != null ? Path.Combine(HomeNav.Api.Paths.ConfigurationPath, "playnite.log") : "");
                    report["seleneLog"] = Scrub(string.Join("\n", Last(seleneLog.Where(l => !Noise(l)), 350)));
                    report["seleneProblems"] = Scrub(string.Join("\n", Last(seleneLog.Where(Problem), 40)));
                    report["playniteLog"] = Scrub(string.Join("\n", Last(playniteLog.Where(l => l.Contains("|ERROR") || l.Contains("|WARN") || l.StartsWith("   ") || l.StartsWith("System.")), 120)));
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var body = Encoding.UTF8.GetBytes(js.Serialize(report));
                    var request = (HttpWebRequest)WebRequest.Create(Service);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.UserAgent = "Selene/" + Brand.Version;
                    request.Timeout = 20000;
                    using (var s = request.GetRequestStream()) s.Write(body, 0, body.Length);
                    string text;
                    try { using (var response = request.GetResponse()) using (var r = new StreamReader(response.GetResponseStream())) text = r.ReadToEnd(); }
                    catch (WebException ex) { if (ex.Response == null) throw; using (var r = new StreamReader(ex.Response.GetResponseStream())) text = r.ReadToEnd(); }
                    var result = js.Deserialize<Dictionary<string, object>>(text);
                    object ok, link, err;
                    if (result.TryGetValue("ok", out ok) && ok is bool && (bool)ok && result.TryGetValue("url", out link)) url = link as string;
                    else error = result.TryGetValue("error", out err) ? err as string : "unknown";
                }
                catch (Exception ex) { error = ex.Message; }
                dispatcher.BeginInvoke(new Action(() => Done(url, error)));
            });
        }

        // the frame timings and polling lines fill a log without saying what happened
        private static bool Noise(string line)
        {
            foreach (var n in new[] { "slow job", "frame gap", "frames: ", "UPS ", "fade image", "watch: ", "details view found", "screenshots show", "full refresh rate" })
                if (line.IndexOf(n, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static bool Problem(string line)
        {
            return Regex.IsMatch(line, @"fail|error|exception|couldn't|not found|crash", RegexOptions.IgnoreCase) && !Noise(line);
        }

        private void Done(string url, string error)
        {
            sending = false;
            bool ok = url != null;
            if (ok) { Ui.Play("Success.wav", 0.8); HomeNav.Log("report: sent, " + url); }
            else HomeNav.Log("report: sending failed: " + error);
            var number = ok ? url.Substring(url.LastIndexOf('/') + 1) : null;
            var card = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(48), HorizontalAlignment = HorizontalAlignment.Center, Background = ok ? Ink.Brush("SeleneFocusColor") : Ink.Brush("SeleneInkColor", 0.14) };
            badge.Child = new TextBlock { Text = Ui.Icon(ok ? 0xe876 : 0xe000), FontFamily = Ui.Icons, FontSize = 52, Foreground = ok ? Ink.Brush("SeleneOnFocusColor") : Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            card.Children.Add(badge);
            card.Children.Add(new TextBlock { Text = ok ? "Thanks, report #" + number + " is sent" : "Couldn't send it", FontSize = 38, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) });
            card.Children.Add(new TextBlock
            {
                Text = ok ? "Scan the code to follow it on GitHub, or to add screenshots."
                     : error == "slow down" ? "That's a few reports in a row. Try again in an hour, or scan the code to report on GitHub."
                     : "No internet, maybe. Scan the code to report on GitHub instead.",
                FontSize = 20, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", 0.65), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 700, Margin = new Thickness(0, 10, 0, 0)
            });
            try
            {
                var link = ok ? url : Form + "&selene=" + Uri.EscapeDataString(Brand.Version) + "&playnite=" + Uri.EscapeDataString(PlayniteVersion());
                var code = new Image { Source = new QrCode(link).Picture(), Width = 230, Height = 230 };
                RenderOptions.SetEdgeMode(code, EdgeMode.Aliased);
                card.Children.Add(new Border { Child = code, Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) });
            }
            catch (Exception ex) { HomeNav.Log("report: code: " + ex.Message); }
            var result = new Grid();
            result.Children.Add(card);
            var page = stepRoot;
            step = 3;
            stepRoot = result;
            stage.Children.Add(result);
            if (page != null) Ui.Fade(page, 0, 200, 0, () => stage.Children.Remove(page));
            Ui.Fade(header, 0, 200);
            for (int i = 0; i < card.Children.Count; i++) Ui.Rise((UIElement)card.Children[i], 40 + i * 60, 14, 320);
        }

        private static List<string> ReadLines(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<string>();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs)) return r.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            }
            catch (Exception ex) { return new List<string> { "(couldn't read: " + ex.Message + ")" }; }
        }

        private static IEnumerable<string> Last(IEnumerable<string> lines, int n)
        {
            var list = lines.ToList();
            return list.Skip(Math.Max(0, list.Count - n));
        }

        // the service takes out home folders and emails too; the Windows user and PC names go here as well
        private static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var name in new[] { Environment.UserName, Environment.MachineName })
                if (!string.IsNullOrEmpty(name) && name.Length >= 3)
                    text = Regex.Replace(text, Regex.Escape(name), "<user>", RegexOptions.IgnoreCase);
            return text;
        }

        private static string PlayniteVersion()
        {
            try { var v = HomeNav.Api.ApplicationInfo.ApplicationVersion; return v.Major + "." + v.Minor; } catch { return ""; }
        }

        private static string WindowsName()
        {
            var v = Environment.OSVersion.Version;
            if (v.Major != 10) return "Windows " + v;
            return (v.Build >= 22000 ? "Windows 11" : "Windows 10") + " (" + v.Build + ")";
        }
    }
}
