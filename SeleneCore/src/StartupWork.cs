using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Selene
{
    // Keep optional startup work out of the intro, then admit one job per quiet interval.
    // All actions stay on the dispatcher: Playnite's UI and add-on APIs are not thread safe.
    public static class StartupWork
    {
        private class Job { public string Name; public Action Run; public int QuietMs; }
        private static readonly Queue<Job> jobs = new Queue<Job>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static Func<bool> ready;
        private static DispatcherTimer timer;
        private static double lastBusy, lastFrame = -1, readySince = -1;
        private static bool watching;

        public static void Start(Func<bool> homeReady)
        {
            ready = homeReady;
            lastBusy = clock.Elapsed.TotalMilliseconds;
            readySince = -1;
            if (Application.Current != null) Application.Current.Exit += (s, e) => Stop();
        }

        public static void AfterHome(string name, Action action, int quietMs = 750)
        {
            jobs.Enqueue(new Job { Name = name, Run = action, QuietMs = quietMs });
            if (watching) return;
            watching = true;
            lastFrame = -1;
            readySince = -1;
            lastBusy = clock.Elapsed.TotalMilliseconds;
            CompositionTarget.Rendering += OnFrame;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Tick;
            timer.Start();
        }

        public static bool HomeReady { get { return ready != null && ready(); } }

        private static void OnFrame(object sender, EventArgs e)
        {
            var frame = e as RenderingEventArgs;
            if (frame == null) return;
            double now = frame.RenderingTime.TotalMilliseconds;
            if (now == lastFrame) return;
            if (lastFrame >= 0 && now - lastFrame > 40) lastBusy = clock.Elapsed.TotalMilliseconds;
            lastFrame = now;
        }

        private static void Tick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            if (!HomeReady) { lastBusy = now; readySince = -1; return; }
            if (readySince < 0) readySince = now;
            if (jobs.Count == 0) { Stop(); return; }
            var job = jobs.Peek();
            // A persistently slow renderer must not starve enabled features forever. Readiness
            // still gates the intro/game; only the quiet-frame requirement has a time bound.
            if (now - lastBusy < job.QuietMs && now - readySince < 10000) return;
            jobs.Dequeue();
            var elapsed = Stopwatch.StartNew();
            try { job.Run(); }
            catch (Exception ex) { HomeNav.Log("startup job " + job.Name + " failed: " + ex.Message); }
            HomeNav.Log("startup job " + job.Name + ": " + elapsed.ElapsedMilliseconds + " ms");
            lastBusy = clock.Elapsed.TotalMilliseconds;
            readySince = lastBusy;
            if (jobs.Count == 0) Stop();
        }

        private static void Stop()
        {
            if (timer != null) { timer.Stop(); timer.Tick -= Tick; timer = null; }
            CompositionTarget.Rendering -= OnFrame;
            watching = false;
            jobs.Clear();
        }

        // Split required UI setup into separate dispatcher turns, letting input/layout/render run in between.
        public static void Yield(params Action[] steps)
        {
            var dispatcher = Application.Current.Dispatcher;
            int index = 0;
            Action next = null;
            next = () =>
            {
                if (dispatcher.HasShutdownStarted || index >= steps.Length) return;
                try { steps[index++](); }
                finally { if (index < steps.Length && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(next, DispatcherPriority.Background); }
            };
            dispatcher.BeginInvoke(next, DispatcherPriority.Background);
        }
    }
}
