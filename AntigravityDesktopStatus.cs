using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal static class AntigravityDesktopStatus
    {
        internal const int MaximumReplyLength = 1024 * 1024;
        private const int TimeoutMilliseconds = 2200;
        private const string ServicePath = "/exa.language_server_pb.LanguageServerService/";
        private const string UserMetadata = "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\"}}";

        public static SourceQuotaSnapshot Read(CancellationToken cancellation)
        {
            foreach (DesktopEndpoint endpoint in FindEndpoints(cancellation))
            {
                string user = Query(endpoint.Port, endpoint.Csrf, "GetUserStatus", UserMetadata, cancellation);
                string quotas = Query(endpoint.Port, endpoint.Csrf, "RetrieveUserQuotaSummary",
                    "{\"request\":{},\"forceRefresh\":false}", cancellation);
                SourceQuotaSnapshot snapshot = SourceQuotaStatusLine.ParseAntigravityDesktop(user, quotas, DateTime.UtcNow);
                if (snapshot != null) return snapshot;
            }
            return null;
        }

        public static AntigravityCreditsSnapshot ReadCredits(CancellationToken cancellation)
        {
            foreach (DesktopEndpoint endpoint in FindEndpoints(cancellation))
            {
                string response = Query(endpoint.Port, endpoint.Csrf, "GetLoadCodeAssist", UserMetadata, cancellation);
                AntigravityCreditsSnapshot snapshot = SourceQuotaStatusLine.ParseAntigravityDesktopCredits(response, DateTime.UtcNow);
                if (snapshot != null) return snapshot;
            }
            return null;
        }

        private static IEnumerable<DesktopEndpoint> FindEndpoints(CancellationToken cancellation)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".gemini", "antigravity", "daemon");
            if (!Directory.Exists(directory)) yield break;

            int session;
            using (Process self = Process.GetCurrentProcess()) session = self.SessionId;
            Dictionary<int, int> listeners = ReadListenerOwners();
            foreach (KeyValuePair<int, int> listener in listeners)
            {
                cancellation.ThrowIfCancellationRequested();
                string file = Path.Combine(directory, "hub_" + listener.Key.ToString(CultureInfo.InvariantCulture) + ".json");
                if (!File.Exists(file) || !IsDesktopServer(listener.Value, session)) continue;

                IDictionary<string, object> descriptor = ReadDescriptor(file);
                int port = ReadInt(descriptor, "httpPort");
                int owner;
                if (ReadInt(descriptor, "pid") != listener.Value || port < 1 || port > 65535 ||
                    !listeners.TryGetValue(port, out owner) || owner != listener.Value) continue;

                // The desktop's short-lived channel credential stays in memory and is sent only to its listener.
                string csrf = AdditionalUsageReader.ReadText(descriptor, "csrfToken");
                if (string.IsNullOrWhiteSpace(csrf) || csrf.Length > 256 || csrf.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    continue;

                yield return new DesktopEndpoint { Port = port, Csrf = csrf };
            }
        }

        private struct DesktopEndpoint
        {
            public int Port;
            public string Csrf;
        }

        private static IDictionary<string, object> ReadDescriptor(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    if (stream.Length > 8192) return null;
                    return AdditionalUsageReader.ParseObject(new JavaScriptSerializer { MaxJsonLength = 8192 }, reader.ReadToEnd());
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static int ReadInt(IDictionary<string, object> data, string key)
        {
            decimal? number = AdditionalUsageReader.ReadNumber(data, key);
            return number > 0 && number <= int.MaxValue && number.Value == decimal.Truncate(number.Value) ? (int)number.Value : 0;
        }

        private static bool IsDesktopServer(int pid, int session)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    return process.SessionId == session && string.Equals(process.ProcessName, "language_server", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
        }

        private static string Query(int port, string csrf, string method, string body, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" +
                port.ToString(CultureInfo.InvariantCulture) + ServicePath + method);
            request.Method = "POST";
            request.Proxy = null;
            request.AllowAutoRedirect = false;
            request.KeepAlive = false;
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.ContentType = "application/json";
            request.Headers["Connect-Protocol-Version"] = "1";
            request.Headers["X-Codeium-Csrf-Token"] = csrf;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;
            using (cancellation.Register(request.Abort))
            {
                try
                {
                    using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > MaximumReplyLength) return null;
                        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            var buffer = new char[MaximumReplyLength + 1];
                            int length = reader.ReadBlock(buffer, 0, buffer.Length);
                            cancellation.ThrowIfCancellationRequested();
                            return length <= MaximumReplyLength ? new string(buffer, 0, length) : null;
                        }
                    }
                }
                catch (WebException error)
                {
                    if (error.Response != null) error.Response.Close();
                    cancellation.ThrowIfCancellationRequested();
                    return null;
                }
                catch (IOException) { cancellation.ThrowIfCancellationRequested(); return null; }
            }
        }

        private static Dictionary<int, int> ReadListenerOwners()
        {
            const uint ipv4 = 2;
            const int tcpTableOwnerPidListener = 3;
            const int rowSize = 24;
            int size = 0;
            var result = new Dictionary<int, int>();
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, ipv4, tcpTableOwnerPidListener, 0);
            if (size < sizeof(int) || size > 1024 * 1024) return result;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, ipv4, tcpTableOwnerPidListener, 0) != 0) return result;
                int count = Marshal.ReadInt32(buffer);
                int loopback = BitConverter.ToInt32(IPAddress.Loopback.GetAddressBytes(), 0);
                for (int index = 0; index < count && index < (size - sizeof(int)) / rowSize; index++)
                {
                    int offset = sizeof(int) + index * rowSize;
                    int address = Marshal.ReadInt32(buffer, offset + 4);
                    if (address != loopback && address != 0) continue;
                    int port = (ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt32(buffer, offset + 8));
                    int owner = Marshal.ReadInt32(buffer, offset + 20);
                    // A bind on 127.0.0.1 takes precedence over a wildcard bind on the same port.
                    if (address == loopback || !result.ContainsKey(port)) result[port] = owner;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return result;
        }

        [DllImport("iphlpapi.dll")]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sorted,
            uint addressFamily, int tableClass, uint reserved);
    }
}
