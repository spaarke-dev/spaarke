# Phase 3: Spe.Bff.Api Proxy Implementation

**Status**: 🔲 Not Started
**Duration**: 2 days
**Prerequisites**: Phase 2 complete, BaseProxyPlugin tested

---

## Phase Objectives

Implement operation-specific plugins for all file operations:
- Download File proxy
- Delete File proxy
- Replace/Update File proxy
- Upload File proxy

Create Custom API definitions and register plugins in Dataverse.

---

## Context for AI Vibe Coding

### What We're Building
Four operation-specific plugins that inherit from BaseProxyPlugin and implement file operations against Spe.Bff.Api:

1. **ProxyDownloadFilePlugin**: Downloads file from SPE container, returns Base64 encoded content
2. **ProxyDeleteFilePlugin**: Deletes file from SPE container
3. **ProxyReplaceFilePlugin**: Replaces existing file in SPE container with new content
4. **ProxyUploadFilePlugin**: Uploads new file to SPE container

### API Endpoints (Spe.Bff.Api)

Based on Sprint 4 implementation:

```
GET  /api/files/{fileId}/download       - Download file (returns file stream)
GET  /api/files/download?url={url}      - Download by URL (returns file stream)
DELETE /api/files/{fileId}              - Delete file
PUT  /api/files/{fileId}                - Replace file (multipart/form-data)
POST /api/files                         - Upload file (multipart/form-data)
```

### Key Patterns
- **Document Access Validation**: Always verify user has access to document before proxying
- **Error Mapping**: Map HTTP status codes to meaningful error messages
- **Content Encoding**: Files transferred as Base64 between Dataverse and PCF
- **Multipart Handling**: Upload/Replace use multipart/form-data encoding

---

## Task Breakdown

### Task 3.1: Implement ProxyDownloadFilePlugin

**Objective**: Download file from Spe.Bff.Api and return Base64 encoded content to PCF control.

**AI Instructions**:

Create file: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/ProxyDownloadFilePlugin.cs`

**Input Parameters**:
- `DocumentId` (string, required): Dataverse document ID (sprk_document)
- `FileId` (string, optional): SharePoint Embedded file ID
- `DownloadUrl` (string, optional): Direct download URL from SPE

**Output Parameters**:
- `FileContent` (string): Base64 encoded file content
- `FileName` (string): File name from response headers
- `ContentType` (string): MIME type
- `StatusCode` (int): HTTP status code

**Implementation Requirements**:

1. **Validate input parameters**:
   - DocumentId is required
   - Either FileId OR DownloadUrl must be provided

2. **Validate document access**:
   - Try to retrieve document record from Dataverse
   - If retrieve fails, user doesn't have access (throw exception)

3. **Build request URL**:
   - If DownloadUrl provided, use it directly
   - Else, use `/api/files/{FileId}/download`

4. **Execute HTTP request**:
   - Get service config for "SpeBffApi"
   - Create authenticated HttpClient
   - Execute GET request with retry logic
   - Handle HTTP errors (404, 403, 500)

5. **Process response**:
   - Read response content as byte array
   - Convert to Base64 string
   - Extract filename from Content-Disposition header
   - Extract ContentType from Content-Type header

6. **Set output parameters**

**Implementation**:

```csharp
using System;
using System.Linq;
using System.Net.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Downloads file from external API (Spe.Bff.Api) via proxy.
    /// Returns Base64 encoded file content to PCF control.
    /// </summary>
    public class ProxyDownloadFilePlugin : BaseProxyPlugin
    {
        public ProxyDownloadFilePlugin() : base("ProxyDownloadFile") { }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            TracingService.Trace("ProxyDownloadFilePlugin: Starting execution");

            // Get input parameters
            var documentId = GetInputParameter<string>("DocumentId");
            var fileId = GetInputParameter<string>("FileId", required: false);
            var downloadUrl = GetInputParameter<string>("DownloadUrl", required: false);

            TracingService.Trace($"DocumentId: {documentId}, FileId: {fileId}, DownloadUrl: {downloadUrl}");

            // Validate parameters
            if (string.IsNullOrEmpty(fileId) && string.IsNullOrEmpty(downloadUrl))
            {
                throw new InvalidPluginExecutionException("Either FileId or DownloadUrl must be provided");
            }

            // Validate user has access to document
            ValidateDocumentAccess(documentId);

            // Get service configuration
            var serviceConfig = GetServiceConfig("SpeBffApi");
            TracingService.Trace($"Service config loaded: {serviceConfig.BaseUrl}");

            // Build request URL
            string requestUrl;
            if (!string.IsNullOrEmpty(downloadUrl))
            {
                requestUrl = downloadUrl;
            }
            else
            {
                requestUrl = $"{serviceConfig.BaseUrl}/api/files/{fileId}/download";
            }

            TracingService.Trace($"Request URL: {requestUrl}");

            // Create authenticated HTTP client
            using (var httpClient = CreateAuthenticatedHttpClient(serviceConfig))
            {
                // Execute request with retry
                var response = ExecuteWithRetry(() =>
                {
                    var result = httpClient.GetAsync(requestUrl).Result;
                    return result;
                }, serviceConfig);

                TracingService.Trace($"Response status: {response.StatusCode}");

                // Check for errors
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = response.Content.ReadAsStringAsync().Result;
                    throw new InvalidPluginExecutionException(
                        $"Failed to download file. Status: {response.StatusCode}, Error: {errorContent}"
                    );
                }

                // Read file content
                var fileContent = response.Content.ReadAsByteArrayAsync().Result;
                var base64Content = Convert.ToBase64String(fileContent);

                TracingService.Trace($"File downloaded: {fileContent.Length} bytes");

                // Extract metadata from response
                var fileName = ExtractFileName(response);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                TracingService.Trace($"FileName: {fileName}, ContentType: {contentType}");

                // Set output parameters
                ExecutionContext.OutputParameters["FileContent"] = base64Content;
                ExecutionContext.OutputParameters["FileName"] = fileName ?? $"document-{documentId}.bin";
                ExecutionContext.OutputParameters["ContentType"] = contentType;
                ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;

                TracingService.Trace("ProxyDownloadFilePlugin: Completed successfully");
            }
        }

        /// <summary>
        /// Validate user has access to document.
        /// </summary>
        private void ValidateDocumentAccess(string documentId)
        {
            TracingService.Trace($"Validating access to document: {documentId}");

            try
            {
                // Attempt to retrieve document - will fail if user doesn't have access
                var document = OrganizationService.Retrieve(
                    "sprk_document",
                    new Guid(documentId),
                    new ColumnSet("sprk_documentid", "sprk_documentname")
                );

                TracingService.Trace($"Document access validated: {document.GetAttributeValue<string>("sprk_documentname")}");
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Document access validation failed: {ex.Message}");
                throw new InvalidPluginExecutionException(
                    $"User does not have access to document {documentId} or document does not exist.",
                    ex
                );
            }
        }

        /// <summary>
        /// Extract filename from Content-Disposition header.
        /// </summary>
        private string ExtractFileName(HttpResponseMessage response)
        {
            if (response.Content.Headers.ContentDisposition != null)
            {
                var fileName = response.Content.Headers.ContentDisposition.FileName;
                if (!string.IsNullOrEmpty(fileName))
                {
                    // Remove quotes if present
                    return fileName.Trim('"');
                }
            }

            // Try to extract from Content-Disposition header manually
            if (response.Content.Headers.TryGetValues("Content-Disposition", out var values))
            {
                var disposition = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(disposition))
                {
                    // Parse: attachment; filename="document.pdf"
                    var parts = disposition.Split(';');
                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim();
                        if (trimmed.StartsWith("filename="))
                        {
                            var fileName = trimmed.Substring("filename=".Length).Trim('"');
                            return fileName;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get input parameter with validation.
        /// </summary>
        private T GetInputParameter<T>(string name, bool required = true)
        {
            if (!ExecutionContext.InputParameters.Contains(name))
            {
                if (required)
                {
                    throw new InvalidPluginExecutionException($"Required parameter '{name}' is missing");
                }
                return default(T);
            }

            var value = ExecutionContext.InputParameters[name];
            if (value == null)
            {
                if (required)
                {
                    throw new InvalidPluginExecutionException($"Required parameter '{name}' is null");
                }
                return default(T);
            }

            return (T)value;
        }
    }
}
```

**Validation**:
- Code compiles without errors
- Handles missing parameters
- Validates document access
- Properly extracts filename from headers
- Returns Base64 encoded content

---

### Task 3.2: Implement ProxyDeleteFilePlugin

**Objective**: Delete file from Spe.Bff.Api.

**AI Instructions**:

Create file: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/ProxyDeleteFilePlugin.cs`

**Input Parameters**:
- `DocumentId` (string, required): Dataverse document ID
- `FileId` (string, required): SharePoint Embedded file ID

**Output Parameters**:
- `Success` (bool): Operation succeeded
- `StatusCode` (int): HTTP status code
- `ErrorMessage` (string, optional): Error message if failed

**Implementation**:

```csharp
using System;
using System.Net.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Deletes file from external API (Spe.Bff.Api) via proxy.
    /// </summary>
    public class ProxyDeleteFilePlugin : BaseProxyPlugin
    {
        public ProxyDeleteFilePlugin() : base("ProxyDeleteFile") { }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            TracingService.Trace("ProxyDeleteFilePlugin: Starting execution");

            // Get input parameters
            var documentId = GetInputParameter<string>("DocumentId");
            var fileId = GetInputParameter<string>("FileId");

            TracingService.Trace($"DocumentId: {documentId}, FileId: {fileId}");

            // Validate user has access to document
            ValidateDocumentAccess(documentId);

            // Get service configuration
            var serviceConfig = GetServiceConfig("SpeBffApi");

            // Build request URL
            var requestUrl = $"{serviceConfig.BaseUrl}/api/files/{fileId}";
            TracingService.Trace($"Request URL: {requestUrl}");

            // Create authenticated HTTP client
            using (var httpClient = CreateAuthenticatedHttpClient(serviceConfig))
            {
                try
                {
                    // Execute DELETE request with retry
                    var response = ExecuteWithRetry(() =>
                    {
                        return httpClient.DeleteAsync(requestUrl).Result;
                    }, serviceConfig);

                    TracingService.Trace($"Response status: {response.StatusCode}");

                    // Check for errors
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = response.Content.ReadAsStringAsync().Result;

                        ExecutionContext.OutputParameters["Success"] = false;
                        ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;
                        ExecutionContext.OutputParameters["ErrorMessage"] =
                            $"Failed to delete file. Status: {response.StatusCode}, Error: {errorContent}";

                        TracingService.Trace($"Delete failed: {errorContent}");
                    }
                    else
                    {
                        // Success
                        ExecutionContext.OutputParameters["Success"] = true;
                        ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;
                        ExecutionContext.OutputParameters["ErrorMessage"] = null;

                        TracingService.Trace("File deleted successfully");

                        // Update document record in Dataverse
                        UpdateDocumentAfterDelete(documentId);
                    }
                }
                catch (Exception ex)
                {
                    TracingService.Trace($"Exception during delete: {ex.Message}");

                    ExecutionContext.OutputParameters["Success"] = false;
                    ExecutionContext.OutputParameters["StatusCode"] = 500;
                    ExecutionContext.OutputParameters["ErrorMessage"] = ex.Message;

                    throw new InvalidPluginExecutionException($"Failed to delete file: {ex.Message}", ex);
                }
            }

            TracingService.Trace("ProxyDeleteFilePlugin: Completed");
        }

        private void ValidateDocumentAccess(string documentId)
        {
            TracingService.Trace($"Validating access to document: {documentId}");

            try
            {
                var document = OrganizationService.Retrieve(
                    "sprk_document",
                    new Guid(documentId),
                    new ColumnSet("sprk_documentid", "sprk_documentname")
                );

                TracingService.Trace("Document access validated");
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Document access validation failed: {ex.Message}");
                throw new InvalidPluginExecutionException(
                    $"User does not have access to document {documentId} or document does not exist.",
                    ex
                );
            }
        }

        /// <summary>
        /// Update document record after successful delete.
        /// Clear file-related fields.
        /// </summary>
        private void UpdateDocumentAfterDelete(string documentId)
        {
            try
            {
                var document = new Entity("sprk_document", new Guid(documentId));
                document["sprk_hasfile"] = false;
                document["sprk_fileid"] = null;
                document["sprk_downloadurl"] = null;
                document["sprk_filename"] = null;
                document["sprk_filesize"] = null;
                document["sprk_mimetype"] = null;

                OrganizationService.Update(document);

                TracingService.Trace("Document record updated after delete");
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Failed to update document after delete: {ex.Message}");
                // Don't fail the operation if update fails
            }
        }

        private T GetInputParameter<T>(string name, bool required = true)
        {
            if (!ExecutionContext.InputParameters.Contains(name))
            {
                if (required)
                    throw new InvalidPluginExecutionException($"Required parameter '{name}' is missing");
                return default(T);
            }

            var value = ExecutionContext.InputParameters[name];
            if (value == null && required)
                throw new InvalidPluginExecutionException($"Required parameter '{name}' is null");

            return (T)value;
        }
    }
}
```

**Validation**:
- Deletes file successfully
- Updates document record after delete
- Returns appropriate error messages

---

### Task 3.3: Implement ProxyReplaceFilePlugin

**Objective**: Replace existing file in Spe.Bff.Api with new content.

**AI Instructions**:

Create file: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/ProxyReplaceFilePlugin.cs`

**Input Parameters**:
- `DocumentId` (string, required): Dataverse document ID
- `FileId` (string, required): SharePoint Embedded file ID
- `FileContent` (string, required): Base64 encoded file content
- `FileName` (string, required): File name
- `ContentType` (string, optional): MIME type

**Output Parameters**:
- `Success` (bool): Operation succeeded
- `NewFileId` (string, optional): New file ID if API returns one
- `StatusCode` (int): HTTP status code
- `ErrorMessage` (string, optional): Error message if failed

**Key Implementation Details**:

1. Decode Base64 content to bytes
2. Create multipart/form-data request with file
3. Send PUT request to `/api/files/{fileId}`
4. Update document record with new metadata

**Implementation Template**:

```csharp
using System;
using System.Net.Http;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse.CustomApiProxy
{
    public class ProxyReplaceFilePlugin : BaseProxyPlugin
    {
        public ProxyReplaceFilePlugin() : base("ProxyReplaceFile") { }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            // TODO: Get input parameters (DocumentId, FileId, FileContent, FileName, ContentType)
            // TODO: Validate parameters
            // TODO: Validate document access
            // TODO: Decode Base64 content
            // TODO: Create multipart form data content
            // TODO: Send PUT request to /api/files/{fileId}
            // TODO: Process response
            // TODO: Update document record
            // TODO: Set output parameters

            throw new NotImplementedException("Task 3.3 implementation");
        }

        private void ValidateDocumentAccess(string documentId)
        {
            // Same as ProxyDownloadFilePlugin
        }

        private void UpdateDocumentAfterReplace(string documentId, string fileName, long fileSize, string contentType)
        {
            // Update document record with new file metadata
        }

        private T GetInputParameter<T>(string name, bool required = true)
        {
            // Same as ProxyDownloadFilePlugin
        }
    }
}
```

**Full Implementation Guidance**:

```csharp
protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
{
    TracingService.Trace("ProxyReplaceFilePlugin: Starting execution");

    // Get input parameters
    var documentId = GetInputParameter<string>("DocumentId");
    var fileId = GetInputParameter<string>("FileId");
    var fileContentBase64 = GetInputParameter<string>("FileContent");
    var fileName = GetInputParameter<string>("FileName");
    var contentType = GetInputParameter<string>("ContentType", required: false) ?? "application/octet-stream";

    TracingService.Trace($"DocumentId: {documentId}, FileId: {fileId}, FileName: {fileName}");

    // Validate document access
    ValidateDocumentAccess(documentId);

    // Decode Base64 content
    byte[] fileBytes;
    try
    {
        fileBytes = Convert.FromBase64String(fileContentBase64);
        TracingService.Trace($"File decoded: {fileBytes.Length} bytes");
    }
    catch (Exception ex)
    {
        throw new InvalidPluginExecutionException("Invalid Base64 encoded file content", ex);
    }

    // Get service configuration
    var serviceConfig = GetServiceConfig("SpeBffApi");

    // Build request URL
    var requestUrl = $"{serviceConfig.BaseUrl}/api/files/{fileId}";
    TracingService.Trace($"Request URL: {requestUrl}");

    using (var httpClient = CreateAuthenticatedHttpClient(serviceConfig))
    {
        // Create multipart form data
        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);

            // Execute PUT request
            var response = ExecuteWithRetry(() =>
            {
                return httpClient.PutAsync(requestUrl, content).Result;
            }, serviceConfig);

            TracingService.Trace($"Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().Result;

                ExecutionContext.OutputParameters["Success"] = false;
                ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;
                ExecutionContext.OutputParameters["ErrorMessage"] =
                    $"Failed to replace file. Status: {response.StatusCode}, Error: {errorContent}";
            }
            else
            {
                ExecutionContext.OutputParameters["Success"] = true;
                ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;

                // Update document record
                UpdateDocumentAfterReplace(documentId, fileName, fileBytes.Length, contentType);

                TracingService.Trace("File replaced successfully");
            }
        }
    }
}
```

---

### Task 3.4: Implement ProxyUploadFilePlugin

**Objective**: Upload new file to Spe.Bff.Api.

**AI Instructions**:

Create file: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/ProxyUploadFilePlugin.cs`

**Input Parameters**:
- `DocumentId` (string, required): Dataverse document ID
- `FileContent` (string, required): Base64 encoded file content
- `FileName` (string, required): File name
- `ContentType` (string, optional): MIME type

**Output Parameters**:
- `FileId` (string): SharePoint Embedded file ID
- `DownloadUrl` (string): Download URL
- `StatusCode` (int): HTTP status code
- `ErrorMessage` (string, optional): Error message if failed

**Implementation** (similar to Replace, but POST to `/api/files`):

```csharp
using System;
using System.Net.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;

namespace Spaarke.Dataverse.CustomApiProxy
{
    public class ProxyUploadFilePlugin : BaseProxyPlugin
    {
        public ProxyUploadFilePlugin() : base("ProxyUploadFile") { }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            TracingService.Trace("ProxyUploadFilePlugin: Starting execution");

            // Get input parameters
            var documentId = GetInputParameter<string>("DocumentId");
            var fileContentBase64 = GetInputParameter<string>("FileContent");
            var fileName = GetInputParameter<string>("FileName");
            var contentType = GetInputParameter<string>("ContentType", required: false) ?? "application/octet-stream";

            TracingService.Trace($"DocumentId: {documentId}, FileName: {fileName}");

            // Validate document access
            ValidateDocumentAccess(documentId);

            // Get container ID from document
            var document = OrganizationService.Retrieve(
                "sprk_document",
                new Guid(documentId),
                new ColumnSet("sprk_containerid")
            );

            var containerId = document.GetAttributeValue<string>("sprk_containerid");
            if (string.IsNullOrEmpty(containerId))
            {
                throw new InvalidPluginExecutionException("Document does not have a container ID");
            }

            TracingService.Trace($"ContainerId: {containerId}");

            // Decode Base64 content
            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(fileContentBase64);
                TracingService.Trace($"File decoded: {fileBytes.Length} bytes");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException("Invalid Base64 encoded file content", ex);
            }

            // Get service configuration
            var serviceConfig = GetServiceConfig("SpeBffApi");

            // Build request URL
            var requestUrl = $"{serviceConfig.BaseUrl}/api/files?containerId={containerId}";
            TracingService.Trace($"Request URL: {requestUrl}");

            using (var httpClient = CreateAuthenticatedHttpClient(serviceConfig))
            {
                // Create multipart form data
                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                    content.Add(fileContent, "file", fileName);

                    // Execute POST request
                    var response = ExecuteWithRetry(() =>
                    {
                        return httpClient.PostAsync(requestUrl, content).Result;
                    }, serviceConfig);

                    TracingService.Trace($"Response status: {response.StatusCode}");

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = response.Content.ReadAsStringAsync().Result;
                        throw new InvalidPluginExecutionException(
                            $"Failed to upload file. Status: {response.StatusCode}, Error: {errorContent}"
                        );
                    }

                    // Parse response
                    var responseContent = response.Content.ReadAsStringAsync().Result;
                    var uploadResult = JsonConvert.DeserializeObject<UploadResponse>(responseContent);

                    ExecutionContext.OutputParameters["FileId"] = uploadResult.FileId;
                    ExecutionContext.OutputParameters["DownloadUrl"] = uploadResult.DownloadUrl;
                    ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;

                    // Update document record
                    UpdateDocumentAfterUpload(documentId, uploadResult.FileId, uploadResult.DownloadUrl, fileName, fileBytes.Length, contentType);

                    TracingService.Trace("File uploaded successfully");
                }
            }
        }

        private void ValidateDocumentAccess(string documentId)
        {
            // Same as other plugins
        }

        private void UpdateDocumentAfterUpload(string documentId, string fileId, string downloadUrl, string fileName, long fileSize, string contentType)
        {
            try
            {
                var document = new Entity("sprk_document", new Guid(documentId));
                document["sprk_hasfile"] = true;
                document["sprk_fileid"] = fileId;
                document["sprk_downloadurl"] = downloadUrl;
                document["sprk_filename"] = fileName;
                document["sprk_filesize"] = (int)fileSize;
                document["sprk_mimetype"] = contentType;

                OrganizationService.Update(document);

                TracingService.Trace("Document record updated after upload");
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Failed to update document after upload: {ex.Message}");
                // Don't fail operation
            }
        }

        private T GetInputParameter<T>(string name, bool required = true)
        {
            // Same as other plugins
        }

        private class UploadResponse
        {
            public string FileId { get; set; }
            public string DownloadUrl { get; set; }
        }
    }
}
```

---

### Task 3.5: Create Custom API Definitions

**Objective**: Define Custom APIs in Dataverse for each operation.

**AI Instructions**:

Custom APIs can be created using:
1. **Power Platform CLI** (recommended for source control)
2. **Dataverse Web API** (programmatic)
3. **Power Apps Maker Portal** (manual)

**Option A: Using make.powerapps.com (Recommended for Phase 3)**

1. Navigate to https://make.powerapps.com
2. Select environment: spaarkedev1
3. Go to **More** → **Custom APIs** → **New Custom API**

For each operation, create Custom API:

#### Custom API: sprk_ProxyDownloadFile

- **Unique Name**: sprk_ProxyDownloadFile
- **Display Name**: Proxy Download File
- **Description**: Download file through external API proxy
- **Binding Type**: Global
- **Is Function**: No
- **Execute Privilege Name**: prvReadsprk_document

**Request Parameters**:
| Name | Display Name | Type | Required |
|------|--------------|------|----------|
| DocumentId | Document ID | String | Yes |
| FileId | File ID | String | No |
| DownloadUrl | Download URL | String | No |

**Response Properties**:
| Name | Display Name | Type |
|------|--------------|------|
| FileContent | File Content | String |
| FileName | File Name | String |
| ContentType | Content Type | String |
| StatusCode | Status Code | Integer |

#### Custom API: sprk_ProxyDeleteFile

- **Unique Name**: sprk_ProxyDeleteFile
- **Display Name**: Proxy Delete File
- **Binding Type**: Global
- **Is Function**: No
- **Execute Privilege Name**: prvWritesprk_document

**Request Parameters**:
| Name | Type | Required |
|------|------|----------|
| DocumentId | String | Yes |
| FileId | String | Yes |

**Response Properties**:
| Name | Type |
|------|------|
| Success | Boolean |
| StatusCode | Integer |
| ErrorMessage | String |

#### Custom API: sprk_ProxyReplaceFile

- **Unique Name**: sprk_ProxyReplaceFile
- **Binding Type**: Global

**Request Parameters**:
| Name | Type | Required |
|------|------|----------|
| DocumentId | String | Yes |
| FileId | String | Yes |
| FileContent | String | Yes |
| FileName | String | Yes |
| ContentType | String | No |

**Response Properties**:
| Name | Type |
|------|------|
| Success | Boolean |
| NewFileId | String |
| StatusCode | Integer |
| ErrorMessage | String |

#### Custom API: sprk_ProxyUploadFile

- **Unique Name**: sprk_ProxyUploadFile
- **Binding Type**: Global

**Request Parameters**:
| Name | Type | Required |
|------|------|----------|
| DocumentId | String | Yes |
| FileContent | String | Yes |
| FileName | String | Yes |
| ContentType | String | No |

**Response Properties**:
| Name | Type |
|------|------|
| FileId | String |
| DownloadUrl | String |
| StatusCode | Integer |
| ErrorMessage | String |

**Validation**:
- All Custom APIs created in Dataverse
- Parameters and response properties defined
- Execute privileges configured

---

### Task 3.6: Register Plugins

**Objective**: Register plugin assemblies and steps in Dataverse using Plugin Registration Tool.

**AI Instructions**:

1. **Build plugin assembly**:
```bash
cd src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy
dotnet build -c Release
```

2. **Download Plugin Registration Tool** (if not already available):
```bash
pac tool prt
```

3. **Launch Plugin Registration Tool**:
```bash
# Tool will be in %USERPROFILE%\.pac\tools\PluginRegistration
```

4. **Connect to Dataverse**:
   - Click "Create new connection"
   - Select "Microsoft 365"
   - Login with your credentials
   - Select environment: spaarkedev1

5. **Register Assembly**:
   - Click "Register" → "Register New Assembly"
   - Select DLL: `bin/Release/net462/Spaarke.Dataverse.CustomApiProxy.dll`
   - Isolation Mode: Sandbox
   - Location: Database
   - Click "Register Selected Plugins"

6. **Register Steps for Each Plugin**:

For each plugin class, no step registration needed if using Custom API!
Custom APIs automatically execute the plugin when called via `context.webAPI.execute()`.

However, you must **associate the plugin with the Custom API**:

7. **Associate Plugins with Custom APIs**:
   - In Plugin Registration Tool, right-click on plugin assembly
   - View registered plugins
   - For each Custom API, the plugin is automatically linked by name convention

**Important**: Plugin class name must match Custom API unique name pattern:
- Custom API: `sprk_ProxyDownloadFile`
- Plugin class: `ProxyDownloadFilePlugin`
- Dataverse automatically maps them

**Validation**:
- Assembly registered in Dataverse
- All 4 plugins visible in Plugin Registration Tool
- Plugins associated with Custom APIs

---

### Task 3.7: Configure External Service

**Objective**: Create configuration record for Spe.Bff.Api in Dataverse.

**AI Instructions**:

1. Navigate to model-driven app in spaarkedev1
2. Go to **Advanced Settings** → **Settings** → **Customizations** → **Custom Entities**
3. Find **External Service Configuration** entity
4. Click **New**

**Configuration for Spe.Bff.Api**:

| Field | Value |
|-------|-------|
| **Name** | SpeBffApi |
| **Display Name** | SPE BFF API |
| **Base URL** | https://spe-api-dev-67e2xz.azurewebsites.net |
| **Description** | SharePoint Embedded Backend-for-Frontend API |
| **Authentication Type** | Client Credentials |
| **Tenant ID** | [From Azure AD] |
| **Client ID** | [API_APP_ID from Key Vault] |
| **Client Secret** | [From Key Vault] |
| **Scope** | api://[API_APP_ID]/.default |
| **Timeout** | 300 (seconds) |
| **Retry Count** | 3 |
| **Retry Delay** | 1000 (ms) |
| **Is Enabled** | Yes |
| **Health Status** | Healthy |

**Getting Values**:

```bash
# Get API_APP_ID from Key Vault
az keyvault secret show --vault-name spaarke-dev-kv --name API-APP-ID --query value -o tsv

# Get Client Secret from Key Vault
az keyvault secret show --vault-name spaarke-dev-kv --name Dataverse--ClientSecret --query value -o tsv

# Get Tenant ID
az account show --query tenantId -o tsv
```

**Validation**:
- Configuration record created
- All required fields populated
- Client secret is masked in UI

---

### Task 3.8: Write Integration Tests

**Objective**: Create integration tests that call Custom APIs in Dataverse.

**AI Instructions**:

Create test file: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy.Tests/IntegrationTests.cs`

```csharp
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Spaarke.Dataverse.CustomApiProxy.Tests
{
    [TestClass]
    [TestCategory("Integration")]
    public class IntegrationTests
    {
        private ServiceClient _serviceClient;

        [TestInitialize]
        public void Setup()
        {
            // Connect to Dataverse
            var connectionString = Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION_STRING");
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Inconclusive("DATAVERSE_CONNECTION_STRING environment variable not set");
            }

            _serviceClient = new ServiceClient(connectionString);
            Assert.IsTrue(_serviceClient.IsReady, "Failed to connect to Dataverse");
        }

        [TestMethod]
        public void ProxyDownloadFile_ValidRequest_ReturnsFileContent()
        {
            // Arrange
            var request = new OrganizationRequest("sprk_ProxyDownloadFile");
            request["DocumentId"] = "test-doc-id"; // Replace with actual test document ID
            request["FileId"] = "test-file-id";

            // Act
            var response = _serviceClient.Execute(request);

            // Assert
            Assert.IsNotNull(response);
            Assert.IsTrue(response.Results.Contains("FileContent"));
            Assert.IsTrue(response.Results.Contains("FileName"));

            var fileContent = response["FileContent"] as string;
            Assert.IsFalse(string.IsNullOrEmpty(fileContent));
        }

        [TestMethod]
        public void ProxyDeleteFile_ValidRequest_ReturnsSuccess()
        {
            // Arrange
            var request = new OrganizationRequest("sprk_ProxyDeleteFile");
            request["DocumentId"] = "test-doc-id";
            request["FileId"] = "test-file-id";

            // Act
            var response = _serviceClient.Execute(request);

            // Assert
            Assert.IsNotNull(response);
            Assert.IsTrue((bool)response["Success"]);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _serviceClient?.Dispose();
        }
    }
}
```

**To run integration tests**:

1. Set environment variable:
```bash
# PowerShell
$env:DATAVERSE_CONNECTION_STRING = "AuthType=ClientSecret;Url=https://spaarkedev1.crm.dynamics.com;ClientId=...;ClientSecret=..."

# Bash
export DATAVERSE_CONNECTION_STRING="..."
```

2. Run tests:
```bash
dotnet test --filter TestCategory=Integration
```

**Validation**:
- Integration tests connect to Dataverse
- Custom APIs callable via OrganizationRequest
- Responses match expected schema

---

## Deliverables

✅ ProxyDownloadFilePlugin implemented and tested
✅ ProxyDeleteFilePlugin implemented and tested
✅ ProxyReplaceFilePlugin implemented and tested
✅ ProxyUploadFilePlugin implemented and tested
✅ Custom API definitions created in Dataverse
✅ Plugin assembly registered in Dataverse
✅ Plugins associated with Custom APIs
✅ External service configuration created for Spe.Bff.Api
✅ Integration tests written and passing

---

## Validation Checklist

- [ ] All 4 plugins compile without errors
- [ ] Plugin assembly is signed
- [ ] Custom APIs created in Dataverse
- [ ] Plugins registered in Plugin Registration Tool
- [ ] External service config record exists with valid credentials
- [ ] Integration tests pass against real Dataverse
- [ ] Can call Custom API from Plugin Registration Tool (test step)
- [ ] Document records updated correctly after file operations

---

## Next Steps

Proceed to **Phase 4: PCF Control Integration**

**Phase 4 will**:
- Update PCF control to call Custom APIs via context.webAPI
- Replace direct Spe.Bff.Api calls with proxy calls
- Update error handling and user feedback
- Add TypeScript type definitions
- Test end-to-end with Universal Dataset Grid

---

## Knowledge Resources

### Internal Documentation
- [Phase 2 Foundation](./PHASE-2-DATAVERSE-FOUNDATION.md)
- [Custom API Proxy Architecture](./CUSTOM-API-PROXY-ARCHITECTURE.md)
- [Sprint 4 Spe.Bff.Api Implementation](../Sprint%204/)

### External Resources
- [Custom API Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [Plugin Registration Tool](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/download-tools-nuget)
- [HttpClient Best Practices](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- [Multipart Form Data](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.multipartformdatacontent)

---

## Notes for AI Vibe Coding

**Common Pitfalls**:

1. **Base64 Encoding**: Always validate Base64 strings before decoding (try-catch)
2. **Multipart Content**: Dispose HttpContent properly to avoid memory leaks
3. **File Names**: Handle special characters and spaces in filenames
4. **Content-Disposition**: Parse header manually if ContentDisposition property is null
5. **Document Updates**: Don't fail operation if document update fails (log and continue)

**Testing Tips**:

1. Test with various file types (PDF, DOCX, images)
2. Test with large files (>10MB)
3. Test error cases (404, 403, 500 from API)
4. Test retry logic with transient errors
5. Verify document records updated correctly

**Debugging**:

1. Use Plugin Registration Tool to test Custom APIs directly
2. Check Plugin Trace Logs in Dataverse
3. Check Application Insights for telemetry
4. Check sprk_proxyauditlog records for request/response details
