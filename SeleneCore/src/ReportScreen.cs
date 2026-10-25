using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // Report a problem: a QR code that opens a GitHub bug report on a phone with the versions filled in, the same link in
    // the browser, and the logs saved to a Desktop folder to attach. Left / Right pick a button, A runs it, B goes back.
    public class ReportScreen : Screen
    {
        private const string Issues = "https://github.com/mattxslv/Selene/issues/new";

        private readonly Func<Screen> back;
        private readonly Pill[] buttons;
        private readonly TextBlock status;
        private readonly FrameworkElement[] rising;
        private readonly string link;
        private int index;

        public override bool Overlay { get { return true; } }
        public override Brush OverlayShade { get { return Ink.Brush("SeleneScrimColor", 0.94); } }

        public ReportScreen(Func<Screen> back)
        {
            this.back = back;
            link = Issues + "?template=bug_report.yml&selene=" + Uri.EscapeDataString(Brand.Version) + "&playnite=" + Uri.EscapeDataString(PlayniteVersion())
                 + (WindowsName() != null ? "&windows=" + Uri.EscapeDataString(WindowsName()) : "");

            var layout = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(720) });

            // the code: dark on white (a phone camera needs the contrast), sharp at any size
            var code = new Image { Source = new QrCode(link).Picture(), Width = 340, Height = 340 };
            RenderOptions.SetEdgeMode(code, EdgeMode.Aliased);
            var tile = new Border { Child = code, Background = Brushes.White, CornerRadius = new CornerRadius(20), Padding = new Thickness(10), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 70, 0) };
            layout.Children.Add(tile);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(words, 1);
            var title = new TextBlock { Text = "Report a problem", FontSize = 44, FontFamily = Ui.Display, Foreground = Ink.Brush("SeleneInkColor") };
            var scan = Line("Scan the code with your phone to open a bug report on GitHub. Your Selene, Playnite and Windows versions are already filled in.", 0.85, 22);
            var logs = Line("Logs help a lot: save them below and they land in a folder on your Desktop, ready to drag into the report.", 0.65, 19);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 34, 0, 0) };
            buttons = new[] { new Pill("Open in browser"), new Pill("Save logs to Desktop") };
            foreach (var b in buttons) row.Children.Add(b.Root);
            status = Line("github.com/mattxslv/Selene/issues", 0.5, 16);
            status.Margin = new Thickness(0, 22, 0, 0);
            foreach (var el in new FrameworkElement[] { title, scan, logs, row, status }) words.Children.Add(el);
            layout.Children.Add(words);
            Root.Children.Add(layout);
            rising = new FrameworkElement[] { tile, title, scan, logs, row, status };

            Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
        }

        private static TextBlock Line(string text, double alpha, double size)
        {
            return new TextBlock { Text = text, FontSize = size, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor", alpha), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), LineHeight = size * 1.45 };
        }

        private static string PlayniteVersion()
        {
            try { var v = HomeNav.Api.ApplicationInfo.ApplicationVersion; return v.Major + "." + v.Minor; } catch { return ""; }
        }

        private static string WindowsName()
        {
            var v = Environment.OSVersion.Version;
            if (v.Major != 10) return null;
            return v.Build >= 22000 ? "Windows 11" : "Windows 10";
        }

        public override void Shown()
        {
            for (int i = 0; i < rising.Length; i++) Ui.Rise(rising[i], 40 + i * 50, 14, 300);
            Ui.Play("OpenPanel.wav", 0.8);
            ShowFocus();
        }

        private void ShowFocus()
        {
            for (int i = 0; i < buttons.Length; i++) buttons[i].Focus(i == index);
        }

        public override void Handle(string b)
        {
            if (b == "B") { Ui.Back(); LoginScene.Show(back(), -1); return; }
            if (b == "DPadLeft" || b == "DPadRight")
            {
                int next = index + (b == "DPadRight" ? 1 : -1);
                if (next < 0 || next >= buttons.Length) { Ui.Shake(buttons[index].Root); return; }
                index = next;
                Ui.Tick();
                ShowFocus();
                return;
            }
            if (b != "A") return;
            Ui.Confirm();
            if (index == 0) OpenInBrowser(); else SaveLogs();
        }

        private void OpenInBrowser()
        {
            try { Process.Start(link); Say("Opened in your browser."); }
            catch (Exception ex) { HomeNav.Log("report: browser failed: " + ex.Message); Say("Couldn't open the browser. Scan the code instead."); }
        }

        private void SaveLogs()
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Selene report " + DateTime.Now.ToString("yyyy-MM-dd HHmm"));
                Directory.CreateDirectory(folder);
                Copy(Path.Combine(Path.GetTempPath(), "selene.log"), Path.Combine(folder, "selene.log"));
                if (HomeNav.Api != null) Copy(Path.Combine(HomeNav.Api.Paths.ConfigurationPath, "playnite.log"), Path.Combine(folder, "playnite.log"));
                File.WriteAllText(Path.Combine(folder, "versions.txt"),
                    "Selene " + Brand.Version + "\r\nPlaynite " + PlayniteVersion() + "\r\n" + (WindowsName() ?? "Windows") + " (" + Environment.OSVersion.Version + ")\r\n");
                Ui.Play("Success.wav", 0.8);
                Say("Saved to your Desktop: " + Path.GetFileName(folder) + ". Drag the files into the report.");
            }
            catch (Exception ex) { HomeNav.Log("report: saving logs failed: " + ex.Message); Say("Couldn't save the logs: " + ex.Message); }
        }

        // the logs are open for writing (Playnite and Selene keep logging): read them shared
        private static void Copy(string from, string to)
        {
            if (!File.Exists(from)) return;
            using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = new FileStream(to, FileMode.Create, FileAccess.Write)) src.CopyTo(dst);
        }

        private void Say(string text)
        {
            status.Text = text;
            status.Foreground = Ink.Brush("SeleneInkColor", 0.85);
            Ui.Pop(status);
        }
    }
}
