using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using UsagePeek;

internal static class WinExeProviderQa
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = args.Length > 0
            ? args[0]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "WinExeProviderQa.result.json");
        IDictionary<string, object> result = new Dictionary<string, object>();

        try
        {
            UsageSnapshot snapshot = new CodexUsageProvider()
                .FetchAsync().GetAwaiter().GetResult();
            result["success"] = true;
            result["provider"] = snapshot.ProviderId;
            result["plan"] = snapshot.PlanType;
            result["primary"] = snapshot.Primary == null
                ? (object)null : snapshot.Primary.UsedPercent;
            result["secondary"] = snapshot.Secondary == null
                ? (object)null : snapshot.Secondary.UsedPercent;
        }
        catch (Exception ex)
        {
            result["success"] = false;
            result["error"] = Sanitize(ex.Message);
        }

        File.WriteAllText(output,
            new JavaScriptSerializer().Serialize(result));
    }

    private static string Sanitize(string value)
    {
        string profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            value = value.Replace(profile, "%USERPROFILE%");
        }

        return value.Length > 400 ? value.Substring(0, 400) : value;
    }
}
