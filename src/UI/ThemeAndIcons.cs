using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MinimalQuestLauncher
{

    internal static class Theme
    {
        public static readonly Color BgWindow = Color.FromArgb(30, 31, 35);
        public static readonly Color BgDarker = Color.FromArgb(28, 29, 33);
        public static readonly Color BgCard = Color.FromArgb(37, 38, 43);
        public static readonly Color BgCardHover = Color.FromArgb(49, 51, 56);
        public static readonly Color BgCardSelected = Color.FromArgb(56, 58, 64);

        public static readonly Color Blurple = Color.FromArgb(88, 101, 242);
        public static readonly Color BlurpleHover = Color.FromArgb(71, 82, 196);
        public static readonly Color BlurplePressed = Color.FromArgb(60, 69, 165);

        public static readonly Color Green = Color.FromArgb(35, 165, 90);
        public static readonly Color GreenHover = Color.FromArgb(45, 185, 100);
        public static readonly Color GreenPressed = Color.FromArgb(28, 135, 72);

        public static readonly Color Secondary = Color.FromArgb(48, 50, 57);
        public static readonly Color SecondaryHover = Color.FromArgb(78, 82, 90);
        public static readonly Color SecondaryPressed = Color.FromArgb(54, 57, 63);

        public static readonly Color Yellow = Color.FromArgb(240, 178, 50);
        public static readonly Color Red = Color.FromArgb(237, 66, 69);
        public static readonly Color RedHover = Color.FromArgb(200, 50, 53);

        public static readonly Color TextPrimary = Color.White;
        public static readonly Color TextNormal = Color.FromArgb(219, 222, 225);
        public static readonly Color TextMuted = Color.FromArgb(160, 165, 177);

        public static readonly Color Border = Color.FromArgb(58, 60, 67);
        public static readonly Color InputBorder = Color.FromArgb(60, 62, 68);
        public static readonly Color FocusRing = Color.FromArgb(130, 145, 255);
    }

    internal static class LucideIcons
    {
        private static readonly System.Collections.Generic.Dictionary<string, Image> Images =
            new System.Collections.Generic.Dictionary<string, Image>();
        public static Image Get(string name)
        {
            Image value;
            if (!Images.TryGetValue(name, out value))
            {
                using (var stream = typeof(LucideIcons).Assembly.GetManifestResourceStream("Icons." + name + ".png"))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Missing icon: " + name);
                    using (var original = Image.FromStream(stream)) value = new Bitmap(original);
                }
                Images.Add(name, value);
            }
            return value;
        }
        public static void Draw(Graphics g, string name, Rectangle bounds, Color color)
        {
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                var matrix = new System.Drawing.Imaging.ColorMatrix(
                    new float[][] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, color.A / 255f, 0 },
                                    new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 0, 1 } });
                attributes.SetColorMatrix(matrix);
                var img = Get(name);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, bounds, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attributes);
            }
        }
    }
    internal static class TablerIcons
    {
        public static Image FolderIcon
        {
            get {
                return LucideIcons.Get("folder");
            }
        }
        public static Image GamepadIcon
        {
            get {
                return LucideIcons.Get("gamepad-2");
            }
        }
        public static void DrawPlayerPlay(Graphics g, Rectangle bounds, Color color)
        {
            LucideIcons.Draw(g, "play", bounds, color);
        }
        public static void DrawStar(Graphics g, RectangleF bounds, bool filled, Color color)
        {
            LucideIcons.Draw(g, "star", Rectangle.Round(bounds), color);
        }
    }

    internal sealed class EmptyLibraryView : Control
    {
        public bool Filtered;
        public EmptyLibraryView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
                     true);
            BackColor = Theme.BgCard;
            TabStop = false;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = Width / 476f;
            scale = Math.Max(1f, Math.Min(3f, scale));
            int icon = (int)(44 * scale), top = (int)(36 * scale);
            LucideIcons.Draw(e.Graphics, Filtered ? "search" : "library",
                             new Rectangle((Width - icon) / 2, top, icon, icon), Theme.TextMuted);
            using (var title = new Font("Segoe UI", 11f * scale, FontStyle.Bold)) using (
                var note = new Font("Segoe UI", 9f * scale))
            {
                TextRenderer.DrawText(e.Graphics, Filtered ? "Ничего не найдено" : "Здесь будут ваши игры", title,
                                      new Rectangle(8, top + icon + (int)(20 * scale), Width - 16, (int)(26 * scale)),
                                      Theme.TextNormal,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics,
                                      Filtered ? "Измените запрос или выберите другой фильтр."
                                               : "Создайте копию слева или импортируйте\nуже существующие файлы.",
                                      note,
                                      new Rectangle(8, top + icon + (int)(52 * scale), Width - 16, (int)(48 * scale)),
                                      Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            }
        }
    }

    internal sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder
        {
            get {
                return Theme.Border;
            }
        }
        public override Color MenuItemBorder
        {
            get {
                return Color.Transparent;
            }
        }
        public override Color MenuItemSelected
        {
            get {
                return Theme.Blurple;
            }
        }
        public override Color ToolStripDropDownBackground
        {
            get {
                return Theme.BgDarker;
            }
        }
        public override Color ImageMarginGradientBegin
        {
            get {
                return Theme.BgDarker;
            }
        }
        public override Color ImageMarginGradientMiddle
        {
            get {
                return Theme.BgDarker;
            }
        }
        public override Color ImageMarginGradientEnd
        {
            get {
                return Theme.BgDarker;
            }
        }
        public override Color MenuStripGradientBegin
        {
            get {
                return Theme.BgDarker;
            }
        }
        public override Color MenuStripGradientEnd
        {
            get {
                return Theme.BgDarker;
            }
        }
        public override Color MenuItemSelectedGradientBegin
        {
            get {
                return Theme.Blurple;
            }
        }
        public override Color MenuItemSelectedGradientEnd
        {
            get {
                return Theme.Blurple;
            }
        }
        public override Color MenuItemPressedGradientBegin
        {
            get {
                return Theme.BlurpleHover;
            }
        }
        public override Color MenuItemPressedGradientEnd
        {
            get {
                return Theme.BlurpleHover;
            }
        }
        public override Color SeparatorDark
        {
            get {
                return Theme.Border;
            }
        }
        public override Color SeparatorLight
        {
            get {
                return Theme.BgWindow;
            }
        }
    }

    internal sealed class DarkContextMenu : ContextMenuStrip
    {
        protected override void OnOpened(EventArgs e)
        {
            using (var path = WinApi.CreateRoundRect(new RectangleF(0, 0, Width, Height), 8))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null)
                    old.Dispose();
            }
            base.OnOpened(e);
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public static readonly DarkMenuRenderer Instance = new DarkMenuRenderer();

        public DarkMenuRenderer() : base(new DarkMenuColorTable())
        {
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var fill = new SolidBrush(Theme.BgCard)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var fill = new SolidBrush(Theme.BgCard)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var path = WinApi.CreateRoundRect(
                       new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1),
                       8)) using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e != null && e.Item != null)
            {
                e.TextColor = !e.Item.Enabled ? Theme.TextMuted : (e.Item.Selected ? Color.White : Theme.TextNormal);
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (e != null)
            {
                bool sel = (e.Item != null && e.Item.Selected);
                e.ArrowColor = sel ? Color.White : Theme.TextMuted;
            }
            base.OnRenderArrow(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e == null || e.Item == null || e.Graphics == null)
                return;
            if (e.Item.Selected && e.Item.Enabled)
            {
                using (var b = new SolidBrush(Theme.Blurple))
                {
                    var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
                    using (var path = WinApi.CreateRoundRect(r, 4))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(b, path);
                    }
                }
            }
            else
            {
                base.OnRenderMenuItemBackground(e);
            }
        }
    }
}
