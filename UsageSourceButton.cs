using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace UsagePeek
{
    internal enum UsageSource
    {
        Codex,
        Antigravity,
        ClaudeCode,
        UstcDeepSeek
    }

    internal sealed class UsageSourceButton : Button
    {
        private readonly Bitmap mark;
        private readonly Rectangle artwork;
        private readonly bool circular;
        private bool hovered;
        private bool selected;

        public UsageSourceButton(string name, string resource, Rectangle artwork, bool circular)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            Text = name;
            AccessibleName = name;
            AccessibleRole = AccessibleRole.PageTab;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.Opaque, false);
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Size = new Size(42, 42);

            this.artwork = artwork;
            this.circular = circular;
            using (Stream stream = typeof(UsageSourceButton).Assembly.GetManifestResourceStream("UsagePeek.Assets." + resource))
            {
                if (stream != null)
                using (Image original = Image.FromStream(stream))
                    mark = new Bitmap(original);
            }
        }

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                Invalidate();
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new SourceAccessibleObject(this);
        }

        private sealed class SourceAccessibleObject : ControlAccessibleObject
        {
            private readonly UsageSourceButton button;

            public SourceAccessibleObject(UsageSourceButton owner) : base(owner) { button = owner; }

            public override AccessibleStates State
            {
                get { return base.State | AccessibleStates.Selectable |
                    (button.Selected ? AccessibleStates.Selected : AccessibleStates.None); }
            }

            public override string DefaultAction { get { return "切换来源"; } }

            public override void DoDefaultAction()
            {
                if (button.IsHandleCreated) button.BeginInvoke(new MethodInvoker(button.PerformClick));
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int width = UiDrawing.LogicalWidth(this);
            int height = UiDrawing.LogicalHeight(this);
            if (width < 2 || height < 2) return;

            using (GraphicsPath tile = UiDrawing.RoundedRectangle(new Rectangle(1, 1, width - 2, height - 2), 12))
            {
                if (hovered)
                using (SolidBrush background = new SolidBrush(UiColors.Field))
                    e.Graphics.FillPath(background, tile);

                if (selected)
                using (GraphicsPath marker = UiDrawing.RoundedRectangle(new Rectangle(2, 13, 3, height - 26), 2))
                using (SolidBrush tint = new SolidBrush(UiColors.Accent))
                    e.Graphics.FillPath(tint, marker);

                if (Focused && ShowFocusCues)
                using (Pen outline = new Pen(UiColors.Accent)) e.Graphics.DrawPath(outline, tile);
            }

            bool withLabel = width >= 104;
            int iconCenter = withLabel ? 23 : width / 2;
            if (withLabel)
            {
                using (Font label = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.5f,
                    selected ? FontStyle.Bold : FontStyle.Regular))
                using (SolidBrush ink = new SolidBrush(selected ? UiColors.Text : UiColors.Secondary))
                using (StringFormat format = new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                    e.Graphics.DrawString(Text, label, ink, new RectangleF(38, 0, width - 39, height), format);
            }

            if (mark != null)
            {
                float iconSize = circular ? 26 : 23;
                float scale = iconSize / Math.Max(artwork.Width, artwork.Height);
                float iconWidth = artwork.Width * scale;
                float iconHeight = artwork.Height * scale;
                RectangleF target = new RectangleF(iconCenter - iconWidth / 2f,
                    (height - iconHeight) / 2f, iconWidth, iconHeight);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (circular)
                {
                    using (GraphicsPath circle = new GraphicsPath())
                    {
                        GraphicsState state = e.Graphics.Save();
                        circle.AddEllipse(target);
                        e.Graphics.SetClip(circle, CombineMode.Intersect);
                        e.Graphics.DrawImage(mark, target, artwork, GraphicsUnit.Pixel);
                        e.Graphics.Restore(state);
                    }
                }
                else e.Graphics.DrawImage(mark, target, artwork, GraphicsUnit.Pixel);
            }
            else
            {
                using (Font logicalFont = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 10, FontStyle.Bold))
                using (Brush foreground = new SolidBrush(UiColors.Text))
                using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    e.Graphics.DrawString(Text.Substring(0, 1), logicalFont, foreground,
                        new Rectangle(iconCenter - 12, 0, 24, height), centered);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && mark != null) mark.Dispose();
            base.Dispose(disposing);
        }
    }
}
