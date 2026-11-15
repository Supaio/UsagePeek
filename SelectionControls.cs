using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class ModernSelectControl : Control
    {
        private readonly List<object> items = new List<object>();
        private int selectedIndex = -1;
        private bool hovered;
        private ContextMenuStrip popup;
        private Font popupFont;

        public ModernSelectControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Opaque | ControlStyles.UserPaint, true);
            BackColor = UiColors.Surface;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.ComboBox;
            Size = new Size(152, 30);
        }

        public List<object> Items { get { return items; } }
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int next = Math.Max(-1, Math.Min(items.Count - 1, value));
                if (next == selectedIndex) return;
                selectedIndex = next;
                Text = next >= 0 ? Convert.ToString(items[next], CultureInfo.CurrentCulture) : string.Empty;
                Invalidate();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            OpenList();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space ||
                e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
            {
                e.SuppressKeyPress = true;
                OpenList();
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                SelectedIndex = Math.Max(0, selectedIndex + (e.KeyCode == Keys.Up ? -1 : 1));
            }
        }

        private void OpenList()
        {
            if (!Enabled || items.Count == 0) return;
            if (popup != null && popup.Visible) { popup.Close(); return; }
            if (popup == null)
            {
                popup = new ContextMenuStrip();
                popup.Closed += delegate { if (!IsDisposed && !Disposing) Invalidate(); };
            }
            ContextMenuStrip menu = popup;
            while (menu.Items.Count > 0)
            {
                ToolStripItem previous = menu.Items[0];
                menu.Items.RemoveAt(0);
                previous.Dispose();
            }
            menu.Renderer = new ToolStripProfessionalRenderer(new UsageMenuColors());
            menu.BackColor = UiColors.Surface;
            menu.ForeColor = UiColors.Text;
            DpiAwareForm form = FindForm() as DpiAwareForm;
            float dpi = form == null ? 96f : form.CurrentDpi;
            Font previousFont = popupFont;
            popupFont = new Font("Microsoft YaHei UI",
                8.5f * dpi / DpiNative.GetSystemDpi(), FontStyle.Regular);
            menu.Font = popupFont;
            if (previousFont != null) previousFont.Dispose();
            menu.Padding = new Padding(5);
            menu.MinimumSize = new Size(Width, 0);
            for (int index = 0; index < items.Count; index++)
            {
                int choice = index;
                ToolStripMenuItem item = new ToolStripMenuItem(
                    Convert.ToString(items[index], CultureInfo.CurrentCulture));
                item.Checked = index == selectedIndex;
                item.Padding = new Padding(6, 5, 10, 5);
                item.Click += delegate { SelectedIndex = choice; };
                menu.Items.Add(item);
            }
            menu.Show(this, new Point(0, Height + 4));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(UiDrawing.BackdropColor(this)))
                e.Graphics.FillRectangle(background, ClientRectangle);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int width = UiDrawing.LogicalWidth(this);
            int height = UiDrawing.LogicalHeight(this);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(1, 1, width - 2, height - 2), 8))
            using (SolidBrush fill = new SolidBrush(hovered || (popup != null && popup.Visible)
                ? UiColors.FieldHover : UiColors.Field))
            using (Pen border = new Pen(Focused ? UiColors.Accent : UiColors.Field))
            using (Pen chevron = new Pen(UiColors.Secondary, 1.4f))
            using (Font font = UiDrawing.LogicalFont(this,
                "Microsoft YaHei UI", 8.2f, FontStyle.Regular))
            using (SolidBrush text = new SolidBrush(Enabled ? UiColors.Text : UiColors.Muted))
            using (StringFormat format = new StringFormat())
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                e.Graphics.DrawString(Text, font, text,
                    new RectangleF(10, 0, Math.Max(1, width - 36), height), format);
                chevron.StartCap = chevron.EndCap = LineCap.Round;
                e.Graphics.DrawLines(chevron, new[]
                {
                    new PointF(width - 23, height / 2f - 2),
                    new PointF(width - 19, height / 2f + 2),
                    new PointF(width - 15, height / 2f - 2)
                });
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new SelectAccessibleObject(this);
        }

        private sealed class SelectAccessibleObject : ControlAccessibleObject
        {
            private readonly ModernSelectControl owner;
            public SelectAccessibleObject(ModernSelectControl control) : base(control) { owner = control; }
            public override string Value { get { return owner.Text; } }
            public override string DefaultAction { get { return "展开选项"; } }
            public override void DoDefaultAction()
            {
                if (owner.IsHandleCreated) owner.BeginInvoke(new MethodInvoker(owner.OpenList));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (popup != null)
                {
                    popup.Dispose();
                    popup = null;
                }
                if (popupFont != null)
                {
                    popupFont.Dispose();
                    popupFont = null;
                }
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernNumberControl : Control
    {
        private readonly TextBox input;
        private readonly GlyphButtonControl decrease;
        private readonly GlyphButtonControl increase;
        private decimal minimum = 50;
        private decimal maximum = 200;
        private decimal number = 100;
        private bool updating;

        public ModernNumberControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
            AccessibleRole = AccessibleRole.SpinButton;
            TabStop = false;
            Increment = 5;
            input = new TextBox();
            input.BorderStyle = BorderStyle.None;
            input.BackColor = UiColors.Field;
            input.ForeColor = UiColors.Text;
            input.TextAlign = HorizontalAlignment.Center;
            input.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            input.AccessibleName = "桌宠缩放百分比";
            input.Text = "100";
            input.TextChanged += delegate
            {
                decimal parsed;
                if (!updating && decimal.TryParse(input.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out parsed) && parsed >= minimum && parsed <= maximum)
                    Value = parsed;
            };
            input.Leave += delegate { CommitText(); };
            input.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Up || args.KeyCode == Keys.Down)
                {
                    args.SuppressKeyPress = true;
                    Step(args.KeyCode == Keys.Up ? 1 : -1);
                }
                else if (args.KeyCode == Keys.Enter)
                {
                    args.SuppressKeyPress = true;
                    CommitText();
                }
            };
            decrease = new GlyphButtonControl("−", false);
            increase = new GlyphButtonControl("+", false);
            decrease.AccessibleName = "缩小桌宠";
            increase.AccessibleName = "放大桌宠";
            decrease.Font = increase.Font = new Font("Segoe UI", 11f);
            decrease.Click += delegate { Step(-1); };
            increase.Click += delegate { Step(1); };
            Controls.Add(input);
            Controls.Add(decrease);
            Controls.Add(increase);
            Size = new Size(152, 30);
            UpdateButtons();
        }

        public event EventHandler ValueChanged;
        public decimal Minimum { get { return minimum; } set { minimum = value; Value = number; } }
        public decimal Maximum { get { return maximum; } set { maximum = value; Value = number; } }
        public decimal Increment { get; set; }
        public HorizontalAlignment TextAlign { get { return input.TextAlign; } set { input.TextAlign = value; } }
        public decimal Value
        {
            get { return number; }
            set
            {
                decimal next = Math.Max(minimum, Math.Min(maximum, decimal.Truncate(value)));
                bool changed = number != next;
                number = next;
                updating = true;
                try { input.Text = number.ToString("0", CultureInfo.InvariantCulture); }
                finally { updating = false; }
                UpdateButtons();
                Invalidate();
                EventHandler handler = ValueChanged;
                if (changed && handler != null) handler(this, EventArgs.Empty);
            }
        }

        internal void Step(int direction) { Value = number + direction * Increment; }

        private void CommitText()
        {
            decimal parsed;
            Value = decimal.TryParse(input.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed) ? parsed : number;
        }

        private void UpdateButtons()
        {
            if (decrease == null || increase == null) return;
            decrease.Enabled = number > minimum;
            increase.Enabled = number < maximum;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (input != null) input.Font = Font;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (input == null || decrease == null || increase == null) return;
            DpiAwareForm form = FindForm() as DpiAwareForm;
            float scale = form == null ? 1f : form.DpiScaleFactor;
            int buttonWidth = (int)Math.Round(24 * scale);
            int horizontalInset = (int)Math.Round(5 * scale);
            int verticalInset = (int)Math.Round(4 * scale);
            int buttonHeight = Math.Max(1, Height - verticalInset * 2);
            decrease.Bounds = new Rectangle(horizontalInset, verticalInset, buttonWidth, buttonHeight);
            increase.Bounds = new Rectangle(Math.Max(horizontalInset, Width - buttonWidth - horizontalInset),
                verticalInset, buttonWidth, buttonHeight);
            int textHeight = input.PreferredHeight;
            int textInset = buttonWidth + horizontalInset;
            input.Bounds = new Rectangle(textInset, (Height - textHeight) / 2,
                Math.Max(1, Width - textInset * 2 - (int)Math.Round(20 * scale)), textHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(UiDrawing.BackdropColor(this)))
                e.Graphics.FillRectangle(background, ClientRectangle);
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int width = UiDrawing.LogicalWidth(this);
            int height = UiDrawing.LogicalHeight(this);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, width - 1, height - 1), 8))
            using (SolidBrush fill = new SolidBrush(UiColors.Field))
                e.Graphics.FillPath(fill, path);
            e.Graphics.Restore(state);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            int width = UiDrawing.LogicalWidth(this);
            int height = UiDrawing.LogicalHeight(this);
            using (Font font = UiDrawing.LogicalFont(this, "Segoe UI", 8.5f, FontStyle.Regular))
            using (SolidBrush text = new SolidBrush(UiColors.Muted))
                e.Graphics.DrawString("%", font, text, width - 48, (height - 16) / 2f);
        }
    }
}
