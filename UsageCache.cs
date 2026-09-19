using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class UsageCache
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly string cachePath;

        public UsageCache()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            cachePath = Path.Combine(root, "UsagePeek", "cache.json");
        }

        public UsageSnapshot Load()
        {
            try
            {
                if (!File.Exists(cachePath))
                {
                    return null;
                }

                UsageSnapshot snapshot = serializer.Deserialize<UsageSnapshot>(
                    File.ReadAllText(cachePath, Encoding.UTF8));
                if (snapshot != null)
                {
                    snapshot.IsStale = true;
                }

                return snapshot;
            }
            catch
            {
                return null;
            }
        }

        public void Save(UsageSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(cachePath);
                Directory.CreateDirectory(directory);
                File.WriteAllText(cachePath, serializer.Serialize(snapshot), Encoding.UTF8);
            }
            catch
            {
                // A cache failure must never prevent fresh usage from being displayed.
            }
        }
    }
}
