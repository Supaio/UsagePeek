using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class AboutForm : DpiAwareForm
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;
        internal const string OpenUsageCredit =
            "产品构思、用量展示与交互思路";
        internal const string CharacterCredit =
            "鲸鱼娘形象 · ZipZipPipe";

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public AboutForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);

            Text = "关于 UsagePeek";
            ClientSize = new Size(390, 348);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(9, 14, 22);
            ForeColor = Color.FromArgb(233, 239, 247);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(20, 16);

            Label titleLabel = CreateLabel(
                "UsagePeek", new Point(70, 12), new Size(240, 30),
                14.2f, FontStyle.Bold, ForeColor);
            Label subtitleLabel = CreateLabel(
                "ChatGPT / Codex 用量监控", new Point(72, 42),
                new Size(250, 20), 8f, FontStyle.Regular,
                Color.FromArgb(120, 139, 163));

            GlyphButtonControl closeButton =
                new GlyphButtonControl("×", false);
            closeButton.Size = new Size(30, 30);
            closeButton.Location = new Point(342, 20);
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.ForeColor = Color.FromArgb(164, 179, 198);
            closeButton.Click += delegate { Close(); };

            Label versionLabel = CreateLabel(
                "版本 " + ReadVersion(), new Point(20, 78),
                new Size(350, 22), 8.4f, FontStyle.Bold,
                Color.FromArgb(45, 212, 191));
            Label descriptionLabel = CreateLabel(
                "轻量、只读的 Windows 用量查看器。不会上传本机会话内容。",
                new Point(20, 101), new Size(350, 38),
                8.1f, FontStyle.Regular, Color.FromArgb(150, 169, 193));

            Label creditsLabel = CreateLabel(
                "特别鸣谢", new Point(20, 143), new Size(350, 24),
                9.5f, FontStyle.Bold, ForeColor);

            Panel openUsageCard = CreateCreditCard(
                new Point(20, 174), Color.FromArgb(45, 212, 191));
            LinkLabel openUsageName = new LinkLabel();
            openUsageName.AutoSize = false;
            openUsageName.BackColor = Color.Transparent;
            openUsageName.Font = new Font("Microsoft YaHei UI", 9.2f,
                FontStyle.Bold);
            openUsageName.LinkBehavior = LinkBehavior.HoverUnderline;
            openUsageName.LinkColor = Color.FromArgb(104, 222, 235);
            openUsageName.ActiveLinkColor = Color.FromArgb(155, 240, 247);
            openUsageName.Location = new Point(16, 8);
            openUsageName.Size = new Size(320, 24);
            openUsageName.Text = "OpenUsage";
            openUsageName.LinkClicked += delegate
            {
                OpenWebPage("https://github.com/robinebers/openusage");
            };
            Label openUsageDescription = CreateLabel(
                OpenUsageCredit, new Point(16, 31), new Size(320, 22),
                7.9f, FontStyle.Regular, Color.FromArgb(135, 155, 180));
            openUsageCard.Controls.Add(openUsageName);
            openUsageCard.Controls.Add(openUsageDescription);

            Panel characterCard = CreateCreditCard(
                new Point(20, 240), Color.FromArgb(251, 113, 133));
            Label characterName = CreateLabel(
                "ZipZipPipe", new Point(16, 8), new Size(320, 24),
                9.2f, FontStyle.Bold, Color.FromArgb(255, 160, 179));
            Label characterDescription = CreateLabel(
                "鲸鱼娘形象", new Point(16, 31), new Size(320, 22),
                7.9f, FontStyle.Regular, Color.FromArgb(135, 155, 180));
            characterCard.Controls.Add(characterName);
            characterCard.Controls.Add(characterDescription);

            Label footerLabel = CreateLabel(
                "独立社区实现 · 感谢每一位测试和反馈的朋友",
                new Point(20, 312), new Size(350, 20),
                7.4f, FontStyle.Regular, Color.FromArgb(88, 106, 128));
            footerLabel.TextAlign = ContentAlignment.MiddleCenter;

            Controls.Add(brandMark);
            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(closeButton);
            Controls.Add(versionLabel);
            Controls.Add(descriptionLabel);
            Controls.Add(creditsLabel);
            Controls.Add(openUsageCard);
            Controls.Add(characterCard);
            Controls.Add(footerLabel);

            MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;
            subtitleLabel.MouseDown += DragWindow;
            Resize += delegate { UpdateRoundedRegion(); };
            UpdateRoundedRegion();
            InitializeDpiLayout();
        }

        public void PlaceNearCursor()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            PrepareForScreen(screen);
            Rectangle area = screen.WorkingArea;
            Location = new Point(
                area.Left + Math.Max(8, (area.Width - Width) / 2),
                area.Top + Math.Max(8, (area.Height - Height) / 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRectangle(
                new Rectangle(0, 0, Width - 1, Height - 1),
                ScaleLogical(18)))
            using (Pen border = new Pen(Color.FromArgb(42, 61, 82),
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

        private static Label CreateLabel(
            string text,
            Point location,
            Size size,
            float fontSize,
            FontStyle fontStyle,
            Color color)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Microsoft YaHei UI", fontSize, fontStyle);
            label.ForeColor = color;
            label.Location = location;
            label.Size = size;
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static Panel CreateCreditCard(Point location, Color accent)
        {
            Panel panel = new Panel();
            panel.BackColor = Color.FromArgb(18, 27, 39);
            panel.Location = location;
            panel.Size = new Size(350, 58);

            Panel accentBar = new Panel();
            accentBar.BackColor = accent;
            accentBar.Location = new Point(0, 0);
            accentBar.Size = new Size(4, 58);
            panel.Controls.Add(accentBar);
            return panel;
        }

        private static string ReadVersion()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return string.Format("v{0}.{1}.{2}",
                version.Major, version.Minor, version.Build);
        }

        private static void OpenWebPage(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开链接：" + ex.Message,
                    "UsagePeek", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
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

            using (GraphicsPath path = RoundedRectangle(
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

        private static GraphicsPath RoundedRectangle(
            Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top,
                diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top,
                diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter,
                diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter,
                diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
