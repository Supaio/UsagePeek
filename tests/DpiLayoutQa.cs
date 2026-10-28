using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using UsagePeek;

internal static class DpiLayoutQa
{
    private static int failures;

    [STAThread]
    private static int Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        VerifyMainForm();
        VerifyModelForm();
        VerifyAboutForm();
        VerifySettingsForm();
        VerifyPetSizeDialog();
        VerifyPetForm();
        VerifySharedFonts();
        VerifyAccessibleButtons();
        VerifyThemeChanges();

        if (failures > 0)
        {
            Console.Error.WriteLine("DPI layout QA failed: " + failures);
            return 1;
        }

        Console.WriteLine("DPI layout QA passed.");
        return 0;
    }

    private static void VerifyMainForm()
    {
        using (MainForm form = new MainForm())
        {
            AssertEqual(false, form.TopMost,
                "main form is not forced above other apps");
            int settingsRequests = 0;
            form.SettingsRequested += delegate { settingsRequests++; };
            form.RequestSettingsForTesting();
            AssertEqual(1, settingsRequests,
                "main form exposes the centralized settings action");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(642, 1062), form.ClientSize,
                "main form scales to 150 percent");

            UsageCardControl card = FindControl<UsageCardControl>(form);
            AssertEqual(new Rectangle(33, 204, 279, 261), card.Bounds,
                "main usage card geometry scales to 150 percent");

            form.ApplyDpiForTesting(192);
            AssertEqual(new Size(856, 1416), form.ClientSize,
                "main form scales to 200 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(428, 708), form.ClientSize,
                "main form returns exactly to 100 percent");
            AssertEqual(new Rectangle(22, 136, 186, 174), card.Bounds,
                "main usage card returns without rounding drift");

            IntPtr unused = form.Handle;
            form.ApplyDpiForTesting(192);
            form.FitToWorkingAreaForTesting(new Rectangle(-1000, 0, 1000, 800));
            AssertEqual(true, form.Width <= 984 && form.Height <= 784,
                "large popup fits within a small monitor working area (window " +
                form.Size + ", client " + form.ClientSize + ")");
            AssertEqual(true, form.AutoScrollMinSize.Height >= 1416,
                "content below a scaled popup remains available by scrolling");
            form.ApplyDpiForTesting(96);
            AssertEqual(Size.Empty, form.AutoScrollMinSize,
                "normal DPI clears the temporary scroll extent");
        }
    }

    private static void VerifyModelForm()
    {
        UsageSnapshot snapshot = new UsageSnapshot();
        snapshot.LocalTokenUsage = new LocalTokenUsageSnapshot();
        snapshot.LocalTokenUsage.ModelUsage =
            new List<ModelTokenUsageSnapshot>
            {
                new ModelTokenUsageSnapshot
                {
                    Model = "gpt-6-astra",
                    TotalTokens = 75,
                    Percentage = 75d
                },
                new ModelTokenUsageSnapshot
                {
                    Model = "gpt-6-sol",
                    TotalTokens = 25,
                    Percentage = 25d
                }
            };

        using (ModelUsageForm form = new ModelUsageForm(snapshot))
        {
            AssertEqual(false, form.TopMost,
                "model usage form is not forced above other apps");
            Size designSize = form.ClientSize;
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(
                Scale(designSize.Width, 1.5f),
                Scale(designSize.Height, 1.5f)), form.ClientSize,
                "model form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "model form returns exactly to 100 percent");
        }
    }

    private static void VerifyPetForm()
    {
        using (PetForm form = new PetForm())
        {
            AssertEqual(false, form.TopMost,
                "pet form is not forced above other apps by default");
            form.SetAlwaysOnTop(true);
            AssertEqual(true, form.TopMost,
                "pet form honors the optional topmost setting");
            form.SetAlwaysOnTop(false);
            Size designSize = form.ClientSize;
            AssertEqual(new Size(220, 310), designSize,
                "compact pet form reserves space for the bubble and hint");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(330, 465), form.ClientSize,
                "pet form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "pet form returns exactly to 100 percent");

            form.SetScalePercent(75);
            AssertEqual(new Size(165, 233), form.ClientSize,
                "pet form supports a 75 percent custom size");
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(248, 349), form.ClientSize,
                "custom pet size combines with 150 percent display DPI");
            form.SetScalePercent(137);
            AssertEqual(new Size(452, 637), form.ClientSize,
                "arbitrary pet size remains correct at 150 percent DPI");
            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(301, 425), form.ClientSize,
                "arbitrary pet size returns correctly to 100 percent DPI");
        }
    }

    private static void VerifyPetSizeDialog()
    {
        using (PetSizeDialog form = new PetSizeDialog(137))
        {
            AssertEqual(false, form.TopMost,
                "custom size dialog is not forced above other apps");
            AssertEqual(137, form.SelectedPercent,
                "custom size dialog keeps the requested percentage");
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(480, 264), form.ClientSize,
                "custom size dialog scales to 150 percent");
        }
    }

    private static void VerifyAboutForm()
    {
        using (AboutForm form = new AboutForm())
        {
            AssertEqual(false, form.TopMost,
                "about form is not forced above other apps");
            Size designSize = form.ClientSize;
            AssertEqual(new Size(390, 370), designSize,
                "about form uses the expected design size");
            AssertEqual("产品构思、用量展示与交互思路",
                AboutForm.OpenUsageCredit,
                "about form credits OpenUsage ideas");
            AssertEqual("鲸鱼娘形象 · ZipZipPipe",
                AboutForm.CharacterCredit,
                "about form credits the character artist");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(585, 555), form.ClientSize,
                "about form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "about form returns exactly to 100 percent");
        }
    }

    private static void VerifySettingsForm()
    {
        SettingsSelection initial = new SettingsSelection
        {
            DisplayMode = UsageDisplayMode.Pet,
            PetAppearance = PetAppearance.WhaleMaid,
            PetUsageDisplayMode = PetUsageDisplayMode.Used,
            PetScalePercent = 100,
            PetAlwaysOnTop = false,
            StartupEnabled = false
        };
        using (SettingsForm form = new SettingsForm(initial, delegate
        {
            return new DiagnosticsSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                AppVersion = "v2.0.0",
                OperatingSystem = "Windows QA",
                ProcessArchitecture = "64 位",
                DisplayCount = 1,
                CurrentDpi = 96,
                DisplayMode = "桌宠模式",
                PetAppearance = "鲸鱼娘趴趴",
                PetUsageMode = "显示已用百分比",
                PetScalePercent = 100,
                CodexStatus = "未找到本地组件",
                UsageStatus = "尚未读取",
                PriceCoverage = "尚无本地统计"
            };
        }))
        {
            AssertEqual(false, form.TopMost,
                "settings form is not forced above other apps");
            AssertEqual(new Size(560, 530), form.ClientSize,
                "settings form uses the expected design size");
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(840, 795), form.ClientSize,
                "settings form scales to 150 percent");
            form.ApplyDpiForTesting(192);
            AssertEqual(new Size(1120, 1060), form.ClientSize,
                "settings form scales to 200 percent");
            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(560, 530), form.ClientSize,
                "settings form returns exactly to 100 percent");
        }
    }

    private static void VerifySharedFonts()
    {
        using (Font shared = new Font("Segoe UI", 10f))
        using (NativeFontForm form = new NativeFontForm(shared))
        {
            IntPtr unused = form.Handle;
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (int dpi in new[] { 96, 144, 192, 96 })
                {
                    form.ApplyDpiForTesting(dpi);
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap,
                            new Rectangle(Point.Empty, bitmap.Size));
                    }
                }
            }
            AssertEqual(true, shared.GetHeight() > 0,
                "repeated DPI changes do not dispose a shared input font");
            form.ApplyDpiForTesting(144);
            AssertEqual(true, Math.Abs(form.Input.Font.SizeInPoints -
                10f * 144f / DpiNative.GetSystemDpi()) < 0.1f,
                "native dropdown font follows monitor DPI");
        }
    }

    private static void VerifyAccessibleButtons()
    {
        using (GlyphButtonControl button = new GlyphButtonControl("测试操作", false))
        {
            int clicks = 0;
            button.Click += delegate { clicks++; };
            button.AccessibilityObject.DoDefaultAction();
            Application.DoEvents();
            AssertEqual(1, clicks, "accessible button action invokes its command");
            button.Enabled = false;
            button.AccessibilityObject.DoDefaultAction();
            Application.DoEvents();
            AssertEqual(1, clicks, "disabled button ignores accessible invocation");
            AssertEqual(true, button.TabStop, "toolbar buttons support keyboard navigation");
        }
    }

    private static void VerifyThemeChanges()
    {
        UsageThemeMode original = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(UsageThemeMode.Light);
            using (MainForm form = new MainForm())
            using (SettingsForm settings = new SettingsForm(null, null))
            {
                Color day = form.BackColor;
                for (int pass = 0; pass < 3; pass++)
                {
                    UiTheme.SetMode(UsageThemeMode.Dark);
                    AssertEqual(UiColors.Background, form.BackColor,
                        "existing main window follows dark theme");
                    AssertEqual(UiColors.Background, settings.BackColor,
                        "existing settings window follows dark theme");
                    UsageCardControl card = FindControl<UsageCardControl>(form);
                    using (Bitmap bitmap = new Bitmap(card.Width, card.Height))
                    {
                        card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        AssertEqual(UiColors.Surface.ToArgb(), bitmap.GetPixel(8, 80).ToArgb(),
                            "usage card paints the dark surface rather than a white box");
                    }
                    UiTheme.SetMode(UsageThemeMode.Light);
                    AssertEqual(day, form.BackColor,
                        "switching back restores the original light palette");
                }
            }
            UiTheme.SetMode(UsageThemeMode.Light);
            UiTheme.UpdateSystemTheme(true);
            AssertEqual(false, UiTheme.IsDark, "fixed light theme ignores a dark system");
            UiTheme.SetMode(UsageThemeMode.Dark);
            UiTheme.UpdateSystemTheme(false);
            AssertEqual(true, UiTheme.IsDark, "fixed dark theme ignores a light system");
            UiTheme.SetMode(UsageThemeMode.System);
            UiTheme.UpdateSystemTheme(true);
            AssertEqual(true, UiTheme.IsDark, "system theme tracks dark mode changes");
            UiTheme.UpdateSystemTheme(false);
            AssertEqual(false, UiTheme.IsDark, "system theme tracks light mode changes");
        }
        finally { UiTheme.SetMode(original); }
    }

    private sealed class NativeFontForm : DpiAwareForm
    {
        public readonly ComboBox Input;

        public NativeFontForm(Font shared)
        {
            ClientSize = new Size(400, 200);
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(new Label { Font = shared, Text = "Shared A",
                Location = new Point(10, 10), Size = new Size(120, 24) });
            Controls.Add(new Label { Font = shared, Text = "Shared B",
                Location = new Point(150, 10), Size = new Size(120, 24) });
            Input = new ComboBox { Font = shared,
                Location = new Point(10, 50), Size = new Size(170, 28) };
            Input.Items.Add("Native dropdown");
            Input.SelectedIndex = 0;
            Controls.Add(Input);
            Controls.Add(new NumericUpDown { Font = shared,
                Location = new Point(200, 50), Size = new Size(100, 28) });
            Controls.Add(new Button { Font = shared, Text = "Native button",
                Location = new Point(10, 100), Size = new Size(160, 32) });
            InitializeDpiLayout();
        }
    }

    private static T FindControl<T>(Control root) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            T match = control as T;
            if (match != null)
            {
                return match;
            }
            if (control.HasChildren)
            {
                match = FindControl<T>(control);
                if (match != null)
                {
                    return match;
                }
            }
        }
        return null;
    }

    private static int Scale(int value, float factor)
    {
        return (int)Math.Round(value * factor,
            MidpointRounding.AwayFromZero);
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (object.Equals(expected, actual))
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        failures++;
        Console.Error.WriteLine("FAIL " + name +
            " expected=" + expected + " actual=" + actual);
    }
}
