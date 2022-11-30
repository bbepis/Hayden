using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hayden.WebServer.Services.Captcha
{
    public class HCaptchaProvider : ICaptchaProvider
    {
        public static readonly string DummySecret = "0x0000000000000000000000000000000000000000";
        public static readonly string DummySiteKey = "10000000-ffff-ffff-ffff-000000000001";

        protected string SiteKey { get; set; }
        protected string Secret { get; set; }

        public HCaptchaProvider(string sitekey, string secret)
        {
	        SiteKey = sitekey;
            Secret = secret ?? throw new ArgumentNullException(nameof(secret));
        }

        private static readonly HttpClient CaptchaHttpClient = new();

        public async Task<bool> VerifyCaptchaAsync(string response)
        {
            var formDictionary = new Dictionary<string, string>
            {
                ["response"] = response,
                ["secret"] = Secret
            };

            if (SiteKey != null)
                formDictionary["sitekey"] = SiteKey;

            var verifyResponse = await CaptchaHttpClient.PostAsync("https://hcaptcha.com/siteverify", new FormUrlEncodedContent(formDictionary));

            if (!verifyResponse.IsSuccessStatusCode)
                return false;

            var responseString = await verifyResponse.Content.ReadAsStringAsync();
            var jObject = JObject.Parse(responseString);

            return jObject.Value<bool>("success");
        }
    }
}
