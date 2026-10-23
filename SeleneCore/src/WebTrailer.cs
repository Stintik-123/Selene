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

        // a clip of your own (the NVIDIA app records AV1, which Playnite's own player can't decode): the browser plays the
        // file, with sound when it may, else muted. A small page of its own in the temp folder (a file page may show a file).
        public static WebTrailer CreateFile(string path, Action playing)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("CefSharp.Wpf.ChromiumWebBrowser", false)).FirstOrDefault(t => t != null);
                if (type == null) { HomeNav.Log("clip: no browser"); return null; }
                var t2 = new WebTrailer();
                var view = (FrameworkElement)Activator.CreateInstance(type);
                view.IsHitTestVisible = false;
                view.Focusable = false;
                view.Opacity = 0;
                t2.browser = view;
                var title = DependencyPropertyDescriptor.FromName("Title", type, type);
                if (title != null)
                    title.AddValueChanged(view, (s, e) =>
                    {
                        var text = view.GetValue(title.DependencyProperty) as string;
                        if (string.IsNullOrEmpty(text)) return;
                        if (text.StartsWith("playing") && !t2.shown)
                        {
                            t2.shown = true;
                            view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
                            HomeNav.Log("clip: " + text);
                            if (playing != null) playing();
                        }
                        else if (text.StartsWith("failed")) HomeNav.Log("clip: the browser couldn't play it (" + text + ")");
                        else if (text.StartsWith("t=") && t2.reports++ < 3) HomeNav.Log("clip: " + text + " (" + (int)view.ActualWidth + "x" + (int)view.ActualHeight + ", visible=" + view.IsVisible + ")");
                    });
                var src = new Uri(path).AbsoluteUri;
                // the frames go through a canvas: a big clip is decoded by the GPU and composited apart, which the browser's
                // off-screen picture (what WPF shows) never got, so the page stayed black while the clip "played"
                var html = "<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;background:#000;overflow:hidden}"
                    + "canvas{position:fixed;left:0;top:0;width:100vw;height:100vh}video{position:fixed;left:0;top:0;width:4px;height:4px;opacity:0.01}</style></head><body>"
                    + "<canvas id='c'></canvas><video id='v' src=\"" + src.Replace("\"", "%22") + "\" autoplay playsinline loop></video><script>"
                    + "var v=document.getElementById('v'),c=document.getElementById('c'),x=c.getContext('2d'),n=0;"
                    + "function fit(){c.width=innerWidth;c.height=innerHeight;}fit();addEventListener('resize',fit);"
                    + "function draw(){if(v.videoWidth>0){var s=Math.min(c.width/v.videoWidth,c.height/v.videoHeight),w=v.videoWidth*s,h=v.videoHeight*s;"
                    + "x.drawImage(v,(c.width-w)/2,(c.height-h)/2,w,h);n++;}requestAnimationFrame(draw);}requestAnimationFrame(draw);"
                    + "v.addEventListener('playing',function(){document.title=(v.muted?'playing muted ':'playing ')+v.videoWidth+'x'+v.videoHeight;});"
                    + "setInterval(function(){if(!v.paused)document.title='t='+v.currentTime.toFixed(1)+' drawn='+n;},3000);"
                    + "v.addEventListener('error',function(){document.title='failed: '+(v.error?v.error.code+' '+v.error.message:'');});"
                    + "v.muted=false;var p=v.play();if(p&&p.catch)p.catch(function(){v.muted=true;v.play();});"
                    + "document.title='loading';</script></body></html>";
                var page = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-clip-" + DateTime.Now.Ticks + ".html");
                System.IO.File.WriteAllText(page, html, Encoding.UTF8);
                t2.pageFile = page;
                type.GetProperty("Address").SetValue(view, new Uri(page).AbsoluteUri, null);
                HomeNav.Log("clip: starting " + System.IO.Path.GetFileName(path));
                return t2;
            }
            catch (Exception ex) { HomeNav.Log("clip: " + ex.Message); return null; }
        }

        private string pageFile;
        private int reports;

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
            if (pageFile != null) { try { System.IO.File.Delete(pageFile); } catch { } pageFile = null; }
        }
    }
}
