using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class SettingsSelection
    {
        public UsageDisplayMode DisplayMode { get; set; }
        public PetAppearance PetAppearance { get; set; }
        public PetUsageDisplayMode PetUsageDisplayMode { get; set; }
        public int PetScalePercent { get; set; }
        public bool PetAlwaysOnTop { get; set; }
        public bool StartupEnabled { get; set; }
        public UsageThemeMode ThemeMode { get; set; }
    }

    internal sealed class SettingsForm : DpiAwareForm
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;
        private const int WsExComposited = 0x02000000;

        private readonly ModernSelectControl themeInput;
        private readonly ModernSelectControl displayModeInput;
        private readonly ModernSelectControl appearanceInput;
        private readonly ModernSelectControl usageModeInput;
        private readonly ModernNumberControl scaleInput;
        private readonly CheckBox petAlwaysOnTopInput;
        private readonly CheckBox startupInput;
        private readonly Panel sidebarPanel;
        private readonly Button settingsNavButton;
        private readonly Button diagnosticsNavButton;
        private readonly Panel settingsPage;
        private readonly Panel diagnosticsPage;
        private readonly TextBox diagnosticsText;
        private readonly Label diagnosticsHeadlineLabel;
        private readonly Label diagnosticsStatusLabel;
        private readonly Label diagnosticsSubtitleLabel;
        private readonly Label connectionValueLabel;
        private readonly Label connectionDetailLabel;
        private readonly Label priceValueLabel;
        private readonly Label priceDetailLabel;
        private readonly Label displayValueLabel;
        private readonly Label displayDetailLabel;
        private readonly SurfacePanel diagnosticsStatusCard;
        private readonly SurfacePanel diagnosticsOverviewCard;
        private readonly SurfacePanel diagnosticsReportCard;
        private readonly Button diagnosticsDetailsButton;
        private readonly Label pageHeaderTitle;
        private readonly Label autoSaveStatusLabel;
        private readonly Button testConnectionButton;
        private readonly Func<DiagnosticsSnapshot> diagnosticsProvider;
        private bool diagnosticsPageSelected;
        private bool suppressSettingsChanged;
        private bool displayPrepared;
        private bool diagnosticsDetailsExpanded;
        private bool systemRoundedCorners;
        private DiagnosticsSnapshot lastDiagnosticsSnapshot;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public event EventHandler ConnectionTestRequested;
        public event EventHandler SettingsChanged;

        public SettingsForm(
            SettingsSelection initial,
            Func<DiagnosticsSnapshot> reportProvider)
        {
            SettingsSelection safe = initial ?? new SettingsSelection
            {
                DisplayMode = UsageDisplayMode.Pet,
                PetAppearance = PetAppearance.WhaleMaid,
                PetUsageDisplayMode = PetUsageDisplayMode.Used,
                PetScalePercent = 100,
                PetAlwaysOnTop = false
            };
            diagnosticsProvider = reportProvider;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
            Text = "UsagePeek 设置与诊断";
            ClientSize = new Size(560, 530);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiColors.Background;
            ForeColor = UiColors.Text;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;

            sidebarPanel = new Panel();
            sidebarPanel.BackColor = UiColors.Sidebar;
            sidebarPanel.Location = new Point(0, 0);
            sidebarPanel.Size = new Size(154, 530);

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(16, 18);
            brandMark.Size = new Size(34, 34);
            Label titleLabel = CreateLabel("UsagePeek",
                new Point(58, 16), new Size(88, 22), 10.5f,
                FontStyle.Bold, ForeColor);
            Label subtitleLabel = CreateLabel("设置与诊断",
                new Point(58, 37), new Size(88, 16), 7.6f,
                FontStyle.Regular, UiColors.Muted);

            settingsNavButton = CreateNavButton("常规设置",
                new Point(12, 74), Color.FromArgb(0, 122, 255), "◐");
            diagnosticsNavButton = CreateNavButton("诊断信息",
                new Point(12, 114), Color.FromArgb(40, 190, 82), "✓");
            settingsNavButton.Click += delegate { ShowPage(false); };
            diagnosticsNavButton.Click += delegate { ShowPage(true); };

            sidebarPanel.Controls.Add(brandMark);
            sidebarPanel.Controls.Add(titleLabel);
            sidebarPanel.Controls.Add(subtitleLabel);
            sidebarPanel.Controls.Add(settingsNavButton);
            sidebarPanel.Controls.Add(diagnosticsNavButton);

            Panel divider = new Panel();
            divider.BackColor = UiColors.Border;
            divider.Location = new Point(154, 0);
            divider.Size = new Size(1, 530);

            GlyphButtonControl closeButton =
                new GlyphButtonControl("×", false);
            closeButton.VisualStyle = GlyphButtonStyle.Ghost;
            closeButton.Size = new Size(28, 28);
            closeButton.Location = new Point(518, 16);
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.ForeColor = UiColors.Muted;
            closeButton.Click += delegate { Dismiss(); };

            settingsPage = new Panel();
            settingsPage.BackColor = Color.Transparent;
            settingsPage.Location = new Point(170, 46);
            settingsPage.Size = new Size(374, 468);

            Label settingsTitle = CreateLabel("常规设置",
                new Point(172, 16), new Size(160, 28), 13.5f,
                FontStyle.Bold, ForeColor);
            autoSaveStatusLabel = CreateLabel("●  更改会自动保存",
                new Point(352, 20), new Size(156, 22), 7.5f,
                FontStyle.Regular, UiColors.Success);
            autoSaveStatusLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(settingsTitle);
            Controls.Add(autoSaveStatusLabel);

            // Group 1: Appearance & Display
            settingsPage.Controls.Add(CreateSectionHeader("外观与显示", new Point(10, 6)));
            SurfacePanel appearanceGroup = CreateCard(new Point(0, 26), new Size(374, 96), true);
            themeInput = CreateComboBox();
            themeInput.Items.AddRange(new object[] { "跟随系统", "浅色模式", "深色模式" });
            themeInput.SelectedIndex = (int)safe.ThemeMode;
            AddGroupedSettingRow(appearanceGroup, 2, "主题",
                "浅色、深色或跟随 Windows", themeInput, false);

            displayModeInput = CreateComboBox();
            displayModeInput.Items.AddRange(new object[]
            {
                "经典面板",
                "桌宠模式"
            });
            displayModeInput.SelectedIndex = safe.DisplayMode ==
                UsageDisplayMode.Pet ? 1 : 0;
            AddGroupedSettingRow(appearanceGroup, 49, "显示模式",
                "桌宠点击后打开用量详情", displayModeInput, true);
            settingsPage.Controls.Add(appearanceGroup);

            // Group 2: Desktop Pet
            settingsPage.Controls.Add(CreateSectionHeader("桌面宠物", new Point(10, 132)));
            SurfacePanel petGroup = CreateCard(new Point(0, 152), new Size(374, 188), true);
            appearanceInput = CreateComboBox();
            appearanceInput.Items.AddRange(new object[]
            {
                "鲸鱼娘趴趴",
                "菲比啾比",
                "ChatGPT 娘",
                "Claude 娘",
                "奶蛙",
                "Clawd"
            });
            appearanceInput.SelectedIndex = Math.Max(0,
                Math.Min(appearanceInput.Items.Count - 1, (int)safe.PetAppearance));
            AddGroupedSettingRow(petGroup, 2, "桌宠外形",
                "六种外形，支持散步、摸头", appearanceInput, false);

            usageModeInput = CreateComboBox();
            usageModeInput.Items.AddRange(new object[]
            {
                "已用百分比",
                "剩余百分比"
            });
            usageModeInput.SelectedIndex = safe.PetUsageDisplayMode ==
                PetUsageDisplayMode.Remaining ? 1 : 0;
            AddGroupedSettingRow(petGroup, 49, "气泡数字",
                "预警按实际已用比例判断", usageModeInput, true);

            scaleInput = new ModernNumberControl();
            scaleInput.Minimum = DisplayModePreference.MinimumPetScalePercent;
            scaleInput.Maximum = DisplayModePreference.MaximumPetScalePercent;
            scaleInput.Increment = 5;
            scaleInput.Value = Math.Max((int)scaleInput.Minimum,
                Math.Min((int)scaleInput.Maximum, safe.PetScalePercent));
            scaleInput.TextAlign = HorizontalAlignment.Center;
            scaleInput.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            scaleInput.BackColor = UiColors.Surface;
            scaleInput.ForeColor = ForeColor;
            AddGroupedSettingRow(petGroup, 96, "桌宠大小",
                "50%–200%，随显示器缩放适配", scaleInput, true);

            AddInsetDivider(petGroup, 143);
            petAlwaysOnTopInput = new SwitchCheckBox();
            petAlwaysOnTopInput.AutoSize = false;
            petAlwaysOnTopInput.Location = new Point(14, 150);
            petAlwaysOnTopInput.Size = new Size(346, 30);
            petAlwaysOnTopInput.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
            petAlwaysOnTopInput.ForeColor = ForeColor;
            petAlwaysOnTopInput.BackColor = UiColors.Surface;
            petAlwaysOnTopInput.Text = "桌宠保持置顶";
            petAlwaysOnTopInput.Checked = safe.PetAlwaysOnTop;
            petAlwaysOnTopInput.UseVisualStyleBackColor = false;
            petGroup.Controls.Add(petAlwaysOnTopInput);
            settingsPage.Controls.Add(petGroup);

            // Group 3: System
            settingsPage.Controls.Add(CreateSectionHeader("系统", new Point(10, 350)));
            SurfacePanel systemGroup = CreateCard(new Point(0, 370), new Size(374, 46), true);
            startupInput = new SwitchCheckBox();
            startupInput.AutoSize = false;
            startupInput.Location = new Point(14, 8);
            startupInput.Size = new Size(346, 30);
            startupInput.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
            startupInput.ForeColor = ForeColor;
            startupInput.BackColor = UiColors.Surface;
            startupInput.Text = "登录 Windows 后自动启动 UsagePeek";
            startupInput.Checked = safe.StartupEnabled;
            startupInput.UseVisualStyleBackColor = false;
            systemGroup.Controls.Add(startupInput);
            settingsPage.Controls.Add(systemGroup);

            // Diagnostics Page
            diagnosticsPage = new Panel();
            diagnosticsPage.BackColor = Color.Transparent;
            diagnosticsPage.Location = new Point(170, 46);
            diagnosticsPage.Size = new Size(374, 468);
            Label diagnosticsHint = CreateLabel(
                "查看连接、价格覆盖和显示环境。",
                new Point(2, 0), new Size(368, 20), 7.8f,
                FontStyle.Regular, UiColors.Muted);
            diagnosticsPage.Controls.Add(diagnosticsHint);

            diagnosticsStatusCard = CreateCard(new Point(0, 26), new Size(374, 90), true);
            diagnosticsStatusCard.AccentStripeColor = UiColors.Accent;
            diagnosticsHeadlineLabel = CreateLabel("准备检查运行状态",
                new Point(18, 10), new Size(340, 28), 11.2f,
                FontStyle.Bold, UiColors.Text);
            diagnosticsSubtitleLabel = CreateLabel("打开诊断后读取本机状态。",
                new Point(18, 38), new Size(340, 22), 8f,
                FontStyle.Regular, UiColors.Secondary);
            diagnosticsStatusLabel = CreateLabel("等待检测",
                new Point(18, 63), new Size(340, 18), 7.4f,
                FontStyle.Regular, UiColors.Muted);
            diagnosticsStatusCard.Controls.Add(diagnosticsHeadlineLabel);
            diagnosticsStatusCard.Controls.Add(diagnosticsSubtitleLabel);
            diagnosticsStatusCard.Controls.Add(diagnosticsStatusLabel);
            diagnosticsPage.Controls.Add(diagnosticsStatusCard);

            diagnosticsOverviewCard = CreateCard(new Point(0, 126), new Size(374, 174), true);
            AddDiagnosticsRow(diagnosticsOverviewCard, 0, "Codex 连接",
                out connectionValueLabel, out connectionDetailLabel);
            AddDiagnosticsRow(diagnosticsOverviewCard, 58, "价格覆盖",
                out priceValueLabel, out priceDetailLabel);
            AddDiagnosticsRow(diagnosticsOverviewCard, 116, "显示环境",
                out displayValueLabel, out displayDetailLabel);
            diagnosticsPage.Controls.Add(diagnosticsOverviewCard);

            diagnosticsText = new TextBox();
            diagnosticsText.Location = new Point(14, 12);
            diagnosticsText.Size = new Size(346, 150);
            diagnosticsText.Multiline = true;
            diagnosticsText.ReadOnly = true;
            diagnosticsText.ScrollBars = ScrollBars.Vertical;
            diagnosticsText.WordWrap = true;
            diagnosticsText.BorderStyle = BorderStyle.None;
            diagnosticsText.BackColor = UiColors.Surface;
            diagnosticsText.ForeColor = UiColors.Secondary;
            diagnosticsText.Font = new Font("Consolas", 8.4f, FontStyle.Regular);
            diagnosticsReportCard = CreateCard(new Point(0, 126), new Size(374, 174), true);
            diagnosticsReportCard.Controls.Add(diagnosticsText);
            diagnosticsReportCard.Visible = false;
            diagnosticsPage.Controls.Add(diagnosticsReportCard);

            diagnosticsDetailsButton = CreateActionButton("查看技术详情  ›", false);
            ((RoundedButton)diagnosticsDetailsButton).ShowBorder = false;
            diagnosticsDetailsButton.Location = new Point(0, 308);
            diagnosticsDetailsButton.Size = new Size(374, 30);
            diagnosticsDetailsButton.TextAlign = ContentAlignment.MiddleLeft;
            diagnosticsDetailsButton.Padding = new Padding(10, 0, 0, 0);
            diagnosticsDetailsButton.BackColor = UiColors.Background;
            diagnosticsDetailsButton.ForeColor = UiColors.Accent;
            diagnosticsDetailsButton.Click += delegate
            {
                ShowDiagnosticsDetails(!diagnosticsDetailsExpanded);
            };
            diagnosticsPage.Controls.Add(diagnosticsDetailsButton);

            Label privacyLabel = CreateLabel(
                "复制报告已隐藏私人路径和登录信息。",
                new Point(4, 388), new Size(368, 20), 7.4f,
                FontStyle.Regular, UiColors.Muted);
            diagnosticsPage.Controls.Add(privacyLabel);

            testConnectionButton = CreateActionButton("测试连接", true);
            testConnectionButton.Location = new Point(0, 348);
            testConnectionButton.Size = new Size(118, 34);
            testConnectionButton.Click += delegate
            {
                EventHandler handler = ConnectionTestRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Button refreshDiagnosticsButton = CreateActionButton("重新检测", false);
            refreshDiagnosticsButton.Location = new Point(126, 348);
            refreshDiagnosticsButton.Size = new Size(118, 34);
            refreshDiagnosticsButton.Click += delegate { RefreshDiagnostics(); };
            Button copyButton = CreateActionButton("复制报告", false);
            copyButton.Location = new Point(252, 348);
            copyButton.Size = new Size(122, 34);
            copyButton.Click += delegate { CopyDiagnostics(); };
            diagnosticsPage.Controls.Add(testConnectionButton);
            diagnosticsPage.Controls.Add(refreshDiagnosticsButton);
            diagnosticsPage.Controls.Add(copyButton);

            pageHeaderTitle = settingsTitle;

            Controls.Add(sidebarPanel);
            Controls.Add(divider);
            Controls.Add(closeButton);
            Controls.Add(settingsPage);
            Controls.Add(diagnosticsPage);

            MouseDown += DragWindow;
            sidebarPanel.MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;
            subtitleLabel.MouseDown += DragWindow;
            settingsPage.MouseDown += DragWindow;
            diagnosticsPage.MouseDown += DragWindow;
            Resize += delegate { UpdateRoundedRegion(); };
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape)
                {
                    args.SuppressKeyPress = true;
                    Dismiss();
                }
            };
            displayModeInput.SelectedIndexChanged += delegate { RaiseSettingsChanged(); };
            themeInput.SelectedIndexChanged += delegate { RaiseSettingsChanged(); };
            appearanceInput.SelectedIndexChanged += delegate { RaiseSettingsChanged(); };
            usageModeInput.SelectedIndexChanged += delegate { RaiseSettingsChanged(); };
            scaleInput.ValueChanged += delegate { RaiseSettingsChanged(); };
            petAlwaysOnTopInput.CheckedChanged += delegate { RaiseSettingsChanged(); };
            startupInput.CheckedChanged += delegate { RaiseSettingsChanged(); };
            ShowPage(false);
            UpdateRoundedRegion();
            InitializeDpiLayout();
        }

        public SettingsSelection Selection
        {
            get
            {
                return new SettingsSelection
                {
                    DisplayMode = displayModeInput.SelectedIndex == 1
                        ? UsageDisplayMode.Pet
                        : UsageDisplayMode.Classic,
                    PetAppearance = (PetAppearance)Math.Max(0,
                        appearanceInput.SelectedIndex),
                    PetUsageDisplayMode = usageModeInput.SelectedIndex == 1
                        ? PetUsageDisplayMode.Remaining
                        : PetUsageDisplayMode.Used,
                    PetScalePercent = Decimal.ToInt32(scaleInput.Value),
                    PetAlwaysOnTop = petAlwaysOnTopInput.Checked,
                    StartupEnabled = startupInput.Checked,
                    ThemeMode = (UsageThemeMode)Math.Max(0, themeInput.SelectedIndex)
                };
            }
        }

        internal string DiagnosticsTextForTesting
        {
            get { return diagnosticsText.Text; }
        }

        internal bool DiagnosticsPageVisibleForTesting
        {
            get { return diagnosticsPageSelected; }
        }

        internal bool DiagnosticsDetailsExpandedForTesting
        {
            get { return diagnosticsDetailsExpanded; }
        }

        internal string DiagnosticsOverviewForTesting
        {
            get
            {
                return diagnosticsHeadlineLabel.Text + "\n" + diagnosticsSubtitleLabel.Text +
                    "\n" + connectionValueLabel.Text + "\n" + connectionDetailLabel.Text +
                    "\n" + priceValueLabel.Text + "\n" + priceDetailLabel.Text +
                    "\n" + displayValueLabel.Text;
            }
        }

        internal void ShowDiagnosticsDetailsForTesting(bool expanded)
        {
            ShowDiagnosticsDetails(expanded);
        }

        internal bool VisibleControlsReadyForTesting
        {
            get
            {
                return IsHandleCreated &&
                    settingsPage.IsHandleCreated &&
                    themeInput.IsHandleCreated &&
                    displayModeInput.IsHandleCreated &&
                    appearanceInput.IsHandleCreated &&
                    usageModeInput.IsHandleCreated &&
                    scaleInput.IsHandleCreated &&
                    petAlwaysOnTopInput.IsHandleCreated &&
                    startupInput.IsHandleCreated;
            }
        }

        internal bool UsesCompositedPaintingForTesting
        {
            get { return (CreateParams.ExStyle & WsExComposited) != 0; }
        }

        internal void ShowDiagnosticsForTesting()
        {
            ShowPage(true);
        }

        public void ShowGeneralSettings()
        {
            ShowPage(false);
        }

        internal void SelectDisplayModeForTesting(UsageDisplayMode mode)
        {
            displayModeInput.SelectedIndex = mode == UsageDisplayMode.Pet ? 1 : 0;
        }

        internal void SelectThemeForTesting(UsageThemeMode mode)
        {
            themeInput.SelectedIndex = (int)mode;
        }

        internal void SetPetAlwaysOnTopForTesting(bool enabled)
        {
            petAlwaysOnTopInput.Checked = enabled;
        }

        public void SetPetAlwaysOnTopState(bool enabled)
        {
            suppressSettingsChanged = true;
            try { petAlwaysOnTopInput.Checked = enabled; }
            finally { suppressSettingsChanged = false; }
        }

        internal void DismissForTesting()
        {
            Dismiss();
        }

        public void SetSelection(SettingsSelection value)
        {
            if (value == null) return;

            suppressSettingsChanged = true;
            try
            {
                themeInput.SelectedIndex = (int)value.ThemeMode;
                displayModeInput.SelectedIndex = value.DisplayMode == UsageDisplayMode.Pet ? 1 : 0;
                appearanceInput.SelectedIndex = Math.Max(0,
                    Math.Min(appearanceInput.Items.Count - 1, (int)value.PetAppearance));
                usageModeInput.SelectedIndex = value.PetUsageDisplayMode == PetUsageDisplayMode.Remaining ? 1 : 0;
                scaleInput.Value = Math.Max((int)scaleInput.Minimum,
                    Math.Min((int)scaleInput.Maximum, value.PetScalePercent));
                petAlwaysOnTopInput.Checked = value.PetAlwaysOnTop;
                startupInput.Checked = value.StartupEnabled;
            }
            finally { suppressSettingsChanged = false; }
        }

        public void SetApplyResult(bool success)
        {
            autoSaveStatusLabel.Text = success ? "●  已自动保存" : "●  保存失败，已恢复";
            autoSaveStatusLabel.ForeColor = success ? UiColors.Success : UiColors.Danger;
        }

        public void PlaceNearCursor()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            PrepareForScreen(screen);
            Rectangle area = screen.WorkingArea;
            int left = Width >= area.Width ? area.Left : area.Left + (area.Width - Width) / 2;
            int top = Height >= area.Height ? area.Top : area.Top + (area.Height - Height) / 2;
            Location = new Point(
                Math.Max(area.Left, Math.Min(left, area.Right - Width)),
                Math.Max(area.Top, Math.Min(top, area.Bottom - Height)));
        }

        public void PrepareForDisplay()
        {
            if (displayPrepared && VisibleControlsReadyForTesting) return;
            CreateChildHandles(this);
            PerformLayout();
            displayPrepared = true;
        }

        public void RefreshDiagnostics()
        {
            diagnosticsStatusLabel.Text = "正在检测…";
            diagnosticsStatusLabel.ForeColor = UiColors.Warning;
            try
            {
                DiagnosticsSnapshot snapshot = diagnosticsProvider == null ? null : diagnosticsProvider();
                diagnosticsText.Text = snapshot == null
                    ? "暂时无法生成诊断信息。"
                    : snapshot.BuildReport();
                lastDiagnosticsSnapshot = snapshot;
                diagnosticsStatusLabel.Text = snapshot == null
                    ? "检测不可用"
                    : "检查于 " + snapshot.GeneratedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                diagnosticsStatusLabel.ForeColor = snapshot == null ? UiColors.Danger : UiColors.Muted;
                UpdateDiagnosticsOverview(snapshot);
            }
            catch
            {
                diagnosticsText.Text = "诊断生成失败，请稍后重新检测。";
                diagnosticsStatusLabel.Text = "检测失败";
                diagnosticsStatusLabel.ForeColor = UiColors.Danger;
                lastDiagnosticsSnapshot = null;
                UpdateDiagnosticsOverview(null);
            }
        }

        public void RefreshDiagnosticsIfVisible()
        {
            if (Visible && diagnosticsPageSelected) RefreshDiagnostics();
        }

        public void SetConnectionTestBusy(bool busy)
        {
            testConnectionButton.Enabled = !busy;
            testConnectionButton.Text = busy ? "测试中…" : "测试连接";
            if (busy)
            {
                diagnosticsHeadlineLabel.Text = "正在测试连接";
                diagnosticsHeadlineLabel.ForeColor = UiColors.Text;
                diagnosticsSubtitleLabel.Text = "正在读取 Codex 的最新用量。";
            }
            else UpdateDiagnosticsOverview(lastDiagnosticsSnapshot);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            systemRoundedCorners = UiTheme.TryApplyRoundedCorners(this);
            UpdateRoundedRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, logicalWidth - 1, logicalHeight - 1),
                systemRoundedCorners ? 8 : 18))
            using (Pen border = new Pen(UiColors.Border))
            {
                e.Graphics.DrawPath(border, path);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExComposited;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Refresh();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Dismiss();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnDpiScaleChanged()
        {
            base.OnDpiScaleChanged();
            UpdateRoundedRegion();
        }

        private void ShowPage(bool diagnostics)
        {
            if (diagnostics && !diagnosticsPageSelected) ShowDiagnosticsDetails(false);
            diagnosticsPageSelected = diagnostics;
            diagnosticsPage.Visible = diagnostics;
            settingsPage.Visible = !diagnostics;
            pageHeaderTitle.Text = diagnostics ? "诊断信息" : "常规设置";
            autoSaveStatusLabel.Visible = !diagnostics;
            StyleNavButton(settingsNavButton, !diagnostics);
            StyleNavButton(diagnosticsNavButton, diagnostics);
            if (diagnostics) RefreshDiagnostics();
        }

        private void RaiseSettingsChanged()
        {
            if (suppressSettingsChanged) return;
            autoSaveStatusLabel.Text = "●  正在应用…";
            autoSaveStatusLabel.ForeColor = UiColors.Warning;
            EventHandler handler = SettingsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void CopyDiagnostics()
        {
            if (string.IsNullOrWhiteSpace(diagnosticsText.Text)) RefreshDiagnostics();

            try
            {
                Clipboard.SetText(diagnosticsText.Text);
                diagnosticsStatusLabel.Text = "已复制到剪贴板";
                diagnosticsStatusLabel.ForeColor = UiColors.Success;
            }
            catch (Exception ex)
            {
                diagnosticsStatusLabel.Text = "复制失败";
                diagnosticsStatusLabel.ForeColor = UiColors.Danger;
                MessageBox.Show(this, "无法复制诊断信息：" + ex.Message,
                    "UsagePeek", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowDiagnosticsDetails(bool expanded)
        {
            diagnosticsDetailsExpanded = expanded;
            diagnosticsOverviewCard.Visible = !expanded;
            diagnosticsReportCard.Visible = expanded;
            diagnosticsDetailsButton.Text = expanded ? "‹  返回状态概览" : "查看技术详情  ›";
        }

        private void UpdateDiagnosticsOverview(DiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                diagnosticsStatusCard.AccentStripeColor = UiColors.Danger;
                diagnosticsStatusCard.Invalidate();
                diagnosticsHeadlineLabel.Text = "暂时无法检查运行状态";
                diagnosticsHeadlineLabel.ForeColor = UiColors.Danger;
                diagnosticsSubtitleLabel.Text = "可重新检测，或测试 Codex 连接。";
                connectionValueLabel.Text = priceValueLabel.Text = displayValueLabel.Text = "未读取";
                connectionValueLabel.ForeColor = priceValueLabel.ForeColor = UiColors.Muted;
                displayValueLabel.ForeColor = UiColors.Muted;
                connectionDetailLabel.Text = "重新检测后显示本地组件和连接状态。";
                priceDetailLabel.Text = "读取本机记录后显示价格覆盖情况。";
                displayDetailLabel.Text = "重新检测后显示屏幕与缩放信息。";
                return;
            }

            bool live = string.Equals(snapshot.UsageStatus, "实时数据", StringComparison.Ordinal);
            bool cached = string.Equals(snapshot.UsageStatus, "缓存数据", StringComparison.Ordinal);
            diagnosticsStatusCard.AccentStripeColor = live ? UiColors.Success :
                cached ? UiColors.Warning : UiColors.Danger;
            diagnosticsStatusCard.Invalidate();
            diagnosticsHeadlineLabel.Text = live ? "实时用量已同步" :
                cached ? "当前使用最近记录" : "连接需要检查";
            diagnosticsHeadlineLabel.ForeColor = live ? UiColors.Text :
                cached ? UiColors.Warning : UiColors.Danger;
            diagnosticsSubtitleLabel.Text = live ? "连接已就绪，每 5 分钟自动刷新。" :
                cached ? "实时刷新暂时不可用，当前显示缓存数据。" : snapshot.ConnectionGuidance;

            connectionValueLabel.Text = live ? "已连接" : cached ? "缓存数据" : "未连接";
            connectionValueLabel.ForeColor = live ? UiColors.Success :
                cached ? UiColors.Warning : UiColors.Danger;
            string source = snapshot.CodexSourceDisplay;
            connectionDetailLabel.Text = snapshot.CodexCandidateCount > 0
                ? source + " · 找到 " + snapshot.CodexCandidateCount + " 个组件"
                : "尚未找到本地组件，可在技术详情中检查路径。";

            if (snapshot.HasLocalUsage == false || !snapshot.HasCompletePriceCoverage.HasValue)
            {
                priceValueLabel.Text = "尚无统计";
                priceValueLabel.ForeColor = UiColors.Muted;
                priceDetailLabel.Text = "产生本机用量记录后，显示价格覆盖情况。";
            }
            else
            {
                bool complete = snapshot.HasCompletePriceCoverage.Value;
                priceValueLabel.Text = complete ? "完整覆盖" : snapshot.PriceCoveragePercent.HasValue
                    ? snapshot.PriceCoveragePercent.Value >= 100 ? "接近完整" :
                        "已覆盖 " + Math.Max(0, snapshot.PriceCoveragePercent.Value) + "%"
                    : "部分待补价";
                priceValueLabel.ForeColor = complete ? UiColors.Success : UiColors.Warning;
                string scanned = snapshot.LocalFilesScanned.HasValue
                    ? "已扫描 " + snapshot.LocalFilesScanned.Value + " 份记录" : "本机累计 API 等价估值";
                priceDetailLabel.Text = snapshot.UnpricedModelCount.GetValueOrDefault() > 0
                    ? scanned + " · " + snapshot.UnpricedModelCount.Value + " 个模型待补价"
                    : scanned + " · " + (complete ? "模型均可估算" : "部分用量价格尚不完整");
            }

            displayValueLabel.Text = snapshot.CurrentDpi > 0 && snapshot.DisplayCount > 0
                ? snapshot.DisplayCount + " 块屏幕 · " +
                    (int)Math.Round(snapshot.CurrentDpi * 100d / 96d) + "% 缩放"
                : "尚未读取";
            displayValueLabel.ForeColor = snapshot.CurrentDpi > 0 && snapshot.DisplayCount > 0
                ? UiColors.Text : UiColors.Muted;
            string appearance = string.Equals(snapshot.DisplayMode, "桌宠模式", StringComparison.Ordinal)
                ? snapshot.PetAppearance + " " + snapshot.PetScalePercent + "%" : "经典面板";
            string theme = UiTheme.Mode == UsageThemeMode.System ? "跟随系统" :
                UiTheme.Mode == UsageThemeMode.Dark ? "深色模式" : "浅色模式";
            displayDetailLabel.Text = appearance + " · " + theme;
        }

        private static Label CreateSectionHeader(string text, Point location)
        {
            return CreateLabel(text, location, new Size(200, 16), 7.6f, FontStyle.Regular, UiColors.Muted);
        }

        private static void AddInsetDivider(Control parent, int top)
        {
            Panel divider = new Panel();
            divider.Bounds = new Rectangle(14, top, Math.Max(1, parent.Width - 28), 1);
            divider.BackColor = UiColors.Border;
            parent.Controls.Add(divider);
        }

        private static void AddDiagnosticsRow(Control parent, int top, string title,
            out Label valueLabel, out Label detailLabel)
        {
            Label titleLabel = CreateLabel(title,
                new Point(16, top + 7), new Size(146, 24), 8.5f,
                FontStyle.Regular, UiColors.Text);
            valueLabel = CreateLabel("等待检测",
                new Point(168, top + 7), new Size(190, 24), 8.5f,
                FontStyle.Bold, UiColors.Muted);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            detailLabel = CreateLabel("读取后显示检查结果。",
                new Point(16, top + 31), new Size(342, 20), 7.5f,
                FontStyle.Regular, UiColors.Muted);
            parent.Controls.Add(titleLabel);
            parent.Controls.Add(valueLabel);
            parent.Controls.Add(detailLabel);
            if (top > 0) AddInsetDivider(parent, top);
        }

        private static void AddGroupedSettingRow(
            Control parent, int top, string title, string hint,
            Control input, bool withDivider)
        {
            if (withDivider) AddInsetDivider(parent, top - 1);
            Label titleLabel = CreateLabel(title,
                new Point(14, top + 4), new Size(192, 20), 8.3f,
                FontStyle.Regular, UiColors.Text);
            Label hintLabel = CreateLabel(hint,
                new Point(14, top + 24), new Size(196, 17), 7.2f,
                FontStyle.Regular, UiColors.Muted);
            input.Location = new Point(214, top + 7);
            input.Size = new Size(146, 30);
            input.AccessibleName = title;
            input.AccessibleDescription = hint;
            parent.Controls.Add(titleLabel);
            parent.Controls.Add(hintLabel);
            parent.Controls.Add(input);
        }

        private static SurfacePanel CreateCard(Point location, Size size, bool softCard = false)
        {
            SurfacePanel panel = new SurfacePanel();
            panel.Location = location;
            panel.Size = size;
            panel.ShowSoftCard = softCard;
            panel.CardRadius = 13;
            if (softCard) panel.ShowTopRule = false;
            return panel;
        }

        private static ModernSelectControl CreateComboBox()
        {
            ModernSelectControl input = new ModernSelectControl();
            input.BackColor = UiColors.Surface;
            input.ForeColor = UiColors.Text;
            input.Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular);
            return input;
        }

        private static Button CreateNavButton(string text, Point location, Color badgeColor, string glyph)
        {
            RoundedButton button = new RoundedButton();
            button.Location = location;
            button.Size = new Size(130, 34);
            UiDrawing.StyleButton(button, false);
            button.NavBadgeColor = badgeColor;
            button.NavBadgeGlyph = glyph;
            button.Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(36, 0, 0, 0);
            button.Text = text;
            return button;
        }

        private static void StyleNavButton(Button button, bool selected)
        {
            button.BackColor = selected ? UiColors.Selection : UiColors.Sidebar;
            button.ForeColor = selected ? UiColors.Accent : UiColors.Text;
            button.Invalidate();
        }

        private static Button CreateActionButton(string text, bool accent)
        {
            RoundedButton button = new RoundedButton();
            button.Size = new Size(100, 34);
            UiDrawing.StyleButton(button, accent);
            if (!accent)
            {
                button.BackColor = UiColors.Surface;
                button.ForeColor = UiColors.Text;
                button.ShowBorder = true;
            }
            button.Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular);
            button.Text = text;
            return button;
        }

        private static Label CreateLabel(
            string text, Point location, Size size, float fontSize,
            FontStyle style, Color color)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Microsoft YaHei UI", fontSize, style);
            label.ForeColor = color;
            label.Location = location;
            label.Size = size;
            label.Text = text;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        private void Dismiss()
        {
            Hide();
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            if (systemRoundedCorners)
            {
                Region previousRegion = Region;
                if (previousRegion != null)
                {
                    Region = null;
                    previousRegion.Dispose();
                }
                Invalidate();
                return;
            }
            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), ScaleLogical(18)))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
            Invalidate();
        }
    }
}
