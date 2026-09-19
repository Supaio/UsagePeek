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
            Size = new Size(388, 219);
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 14))
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                Color.FromArgb(20, 28, 39),
                Color.FromArgb(14, 21, 30),
                90f))
            using (Pen border = new Pen(Color.FromArgb(40, 52, 68)))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);
            }

            DrawRows(e.Graphics);
        }

        private void DrawRows(Graphics graphics)
        {
            using (Font labelFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular))
            using (Font valueFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold))
            using (Font smallFont = new Font("Microsoft YaHei UI", 7.1f, FontStyle.Regular))
            using (Font lifetimeFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(129, 146, 168)))
            using (SolidBrush valueBrush = new SolidBrush(Color.FromArgb(207, 219, 233)))
            using (SolidBrush mutedBrush = new SolidBrush(Color.FromArgb(100, 118, 141)))
            using (SolidBrush tealBrush = new SolidBrush(Color.FromArgb(94, 234, 212)))
            using (Pen divider = new Pen(Color.FromArgb(34, 46, 61)))
            {
                DrawLabel(graphics, "重置额度", labelFont, labelBrush, 16, 4);
                DrawResetCount(graphics, valueFont, valueBrush, tealBrush, 4);

                DrawLabel(graphics, "最近到期", labelFont, labelBrush, 16, 27);
                string expiryValue;
                string expiryDetail;
                FormatExpiry(out expiryValue, out expiryDetail);
                DrawRight(graphics, expiryValue, valueFont, valueBrush, 27);
                DrawRight(graphics, expiryDetail, smallFont, mutedBrush, 43);

                graphics.DrawLine(divider, 15, 61, Width - 15, 61);

                DrawStandardRow(graphics, "Credits", FormatCredits(), 67,
                    labelFont, valueFont, labelBrush, valueBrush);
                DrawStandardRow(graphics, "今天", FormatPeriod(0), 90,
                    labelFont, valueFont, labelBrush, valueBrush);
                DrawStandardRow(graphics, "昨天", FormatPeriod(1), 113,
                    labelFont, valueFont, labelBrush, valueBrush);
                DrawStandardRow(graphics, "近 30 天", FormatPeriod(2), 136,
                    labelFont, valueFont, labelBrush, valueBrush);
                DrawStandardRow(graphics, "本机累计估值", FormatLifetimeCost(), 159,
                    labelFont, valueFont, labelBrush, tealBrush);

                graphics.DrawLine(divider, 15, 183, Width - 15, 183);

                DrawLabel(graphics, "累计 Token", labelFont, labelBrush, 16, 191);
                DrawRight(graphics, DisplayFormatting.FormatLifetimeTokens(snapshot),
                    lifetimeFont, tealBrush, 186);
                DrawRight(graphics, DisplayFormatting.FormatLifetimeScope(snapshot),
                    smallFont, mutedBrush, 204);
            }
        }

        private void DrawResetCount(
            Graphics graphics,
            Font font,
            Brush valueBrush,
            Brush tealBrush,
            float y)
        {
            string value = snapshot != null &&
                snapshot.RateLimitResetCreditsAvailable.HasValue
                ? snapshot.RateLimitResetCreditsAvailable.Value + " 个可用"
                : "暂不可用";
            Brush brush = snapshot != null &&
                snapshot.RateLimitResetCreditsAvailable.GetValueOrDefault() > 0
                ? tealBrush
                : valueBrush;
            SizeF size = graphics.MeasureString(value, font);
            float x = Width - size.Width - 16;
            if (snapshot != null &&
                snapshot.RateLimitResetCreditsAvailable.GetValueOrDefault() > 0)
            {
                graphics.FillEllipse(tealBrush, x - 13, y + 6, 6, 6);
            }
            graphics.DrawString(value, font, brush, x, y);
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

        private string FormatPeriod(int index)
        {
            if (snapshot == null || snapshot.LocalTokenUsage == null)
            {
                return "暂无数据";
            }

            TokenPeriodSnapshot period = index == 0
                ? snapshot.LocalTokenUsage.Today
                : index == 1
                    ? snapshot.LocalTokenUsage.Yesterday
                    : snapshot.LocalTokenUsage.Last30Days;
            return DisplayFormatting.FormatSpendPeriod(period, currency);
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
                return "$-- · 部分模型无公开价格";
            }

            return DisplayFormatting.FormatEstimatedMoney(
                lifetime.EstimatedCostUsd, currency);
        }

        private static void DrawStandardRow(
            Graphics graphics,
            string label,
            string value,
            float y,
            Font labelFont,
            Font valueFont,
            Brush labelBrush,
            Brush valueBrush)
        {
            DrawLabel(graphics, label, labelFont, labelBrush, 16, y);
            DrawRight(graphics, value, valueFont, valueBrush, y);
        }

        private static void DrawLabel(
            Graphics graphics,
            string text,
            Font font,
            Brush brush,
            float x,
            float y)
        {
            graphics.DrawString(text, font, brush, x, y);
        }

        private static void DrawRight(
            Graphics graphics,
            string text,
            Font font,
            Brush brush,
            float y)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            SizeF size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, brush,
                graphics.VisibleClipBounds.Right - size.Width - 16, y);
        }
    }
}
