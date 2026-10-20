using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace MinimalQuestLauncher
{
    public class LauncherForm : Form
    {
        private const int BaseWidth = 900;
        private const int BaseHeight = 520;

        private readonly Library _library;
        private readonly CopyService _copyService;
        private Settings _settings;

        private List<LibraryEntry> _allEntries = new List<LibraryEntry>();
        private List<EntryView> _visibleEntries = new List<EntryView>();
        private Dictionary<string, List<int>> _runningMap = new Dictionary<string, List<int>>();
        private string _activeFilter = "all";
        private CancellationTokenSource _scanCts;
        private bool _isScanning;
        private bool _isCheckingUpdates;
        private bool _isUpdatingAll;

        private Label _lblLeftTitle;
        private Label _lblGame;
        private AnimatedButton _btnQuickLaunch;
        private AnimatedInputBox _inputGame;
        private Label _lblFolder;
        private AnimatedInputBox _inputFolder;
        private AnimatedButton _btnBrowseFolder;
        private Label _lblPreviewCaption;
        private Panel _previewCard;
        private Label _lblPreviewPath;
        private Label _lblPreviewStatus;
        private AnimatedButton _btnLaunch;
        private Label _lblFooterStatus;
        private AnimatedButton _btnCheckUpdates;
        private AnimatedButton _btnOpenLog;

        private Label _lblRightTitle;
        private AnimatedButton _btnImportScan;
        private AnimatedButton _btnRefreshLib;
        private AnimatedInputBox _inputSearch;
        private AnimatedButton _btnFilterAll;
        private AnimatedButton _btnFilterRecent;
        private AnimatedButton _btnFilterFav;
        private LibraryListBox _listLibrary;
        private LibraryScrollBar _libraryScrollBar;
        private EmptyLibraryView _emptyLibrary;
        private AnimatedButton _btnMore;
        private AnimatedButton _btnEmptyImport;
        private ContextMenuStrip _appMenu;
        private AnimatedButton _btnLibRun;
        private AnimatedButton _btnLibUpdate;
        private AnimatedButton _btnLibOpenFolder;
        private AnimatedButton _btnLibCopyPath;
        private AnimatedButton _btnLibFav;
        private AnimatedButton _btnLibDelete;

        private ContextMenuStrip _quickMenu;
        private ContextMenuStrip _entryContextMenu;
        private ToolTip _toolTip;

        private Font _titleFont;
        private Font _sectionFont;
        private Font _boldFont;
        private Font _labelFont;
        private Font _smallFont;
        private Font _itemTitleFont;
        private Font _itemSubFont;
        private Font _captionFont;
        private float _fontScale = 1f;
        private StringFormat _centerFormat;

        private Rectangle _headerRect;
        private Rectangle _closeRect;
        private Rectangle _minRect;
        private int _dividerX;

        private bool _isCloseHovered;
        private bool _isMinHovered;
        private bool _isClosing;
        private bool _isMinimizing;
        private bool _wasMinimized;
        private bool _isLaunching;
        private float _scale = 1f;

        private Timer _fadeTimer;
        private Timer _processPollTimer;

        protected override CreateParams CreateParams
        {
            get {
                CreateParams cp = base.CreateParams;
                cp.Style |= WinApi.WS_MINIMIZEBOX;
                return cp;
            }
        }

        public LauncherForm() : this(null)
        {
        }

        public LauncherForm(string launcherExeOverride)
        {
            _library = new Library(DataPaths.LibraryPath);
            _copyService = new CopyService(_library, launcherExeOverride ?? AppInfo.ExecutablePath, AppInfo.Version);

            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            BackColor = Theme.BgWindow;
            ForeColor = Color.White;
            Opacity = 0.0;
            KeyPreview = true;
            Text = "Quest Launcher v" + AppInfo.Version;
            AccessibleName = Text;

            _titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _sectionFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            _boldFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            _labelFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            _smallFont = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            _itemTitleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _itemSubFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            _captionFont = new Font("Segoe UI", 10f, FontStyle.Regular);
            _centerFormat =
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            _toolTip = new ToolTip { InitialDelay = 350, ReshowDelay = 200 };

            InitControls();
            InitContextMenus();
            InitVisualRefinement();
            ApplyLayout(96);

            MouseDown += OnFormMouseDown;
            MouseMove += OnFormMouseMove;
            MouseLeave += (s, e) =>
            {
                _isCloseHovered = false;
                _isMinHovered = false;
                Invalidate(_headerRect);
            };

            FormClosing += OnLauncherClosing;
            Paint += OnFormPaint;

            LoadConfigAndLibrary();

            _processPollTimer = new Timer { Interval = 3500 };
            _processPollTimer.Tick += (s, e) => RefreshRunningStatesOnly();
            _processPollTimer.Start();

            StartFadeIn();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayout(WinApi.GetWindowDpi(this));
            CenterToScreen();
            UpdatePreview();
            _inputGame.InnerTextBox.Focus();
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

        private void EnsureScaledFonts(float scale)
        {
            if (Math.Abs(_fontScale - scale) < 0.01f && _titleFont != null)
                return;
            _fontScale = scale;

            Font oldTitle = _titleFont;
            Font oldSection = _sectionFont;
            Font oldBold = _boldFont;
            Font oldLabel = _labelFont;
            Font oldSmall = _smallFont;
            Font oldItemTitle = _itemTitleFont;
            Font oldItemSub = _itemSubFont;
            Font oldCaption = _captionFont;

            _titleFont = new Font("Segoe UI", 10.5f * scale, FontStyle.Bold);
            _sectionFont = new Font("Segoe UI", 12f * scale, FontStyle.Bold);
            _boldFont = new Font("Segoe UI", 12f * scale, FontStyle.Bold);
            _labelFont = new Font("Segoe UI", 9f * scale, FontStyle.Regular);
            _smallFont = new Font("Segoe UI", 8.75f * scale, FontStyle.Regular);
            _itemTitleFont = new Font("Segoe UI", 10.5f * scale, FontStyle.Bold);
            _itemSubFont = new Font("Segoe UI", 8.5f * scale, FontStyle.Regular);
            _captionFont = new Font("Segoe UI", 10f * scale, FontStyle.Regular);

            if (_lblLeftTitle != null)
            {
                _lblLeftTitle.Font = _sectionFont;
                _lblGame.Font = _labelFont;
                _lblFolder.Font = _labelFont;
                _btnBrowseFolder.Font = _labelFont;
                _lblPreviewCaption.Font = _labelFont;
                _lblPreviewPath.Font = _smallFont;
                _lblPreviewStatus.Font = _smallFont;
                _btnLaunch.Font = _boldFont;
                _lblFooterStatus.Font = _smallFont;
                _btnCheckUpdates.Font = _smallFont;
                _btnOpenLog.Font = _smallFont;

                _lblRightTitle.Font = _sectionFont;
                _btnImportScan.Font = _smallFont;
                _btnRefreshLib.Font = _smallFont;
                _btnFilterAll.Font = _smallFont;
                _btnFilterRecent.Font = _smallFont;
                _btnFilterFav.Font = _smallFont;
                _btnLibRun.Font = _smallFont;
                _btnLibUpdate.Font = _smallFont;
                _btnLibFav.Font = _smallFont;
                _btnLibOpenFolder.Font = _smallFont;
                _btnLibCopyPath.Font = _smallFont;
                _btnLibDelete.Font = _smallFont;

                _inputGame.SetFontSize(9.5f * scale);
                _inputFolder.SetFontSize(9.5f * scale);
                _inputSearch.SetFontSize(9f * scale);
            }

            if (_appMenu != null)
                _appMenu.Font = _labelFont;
            if (_btnMore != null)
            {
                _btnMore.Font = _labelFont;
                _btnEmptyImport.Font = _labelFont;
            }
            if (_quickMenu != null)
                _quickMenu.Font = _labelFont;
            if (_entryContextMenu != null)
                _entryContextMenu.Font = _labelFont;

            if (oldTitle != null)
                oldTitle.Dispose();
            if (oldSection != null)
                oldSection.Dispose();
            if (oldBold != null)
                oldBold.Dispose();
            if (oldLabel != null)
                oldLabel.Dispose();
            if (oldSmall != null)
                oldSmall.Dispose();
            if (oldItemTitle != null)
                oldItemTitle.Dispose();
            if (oldItemSub != null)
                oldItemSub.Dispose();
            if (oldCaption != null)
                oldCaption.Dispose();
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

            EnsureScaledFonts(_scale / sysScale);

            int w = S(BaseWidth);
            int h = S(BaseHeight);
            ClientSize = new Size(w, h);
            WinApi.SetRoundedRegion(this, w, h, S(14));

            _headerRect = new Rectangle(0, 0, w, S(36));
            int capW = S(28);
            int capH = S(24);
            _closeRect = new Rectangle(w - S(10) - capW, S(6), capW, capH);
            _minRect = new Rectangle(_closeRect.Left - S(4) - capW, S(6), capW, capH);

            int leftX = S(22);
            int leftW = S(364);
            _dividerX = leftX + leftW + S(18);
            int rightX = _dividerX + S(18);
            int rightW = w - rightX - S(22);

            leftX = S(36);
            leftW = S(296);
            rightX = S(388);
            rightW = S(476);
            _dividerX = S(352);
            _lblLeftTitle.Bounds = new Rectangle(leftX, S(92), leftW, S(26));
            _btnQuickLaunch.Bounds = new Rectangle(_minRect.Left - S(36), S(6), S(28), S(24));
            _lblGame.Bounds = new Rectangle(leftX, S(150), leftW, S(20));
            _inputGame.Bounds = new Rectangle(leftX, S(176), leftW, S(40));
            _lblFolder.Bounds = new Rectangle(leftX, S(234), leftW, S(20));
            _inputFolder.Bounds = new Rectangle(leftX, S(260), leftW - S(48), S(40));
            _btnBrowseFolder.Bounds = new Rectangle(leftX + leftW - S(40), S(260), S(40), S(40));
            _lblPreviewCaption.Bounds = new Rectangle(leftX, S(318), leftW, S(18));
            _previewCard.Bounds = new Rectangle(leftX, S(342), leftW, S(66));
            _lblPreviewPath.Bounds = new Rectangle(S(2), S(2), leftW - S(4), S(30));
            _lblPreviewStatus.Bounds = new Rectangle(S(2), S(34), leftW - S(4), S(30));
            _btnLaunch.Bounds = new Rectangle(leftX, S(426), leftW, S(42));
            _lblFooterStatus.Bounds = new Rectangle(S(36), S(490), S(828), S(20));
            _btnCheckUpdates.Visible = false;
            _btnOpenLog.Visible = false;
            _lblRightTitle.Bounds = new Rectangle(rightX, S(92), S(220), S(26));
            _btnImportScan.Bounds = new Rectangle(rightX + rightW - S(166), S(88), S(124), S(34));
            _btnRefreshLib.Bounds = new Rectangle(rightX + rightW - S(34), S(88), S(34), S(34));
            _inputSearch.Bounds = new Rectangle(rightX, S(138), rightW, S(40));
            int tabW = rightW / 3;
            _btnFilterAll.Bounds = new Rectangle(rightX, S(190), tabW, S(32));
            _btnFilterRecent.Bounds = new Rectangle(rightX + tabW, S(190), tabW, S(32));
            _btnFilterFav.Bounds = new Rectangle(rightX + tabW * 2, S(190), rightW - tabW * 2, S(32));
            _listLibrary.Bounds = new Rectangle(rightX, S(236), rightW - S(12), S(200));
            _listLibrary.ItemHeight = S(76);
            if (_libraryScrollBar != null)
            {
                _libraryScrollBar.Bounds = new Rectangle(rightX + rightW - S(10), S(236), S(10), S(200));
                _libraryScrollBar.RefreshView();
            }
            if (_emptyLibrary != null)
            {
                _emptyLibrary.Bounds = new Rectangle(rightX, S(236), rightW, S(240));
                _btnEmptyImport.Bounds = new Rectangle((rightW - S(190)) / 2, S(182), S(190), S(36));
                _btnMore.Bounds = new Rectangle(rightX + rightW - S(40), S(440), S(40), S(36));
            }
            _btnLibRun.Bounds = new Rectangle(rightX, S(440), S(184), S(36));
            _btnLibUpdate.Bounds = new Rectangle(rightX + S(196), S(440), S(228), S(36));
            _btnLibFav.Visible = false;
            _btnLibOpenFolder.Visible = false;
            _btnLibCopyPath.Visible = false;
            _btnLibDelete.Visible = false;
            foreach (Control control in Controls)
            {
                var button = control as AnimatedButton;
                if (button != null)
                    button.Radius = S(8);
            }

            Invalidate();
        }

        private void InitControls()
        {

            _lblLeftTitle = new Label { Text = "Новая копия", Font = _sectionFont, ForeColor = Color.White,
                                        BackColor = Color.Transparent };
            Controls.Add(_lblLeftTitle);

            _btnQuickLaunch = new AnimatedButton { Radius = 6,
                                                   IsPlayButton = true,
                                                   NormalColor = Theme.Green,
                                                   HoverColor = Theme.GreenHover,
                                                   PressedColor = Theme.GreenPressed,
                                                   TabIndex = 0,
                                                   AccessibleName = "Быстрый запуск из библиотеки" };
            _toolTip.SetToolTip(_btnQuickLaunch, "Быстрый запуск созданных копий из библиотеки");
            _btnQuickLaunch.Click += (s, e) => ShowAppMenu();
            Controls.Add(_btnQuickLaunch);

            _lblGame = new Label { Text = "Имя файла", Font = _labelFont, ForeColor = Theme.TextNormal,
                                   BackColor = Color.Transparent };
            Controls.Add(_lblGame);

            _inputGame = new AnimatedInputBox { TabIndex = 1, PlaceholderText = "Например, Endfield.exe" };
            _inputGame.SetAccessibleInfo("Имя исполняемого файла игры", "Введите имя EXE-файла или полный путь");
            _inputGame.InnerTextBox.KeyDown += OnInputKeyDown;
            _inputGame.InputTextChanged += (s, e) => UpdatePreview();
            Controls.Add(_inputGame);

            _lblFolder = new Label { Text = "Папка назначения", Font = _labelFont, ForeColor = Theme.TextNormal,
                                     BackColor = Color.Transparent };
            Controls.Add(_lblFolder);

            _inputFolder = new AnimatedInputBox { InputText = PathResolver.DefaultFolder, TabIndex = 2,
                                                  PlaceholderText = "games" };
            _inputFolder.SetAccessibleInfo("Папка назначения", "Укажите относительную или полную папку для копии");
            _inputFolder.InnerTextBox.KeyDown += OnInputKeyDown;
            _inputFolder.InputTextChanged += (s, e) => UpdatePreview();
            Controls.Add(_inputFolder);

            _btnBrowseFolder = new AnimatedButton { Text = "Обзор…",
                                                    Font = _labelFont,
                                                    NormalColor = Theme.Secondary,
                                                    HoverColor = Theme.SecondaryHover,
                                                    PressedColor = Theme.SecondaryPressed,
                                                    Radius = 8,
                                                    TabIndex = 3,
                                                    AccessibleName = "Выбрать папку назначения" };
            _btnBrowseFolder.Click += (s, e) => BrowseFolder();
            Controls.Add(_btnBrowseFolder);

            _lblPreviewCaption = new Label { Text = "Итоговый путь", Font = _labelFont, ForeColor = Theme.TextNormal,
                                             BackColor = Color.Transparent };
            Controls.Add(_lblPreviewCaption);

            _previewCard = new Panel { BackColor = Theme.BgDarker, TabStop = false };
            _previewCard.Paint += (s, e) =>
            {

            };
            _lblPreviewPath = new Label { Font = _smallFont, ForeColor = Color.White, BackColor = Color.Transparent,
                                          AutoEllipsis = true };
            _lblPreviewStatus =
                new Label { Font = _smallFont, ForeColor = Theme.TextMuted, BackColor = Color.Transparent };
            _previewCard.Controls.Add(_lblPreviewPath);
            _previewCard.Controls.Add(_lblPreviewStatus);
            Controls.Add(_previewCard);

            _btnLaunch = new AnimatedButton { Text = "Создать и запустить", Font = _boldFont, Radius = 8, TabIndex = 4,
                                              AccessibleName = "Создать или запустить копию" };
            _btnLaunch.Click += (s, e) => LaunchFromInputs();
            Controls.Add(_btnLaunch);

            _lblFooterStatus = new Label { Text = "Готово к работе", Font = _smallFont, ForeColor = Theme.TextMuted,
                                           BackColor = Color.Transparent };
            Controls.Add(_lblFooterStatus);

            _btnCheckUpdates = new AnimatedButton { Text = "Проверить обновления",
                                                    Font = _smallFont,
                                                    NormalColor = Theme.Secondary,
                                                    HoverColor = Theme.SecondaryHover,
                                                    PressedColor = Theme.SecondaryPressed,
                                                    Radius = 6,
                                                    TabIndex = 5,
                                                    AccessibleName = "Проверить наличие новой версии на GitHub" };
            _btnCheckUpdates.Click += (s, e) => CheckForUpdatesAsync();
            Controls.Add(_btnCheckUpdates);

            _btnOpenLog = new AnimatedButton { Text = "Журнал",
                                               Font = _smallFont,
                                               NormalColor = Theme.Secondary,
                                               HoverColor = Theme.SecondaryHover,
                                               PressedColor = Theme.SecondaryPressed,
                                               Radius = 6,
                                               TabIndex = 6,
                                               AccessibleName = "Открыть файл журнала диагностики" };
            _btnOpenLog.Click += (s, e) => OpenLogFile();
            Controls.Add(_btnOpenLog);

            _lblRightTitle = new Label { Text = "Библиотека", Font = _sectionFont, ForeColor = Color.White,
                                         BackColor = Color.Transparent };
            Controls.Add(_lblRightTitle);

            _btnImportScan = new AnimatedButton { Text = "Импорт",
                                                  Font = _smallFont,
                                                  NormalColor = Theme.Secondary,
                                                  HoverColor = Theme.SecondaryHover,
                                                  PressedColor = Theme.SecondaryPressed,
                                                  Radius = 6,
                                                  TabIndex = 7,
                                                  AccessibleName = "Сканировать папку и импортировать старые копии" };
            _toolTip.SetToolTip(_btnImportScan,
                                "Проверить папку в фоне и импортировать существующие копии QuestLauncher");
            _btnImportScan.Click += (s, e) => PromptAndStartImportScan();
            Controls.Add(_btnImportScan);

            _btnRefreshLib = new AnimatedButton { Text = "Обновить",
                                                  Font = _smallFont,
                                                  NormalColor = Theme.Secondary,
                                                  HoverColor = Theme.SecondaryHover,
                                                  PressedColor = Theme.SecondaryPressed,
                                                  Radius = 6,
                                                  TabIndex = 8,
                                                  AccessibleName = "Обновить состояние записей библиотеки" };
            _btnRefreshLib.Click += (s, e) => ReloadLibraryView();
            Controls.Add(_btnRefreshLib);

            _inputSearch = new AnimatedInputBox { TabIndex = 9, PlaceholderText = "Найти игру или путь…" };
            _inputSearch.SetAccessibleInfo("Поиск в библиотеке", "Фильтрация записей по имени или полному пути");
            _inputSearch.InputTextChanged += (s, e) => RebuildVisibleEntries();
            Controls.Add(_inputSearch);

            _btnFilterAll = new AnimatedButton { Text = "Все (0)", Font = _smallFont, Radius = 6, TabIndex = 10,
                                                 AccessibleName = "Показать все записи" };
            _btnFilterAll.Click += (s, e) => SetFilter("all");
            Controls.Add(_btnFilterAll);

            _btnFilterRecent = new AnimatedButton { Text = "Недавние", Font = _smallFont, Radius = 6, TabIndex = 11,
                                                    AccessibleName = "Показать недавно запускавшиеся записи" };
            _btnFilterRecent.Click += (s, e) => SetFilter("recent");
            Controls.Add(_btnFilterRecent);

            _btnFilterFav = new AnimatedButton { Text = "Избранное", Font = _smallFont, Radius = 6, TabIndex = 12,
                                                 AccessibleName = "Показать избранные записи" };
            _btnFilterFav.Click += (s, e) => SetFilter("fav");
            Controls.Add(_btnFilterFav);

            _listLibrary = new LibraryListBox { BackColor = Theme.BgDarker,
                                                ForeColor = Color.White,
                                                BorderStyle = BorderStyle.None,
                                                DrawMode = DrawMode.OwnerDrawFixed,
                                                IntegralHeight = false,
                                                TabIndex = 13,
                                                AccessibleName = "Список созданных копий в библиотеке" };
            _listLibrary.DrawItem += OnDrawLibraryItem;
            _listLibrary.SelectedIndexChanged += (s, e) => OnLibrarySelectionChanged();
            _listLibrary.DoubleClick += (s, e) => LaunchSelectedEntry();
            _listLibrary.KeyDown += OnLibraryListKeyDown;
            _listLibrary.MouseUp += OnLibraryListMouseUp;
            Controls.Add(_listLibrary);
            _libraryScrollBar = new LibraryScrollBar(_listLibrary);
            Controls.Add(_libraryScrollBar);

            _btnLibRun = new AnimatedButton { Text = "Запустить",
                                              Font = _smallFont,
                                              NormalColor = Theme.Green,
                                              HoverColor = Theme.GreenHover,
                                              PressedColor = Theme.GreenPressed,
                                              Radius = 6,
                                              TabIndex = 14,
                                              AccessibleName = "Запустить выбранную копию" };
            _btnLibRun.Click += (s, e) => LaunchSelectedEntry();
            Controls.Add(_btnLibRun);

            _btnLibUpdate =
                new AnimatedButton { Text = "Обновить копию",
                                     Font = _smallFont,
                                     NormalColor = Theme.Blurple,
                                     HoverColor = Theme.BlurpleHover,
                                     PressedColor = Theme.BlurplePressed,
                                     Radius = 6,
                                     TabIndex = 15,
                                     AccessibleName = "Безопасно обновить выбранную копию до текущей версии" };
            _btnLibUpdate.Click += (s, e) => UpdateSelectedEntry();
            Controls.Add(_btnLibUpdate);

            _btnLibFav = new AnimatedButton { Text = "В избранное",
                                              Font = _smallFont,
                                              NormalColor = Theme.Secondary,
                                              HoverColor = Theme.SecondaryHover,
                                              PressedColor = Theme.SecondaryPressed,
                                              Radius = 6,
                                              TabIndex = 16,
                                              AccessibleName = "Добавить или убрать выбранную запись из избранного" };
            _btnLibFav.Click += (s, e) => ToggleSelectedFavorite();
            Controls.Add(_btnLibFav);

            _btnLibOpenFolder = new AnimatedButton { Text = "Открыть папку",
                                                     Font = _smallFont,
                                                     NormalColor = Theme.Secondary,
                                                     HoverColor = Theme.SecondaryHover,
                                                     PressedColor = Theme.SecondaryPressed,
                                                     Radius = 6,
                                                     TabIndex = 17,
                                                     AccessibleName = "Открыть папку с файлом в Проводнике" };
            _btnLibOpenFolder.Click += (s, e) => OpenSelectedFolder();
            Controls.Add(_btnLibOpenFolder);

            _btnLibCopyPath = new AnimatedButton { Text = "Скопировать путь",
                                                   Font = _smallFont,
                                                   NormalColor = Theme.Secondary,
                                                   HoverColor = Theme.SecondaryHover,
                                                   PressedColor = Theme.SecondaryPressed,
                                                   Radius = 6,
                                                   TabIndex = 18,
                                                   AccessibleName = "Скопировать полный путь к файлу в буфер обмена" };
            _btnLibCopyPath.Click += (s, e) => CopySelectedPath();
            Controls.Add(_btnLibCopyPath);

            _btnLibDelete = new AnimatedButton { Text = "Удалить…",
                                                 Font = _smallFont,
                                                 NormalColor = Theme.Red,
                                                 HoverColor = Theme.RedHover,
                                                 PressedColor = Color.FromArgb(165, 38, 40),
                                                 Radius = 6,
                                                 TabIndex = 19,
                                                 AccessibleName = "Удалить выбранную копию или запись" };
            _btnLibDelete.Click += (s, e) => DeleteSelectedEntry();
            Controls.Add(_btnLibDelete);
        }

        private void InitVisualRefinement()
        {
            _emptyLibrary = new EmptyLibraryView();
            Controls.Add(_emptyLibrary);
            _btnEmptyImport = new AnimatedButton { Text = "Импортировать копии",
                                                   IconName = "download",
                                                   Font = _labelFont,
                                                   NormalColor = Theme.Secondary,
                                                   HoverColor = Theme.SecondaryHover,
                                                   PressedColor = Theme.SecondaryPressed,
                                                   AccessibleName = "Импортировать существующие копии",
                                                   TabIndex = 13 };
            _btnEmptyImport.BackColor = Theme.BgCard;
            _btnEmptyImport.Click += (s, e) => PromptAndStartImportScan();
            _emptyLibrary.Controls.Add(_btnEmptyImport);
            _btnMore = new AnimatedButton { IconName = "ellipsis",
                                            Font = _labelFont,
                                            NormalColor = Theme.Secondary,
                                            HoverColor = Theme.SecondaryHover,
                                            PressedColor = Theme.SecondaryPressed,
                                            AccessibleName = "Действия с выбранной записью",
                                            TabIndex = 16 };
            _btnMore.Click += (s, e) => ShowEntryMenu(_btnMore, new Point(_btnMore.Width, 0));
            Controls.Add(_btnMore);
            _appMenu = new DarkContextMenu { Renderer = DarkMenuRenderer.Instance, BackColor = Theme.BgDarker,
                                             ForeColor = Theme.TextNormal, Font = _labelFont };
            _appMenu.Items.Add(
                new ToolStripMenuItem("Быстрый запуск", LucideIcons.Get("play"), (s, e) => ShowQuickLaunchMenu()));
            _appMenu.Items.Add(new ToolStripMenuItem("Проверить обновления", LucideIcons.Get("download"),
                                                     (s, e) => CheckForUpdatesAsync()));
            _appMenu.Items.Add(new ToolStripMenuItem("Журнал", LucideIcons.Get("file-text"), (s, e) => OpenLogFile()));
            _appMenu.Items.Add(new ToolStripSeparator());
            _appMenu.Items.Add(new ToolStripMenuItem(
                "О программе", LucideIcons.Get("gamepad-2"),
                (s, e) => MessageBox.Show(
                    this, "Quest Launcher v" + AppInfo.Version + "\nАвтор: vaelit\nИконки: Lucide (ISC / MIT).",
                    "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            _btnQuickLaunch.IsPlayButton = false;
            _btnQuickLaunch.IconName = "settings-2";
            _btnQuickLaunch.AccessibleName = "Меню приложения";
            _toolTip.SetToolTip(_btnQuickLaunch, "Меню приложения");
            _btnQuickLaunch.NormalColor = Theme.BgWindow;
            _btnQuickLaunch.HoverColor = Theme.SecondaryHover;
            _btnQuickLaunch.PressedColor = Theme.SecondaryPressed;
            _btnBrowseFolder.Text = "";
            _btnBrowseFolder.IconName = "folder";
            _toolTip.SetToolTip(_btnBrowseFolder, "Выбрать папку");
            _btnImportScan.IconName = "plus";
            _btnRefreshLib.Text = "";
            _btnRefreshLib.IconName = "refresh-cw";
            _toolTip.SetToolTip(_btnRefreshLib, "Обновить библиотеку");
            _btnLaunch.IconName = "play";
            _btnLibRun.IconName = "play";
            _btnLibUpdate.IconName = "refresh-cw";
            _btnLibRun.NormalColor = Theme.Blurple;
            _btnLibRun.HoverColor = Theme.BlurpleHover;
            _btnLibRun.PressedColor = Theme.BlurplePressed;
            _btnLibUpdate.NormalColor = Theme.Secondary;
            _btnLibUpdate.HoverColor = Theme.SecondaryHover;
            _btnLibUpdate.PressedColor = Theme.SecondaryPressed;
            _inputSearch.IconName = "search";
            _previewCard.BackColor = Theme.BgCard;
            _listLibrary.BackColor = Theme.BgCard;
            foreach (Control c in Controls)
                if (c is Label || c is AnimatedButton || c is AnimatedInputBox)
                    c.BackColor = Theme.BgCard;
            _btnQuickLaunch.BackColor = Theme.BgWindow;
            _lblFooterStatus.BackColor = Theme.BgWindow;
        }
        private void ShowAppMenu()
        {
            _appMenu.Show(_btnQuickLaunch, new Point(0, _btnQuickLaunch.Height + 4));
        }

        private void InitContextMenus()
        {
            _quickMenu = new DarkContextMenu { Renderer = DarkMenuRenderer.Instance, ShowImageMargin = true,
                                               ImageScalingSize = new Size(16, 16),  Font = _labelFont,
                                               BackColor = Theme.BgDarker,           ForeColor = Color.White };

            _entryContextMenu =
                new DarkContextMenu { Renderer = DarkMenuRenderer.Instance, ShowImageMargin = true, Font = _labelFont,
                                      BackColor = Theme.BgDarker, ForeColor = Color.White };
        }

        private void LoadConfigAndLibrary()
        {
            _settings = Settings.Load(DataPaths.ConfigPath, DataPaths.LegacyConfigPath);
            _inputGame.InputText = _settings.Game;
            if (_inputGame.InnerTextBox != null)
                _inputGame.InnerTextBox.SelectionStart = _inputGame.InnerTextBox.Text.Length;

            if (!string.IsNullOrEmpty(_settings.Folder))
                _inputFolder.InputText = _settings.Folder;

            string f = (_settings.Filter ?? "all").ToLowerInvariant();
            if (f != "all" && f != "recent" && f != "fav")
                f = "all";
            _activeFilter = f;

            ReloadLibraryView();

            if (!string.IsNullOrEmpty(_settings.LoadWarning))
                SetStatus(_settings.LoadWarning, true);
            else if (!string.IsNullOrEmpty(_library.LastWarning))
                SetStatus(_library.LastWarning, true);
            else if (_allEntries.Count == 0)
                SetStatus("Создайте новую копию или импортируйте существующие файлы.", false);
        }

        private void SaveConfig()
        {
            try
            {
                if (_settings == null)
                    _settings = new Settings();
                _settings.Game = _inputGame != null ? _inputGame.InputText.Trim() : string.Empty;
                string folder = _inputFolder != null ? _inputFolder.InputText.Trim() : PathResolver.DefaultFolder;
                _settings.Folder = string.IsNullOrEmpty(folder) ? PathResolver.DefaultFolder : folder;
                _settings.Filter = _activeFilter;
                _settings.Save(DataPaths.ConfigPath);
            }
            catch (Exception ex)
            {
                Log.Error("Сохранение конфигурации", ex);
            }
        }

        private void SetStatus(string text, bool isWarning)
        {
            if (_lblFooterStatus == null)
                return;
            _lblFooterStatus.ForeColor = isWarning ? Theme.Yellow : Theme.TextMuted;
            _lblFooterStatus.Text = text ?? string.Empty;
        }

        private void BrowseFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Выберите папку назначения для создания копии игры:";
                dlg.ShowNewFolderButton = true;

                string cur = PathResolver.CleanInput(_inputFolder.InputText);
                if (PathResolver.IsFullyQualified(cur) && Directory.Exists(cur))
                    dlg.SelectedPath = cur;
                else
                {
                    string rel = Path.Combine(AppInfo.AppDirectory,
                                              string.IsNullOrEmpty(cur) ? PathResolver.DefaultFolder : cur);
                    dlg.SelectedPath = Directory.Exists(rel) ? rel : AppInfo.AppDirectory;
                }

                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                {
                    _inputFolder.InputText = PathResolver.ToFolderInput(dlg.SelectedPath, AppInfo.AppDirectory);
                    SaveConfig();
                }
            }
        }

        private void UpdatePreview()
        {
            if (_lblPreviewPath == null)
                return;

            ResolvedTarget rt =
                PathResolver.Resolve(_inputGame.InputText, _inputFolder.InputText, AppInfo.AppDirectory);
            if (string.IsNullOrWhiteSpace(_inputGame.InputText))
            {
                _lblPreviewPath.Text = "Появится после ввода имени файла";
                _lblPreviewPath.ForeColor = Theme.TextMuted;
                _lblPreviewStatus.Text = "Существующие файлы не будут заменены.";
                _lblPreviewStatus.ForeColor = Theme.TextMuted;
                _btnLaunch.Enabled = false;
                _btnLaunch.Text = "Создать и запустить";
                _toolTip.SetToolTip(_lblPreviewPath, string.Empty);
                return;
            }
            _lblPreviewPath.ForeColor = Theme.TextNormal;
            if (!rt.Ok)
            {
                _lblPreviewPath.ForeColor = Theme.TextMuted;
                _lblPreviewPath.Text = rt.Error;
                _lblPreviewStatus.ForeColor = Theme.Yellow;
                _lblPreviewStatus.Text = "Укажите корректное имя и папку.";
                _btnLaunch.Enabled = false;
                _btnLaunch.Text = "Создать и запустить";
                return;
            }

            _lblPreviewPath.ForeColor = Color.White;
            _lblPreviewPath.Text = rt.FullPath;
            _toolTip.SetToolTip(_lblPreviewPath, rt.FullPath);

            if (PathResolver.SamePath(rt.FullPath, AppInfo.ExecutablePath))
            {
                _lblPreviewStatus.ForeColor = Theme.Red;
                _lblPreviewStatus.Text = "Запрещено: путь совпадает с самим лаунчером.";
                _btnLaunch.Enabled = false;
                return;
            }

            string curBuild = null;
            try
            {
                curBuild = _copyService.CurrentBuildHash;
            }
            catch
            {
            }
            LibraryEntry reg = Library.FindByPath(_allEntries, rt.FullPath);
            FileInspection fi = CopyIdentity.Inspect(rt.FullPath, curBuild);

            if (fi.Kind == FileKind.Missing)
            {
                _lblPreviewStatus.ForeColor = Color.FromArgb(112, 207, 158);
                _lblPreviewStatus.Text = "Файл ещё не существует — будет создана новая копия v" + AppInfo.Version +
                                         (rt.Note != null ? " (" + rt.Note + ")" : "");
                _btnLaunch.Enabled = true;
                _btnLaunch.Text = "Создать и запустить";
                return;
            }

            if (reg != null && string.Equals(reg.Sha256, fi.Sha256, StringComparison.OrdinalIgnoreCase) &&
                fi.IsVerifiedOwnedCopy)
            {
                bool sameBuild = !string.IsNullOrEmpty(curBuild) &&
                                 string.Equals(curBuild, fi.BuildHash, StringComparison.OrdinalIgnoreCase);
                _lblPreviewStatus.ForeColor = sameBuild ? Color.FromArgb(112, 207, 158) : Theme.Yellow;
                _lblPreviewStatus.Text = sameBuild ? "Копия готова к запуску."
                                                   : "Устаревшая собственная копия (можно запустить или обновить).";
                _btnLaunch.Enabled = true;
                _btnLaunch.Text = "Запустить существующую";
                return;
            }

            if (reg != null && string.Equals(reg.Sha256, fi.Sha256, StringComparison.OrdinalIgnoreCase) &&
                fi.Kind == FileKind.LegacyLikely)
            {
                _lblPreviewStatus.ForeColor = Theme.Yellow;
                _lblPreviewStatus.Text =
                    "Неподтверждённый кандидат в реестре (разрешён только запуск без замены файла).";
                _btnLaunch.Enabled = true;
                _btnLaunch.Text = "Запустить существующую";
                return;
            }

            if (fi.IsVerifiedOwnedCopy || fi.Kind == FileKind.LegacyLikely)
            {
                _lblPreviewStatus.ForeColor = Theme.Yellow;
                _lblPreviewStatus.Text = fi.IsVerifiedOwnedCopy
                                             ? "Существующая копия QuestLauncher вне реестра. Будет предложено " +
                                                   "импортировать и запустить (без перезаписи)."
                                             : "Неподтверждённый кандидат по сигнатуре вне реестра. Можно добавить в " +
                                                   "список только для запуска (без права замены).";
                _btnLaunch.Enabled = true;
                _btnLaunch.Text = "Проверить и запустить";
                return;
            }

            _lblPreviewStatus.ForeColor = Theme.Red;
            _lblPreviewStatus.Text = "Заблокировано: по этому пути уже находится " + CopyIdentity.Describe(fi.Kind) +
                                     ". Перезапись запрещена.";
            _btnLaunch.Enabled = false;
            _btnLaunch.Text = "Перезапись запрещена";
        }

        private void LaunchFromInputs()
        {
            if (_isLaunching)
                return;
            _isLaunching = true;
            try
            {
                LaunchFromInputsCore();
            }
            finally
            {
                _isLaunching = false;
            }
        }

        private void LaunchFromInputsCore()
        {
            ResolvedTarget rt =
                PathResolver.Resolve(_inputGame.InputText, _inputFolder.InputText, AppInfo.AppDirectory);
            if (!rt.Ok)
            {
                MessageBox.Show(this, rt.Error, "Некорректный путь", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (PathResolver.SamePath(rt.FullPath, AppInfo.ExecutablePath))
            {
                MessageBox.Show(this, "Нельзя создать или запустить игру поверх самого лаунчера.", "Защита лаунчера",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveConfig();

            string curBuild = null;
            try
            {
                curBuild = _copyService.CurrentBuildHash;
            }
            catch
            {
            }
            LibraryEntry existingEntry = Library.FindByPath(_allEntries, rt.FullPath);
            FileInspection fi = CopyIdentity.Inspect(rt.FullPath, curBuild);

            if (fi.Kind == FileKind.Missing)
            {
                CopyServiceResult cr = _copyService.CreateCopy(rt.FullPath, rt.FileName);
                if (!cr.Ok)
                {
                    MessageBox.Show(this, cr.Message, "Ошибка создания копии", MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                    ReloadLibraryView();
                    return;
                }
                ReloadLibraryView();
                SelectEntryByPath(rt.FullPath);
                StartEntryProcess(cr.Entry);
                return;
            }

            if (existingEntry != null)
            {
                EntryView ev = _copyService.Evaluate(existingEntry, _runningMap);
                if (ev.Status == EntryStatus.Modified || ev.Status == EntryStatus.Unreadable)
                {
                    MessageBox.Show(this,
                                    "Файл по целевому пути не прошёл проверку принадлежности (" + ev.StatusText +
                                        "):\n" + rt.FullPath +
                                        "\n\nВ целях безопасности он не будет перезаписан или запущен.",
                                    "Защита от перезаписи", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (ev.Status == EntryStatus.Outdated)
                {
                    DialogResult dr = MessageBox.Show(
                        this,
                        "Копия «" + existingEntry.Name + "» создана более старой версией (" +
                            (existingEntry.Version ?? "старая") + ").\n\n" + "Да — безопасно обновить до v" +
                            AppInfo.Version + " и запустить\n" + "Нет — запустить текущую копию без изменений\n" +
                            "Отмена — не запускать",
                        "Доступно обновление копии", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (dr == DialogResult.Cancel)
                        return;
                    if (dr == DialogResult.Yes)
                    {
                        CopyServiceResult ur = _copyService.UpdateCopy(existingEntry);
                        if (!ur.Ok)
                        {
                            MessageBox.Show(this, ur.Message, "Не удалось обновить копию", MessageBoxButtons.OK,
                                            MessageBoxIcon.Warning);
                            ReloadLibraryView();
                            return;
                        }
                        existingEntry = ur.Entry;
                        ReloadLibraryView();
                    }
                }
                SelectEntryByPath(existingEntry.Path);
                StartEntryProcess(existingEntry);
                return;
            }

            if (fi.Kind == FileKind.OwnCopy || fi.Kind == FileKind.LegacyKnown || fi.Kind == FileKind.LegacyLikely)
            {
                DialogResult dr = MessageBox.Show(
                    this,
                    "По указанному пути уже находится " + CopyIdentity.Describe(fi.Kind) + ":\n" + rt.FullPath +
                        "\n\nДобавить этот файл в библиотеку и запустить его (без перезаписи)?",
                    "Найдена существующая копия", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes)
                    return;

                var cand =
                    new ImportCandidate { Path = rt.FullPath,   Name = rt.FileName, Kind = fi.Kind,
                                          Version = fi.Version, Sha256 = fi.Sha256, BuildHash = fi.BuildHash,
                                          CopyId = fi.CopyId,   Size = fi.Size,     LastWriteUtc = fi.LastWriteUtc,
                                          Selected = true };
                _copyService.ImportSelected(new[] { cand });
                ReloadLibraryView();
                LibraryEntry imported = Library.FindByPath(_allEntries, rt.FullPath);
                if (imported != null)
                {
                    SelectEntryByPath(imported.Path);
                    StartEntryProcess(imported);
                }
                return;
            }

            MessageBox.Show(
                this,
                "По указанному пути уже находится посторонний файл:\n" + rt.FullPath +
                    "\n\nQuestLauncher никогда не перезаписывает чужие EXE-файлы. Выберите другое имя или папку.",
                "Перезапись заблокирована", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ReloadLibraryView()
        {
            _allEntries = _library.Load();
            var paths = new List<string>(_allEntries.Count);
            foreach (LibraryEntry e in _allEntries)
                paths.Add(e.Path);
            _runningMap = ProcessTools.FindRunning(paths);
            RebuildVisibleEntries();
            UpdatePreview();
        }

        private void RefreshRunningStatesOnly()
        {
            if (_isClosing || WindowState == FormWindowState.Minimized || _allEntries.Count == 0)
                return;
            var paths = new List<string>(_allEntries.Count);
            foreach (LibraryEntry e in _allEntries)
                paths.Add(e.Path);
            Dictionary<string, List<int>> newMap = ProcessTools.FindRunning(paths);

            bool changed = false;
            foreach (EntryView v in _visibleEntries)
            {
                int c = ProcessTools.CountFor(newMap, v.Entry.Path);
                if (c != v.RunningCount)
                {
                    changed = true;
                    break;
                }
            }
            _runningMap = newMap;
            if (changed)
            {
                RebuildVisibleEntries();
            }
        }

        private void SetFilter(string filter)
        {
            _activeFilter = filter;
            SaveConfig();
            RebuildVisibleEntries();
        }

        private void UpdateFilterButtonStyles()
        {
            int favCount = 0;
            int recentCount = 0;
            foreach (LibraryEntry e in _allEntries)
            {
                if (e.Favorite)
                    favCount++;
                if (e.LastLaunchUtc != DateTime.MinValue)
                    recentCount++;
            }

            _btnFilterAll.Text = "Все (" + _allEntries.Count + ")";
            _btnFilterRecent.Text = "Недавние (" + recentCount + ")";
            _btnFilterFav.Text = "Избранное (" + favCount + ")";

            StyleTabButton(_btnFilterAll, _activeFilter == "all");
            StyleTabButton(_btnFilterRecent, _activeFilter == "recent");
            StyleTabButton(_btnFilterFav, _activeFilter == "fav");
        }

        private static void StyleTabButton(AnimatedButton btn, bool active)
        {
            btn.NormalColor = active ? Theme.Secondary : Theme.BgCard;
            btn.HoverColor = Theme.SecondaryHover;
            btn.PressedColor = Theme.SecondaryPressed;
            btn.ForeColor = active ? Color.White : Theme.TextMuted;
        }

        private void RebuildVisibleEntries()
        {
            string selectedPath = GetSelectedEntry() != null ? GetSelectedEntry().Entry.Path : null;
            UpdateFilterButtonStyles();

            string q = (_inputSearch != null ? _inputSearch.InputText : string.Empty).Trim();
            var views = new List<EntryView>();

            foreach (LibraryEntry e in _allEntries)
            {
                if (_activeFilter == "fav" && !e.Favorite)
                    continue;
                if (_activeFilter == "recent" && e.LastLaunchUtc == DateTime.MinValue)
                    continue;

                if (q.Length > 0)
                {
                    bool matchName = e.Name != null && e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchPath = e.Path != null && e.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchName && !matchPath)
                        continue;
                }

                views.Add(_copyService.Evaluate(e, _runningMap));
            }

            views.Sort((a, b) =>
                       {
                           if (_activeFilter == "recent")
                           {
                               int cmp = b.Entry.LastLaunchUtc.CompareTo(a.Entry.LastLaunchUtc);
                               if (cmp != 0)
                                   return cmp;
                           }
                           if (a.Entry.Favorite != b.Entry.Favorite)
                               return a.Entry.Favorite ? -1 : 1;
                           int nameCmp = string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase);
                           if (nameCmp != 0)
                               return nameCmp;
                           return string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase);
                       });

            _visibleEntries = views;

            _listLibrary.BeginUpdate();
            try
            {
                _listLibrary.Items.Clear();
                int restoreIdx = -1;
                for (int i = 0; i < _visibleEntries.Count; i++)
                {
                    _listLibrary.Items.Add(_visibleEntries[i]);
                    if (selectedPath != null && PathResolver.SamePath(_visibleEntries[i].Entry.Path, selectedPath))
                        restoreIdx = i;
                }
                if (restoreIdx >= 0)
                    _listLibrary.SelectedIndex = restoreIdx;
                else if (_visibleEntries.Count > 0)
                    _listLibrary.SelectedIndex = 0;
            }
            finally
            {
                _listLibrary.EndUpdate();
            }

            UpdateLibraryActionButtons();
        }

        private EntryView GetSelectedEntry()
        {
            int idx = _listLibrary != null ? _listLibrary.SelectedIndex : -1;
            if (idx < 0 || idx >= _visibleEntries.Count)
                return null;
            return _visibleEntries[idx];
        }

        private void SelectEntryByPath(string path)
        {
            for (int i = 0; i < _visibleEntries.Count; i++)
            {
                if (PathResolver.SamePath(_visibleEntries[i].Entry.Path, path))
                {
                    _listLibrary.SelectedIndex = i;
                    break;
                }
            }
        }

        private void OnLibrarySelectionChanged()
        {
            EntryView v = GetSelectedEntry();
            if (v != null)
            {
                _inputGame.InputText = Path.GetFileName(v.Entry.Path);
                _inputFolder.InputText =
                    PathResolver.ToFolderInput(Path.GetDirectoryName(v.Entry.Path), AppInfo.AppDirectory);
            }
            UpdateLibraryActionButtons();
        }

        private void UpdateLibraryActionButtons()
        {
            EntryView v = GetSelectedEntry();
            bool has = v != null;
            _btnLibRun.Visible = has;
            _btnLibUpdate.Visible = has && (v.Status == EntryStatus.Outdated || v.Status == EntryStatus.Missing ||
                                            v.Status == EntryStatus.RecoveryPending);
            if (_btnMore != null)
                _btnMore.Visible = has;
            if (_emptyLibrary != null)
            {
                bool empty = _visibleEntries.Count == 0;
                _listLibrary.Visible = !empty;
                _libraryScrollBar.RefreshView();
                _emptyLibrary.Visible = empty;
                _emptyLibrary.Filtered = _allEntries.Count > 0;
                _emptyLibrary.Invalidate();
                _btnEmptyImport.Visible = empty && _allEntries.Count == 0;
                if (empty)
                {
                    _emptyLibrary.BringToFront();
                    _btnEmptyImport.BringToFront();
                }
            }
            _btnLibRun.Enabled = has && (v.Status == EntryStatus.Ready || v.Status == EntryStatus.Outdated ||
                                         v.Status == EntryStatus.Unverified || v.Status == EntryStatus.Missing);
            _btnLibUpdate.Enabled = has && (v.Status == EntryStatus.Outdated || v.Status == EntryStatus.Missing ||
                                            v.Status == EntryStatus.RecoveryPending);
            if (has && v.Status == EntryStatus.Missing)
                _btnLibUpdate.Text = "Воссоздать";
            else if (has && v.Status == EntryStatus.RecoveryPending)
                _btnLibUpdate.Text = "Восстановить (.qlbak)…";
            else
                _btnLibUpdate.Text = "Обновить копию";
            _btnLibFav.Enabled = has;
            _btnLibFav.Text = (has && v.Entry.Favorite) ? "В избранном" : "В избранное";
            _btnLibOpenFolder.Enabled = has;
            _btnLibCopyPath.Enabled = has;
            _btnLibDelete.Enabled = has;
        }

        private void StartEntryProcess(LibraryEntry entry)
        {
            if (entry == null)
                return;

            EntryView ev = _copyService.Evaluate(entry, _runningMap);
            if (ev.Status == EntryStatus.Modified || ev.Status == EntryStatus.Unreadable)
            {
                MessageBox.Show(
                    this,
                    "Запуск отменён: файл по пути записи был изменён или подменён и не соответствует сохранённому хешу:\n" +
                        entry.Path,
                    "Проверка безопасности", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (ev.Status == EntryStatus.Missing)
            {
                DialogResult createDr = MessageBox.Show(
                    this, "Файл копии отсутствует на диске:\n" + entry.Path + "\n\nВоссоздать копию и запустить?",
                    "Файл отсутствует", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (createDr != DialogResult.Yes)
                    return;
                CopyServiceResult rec = _copyService.RecreateMissing(entry);
                if (!rec.Ok)
                {
                    MessageBox.Show(this, rec.Message, "Ошибка воссоздания", MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                    return;
                }
                entry = rec.Entry;
            }

            List<int> runningPids = ProcessTools.FindRunning(entry.Path);
            if (runningPids.Count > 0)
            {
                DialogResult dupDr =
                    MessageBox.Show(this,
                                    "Эта копия уже запущена (PID: " + string.Join(", ", runningPids) + "):\n" +
                                        entry.Path + "\n\nЗапустить ещё один экземпляр?",
                                    "Копия уже запущена", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dupDr != DialogResult.Yes)
                    return;
            }

            try
            {
                int pid = ProcessTools.StartCopy(entry.Path);
                _copyService.RecordLaunch(entry.Path);
                SetStatus("Запущено: " + entry.Name + (pid > 0 ? " (PID " + pid + ")" : ""), false);
                ReloadLibraryView();
                SelectEntryByPath(entry.Path);
            }
            catch (Exception ex)
            {
                Log.Error("Запуск копии " + entry.Path, ex);
                MessageBox.Show(this, "Не удалось запустить копию:\n" + entry.Path + "\n\n" + ex.Message,
                                "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LaunchSelectedEntry()
        {
            EntryView v = GetSelectedEntry();
            if (v == null)
                return;
            StartEntryProcess(v.Entry);
        }

        private void UpdateSelectedEntry()
        {
            EntryView v = GetSelectedEntry();
            if (v == null)
                return;

            if (v.Status == EntryStatus.RecoveryPending)
            {
                string bakPath = FileOps.GetBackupPath(v.Entry.Path);
                DialogResult dr = MessageBox.Show(
                    this,
                    "Обнаружена подлинная резервная копия (.qlbak) от незавершённого обновления, но " +
                        "текущий целевой файл отличается от ожидаемого:\n\n" + "Файл: " + v.Entry.Path + "\n" +
                        "Резервная копия: " + bakPath + "\n\n" +
                        ("Поскольку незавершённую запись нельзя автоматически отличить от внешней замены " +
                         "файла, оба файла были сохранены.\n\n") +
                        ("Да — сохранить текущий файл рядом в .displaced-<время>.bak и восстановить " +
                         "проверенную копию из .qlbak\n") +
                        "Нет — ничего не менять (оставить оба файла как есть)",
                    "Подтверждение восстановления из резервной копии", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (dr != DialogResult.Yes)
                    return;

                CopyServiceResult restoreRes = _copyService.RestoreFromBackupConfirmed(v.Entry, true);
                if (!restoreRes.Ok)
                {
                    MessageBox.Show(this, restoreRes.Message, "Восстановление из .qlbak", MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                }
                else
                {
                    SetStatus(restoreRes.Message + " (" + v.Entry.Name + ")", false);
                }
                ReloadLibraryView();
                SelectEntryByPath(v.Entry.Path);
                return;
            }

            CopyServiceResult res = (v.Status == EntryStatus.Missing) ? _copyService.RecreateMissing(v.Entry)
                                                                      : _copyService.UpdateCopy(v.Entry);

            if (!res.Ok)
            {
                MessageBox.Show(this, res.Message, "Обновление копии", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                SetStatus(res.Message + " (" + v.Entry.Name + ")", false);
            }
            ReloadLibraryView();
            SelectEntryByPath(v.Entry.Path);
        }

        private void ToggleSelectedFavorite()
        {
            EntryView v = GetSelectedEntry();
            if (v == null)
                return;
            _copyService.ToggleFavorite(v.Entry.Path);
            ReloadLibraryView();
            SelectEntryByPath(v.Entry.Path);
        }

        private void OpenSelectedFolder()
        {
            EntryView v = GetSelectedEntry();
            if (v == null)
                return;
            try
            {
                ProcessTools.OpenFolder(v.Entry.Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
            }
        }

        private void CopySelectedPath()
        {
            EntryView v = GetSelectedEntry();
            if (v == null)
                return;
            try
            {
                Clipboard.SetText(v.Entry.Path);
                SetStatus("Путь скопирован в буфер обмена: " + v.Entry.Path, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось скопировать путь в буфер обмена:\n" + ex.Message, "Ошибка",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeleteSelectedEntry()
        {
            EntryView v = GetSelectedEntry();
            if (v == null)
                return;

            if (v.Status == EntryStatus.Missing)
            {
                DialogResult drMissing = MessageBox.Show(
                    this, "Файл уже отсутствует на диске:\n" + v.Entry.Path + "\n\nУбрать эту запись из библиотеки?",
                    "Удаление записи", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (drMissing == DialogResult.Yes)
                {
                    _copyService.DeleteCopy(v.Entry, false);
                    ReloadLibraryView();
                }
                return;
            }

            if (v.Status == EntryStatus.Modified || v.Status == EntryStatus.Unverified ||
                v.Status == EntryStatus.RecoveryPending)
            {
                string reason;
                if (v.Status == EntryStatus.Unverified)
                    reason =
                        "Файл по этому пути определён только по косвенным признакам (неподтверждённый кандидат) и " +
                        "не имеет подтверждённого хеша релиза:\n";
                else if (v.Status == EntryStatus.RecoveryPending)
                    reason = "Для этого пути обнаружен неразрешённый конфликт с резервной копией (.qlbak):\n";
                else
                    reason = "Файл по этому пути был изменён извне и больше не совпадает с записью библиотеки:\n";
                DialogResult drMod = MessageBox.Show(
                    this,
                    reason + v.Entry.Path +
                        "\n\nВ целях безопасности сам файл на диске удалён НЕ будет.\nУбрать запись из библиотеки?",
                    "Удаление только записи", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (drMod == DialogResult.Yes)
                {
                    _copyService.DeleteCopy(v.Entry, false);
                    ReloadLibraryView();
                }
                return;
            }

            DialogResult dr = MessageBox.Show(
                this,
                "Удалить копию «" + v.Entry.Name + "»?\n" + v.Entry.Path + "\n\n" +
                    "Да — удалить собственный EXE-файл с диска и убрать запись из библиотеки\n" +
                    "Нет — оставить файл на диске, убрать только запись из библиотеки\n" + "Отмена — ничего не делать",
                "Подтверждение удаления", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button3);

            if (dr == DialogResult.Cancel)
                return;
            bool deleteFile = (dr == DialogResult.Yes);
            CopyServiceResult res = _copyService.DeleteCopy(v.Entry, deleteFile);
            if (!res.Ok)
            {
                MessageBox.Show(this, res.Message, "Ошибка удаления", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                SetStatus(res.Message, false);
            }
            ReloadLibraryView();
        }

        private void OnLibraryListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                LaunchSelectedEntry();
            }
            else if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                DeleteSelectedEntry();
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                e.Handled = true;
                CopySelectedPath();
            }
        }

        private void OnLibraryListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;
            int idx = _listLibrary.IndexFromPoint(e.Location);
            if (idx >= 0 && idx < _visibleEntries.Count)
                _listLibrary.SelectedIndex = idx;

            ShowEntryMenu(_listLibrary, e.Location);
        }

        private void ShowEntryMenu(Control anchor, Point location)
        {

            EntryView v = GetSelectedEntry();
            if (v == null)
                return;

            while (_entryContextMenu.Items.Count > 0)
                _entryContextMenu.Items[0].Dispose();
            var miRun = new ToolStripMenuItem("Запустить", null,
                                              (s, a) => LaunchSelectedEntry()) { Enabled = _btnLibRun.Enabled };
            var miUpdate = new ToolStripMenuItem(_btnLibUpdate.Text, null,
                                                 (s, a) => UpdateSelectedEntry()) { Enabled = _btnLibUpdate.Enabled };
            var miUpdateAll = new ToolStripMenuItem("Обновить все", LucideIcons.Get("refresh-cw"),
                                                    (s, a) => UpdateAllCopies()) { Enabled = !_isUpdatingAll &&
                                                                                             _allEntries.Count > 0 };
            var miFav = new ToolStripMenuItem(v.Entry.Favorite ? "Убрать из избранного" : "Добавить в избранное", null,
                                              (s, a) => ToggleSelectedFavorite());
            var miOpen = new ToolStripMenuItem("Открыть папку в Проводнике", null, (s, a) => OpenSelectedFolder());
            var miCopy = new ToolStripMenuItem("Скопировать полный путь", null, (s, a) => CopySelectedPath());
            var miDel = new ToolStripMenuItem("Удалить…", null, (s, a) => DeleteSelectedEntry());

            miRun.Image = LucideIcons.Get("play");
            miUpdate.Image = LucideIcons.Get("refresh-cw");
            miFav.Image = LucideIcons.Get("star");
            miOpen.Image = LucideIcons.Get("folder-open");
            miCopy.Image = LucideIcons.Get("copy");
            miDel.Image = LucideIcons.Get("x");
            _entryContextMenu.Items.Add(miRun);
            _entryContextMenu.Items.Add(miUpdate);
            _entryContextMenu.Items.Add(miUpdateAll);
            _entryContextMenu.Items.Add(miFav);
            _entryContextMenu.Items.Add(new ToolStripSeparator());
            _entryContextMenu.Items.Add(miOpen);
            _entryContextMenu.Items.Add(miCopy);
            _entryContextMenu.Items.Add(new ToolStripSeparator());
            _entryContextMenu.Items.Add(miDel);

            _entryContextMenu.ShowImageMargin = true;
            _entryContextMenu.ShowCheckMargin = false;
            _entryContextMenu.ShowItemToolTips = true;
            _entryContextMenu.Padding = new Padding(S(5));
            _entryContextMenu.ImageScalingSize = new Size(S(16), S(16));
            foreach (ToolStripItem item in _entryContextMenu.Items)
            {
                if (item is ToolStripMenuItem)
                    item.Padding = new Padding(S(8), S(5), S(10), S(5));
            }
            _entryContextMenu.Show(anchor, location, ToolStripDropDownDirection.AboveLeft);
        }

        private async void UpdateAllCopies()
        {
            if (_isUpdatingAll)
                return;
            _isUpdatingAll = true;
            _processPollTimer.Stop();
            Enabled = false;
            SetStatus("Обновление копий…", false);
            try
            {
                BulkUpdateResult result = await Task.Run(() => _copyService.UpdateOutdatedCopies());
                ReloadLibraryView();
                UpdatePreview();
                string summary = "Обновлено: " + result.Updated + ". Пропущено: " + result.Skipped +
                                 ". Ошибок: " + result.Errors.Count + ".";
                SetStatus(summary, result.Errors.Count > 0);
                if (result.Errors.Count > 0)
                {
                    foreach (string error in result.Errors)
                        Log.Warn(error);
                    MessageBox.Show(this,
                                    summary + "\n\n" +
                                        string.Join("\n", result.Errors.GetRange(0, Math.Min(8, result.Errors.Count))) +
                                        (result.Errors.Count > 8 ? "\nОстальные ошибки записаны в журнал." : ""),
                                    "Обновление копий", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Обновление всех копий", ex);
                SetStatus("Не удалось завершить обновление. Подробности — в журнале.", true);
            }
            finally
            {
                _isUpdatingAll = false;
                Enabled = true;
                _processPollTimer.Start();
            }
        }

        private void OnDrawLibraryItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _visibleEntries.Count)
                return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var v = _visibleEntries[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(Theme.BgCard)) g.FillRectangle(bg, e.Bounds);
            var card =
                new Rectangle(e.Bounds.X + S(1), e.Bounds.Y + S(2), e.Bounds.Width - S(3), e.Bounds.Height - S(6));
            using (var path = WinApi.CreateRoundRect(card, S(8))) using (
                var fill = new SolidBrush(selected ? Theme.BgCardSelected : Theme.BgCard))
            {
                g.FillPath(fill, path);
                if (selected && _listLibrary.Focused)
                    using (var pen = new Pen(Theme.FocusRing)) g.DrawPath(pen, path);
            }
            LucideIcons.Draw(g, "gamepad-2", new Rectangle(card.Left + S(12), card.Top + S(17), S(28), S(28)),
                             selected ? Color.FromArgb(160, 166, 255) : Theme.TextMuted);
            int textX = card.Left + S(56);
            var titleRect = new Rectangle(textX, card.Top + S(8), card.Width - S(88), S(22));
            TextRenderer.DrawText(g, v.Entry.Name, _itemTitleFont, titleRect, Theme.TextNormal,
                                  TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (v.Entry.Favorite)
                LucideIcons.Draw(g, "star", new Rectangle(card.Right - S(27), card.Top + S(10), S(16), S(16)),
                                 Theme.Yellow);
            var pathRect = new Rectangle(textX, card.Top + S(31), card.Width - S(68), S(17));
            TextRenderer.DrawText(g, CompactPathMiddle(g, v.Entry.Path, _itemSubFont, pathRect.Width), _itemSubFont,
                                  pathRect, Theme.TextMuted,
                                  TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            Color status = (v.Status == EntryStatus.Ready) ? Theme.TextMuted
                           : (v.Status == EntryStatus.Outdated || v.Status == EntryStatus.Unverified ||
                              v.Status == EntryStatus.RecoveryPending)
                               ? Theme.Yellow
                               : Theme.Red;
            if (v.RunningCount > 0)
                status = Theme.Green;
            TextRenderer.DrawText(g, v.StatusText, _itemSubFont,
                                  new Rectangle(textX, card.Top + S(50), card.Width - S(68), S(17)), status,
                                  TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private static string CompactPathMiddle(Graphics g, string fullPath, Font font, int maxWidthPx)
        {
            if (string.IsNullOrEmpty(fullPath))
                return string.Empty;
            if (TextRenderer.MeasureText(g, fullPath, font).Width <= maxWidthPx)
                return fullPath;

            string fileName = Path.GetFileName(fullPath);
            string dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string parent = Path.GetFileName(dir);
            string tail = string.IsNullOrEmpty(parent) ? ("\\" + fileName) : ("\\" + parent + "\\" + fileName);

            int prefixLen = Math.Max(3, fullPath.Length - tail.Length - 4);
            while (prefixLen > 3)
            {
                string candidate = fullPath.Substring(0, prefixLen) + "…" + tail;
                if (TextRenderer.MeasureText(g, candidate, font).Width <= maxWidthPx)
                    return candidate;
                prefixLen -= 4;
            }
            return "…" + tail;
        }

        private void ShowQuickLaunchMenu()
        {
            _quickMenu.Items.Clear();

            if (_allEntries.Count == 0)
            {
                var empty = new ToolStripMenuItem("(В библиотеке пока нет записей)") { Enabled = false };
                _quickMenu.Items.Add(empty);
                var importItem = new ToolStripMenuItem("Найти и импортировать существующие копии…",
                                                       TablerIcons.FolderIcon, (s, e) => PromptAndStartImportScan());
                _quickMenu.Items.Add(importItem);
                _quickMenu.Show(_btnQuickLaunch, new Point(0, _btnQuickLaunch.Height + 2));
                return;
            }

            var byFolder = new Dictionary<string, List<LibraryEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (LibraryEntry e in _allEntries)
            {
                string folder = PathResolver.ToFolderInput(Path.GetDirectoryName(e.Path), AppInfo.AppDirectory);
                List<LibraryEntry> list;
                if (!byFolder.TryGetValue(folder, out list))
                    byFolder[folder] = list = new List<LibraryEntry>();
                list.Add(e);
            }

            foreach (var kv in byFolder)
            {
                var folderItem = new ToolStripMenuItem(kv.Key, TablerIcons.FolderIcon);
                foreach (LibraryEntry entry in kv.Value)
                {
                    LibraryEntry captured = entry;
                    var exeItem =
                        new ToolStripMenuItem(captured.Name + "  (" + captured.Path + ")", TablerIcons.GamepadIcon);
                    exeItem.Click += (s, e) =>
                    {
                        SelectEntryByPath(captured.Path);
                        StartEntryProcess(captured);
                    };
                    folderItem.DropDownItems.Add(exeItem);
                }
                _quickMenu.Items.Add(folderItem);
            }

            _quickMenu.Items.Add(new ToolStripSeparator());
            _quickMenu.Items.Add(
                new ToolStripMenuItem("Найти и импортировать копии…", null, (s, e) => PromptAndStartImportScan()));
            _quickMenu.Show(_btnQuickLaunch, new Point(0, _btnQuickLaunch.Height + 2));
        }

        private void PromptAndStartImportScan()
        {
            if (_isScanning)
            {
                if (_scanCts != null)
                    _scanCts.Cancel();
                return;
            }

            string rootToScan = AppInfo.AppDirectory;
            DialogResult ask = MessageBox.Show(
                this,
                "Просканировать папку лаунчера и вложенные каталоги (глубина до 3 уровней)?\n\n" +
                    AppInfo.AppDirectory + "\n\n" + "Да — сканировать папку лаунчера\n" +
                    "Нет — выбрать другую папку\n" + "Отмена — не сканировать",
                "Поиск существующих копий QuestLauncher", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (ask == DialogResult.Cancel)
                return;
            if (ask == DialogResult.No)
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Выберите папку для поиска существующих копий QuestLauncher:";
                    dlg.SelectedPath = AppInfo.AppDirectory;
                    if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedPath))
                        return;
                    rootToScan = dlg.SelectedPath;
                }
            }

            StartBackgroundScan(rootToScan);
        }

        private void StartBackgroundScan(string rootFolder)
        {
            _isScanning = true;
            _scanCts = new CancellationTokenSource();
            CancellationToken ct = _scanCts.Token;

            _btnImportScan.Text = "Стоп";
            SetStatus("Сканирование папки в фоне: " + rootFolder + "…", false);

            var existingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LibraryEntry e in _allEntries)
                existingSet.Add(PathResolver.Normalize(e.Path));

            Task.Factory
                .StartNew(() =>
                          { return _copyService.ScanForCopies(rootFolder, 3, existingSet, ct, null); },
                          ct)
                .ContinueWith(
                    t =>
                    {
                        if (IsDisposed || !IsHandleCreated)
                            return;
                        BeginInvoke((MethodInvoker)delegate {
                            _isScanning = false;
                            _btnImportScan.Text = "Импорт";
                            if (_scanCts != null)
                            {
                                _scanCts.Dispose();
                                _scanCts = null;
                            }

                            if (t.IsCanceled || ct.IsCancellationRequested)
                            {
                                SetStatus("Сканирование отменено.", false);
                                return;
                            }
                            if (t.IsFaulted)
                            {
                                Exception ex = t.Exception != null ? t.Exception.GetBaseException() : null;
                                Log.Error("Фоновое сканирование", ex);
                                SetStatus("Ошибка сканирования: " + (ex != null ? ex.Message : ""), true);
                                return;
                            }

                            List<ImportCandidate> found = t.Result ?? new List<ImportCandidate>();
                            if (found.Count == 0)
                            {
                                SetStatus("Новых копий QuestLauncher для импорта не найдено.", false);
                                MessageBox.Show(
                                    this,
                                    "В выбранной папке не найдено новых копий QuestLauncher, отсутствующих в библиотеке.",
                                    "Импорт копий", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                return;
                            }

                            using (var dlg = new ImportDialog(found, rootFolder))
                            {
                                if (dlg.ShowDialog(this) == DialogResult.OK)
                                {
                                    int added = _copyService.ImportSelected(found);
                                    ReloadLibraryView();
                                    SetStatus("Импортировано копий в библиотеку: " + added + ".", false);
                                }
                                else
                                {
                                    SetStatus("Импорт отменён.", false);
                                }
                            }
                        });
                    });
        }

        private void CheckForUpdatesAsync()
        {
            if (_isCheckingUpdates)
                return;
            _isCheckingUpdates = true;
            _btnCheckUpdates.Enabled = false;
            _btnCheckUpdates.Text = "Проверка…";
            SetStatus("Проверка последнего релиза на GitHub…", false);

            Task.Factory.StartNew(() => UpdateChecker.CheckLatest())
                .ContinueWith(
                    t =>
                    {
                        if (IsDisposed || !IsHandleCreated)
                            return;
                        BeginInvoke((MethodInvoker)delegate {
                            _isCheckingUpdates = false;
                            _btnCheckUpdates.Enabled = true;
                            _btnCheckUpdates.Text = "Проверить обновления";

                            UpdateCheckResult r =
                                t.IsFaulted
                                    ? new UpdateCheckResult { Ok = false, ErrorMessage = "Ошибка проверки обновлений." }
                                    : t.Result;
                            if (!r.Ok)
                            {
                                SetStatus(r.ErrorMessage, true);
                                DialogResult openDr = MessageBox.Show(
                                    this, r.ErrorMessage + "\n\nОткрыть страницу релизов на GitHub в браузере?",
                                    "Проверка обновлений", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                                if (openDr == DialogResult.Yes)
                                {
                                    try
                                    {
                                        ProcessTools.OpenUrl(AppInfo.ReleasesUrl);
                                    }
                                    catch
                                    {
                                    }
                                }
                                return;
                            }

                            if (r.HasNewerVersion)
                            {
                                SetStatus("Доступна новая версия: " + r.LatestTag, false);
                                DialogResult dr = MessageBox.Show(
                                    this,
                                    "Доступен новый релиз: " + r.LatestName + " (" + r.LatestTag + ")\n" +
                                        "Установленная версия: v" + r.CurrentVersion + "\n\n" +
                                        "Открыть страницу релиза на GitHub для скачивания?",
                                    "Доступно обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                                if (dr == DialogResult.Yes)
                                {
                                    try
                                    {
                                        ProcessTools.OpenUrl(r.HtmlUrl);
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            else
                            {
                                SetStatus("У вас актуальная версия (v" + r.CurrentVersion +
                                              ", на GitHub: " + r.LatestTag + ").",
                                          false);
                                MessageBox.Show(
                                    this,
                                    "У вас установлена актуальная версия Quest Launcher (v" + r.CurrentVersion +
                                        ").\nПоследний опубликованный релиз на GitHub: " + r.LatestTag + ".",
                                    "Обновления не требуются", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        });
                    });
        }

        private void OpenLogFile()
        {
            string logPath = DataPaths.LogPath;
            if (!File.Exists(logPath))
                Log.Info("Журнал открыт пользователем.");
            try
            {
                ProcessTools.OpenFolder(logPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Путь к журналу:\n" + logPath + "\n\n" + ex.Message, "Журнал",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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

        private void StartFadeIn()
        {
            StopFadeTimer();

            var timer = new Timer { Interval = 15 };
            _fadeTimer = timer;

            timer.Tick += (s, e) =>
            {
                if (Opacity < 1.0)
                {
                    Opacity = Math.Min(1.0, Opacity + 0.16);
                }
                else
                {
                    StopFadeTimer();
                }
            };
            timer.Start();
        }

        private void SmoothClose()
        {
            if (_isClosing)
                return;
            _isClosing = true;
            SaveConfig();
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
                    _wasMinimized = true;
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
            if (WindowState == FormWindowState.Minimized)
            {
                _wasMinimized = true;
            }
            else if (WindowState == FormWindowState.Normal && _wasMinimized && !_isClosing)
            {
                _wasMinimized = false;
                Opacity = 0.2;
                StartFadeIn();
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

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                LaunchFromInputs();
            }
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

        private void OnFormPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var accent = new SolidBrush(Theme.Blurple)) g.FillRectangle(accent, S(8), 0, Width - S(16), S(3));

            using (var panelPath = WinApi.CreateRoundRect(new Rectangle(S(16), S(76), S(336), S(404)), S(12))) using (
                var fill = new SolidBrush(Theme.BgCard)) g.FillPath(fill, panelPath);
            using (var panelPath = WinApi.CreateRoundRect(new Rectangle(S(368), S(76), S(516), S(404)), S(12))) using (
                var fill = new SolidBrush(Theme.BgCard)) g.FillPath(fill, panelPath);
            TextRenderer.DrawText(g, "Quest Launcher", _boldFont, new Rectangle(S(24), S(13), S(250), S(23)),
                                  Theme.TextNormal, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "v" + AppInfo.Version + "  /  by vaelit", _smallFont,
                                  new Rectangle(S(24), S(38), S(280), S(18)), Theme.TextMuted, TextFormatFlags.Left);
            TextRenderer.DrawText(g, "Создайте файл для нового запуска", _smallFont,
                                  new Rectangle(S(36), S(122), S(296), S(18)), Theme.TextMuted, TextFormatFlags.Left);
            LucideIcons.Draw(g, "minus", Rectangle.Inflate(_minRect, -S(7), -S(5)),
                             _isMinHovered ? Color.White : Theme.TextMuted);
            LucideIcons.Draw(g, "x", Rectangle.Inflate(_closeRect, -S(7), -S(5)),
                             _isCloseHovered ? Theme.Red : Theme.TextMuted);

            var borderRect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = WinApi.CreateRoundRect(borderRect, S(7))) using (var borderPen =
                                                                                   new Pen(Theme.Border, 1f))
            {
                g.DrawPath(borderPen, path);
            }
        }

        private void OnLauncherClosing(object sender, FormClosingEventArgs e)
        {
            if (_isUpdatingAll)
            {
                e.Cancel = true;
                return;
            }
            if (_scanCts != null)
            {
                try
                {
                    _scanCts.Cancel();
                }
                catch
                {
                }
            }
            SaveConfig();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopFadeTimer();
                if (_processPollTimer != null)
                {
                    _processPollTimer.Stop();
                    _processPollTimer.Dispose();
                    _processPollTimer = null;
                }
                if (_scanCts != null)
                {
                    try
                    {
                        _scanCts.Cancel();
                        _scanCts.Dispose();
                    }
                    catch
                    {
                    }
                    _scanCts = null;
                }
                if (_appMenu != null)
                    _appMenu.Dispose();
                if (_quickMenu != null)
                    _quickMenu.Dispose();
                if (_entryContextMenu != null)
                    _entryContextMenu.Dispose();
                if (_toolTip != null)
                    _toolTip.Dispose();

                if (_titleFont != null)
                    _titleFont.Dispose();
                if (_sectionFont != null)
                    _sectionFont.Dispose();
                if (_boldFont != null)
                    _boldFont.Dispose();
                if (_labelFont != null)
                    _labelFont.Dispose();
                if (_smallFont != null)
                    _smallFont.Dispose();
                if (_itemTitleFont != null)
                    _itemTitleFont.Dispose();
                if (_itemSubFont != null)
                    _itemSubFont.Dispose();
                if (_captionFont != null)
                    _captionFont.Dispose();
                if (_centerFormat != null)
                    _centerFormat.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
