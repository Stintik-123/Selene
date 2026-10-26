using System;
using System.Windows.Controls;
using System.Windows.Media;

namespace Selene
{
    // The right stick scrolls a long text (a game's description, an update's notes) smoothly: moved on every frame the screen
    // draws, by the time since the last one, the speed easing in and out (a timer's uneven ticks made it step). The further
    // the stick is pushed, the faster.
    public sealed class StickScroll
    {
        // px a second, stick all the way (a small box, like a game's description, wants it slower to read along)
        public double TopSpeed = 1100;
        private readonly Func<ScrollViewer> target;   // looked up each frame (a page's parts come later than the page)
        private readonly Func<bool> active;
        private double velocity, offset = -1;
        private TimeSpan last;
        private bool on;

        public StickScroll(ScrollViewer scroller, Func<bool> active) : this(() => scroller, active) { }

        public StickScroll(Func<ScrollViewer> target, Func<bool> active)
        {
            this.target = target;
            this.active = active;
        }

        public void Start() { if (on) return; on = true; last = TimeSpan.Zero; velocity = 0; offset = -1; CompositionTarget.Rendering += Frame; }
        public void Stop() { if (!on) return; on = false; CompositionTarget.Rendering -= Frame; }

        // a press (Up / Down) scrolls a step the same smooth way: eased over a fifth of a second instead of jumping; true
        // when there was room to scroll that way (at an end the press is left to whatever comes next)
        public static bool Step(ScrollViewer scroller, double by)
        {
            if (scroller == null) return false;
            double from = stepping == scroller ? stepTo : scroller.VerticalOffset;
            double to = Math.Max(0, Math.Min(scroller.ScrollableHeight, from + by));
            if (Math.Abs(to - scroller.VerticalOffset) < 0.5 && Math.Abs(to - from) < 0.5) return false;
            if (stepping == null) CompositionTarget.Rendering += StepFrame;
            stepping = scroller;
            stepFrom = scroller.VerticalOffset;
            stepTo = to;
            stepStart = DateTime.Now;
            return true;
        }

        private static ScrollViewer stepping;
        private static double stepFrom, stepTo;
        private static DateTime stepStart;
        private const double StepMs = 200;

        private static void StepFrame(object sender, EventArgs e)
        {
            var scroller = stepping;
            double t = Math.Min(1, (DateTime.Now - stepStart).TotalMilliseconds / StepMs);
            double eased = 1 - Math.Pow(1 - t, 3);   // ease out
            if (scroller != null) scroller.ScrollToVerticalOffset(stepFrom + (stepTo - stepFrom) * eased);
            if (t >= 1 || scroller == null) { CompositionTarget.Rendering -= StepFrame; stepping = null; }
        }

        private void Frame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == last) return;   // (the same frame reported twice)
            double dt = last == TimeSpan.Zero ? 0 : Math.Min(0.05, (now - last).TotalSeconds);
            last = now;
            if (dt <= 0) return;
            double y = active() ? Pad.Read().RightY : 0;
            double want = -y * Math.Abs(y) * TopSpeed;   // (gentle near the middle, quick at the edge)
            velocity += (want - velocity) * Math.Min(1, dt * 12);   // eased toward the stick's speed
            if (Math.Abs(velocity) < 2 && Math.Abs(want) < 1) { velocity = 0; offset = -1; return; }
            var scroller = target();
            if (scroller == null) return;
            if (offset < 0 || Math.Abs(offset - scroller.VerticalOffset) > 40) offset = scroller.VerticalOffset;   // (moved some other way)
            offset = Math.Max(0, Math.Min(scroller.ScrollableHeight, offset + velocity * dt));
            scroller.ScrollToVerticalOffset(offset);
        }
    }
}
