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
        public bool StartupEnabled { get; set; }
    }

    internal sealed class SettingsForm : DpiAwareForm
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly ComboBox displayModeInput;
        private readonly ComboBox appearanceInput;
        private readonly ComboBox usageModeInput;
        private readonly NumericUpDown scaleInput;
        private readonly CheckBox startupInput;
        private readonly Button settingsNavButton;
        private readonly Button diagnosticsNavButton;
        private readonly Panel settingsPage;
        private readonly Panel diagnosticsPage;
        private readonly TextBox diagnosticsText;
        private readonly Label diagnosticsHeadlineLabel;
        private readonly Label diagnosticsStatusLabel;
        private readonly Button testConnectionButton;
        private readonly Func<DiagnosticsSnapshot> diagnosticsProvider;
        private bool diagnosticsPageSelected;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public event EventHandler ConnectionTestRequested;

        public SettingsForm(
            SettingsSelection initial,
            Func<DiagnosticsSnapshot> reportProvider)
        {
            SettingsSelection safe = initial ?? new SettingsSelection
            {
                DisplayMode = UsageDisplayMode.Pet,
                PetAppearance = PetAppearance.WhaleMaid,
                PetUsageDisplayMode = PetUsageDisplayMode.Used,
                PetScalePercent = 100
            };
            diagnosticsProvider = reportProvider;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
            Text = "UsagePeek 设置与诊断";
            ClientSize = new Size(560, 440);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(9, 14, 22);
            ForeColor = Color.FromArgb(233, 239, 247);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(20, 16);
            Label titleLabel = CreateLabel("设置与诊断",
                new Point(72, 12), new Size(300, 30), 14.2f,
                FontStyle.Bold, ForeColor);
            Label subtitleLabel = CreateLabel(
                "集中管理 UsagePeek，并生成安全的排障信息",
                new Point(72, 42), new Size(390, 20), 8f,
                FontStyle.Regular, Color.FromArgb(120, 139, 163));
            GlyphButtonControl closeButton =
                new GlyphButtonControl("×", false);
            closeButton.Size = new Size(30, 30);
            closeButton.Location = new Point(510, 20);
            closeButton.Font = new Font("Segoe UI", 13f,
                FontStyle.Regular);
            closeButton.ForeColor = Color.FromArgb(164, 179, 198);
            closeButton.Click += delegate { Close(); };

            Panel divider = new Panel();
            divider.BackColor = Color.FromArgb(31, 43, 57);
            divider.Location = new Point(146, 80);
            divider.Size = new Size(1, 340);

            settingsNavButton = CreateNavButton("⚙  常规设置",
                new Point(14, 86));
            diagnosticsNavButton = CreateNavButton("✦  诊断信息",
                new Point(14, 132));
            settingsNavButton.Click += delegate { ShowPage(false); };
            diagnosticsNavButton.Click += delegate { ShowPage(true); };

            settingsPage = new Panel();
            settingsPage.BackColor = Color.Transparent;
            settingsPage.Location = new Point(158, 76);
            settingsPage.Size = new Size(382, 344);

            Label settingsTitle = CreateLabel("常规设置",
                new Point(0, 0), new Size(382, 28), 12f,
                FontStyle.Bold, ForeColor);
            Label settingsHint = CreateLabel(
                "修改会在保存后立即生效，原有用量数据不会受影响。",
                new Point(0, 27), new Size(382, 20), 7.6f,
                FontStyle.Regular, Color.FromArgb(121, 139, 162));
            settingsPage.Controls.Add(settingsTitle);
            settingsPage.Controls.Add(settingsHint);

            displayModeInput = CreateComboBox();
            displayModeInput.Items.AddRange(new object[]
            {
                "经典面板（一页看完）",
                "桌宠模式（点击看详情）"
            });
            displayModeInput.SelectedIndex = safe.DisplayMode ==
                UsageDisplayMode.Pet ? 1 : 0;
            AddSettingRow(settingsPage, 52, "显示模式",
                "选择启动后主要显示的界面", displayModeInput);

            appearanceInput = CreateComboBox();
            appearanceInput.Items.AddRange(new object[]
            {
                "鲸鱼娘趴趴",
                "菲比啾比"
            });
            appearanceInput.SelectedIndex = safe.PetAppearance ==
                PetAppearance.PhoebeChibi ? 1 : 0;
            AddSettingRow(settingsPage, 102, "桌宠外形",
                "两种形象共用摸头和状态表情", appearanceInput);

            usageModeInput = CreateComboBox();
            usageModeInput.Items.AddRange(new object[]
            {
                "显示已用百分比",
                "显示剩余百分比"
            });
            usageModeInput.SelectedIndex = safe.PetUsageDisplayMode ==
                PetUsageDisplayMode.Remaining ? 1 : 0;
            AddSettingRow(settingsPage, 152, "气泡数字",
                "额度预警始终按实际已用比例判断", usageModeInput);

            scaleInput = new NumericUpDown();
            scaleInput.Minimum = DisplayModePreference.MinimumPetScalePercent;
            scaleInput.Maximum = DisplayModePreference.MaximumPetScalePercent;
            scaleInput.Increment = 5;
            scaleInput.Value = Math.Max((int)scaleInput.Minimum,
                Math.Min((int)scaleInput.Maximum, safe.PetScalePercent));
            scaleInput.TextAlign = HorizontalAlignment.Center;
            scaleInput.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            scaleInput.BackColor = Color.FromArgb(12, 21, 32);
            scaleInput.ForeColor = ForeColor;
            AddSettingRow(settingsPage, 202, "桌宠大小",
                "50%–200%，切换显示器时仍会自动适配", scaleInput);

            Panel startupCard = CreateCard(new Point(0, 252),
                new Size(382, 42));
            startupInput = new CheckBox();
            startupInput.AutoSize = false;
            startupInput.Location = new Point(14, 6);
            startupInput.Size = new Size(354, 30);
            startupInput.Font = new Font("Microsoft YaHei UI", 8.8f,
                FontStyle.Bold);
            startupInput.ForeColor = ForeColor;
            startupInput.BackColor = Color.Transparent;
            startupInput.Text = "登录 Windows 后自动启动 UsagePeek";
            startupInput.Checked = safe.StartupEnabled;
            startupInput.UseVisualStyleBackColor = false;
            startupCard.Controls.Add(startupInput);
            settingsPage.Controls.Add(startupCard);

            Button cancelButton = CreateActionButton("取消", false);
            cancelButton.Location = new Point(190, 304);
            cancelButton.Size = new Size(88, 32);
            cancelButton.DialogResult = DialogResult.Cancel;
            Button saveButton = CreateActionButton("保存并应用", true);
            saveButton.Location = new Point(282, 304);
            saveButton.Size = new Size(100, 32);
            saveButton.DialogResult = DialogResult.OK;
            settingsPage.Controls.Add(cancelButton);
            settingsPage.Controls.Add(saveButton);
            AcceptButton = saveButton;
            CancelButton = cancelButton;

            diagnosticsPage = new Panel();
            diagnosticsPage.BackColor = Color.Transparent;
            diagnosticsPage.Location = new Point(158, 76);
            diagnosticsPage.Size = new Size(382, 344);
            Label diagnosticsTitle = CreateLabel("诊断信息",
                new Point(0, 0), new Size(382, 28), 12f,
                FontStyle.Bold, ForeColor);
            Label diagnosticsHint = CreateLabel(
                "用于定位连接、价格与显示问题，可直接复制给开发者。",
                new Point(0, 27), new Size(382, 20), 7.6f,
                FontStyle.Regular, Color.FromArgb(121, 139, 162));
            diagnosticsPage.Controls.Add(diagnosticsTitle);
            diagnosticsPage.Controls.Add(diagnosticsHint);

            Panel statusCard = CreateCard(new Point(0, 52),
                new Size(382, 42));
            diagnosticsHeadlineLabel = CreateLabel("●  本机诊断快照",
                new Point(12, 6), new Size(184, 30), 8.3f,
                FontStyle.Bold, Color.FromArgb(94, 234, 212));
            diagnosticsStatusLabel = CreateLabel("等待检测",
                new Point(196, 6), new Size(174, 30), 7.6f,
                FontStyle.Bold, Color.FromArgb(148, 163, 184));
            diagnosticsStatusLabel.TextAlign = ContentAlignment.MiddleRight;
            statusCard.Controls.Add(diagnosticsHeadlineLabel);
            statusCard.Controls.Add(diagnosticsStatusLabel);
            diagnosticsPage.Controls.Add(statusCard);

            diagnosticsText = new TextBox();
            diagnosticsText.Location = new Point(0, 100);
            diagnosticsText.Size = new Size(382, 176);
            diagnosticsText.Multiline = true;
            diagnosticsText.ReadOnly = true;
            diagnosticsText.ScrollBars = ScrollBars.Vertical;
            diagnosticsText.WordWrap = false;
            diagnosticsText.BorderStyle = BorderStyle.FixedSingle;
            diagnosticsText.BackColor = Color.FromArgb(12, 20, 30);
            diagnosticsText.ForeColor = Color.FromArgb(194, 207, 224);
            diagnosticsText.Font = new Font("Consolas", 8.4f,
                FontStyle.Regular);
            diagnosticsPage.Controls.Add(diagnosticsText);

            Label privacyLabel = CreateLabel(
                "不会读取或复制登录令牌、Cookie、账号标识和会话正文。",
                new Point(0, 278), new Size(382, 20), 7.1f,
                FontStyle.Regular, Color.FromArgb(88, 106, 128));
            diagnosticsPage.Controls.Add(privacyLabel);

            testConnectionButton = CreateActionButton(
                "测试连接", false);
            testConnectionButton.Location = new Point(94, 306);
            testConnectionButton.Size = new Size(90, 32);
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
            refreshDiagnosticsButton.Location = new Point(188, 306);
            refreshDiagnosticsButton.Size = new Size(90, 32);
            refreshDiagnosticsButton.Click += delegate
            {
                RefreshDiagnostics();
            };
            Button copyButton = CreateActionButton("复制诊断", true);
            copyButton.Location = new Point(282, 306);
            copyButton.Size = new Size(100, 32);
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
                    PetAppearance = appearanceInput.SelectedIndex == 1
                        ? PetAppearance.PhoebeChibi
                        : PetAppearance.WhaleMaid,
                    PetUsageDisplayMode = usageModeInput.SelectedIndex == 1
                        ? PetUsageDisplayMode.Remaining
                        : PetUsageDisplayMode.Used,
                    PetScalePercent = Decimal.ToInt32(scaleInput.Value),
                    StartupEnabled = startupInput.Checked
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

        internal void ShowDiagnosticsForTesting()
        {
            ShowPage(true);
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

        public void RefreshDiagnostics()
        {
            diagnosticsStatusLabel.Text = "正在检测…";
            diagnosticsStatusLabel.ForeColor = Color.FromArgb(251, 191, 36);
            try
            {
                DiagnosticsSnapshot snapshot = diagnosticsProvider == null
                    ? null
                    : diagnosticsProvider();
                diagnosticsText.Text = snapshot == null
                    ? "暂时无法生成诊断信息。"
                    : snapshot.BuildReport();
                diagnosticsStatusLabel.Text = snapshot == null
                    ? "检测不可用"
                    : "已更新 " + DateTime.Now.ToString("HH:mm:ss");
                diagnosticsStatusLabel.ForeColor = snapshot == null
                    ? Color.FromArgb(251, 113, 133)
                    : Color.FromArgb(94, 234, 212);
                UpdateDiagnosticsHealth(snapshot == null
                    ? null
                    : snapshot.UsageStatus);
            }
            catch
            {
                diagnosticsText.Text = "诊断生成失败，请稍后重新检测。";
                diagnosticsStatusLabel.Text = "检测失败";
                diagnosticsStatusLabel.ForeColor =
                    Color.FromArgb(251, 113, 133);
                UpdateDiagnosticsHealth(null);
            }
        }

        public void SetConnectionTestBusy(bool busy)
        {
            testConnectionButton.Enabled = !busy;
            testConnectionButton.Text = busy ? "测试中…" : "测试连接";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush glow = new LinearGradientBrush(
                new Rectangle(0, 0, Width, ScaleLogical(84)),
                Color.FromArgb(16, 34, 54),
                Color.FromArgb(9, 14, 22), 0f))
            {
                e.Graphics.FillRectangle(glow, 0, 0, Width,
                    ScaleLogical(84));
            }
            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width - 1, Height - 1),
                ScaleLogical(18)))
            using (Pen border = new Pen(Color.FromArgb(45, 62, 82),
                Math.Max(1f, DpiScaleFactor)))
            {
                e.Graphics.DrawPath(border, path);
            }
        }

        protected override void OnDpiScaleChanged()
        {
            base.OnDpiScaleChanged();
            UpdateRoundedRegion();
        }

        private void ShowPage(bool diagnostics)
        {
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
                    Color.FromArgb(94, 234, 212);
            }
            catch (Exception ex)
            {
                diagnosticsStatusLabel.Text = "复制失败";
                diagnosticsStatusLabel.ForeColor =
                    Color.FromArgb(251, 113, 133);
                MessageBox.Show("无法复制诊断信息：" + ex.Message,
                    "UsagePeek", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void UpdateDiagnosticsHealth(string usageStatus)
        {
            if (string.Equals(usageStatus, "实时数据",
                StringComparison.Ordinal))
            {
                diagnosticsHeadlineLabel.Text = "●  实时状态正常";
                diagnosticsHeadlineLabel.ForeColor =
                    Color.FromArgb(94, 234, 212);
                return;
            }
            if (string.Equals(usageStatus, "缓存数据",
                StringComparison.Ordinal))
            {
                diagnosticsHeadlineLabel.Text = "●  当前使用缓存";
                diagnosticsHeadlineLabel.ForeColor =
                    Color.FromArgb(251, 191, 36);
                return;
            }
            diagnosticsHeadlineLabel.Text = "●  当前尚未连通";
            diagnosticsHeadlineLabel.ForeColor =
                Color.FromArgb(251, 113, 133);
        }

        private static void AddSettingRow(
            Control parent, int top, string title, string hint,
            Control input)
        {
            Panel card = CreateCard(new Point(0, top), new Size(382, 46));
            Label titleLabel = CreateLabel(title,
                new Point(12, 2), new Size(198, 22), 8.2f,
                FontStyle.Bold, Color.FromArgb(226, 233, 242));
            Label hintLabel = CreateLabel(hint,
                new Point(12, 23), new Size(202, 18), 6.9f,
                FontStyle.Regular, Color.FromArgb(105, 124, 148));
            input.Location = new Point(218, 9);
            input.Size = new Size(152, 28);
            card.Controls.Add(titleLabel);
            card.Controls.Add(hintLabel);
            card.Controls.Add(input);
            parent.Controls.Add(card);
        }

        private static Panel CreateCard(Point location, Size size)
        {
            Panel panel = new Panel();
            panel.Location = location;
            panel.Size = size;
            panel.BackColor = Color.FromArgb(17, 26, 38);
            return panel;
        }

        private static ComboBox CreateComboBox()
        {
            ComboBox input = new ComboBox();
            input.DropDownStyle = ComboBoxStyle.DropDownList;
            input.FlatStyle = FlatStyle.Flat;
            input.BackColor = Color.FromArgb(12, 21, 32);
            input.ForeColor = Color.FromArgb(233, 239, 247);
            input.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Regular);
            return input;
        }

        private static Button CreateNavButton(string text, Point location)
        {
            Button button = new Button();
            button.Location = location;
            button.Size = new Size(122, 36);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font("Microsoft YaHei UI", 8.5f,
                FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(10, 0, 0, 0);
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static void StyleNavButton(Button button, bool selected)
        {
            button.BackColor = selected
                ? Color.FromArgb(15, 75, 73)
                : Color.FromArgb(13, 21, 31);
            button.ForeColor = selected
                ? Color.FromArgb(153, 246, 228)
                : Color.FromArgb(139, 155, 176);
            button.FlatAppearance.BorderColor = selected
                ? Color.FromArgb(34, 139, 131)
                : Color.FromArgb(35, 47, 62);
        }

        private static Button CreateActionButton(string text, bool accent)
        {
            Button button = new Button();
            button.Size = new Size(100, 34);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = accent
                ? Color.FromArgb(45, 212, 191)
                : Color.FromArgb(62, 79, 100);
            button.BackColor = accent
                ? Color.FromArgb(14, 104, 96)
                : Color.FromArgb(18, 27, 39);
            button.ForeColor = Color.FromArgb(233, 239, 247);
            button.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Bold);
            button.Text = text;
            button.UseVisualStyleBackColor = false;
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
