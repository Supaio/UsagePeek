using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UsageDetailsControl : Control
    {
        private UsageSnapshot snapshot;
        private CurrencyDisplayState currency = new CurrencyDisplayState();

        public UsageDetailsControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(384, 258);
        }

        public void SetSnapshot(UsageSnapshot value)
        {
            snapshot = value;
            Invalidate();
        }

        public void SetCurrency(CurrencyDisplayState value)
        {
            currency = value ?? new CurrencyDisplayState();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle bounds = new Rectangle(
                0, 0, logicalWidth - 1, logicalHeight - 1);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 14))
            using (SolidBrush background = new SolidBrush(UiColors.Surface))
            using (Pen border = new Pen(UiColors.Border))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);
            }

            DrawRows(e.Graphics);
        }

        private void DrawRows(Graphics graphics)
        {
            using (Font labelFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Font valueFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font smallFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 7.2f, FontStyle.Regular))
            using (SolidBrush labelBrush = new SolidBrush(UiColors.Muted))
            using (SolidBrush valueBrush = new SolidBrush(UiColors.Text))
            using (SolidBrush mutedBrush = new SolidBrush(UiColors.Muted))
            using (SolidBrush accentBrush = new SolidBrush(UiColors.Success))
            using (Pen divider = new Pen(UiColors.Track))
            {
                float width = UiDrawing.LogicalWidth(this);
                float half = width / 2f;
                DrawText(graphics, "累计 Token", labelFont, labelBrush,
                    new RectangleF(18, 14, half - 32, 18), false);
                DrawText(graphics, "本机累计估值", labelFont, labelBrush,
                    new RectangleF(half + 18, 14, half - 36, 18), false);
                string lifetimeTokens = DisplayFormatting.FormatLifetimeTokens(snapshot);
                string lifetimeCost = FormatLifetimeCost();
                DrawMetric(graphics, lifetimeTokens == "暂无数据"
                        ? "—" : lifetimeTokens.Replace(" tokens", string.Empty),
                    valueBrush, new RectangleF(16, 32, half - 30, 34));
                DrawMetric(graphics, lifetimeCost == "暂无数据" ? "—" : lifetimeCost, valueBrush,
                    new RectangleF(half + 16, 32, half - 34, 34));
                DrawText(graphics, DisplayFormatting.FormatLifetimeScope(snapshot),
                    smallFont, mutedBrush, new RectangleF(18, 66, half - 30, 16), false);
                DrawText(graphics, FormatLifetimeCostNote(), smallFont, mutedBrush,
                    new RectangleF(half + 18, 66, half - 36, 16), false);

                graphics.DrawLine(divider, half, 16, half, 76);
                graphics.DrawLine(divider, 18, 89, width - 18, 89);
                DrawText(graphics, "周期", smallFont, mutedBrush,
                    new RectangleF(18, 100, 70, 18), false);
                DrawText(graphics, "Token", smallFont, mutedBrush,
                    new RectangleF(92, 100, 120, 18), true);
                DrawText(graphics, "API 等价估值", smallFont, mutedBrush,
                    new RectangleF(220, 100, width - 238, 18), true);

                DrawPeriodRow(graphics, "今天", 0, 120,
                    labelFont, valueFont, labelBrush, valueBrush);
                DrawPeriodRow(graphics, "昨天", 1, 143,
                    labelFont, valueFont, labelBrush, valueBrush);
                DrawPeriodRow(graphics, "近 30 天", 2, 166,
                    labelFont, valueFont, labelBrush, valueBrush);

                graphics.DrawLine(divider, 18, 191, width - 18, 191);
                DrawText(graphics, "重置卡", labelFont, labelBrush,
                    new RectangleF(18, 202, 56, 18), false);
                string resetCount = snapshot != null &&
                    snapshot.RateLimitResetCreditsAvailable.HasValue
                    ? snapshot.RateLimitResetCreditsAvailable.Value + " 个可用"
                    : "暂不可用";
                DrawText(graphics, resetCount, labelFont,
                    snapshot != null && snapshot.RateLimitResetCreditsAvailable.GetValueOrDefault() > 0
                        ? accentBrush : valueBrush,
                    new RectangleF(76, 202, 114, 18), false);
                DrawText(graphics, "Credits", smallFont, labelBrush,
                    new RectangleF(198, 203, 56, 18), false);
                DrawText(graphics, FormatCredits(), labelFont, valueBrush,
                    new RectangleF(252, 202, width - 270, 18), true);

                DrawText(graphics, "最近到期", labelFont, labelBrush,
                    new RectangleF(18, 225, 80, 18), false);
                string expiryValue;
                string expiryDetail;
                FormatExpiry(out expiryValue, out expiryDetail);
                DrawText(graphics, expiryValue, labelFont, valueBrush,
                    new RectangleF(110, 222, width - 128, 18), true);
                DrawText(graphics, expiryDetail, smallFont, mutedBrush,
                    new RectangleF(100, 240, width - 118, 16), true);
            }
        }

        private void DrawMetric(
            Graphics graphics,
            string text,
            Brush brush,
            RectangleF bounds)
        {
            bool awaitingPrice = text == "价格待补充";
            for (float size = awaitingPrice ? 14.5f : 20.5f; size >= 12.5f; size -= 1f)
            {
                using (Font font = UiDrawing.LogicalFont(
                    this, awaitingPrice ? "Microsoft YaHei UI" : "Segoe UI", size, FontStyle.Bold))
                {
                    if (graphics.MeasureString(text, font).Width <= bounds.Width || size <= 12.5f)
                    {
                        DrawText(graphics, text, font, brush, bounds, false);
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
                new RectangleF(18, y, 70, 20), false);
            DrawText(graphics, hasData ? DisplayFormatting.FormatTokens(period.TotalTokens) : "—",
                valueFont, valueBrush, new RectangleF(92, y - 1, 120, 20), true);
            DrawText(graphics, cost, valueFont, valueBrush,
                new RectangleF(220, y - 1, UiDrawing.LogicalWidth(this) - 238, 20), true);
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
