using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Selene
{
    // A piece of the screen drawn and animated on a thread of its own (a HostVisual with its own dispatcher, as the login's
    // cover fade is): whatever the main thread is busy with (a game starting, the home being rebuilt), this keeps moving.
    // `build` runs on that thread and must only use what can cross threads: frozen bitmaps and brushes, plain colors, numbers.
    public class OffThread : FrameworkElement
    {
        private readonly HostVisual host = new HostVisual();
        private Dispatcher thread;
        private FrameworkElement root;
        private Size size;

        // resized (the window changed): called on the drawing thread with the element and its new size
        public Action<FrameworkElement, Size> Resized;

        public OffThread(Func<Size, FrameworkElement> build, string name = "Selene stage")
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            AddVisualChild(host);
            size = StartSize();
            var start = size;
            var ready = new ManualResetEvent(false);
            var t = new Thread(() =>
            {
                try
                {
                    var target = new VisualTarget(host);
                    root = build(start);
                    Arrange(start);
                    target.RootVisual = root;
                    thread = Dispatcher.CurrentDispatcher;
                    thread.UnhandledException += (s, e) => { HomeNav.Log("off-thread stage: " + e.Exception.Message); e.Handled = true; };
                    ready.Set();
                    Dispatcher.Run();
                    target.Dispose();
                }
                catch (Exception ex) { ready.Set(); HomeNav.Log("off-thread stage: " + ex.Message); }
            }) { IsBackground = true, Name = name };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            ready.WaitOne(2000);
            SizeChanged += (s, e) =>
            {
                var d = thread;
                var sz = e.NewSize;
                if (d == null || sz.Width <= 0 || sz.Height <= 0) return;
                size = sz;
                d.BeginInvoke(new Action(() => { Arrange(sz); if (Resized != null && root != null) Resized(root, sz); }));
            };
        }

        // something to do with the element, on its thread
        public void Run(Action<FrameworkElement> act)
        {
            var d = thread;
            if (d == null) return;
            d.BeginInvoke(new Action(() => { if (root != null) act(root); }));
        }

        // the thread ends once the element is done with (a loading screen that went away)
        public void Stop()
        {
            var d = thread;
            thread = null;
            if (d != null) d.BeginInvokeShutdown(DispatcherPriority.Background);
        }

        // the window's size until layout gives one
        private static Size StartSize()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w != null && w.ActualWidth > 0) return new Size(w.ActualWidth, w.ActualHeight);
            return new Size(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        private void Arrange(Size sz)
        {
            if (root == null) return;
            root.Measure(sz);
            root.Arrange(new Rect(sz));
        }

        protected override int VisualChildrenCount { get { return 1; } }
        protected override Visual GetVisualChild(int index) { return host; }
    }
}
