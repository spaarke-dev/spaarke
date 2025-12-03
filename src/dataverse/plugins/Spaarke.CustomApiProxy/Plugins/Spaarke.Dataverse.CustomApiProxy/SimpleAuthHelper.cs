using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Simple OAuth2 helper that uses plain HTTP without Azure.Identity dependencies.
    /// This eliminates the need for ILMerge and complex dependency management.
    /// </summary>
    public static class SimpleAuthHelper
    {
        /// <summary>
        /// Get OAuth2 access token using client credentials flow.
        /// </summary>
        public static string GetClientCredentialsToken(string tenantId, string clientId, string clientSecret, string scope)
        {
            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

            var postData = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "scope", scope },
                { "grant_type", "client_credentials" }
            };

            var result = PostForm(tokenUrl, postData);
            var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(result);

            if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
            {
                throw new InvalidOperationException("Failed to acquire access token. Response did not contain access_token.");
            }

            return tokenResponse.AccessToken;
        }

        private static string PostForm(string url, Dictionary<string, string> formData)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.Timeout = 30000; // 30 seconds

            // Build form body
            var formBody = new StringBuilder();
            foreach (var kvp in formData)
            {
                if (formBody.Length > 0)
                    formBody.Append("&");

                formBody.Append(Uri.EscapeDataString(kvp.Key));
                formBody.Append("=");
                formBody.Append(Uri.EscapeDataString(kvp.Value));
            }

            var bodyBytes = Encoding.UTF8.GetBytes(formBody.ToString());
            request.ContentLength = bodyBytes.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return reader.ReadToEnd();
            }
        }

        private class TokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonProperty("token_type")]
            public string TokenType { get; set; }
        }
    }
}
