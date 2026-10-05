using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using UsagePeek;

internal static class SettingsDiagnosticsQa
{
    private static int failures;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        VerifySafeReport();
        VerifySettingsSelection();
        VerifyTrySave();

        if (failures > 0)
        {
            Console.Error.WriteLine(
                "Settings/diagnostics QA failed: " + failures);
            return 1;
        }

        Console.WriteLine("Settings/diagnostics QA passed.");
        return 0;
    }

    private static void VerifySafeReport()
    {
        string profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        DiagnosticsSnapshot snapshot = CreateDiagnostics();
        snapshot.ExecutablePath = Path.Combine(profile,
            "PrivateFolder", "UsagePeek.exe");
        snapshot.CodexPath = @"F:\PrivateOwner\SecretFolder\codex.exe";
        snapshot.CodexSource = "Cookie: session=DO_NOT_COPY";
        snapshot.ProviderName = "Bearer DO_NOT_COPY";
        snapshot.LastRefreshError =
            "Authorization: Bearer DO_NOT_COPY?access_token=SECRET";

        string report = snapshot.BuildReport();
        Check(report.Contains("%USERPROFILE%"),
            "known user path is normalized");
        Check(!report.Contains(profile),
            "raw user profile is absent");
        Check(!report.Contains("PrivateFolder"),
            "private folders below the user profile are absent");
        Check(report.Contains(@"F:\…\codex.exe"),
            "external absolute path is collapsed");
        Check(!report.Contains("PrivateOwner") &&
            !report.Contains("SecretFolder"),
            "external directory names are absent");
        Check(DiagnosticsSnapshot.RedactPath(
                @"\\private-server\secret-share\folder\codex.exe") ==
                @"\\…\codex.exe",
            "UNC server and share names are collapsed");
        Check(!report.Contains("DO_NOT_COPY") &&
            !report.Contains("access_token") &&
            !report.Contains("Authorization"),
            "unknown diagnostic text cannot leak secrets");
        Check(report.Contains("UP-CX-999"),
            "unknown errors become a safe diagnostic code");
        Check(report.Contains("未包含登录令牌"),
            "report states its privacy boundary");
    }

    private static void VerifySettingsSelection()
    {
        SettingsSelection initial = new SettingsSelection
        {
            DisplayMode = UsageDisplayMode.Classic,
            PetAppearance = PetAppearance.PhoebeChibi,
            PetUsageDisplayMode = PetUsageDisplayMode.Remaining,
            PetScalePercent = 137,
            StartupEnabled = true
        };
        using (SettingsForm form = new SettingsForm(initial,
            CreateDiagnostics))
        {
            int settingsChanges = 0;
            form.SettingsChanged += delegate { settingsChanges++; };
            SettingsSelection selected = form.Selection;
            Check(selected.DisplayMode == UsageDisplayMode.Classic,
                "settings keeps classic mode");
            Check(selected.PetAppearance == PetAppearance.PhoebeChibi,
                "settings keeps the selected appearance");
            Check(selected.PetUsageDisplayMode ==
                    PetUsageDisplayMode.Remaining,
                "settings keeps remaining usage mode");
            Check(selected.PetScalePercent == 137,
                "settings keeps custom pet scale");
            Check(selected.StartupEnabled,
                "settings keeps startup choice");

            form.SelectDisplayModeForTesting(UsageDisplayMode.Pet);
            Check(settingsChanges == 1 &&
                    form.Selection.DisplayMode == UsageDisplayMode.Pet,
                "changing an option immediately raises an apply request");
            form.SetSelection(initial);
            Check(settingsChanges == 1 &&
                    form.Selection.DisplayMode == UsageDisplayMode.Classic,
                "restoring a failed choice does not raise another request");

            form.ShowDiagnosticsForTesting();
            Check(form.DiagnosticsTextForTesting.Contains(
                    "UsagePeek 诊断信息"),
                "diagnostics page builds its report locally");
            Check(form.DiagnosticsPageVisibleForTesting,
                "diagnostics navigation changes the visible page");
        }
    }

    private static void VerifyTrySave()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "UsagePeekSettingsQa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "display.json");
        try
        {
            DisplayModeSettings settings = new DisplayModeSettings(path);
            DisplayModePreference preference = settings.Load();
            preference.SetDisplayMode(UsageDisplayMode.Classic);
            preference.SetPetAppearance(PetAppearance.PhoebeChibi);
            preference.SetPetUsageDisplayMode(
                PetUsageDisplayMode.Remaining);
            preference.SetPetScalePercent(137);
            Check(settings.TrySave(preference),
                "settings reports a successful persisted save");

            DisplayModePreference restored = settings.Load();
            Check(restored.GetDisplayMode() == UsageDisplayMode.Classic &&
                restored.GetPetAppearance() == PetAppearance.PhoebeChibi &&
                restored.GetPetUsageDisplayMode() ==
                    PetUsageDisplayMode.Remaining &&
                restored.GetPetScalePercent() == 137,
                "central settings round-trip through the existing format");
            Check(!settings.TrySave(null),
                "null settings cannot be reported as saved");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }

    private static DiagnosticsSnapshot CreateDiagnostics()
    {
        return new DiagnosticsSnapshot
        {
            GeneratedAtUtc = DateTime.UtcNow,
            AppVersion = "v1.1.2",
            OperatingSystem = "Windows QA",
            ProcessArchitecture = "64 位",
            ExecutablePath = @"C:\Program Files\UsagePeek\UsagePeek.exe",
            DisplayCount = 2,
            CurrentDpi = 144,
            DisplayMode = "桌宠模式",
            PetAppearance = "鲸鱼娘趴趴",
            PetUsageMode = "显示已用百分比",
            PetScalePercent = 100,
            StartupEnabled = true,
            UpdateConfigured = true,
            CodexStatus = "已找到本地组件",
            CodexCandidateCount = 2,
            CodexSource = "Codex 独立安装",
            CodexPath = @"C:\Program Files\OpenAI\codex.exe",
            UsageStatus = "实时数据",
            ProviderName = "ChatGPT / Codex",
            SnapshotFetchedAtUtc = DateTime.UtcNow,
            LastRefreshAttemptUtc = DateTime.UtcNow,
            LastRefreshSuccessUtc = DateTime.UtcNow,
            LocalFilesScanned = 12,
            PriceCoverage = "完整（已扫描模型均可计价）"
        };
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + name);
            return;
        }
        failures++;
        Console.Error.WriteLine("FAIL " + name);
    }
}
