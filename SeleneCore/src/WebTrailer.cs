using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media.Animation;

namespace Selene
{
    // A Store trailer (Steam's are streams WPF can't play) played by the browser Playnite ships with (CefSharp, Chromium):
    // a muted, looping video in a page of its own, shown only once it's really playing (the page says so in its title).
    // Reached by reflection, so Selene Core still loads where that browser isn't there; anything going wrong: no trailer.
    public class WebTrailer
    {
        private FrameworkElement browser;
        private bool shown;
        public FrameworkElement View { get { return browser; } }

        // null when the browser can't be made
        public static WebTrailer Create(string dashUrl, Action playing)
        {
            if (string.IsNullOrEmpty(dashUrl)) return null;
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("CefSharp.Wpf.ChromiumWebBrowser", false)).FirstOrDefault(t => t != null);
                if (type == null) { HomeNav.Log("trailer: no browser"); return null; }
                var t2 = new WebTrailer();
                var view = (FrameworkElement)Activator.CreateInstance(type);
                view.IsHitTestVisible = false;
                view.Focusable = false;
                view.Opacity = 0;
                t2.browser = view;
                // the page tells us when the video plays (or that it can't) through its title
                var title = DependencyPropertyDescriptor.FromName("Title", type, type);
                if (title != null)
                    title.AddValueChanged(view, (s, e) =>
                    {
                        var text = view.GetValue(title.DependencyProperty) as string;
                        if (!string.IsNullOrEmpty(text) && text != "playing") HomeNav.Log("trailer page: " + text);
                        if (text == "playing" && !t2.shown)
                        {
                            t2.shown = true;
                            view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(900)) { EasingFunction = Ui.Out() });
                            HomeNav.Log("trailer: playing");
                            if (playing != null) playing();
                        }
                        else if (text != null && text.StartsWith("failed")) HomeNav.Log("trailer: the browser couldn't play it (" + text + ")");
                    });
                var html = "<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;background:#000;overflow:hidden}"
                    + "video{position:fixed;inset:0;width:100vw;height:100vh;object-fit:cover}</style></head><body>"
                    + "<video id='v' muted autoplay playsinline></video>"
                    + "<script src='https://cdn.jsdelivr.net/npm/dashjs@4.7.4/dist/dash.all.min.js'></script><script>"
                    + "try{var v=document.getElementById('v');var p=dashjs.MediaPlayer().create();"
                    + "p.updateSettings({streaming:{abr:{autoSwitchBitrate:{video:false}},buffer:{fastSwitchEnabled:true}}});"
                    + "p.initialize(v,'" + dashUrl.Replace("'", "%27") + "',true);p.setMute(true);"
                    + "v.addEventListener('playing',function(){document.title='playing';});"
                    + "v.addEventListener('ended',function(){v.currentTime=0;v.play();});"
                    + "p.on(dashjs.MediaPlayer.events.ERROR,function(e){document.title='failed: '+JSON.stringify(e.error||e).slice(0,160);});"
                    + "document.title='loading';"
                    + "}catch(e){document.title='failed: '+e;}</script></body></html>";
                var address = type.GetProperty("Address");
                // a different address each time (the same one twice may not load again)
                address.SetValue(view, "data:text/html;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html.Replace("<body>", "<body data-n='" + DateTime.Now.Ticks + "'>"))), null);
                HomeNav.Log("trailer: starting " + dashUrl.Substring(0, Math.Min(90, dashUrl.Length)));
                // nothing after a while: say so in the log
                var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
                watch.Tick += (s, e) => { watch.Stop(); if (!t2.shown && t2.browser != null) HomeNav.Log("trailer: not playing after 12 s (initialized=" + (type.GetProperty("IsBrowserInitialized") != null ? type.GetProperty("IsBrowserInitialized").GetValue(view, null) : "?") + ")"); };
                watch.Start();
                return t2;
            }
            catch (Exception ex) { HomeNav.Log("trailer: " + ex.Message); return null; }
        }

        // fades away (a screenshot was picked); Stop ends it for good
        public void Hide()
        {
            if (browser != null) browser.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        }

        public void Stop()
        {
            var b = browser;
            browser = null;
            if (b == null) return;
            try
            {
                var parent = System.Windows.Media.VisualTreeHelper.GetParent(b) as System.Windows.Controls.Panel;
                if (parent != null) parent.Children.Remove(b);
                var d = b as IDisposable;
                if (d != null) d.Dispose();
            }
            catch (Exception ex) { HomeNav.Log("trailer stop: " + ex.Message); }
        }
    }
}
