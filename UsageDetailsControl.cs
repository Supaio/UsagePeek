using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UsageDetailsControl : Control
    {
        private readonly bool showTotals;
        private UsageSnapshot snapshot;
        private CurrencyDisplayState currency = new CurrencyDisplayState();
        private bool estimateHovered;

        public event EventHandler EstimateAuditRequested;

        public UsageDetailsControl(bool showTotals)
        {
            this.showTotals = showTotals;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            TabStop = showTotals;
            AccessibleRole = showTotals ? AccessibleRole.PushButton : AccessibleRole.Grouping;
            AccessibleName = showTotals ? "本机累计估值 · 查看对账" : "用量明细、重置卡与 Credits";
        }

        public void SetSnapshot(UsageSnapshot value)
        {
            snapshot = value;
            if (showTotals)
                AccessibleDescription = "累计 Token " + DisplayFormatting.FormatLifetimeTokens(snapshot) +
                    "，" + DisplayFormatting.FormatLifetimeScope(snapshot) + "。";
            Invalidate();
        }

        public void SetCurrency(CurrencyDisplayState value)
        {
            currency = value ?? new CurrencyDisplayState();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetEstimateHover(IsOverEstimate(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetEstimateHover(false);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && IsOverEstimate(e.Location)) RequestEstimateAudit();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!showTotals || (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space)) return;
            e.SuppressKeyPress = true;
            RequestEstimateAudit();
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

        private void RequestEstimateAudit()
        {
            EventHandler handler = EstimateAuditRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void SetEstimateHover(bool hovered)
        {
            if (estimateHovered == hovered) return;
            estimateHovered = hovered;
            Cursor = hovered ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        private Rectangle EstimateBounds()
        {
            int width = UiDrawing.LogicalWidth(this);
            int left = width >= 600 ? width / 2 + 26 : width / 2 + 10;
            return new Rectangle(left, 0, Math.Max(1, width - left), 88);
        }

        private bool IsOverEstimate(Point point)
        {
            if (!showTotals) return false;
            DpiAwareForm form = FindForm() as DpiAwareForm;
            float scale = form == null ? 1f : Math.Max(1f, form.DpiScaleFactor);
            return EstimateBounds().Contains((int)(point.X / scale), (int)(point.Y / scale));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (showTotals) DrawTotals(e.Graphics);
            else DrawRows(e.Graphics);
        }

        private void DrawTotals(Graphics graphics)
        {
            Rectangle estimate = EstimateBounds();
            string lifetimeTokens = DisplayFormatting.FormatLifetimeTokens(snapshot);
            string lifetimeCost = FormatLifetimeCost();
            bool highlight = estimateHovered || (Focused && ShowFocusCues);
            using (Font label = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 9f, FontStyle.Regular))
            using (Font note = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (SolidBrush link = new SolidBrush(highlight ? UiColors.Accent : UiColors.Secondary))
            {
                DrawText(graphics, "累计 Token", label, muted, new RectangleF(0, 2, estimate.Left, 19), false);
                DrawText(graphics, "本机累计估值 ›", label, link,
                    new RectangleF(estimate.Left, 2, estimate.Width, 19), false);
                DrawMetric(graphics, lifetimeTokens == "暂无数据"
                        ? "—" : lifetimeTokens.Replace(" tokens", string.Empty),
                    ink, new RectangleF(0, 20, estimate.Left - 12, 49));
                DrawMetric(graphics, lifetimeCost == "暂无数据" ? "—" : lifetimeCost, ink,
                    new RectangleF(estimate.Left, 20, estimate.Width, 49));
                DrawText(graphics, DisplayFormatting.FormatLifetimeScope(snapshot), note, muted,
                    new RectangleF(0, 69, estimate.Left, 17), false);
                DrawText(graphics, FormatLifetimeCostNote(), note, muted,
                    new RectangleF(estimate.Left, 69, estimate.Width, 17), false);
            }
        }

        private void DrawRows(Graphics graphics)
        {
            using (Font labelFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font valueFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 9f, FontStyle.Regular))
            using (Font smallFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 7.8f, FontStyle.Regular))
            using (SolidBrush valueBrush = new SolidBrush(UiColors.Text))
            using (SolidBrush mutedBrush = new SolidBrush(UiColors.Muted))
            using (SolidBrush accentBrush = new SolidBrush(UiColors.Success))
            using (Pen divider = new Pen(UiColors.Border))
            {
                float width = UiDrawing.LogicalWidth(this);
                float tokenEnd = Math.Max(139, width * 0.53f);
                using (Font heading = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 11f, FontStyle.Bold))
                    DrawText(graphics, "本机用量", heading, valueBrush, new RectangleF(0, 6, width - 130, 24), false);

                DrawText(graphics, "本机周期", smallFont, mutedBrush,
                    new RectangleF(0, 48, 64, 18), false);
                DrawText(graphics, "Token", smallFont, mutedBrush,
                    new RectangleF(67, 48, tokenEnd - 67, 18), true);
                DrawText(graphics, "API 等价估值", smallFont, mutedBrush,
                    new RectangleF(tokenEnd + 8, 48, width - tokenEnd - 8, 18), true);

                DrawPeriodRow(graphics, "今天", 0, 72, tokenEnd,
                    labelFont, valueFont, mutedBrush, valueBrush);
                DrawPeriodRow(graphics, "昨天", 1, 104, tokenEnd,
                    labelFont, valueFont, mutedBrush, valueBrush);
                DrawPeriodRow(graphics, "近 30 天", 2, 136, tokenEnd,
                    labelFont, valueFont, mutedBrush, valueBrush);

                graphics.DrawLine(divider, 0, 171, width, 171);
                DrawText(graphics, "重置卡", labelFont, mutedBrush,
                    new RectangleF(0, 186, 56, 18), false);
                string resetCount = snapshot != null &&
                    snapshot.RateLimitResetCreditsAvailable.HasValue
                    ? snapshot.RateLimitResetCreditsAvailable.Value + " 个可用"
                    : "暂不可用";
                DrawText(graphics, resetCount, labelFont,
                    snapshot != null && snapshot.RateLimitResetCreditsAvailable.GetValueOrDefault() > 0
                        ? accentBrush : valueBrush,
                    new RectangleF(68, 186, width - 68, 18), true);
                DrawText(graphics, "Credits", labelFont, mutedBrush,
                    new RectangleF(0, 212, 70, 18), false);
                DrawText(graphics, FormatCredits(), labelFont, valueBrush,
                    new RectangleF(75, 212, width - 75, 18), true);

                DrawText(graphics, "最近到期", labelFont, mutedBrush,
                    new RectangleF(0, 238, 78, 18), false);
                string expiryValue;
                string expiryDetail;
                FormatExpiry(out expiryValue, out expiryDetail);
                DrawText(graphics, expiryValue, labelFont, valueBrush,
                    new RectangleF(82, 238, width - 82, 18), true);
                DrawText(graphics, expiryDetail, smallFont, mutedBrush,
                    new RectangleF(79, 258, width - 79, 16), true);
            }
        }

        private void DrawMetric(
            Graphics graphics,
            string text,
            Brush brush,
            RectangleF bounds)
        {
            bool awaitingPrice = text == "价格待补充";
            for (float size = awaitingPrice ? 23f : 29f; size >= 13f; size -= 1f)
            {
                using (Font font = UiDrawing.LogicalFont(
                    this, awaitingPrice ? "Microsoft YaHei UI" : "Segoe UI", size, FontStyle.Bold))
                {
                    SizeF measured = graphics.MeasureString(text, font);
                    if (measured.Width <= bounds.Width || size <= 13f)
                    {
                        RectangleF line = new RectangleF(bounds.X,
                            Math.Max(bounds.Top, bounds.Bottom - measured.Height), bounds.Width, measured.Height);
                        DrawText(graphics, text, font, brush, line, false);
                        return;
                    }
                }
            }
        }

        private void FormatExpiry(out string value, out string detail)
        {
            DateTime? earliest = null;
            if (snapshot != null && snapshot.ResetCredits != null)
            {
                foreach (RateLimitResetCreditSnapshot credit in snapshot.ResetCredits)
                {
                    if (credit != null && credit.ExpiresAtUtc.HasValue &&
                        (!earliest.HasValue || credit.ExpiresAtUtc.Value < earliest.Value))
                    {
                        earliest = credit.ExpiresAtUtc.Value;
                    }
                }
            }

            if (!earliest.HasValue)
            {
                bool hasCredits = snapshot != null &&
                    snapshot.RateLimitResetCreditsAvailable.GetValueOrDefault() > 0;
                value = hasCredits ? "到期时间未返回" : "暂无可用额度";
                detail = hasCredits ? "仍可在官方用量页查看" : string.Empty;
                return;
            }

            TimeSpan remaining = earliest.Value - DateTime.UtcNow;
            value = remaining.TotalSeconds <= 0
                ? "已到期"
                : DisplayFormatting.FormatDuration(remaining) + "后";
            detail = string.Format(CultureInfo.CurrentCulture,
                "到期 {0:yyyy年M月d日 HH:mm}", earliest.Value.ToLocalTime());
        }

        private string FormatCredits()
        {
            if (snapshot == null)
            {
                return "尚未读取";
            }
            if (snapshot.UnlimitedCredits == true)
            {
                return "无限额度";
            }
            if (snapshot.CreditBalance.HasValue)
            {
                return DisplayFormatting.FormatCreditMoney(
                    snapshot.CreditBalance.Value, currency) + " · " +
                    (snapshot.HasCredits == true ? "已启用" : "未启用");
            }
            return snapshot.HasCredits == true ? "已启用" : "未启用";
        }

        private TokenPeriodSnapshot GetPeriod(int index)
        {
            if (snapshot == null || snapshot.LocalTokenUsage == null)
            {
                return null;
            }

            return index == 0
                ? snapshot.LocalTokenUsage.Today
                : index == 1
                    ? snapshot.LocalTokenUsage.Yesterday
                    : snapshot.LocalTokenUsage.Last30Days;
        }

        private string FormatLifetimeCost()
        {
            TokenPeriodSnapshot lifetime = snapshot == null ||
                snapshot.LocalTokenUsage == null
                ? null
                : snapshot.LocalTokenUsage.Lifetime;
            if (lifetime == null || !lifetime.HasData)
            {
                return "暂无数据";
            }
            if (!lifetime.HasCompleteCostEstimate)
            {
                if (lifetime.PricedTokens > 0 ||
                    lifetime.EstimatedCostUsd > 0m)
                {
                    return DisplayFormatting.FormatPartialEstimatedMoney(
                        lifetime.EstimatedCostUsd, currency);
                }

                return "价格待补充";
            }

            return DisplayFormatting.FormatEstimatedMoney(
                lifetime.EstimatedCostUsd, currency);
        }

        private string FormatLifetimeCostNote()
        {
            TokenPeriodSnapshot lifetime = snapshot == null || snapshot.LocalTokenUsage == null
                ? null : snapshot.LocalTokenUsage.Lifetime;
            return lifetime != null && lifetime.HasData && !lifetime.HasCompleteCostEstimate
                ? "部分模型待补价"
                : "API 等价估值";
        }

        private void DrawPeriodRow(
            Graphics graphics,
            string label,
            int index,
            float y,
            float tokenEnd,
            Font labelFont,
            Font valueFont,
            Brush labelBrush,
            Brush valueBrush)
        {
            TokenPeriodSnapshot period = GetPeriod(index);
            bool hasData = period != null && period.HasData;
            string cost = "暂无数据";
            if (hasData)
            {
                cost = period.HasCompleteCostEstimate
                    ? DisplayFormatting.FormatEstimatedMoney(period.EstimatedCostUsd, currency)
                    : period.PricedTokens > 0 || period.EstimatedCostUsd > 0m
                        ? DisplayFormatting.FormatPartialEstimatedMoney(period.EstimatedCostUsd, currency)
                        : "价格待补充";
            }
            DrawText(graphics, label, labelFont, labelBrush,
                new RectangleF(0, y, 65, 20), false);
            DrawText(graphics, hasData ? DisplayFormatting.FormatTokens(period.TotalTokens) : "—",
                valueFont, valueBrush, new RectangleF(67, y - 1, tokenEnd - 67, 20), true);
            DrawText(graphics, cost, valueFont, valueBrush,
                new RectangleF(tokenEnd + 8, y - 1, UiDrawing.LogicalWidth(this) - tokenEnd - 8, 20), true);
        }

        private static void DrawText(
            Graphics graphics,
            string text,
            Font font,
            Brush brush,
            RectangleF bounds,
            bool alignRight)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = alignRight ? StringAlignment.Far : StringAlignment.Near;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(text, font, brush, bounds, format);
            }
        }
    }
}
