using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class ExchangeRateService
    {
        private const string Endpoint =
            "https://api.frankfurter.dev/v2/rate/usd/cny";
        private readonly JavaScriptSerializer serializer =
            new JavaScriptSerializer();

        public async Task<ExchangeRateSnapshot> FetchUsdToCnyAsync()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string json;
            using (WebClient client = new WebClient())
            {
                client.Encoding = System.Text.Encoding.UTF8;
                client.Headers[HttpRequestHeader.UserAgent] =
                    "UsagePeek/0.3.5 (+https://frankfurter.dev/)";
                json = await client.DownloadStringTaskAsync(new Uri(Endpoint));
            }

            IDictionary<string, object> data = serializer.DeserializeObject(json)
                as IDictionary<string, object>;
            if (data == null || !data.ContainsKey("rate"))
            {
                throw new InvalidOperationException("汇率服务返回了无法识别的数据。");
            }

            decimal rate;
            if (!decimal.TryParse(Convert.ToString(data["rate"],
                    CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out rate) || rate <= 0m)
            {
                throw new InvalidOperationException("汇率服务没有返回有效的人民币汇率。");
            }

            DateTime parsedDate;
            DateTime? rateDate = null;
            object dateValue;
            if (data.TryGetValue("date", out dateValue) && dateValue != null &&
                DateTime.TryParse(Convert.ToString(dateValue,
                        CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsedDate))
            {
                rateDate = parsedDate;
            }

            return new ExchangeRateSnapshot
            {
                UsdToCnyRate = rate,
                RateDateUtc = rateDate
            };
        }
    }
}
