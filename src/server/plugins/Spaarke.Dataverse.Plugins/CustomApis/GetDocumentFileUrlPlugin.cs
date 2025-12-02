using Microsoft.Xrm.Sdk;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spaarke.Dataverse.Plugins.CustomApis
{
    /// <summary>
    /// Custom API Plugin: sprk_GetDocumentFileUrl
    /// Server-side proxy for getting SharePoint Embedded file URLs from SDAP BFF API.
    ///
    /// This plugin eliminates the need for client-side authentication (MSAL.js) in web resources.
    /// The plugin runs server-side with the user's context and handles all authentication.
    ///
    /// Custom API Registration:
    /// - Name: sprk_GetDocumentFileUrl
    /// - Unique Name: sprk_GetDocumentFileUrl
    /// - Binding Type: Entity (sprk_document)
    /// - Bound Entity Logical Name: sprk_document
    /// - Is Function: Yes
    /// - Allowed Custom Processing Step Type: None (synchronous only)
    ///
    /// Input Parameters:
    /// - DocumentId (EntityReference, required) - The Document record to get file URL for
    /// - EndpointType (String, required) - "preview", "content", or "office"
    ///
    /// Output Parameters:
    /// - FileUrl (String) - The ephemeral file URL
    /// - FileName (String) - The file name
    /// - FileSize (Integer) - The file size in bytes
    /// - ContentType (String) - The MIME type
    /// - ExpiresAt (DateTime) - When the URL expires
    /// </summary>
    public class GetDocumentFileUrlPlugin : IPlugin
    {
        private const string SDAP_API_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net/api";

        public void Execute(IServiceProvider serviceProvider)
        {
            // Get services
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                tracingService.Trace($"GetDocumentFileUrlPlugin: Execution started for user {context.UserId}");

                // Validate this is a bound call on sprk_document entity
                if (context.PrimaryEntityName != "sprk_document" || context.PrimaryEntityId == Guid.Empty)
                {
                    throw new InvalidPluginExecutionException("This Custom API must be called on a Document (sprk_document) record.");
                }

                var documentId = context.PrimaryEntityId;
                tracingService.Trace($"Document ID: {documentId}");

                // Get EndpointType parameter
                var endpointType = context.InputParameters.Contains("EndpointType")
                    ? context.InputParameters["EndpointType"]?.ToString()?.ToLowerInvariant()
                    : "content";

                if (string.IsNullOrEmpty(endpointType))
                {
                    endpointType = "content";
                }

                // Validate endpoint type
                if (endpointType != "preview" && endpointType != "content" && endpointType != "office")
                {
                    throw new InvalidPluginExecutionException($"Invalid EndpointType '{endpointType}'. Must be 'preview', 'content', or 'office'.");
                }

                tracingService.Trace($"Endpoint Type: {endpointType}");

                // Get access token for the current user
                // Note: In Dataverse plugins, you typically use the plugin's app registration
                // or service principal to call external APIs. This requires configuration.
                // For now, we'll document the pattern and implement a basic version.

                string accessToken = GetAccessToken(tracingService);

                // Call SDAP BFF API
                var result = CallSdapBffApiAsync(documentId, endpointType, accessToken, tracingService).GetAwaiter().GetResult();

                // Set output parameters
                context.OutputParameters["FileUrl"] = result.FileUrl;
                context.OutputParameters["FileName"] = result.FileName ?? "";
                context.OutputParameters["FileSize"] = result.FileSize;
                context.OutputParameters["ContentType"] = result.ContentType ?? "";
                context.OutputParameters["ExpiresAt"] = result.ExpiresAt;

                tracingService.Trace($"Successfully retrieved file URL for document {documentId}");
            }
            catch (Exception ex)
            {
                tracingService.Trace($"Error in GetDocumentFileUrlPlugin: {ex.Message}");
                tracingService.Trace($"Stack trace: {ex.StackTrace}");
                throw new InvalidPluginExecutionException($"Failed to get file URL: {ex.Message}", ex);
            }
        }

        private string GetAccessToken(ITracingService tracingService)
        {
            // TODO: Implement proper token acquisition
            // Options:
            // 1. Use Dataverse service principal configured in Key Vault
            // 2. Use Azure AD client credentials flow
            // 3. Use managed identity if running in Azure

            // For now, this is a placeholder that documents the requirement
            // In production, you would:
            // - Get ClientId/ClientSecret from secure configuration
            // - Call Azure AD token endpoint
            // - Return access token for SDAP BFF API

            tracingService.Trace("TODO: Implement token acquisition from Azure AD");

            throw new InvalidPluginExecutionException(
                "Token acquisition not yet implemented. " +
                "This requires configuring Azure AD app registration for the plugin. " +
                "See deployment documentation for setup instructions.");
        }

        private async Task<FileUrlResult> CallSdapBffApiAsync(
            Guid documentId,
            string endpointType,
            string accessToken,
            ITracingService tracingService)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var endpoint = $"{SDAP_API_BASE_URL}/documents/{documentId}/{endpointType}";
                tracingService.Trace($"Calling SDAP BFF API: {endpoint}");

                var response = await httpClient.GetAsync(endpoint);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    tracingService.Trace($"SDAP BFF API error: {response.StatusCode} - {errorContent}");
                    throw new InvalidPluginExecutionException($"SDAP BFF API returned {response.StatusCode}: {errorContent}");
                }

                var content = await response.Content.ReadAsStringAsync();
                tracingService.Trace($"SDAP BFF API response: {content}");

                // Parse response based on endpoint type
                var result = ParseSdapResponse(content, endpointType, tracingService);
                return result;
            }
        }

        private FileUrlResult ParseSdapResponse(string jsonResponse, string endpointType, ITracingService tracingService)
        {
            try
            {
                using (var document = JsonDocument.Parse(jsonResponse))
                {
                    var root = document.RootElement;
                    var data = root.GetProperty("data");

                    var result = new FileUrlResult();

                    switch (endpointType)
                    {
                        case "preview":
                            result.FileUrl = data.GetProperty("previewUrl").GetString();
                            result.ContentType = data.TryGetProperty("contentType", out var ct) ? ct.GetString() : null;
                            result.ExpiresAt = data.TryGetProperty("expiresAt", out var exp) ? exp.GetDateTime() : DateTime.UtcNow.AddMinutes(10);
                            break;

                        case "content":
                            result.FileUrl = data.GetProperty("downloadUrl").GetString();
                            result.FileName = data.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                            result.FileSize = data.TryGetProperty("size", out var size) ? size.GetInt64() : 0;
                            result.ContentType = data.TryGetProperty("contentType", out var ct2) ? ct2.GetString() : null;
                            result.ExpiresAt = data.TryGetProperty("expiresAt", out var exp2) ? exp2.GetDateTime() : DateTime.UtcNow.AddMinutes(5);
                            break;

                        case "office":
                            result.FileUrl = data.GetProperty("viewerUrl").GetString();
                            result.ContentType = data.TryGetProperty("fileType", out var ft) ? ft.GetString() : null;
                            result.ExpiresAt = data.TryGetProperty("expiresAt", out var exp3) ? exp3.GetDateTime() : DateTime.UtcNow.AddMinutes(10);
                            break;
                    }

                    // Get metadata if available
                    if (root.TryGetProperty("metadata", out var metadata))
                    {
                        if (metadata.TryGetProperty("fileName", out var metaFileName))
                        {
                            result.FileName = metaFileName.GetString();
                        }
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"Error parsing SDAP response: {ex.Message}");
                throw new InvalidPluginExecutionException($"Failed to parse SDAP BFF API response: {ex.Message}", ex);
            }
        }

        private class FileUrlResult
        {
            public string FileUrl { get; set; }
            public string FileName { get; set; }
            public long FileSize { get; set; }
            public string ContentType { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
