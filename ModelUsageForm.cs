using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class ModelUsageForm : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly Label summaryLabel;
        private readonly FlowLayoutPanel rowsPanel;
        private readonly Label privacyLabel;

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
            BackColor = Color.FromArgb(9, 14, 22);
            ForeColor = Color.FromArgb(233, 239, 247);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(20, 15);

            Label titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = new Font("Microsoft YaHei UI", 14.2f,
                FontStyle.Bold);
            titleLabel.ForeColor = ForeColor;
            titleLabel.Location = new Point(70, 12);
            titleLabel.Text = "模型用量占比";

            Label subtitleLabel = new Label();
            subtitleLabel.AutoSize = true;
            subtitleLabel.BackColor = Color.Transparent;
            subtitleLabel.Font = new Font("Microsoft YaHei UI", 8f,
                FontStyle.Regular);
            subtitleLabel.ForeColor = Color.FromArgb(120, 139, 163);
            subtitleLabel.Location = new Point(72, 42);
            subtitleLabel.Text = "本机累计 · 按 Token 分组";

            GlyphButtonControl closeButton = new GlyphButtonControl("×", false);
            closeButton.Size = new Size(30, 30);
            closeButton.Location = new Point(380, 20);
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.ForeColor = Color.FromArgb(164, 179, 198);
            closeButton.Click += delegate { Close(); };

            summaryLabel = new Label();
            summaryLabel.AutoSize = false;
            summaryLabel.BackColor = Color.Transparent;
            summaryLabel.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Regular);
            summaryLabel.ForeColor = Color.FromArgb(129, 146, 168);
            summaryLabel.Location = new Point(20, 75);
            summaryLabel.Size = new Size(388, 22);
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;

            rowsPanel = new FlowLayoutPanel();
            rowsPanel.Location = new Point(20, 105);
            rowsPanel.Size = new Size(388, 320);
            rowsPanel.BackColor = Color.FromArgb(9, 14, 22);
            rowsPanel.AutoScroll = true;
            rowsPanel.FlowDirection = FlowDirection.TopDown;
            rowsPanel.WrapContents = false;
            rowsPanel.Padding = new Padding(0);

            privacyLabel = new Label();
            privacyLabel.AutoSize = false;
            privacyLabel.BackColor = Color.Transparent;
            privacyLabel.Font = new Font("Microsoft YaHei UI", 7.4f,
                FontStyle.Regular);
            privacyLabel.ForeColor = Color.FromArgb(88, 106, 128);
            privacyLabel.Location = new Point(20, 441);
            privacyLabel.Size = new Size(388, 23);
            privacyLabel.TextAlign = ContentAlignment.MiddleLeft;
            privacyLabel.Text = "仅解析模型名与 token_count 元数据，不读取对话正文";

            Controls.Add(brandMark);
            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(closeButton);
            Controls.Add(summaryLabel);
            Controls.Add(rowsPanel);
            Controls.Add(privacyLabel);

            MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;
            subtitleLabel.MouseDown += DragWindow;

            Resize += delegate { UpdateRoundedRegion(); };
            UpdateRoundedRegion();
            SetSnapshot(snapshot);
        }

        public void PlaceNearTray()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                Color.FromArgb(12, 19, 29),
                Color.FromArgb(7, 12, 19),
                90f))
            {
                e.Graphics.FillRectangle(background, bounds);
            }

            using (SolidBrush tealGlow = new SolidBrush(
                Color.FromArgb(15, 45, 212, 191)))
            using (SolidBrush blueGlow = new SolidBrush(
                Color.FromArgb(10, 56, 189, 248)))
            {
                e.Graphics.FillEllipse(tealGlow, -80, -110, 300, 230);
                e.Graphics.FillEllipse(blueGlow, Width - 170, -95, 230, 190);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle borderBounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath borderPath = UiDrawing.RoundedRectangle(
                borderBounds, 19))
            using (Pen border = new Pen(Color.FromArgb(49, 62, 80)))
            using (LinearGradientBrush accentLine = new LinearGradientBrush(
                new Rectangle(22, 65, Width - 44, 1),
                Color.FromArgb(0, 45, 212, 191),
                Color.FromArgb(80, 45, 212, 191),
                0f))
            {
                e.Graphics.DrawPath(border, borderPath);
                e.Graphics.FillRectangle(accentLine, 22, 65, Width - 44, 1);
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
                return right.TotalTokens.CompareTo(left.TotalTokens);
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
                emptyLabel.Size = new Size(366, 86);
                emptyLabel.Margin = new Padding(0, 0, 0, 8);
                emptyLabel.BackColor = Color.FromArgb(18, 27, 39);
                emptyLabel.ForeColor = Color.FromArgb(129, 146, 168);
                emptyLabel.Font = new Font("Microsoft YaHei UI", 9f,
                    FontStyle.Regular);
                emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
                emptyLabel.Text = "刷新后会按模型显示 Token 数量与百分比";
                rowsPanel.Controls.Add(emptyLabel);
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
        }

        private void ResizeForRows(int modelCount)
        {
            int visibleRows = modelCount <= 0
                ? 1
                : Math.Min(7, modelCount);
            rowsPanel.Height = modelCount <= 0
                ? 100
                : visibleRows * 70 + 2;
            privacyLabel.Location = new Point(20, rowsPanel.Bottom + 12);
            ClientSize = new Size(428, privacyLabel.Bottom + 18);
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
                new Rectangle(0, 0, Width, Height), 20))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
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
            Size = new Size(366, 62);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle card = new Rectangle(0, 0, Width - 1, 56);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(card, 11))
            using (LinearGradientBrush background = new LinearGradientBrush(
                card,
                Color.FromArgb(21, 31, 44),
                Color.FromArgb(15, 23, 34),
                90f))
            using (Pen border = new Pen(Color.FromArgb(40, 53, 70)))
            using (Font modelFont = new Font("Segoe UI", 9.2f, FontStyle.Bold))
            using (Font percentFont = new Font("Segoe UI", 9.4f, FontStyle.Bold))
            using (Font tokensFont = new Font("Microsoft YaHei UI", 7.4f,
                FontStyle.Regular))
            using (SolidBrush modelBrush = new SolidBrush(
                Color.FromArgb(219, 230, 242)))
            using (SolidBrush percentBrush = new SolidBrush(
                Color.FromArgb(94, 234, 212)))
            using (SolidBrush mutedBrush = new SolidBrush(
                Color.FromArgb(112, 132, 157)))
            using (SolidBrush trackBrush = new SolidBrush(
                Color.FromArgb(34, 47, 63)))
            using (SolidBrush fillBrush = new SolidBrush(
                Color.FromArgb(45, 212, 191)))
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
                    new RectangleF(14, 7, Math.Max(80, Width - 112), 20),
                    modelFormat);

                string percentage = Math.Max(0d, usage.Percentage)
                    .ToString("0.0", CultureInfo.InvariantCulture) + "%";
                SizeF percentSize = e.Graphics.MeasureString(
                    percentage, percentFont);
                e.Graphics.DrawString(percentage, percentFont, percentBrush,
                    Width - percentSize.Width - 14, 6);

                string tokens = DisplayFormatting.FormatTokens(
                    Math.Max(0L, usage.TotalTokens)) + " tokens";
                e.Graphics.DrawString(tokens, tokensFont, mutedBrush, 14, 27);

                Rectangle track = new Rectangle(14, 47,
                    Math.Max(1, Width - 28), 4);
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
