using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{

    public class DummyForm : Form
    {
        private const int BaseWidth = 480;
        private const int BaseHeight = 250;

        private Font _titleFont;
        private Font _textFont;
        private Font _timerFont;
        private Font _noteFont;
        private Font _authorFont;
        private Font _captionFont;
        private StringFormat _centerFormat;
        private StringFormat _farFormat;

        private Rectangle _headerRect;
        private Rectangle _closeRect;
        private Rectangle _minRect;
        private RectangleF _cardRect;

        private bool _isCloseHovered;
        private bool _isMinHovered;
        private bool _isClosing;
        private bool _isMinimizing;

        private readonly string _gameName;
        private readonly Stopwatch _uptime;
        private Timer _clockTimer;
        private Timer _fadeTimer;
        private float _scale = 1f;

        protected override CreateParams CreateParams
        {
            get {
                CreateParams cp = base.CreateParams;
                cp.Style |= WinApi.WS_MINIMIZEBOX;
                return cp;
            }
        }

        public DummyForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            BackColor = Theme.BgWindow;
            ForeColor = Color.White;
            KeyPreview = true;

            _gameName = ResolveCurrentExeName();
            Text = "Discord Quest — " + _gameName;
            AccessibleName = Text;

            _titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            _textFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            _timerFont = new Font("Consolas", 16f, FontStyle.Bold);
            _noteFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            _authorFont = new Font("Segoe UI", 8.5f, FontStyle.Italic);
            _captionFont = new Font("Segoe UI", 10f, FontStyle.Regular);
            _centerFormat =
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            _farFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

            _uptime = Stopwatch.StartNew();
            _clockTimer = new Timer { Interval = 1000 };
            _clockTimer.Tick += (s, e) => Invalidate(Rectangle.Round(_cardRect));
            _clockTimer.Start();

            ApplyLayout(96);

            MouseDown += OnFormMouseDown;
            MouseMove += OnFormMouseMove;
            MouseLeave += (s, e) =>
            {
                _isCloseHovered = false;
                _isMinHovered = false;
                Invalidate(_headerRect);
            };

            Paint += OnFormPaint;
        }

        private static string ResolveCurrentExeName()
        {
            try
            {
                return Path.GetFileName(AppInfo.ExecutablePath);
            }
            catch
            {
                return "Game.exe";
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayout(WinApi.GetWindowDpi(this));
            CenterToScreen();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WinApi.WM_DPICHANGED)
            {
                int newDpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                ApplyLayout(newDpi > 0 ? newDpi : WinApi.GetWindowDpi(this));
            }
        }

        private int S(int px)
        {
            return WinApi.Scale(px, _scale);
        }

        public void ApplyLayout(int dpi)
        {
            _scale = Math.Max(1f, dpi / 96f);
            float sysScale = 1f;
            try
            {
                using (Graphics g = CreateGraphics()) sysScale = Math.Max(1f, g.DpiX / 96f);
            }
            catch
            {
            }
            float fs = _scale / sysScale;
            if (_titleFont != null)
                _titleFont.Dispose();
            if (_textFont != null)
                _textFont.Dispose();
            if (_timerFont != null)
                _timerFont.Dispose();
            if (_noteFont != null)
                _noteFont.Dispose();
            if (_authorFont != null)
                _authorFont.Dispose();
            if (_captionFont != null)
                _captionFont.Dispose();

            _titleFont = new Font("Segoe UI", 9f * fs, FontStyle.Bold);
            _textFont = new Font("Segoe UI", 10.5f * fs, FontStyle.Bold);
            _timerFont = new Font("Consolas", 15.5f * fs, FontStyle.Bold);
            _noteFont = new Font("Segoe UI", 9f * fs, FontStyle.Regular);
            _authorFont = new Font("Segoe UI", 8.5f * fs, FontStyle.Italic);
            _captionFont = new Font("Segoe UI", 10f * fs, FontStyle.Regular);
            int w = S(BaseWidth);
            int h = S(BaseHeight);
            ClientSize = new Size(w, h);
            WinApi.SetRoundedRegion(this, w, h, S(14));

            _headerRect = new Rectangle(0, 0, w, S(36));
            int btnW = S(28);
            int btnH = S(24);
            _closeRect = new Rectangle(w - S(10) - btnW, S(6), btnW, btnH);
            _minRect = new Rectangle(_closeRect.Left - S(4) - btnW, S(6), btnW, btnH);
            _cardRect = new RectangleF(S(18), S(38), w - S(36), h - S(72));
            Invalidate();
        }

        private void StopFadeTimer()
        {
            if (_fadeTimer != null)
            {
                try
                {
                    _fadeTimer.Stop();
                    _fadeTimer.Dispose();
                }
                catch
                {
                }
                _fadeTimer = null;
            }
        }

        private void SmoothClose()
        {
            if (_isClosing)
                return;
            _isClosing = true;
            StopFadeTimer();

            var closeTimer = new Timer { Interval = 12 };
            _fadeTimer = closeTimer;
            closeTimer.Tick += (s, e) =>
            {
                if (Opacity > 0.08)
                {
                    Opacity = Math.Max(0.0, Opacity - 0.18);
                }
                else
                {
                    StopFadeTimer();
                    Close();
                }
            };
            closeTimer.Start();
        }

        private void SmoothMinimize()
        {
            if (_isMinimizing)
                return;
            _isMinimizing = true;
            StopFadeTimer();

            int origTop = Top;
            var minTimer = new Timer { Interval = 12 };
            _fadeTimer = minTimer;
            int step = 0;
            const int totalSteps = 6;

            minTimer.Tick += (s, e) =>
            {
                step++;
                float p = (float)step / totalSteps;
                Opacity = Math.Max(0.0, 1.0 - p);
                Top = origTop + (int)(p * S(20));

                if (step >= totalSteps)
                {
                    StopFadeTimer();
                    WindowState = FormWindowState.Minimized;
                    Opacity = 1.0;
                    Top = origTop;
                    _isMinimizing = false;
                }
            };
            minTimer.Start();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Normal && !_isMinimizing && !_isClosing)
            {
                Opacity = 1.0;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                SmoothClose();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnFormMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (_closeRect.Contains(e.Location))
            {
                SmoothClose();
                return;
            }

            if (_minRect.Contains(e.Location))
            {
                SmoothMinimize();
                return;
            }

            WinApi.DragWindow(Handle);
        }

        private void OnFormMouseMove(object sender, MouseEventArgs e)
        {
            bool ch = _closeRect.Contains(e.Location);
            bool mh = _minRect.Contains(e.Location);

            if (ch != _isCloseHovered || mh != _isMinHovered)
            {
                _isCloseHovered = ch;
                _isMinHovered = mh;
                Invalidate(_headerRect);
            }
        }

        private string FormatElapsed()
        {
            TimeSpan ts = _uptime.Elapsed;
            return string.Format("{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }

        private void OnFormPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            LucideIcons.Draw(g, "gamepad-2", new Rectangle(S(20), S(10), S(18), S(18)), Theme.TextMuted);
            TextRenderer.DrawText(g, "Quest Launcher", _titleFont, new Rectangle(S(46), S(8), S(280), S(24)),
                                  Theme.TextNormal, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            LucideIcons.Draw(g, "minus", Rectangle.Inflate(_minRect, -S(6), -S(4)),
                             _isMinHovered ? Color.White : Theme.TextMuted);
            LucideIcons.Draw(g, "x", Rectangle.Inflate(_closeRect, -S(6), -S(4)),
                             _isCloseHovered ? Theme.Red : Theme.TextMuted);
            using (var path = WinApi.CreateRoundRect(new Rectangle(S(16), S(44), Width - S(32), S(170)), S(12))) using (
                var fill = new SolidBrush(Theme.BgCard)) g.FillPath(fill, path);
            LucideIcons.Draw(g, "gamepad-2", new Rectangle(S(32), S(64), S(26), S(26)), Color.FromArgb(160, 166, 255));
            TextRenderer.DrawText(g, _gameName, _textFont, new Rectangle(S(70), S(57), Width - S(102), S(26)),
                                  Theme.TextNormal,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                      TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "Процесс запущен", _noteFont, new Rectangle(S(70), S(85), Width - S(102), S(20)),
                                  Color.FromArgb(112, 207, 158), TextFormatFlags.Left);
            LucideIcons.Draw(g, "clock-3", new Rectangle(S(34), S(126), S(20), S(20)), Theme.TextMuted);
            TextRenderer.DrawText(g, FormatElapsed(), _timerFont, new Rectangle(S(68), S(114), S(180), S(42)),
                                  Theme.TextNormal, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "Время работы процесса", _noteFont, new Rectangle(S(254), S(127), S(188), S(20)),
                                  Theme.TextMuted, TextFormatFlags.Right);
            TextRenderer.DrawText(g,
                                  "Не закрывайте окно до завершения квеста.\nТаймер не показывает прогресс в Discord.",
                                  _noteFont, new Rectangle(S(32), S(171), Width - S(64), S(38)), Theme.TextMuted,
                                  TextFormatFlags.Left | TextFormatFlags.WordBreak);
            TextRenderer.DrawText(g, "v" + AppInfo.Version + "  •  vaelit", _noteFont,
                                  new Rectangle(S(24), Height - S(26), Width - S(48), S(18)), Theme.TextMuted,
                                  TextFormatFlags.Right);

            var borderRect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = WinApi.CreateRoundRect(borderRect, S(7))) using (var borderPen =
                                                                                   new Pen(Theme.Border, 1f))
            {
                g.DrawPath(borderPen, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopFadeTimer();
                if (_clockTimer != null)
                {
                    _clockTimer.Stop();
                    _clockTimer.Dispose();
                    _clockTimer = null;
                }
                if (_titleFont != null)
                    _titleFont.Dispose();
                if (_textFont != null)
                    _textFont.Dispose();
                if (_timerFont != null)
                    _timerFont.Dispose();
                if (_noteFont != null)
                    _noteFont.Dispose();
                if (_authorFont != null)
                    _authorFont.Dispose();
                if (_captionFont != null)
                    _captionFont.Dispose();
                if (_centerFormat != null)
                    _centerFormat.Dispose();
                if (_farFormat != null)
                    _farFormat.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
