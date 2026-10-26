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
        private const double TopSpeed = 1100;   // px a second, stick all the way
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
