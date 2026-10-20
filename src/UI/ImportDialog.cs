using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{

    internal sealed class ImportDialog : Form
    {
        private readonly List<ImportCandidate> _candidates;
        private CheckedListBox _listBox;
        private Label _lblSubtitle;
        private Label _lblPathInfo;
        private AnimatedButton _btnSelectVerified;
        private AnimatedButton _btnSelectNone;
        private AnimatedButton _btnImport;
        private AnimatedButton _btnCancel;

        private Font _titleFont;
        private Font _bodyFont;
        private Font _smallFont;
        private float _scale = 1f;

        public ImportDialog(List<ImportCandidate> candidates, string scannedRoot)
        {
            _candidates = candidates ?? new List<ImportCandidate>();

            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            BackColor = Theme.BgWindow;
            ForeColor = Color.White;
            Text = "Импорт найденных копий — Quest Launcher";
            KeyPreview = true;

            _titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            _bodyFont = new Font("Segoe UI", 9.2f, FontStyle.Regular);
            _smallFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);

            InitControls(scannedRoot);
            PopulateList();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyDpi(WinApi.GetWindowDpi(this));
        }

        private int S(int px)
        {
            return WinApi.Scale(px, _scale);
        }

        private void ApplyDpi(int dpi)
        {
            _scale = dpi / 96f;
            if (_scale < 1f)
                _scale = 1f;

            int w = S(660);
            int h = S(440);
            ClientSize = new Size(w, h);
            WinApi.SetRoundedRegion(this, w, h, S(12));

            int pad = S(18);
            _lblSubtitle.Location = new Point(pad, S(40));
            _lblSubtitle.Size = new Size(w - pad * 2, S(36));

            _listBox.Location = new Point(pad, S(80));
            _listBox.Size = new Size(w - pad * 2, S(250));
            _listBox.ItemHeight = S(22);

            _lblPathInfo.Location = new Point(pad, S(338));
            _lblPathInfo.Size = new Size(w - pad * 2, S(38));

            int btnY = h - S(50);
            int btnH = S(34);
            _btnSelectVerified.Bounds = new Rectangle(pad, btnY, S(165), btnH);
            _btnSelectNone.Bounds = new Rectangle(pad + S(173), btnY, S(120), btnH);
            _btnCancel.Bounds = new Rectangle(w - pad - S(230), btnY, S(105), btnH);
            _btnImport.Bounds = new Rectangle(w - pad - S(115), btnY, S(115), btnH);

            CenterToParent();
            Invalidate();
        }

        private void InitControls(string scannedRoot)
        {
            _lblSubtitle =
                new Label { Text = "Папка: " + scannedRoot +
                                   "\nОтметьте копии для добавления в библиотеку (сами файлы на диске не изменяются):",
                            Font = _smallFont, ForeColor = Theme.TextNormal, BackColor = Color.Transparent };
            Controls.Add(_lblSubtitle);

            _listBox = new CheckedListBox { BackColor = Theme.BgDarker,
                                            ForeColor = Color.White,
                                            BorderStyle = BorderStyle.FixedSingle,
                                            Font = _bodyFont,
                                            CheckOnClick = true,
                                            IntegralHeight = false,
                                            AccessibleName = "Список найденных копий для импорта" };
            _listBox.SelectedIndexChanged += (s, e) => UpdateSelectedInfo();
            _listBox.ItemCheck += (s, e) =>
            {
                if (e.Index >= 0 && e.Index < _candidates.Count)
                    _candidates[e.Index].Selected = (e.NewValue == CheckState.Checked);
                UpdateImportButtonText();
            };
            Controls.Add(_listBox);

            _lblPathInfo = new Label { Text = "Выберите строку, чтобы увидеть полный путь и SHA-256.",
                                       Font = _smallFont, ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
            Controls.Add(_lblPathInfo);

            _btnSelectVerified = new AnimatedButton { Text = "Отметить проверенные",
                                                      Font = _smallFont,
                                                      NormalColor = Theme.Secondary,
                                                      HoverColor = Theme.SecondaryHover,
                                                      PressedColor = Theme.SecondaryPressed,
                                                      Radius = 6,
                                                      AccessibleName = "Отметить все подтверждённые копии" };
            _btnSelectVerified.Click += (s, e) =>
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    bool check =
                        (_candidates[i].Kind == FileKind.OwnCopy || _candidates[i].Kind == FileKind.LegacyKnown);
                    _candidates[i].Selected = check;
                    _listBox.SetItemChecked(i, check);
                }
                UpdateImportButtonText();
            };
            Controls.Add(_btnSelectVerified);

            _btnSelectNone = new AnimatedButton { Text = "Снять отметки",
                                                  Font = _smallFont,
                                                  NormalColor = Theme.Secondary,
                                                  HoverColor = Theme.SecondaryHover,
                                                  PressedColor = Theme.SecondaryPressed,
                                                  Radius = 6,
                                                  AccessibleName = "Снять все отметки" };
            _btnSelectNone.Click += (s, e) =>
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    _candidates[i].Selected = false;
                    _listBox.SetItemChecked(i, false);
                }
                UpdateImportButtonText();
            };
            Controls.Add(_btnSelectNone);

            _btnCancel = new AnimatedButton { Text = "Отмена",
                                              Font = _bodyFont,
                                              NormalColor = Theme.Secondary,
                                              HoverColor = Theme.SecondaryHover,
                                              PressedColor = Theme.SecondaryPressed,
                                              Radius = 6,
                                              DialogResult = DialogResult.Cancel,
                                              AccessibleName = "Отменить импорт" };
            Controls.Add(_btnCancel);

            _btnImport = new AnimatedButton { Text = "Импортировать",
                                              Font = _titleFont,
                                              NormalColor = Theme.Blurple,
                                              HoverColor = Theme.BlurpleHover,
                                              PressedColor = Theme.BlurplePressed,
                                              Radius = 6,
                                              DialogResult = DialogResult.OK,
                                              AccessibleName = "Импортировать выбранные копии" };
            Controls.Add(_btnImport);

            AcceptButton = _btnImport;
            CancelButton = _btnCancel;

            MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.Y < S(36))
                    WinApi.DragWindow(Handle);
            };
        }

        private void PopulateList()
        {
            _listBox.Items.Clear();
            for (int i = 0; i < _candidates.Count; i++)
            {
                ImportCandidate c = _candidates[i];
                string line = c.Name + "  [" + c.KindDescription + "]  —  " + c.Path;
                _listBox.Items.Add(line, c.Selected);
            }
            if (_candidates.Count > 0)
                _listBox.SelectedIndex = 0;
            UpdateImportButtonText();
        }

        private void UpdateSelectedInfo()
        {
            int idx = _listBox.SelectedIndex;
            if (idx < 0 || idx >= _candidates.Count)
            {
                _lblPathInfo.Text = string.Empty;
                return;
            }
            ImportCandidate c = _candidates[idx];
            string shaShort = string.IsNullOrEmpty(c.Sha256) ? "—" : c.Sha256.Substring(0, 16) + "…";
            _lblPathInfo.Text = "Путь: " + c.Path + "\nСтатус: " + c.KindDescription + " • SHA-256: " + shaShort +
                                " • Размер: " + (c.Size / 1024) + " КБ";
        }

        private void UpdateImportButtonText()
        {
            int count = 0;
            foreach (ImportCandidate c in _candidates)
                if (c.Selected)
                    count++;
            _btnImport.Text = "Импорт (" + count + ")";
            _btnImport.Enabled = count > 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var topBrush = new SolidBrush(Theme.Blurple))
                g.FillRectangle(topBrush, 0, 0, Width, Math.Max(3, S(3)));

            using (var titleBrush = new SolidBrush(Theme.TextNormal)) g.DrawString(
                "Импорт существующих копий (" + _candidates.Count + " найдено)", _titleFont, titleBrush, S(18), S(10));

            var borderRect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = WinApi.CreateRoundRect(borderRect, S(7))) using (
                var borderPen = new Pen(Theme.Border, 1f)) g.DrawPath(borderPen, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_titleFont != null)
                    _titleFont.Dispose();
                if (_bodyFont != null)
                    _bodyFont.Dispose();
                if (_smallFont != null)
                    _smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
