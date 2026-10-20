using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{

    internal static class WinApi
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        public const int WS_MINIMIZEBOX = 0x00020000;
        public const int WM_DPICHANGED = 0x02E0;

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new IntPtr(-3);

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        public static void EnableHighDpi()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                    return;
            }
            catch
            {
            }
            try
            {
                if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
                    return;
            }
            catch
            {
            }
            try
            {
                if (SetProcessDpiAwareness(2) == 0)
                    return;
            }
            catch
            {
            }
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }
        }

        public static int GetWindowDpi(Control c)
        {
            try
            {
                if (c != null && c.IsHandleCreated)
                {
                    uint dpi = GetDpiForWindow(c.Handle);
                    if (dpi >= 72 && dpi <= 480)
                        return (int)dpi;
                }
            }
            catch
            {
            }
            try
            {
                uint sys = GetDpiForSystem();
                if (sys >= 72 && sys <= 480)
                    return (int)sys;
            }
            catch
            {
            }
            try
            {
                if (c != null)
                {
                    using (Graphics g = c.CreateGraphics()) return (int)Math.Round(g.DpiX);
                }
            }
            catch
            {
            }
            return 96;
        }

        public static int Scale(int px, float scale)
        {
            return (int)Math.Round(px * scale);
        }

        public static void SetRoundedRegion(Form form, int width, int height, int radius)
        {
            IntPtr hRgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
            if (hRgn != IntPtr.Zero)
            {
                Region old = form.Region;
                form.Region = Region.FromHrgn(hRgn);
                DeleteObject(hRgn);
                if (old != null)
                    old.Dispose();
            }
        }

        public static void DragWindow(IntPtr handle)
        {
            ReleaseCapture();
            SendMessage(handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        public static GraphicsPath CreateRoundRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, radius * 2f);
            if (d > rect.Width)
                d = rect.Width;
            if (d > rect.Height)
                d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
