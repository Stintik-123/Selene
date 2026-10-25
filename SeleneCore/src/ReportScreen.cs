using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // Report a problem, no account needed: pick what went wrong (Up / Down), type a line if there's a keyboard, Send. The
    // versions and the recent logs go along (names, folders and emails taken out) to Selene's report service, which opens a
    // GitHub issue; a code to scan then follows it. If sending fails, a code opens GitHub's own form instead.
    public class ReportScreen : Screen
    {
        private const string Service = "https://selene-report.mattxslv.workers.dev/report";
        private const string Form = "https://github.com/mattxslv/Selene/issues/new?template=bug_report.yml";
        private static readonly string[] Kinds = { "Something doesn't work", "Something looks wrong", "Selene froze or closed", "Controller or navigation", "Sound or music", "Other" };

        private readonly Func<Screen> back;
        private readonly List<Border> rows = new List<Border>();
        private readonly Pill send = new Pill("Send report", 260);
        private readonly TextBlock noteText, status;
        private readonly Border noteBox, codeTile;
        private readonly Image code = new Image { Width = 220, Height = 220 };
        private readonly FrameworkElement[] rising;
        private string note = "";
        private int kind;
        private bool onSend, sending, finished;

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Ink.Brush("SeleneScrimColor", 0.94); } }
        public override bool WantsText { get { return !sending && !finished; } }

        public ReportScreen(Func<Screen> back)
        {
            this.back = back;
            var layout = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(720) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 60, 0) };
            var title = new TextBlock { Text = "Report a problem", FontSize = 44, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor") };
            var ask = Line("What went wrong?", 0.6, 19);
            ask.Margin = new Thickness(4, 8, 0, 18);
            left.Children.Add(title);
            left.Children.Add(ask);
            foreach (var k in Kinds)
            {
                var row = new Border
                {
                    Child = new TextBlock { Text = k, FontSize = 21, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor") },
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 13, 22, 14), Margin = new Thickness(0, 0, 0, 8),
                    BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Ink.Brush("SeleneGlassColor", 0.7)
                };
                rows.Add(row);
                left.Children.Add(row);
            }
            layout.Children.Add(left);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(right, 1);
            noteText = new TextBlock { FontSize = 20, FontFamily = Ui.Body, TextWrapping = TextWrapping.Wrap };
            noteBox = new Border { Child = noteText, MinHeight = 120, CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 16, 22, 16), Background = Ink.Brush("SeleneInkColor", 0.08), BorderBrush = Ink.Brush("SeleneInkColor", 0.18), BorderThickness = new Thickness(1) };
            var privacy = Line("Your Selene, Playnite and Windows versions and the recent logs go with it, with names, folders and emails taken out. The report shows up publicly on GitHub. No account needed.", 0.6, 16);
            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 26, 0, 0) };
            buttonRow.Children.Add(send.Root);
            status = Line("", 0.85, 18);
            status.Margin = new Thickness(0, 20, 0, 0);
            codeTile = new Border { Child = code, Background = Brushes.White, CornerRadius = new CornerRadius(16), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 0), Visibility = Visibility.Collapsed };
            RenderOptions.SetEdgeMode(code, EdgeMode.Aliased);
            foreach (var el in new FrameworkElement[] { noteBox, privacy, buttonRow, status, codeTile }) right.Children.Add(el);
            layout.Children.Add(right);
            Root.Children.Add(layout);
            rising = new FrameworkElement[] { title, ask, noteBox, privacy, buttonRow };
            ShowNote();

            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
        }

        private static TextBlock Line(string text, double alpha, double size)
        {
            return new TextBlock { Text = text, FontSize = size, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", alpha), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), LineHeight = size * 1.45 };
        }

        public override void Shown()
        {
            for (int i = 0; i < rising.Length; i++) Ui.Rise(rising[i], 40 + i * 50, 14, 300);
            for (int i = 0; i < rows.Count; i++) Ui.Rise(rows[i], 120 + i * 45, 12, 280);
            Ui.Play("OpenPanel.wav", 0.8);
            ShowFocus();
        }

        private void ShowFocus()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                bool focused = !onSend && i == kind, picked = onSend && i == kind;
                rows[i].BorderBrush = focused ? Ink.Brush("SeleneFocusColor") : picked ? Ink.Brush("SeleneFocusColor", 0.45) : Brushes.Transparent;
                Ui.ScaleTo(rows[i], focused ? 1.02 : 1, 160);
            }
            send.Focus(onSend);
        }

        private void ShowNote()
        {
            bool empty = note.Length == 0;
            noteText.Text = empty ? "Anything to add? Type it on a keyboard (optional)." : note;
            noteText.Foreground = Ink.Brush("SeleneInkColor", empty ? 0.45 : 0.95);
        }

        public override void Type(string text)
        {
            if (note.Length >= 600) return;
            note += text;
            ShowNote();
        }

        public override void Erase()
        {
            if (note.Length == 0) return;
            note = note.Substring(0, note.Length - 1);
            ShowNote();
        }

        public override void Handle(string b)
        {
            if (sending) return;
            if (finished) { if (b == "A" || b == "B") { Ui.Back(); LoginScene.Show(back(), -1); } return; }
            if (b == "B")
            {
                Ui.Back();
                if (onSend) { onSend = false; ShowFocus(); } else LoginScene.Show(back(), -1);
                return;
            }
            if (!onSend && (b == "DPadUp" || b == "DPadDown"))
            {
                int next = kind + (b == "DPadDown" ? 1 : -1);
                if (next < 0 || next >= rows.Count) { Ui.Shake(rows[kind]); return; }
                kind = next;
                Ui.Tick();
                ShowFocus();
                return;
            }
            if (!onSend && (b == "A" || b == "DPadRight" || b == "Start")) { if (b == "A") Ui.Confirm(); else Ui.Tick(); onSend = true; ShowFocus(); return; }
            if (onSend && b == "DPadLeft") { Ui.Tick(); onSend = false; ShowFocus(); return; }
            if (onSend && (b == "A" || b == "Start")) { Ui.Confirm(); Send(); }
        }

        private void Send()
        {
            sending = true;
            send.Text = "Sending...";
            Say("Sending your report...");
            var report = new Dictionary<string, object>
            {
                { "category", Kinds[kind] }, { "note", Scrub(note) }, { "selene", Brand.Version }, { "playnite", PlayniteVersion() },
                { "windows", WindowsName() }, { "seleneLog", Scrub(Tail(Path.Combine(Path.GetTempPath(), "selene.log"), 400)) },
                { "playniteLog", Scrub(Tail(HomeNav.Api != null ? Path.Combine(HomeNav.Api.Paths.ConfigurationPath, "playnite.log") : "", 200)) },
            };
            var dispatcher = Root.Dispatcher;
            Task.Factory.StartNew(() =>
            {
                string url = null, error = null;
                try
                {
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

        private void Done(string url, string error)
        {
            sending = false;
            finished = true;
            send.Root.Visibility = Visibility.Collapsed;
            if (url != null)
            {
                Ui.Play("Success.wav", 0.8);
                Say("Thanks! Your report is on GitHub. Scan the code to follow it, or add screenshots there.");
                ShowCode(url);
                HomeNav.Log("report: sent, " + url);
            }
            else
            {
                HomeNav.Log("report: sending failed: " + error);
                Say(error == "slow down" ? "That's a few reports in a row. Please try again in an hour, or scan the code to report on GitHub."
                                         : "Couldn't send it (no internet?). Scan the code to report on GitHub instead.");
                ShowCode(Form + "&selene=" + Uri.EscapeDataString(Brand.Version) + "&playnite=" + Uri.EscapeDataString(PlayniteVersion()));
            }
        }

        private void ShowCode(string link)
        {
            try { code.Source = new QrCode(link).Picture(); }
            catch (Exception ex) { HomeNav.Log("report: code: " + ex.Message); return; }
            codeTile.Visibility = Visibility.Visible;
            codeTile.Opacity = 0;
            Ui.Fade(codeTile, 1, 260);
            Ui.Rise(codeTile, 0, 12, 280);
        }

        private void Say(string text)
        {
            status.Text = text;
            Ui.Pop(status);
        }

        private static string Tail(string path, int lines)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
                string all;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs)) all = r.ReadToEnd();
                var split = all.Split('\n');
                int from = Math.Max(0, split.Length - lines);
                return string.Join("\n", split, from, split.Length - from);
            }
            catch (Exception ex) { return "(couldn't read: " + ex.Message + ")"; }
        }

        // the service takes out home folders and emails too; the Windows user and PC names go here as well
        private static string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var name in new[] { Environment.UserName, Environment.MachineName })
                if (!string.IsNullOrEmpty(name) && name.Length >= 3)
                    text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(name), "<user>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
