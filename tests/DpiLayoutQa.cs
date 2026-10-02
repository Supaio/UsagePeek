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
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        VerifyMainForm();
        VerifyModelForm();
        VerifyAboutForm();
        VerifyPetSizeDialog();
        VerifyPetForm();

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
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(642, 1062), form.ClientSize,
                "main form scales to 150 percent");

            UsageCardControl card = FindControl<UsageCardControl>(form);
            AssertEqual(new Rectangle(30, 257, 582, 168), card.Bounds,
                "main usage card geometry scales to 150 percent");

            form.ApplyDpiForTesting(192);
            AssertEqual(new Size(856, 1416), form.ClientSize,
                "main form scales to 200 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(428, 708), form.ClientSize,
                "main form returns exactly to 100 percent");
            AssertEqual(new Rectangle(20, 171, 388, 112), card.Bounds,
                "main usage card returns without rounding drift");
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
            Size designSize = form.ClientSize;
            AssertEqual(new Size(220, 272), designSize,
                "compact pet form reserves space for the usage bubble");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(330, 408), form.ClientSize,
                "pet form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "pet form returns exactly to 100 percent");

            form.SetScalePercent(75);
            AssertEqual(new Size(165, 204), form.ClientSize,
                "pet form supports a 75 percent custom size");
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(248, 306), form.ClientSize,
                "custom pet size combines with 150 percent display DPI");
            form.SetScalePercent(137);
            AssertEqual(new Size(452, 559), form.ClientSize,
                "arbitrary pet size remains correct at 150 percent DPI");
            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(301, 373), form.ClientSize,
                "arbitrary pet size returns correctly to 100 percent DPI");
        }
    }

    private static void VerifyPetSizeDialog()
    {
        using (PetSizeDialog form = new PetSizeDialog(137))
        {
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
            Size designSize = form.ClientSize;
            AssertEqual(new Size(390, 348), designSize,
                "about form uses the expected design size");
            AssertEqual("产品构思、用量展示与交互思路",
                AboutForm.OpenUsageCredit,
                "about form credits OpenUsage ideas");
            AssertEqual("鲸鱼娘形象 · ZipZipPipe",
                AboutForm.CharacterCredit,
                "about form credits the character artist");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(585, 522), form.ClientSize,
                "about form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "about form returns exactly to 100 percent");
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
