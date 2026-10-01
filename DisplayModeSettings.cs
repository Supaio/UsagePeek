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

    internal sealed class DisplayModePreference
    {
        public string Mode { get; set; }
        public int? PetLeft { get; set; }
        public int? PetTop { get; set; }

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
