using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{

    internal sealed class LibraryListBox : ListBox
    {
        private bool _hidingScrollBar;
        private int _wheelRemainder;
        public event EventHandler ViewChanged;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr handle, int bar, bool show);

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x020A)
            {
                ScrollWheel((short)((message.WParam.ToInt64() >> 16) & 0xFFFF));
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
            if (_hidingScrollBar || !IsHandleCreated)
                return;

            switch (message.Msg)
            {
            case 0x0005:
            case 0x000F:
            case 0x0100:
            case 0x0115:
            case 0x020A:
            case 0x0180:
            case 0x0182:
            case 0x0184:
            case 0x0197:
                _hidingScrollBar = true;
                try
                {
                    ShowScrollBar(Handle, 1, false);
                }
                finally
                {
                    _hidingScrollBar = false;
                }
                if (ViewChanged != null)
                    ViewChanged(this, EventArgs.Empty);
                break;
            }
        }

        internal void ScrollWheel(int delta)
        {
            _wheelRemainder += delta;
            int steps = _wheelRemainder / 120;
            _wheelRemainder %= 120;
            int rows = Math.Max(1, ClientSize.Height / Math.Max(1, ItemHeight));
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines < 0)
                lines = rows;
            int maxTop = Math.Max(0, Items.Count - rows);
            if (Items.Count > 0 && steps != 0)
                TopIndex = Math.Max(0, Math.Min(maxTop, TopIndex - steps * lines));
        }
    }

    internal sealed class LibraryScrollBar : Control
    {
        private readonly LibraryListBox _list;
        private int _dragOffset = -1;
        private bool _hovered;

        public LibraryScrollBar(LibraryListBox list)
        {
            _list = list;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable,
                     true);
            BackColor = Theme.BgCard;
            AccessibleName = "Прокрутка библиотеки";
            TabStop = false;
            Cursor = Cursors.Hand;
            _list.ViewChanged += OnViewChanged;
        }

        public void RefreshView()
        {
            Visible = _list.Visible && _list.Items.Count * _list.ItemHeight > _list.ClientSize.Height;
            Invalidate();
        }

        private void OnViewChanged(object sender, EventArgs e)
        {
            RefreshView();
        }

        internal Rectangle ThumbBounds
        {
            get {
                int trackHeight = Math.Max(1, Height - 4);
                int totalHeight = Math.Max(1, _list.Items.Count *_list.ItemHeight);
                int thumbHeight =
                    Math.Min(trackHeight, Math.Max(Width * 2, trackHeight *_list.ClientSize.Height / totalHeight));
                int rows = Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.ItemHeight));
                int maxTop = Math.Max(1, _list.Items.Count - rows);
                int top = 2 + (trackHeight - thumbHeight) * _list.TopIndex / maxTop;
                return new Rectangle(2, top, Math.Max(3, Width - 4), thumbHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var thumb = ThumbBounds;
            using (var path = WinApi.CreateRoundRect(thumb, Math.Max(1, thumb.Width / 2f))) using (
                var fill = new SolidBrush(_hovered || Capture ? Theme.TextMuted : Color.FromArgb(86, 89, 101)))
                e.Graphics.FillPath(fill, path);
        }

        private void SetPosition(int thumbTop)
        {
            int rows = Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.ItemHeight));
            int maxTop = Math.Max(0, _list.Items.Count - rows);
            int travel = Math.Max(1, Height - 4 - ThumbBounds.Height);
            _list.TopIndex = Math.Max(0, Math.Min(maxTop, (int)Math.Round((thumbTop - 2) * (double)maxTop / travel)));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            Rectangle thumb = ThumbBounds;
            _dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2;
            Capture = true;
            SetPosition(e.Y - _dragOffset);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && _dragOffset >= 0)
                SetPosition(e.Y - _dragOffset);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            _dragOffset = -1;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _list.ScrollWheel(e.Delta);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _list.ViewChanged -= OnViewChanged;
            base.Dispose(disposing);
        }
    }

    public class AnimatedButton : Control, IButtonControl
    {
        private bool _isHovered;
        private bool _isPressed;
        private bool _isDefault;
        private DialogResult _dialogResult = DialogResult.None;

        private Color _normalColor;
        private Color _hoverColor;
        private Color _pressedColor;
        private Color _currentColor;
        private Color _targetColor;

        private readonly Timer _animTimer;
        private int _radius;

        public bool IsPlayButton { get; set; }
        public string IconName { get; set; }

        public Color NormalColor
        {
            get {
                return _normalColor;
            }
            set {
                _normalColor = value;
                if (!_isHovered && !_isPressed)
                {
                    _currentColor = value;
                    _targetColor = value;
                    Invalidate();
                }
            }
        }

        public Color HoverColor
        {
            get {
                return _hoverColor;
            }
            set {
                _hoverColor = value;
            }
        }

        public Color PressedColor
        {
            get {
                return _pressedColor;
            }
            set {
                _pressedColor = value;
            }
        }

        public int Radius
        {
            get {
                return _radius;
            }
            set {
                _radius = value;
                Invalidate();
            }
        }

        public DialogResult DialogResult
        {
            get {
                return _dialogResult;
            }
            set {
                _dialogResult = value;
            }
        }

        public AnimatedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.StandardClick,
                     true);

            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            BackColor = Theme.BgWindow;
            Cursor = Cursors.Hand;
            ForeColor = Color.White;

            _normalColor = Theme.Blurple;
            _hoverColor = Theme.BlurpleHover;
            _pressedColor = Theme.BlurplePressed;
            _currentColor = _normalColor;
            _targetColor = _normalColor;
            _radius = 8;

            _animTimer = new Timer { Interval = 15 };
            _animTimer.Tick += OnAnimTick;
        }

        public void NotifyDefault(bool value)
        {
            _isDefault = value;
            Invalidate();
        }

        public void PerformClick()
        {
            if (CanSelect && Enabled)
            {
                OnClick(EventArgs.Empty);
            }
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            int dr = _targetColor.R - _currentColor.R;
            int dg = _targetColor.G - _currentColor.G;
            int db = _targetColor.B - _currentColor.B;

            if (Math.Abs(dr) <= 2 && Math.Abs(dg) <= 2 && Math.Abs(db) <= 2)
            {
                _currentColor = _targetColor;
                _animTimer.Stop();
            }
            else
            {
                _currentColor = Color.FromArgb(_currentColor.R + (dr > 0 ? Math.Max(1, dr / 3) : Math.Min(-1, dr / 3)),
                                               _currentColor.G + (dg > 0 ? Math.Max(1, dg / 3) : Math.Min(-1, dg / 3)),
                                               _currentColor.B + (db > 0 ? Math.Max(1, db / 3) : Math.Min(-1, db / 3)));
            }
            Invalidate();
        }

        private void AnimateTo(Color target)
        {
            _targetColor = target;
            if (!_animTimer.Enabled)
                _animTimer.Start();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            _isPressed = false;
            AnimateTo(_isHovered ? _hoverColor : _normalColor);
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled)
                return;
            if (e.KeyCode == Keys.Space)
            {
                _isPressed = true;
                AnimateTo(_pressedColor);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                PerformClick();
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!Enabled)
                return;
            if (e.KeyCode == Keys.Space && _isPressed)
            {
                _isPressed = false;
                AnimateTo(_isHovered ? _hoverColor : _normalColor);
                e.Handled = true;
                PerformClick();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            if (Enabled)
                AnimateTo(_hoverColor);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            _isPressed = false;
            AnimateTo(_normalColor);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                Focus();
                _isPressed = true;
                AnimateTo(_pressedColor);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isPressed = false;
            if (Enabled)
                AnimateTo(ClientRectangle.Contains(e.Location) ? _hoverColor : _normalColor);
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_dialogResult != DialogResult.None)
            {
                Form f = FindForm();
                if (f != null)
                    f.DialogResult = _dialogResult;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = BackColor;
            using (var pBrush = new SolidBrush(parentBg))
            {
                g.FillRectangle(pBrush, 0, 0, Width, Height);
            }

            Color fill = Enabled ? _currentColor : Color.FromArgb(52, 55, 60);
            Color fg = Enabled ? ForeColor : Theme.TextMuted;

            var rect = new RectangleF(1f, 1f, Math.Max(2f, Width - 3f), Math.Max(2f, Height - 3f));
            using (var path = WinApi.CreateRoundRect(rect, _radius))
            {
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (Focused)
                {
                    using (var focusPen = new Pen(Theme.FocusRing, 1.8f))
                    {
                        g.DrawPath(focusPen, path);
                    }
                }
                else if (_isDefault)
                {
                    using (var defPen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                    {
                        g.DrawPath(defPen, path);
                    }
                }
            }

            if (!string.IsNullOrEmpty(IconName))
            {
                float scale = Height / 36f;
                int size = Math.Max(14, (int)(16 * scale));
                int gap = string.IsNullOrEmpty(Text) ? 0 : (int)(8 * scale);
                int textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, Font).Width;
                int total = Math.Min(Width - 12, size + gap + textWidth);
                int x = (Width - total) / 2;
                LucideIcons.Draw(g, IconName, new Rectangle(x, (Height - size) / 2, size, size), fg);
                if (!string.IsNullOrEmpty(Text))
                    TextRenderer.DrawText(
                        g, Text, Font,
                        new Rectangle(x + size + gap, 0, Math.Max(1, Width - x - size - gap - 6), Height), fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            else if (IsPlayButton)
            {
                TablerIcons.DrawPlayerPlay(g, new Rectangle(0, 0, Width, Height), fg);
            }
            else if (!string.IsNullOrEmpty(Text))
            {
                using (var sf = new StringFormat {
                    Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
                }) using (var textBrush = new SolidBrush(fg))
                {
                    g.DrawString(Text, Font, textBrush, new RectangleF(4, 0, Math.Max(4, Width - 8), Height), sf);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class AnimatedInputBox : UserControl
    {
        private readonly TextBox _innerBox;
        private readonly Timer _borderTimer;
        private Font _ownedFont;

        private Color _currentBorder;
        private Color _targetBorder;
        private readonly Color _normalBorder = Theme.InputBorder;
        private readonly Color _focusedBorder = Theme.Blurple;
        private string _placeholder = string.Empty;
        private string _iconName;
        public string IconName
        {
            get {
                return _iconName;
            }
            set {
                _iconName = value;
                LayoutInnerBox();
                Invalidate();
            }
        }

        public event EventHandler InputTextChanged;

        public string InputText
        {
            get {
                return _innerBox.Text;
            }
            set {
                _innerBox.Text = value ?? string.Empty;
                Invalidate();
            }
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [System.Runtime.InteropServices.DllImport("user32.dll",
                                                  CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public string PlaceholderText
        {
            get {
                return _placeholder;
            }
            set {
                _placeholder = value ?? string.Empty;
                UpdateCueBanner();
            }
        }

        private void UpdateCueBanner()
        {
            try
            {
                if (_innerBox != null && _innerBox.IsHandleCreated)
                    SendMessage(_innerBox.Handle, EM_SETCUEBANNER, new IntPtr(1), _placeholder);
            }
            catch
            {
            }
        }

        public TextBox InnerTextBox
        {
            get {
                return _innerBox;
            }
        }

        public AnimatedInputBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
                     true);

            BackColor = Theme.BgWindow;
            AutoScaleMode = AutoScaleMode.None;
            Size = new Size(376, 38);

            _ownedFont = new Font("Segoe UI", 10f, FontStyle.Regular);

            _innerBox = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.BgDarker,
                                      ForeColor = Color.White, Font = _ownedFont };

            _currentBorder = _normalBorder;
            _targetBorder = _normalBorder;

            _borderTimer = new Timer { Interval = 15 };
            _borderTimer.Tick += OnBorderTick;

            _innerBox.HandleCreated += (s, e) => UpdateCueBanner();
            _innerBox.GotFocus += (s, e) =>
            {
                AnimateBorder(_focusedBorder);
                Invalidate();
            };
            _innerBox.LostFocus += (s, e) =>
            {
                AnimateBorder(_normalBorder);
                Invalidate();
            };
            _innerBox.TextChanged += (s, e) =>
            {
                Invalidate();
                if (InputTextChanged != null)
                    InputTextChanged(this, EventArgs.Empty);
            };

            Controls.Add(_innerBox);
            LayoutInnerBox();
        }

        public void SetFontSize(float pt)
        {
            var newFont = new Font("Segoe UI", pt, FontStyle.Regular);
            _innerBox.Font = newFont;
            if (_ownedFont != null)
                _ownedFont.Dispose();
            _ownedFont = newFont;
            LayoutInnerBox();
        }

        public void SetAccessibleInfo(string name, string description)
        {
            AccessibleName = name;
            AccessibleDescription = description;
            _innerBox.AccessibleName = name;
            _innerBox.AccessibleDescription = description;
        }

        private void LayoutInnerBox()
        {
            if (_innerBox == null)
                return;
            int padX = Math.Max(10, Height / 3);
            int th = _innerBox.PreferredHeight;
            int y = Math.Max(4, (Height - th) / 2);
            int iconPad = string.IsNullOrEmpty(_iconName) ? 0 : Height * 2 / 3;
            _innerBox.Location = new Point(padX + iconPad, y);
            _innerBox.Width = Math.Max(20, Width - padX * 2 - iconPad);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInnerBox();
            Invalidate();
        }

        private void OnBorderTick(object sender, EventArgs e)
        {
            int dr = _targetBorder.R - _currentBorder.R;
            int dg = _targetBorder.G - _currentBorder.G;
            int db = _targetBorder.B - _currentBorder.B;

            if (Math.Abs(dr) <= 2 && Math.Abs(dg) <= 2 && Math.Abs(db) <= 2)
            {
                _currentBorder = _targetBorder;
                _borderTimer.Stop();
            }
            else
            {
                _currentBorder =
                    Color.FromArgb(_currentBorder.R + (dr > 0 ? Math.Max(1, dr / 3) : Math.Min(-1, dr / 3)),
                                   _currentBorder.G + (dg > 0 ? Math.Max(1, dg / 3) : Math.Min(-1, dg / 3)),
                                   _currentBorder.B + (db > 0 ? Math.Max(1, db / 3) : Math.Min(-1, db / 3)));
            }
            Invalidate();
        }

        private void AnimateBorder(Color target)
        {
            _targetBorder = target;
            if (!_borderTimer.Enabled)
                _borderTimer.Start();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _innerBox.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = BackColor;
            using (var pBrush = new SolidBrush(parentBg))
            {
                g.FillRectangle(pBrush, 0, 0, Width, Height);
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
            using (var path = WinApi.CreateRoundRect(rect, 8))
            {
                using (var bgBrush = new SolidBrush(Theme.BgDarker))
                {
                    g.FillPath(bgBrush, path);
                }

                float penWidth = _innerBox.Focused ? 1.6f : 1f;
                using (var borderPen = new Pen(_currentBorder, penWidth))
                {
                    g.DrawPath(borderPen, path);
                }
            }
            if (!string.IsNullOrEmpty(_iconName))
            {
                int size = Height / 2;
                LucideIcons.Draw(g, _iconName, new Rectangle(Height / 3, (Height - size) / 2, size, size),
                                 Theme.TextMuted);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_borderTimer != null)
                {
                    _borderTimer.Stop();
                    _borderTimer.Dispose();
                }
                if (_ownedFont != null)
                {
                    _ownedFont.Dispose();
                    _ownedFont = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
