using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal enum UsageDisplayMode
    {
        Classic,
        Pet
    }

    internal enum PetAppearance
    {
        WhaleMaid,
        PhoebeChibi
    }

    internal enum PetUsageDisplayMode
    {
        Used,
        Remaining
    }

    internal sealed class DisplayModePreference
    {
        internal const int MinimumPetScalePercent = 50;
        internal const int MaximumPetScalePercent = 200;

        public string Mode { get; set; }
        public int? PetLeft { get; set; }
        public int? PetTop { get; set; }
        public string PetAppearanceId { get; set; }
        public string PetUsageMode { get; set; }
        public int? PetScalePercent { get; set; }

        public UsageDisplayMode GetDisplayMode()
        {
            return string.Equals(Mode, "pet",
                StringComparison.OrdinalIgnoreCase)
                ? UsageDisplayMode.Pet
                : UsageDisplayMode.Classic;
        }

        public void SetDisplayMode(UsageDisplayMode value)
        {
            Mode = value == UsageDisplayMode.Pet ? "pet" : "classic";
        }

        public PetAppearance GetPetAppearance()
        {
            return string.Equals(PetAppearanceId, "phoebe-chibi",
                StringComparison.OrdinalIgnoreCase)
                ? PetAppearance.PhoebeChibi
                : PetAppearance.WhaleMaid;
        }

        public void SetPetAppearance(PetAppearance value)
        {
            PetAppearanceId = value == PetAppearance.PhoebeChibi
                ? "phoebe-chibi"
                : "whale-maid";
        }

        public PetUsageDisplayMode GetPetUsageDisplayMode()
        {
            return string.Equals(PetUsageMode, "remaining",
                StringComparison.OrdinalIgnoreCase)
                ? PetUsageDisplayMode.Remaining
                : PetUsageDisplayMode.Used;
        }

        public void SetPetUsageDisplayMode(PetUsageDisplayMode value)
        {
            PetUsageMode = value == PetUsageDisplayMode.Remaining
                ? "remaining"
                : "used";
        }

        public int GetPetScalePercent()
        {
            int value = PetScalePercent.HasValue
                ? PetScalePercent.Value
                : 100;
            return Math.Max(MinimumPetScalePercent,
                Math.Min(MaximumPetScalePercent, value));
        }

        public void SetPetScalePercent(int value)
        {
            PetScalePercent = Math.Max(MinimumPetScalePercent,
                Math.Min(MaximumPetScalePercent, value));
        }

        public Point? GetPetLocation()
        {
            if (!PetLeft.HasValue || !PetTop.HasValue)
            {
                return null;
            }

            return new Point(PetLeft.Value, PetTop.Value);
        }

        public void SetPetLocation(Point value)
        {
            PetLeft = value.X;
            PetTop = value.Y;
        }
    }

    internal sealed class DisplayModeSettings
    {
        private readonly JavaScriptSerializer serializer =
            new JavaScriptSerializer();
        private readonly string settingsPath;

        public DisplayModeSettings()
            : this(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "UsagePeek", "display.json"))
        {
        }

        internal DisplayModeSettings(string path)
        {
            settingsPath = path;
        }

        public DisplayModePreference Load()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    DisplayModePreference value =
                        serializer.Deserialize<DisplayModePreference>(
                            File.ReadAllText(settingsPath, Encoding.UTF8));
                    if (value != null)
                    {
                        value.SetDisplayMode(value.GetDisplayMode());
                        value.SetPetAppearance(value.GetPetAppearance());
                        value.SetPetUsageDisplayMode(
                            value.GetPetUsageDisplayMode());
                        value.SetPetScalePercent(
                            value.GetPetScalePercent());
                        return value;
                    }
                }
            }
            catch
            {
                // A damaged preference file must not prevent UsagePeek starting.
            }

            DisplayModePreference fallback = new DisplayModePreference();
            fallback.SetDisplayMode(UsageDisplayMode.Pet);
            fallback.SetPetAppearance(PetAppearance.WhaleMaid);
            fallback.SetPetUsageDisplayMode(PetUsageDisplayMode.Used);
            fallback.SetPetScalePercent(100);
            return fallback;
        }

        public void Save(DisplayModePreference value)
        {
            if (value == null)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(settingsPath,
                    serializer.Serialize(value), Encoding.UTF8);
            }
            catch
            {
                // Display preferences are optional and must never stop the app.
            }
        }
    }
}
