using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class ModelUsageForm : DpiAwareForm
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly Label summaryLabel;
        private readonly FlowLayoutPanel rowsPanel;
        private readonly Label privacyLabel;
        private readonly GlyphButtonControl returnButton;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public ModelUsageForm(UsageSnapshot snapshot)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);

            Text = "UsagePeek · 模型用量";
            ClientSize = new Size(428, 482);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiColors.Background;
            ForeColor = UiColors.Text;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(22, 21);

            Label titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = new Font("Microsoft YaHei UI", 14.2f,
                FontStyle.Bold);
            titleLabel.ForeColor = ForeColor;
            titleLabel.Location = new Point(72, 18);
            titleLabel.Text = "模型用量占比";

            Label subtitleLabel = new Label();
            subtitleLabel.AutoSize = true;
            subtitleLabel.BackColor = Color.Transparent;
            subtitleLabel.Font = new Font("Microsoft YaHei UI", 8f,
                FontStyle.Regular);
            subtitleLabel.ForeColor = UiColors.Muted;
            subtitleLabel.Location = new Point(74, 47);
            subtitleLabel.Text = "本机累计 · 按 Token 分组";

            GlyphButtonControl closeButton = new GlyphButtonControl("×", false);
            closeButton.Size = new Size(30, 30);
            closeButton.Location = new Point(380, 25);
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.ForeColor = UiColors.Muted;
            closeButton.Click += delegate { Close(); };

            summaryLabel = new Label();
            summaryLabel.AutoSize = false;
            summaryLabel.BackColor = Color.Transparent;
            summaryLabel.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Regular);
            summaryLabel.ForeColor = UiColors.Secondary;
            summaryLabel.Location = new Point(24, 81);
            summaryLabel.Size = new Size(380, 22);
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;

            rowsPanel = new FlowLayoutPanel();
            rowsPanel.Location = new Point(22, 114);
            rowsPanel.Size = new Size(384, 320);
            rowsPanel.BackColor = UiColors.Background;
            rowsPanel.AutoScroll = true;
            rowsPanel.FlowDirection = FlowDirection.TopDown;
            rowsPanel.WrapContents = false;
            rowsPanel.Padding = new Padding(0);
            rowsPanel.ClientSizeChanged += delegate { FitModelRows(); };

            privacyLabel = new Label();
            privacyLabel.AutoSize = false;
            privacyLabel.BackColor = Color.Transparent;
            privacyLabel.Font = new Font("Microsoft YaHei UI", 7.4f,
                FontStyle.Regular);
            privacyLabel.ForeColor = UiColors.Muted;
            privacyLabel.Location = new Point(20, 441);
            privacyLabel.Size = new Size(388, 23);
            privacyLabel.TextAlign = ContentAlignment.MiddleLeft;
            privacyLabel.Text = "仅解析模型名与 token_count 元数据，不读取对话正文";

            returnButton = new GlyphButtonControl("←  返回主界面", true);
            returnButton.Size = new Size(130, 34);
            returnButton.Location = new Point(278, 462);
            returnButton.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(brandMark);
            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(closeButton);
            Controls.Add(summaryLabel);
            Controls.Add(rowsPanel);
            Controls.Add(privacyLabel);
            Controls.Add(returnButton);

            MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;
            subtitleLabel.MouseDown += DragWindow;
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape)
                {
                    args.SuppressKeyPress = true;
                    Close();
                }
            };

            Resize += delegate { UpdateRoundedRegion(); };
            UpdateRoundedRegion();
            SetSnapshot(snapshot);
            InitializeDpiLayout();
        }

        public void PlaceNearTray()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            PrepareForScreen(screen);
            Rectangle workArea = screen.WorkingArea;
            Location = new Point(
                Math.Max(workArea.Left + 8, workArea.Right - Width - 12),
                Math.Max(workArea.Top + 8, workArea.Bottom - Height - 12));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CsDropShadow = 0x00020000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CsDropShadow;
                return parameters;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle bounds = new Rectangle(0, 0, logicalWidth, logicalHeight);
            using (SolidBrush background = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(background, bounds);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle borderBounds = new Rectangle(
                0, 0, logicalWidth - 1, logicalHeight - 1);
            using (GraphicsPath borderPath = UiDrawing.RoundedRectangle(
                borderBounds, 19))
            using (Pen border = new Pen(UiColors.Border))
            {
                e.Graphics.DrawPath(border, borderPath);
            }
        }

        private void SetSnapshot(UsageSnapshot snapshot)
        {
            rowsPanel.SuspendLayout();
            while (rowsPanel.Controls.Count > 0)
            {
                Control control = rowsPanel.Controls[0];
                rowsPanel.Controls.RemoveAt(0);
                control.Dispose();
            }

            List<ModelTokenUsageSnapshot> usage = snapshot == null ||
                snapshot.LocalTokenUsage == null ||
                snapshot.LocalTokenUsage.ModelUsage == null
                ? new List<ModelTokenUsageSnapshot>()
                : new List<ModelTokenUsageSnapshot>(
                    snapshot.LocalTokenUsage.ModelUsage);
            usage.Sort(delegate(
                ModelTokenUsageSnapshot left,
                ModelTokenUsageSnapshot right)
            {
                int byTokens = right.TotalTokens.CompareTo(left.TotalTokens);
                return byTokens != 0 ? byTokens : string.Compare(
                    left.Model, right.Model, StringComparison.OrdinalIgnoreCase);
            });
            ResizeForRows(usage.Count);

            long totalTokens = 0L;
            foreach (ModelTokenUsageSnapshot item in usage)
            {
                totalTokens += Math.Max(0L, item.TotalTokens);
            }

            summaryLabel.Text = usage.Count == 0
                ? "尚无可分组的本机模型记录"
                : string.Format(CultureInfo.CurrentCulture,
                    "本机累计 {0} tokens  ·  {1} 个模型",
                    DisplayFormatting.FormatTokens(totalTokens), usage.Count);

            if (usage.Count == 0)
            {
                Label emptyLabel = new Label();
                emptyLabel.AutoSize = false;
                emptyLabel.Dock = DockStyle.Fill;
                emptyLabel.BackColor = Color.Transparent;
                emptyLabel.ForeColor = UiColors.Muted;
                emptyLabel.Font = new Font("Microsoft YaHei UI", 9f,
                    FontStyle.Regular);
                emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
                emptyLabel.Text = "刷新后会按模型显示 Token 数量与百分比";
                SurfacePanel emptyCard = new SurfacePanel();
                emptyCard.Size = new Size(382, 86);
                emptyCard.Margin = new Padding(0, 0, 0, 8);
                emptyCard.Padding = new Padding(10);
                emptyCard.Controls.Add(emptyLabel);
                rowsPanel.Controls.Add(emptyCard);
            }
            else
            {
                foreach (ModelTokenUsageSnapshot item in usage)
                {
                    ModelUsageRowControl row =
                        new ModelUsageRowControl(item);
                    row.Margin = new Padding(0, 0, 0, 8);
                    rowsPanel.Controls.Add(row);
                }
            }
            rowsPanel.ResumeLayout();
            FitModelRows();
        }

        private void FitModelRows()
        {
            int width = Math.Max(1, rowsPanel.ClientSize.Width - 2);
            foreach (Control row in rowsPanel.Controls)
            {
                if (row.Width != width) row.Width = width;
            }
        }

        private void ResizeForRows(int modelCount)
        {
            int visibleRows = modelCount <= 0
                ? 1
                : Math.Min(7, modelCount);
            rowsPanel.Height = modelCount <= 0
                ? 100
                : visibleRows * 74 + 2;
            privacyLabel.Location = new Point(20, rowsPanel.Bottom + 12);
            returnButton.Location = new Point(278, privacyLabel.Bottom + 8);
            ClientSize = new Size(428, returnButton.Bottom + 16);
            UpdateRoundedRegion();
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), ScaleLogical(20)))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
        }

        protected override void OnDpiScaleChanged()
        {
            UpdateRoundedRegion();
            base.OnDpiScaleChanged();
        }
    }

    internal sealed class ModelUsageRowControl : Control
    {
        private readonly ModelTokenUsageSnapshot usage;

        public ModelUsageRowControl(ModelTokenUsageSnapshot value)
        {
            usage = value ?? new ModelTokenUsageSnapshot();
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(382, 66);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int logicalWidth = UiDrawing.LogicalWidth(this);
            Rectangle card = new Rectangle(0, 0, logicalWidth - 1, 64);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(card, 11))
            using (SolidBrush background = new SolidBrush(UiColors.Surface))
            using (Pen border = new Pen(UiColors.Border))
            using (Font modelFont = UiDrawing.LogicalFont(
                this, "Segoe UI", 9.2f, FontStyle.Regular))
            using (Font percentFont = UiDrawing.LogicalFont(
                this, "Segoe UI", 9.4f, FontStyle.Bold))
            using (Font tokensFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 7.4f, FontStyle.Regular))
            using (SolidBrush modelBrush = new SolidBrush(
                UiColors.Text))
            using (SolidBrush percentBrush = new SolidBrush(
                UiColors.Text))
            using (SolidBrush mutedBrush = new SolidBrush(
                UiColors.Muted))
            using (SolidBrush warningBrush = new SolidBrush(
                UiColors.Warning))
            using (SolidBrush trackBrush = new SolidBrush(
                UiColors.Track))
            using (SolidBrush fillBrush = new SolidBrush(
                UiColors.Accent))
            using (StringFormat modelFormat = new StringFormat())
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);

                modelFormat.Trimming = StringTrimming.EllipsisCharacter;
                modelFormat.FormatFlags = StringFormatFlags.NoWrap;
                string model = string.IsNullOrWhiteSpace(usage.Model)
                    ? "未知模型"
                    : usage.Model;
                e.Graphics.DrawString(model, modelFont, modelBrush,
                    new RectangleF(14, 9,
                        Math.Max(80, logicalWidth - 112), 20),
                    modelFormat);

                string percentage = Math.Max(0d, usage.Percentage)
                    .ToString("0.0", CultureInfo.InvariantCulture) + "%";
                SizeF percentSize = e.Graphics.MeasureString(
                    percentage, percentFont);
                e.Graphics.DrawString(percentage, percentFont, percentBrush,
                    logicalWidth - percentSize.Width - 14, 8);

                string tokens = DisplayFormatting.FormatTokens(
                    Math.Max(0L, usage.TotalTokens)) + " tokens" +
                    (usage.HasPriceEstimate == false ? " · 价格待补充" : string.Empty);
                e.Graphics.DrawString(tokens, tokensFont,
                    usage.HasPriceEstimate == false ? warningBrush : mutedBrush,
                    14, 31);

                Rectangle track = new Rectangle(14, 54,
                    Math.Max(1, logicalWidth - 28), 4);
                e.Graphics.FillRectangle(trackBrush, track);
                int fillWidth = (int)Math.Round(track.Width *
                    Math.Min(100d, Math.Max(0d, usage.Percentage)) / 100d);
                if (fillWidth > 0)
                {
                    e.Graphics.FillRectangle(fillBrush,
                        new Rectangle(track.X, track.Y, fillWidth, track.Height));
                }
            }
        }
    }
}
