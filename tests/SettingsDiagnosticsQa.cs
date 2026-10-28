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
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        VerifySafeReport();
        VerifySettingsSelection();
        VerifyTrySave();
        VerifySwitchRepaint();
        VerifyModernInputs();
        VerifySelectorPopupLifecycle();
        VerifyDiagnosticsOverview();

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
        Check(report.Contains(snapshot.ExecutableSha256),
            "report includes the executable SHA-256");
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
        Check(report.Contains("桌宠置顶: 已开启"),
            "diagnostics reports the optional pet topmost setting");
        Check(report.Contains("未包含登录令牌"),
            "report states its privacy boundary");
        snapshot.LastRefreshError = "UP-CX-006 Authorization: Bearer DO_NOT_COPY";
        report = snapshot.BuildReport();
        Check(report.Contains("UP-CX-006 连接 Codex 超时") && !report.Contains("DO_NOT_COPY"),
            "recognized error codes cannot copy arbitrary secret-bearing payloads");
    }

    private static void VerifyDiagnosticsOverview()
    {
        DiagnosticsSnapshot snapshot = CreateDiagnostics();
        using (SettingsForm form = new SettingsForm(null, delegate { return snapshot; }))
        {
            form.ShowDiagnosticsForTesting();
            Check(!form.DiagnosticsDetailsExpandedForTesting &&
                form.DiagnosticsOverviewForTesting.Contains("实时用量已同步") &&
                form.DiagnosticsOverviewForTesting.Contains("完整覆盖"),
                "diagnostics opens with readable health checks instead of the raw report");
            snapshot.UsageStatus = "缓存数据";
            snapshot.HasCompletePriceCoverage = false;
            snapshot.PriceCoveragePercent = 82;
            snapshot.UnpricedModelCount = 2;
            form.RefreshDiagnostics();
            Check(form.DiagnosticsOverviewForTesting.Contains("缓存数据") &&
                form.DiagnosticsOverviewForTesting.Contains("已覆盖 82%") &&
                form.DiagnosticsOverviewForTesting.Contains("2 个模型待补价"),
                "cached data and incomplete pricing remain distinct diagnostic results");
            form.ShowDiagnosticsDetailsForTesting(true);
            Check(form.DiagnosticsDetailsExpandedForTesting &&
                form.DiagnosticsTextForTesting.Contains("[Codex 连接]") &&
                form.DiagnosticsTextForTesting.Contains("EXE SHA-256:"),
                "technical details preserve the full shareable report");
            form.ShowDiagnosticsDetailsForTesting(false);
            Check(!form.DiagnosticsDetailsExpandedForTesting,
                "technical details return to the health overview");
            snapshot.UsageStatus = "离线";
            snapshot.LastRefreshError = "UP-CX-004 Codex 登录状态不可用";
            snapshot.HasLocalUsage = false;
            snapshot.CodexCandidateCount = 0;
            form.RefreshDiagnostics();
            Check(form.DiagnosticsOverviewForTesting.Contains("请先完成 Codex 登录") &&
                form.DiagnosticsOverviewForTesting.Contains("尚无统计"),
                "offline diagnostics suggests the next step without claiming pricing failure");
        }
        using (SettingsForm unavailable = new SettingsForm(null, delegate
        {
            throw new InvalidOperationException("DO_NOT_COPY private provider details");
        }))
        {
            unavailable.ShowDiagnosticsForTesting();
            Check(unavailable.DiagnosticsOverviewForTesting.Contains("暂时无法检查") &&
                !unavailable.DiagnosticsTextForTesting.Contains("DO_NOT_COPY"),
                "provider failure produces a readable safe state without an error dialog");
        }
    }

    private static void VerifySwitchRepaint()
    {
        using (SwitchCheckBox control = new SwitchCheckBox())
        using (Bitmap reused = new Bitmap(354, 30))
        using (Bitmap fresh = new Bitmap(354, 30))
        using (Graphics previousFrame = Graphics.FromImage(reused))
        using (Graphics cleanFrame = Graphics.FromImage(fresh))
        {
            control.Size = reused.Size;
            previousFrame.Clear(UiColors.Surface);
            cleanFrame.Clear(UiColors.Surface);
            System.Reflection.MethodInfo paint = typeof(SwitchCheckBox).GetMethod(
                "OnPaint", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Rectangle bounds = new Rectangle(Point.Empty, reused.Size);
            control.Text = "登录 Windows 后自动启动 UsagePeek";
            control.Checked = true;
            paint.Invoke(control, new object[] { new PaintEventArgs(previousFrame, bounds) });
            control.Text = "桌宠保持置顶";
            control.Checked = false;
            paint.Invoke(control, new object[] { new PaintEventArgs(previousFrame, bounds) });
            paint.Invoke(control, new object[] { new PaintEventArgs(cleanFrame, bounds) });
            bool equal = true;
            for (int y = 0; y < reused.Height && equal; y++)
            {
                for (int x = 0; x < reused.Width; x++)
                {
                    if (reused.GetPixel(x, y) != fresh.GetPixel(x, y))
                    {
                        equal = false;
                        break;
                    }
                }
            }
            Check(equal, "switch repaint clears the previous label and knob without ghosting");
        }
    }

    private static void VerifyModernInputs()
    {
        using (ModernSelectControl select = new ModernSelectControl())
        {
            select.Items.AddRange(new object[] { "跟随系统", "浅色模式", "深色模式" });
            int changes = 0;
            select.SelectedIndexChanged += delegate { changes++; };
            select.SelectedIndex = 0;
            select.SelectedIndex = 0;
            select.SelectedIndex = 2;
            Check(changes == 2 && select.Text == "深色模式",
                "modern selector raises one event for each actual change");
            select.SelectedIndex = 0;
            int beforeKey = changes;
            typeof(ModernSelectControl).GetMethod("OnKeyDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(select, new object[] { new KeyEventArgs(Keys.Up) });
            Check(changes == beforeKey && select.SelectedIndex == 0,
                "up key at the first choice does not apply a blank selection");
            Check(select.AccessibilityObject.Value == "跟随系统",
                "modern selector exposes its selected value to accessibility");
        }
        using (ModernNumberControl number = new ModernNumberControl())
        {
            int changes = 0;
            number.ValueChanged += delegate { changes++; };
            number.Step(1);
            Check(number.Value == 105 && changes == 1,
                "modern size control applies a single five-percent step");
            number.Value = 1000;
            number.Step(1);
            Check(number.Value == 200 && changes == 2,
                "modern size control clamps at 200 percent without duplicate changes");
            number.Value = 0;
            number.Step(-1);
            Check(number.Value == 50 && changes == 3,
                "modern size control clamps at 50 percent");
        }
    }

    private static void VerifySelectorPopupLifecycle()
    {
        UsageThemeMode original = UiTheme.Mode;
        try
        {
            using (DpiAwareForm form = new DpiAwareForm())
            using (ModernSelectControl select = new ModernSelectControl())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-10000, -10000);
                select.Items.AddRange(new object[] { "跟随系统", "浅色模式", "深色模式" });
                form.Controls.Add(select);
                form.Show();
                System.Reflection.MethodInfo open = typeof(ModernSelectControl).GetMethod(
                    "OpenList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                System.Reflection.FieldInfo popup = typeof(ModernSelectControl).GetField(
                    "popup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                for (int pass = 0; pass < 9; pass++)
                {
                    open.Invoke(select, null);
                    ContextMenuStrip menu = (ContextMenuStrip)popup.GetValue(select);
                    menu.Items[pass % 3].PerformClick();
                    menu.Close(ToolStripDropDownCloseReason.ItemClicked);
                    Application.DoEvents();
                    Check(!menu.IsDisposed && select.SelectedIndex == pass % 3,
                        "selector popup survives selection and close cycle " + pass);
                    UiTheme.SetMode(pass % 2 == 0 ? UsageThemeMode.Dark : UsageThemeMode.Light);
                }
                form.Hide();
            }
        }
        finally { UiTheme.SetMode(original); }
    }

    private static void VerifySettingsSelection()
    {
        SettingsSelection initial = new SettingsSelection
        {
            DisplayMode = UsageDisplayMode.Classic,
            PetAppearance = PetAppearance.PhoebeChibi,
            PetUsageDisplayMode = PetUsageDisplayMode.Remaining,
            PetScalePercent = 137,
            PetAlwaysOnTop = true,
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
            Check(selected.PetAlwaysOnTop,
                "settings keeps the optional pet topmost choice");
            Check(selected.StartupEnabled,
                "settings keeps startup choice");
            Check(!form.TopMost,
                "the settings window is not forced above other apps");
            form.PrepareForDisplay();
            Check(form.VisibleControlsReadyForTesting,
                "settings controls are created before the window is shown");
            Check(form.UsesCompositedPaintingForTesting,
                "settings window composes child controls into one frame");
            form.PrepareForDisplay();
            Check(form.VisibleControlsReadyForTesting,
                "preparing an already warmed settings window is harmless");

            form.SelectDisplayModeForTesting(UsageDisplayMode.Pet);
            Check(settingsChanges == 1 &&
                    form.Selection.DisplayMode == UsageDisplayMode.Pet,
                "changing an option immediately raises an apply request");
            form.SetSelection(initial);
            Check(settingsChanges == 1 &&
                    form.Selection.DisplayMode == UsageDisplayMode.Classic,
                "restoring a failed choice does not raise another request");
            form.SetPetAlwaysOnTopForTesting(false);
            Check(settingsChanges == 2 &&
                    !form.Selection.PetAlwaysOnTop,
                "changing pet topmost applies immediately");
            form.SetSelection(initial);
            Check(settingsChanges == 2 &&
                    form.Selection.PetAlwaysOnTop,
                "restoring pet topmost does not raise another request");
            form.SetPetAlwaysOnTopState(false);
            Check(settingsChanges == 2 &&
                    !form.Selection.PetAlwaysOnTop,
                "external topmost changes synchronize without looping");
            form.SetPetAlwaysOnTopState(true);

            int beforeTheme = settingsChanges;
            form.SelectThemeForTesting(UsageThemeMode.Dark);
            Check(settingsChanges == beforeTheme + 1 &&
                form.Selection.ThemeMode == UsageThemeMode.Dark,
                "theme choice immediately participates in auto-save");

            form.ShowDiagnosticsForTesting();
            Check(form.DiagnosticsTextForTesting.Contains(
                    "UsagePeek 诊断信息"),
                "diagnostics page builds its report locally");
            Check(form.DiagnosticsPageVisibleForTesting,
                "diagnostics navigation changes the visible page");

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-10000, -10000);
            bool firstClose = ShowAndHide(form, true);
            bool secondClose = ShowAndHide(form, false);
            Check(firstClose,
                "the first warmed settings window hides normally");
            Check(secondClose,
                "the reused settings window hides normally");
            Check(!form.IsDisposed,
                "closing the reusable settings dialog keeps it alive");
            Check(form.VisibleControlsReadyForTesting,
                "the reused settings dialog keeps its prepared controls");
        }

        initial.PetAppearance = PetAppearance.ChatGptGirl;
        using (SettingsForm form = new SettingsForm(initial, CreateDiagnostics))
        {
            Check(form.Selection.PetAppearance == PetAppearance.ChatGptGirl,
                "settings opens with the ChatGPT girl selected");
            initial.PetAppearance = PetAppearance.PhoebeChibi;
            form.SetSelection(initial);
            Check(form.Selection.PetAppearance == PetAppearance.PhoebeChibi,
                "settings can restore an existing pet after the new selection");
            initial.PetAppearance = PetAppearance.ChatGptGirl;
            form.SetSelection(initial);
            Check(form.Selection.PetAppearance == PetAppearance.ChatGptGirl,
                "the reusable settings dialog retains the ChatGPT girl selection");
        }
        foreach (PetAppearance appearance in new[]
        {
            PetAppearance.ClaudeGirl, PetAppearance.MilkFrog
        })
        {
            initial.PetAppearance = appearance;
            using (SettingsForm form = new SettingsForm(initial, CreateDiagnostics))
            {
                Check(form.Selection.PetAppearance == appearance,
                    "settings retains the new appearance: " + appearance);
            }
        }
    }

    private static bool ShowAndHide(SettingsForm form, bool useClose)
    {
        form.Show();
        Application.DoEvents();
        if (useClose)
        {
            form.Close();
        }
        else
        {
            form.DismissForTesting();
        }
        Application.DoEvents();
        return !form.Visible;
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
            Check(preference.GetThemeMode() == UsageThemeMode.System,
                "preferences without a theme follow the system by default");
            preference.SetDisplayMode(UsageDisplayMode.Classic);
            preference.SetPetAppearance(PetAppearance.PhoebeChibi);
            preference.SetPetUsageDisplayMode(
                PetUsageDisplayMode.Remaining);
            preference.SetPetScalePercent(137);
            preference.SetPetAlwaysOnTop(true);
            preference.SetThemeMode(UsageThemeMode.Dark);
            Check(settings.TrySave(preference),
                "settings reports a successful persisted save");

            DisplayModePreference restored = settings.Load();
            Check(restored.GetDisplayMode() == UsageDisplayMode.Classic &&
                restored.GetPetAppearance() == PetAppearance.PhoebeChibi &&
                restored.GetPetUsageDisplayMode() ==
                    PetUsageDisplayMode.Remaining &&
                restored.GetPetScalePercent() == 137 &&
                restored.GetPetAlwaysOnTop() && restored.GetThemeMode() == UsageThemeMode.Dark,
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
            AppVersion = "v2.0.0",
            OperatingSystem = "Windows QA",
            ProcessArchitecture = "64 位",
            ExecutablePath = @"C:\Program Files\UsagePeek\UsagePeek.exe",
            ExecutableSha256 =
                "0123456789ABCDEF0123456789ABCDEF" +
                "0123456789ABCDEF0123456789ABCDEF",
            DisplayCount = 2,
            CurrentDpi = 144,
            DisplayMode = "桌宠模式",
            PetAppearance = "鲸鱼娘趴趴",
            PetUsageMode = "显示已用百分比",
            PetScalePercent = 100,
            PetAlwaysOnTop = true,
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
            PriceCoverage = "完整（已扫描模型均可计价）",
            HasLocalUsage = true,
            HasCompletePriceCoverage = true,
            PriceCoveragePercent = 100,
            UnpricedModelCount = 0
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
