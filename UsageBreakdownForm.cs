using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UsageBreakdownForm : DpiAwareForm
    {
        private int nextTop = 101;
        private bool systemRoundedCorners;

        private UsageBreakdownForm(string title, string subtitle, string scope, string backText)
        {
            SuspendLayout();
            Text = "UsagePeek · " + title;
            ClientSize = new Size(520, 450);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiColors.Background;
            ForeColor = UiColors.Text;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            DoubleBuffered = true;

            AddLabel(this, title, new Rectangle(22, 16, 360, 34), 15f, UiColors.Text, true);
            AddLabel(this, subtitle, new Rectangle(23, 51, 475, 23), 9f, UiColors.Text);
            AddLabel(this, scope, new Rectangle(23, 75, 475, 19), 8.3f, UiColors.Secondary);
            RoundedButton back = new RoundedButton
            {
                Text = backText,
                Bounds = new Rectangle(390, 21, 108, 29),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            UiDrawing.StyleButton(back, false);
            back.Click += delegate { Close(); };
            Controls.Add(back);
            CancelButton = back;
        }

        public static UsageBreakdownForm ForConversation(string title, string scope,
            LocalConversationUsage usage, CurrencyDisplayState currency)
        {
            UsageBreakdownForm form = new UsageBreakdownForm("会话明细", title,
                scope + "  ·  本机账本", "← 返回排行");
            List<KeyValuePair<string, string>> tokens = new List<KeyValuePair<string, string>>
            {
                Row("总 Token", Count(usage.TotalTokens)),
                Row("输入", TokenShare(usage.InputTokens, usage.TokensWithBreakdown)),
                Row("其中缓存命中", TokenShare(usage.CachedInputTokens, usage.InputTokens)),
                Row("输出", TokenShare(usage.OutputTokens, usage.TokensWithBreakdown)),
                Row("待补价 Token", Count(usage.TotalTokens - usage.PricedTokens)),
                Row("API 等价估价", DisplayFormatting.FormatConversationEstimate(usage, currency))
            };
            if (usage.TokensWithBreakdown < usage.TotalTokens)
                tokens.Insert(1, Row("有输入和输出记录的 Token", TokenShare(usage.TokensWithBreakdown, usage.TotalTokens)));
            form.AddSection("Token 构成与估价", tokens);
            form.AddSection("按模型", usage.ModelTokens.OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => Row(item.Key, TokenShare(item.Value, usage.TotalTokens))).ToList());

            List<KeyValuePair<string, string>> agents = usage.Subagents.OrderByDescending(item => item.Value.Tokens)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => Row(item.Value.Name ?? "子智能体 " + item.Key.Substring(Math.Max(0, item.Key.Length - 8)),
                    TokenShare(item.Value.Tokens, usage.TotalTokens))).ToList();
            if (agents.Count == 0) agents.Add(Row("本段暂无可归属的子智能体用量", "—"));
            form.AddSection("子智能体", agents);
            form.FinishLayout();
            return form;
        }

        public static UsageBreakdownForm ForEstimate(EstimateAudit audit, LocalTokenUsageSnapshot local)
        {
            EstimateAuditPoint current = audit.Current;
            if (current != null && !current.IsValid) current = null;
            EstimateComparison usd = audit.Compare(false);
            EstimateComparison cny = audit.Compare(true);
            string scope = current == null ? "等待读取本机账本" : TimeLabel(current.AtUtc);
            if (usd != null) scope = TimeLabel(audit.Previous.AtUtc) + "  →  " + scope;
            UsageBreakdownForm form = new UsageBreakdownForm("累计估价对账", "本机账本 · API 等价估价",
                scope, "← 返回详情");
            if (current == null)
            {
                form.AddSection("对账记录", new[] { Row("首次成功读取账本后建立对账基线", "—") });
            }
            else if (usd == null)
            {
                form.AddSection("已记录对账基线", new[]
                {
                    Row("累计估价", Amount(current.EstimatedUsd,
                        current.CnyRate.HasValue ? current.EstimatedUsd * current.CnyRate.Value : (decimal?)null)),
                    Row("待补价 Token", Count(current.TotalTokens - current.PricedTokens)),
                    Row("用量、补价或参考汇率变化后可对账", "—")
                });
            }
            else
            {
                form.AddSection("两次估价", new[]
                {
                    Row("上次估价", Amount(usd.Before, cny == null ? (decimal?)null : cny.Before)),
                    Row("本次估价", Amount(usd.After, cny == null ? (decimal?)null : cny.After)),
                    Row("变动合计", Amount(usd.Total, cny == null ? (decimal?)null : cny.Total, true)),
                    Row("待补价 Token", Count(current.TotalTokens - current.PricedTokens))
                });
                List<KeyValuePair<string, string>> causes = new List<KeyValuePair<string, string>>
                {
                    Row(AttributedLabel("新增入账", usd.CanAttribute, usd.AddedTokens), usd.CanAttribute
                        ? Amount(usd.Added, cny == null ? (decimal?)null : cny.Added, true) : "—"),
                    Row(AttributedLabel("历史补价", usd.CanAttribute, usd.BackfilledTokens), usd.CanAttribute
                        ? Amount(usd.Backfilled, cny == null ? (decimal?)null : cny.Backfilled, true) : "—"),
                    Row("人民币汇率影响", cny != null ? Money(cny.Exchange, "¥", true)
                        : current.CnyRate.HasValue ? "首次记录参考汇率" : "待记录参考汇率")
                };
                if (!usd.CanAttribute || usd.Adjustment != 0m || (cny != null && cny.Adjustment != 0m))
                    causes.Add(Row("账本调整", Amount(usd.Adjustment, cny == null ? (decimal?)null : cny.Adjustment, true)));
                if (usd.Rounding != 0m || (cny != null && cny.Rounding != 0m))
                    causes.Add(Row("显示舍入差", Amount(usd.Rounding, cny == null ? (decimal?)null : cny.Rounding, true)));
                form.AddSection("差额来源", causes);
                if (cny != null)
                    form.AddNote("人民币参考汇率  " + audit.Previous.CnyRate.Value.ToString("0.####", CultureInfo.InvariantCulture) +
                        " → " + current.CnyRate.Value.ToString("0.####", CultureInfo.InvariantCulture));
            }
            TokenPeriodSnapshot lifetime = local == null ? null : local.Lifetime;
            if (current != null && (lifetime == null || current.TotalTokens != lifetime.TotalTokens ||
                current.EstimatedUsd != lifetime.EstimatedCostUsd || current.PricedTokens != lifetime.PricedTokens))
                form.AddNote("本页显示最近一次已核对的账本记录，当前主界面估价尚待核对。");
            form.FinishLayout();
            return form;
        }

        private void AddSection(string title, IList<KeyValuePair<string, string>> rows)
        {
            SurfacePanel panel = new SurfacePanel { Bounds = new Rectangle(18, nextTop, 484, 39 + rows.Count * 24) };
            Controls.Add(panel);
            AddLabel(panel, title, new Rectangle(14, 7, 452, 25), 9.5f, UiColors.Text, true);
            for (int index = 0; index < rows.Count; index++)
            {
                int y = 32 + index * 24;
                AddLabel(panel, rows[index].Key, new Rectangle(16, y, 225, 24), 8.5f, UiColors.Secondary);
                AddLabel(panel, rows[index].Value, new Rectangle(245, y, 223, 24), 8.5f, UiColors.Text)
                    .TextAlign = ContentAlignment.MiddleRight;
            }
            nextTop += panel.Height + 12;
        }

        private void AddNote(string text)
        {
            AddLabel(this, text, new Rectangle(22, nextTop - 4, 476, 22), 8.2f, UiColors.Secondary);
            nextTop += 23;
        }

        private void FinishLayout()
        {
            ClientSize = new Size(520, Math.Min(540, nextTop + 3));
            ResumeLayout(false);
            InitializeDpiLayout();
        }

        public void PlaceOnScreen(Screen screen)
        {
            PrepareForScreen(screen);
            Rectangle area = screen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            systemRoundedCorners = UiTheme.TryApplyRoundedCorners(this);
            UpdateRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (!IsHandleCreated || Width < 2 || Height < 2) return;
            Region old = Region;
            if (systemRoundedCorners) Region = null;
            else
            using (GraphicsPath rounded = UiDrawing.RoundedRectangle(new Rectangle(0, 0, Width, Height), ScaleLogical(18)))
                Region = new Region(rounded);
            if (old != null) old.Dispose();
        }

        private static Label AddLabel(Control parent, string text, Rectangle bounds, float size, Color color, bool bold = false)
        {
            Label label = new Label
            {
                Text = text,
                Bounds = bounds,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false
            };
            parent.Controls.Add(label);
            return label;
        }

        private static KeyValuePair<string, string> Row(string name, string value)
        {
            return new KeyValuePair<string, string>(name, value);
        }

        private static string Count(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string TokenShare(long value, long total)
        {
            return total <= 0 ? "—" : Count(value) + " · " +
                (value / (double)total).ToString("P1", CultureInfo.InvariantCulture);
        }

        private static string AttributedLabel(string label, bool comparable, long tokens)
        {
            return comparable ? label + " · " + Count(tokens) + " Token" : label;
        }

        private static string TimeLabel(DateTime utc)
        {
            return utc.ToLocalTime().ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string Amount(decimal usd, decimal? cny, bool signed = false)
        {
            return Money(usd, "$", signed) + (cny.HasValue ? "   ·   " + Money(cny.Value, "¥", signed) : "");
        }

        private static string Money(decimal amount, string symbol, bool signed)
        {
            string sign = amount < 0m ? "−" : signed && amount > 0m ? "+" : "";
            return sign + symbol + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);
        }
    }
}
