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
        private readonly Panel diagnosticsOverviewCard;
        private readonly Panel diagnosticsReportCard;
        private readonly Button diagnosticsDetailsButton;
        private readonly Label autoSaveStatusLabel;
        private readonly Button testConnectionButton;
        private readonly Func<DiagnosticsSnapshot> diagnosticsProvider;
        private bool diagnosticsPageSelected;
        private bool suppressSettingsChanged;
        private bool displayPrepared;
        private bool diagnosticsDetailsExpanded;
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

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(20, 21);
            Label titleLabel = CreateLabel("设置与诊断",
                new Point(72, 18), new Size(300, 30), 14.2f,
                FontStyle.Bold, ForeColor);
            Label subtitleLabel = CreateLabel(
                "外观、桌宠与运行状态",
                new Point(72, 47), new Size(390, 20), 8f,
                FontStyle.Regular, UiColors.Muted);
            GlyphButtonControl closeButton =
                new GlyphButtonControl("×", false);
            closeButton.Size = new Size(30, 30);
            closeButton.Location = new Point(510, 25);
            closeButton.Font = new Font("Segoe UI", 13f,
                FontStyle.Regular);
            closeButton.ForeColor = UiColors.Muted;
            closeButton.Click += delegate { Dismiss(); };

            Panel divider = new Panel();
            divider.BackColor = UiColors.Border;
            divider.Location = new Point(146, 80);
            divider.Size = new Size(1, 430);

            settingsNavButton = CreateNavButton("常规设置",
                new Point(14, 86));
            diagnosticsNavButton = CreateNavButton("诊断信息",
                new Point(14, 132));
            settingsNavButton.Click += delegate { ShowPage(false); };
            diagnosticsNavButton.Click += delegate { ShowPage(true); };

            settingsPage = new Panel();
            settingsPage.BackColor = Color.Transparent;
            settingsPage.Location = new Point(158, 76);
            settingsPage.Size = new Size(382, 434);

            Label settingsTitle = CreateLabel("常规设置",
                new Point(0, 0), new Size(382, 28), 12f,
                FontStyle.Bold, ForeColor);
            Label settingsHint = CreateLabel(
                "更改即时生效，并自动保存。",
                new Point(0, 27), new Size(382, 20), 7.6f,
                FontStyle.Regular, UiColors.Muted);
            settingsPage.Controls.Add(settingsTitle);
            settingsPage.Controls.Add(settingsHint);

            themeInput = CreateComboBox();
            themeInput.Items.AddRange(new object[] { "跟随系统", "浅色模式", "深色模式" });
            themeInput.SelectedIndex = (int)safe.ThemeMode;
            AddSettingRow(settingsPage, 52, "主题",
                "跟随 Windows 的浅色与深色", themeInput);

            displayModeInput = CreateComboBox();
            displayModeInput.Items.AddRange(new object[]
            {
                "经典面板",
                "桌宠模式"
            });
            displayModeInput.SelectedIndex = safe.DisplayMode ==
                UsageDisplayMode.Pet ? 1 : 0;
            AddSettingRow(settingsPage, 102, "显示模式",
                "桌宠点击后打开用量详情", displayModeInput);

            appearanceInput = CreateComboBox();
            appearanceInput.Items.AddRange(new object[]
            {
                "鲸鱼娘趴趴",
                "菲比啾比",
                "ChatGPT 娘",
                "Claude 娘",
                "奶蛙"
            });
            appearanceInput.SelectedIndex = Math.Max(0,
                Math.Min(appearanceInput.Items.Count - 1, (int)safe.PetAppearance));
            AddSettingRow(settingsPage, 152, "桌宠外形",
                "五种外形，支持散步、摸头", appearanceInput);

            usageModeInput = CreateComboBox();
            usageModeInput.Items.AddRange(new object[]
            {
                "已用百分比",
                "剩余百分比"
            });
            usageModeInput.SelectedIndex = safe.PetUsageDisplayMode ==
                PetUsageDisplayMode.Remaining ? 1 : 0;
            AddSettingRow(settingsPage, 202, "气泡数字",
                "预警按实际已用比例判断", usageModeInput);

            scaleInput = new ModernNumberControl();
            scaleInput.Minimum = DisplayModePreference.MinimumPetScalePercent;
            scaleInput.Maximum = DisplayModePreference.MaximumPetScalePercent;
            scaleInput.Increment = 5;
            scaleInput.Value = Math.Max((int)scaleInput.Minimum,
                Math.Min((int)scaleInput.Maximum, safe.PetScalePercent));
            scaleInput.TextAlign = HorizontalAlignment.Center;
            scaleInput.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            scaleInput.BackColor = UiColors.Surface;
            scaleInput.ForeColor = ForeColor;
            AddSettingRow(settingsPage, 252, "桌宠大小",
                "50%–200%，随显示器缩放适配", scaleInput);

            Panel alwaysOnTopCard = CreateCard(new Point(0, 302),
                new Size(382, 42));
            petAlwaysOnTopInput = new SwitchCheckBox();
            petAlwaysOnTopInput.AutoSize = false;
            petAlwaysOnTopInput.Location = new Point(14, 6);
            petAlwaysOnTopInput.Size = new Size(354, 30);
            petAlwaysOnTopInput.Font = new Font("Microsoft YaHei UI", 8.8f,
                FontStyle.Bold);
            petAlwaysOnTopInput.ForeColor = ForeColor;
            petAlwaysOnTopInput.BackColor = UiColors.Surface;
            petAlwaysOnTopInput.Text = "桌宠保持置顶";
            petAlwaysOnTopInput.Checked = safe.PetAlwaysOnTop;
            petAlwaysOnTopInput.UseVisualStyleBackColor = false;
            alwaysOnTopCard.Controls.Add(petAlwaysOnTopInput);
            settingsPage.Controls.Add(alwaysOnTopCard);

            Panel startupCard = CreateCard(new Point(0, 350),
                new Size(382, 42));
            startupInput = new SwitchCheckBox();
            startupInput.AutoSize = false;
            startupInput.Location = new Point(14, 6);
            startupInput.Size = new Size(354, 30);
            startupInput.Font = new Font("Microsoft YaHei UI", 8.8f,
                FontStyle.Bold);
            startupInput.ForeColor = ForeColor;
            startupInput.BackColor = UiColors.Surface;
            startupInput.Text = "登录 Windows 后自动启动 UsagePeek";
            startupInput.Checked = safe.StartupEnabled;
            startupInput.UseVisualStyleBackColor = false;
            startupCard.Controls.Add(startupInput);
            settingsPage.Controls.Add(startupCard);

            autoSaveStatusLabel = CreateLabel("●  更改会自动保存",
                new Point(0, 400), new Size(382, 28), 7.4f,
                FontStyle.Regular, UiColors.Success);
            settingsPage.Controls.Add(autoSaveStatusLabel);

            diagnosticsPage = new Panel();
            diagnosticsPage.BackColor = Color.Transparent;
            diagnosticsPage.Location = new Point(158, 76);
            diagnosticsPage.Size = new Size(382, 434);
            Label diagnosticsTitle = CreateLabel("诊断信息",
                new Point(0, 0), new Size(382, 28), 12f,
                FontStyle.Bold, ForeColor);
            Label diagnosticsHint = CreateLabel(
                "查看连接、价格覆盖和显示环境。",
                new Point(0, 27), new Size(382, 20), 7.6f,
                FontStyle.Regular, UiColors.Muted);
            diagnosticsPage.Controls.Add(diagnosticsTitle);
            diagnosticsPage.Controls.Add(diagnosticsHint);

            Panel statusCard = CreateCard(new Point(0, 52),
                new Size(382, 88));
            diagnosticsHeadlineLabel = CreateLabel("准备检查运行状态",
                new Point(16, 9), new Size(350, 28), 11.5f,
                FontStyle.Bold, UiColors.Text);
            diagnosticsSubtitleLabel = CreateLabel("打开诊断后读取本机状态。",
                new Point(16, 37), new Size(350, 22), 8f,
                FontStyle.Regular, UiColors.Secondary);
            diagnosticsStatusLabel = CreateLabel("等待检测",
                new Point(16, 63), new Size(350, 18), 7.3f,
                FontStyle.Regular, UiColors.Muted);
            statusCard.Controls.Add(diagnosticsHeadlineLabel);
            statusCard.Controls.Add(diagnosticsSubtitleLabel);
            statusCard.Controls.Add(diagnosticsStatusLabel);
            diagnosticsPage.Controls.Add(statusCard);

            diagnosticsOverviewCard = CreateCard(new Point(0, 150), new Size(382, 174));
            AddDiagnosticsRow(diagnosticsOverviewCard, 0, "Codex 连接",
                out connectionValueLabel, out connectionDetailLabel);
            AddDiagnosticsRow(diagnosticsOverviewCard, 58, "价格覆盖",
                out priceValueLabel, out priceDetailLabel);
            AddDiagnosticsRow(diagnosticsOverviewCard, 116, "显示环境",
                out displayValueLabel, out displayDetailLabel);
            diagnosticsPage.Controls.Add(diagnosticsOverviewCard);

            diagnosticsText = new TextBox();
            diagnosticsText.Location = new Point(12, 10);
            diagnosticsText.Size = new Size(358, 154);
            diagnosticsText.Multiline = true;
            diagnosticsText.ReadOnly = true;
            diagnosticsText.ScrollBars = ScrollBars.Vertical;
            diagnosticsText.WordWrap = true;
            diagnosticsText.BorderStyle = BorderStyle.None;
            diagnosticsText.BackColor = UiColors.Surface;
            diagnosticsText.ForeColor = UiColors.Secondary;
            diagnosticsText.Font = new Font("Consolas", 8.4f,
                FontStyle.Regular);
            diagnosticsReportCard = CreateCard(new Point(0, 150), new Size(382, 174));
            diagnosticsReportCard.Controls.Add(diagnosticsText);
            diagnosticsReportCard.Visible = false;
            diagnosticsPage.Controls.Add(diagnosticsReportCard);

            diagnosticsDetailsButton = CreateActionButton("查看技术详情  ›", false);
            diagnosticsDetailsButton.Location = new Point(0, 332);
            diagnosticsDetailsButton.Size = new Size(382, 30);
            diagnosticsDetailsButton.TextAlign = ContentAlignment.MiddleLeft;
            diagnosticsDetailsButton.Padding = new Padding(12, 0, 0, 0);
            diagnosticsDetailsButton.BackColor = UiColors.Background;
            diagnosticsDetailsButton.Click += delegate
            {
                ShowDiagnosticsDetails(!diagnosticsDetailsExpanded);
            };
            diagnosticsPage.Controls.Add(diagnosticsDetailsButton);

            Label privacyLabel = CreateLabel(
                "复制报告已隐藏私人路径和登录信息。",
                new Point(0, 409), new Size(382, 20), 7.3f,
                FontStyle.Regular, UiColors.Muted);
            diagnosticsPage.Controls.Add(privacyLabel);

            testConnectionButton = CreateActionButton(
                "测试连接", true);
            testConnectionButton.Location = new Point(0, 373);
            testConnectionButton.Size = new Size(118, 32);
            testConnectionButton.Click += delegate
            {
                EventHandler handler = ConnectionTestRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            Button refreshDiagnosticsButton = CreateActionButton(
                "重新检测", false);
            refreshDiagnosticsButton.Location = new Point(126, 373);
            refreshDiagnosticsButton.Size = new Size(118, 32);
            refreshDiagnosticsButton.Click += delegate
            {
                RefreshDiagnostics();
            };
            Button copyButton = CreateActionButton("复制报告", false);
            copyButton.Location = new Point(252, 373);
            copyButton.Size = new Size(130, 32);
            copyButton.Click += delegate { CopyDiagnostics(); };
            diagnosticsPage.Controls.Add(testConnectionButton);
            diagnosticsPage.Controls.Add(refreshDiagnosticsButton);
            diagnosticsPage.Controls.Add(copyButton);

            Controls.Add(brandMark);
            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(closeButton);
            Controls.Add(divider);
            Controls.Add(settingsNavButton);
            Controls.Add(diagnosticsNavButton);
            Controls.Add(settingsPage);
            Controls.Add(diagnosticsPage);

            MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;
            subtitleLabel.MouseDown += DragWindow;
            Resize += delegate { UpdateRoundedRegion(); };
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape)
                {
                    args.SuppressKeyPress = true;
                    Dismiss();
                }
            };
            displayModeInput.SelectedIndexChanged += delegate
            {
                RaiseSettingsChanged();
            };
            themeInput.SelectedIndexChanged += delegate { RaiseSettingsChanged(); };
            appearanceInput.SelectedIndexChanged += delegate
            {
                RaiseSettingsChanged();
            };
            usageModeInput.SelectedIndexChanged += delegate
            {
                RaiseSettingsChanged();
            };
            scaleInput.ValueChanged += delegate { RaiseSettingsChanged(); };
            petAlwaysOnTopInput.CheckedChanged += delegate
            {
                RaiseSettingsChanged();
            };
            startupInput.CheckedChanged += delegate
            {
                RaiseSettingsChanged();
            };
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
            get { return diagnosticsHeadlineLabel.Text + "\n" + diagnosticsSubtitleLabel.Text +
                "\n" + connectionValueLabel.Text + "\n" + connectionDetailLabel.Text +
                "\n" + priceValueLabel.Text + "\n" + priceDetailLabel.Text +
                "\n" + displayValueLabel.Text; }
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
            displayModeInput.SelectedIndex = mode == UsageDisplayMode.Pet
                ? 1
                : 0;
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
            try
            {
                petAlwaysOnTopInput.Checked = enabled;
            }
            finally
            {
                suppressSettingsChanged = false;
            }
        }

        internal void DismissForTesting()
        {
            Dismiss();
        }

        public void SetSelection(SettingsSelection value)
        {
            if (value == null)
            {
                return;
            }

            suppressSettingsChanged = true;
            try
            {
                themeInput.SelectedIndex = (int)value.ThemeMode;
                displayModeInput.SelectedIndex = value.DisplayMode ==
                    UsageDisplayMode.Pet ? 1 : 0;
                appearanceInput.SelectedIndex = Math.Max(0,
                    Math.Min(appearanceInput.Items.Count - 1,
                        (int)value.PetAppearance));
                usageModeInput.SelectedIndex = value.PetUsageDisplayMode ==
                    PetUsageDisplayMode.Remaining ? 1 : 0;
                scaleInput.Value = Math.Max((int)scaleInput.Minimum,
                    Math.Min((int)scaleInput.Maximum,
                        value.PetScalePercent));
                petAlwaysOnTopInput.Checked = value.PetAlwaysOnTop;
                startupInput.Checked = value.StartupEnabled;
            }
            finally
            {
                suppressSettingsChanged = false;
            }
        }

        public void SetApplyResult(bool success)
        {
            autoSaveStatusLabel.Text = success
                ? "●  已自动保存"
                : "●  保存失败，已恢复";
            autoSaveStatusLabel.ForeColor = success
                ? UiColors.Success
                : UiColors.Danger;
        }

        public void PlaceNearCursor()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            PrepareForScreen(screen);
            Rectangle area = screen.WorkingArea;
            int left = Width >= area.Width
                ? area.Left
                : area.Left + (area.Width - Width) / 2;
            int top = Height >= area.Height
                ? area.Top
                : area.Top + (area.Height - Height) / 2;
            Location = new Point(
                Math.Max(area.Left, Math.Min(left, area.Right - Width)),
                Math.Max(area.Top, Math.Min(top, area.Bottom - Height)));
        }

        public void PrepareForDisplay()
        {
            if (displayPrepared && VisibleControlsReadyForTesting)
            {
                return;
            }
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
                DiagnosticsSnapshot snapshot = diagnosticsProvider == null
                    ? null
                    : diagnosticsProvider();
                diagnosticsText.Text = snapshot == null
                    ? "暂时无法生成诊断信息。"
                    : snapshot.BuildReport();
                lastDiagnosticsSnapshot = snapshot;
                diagnosticsStatusLabel.Text = snapshot == null
                    ? "检测不可用"
                    : "检查于 " + snapshot.GeneratedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                diagnosticsStatusLabel.ForeColor = snapshot == null
                    ? UiColors.Danger
                    : UiColors.Muted;
                UpdateDiagnosticsOverview(snapshot);
            }
            catch
            {
                diagnosticsText.Text = "诊断生成失败，请稍后重新检测。";
                diagnosticsStatusLabel.Text = "检测失败";
                diagnosticsStatusLabel.ForeColor =
                    UiColors.Danger;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width - 1, Height - 1),
                ScaleLogical(18)))
            using (Pen border = new Pen(UiColors.Border,
                Math.Max(1f, DpiScaleFactor)))
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
            StyleNavButton(settingsNavButton, !diagnostics);
            StyleNavButton(diagnosticsNavButton, diagnostics);
            if (diagnostics)
            {
                RefreshDiagnostics();
            }
        }

        private void RaiseSettingsChanged()
        {
            if (suppressSettingsChanged)
            {
                return;
            }

            autoSaveStatusLabel.Text = "●  正在应用…";
            autoSaveStatusLabel.ForeColor = UiColors.Warning;
            EventHandler handler = SettingsChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void CopyDiagnostics()
        {
            if (string.IsNullOrWhiteSpace(diagnosticsText.Text))
            {
                RefreshDiagnostics();
            }

            try
            {
                Clipboard.SetText(diagnosticsText.Text);
                diagnosticsStatusLabel.Text = "已复制到剪贴板";
                diagnosticsStatusLabel.ForeColor =
                    UiColors.Success;
            }
            catch (Exception ex)
            {
                diagnosticsStatusLabel.Text = "复制失败";
                diagnosticsStatusLabel.ForeColor =
                    UiColors.Danger;
                MessageBox.Show(this, "无法复制诊断信息：" + ex.Message,
                    "UsagePeek", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ShowDiagnosticsDetails(bool expanded)
        {
            diagnosticsDetailsExpanded = expanded;
            diagnosticsOverviewCard.Visible = !expanded;
            diagnosticsReportCard.Visible = expanded;
            diagnosticsDetailsButton.Text = expanded
                ? "‹  返回状态概览" : "查看技术详情  ›";
        }

        private void UpdateDiagnosticsOverview(DiagnosticsSnapshot snapshot)
        {
            if (snapshot == null)
            {
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

        private static void AddDiagnosticsRow(Control parent, int top, string title,
            out Label valueLabel, out Label detailLabel)
        {
            Label titleLabel = CreateLabel(title,
                new Point(16, top + 7), new Size(150, 24), 8.5f,
                FontStyle.Regular, UiColors.Text);
            valueLabel = CreateLabel("等待检测",
                new Point(172, top + 7), new Size(194, 24), 8.5f,
                FontStyle.Bold, UiColors.Muted);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            detailLabel = CreateLabel("读取后显示检查结果。",
                new Point(16, top + 31), new Size(350, 20), 7.5f,
                FontStyle.Regular, UiColors.Muted);
            parent.Controls.Add(titleLabel);
            parent.Controls.Add(valueLabel);
            parent.Controls.Add(detailLabel);
            if (top > 0)
            {
                Panel divider = new Panel();
                divider.Bounds = new Rectangle(16, top, 350, 1);
                divider.BackColor = UiColors.Border;
                parent.Controls.Add(divider);
            }
        }

        private static void AddSettingRow(
            Control parent, int top, string title, string hint,
            Control input)
        {
            Panel card = CreateCard(new Point(0, top), new Size(382, 46));
            Label titleLabel = CreateLabel(title,
                new Point(12, 2), new Size(198, 22), 8.2f,
                FontStyle.Regular, UiColors.Text);
            Label hintLabel = CreateLabel(hint,
                new Point(12, 23), new Size(202, 18), 7.3f,
                FontStyle.Regular, UiColors.Muted);
            input.Location = new Point(218, 9);
            input.Size = new Size(152, 30);
            input.AccessibleName = title;
            input.AccessibleDescription = hint;
            card.Controls.Add(titleLabel);
            card.Controls.Add(hintLabel);
            card.Controls.Add(input);
            parent.Controls.Add(card);
        }

        private static Panel CreateCard(Point location, Size size)
        {
            Panel panel = new SurfacePanel();
            panel.Location = location;
            panel.Size = size;
            return panel;
        }

        private static void CreateChildHandles(Control root)
        {
            if (!root.IsHandleCreated)
            {
                IntPtr unused = root.Handle;
            }
            foreach (Control child in root.Controls)
            {
                CreateChildHandles(child);
            }
        }

        private static ModernSelectControl CreateComboBox()
        {
            ModernSelectControl input = new ModernSelectControl();
            input.BackColor = UiColors.Surface;
            input.ForeColor = UiColors.Text;
            input.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Regular);
            return input;
        }

        private static Button CreateNavButton(string text, Point location)
        {
            Button button = new RoundedButton();
            button.Location = location;
            button.Size = new Size(122, 36);
            UiDrawing.StyleButton(button, false);
            button.Font = new Font("Microsoft YaHei UI", 8.5f,
                FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(10, 0, 0, 0);
            button.Text = text;
            return button;
        }

        private static void StyleNavButton(Button button, bool selected)
        {
            button.BackColor = selected
                ? UiColors.Selection
                : UiColors.Background;
            button.ForeColor = selected
                ? UiColors.Accent
                : UiColors.Secondary;
        }

        private static Button CreateActionButton(string text, bool accent)
        {
            Button button = new RoundedButton();
            button.Size = new Size(100, 34);
            UiDrawing.StyleButton(button, accent);
            button.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Regular);
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
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown,
                new IntPtr(HtCaption), IntPtr.Zero);
        }

        private void Dismiss()
        {
            Hide();
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }
            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), ScaleLogical(18)))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
            Invalidate();
        }
    }
}
