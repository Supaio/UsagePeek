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
            return Read(cachePath) ?? Read(cachePath + ".bak");
        }

        private UsageSnapshot Read(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                UsageSnapshot snapshot;
                using (FileStream stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    snapshot = serializer.Deserialize<UsageSnapshot>(reader.ReadToEnd());
                if (snapshot != null)
                {
                    snapshot.NormalizeQuotaWindows();
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

            string temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(cachePath);
                Directory.CreateDirectory(directory);
                File.WriteAllText(temporaryPath, serializer.Serialize(snapshot), Encoding.UTF8);
                try
                {
                    if (File.Exists(cachePath)) File.Replace(temporaryPath, cachePath, null);
                    else File.Move(temporaryPath, cachePath);
                }
                catch (IOException) { CopyWithBackup(temporaryPath); }
                catch (UnauthorizedAccessException) { CopyWithBackup(temporaryPath); }
                catch (PlatformNotSupportedException) { CopyWithBackup(temporaryPath); }
            }
            catch
            {
                // A cache failure must never prevent fresh usage from being displayed.
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void CopyWithBackup(string temporaryPath)
        {
            // Some Windows profiles deny replacement of encrypted files but allow overwriting them.
            if (Read(cachePath) != null) File.Copy(cachePath, cachePath + ".bak", true);
            File.Copy(temporaryPath, cachePath, true);
        }
    }
}
