using System;
using System.Net.Http;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json.Linq;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Custom API Plugin: sprk_GetFilePreviewUrl
    ///
    /// Thin server-side proxy that retrieves ephemeral SharePoint Embedded preview URLs
    /// via the SDAP BFF API. This eliminates client-side MSAL.js authentication complexity.
    ///
    /// Architecture:
    /// - Plugin validates inputs and generates correlation ID
    /// - Calls SDAP BFF API /preview-url endpoint (authenticated with app-only token)
    /// - BFF validates user access via Spaarke UAC
    /// - BFF calls Graph API with service principal (app-only)
    /// - Plugin returns ephemeral preview URL to client
    ///
    /// Custom API Registration:
    /// - Unique Name: sprk_GetFilePreviewUrl
    /// - Binding Type: Entity (sprk_document)
    /// - Is Function: Yes
    ///
    /// Input Parameters:
    /// - None (uses bound Document entity ID)
    ///
    /// Output Parameters:
    /// - PreviewUrl (String) - Ephemeral preview URL (expires ~10 min)
    /// - FileName (String) - File name for display
    /// - FileSize (Integer) - File size in bytes
    /// - ContentType (String) - MIME type
    /// - ExpiresAt (DateTime) - When preview URL expires (UTC)
    /// - CorrelationId (String) - Request tracking ID
    ///
    /// Required Configuration:
    /// - External Service Config record: "SDAP_BFF_API"
    /// - BaseUrl: https://spe-api-dev-67e2xz.azurewebsites.net/api
    /// - AuthType: ClientCredentials (1)
    /// - ClientId, ClientSecret, TenantId, Scope
    /// </summary>
    public class GetFilePreviewUrlPlugin : BaseProxyPlugin
    {
        private const string SERVICE_NAME = "SDAP_BFF_API";

        public GetFilePreviewUrlPlugin() : base("GetFilePreviewUrl")
        {
        }

        protected override void ValidateRequest()
        {
            base.ValidateRequest();

            // Validate this is a bound call on sprk_document entity
            if (ExecutionContext.PrimaryEntityName != "sprk_document" ||
                ExecutionContext.PrimaryEntityId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    "This Custom API must be called on a Document (sprk_document) record.");
            }
        }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            var documentId = ExecutionContext.PrimaryEntityId;

            TracingService.Trace($"[{correlationId}] Getting preview URL for document: {documentId}");

            // Get service configuration
            var config = GetServiceConfig(SERVICE_NAME);

            // Call SDAP BFF API with retry logic
            var result = ExecuteWithRetry(() => CallBffApi(documentId, correlationId, config), config);

            // Set output parameters
            ExecutionContext.OutputParameters["PreviewUrl"] = result.PreviewUrl;
            ExecutionContext.OutputParameters["FileName"] = result.FileName ?? "";
            ExecutionContext.OutputParameters["FileSize"] = result.FileSize;
            ExecutionContext.OutputParameters["ContentType"] = result.ContentType ?? "";
            ExecutionContext.OutputParameters["ExpiresAt"] = result.ExpiresAt;
            ExecutionContext.OutputParameters["CorrelationId"] = correlationId;

            TracingService.Trace($"[{correlationId}] Successfully retrieved preview URL");
        }

        private FilePreviewResult CallBffApi(
            Guid documentId,
            string correlationId,
            ExternalServiceConfig config)
        {
            using (var httpClient = CreateAuthenticatedHttpClient(config))
            {
                // Add correlation ID header for tracing
                httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

                var endpoint = $"/documents/{documentId}/preview-url";
                TracingService.Trace($"[{correlationId}] Calling BFF API: {config.BaseUrl}{endpoint}");

                var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    TracingService.Trace($"[{correlationId}] BFF API error: {response.StatusCode} - {errorContent}");

                    throw new InvalidPluginExecutionException(
                        $"SDAP BFF API returned {response.StatusCode}: {errorContent}");
                }

                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                TracingService.Trace($"[{correlationId}] BFF API response received (length: {content.Length})");

                // Parse response
                var result = ParseBffResponse(content, correlationId);
                return result;
            }
        }

        private FilePreviewResult ParseBffResponse(string jsonResponse, string correlationId)
        {
            try
            {
                var json = JObject.Parse(jsonResponse);
                var data = json["data"];
                var metadata = json["metadata"];

                if (data == null)
                {
                    throw new InvalidPluginExecutionException(
                        "BFF API response missing 'data' property");
                }

                var result = new FilePreviewResult
                {
                    PreviewUrl = data["previewUrl"]?.ToString(),
                    ContentType = data["contentType"]?.ToString(),
                    ExpiresAt = data["expiresAt"]?.ToObject<DateTime>() ?? DateTime.UtcNow.AddMinutes(10),
                    FileName = metadata?["fileName"]?.ToString() ?? "",
                    FileSize = metadata?["fileSize"]?.ToObject<long>() ?? 0
                };

                if (string.IsNullOrEmpty(result.PreviewUrl))
                {
                    throw new InvalidPluginExecutionException(
                        "BFF API did not return a preview URL");
                }

                TracingService.Trace($"[{correlationId}] Parsed preview URL successfully");
                return result;
            }
            catch (Exception ex)
            {
                TracingService.Trace($"[{correlationId}] Error parsing BFF response: {ex.Message}");
                throw new InvalidPluginExecutionException(
                    $"Failed to parse BFF API response: {ex.Message}", ex);
            }
        }

        private class FilePreviewResult
        {
            public string PreviewUrl { get; set; }
            public string FileName { get; set; }
            public long FileSize { get; set; }
            public string ContentType { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
