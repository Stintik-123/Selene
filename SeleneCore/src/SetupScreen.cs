using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Selene
{
    // "Let's get Selene ready": a card per thing Selene checks (SetupCheck.Run). Cards rise in one after another;
    // A on a card installs or explains it; when something becomes ready its status pops to a check. First launch
    // goes on to the profile steps; from the menu, B or Continue goes back to the home.
    public class SetupScreen : Screen
    {
        private const int Columns = 6;
        private readonly bool firstRun;
        // where each card sits: its row and its x from the centre (rows are centred, so a short last row sits in the middle)
        private readonly List<int> cardRow = new List<int>();
        private readonly List<double> cardX = new List<double>();
        private const double CardCell = 258;
        private readonly List<Tile> cards = new List<Tile>();
        private readonly List<TextBlock> statuses = new List<TextBlock>();
        private readonly DetailPanel info;
        private readonly Pill installAll, carryOn;
        private List<SetupItem> items;
        private int index;
        private DispatcherTimer recheck;
        private readonly Queue<string> installQueue = new Queue<string>();
        private bool settingUpLudusavi;

        public SetupScreen(bool firstRun)
        {
            this.firstRun = firstRun;
            items = SetupCheck.Run(HomeNav.Api);
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -20, 0, 0) };
            col.Children.Add(Ui.Title("Let's get Selene ready"));
            col.Children.Add(Ui.Sub("Selene checks what it needs. You can come back to this anytime from the menu: Extensions, Selene."));
            // rows as even as possible (11 cards: 6 + 5), each centred
            var grid = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 46, 0, 0) };
            int rows = (items.Count + Columns - 1) / Columns, at = 0;
            for (int r = 0; r < rows; r++)
            {
                int inRow = items.Count / rows + (r < items.Count % rows ? 1 : 0);
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                for (int i = 0; i < inRow; i++, at++)
                {
                    row.Children.Add(Card(items[at]));
                    cardRow.Add(r);
                    cardX.Add((i - (inRow - 1) / 2.0) * CardCell);
                }
                grid.Children.Add(row);
            }
            col.Children.Add(grid);
            info = new DetailPanel(960);
            info.Root.Margin = new Thickness(0, 34, 0, 0);
            info.Root.Height = 156;   // fixed: a taller or shorter text must not shift the centred screen
            col.Children.Add(info.Root);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
            installAll = new Pill("Install all", 240);
            carryOn = new Pill(firstRun ? "Continue" : "Done", 200);
            buttons.Children.Add(installAll.Root);
            buttons.Children.Add(carryOn.Root);
            col.Children.Add(buttons);
            Root.Children.Add(col);
            Root.Children.Add(Ui.Prompts(true, Ui.Prompt("A", "Select")));
            if (!firstRun) Root.Children.Add(Ui.Prompts(false, Ui.Prompt("B", "Back")));
            UpdateInstallAll();
            // the first card that needs something, else Continue
            index = items.FindIndex(i => i.State == SetupState.Missing || i.State == SetupState.Warning);
            if (index < 0) index = ButtonsStart + 1;
        }

        private int ButtonsStart { get { return items.Count; } }

        private FrameworkElement Card(SetupItem item)
        {
            var tile = new Tile(240, 140, 18, 6) { FocusScale = 1.06 };
            // the same layout in every card: icon, title and status always at the same height (status keeps room for 2 lines)
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 20, 16, 0) };
            sp.Children.Add(new TextBlock { Text = Icon(item.Key), FontFamily = Ui.Glyphs, FontSize = 26, Height = 30, Foreground = Ink.Brush("GlyphColor"), HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = item.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Body, Foreground = Ink.Brush("SeleneInkColor"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            var status = new TextBlock { FontSize = 14, FontFamily = Ui.Body, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Height = 38, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            SetStatus(status, item);
            sp.Children.Add(status);
            tile.Face.Child = sp;
            tile.Root.Margin = new Thickness(9);
            cards.Add(tile);
            statuses.Add(status);
            var holder = new Grid();
            holder.Children.Add(tile.Root);
            return holder;
        }

        private static string Icon(string key)
        {
            if (key == "library") return "\uE8F1";
            if (key == "folders") return "\uE8B7";
            if (key == "controller") return "\uE7FC";
            if (key == "ludusavi-program") return "\uE896";
            if (key.StartsWith("clash:")) return "\uE7BA";
            return "\uE74C";   // an add-on
        }

        private static void SetStatus(TextBlock t, SetupItem item)
        {
            switch (item.State)
            {
                case SetupState.Ready: t.Text = "✓ " + item.Detail; t.Foreground = Ink.Brush("SeleneSuccessColor"); break;
                case SetupState.Missing: t.Text = item.Detail; t.Foreground = Ink.Brush("GlyphColor"); break;
                case SetupState.Optional: t.Text = item.Detail; t.Foreground = Ink.Brush("SeleneInkColor", 0.6); break;
                default: t.Text = item.Detail; t.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush"); break;
            }
        }

        public override void Shown()
        {
            for (int i = 0; i < cards.Count; i++) Ui.Rise(cards[i].Root, 140 + i * 45);
            ShowFocus();
            // things change behind the screen (an add-on installed, a controller plugged in): check again every few seconds
            recheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            recheck.Tick += (s, e) => Recheck();
            recheck.Start();
            var w = Application.Current.MainWindow;
            if (w != null) w.Activated += OnActivated;
        }

        public override void Hidden()
        {
            if (recheck != null) recheck.Stop();
            var w = Application.Current.MainWindow;
            if (w != null) w.Activated -= OnActivated;
        }

        private void Recheck()
        {
            List<SetupItem> fresh;
            try { fresh = SetupCheck.Run(HomeNav.Api); }
            catch (Exception ex) { HomeNav.Log("setup check failed: " + ex.Message); return; }
            if (fresh.Count != items.Count) return;   // the list itself changed (an add-on that clashes appeared): next time the screen opens
            for (int i = 0; i < fresh.Count; i++)
            {
                if (fresh[i].Key != items[i].Key || (fresh[i].State == items[i].State && fresh[i].Detail == items[i].Detail)) continue;
                bool nowReady = fresh[i].State == SetupState.Ready && items[i].State != SetupState.Ready;
                items[i] = fresh[i];
                SetStatus(statuses[i], fresh[i]);
                Ui.Pop(statuses[i]);
                if (nowReady) Ui.Tick();
            }
            UpdateInstallAll();
            ShowFocus();
        }

        private void UpdateInstallAll()
        {
            int missing = SetupCheck.MissingIncluded(HomeNav.Api).Count;
            installAll.Text = missing > 0 ? "Install all (" + missing + ")" : "All add-ons installed";
            installAll.Root.Opacity = missing > 0 ? 1 : 0.5;
        }

        private void ShowFocus()
        {
            for (int i = 0; i < cards.Count; i++) cards[i].Focus(i == index);
            installAll.Focus(index == ButtonsStart);
            carryOn.Focus(index == ButtonsStart + 1);
            if (index < items.Count)
            {
                var item = items[index];
                Describe(item);
            }
            else if (index == ButtonsStart)
            {
                int missing = SetupCheck.MissingIncluded(HomeNav.Api).Count;
                if (missing > 0) info.Show("\uE74C", Tone.Accent, "Install all", missing + " to go", "Every add-on that comes with Selene, one after another.", "Playnite asks for each one. They work after a restart.", "A", "Install all");
                else info.Show("\uE74C", Tone.Success, "Install all", "Done", "Every add-on that comes with Selene is installed.", "");
            }
            else
            {
                info.Show("\uE72A", Tone.Accent, firstRun ? "Continue" : "Done", "", firstRun ? "Next: make your profile." : "Back to the home.", "", "A", firstRun ? "Continue" : "Done");
            }
        }

        // the focused card in the panel below: what it is, its state, and what A does
        private void Describe(SetupItem item)
        {
            var icon = Icon(item.Key);
            switch (item.State)
            {
                case SetupState.Ready:
                    info.Show(icon, Tone.Success, item.Title, "Ready", item.About, item.Detail);
                    return;
                case SetupState.Warning:
                    info.Show(icon, Tone.Warning, item.Title, "Clashes", item.About, "Turn it off in Playnite's Add-ons so the two don't fight.");
                    return;
            }
            if (item.AddonId != null) { info.Show(icon, Tone.Accent, item.Title, "Not installed", item.About, "Playnite asks you to confirm. It works after a restart.", "A", "Install"); return; }
            if (item.Key == "ludusavi-program") { info.Show(icon, Tone.Accent, item.Title, "Not set up", item.About, "Downloads the free Ludusavi program and connects it.", "A", "Set up"); return; }
            if (item.Key == "library") { info.Show(icon, Tone.Warning, item.Title, "No games", item.About, "Connect a store in Playnite's settings, under Libraries."); return; }
            if (item.Key == "folders") { info.Show(icon, Tone.Muted, item.Title, "Optional", item.About, "Add folders in Selene Core's settings (desktop mode)."); return; }
            if (item.Key == "controller") { info.Show(icon, Tone.Muted, item.Title, "Not connected", item.About, "Plug in or pair a controller; Selene picks it up by itself."); return; }
            info.Show(icon, Tone.Warning, item.Title, "Turned off", item.About, "Turn it on in Playnite's Add-ons.");
        }

        public override void Handle(string b)
        {
            if (b == "B")
            {
                if (firstRun) { Ui.Shake(Root.Children[0]); return; }
                Ui.Back();
                LoginScene.Finish();
                return;
            }
            if (b == "A") { Activate(); return; }
            if (!b.StartsWith("DPad")) return;
            int next;
            if (index < items.Count)
            {
                next = CardMove(index, b);
                // onto the buttons only with a fresh press: a held stick repeats and skipped the bottom row
                if (next < 0 && b == "DPadDown" && !Ui.Repeated(b)) next = ButtonsStart + 1;
            }
            else
            {
                next = -1;
                if (b == "DPadLeft" && index == ButtonsStart + 1) next = ButtonsStart;
                else if (b == "DPadRight" && index == ButtonsStart) next = ButtonsStart + 1;
                else if (b == "DPadUp") next = Nearest(cardRow[items.Count - 1], index == ButtonsStart ? -CardCell / 2 : CardCell / 2);
            }
            if (next < 0) return;
            index = next;
            Ui.Tick();
            ShowFocus();
        }

        // left/right along the row; up/down to the card in the next row that sits closest above or below
        private int CardMove(int from, string dir)
        {
            if (dir == "DPadLeft") return from > 0 && cardRow[from - 1] == cardRow[from] ? from - 1 : -1;
            if (dir == "DPadRight") return from + 1 < items.Count && cardRow[from + 1] == cardRow[from] ? from + 1 : -1;
            int row = cardRow[from] + (dir == "DPadUp" ? -1 : 1);
            return row < 0 || row > cardRow[items.Count - 1] ? -1 : Nearest(row, cardX[from], dir == "DPadDown");
        }

        // a card exactly between two: going down picks the right one, going up the left one (so up then down comes back)
        private int Nearest(int row, double x, bool tieRight = true)
        {
            int best = -1;
            for (int i = 0; i < items.Count; i++)
                if (cardRow[i] == row && (best < 0 || (tieRight ? Math.Abs(cardX[i] - x) <= Math.Abs(cardX[best] - x) + 0.5 : Math.Abs(cardX[i] - x) < Math.Abs(cardX[best] - x) - 0.5))) best = i;
            return best;
        }

        private void Activate()
        {
            if (index == ButtonsStart + 1)
            {
                Ui.Confirm();
                if (firstRun) LoginScene.Show(new ProfileSteps(null, () => new SetupScreen(true), p => LoginScene.Show(new WhoScreen(p.Id), 1)), 1);
                else LoginScene.Finish();
                return;
            }
            if (index == ButtonsStart)
            {
                var missing = SetupCheck.MissingIncluded(HomeNav.Api);
                if (missing.Count == 0) { Ui.Shake(installAll.Root); return; }
                Ui.Confirm();
                installQueue.Clear();
                foreach (var a in missing) installQueue.Enqueue(a.Id);
                InstallNext();
                return;
            }
            var item = items[index];
            if (item.AddonId != null) { Ui.Confirm(); SetupCheck.Install(item.AddonId); return; }
            if (item.Key == "ludusavi-program" && item.State != SetupState.Ready) { SetUpLudusavi(index); return; }
            if (item.State == SetupState.Ready) { Ui.Pop(cards[index].Root); Ui.Tick(); return; }
            Ui.Shake(cards[index].Root);   // nothing to do from here; the hint says where
        }

        // Playnite shows one install prompt at a time: the next add-on is asked for once Playnite is back in front
        private void InstallNext()
        {
            if (installQueue.Count == 0) return;
            var id = installQueue.Dequeue();
            HomeNav.Log("setup: installing " + id + " (" + installQueue.Count + " more)");
            try { SetupCheck.Install(id); }
            catch (Exception ex) { HomeNav.Log("setup: install " + id + " failed: " + ex.Message); }
        }

        private void OnActivated(object sender, EventArgs e)
        {
            if (installQueue.Count == 0) return;
            var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            later.Tick += (s, a) => { later.Stop(); InstallNext(); };
            later.Start();
        }

        private void SetUpLudusavi(int at)
        {
            if (settingUpLudusavi) return;
            settingUpLudusavi = true;
            Ui.Confirm();
            statuses[at].Text = "Downloading…";
            statuses[at].Foreground = Ink.Brush("SeleneInkColor", 0.7);
            var api = HomeNav.Api;
            var dispatcher = Application.Current.Dispatcher;
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Exception failed = null;
                try { LudusaviApp.DownloadIfNeeded(api); } catch (Exception ex) { failed = ex; }
                dispatcher.BeginInvoke(new Action(() =>
                {
                    settingUpLudusavi = false;
                    try
                    {
                        if (failed != null) throw failed;
                        LudusaviApp.Setup(api);
                        Ui.Chime();
                    }
                    catch (Exception ex)
                    {
                        HomeNav.Log("setup: Ludusavi failed: " + ex);
                        statuses[at].Text = "Couldn't set it up: " + ex.Message;
                        statuses[at].SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                        return;
                    }
                    Recheck();
                }));
            });
        }
    }
}
