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
        private AntigravityCreditsSnapshot antigravityCredits;
        private bool antigravityCreditsLoading;
        private DateTime? ledgerUpdatedAtUtc;
        private bool estimateHovered;

        public bool AntigravityMode { get; set; }

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

        public void SetAntigravityCredits(AntigravityCreditsSnapshot credits, bool loading, DateTime? ledgerObservedUtc)
        {
            antigravityCredits = credits;
            antigravityCreditsLoading = loading;
            if (ledgerObservedUtc.HasValue) ledgerUpdatedAtUtc = ledgerObservedUtc;
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
            Rectangle leftTile = new Rectangle(0, 0, Math.Max(1, estimate.Left - 9), 96);
            Rectangle rightTile = new Rectangle(estimate.Left, 0, Math.Max(1, estimate.Width - 1), 96);
            UiDrawing.DrawAppleCard(graphics, leftTile, 15, false);
            UiDrawing.DrawAppleCard(graphics, rightTile, 15, highlight);
            using (Font label = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 9.0f, FontStyle.Regular))
            using (Font note = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (SolidBrush link = new SolidBrush(highlight ? UiColors.Accent : UiColors.Accent))
            {
                DrawText(graphics, AntigravityMode ? "本机累计 Token" : "累计 Token", label, muted, new RectangleF(14, 11, leftTile.Width - 24, 20), false);
                DrawText(graphics, "本机累计估值 ›", label, link,
                    new RectangleF(rightTile.X + 14, 11, rightTile.Width - 24, 20), false);
                DrawMetric(graphics, lifetimeTokens == "暂无数据"
                        ? "—" : lifetimeTokens.Replace(" tokens", string.Empty),
                    ink, new RectangleF(14, 31, leftTile.Width - 26, 36));
                DrawMetric(graphics, lifetimeCost == "暂无数据" ? "—" : lifetimeCost, ink,
                    new RectangleF(rightTile.X + 14, 31, rightTile.Width - 26, 36));
                DrawText(graphics, DisplayFormatting.FormatLifetimeScope(snapshot), note, muted,
                    new RectangleF(14, 71, leftTile.Width - 24, 18), false);
                DrawText(graphics, FormatLifetimeCostNote(), note, muted,
                    new RectangleF(rightTile.X + 14, 71, rightTile.Width - 24, 18), false);
            }
        }

        private void DrawRows(Graphics graphics)
        {
            int fullW = UiDrawing.LogicalWidth(this);
            int fullH = UiDrawing.LogicalHeight(this);
            UiDrawing.DrawAppleCard(graphics, new Rectangle(0, 0, Math.Max(1, fullW - 1), Math.Max(1, fullH - 2)), 15);
            const float pad = 16f;
            using (Font labelFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 9.2f, FontStyle.Regular))
            using (Font valueFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 9.6f, FontStyle.Regular))
            using (Font smallFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8.6f, FontStyle.Regular))
            using (SolidBrush valueBrush = new SolidBrush(UiColors.Text))
            using (SolidBrush mutedBrush = new SolidBrush(UiColors.Muted))
            using (SolidBrush accentBrush = new SolidBrush(UiColors.Success))
            using (Pen divider = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 120 : 155)))
            {
                float width = fullW - pad;
                float tokenEnd = Math.Max(pad + 138, fullW * 0.54f);
                using (Font heading = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 11f, FontStyle.Bold))
                    DrawText(graphics, AntigravityMode ? "本机用量与 Credits" : "本机用量", heading, valueBrush, new RectangleF(pad, 14, width - 150, 26), false);

                DrawText(graphics, "本机周期", smallFont, mutedBrush,
                    new RectangleF(pad, 52, 64, 18), false);
                DrawText(graphics, "Token", smallFont, mutedBrush,
                    new RectangleF(pad + 66, 52, tokenEnd - pad - 66, 18), true);
                DrawText(graphics, "API 等价估值", smallFont, mutedBrush,
                    new RectangleF(tokenEnd + 8, 52, width - tokenEnd - 8, 18), true);

                DrawPeriodRow(graphics, "今天", 0, 78, tokenEnd,
                    labelFont, valueFont, mutedBrush, valueBrush);
                DrawPeriodRow(graphics, "昨天", 1, 108, tokenEnd,
                    labelFont, valueFont, mutedBrush, valueBrush);
                DrawPeriodRow(graphics, "近 30 天", 2, 138, tokenEnd,
                    labelFont, valueFont, mutedBrush, valueBrush);

                if (AntigravityMode)
                {
                    TokenPeriodSnapshot lifetime = snapshot == null || snapshot.LocalTokenUsage == null
                        ? null : snapshot.LocalTokenUsage.Lifetime;
                    long unpriced = lifetime == null ? 0L : Math.Max(0L, lifetime.TotalTokens - lifetime.PricedTokens);
                    DrawText(graphics, "待补价 Token", labelFont, mutedBrush,
                        new RectangleF(pad, 168, 96, 19), false);
                    string unpricedText = lifetime == null || !lifetime.HasData ? "—"
                        : unpriced > 0 ? DisplayFormatting.FormatTokens(unpriced) + "（待补公开单价）" : "0（已全部估价）";
                    using (SolidBrush warnBrush = new SolidBrush(UiColors.Warning))
                        DrawText(graphics, unpricedText, smallFont, unpriced > 0 ? warnBrush : mutedBrush,
                            new RectangleF(pad + 98, 169, width - pad - 98, 18), true);

                    string ledgerTime = ledgerUpdatedAtUtc.HasValue
                        ? "本机估值更新 " + ledgerUpdatedAtUtc.Value.ToLocalTime().ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "本机账本扫描中…";
                    DrawText(graphics, ledgerTime, smallFont, mutedBrush,
                        new RectangleF(pad, 191, width - pad, 17), true);

                    graphics.DrawLine(divider, pad, 214, width, 214);

                    DrawText(graphics, "Antigravity Credits", labelFont, mutedBrush,
                        new RectangleF(pad, 226, 132, 20), false);
                    string creditsValue = FormatAntigravityCredits();
                    DrawText(graphics, creditsValue, antigravityCredits != null ? valueFont : smallFont,
                        antigravityCredits != null && antigravityCredits.RemainingCredits > 0m ? accentBrush
                            : antigravityCredits != null ? valueBrush : mutedBrush,
                        new RectangleF(pad + 134, 225, width - pad - 134, 20), true);

                    DrawText(graphics, "Credits 更新", labelFont, mutedBrush,
                        new RectangleF(pad, 254, 112, 20), false);
                    string creditsTime = antigravityCredits != null && antigravityCredits.ObservedAtUtc != default(DateTime)
                        ? antigravityCredits.ObservedAtUtc.ToLocalTime().ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : antigravityCreditsLoading ? "同步中…" : "—";
                    DrawText(graphics, creditsTime, smallFont, mutedBrush,
                        new RectangleF(pad + 114, 255, width - pad - 114, 18), true);

                    DrawText(graphics, "官方累计 / 重置卡", labelFont, mutedBrush,
                        new RectangleF(pad, 282, 132, 20), false);
                    DrawText(graphics, "暂未接入", smallFont, mutedBrush,
                        new RectangleF(pad + 134, 283, width - pad - 134, 18), true);
                    return;
                }

                graphics.DrawLine(divider, pad, 178, width, 178);
                DrawText(graphics, "重置卡", labelFont, mutedBrush,
                    new RectangleF(pad, 194, 56, 18), false);
                string resetCount = snapshot != null &&
                    snapshot.RateLimitResetCreditsAvailable.HasValue
                    ? snapshot.RateLimitResetCreditsAvailable.Value + " 个可用"
                    : "暂不可用";
                DrawText(graphics, resetCount, labelFont,
                    snapshot != null && snapshot.RateLimitResetCreditsAvailable.GetValueOrDefault() > 0
                        ? accentBrush : valueBrush,
                    new RectangleF(pad + 66, 194, width - pad - 66, 18), true);
                DrawText(graphics, "Credits", labelFont, mutedBrush,
                    new RectangleF(pad, 222, 70, 18), false);
                DrawText(graphics, FormatCredits(), labelFont, valueBrush,
                    new RectangleF(pad + 74, 222, width - pad - 74, 18), true);

                DrawText(graphics, "最近到期", labelFont, mutedBrush,
                    new RectangleF(pad, 250, 78, 18), false);
                string expiryValue;
                string expiryDetail;
                FormatExpiry(out expiryValue, out expiryDetail);
                DrawText(graphics, expiryValue, labelFont, valueBrush,
                    new RectangleF(pad + 80, 250, width - pad - 80, 18), true);
                DrawText(graphics, expiryDetail, smallFont, mutedBrush,
                    new RectangleF(pad + 76, 270, width - pad - 76, 16), true);
            }
        }

        private void DrawMetric(
            Graphics graphics,
            string text,
            Brush brush,
            RectangleF bounds)
        {
            bool awaitingPrice = text == "价格待补充";
            for (float size = awaitingPrice ? 18f : 23f; size >= 13f; size -= 1f)
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

        internal string FormatAntigravityCredits()
        {
            if (antigravityCredits != null)
                return antigravityCredits.RemainingCredits.ToString("#,0.##", CultureInfo.CurrentCulture) + " Credits";
            return antigravityCreditsLoading ? "读取中…" : "请打开并登录桌面版后刷新";
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
            const float pad = 16f;
            float rightEdge = UiDrawing.LogicalWidth(this) - pad;
            DrawText(graphics, label, labelFont, labelBrush,
                new RectangleF(pad, y, 65, 20), false);
            DrawText(graphics, hasData ? DisplayFormatting.FormatTokens(period.TotalTokens) : "—",
                valueFont, valueBrush, new RectangleF(pad + 66, y - 1, tokenEnd - pad - 66, 20), true);
            DrawText(graphics, cost, valueFont, valueBrush,
                new RectangleF(tokenEnd + 8, y - 1, rightEdge - tokenEnd - 8, 20), true);
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
