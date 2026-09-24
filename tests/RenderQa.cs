using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Windows.Forms;
using UsagePeek;

internal static class RenderQa
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "preview.png";
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        UsageSnapshot snapshot = new UsageSnapshot();
        snapshot.ProviderId = "chatgpt-codex";
        snapshot.ProviderName = "ChatGPT / Codex";
        snapshot.PlanType = "Plus";
        snapshot.FetchedAtUtc = DateTime.UtcNow;
        snapshot.Primary = new UsageWindowSnapshot
        {
            Key = "primary",
            Label = "5 小时窗口",
            UsedPercent = 33,
            WindowDurationMinutes = 300,
            ResetsAtUtc = DateTime.UtcNow.AddHours(2).AddMinutes(14)
        };
        snapshot.Secondary = new UsageWindowSnapshot
        {
            Key = "secondary",
            Label = "7 天窗口",
            UsedPercent = 66,
            WindowDurationMinutes = 10080,
            ResetsAtUtc = DateTime.UtcNow.AddDays(3).AddHours(9)
        };
        snapshot.HasCredits = false;
        snapshot.UnlimitedCredits = false;
        snapshot.CreditBalance = 0m;
        snapshot.RateLimitResetCreditsAvailable = 1;
        snapshot.ResetCredits = new List<RateLimitResetCreditSnapshot>
        {
            new RateLimitResetCreditSnapshot
            {
                GrantedAtUtc = DateTime.UtcNow.AddDays(-14),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(15).AddHours(9),
                Title = "Full reset"
            }
        };
        snapshot.LocalTokenUsage = new LocalTokenUsageSnapshot
        {
            FirstSeenAtUtc = DateTime.UtcNow.AddMonths(-4),
            Today = Period(13200000, 8.59m),
            Yesterday = Period(6100000, 4.65m),
            Last30Days = Period(142400000, 83.03m),
            Lifetime = Period(684200000, 392.41m),
            ModelUsage = new List<ModelTokenUsageSnapshot>
            {
                Model("gpt-5.6-sol", 372204800, 54.4d),
                Model("gpt-6-astra", 171050000, 25.0d),
                Model("codex-auto-review", 68420000, 10.0d),
                Model("gpt-5.6-luna", 47894000, 7.0d),
                Model("gpt-reserve", 24631200, 3.6d)
            }
        };
        snapshot.AccountTokenUsage = new AccountTokenUsageSnapshot
        {
            LifetimeTokens = 829400000,
            DailyTokens = new Dictionary<string, long>()
        };

        bool models = args.Length > 1 && string.Equals(args[1], "models",
            StringComparison.OrdinalIgnoreCase);
        if (models)
        {
            using (ModelUsageForm modelForm = new ModelUsageForm(snapshot))
            {
                Render(modelForm, output);
            }
            return;
        }

        using (MainForm form = new MainForm())
        {
            bool offline = args.Length > 1 && string.Equals(args[1], "offline",
                StringComparison.OrdinalIgnoreCase);
            if (offline)
            {
                form.ShowError("检测到 Codex Windows 桌面版，但其内置组件受 Windows 保护。");
            }
            else
            {
                form.ShowSnapshot(snapshot, null);
            }
            if (!offline && args.Length > 1 && string.Equals(args[1], "cny",
                StringComparison.OrdinalIgnoreCase))
            {
                form.SetCurrencyState(new CurrencyDisplayState
                {
                    ShowCny = true,
                    UsdToCnyRate = 6.7010m,
                    RateDateUtc = DateTime.UtcNow.Date
                });
            }
            form.TopMost = false;
            Render(form, output);
        }
    }

    private static void Render(Form form, string output)
    {
        form.TopMost = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();

        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap,
                new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(output, ImageFormat.Png);
        }

        form.Hide();
    }

    private static ModelTokenUsageSnapshot Model(
        string model,
        long tokens,
        double percentage)
    {
        return new ModelTokenUsageSnapshot
        {
            Model = model,
            TotalTokens = tokens,
            Percentage = percentage
        };
    }

    private static TokenPeriodSnapshot Period(long tokens, decimal cost)
    {
        return new TokenPeriodSnapshot
        {
            TotalTokens = tokens,
            EstimatedCostUsd = cost,
            HasData = true,
            HasCompleteCostEstimate = true
        };
    }
}
