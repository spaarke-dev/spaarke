# File Access Endpoints - SharePoint Embedded Integration

**Feature**: BFF API endpoints for accessing SharePoint Embedded files with proper Microsoft-aligned patterns
**Version**: SDAP BFF API v1.0
**Date**: 2025-01-20
**Status**: ✅ Implemented

---

## 📋 Overview

This document describes the **proper Microsoft-aligned pattern** for accessing files stored in SharePoint Embedded (SPE). Based on Microsoft's November 2025 guidance, these endpoints implement server-side URL resolution with short-lived, ephemeral URLs.

### Key Principles (Microsoft Guidance)

1. **Never persist file URLs** - SharePoint Embedded uses ephemeral URLs that expire quickly
2. **Server-side resolution** - BFF API resolves URLs per request with proper authorization
3. **Short-lived tokens** - Preview URLs (~10 min), Download URLs (~5 min)
4. **Use Graph API actions**:
   - `driveItem: preview` - For embeddable iframe previews
   - `driveItem: content` or `@microsoft.graph.downloadUrl` - For downloads
   - Office web experiences - For Word/Excel/PowerPoint viewing/editing

### References

- [Graph API: driveItem preview](https://learn.microsoft.com/en-us/graph/api/driveitem-preview)
- [Graph API: driveItem content](https://learn.microsoft.com/en-us/graph/api/driveitem-get-content)
- [SharePoint Embedded: Office experiences](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/office-experiences)

---

## 🏗️ Architecture

```
User Action (View/Download File)
  ↓
PCF Control / Web Resource / UI
  ↓
SDAP BFF API Endpoint
  ├─ GET /api/documents/{documentId}/preview   (iframe embedding)
  ├─ GET /api/documents/{documentId}/content   (download/view)
  └─ GET /api/documents/{documentId}/office    (Office viewer)
  ↓
BFF Authorization & Dataverse Lookup
  1. Authorize user (require authentication)
  2. Query Dataverse: Get Document record (sprk_graphitemid, sprk_graphdriveid)
  3. Validate user has access to Document (UAC enforcement)
  ↓
Microsoft Graph API Call (OBO Token)
  - POST /drives/{driveId}/items/{itemId}/preview
  - GET /drives/{driveId}/items/{itemId} (with @microsoft.graph.downloadUrl)
  ↓
BFF Response
  - Return short-lived URL (expires 5-10 minutes)
  - Include expiration timestamp
  - Client uses URL immediately
  ↓
Client Displays/Downloads File
  - iframe.src = previewUrl (for preview)
  - window.open(downloadUrl) (for download)
  - Auto-refresh before expiration
```

---

## 📡 API Endpoints

### Base URL
```
https://spe-api-dev-67e2xz.azurewebsites.net/api
```

### Authentication
All endpoints require Bearer token authentication:
```http
Authorization: Bearer {access_token}
```

---

### 1. Preview Endpoint (iframe Embedding)

**Use Case**: Display file previews in iframes (PDFs, images, Office docs)

```http
GET /api/documents/{documentId}/preview
```

#### Request
```http
GET /api/documents/550e8400-e29b-41d4-a716-446655440000/preview HTTP/1.1
Host: spe-api-dev-67e2xz.azurewebsites.net
Authorization: Bearer {token}
```

#### Response (Success - 200 OK)
```json
{
  "data": {
    "previewUrl": "https://spaarke.sharepoint.com/...",
    "postUrl": null,
    "expiresAt": "2025-01-20T15:30:00Z",
    "contentType": "application/pdf"
  },
  "metadata": {
    "requestId": "trace-abc123",
    "timestamp": "2025-01-20T15:20:00Z",
    "fileName": "document.pdf"
  }
}
```

#### Response (Error - 404 Not Found)
```json
{
  "status": 404,
  "title": "Document Not Found",
  "detail": "Document with ID 550e8400-e29b-41d4-a716-446655440000 was not found",
  "traceId": "trace-abc123"
}
```

#### Usage Example (JavaScript)
```javascript
async function showFilePreview(documentId) {
    const token = await getAccessToken();

    const response = await fetch(
        `https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/${documentId}/preview`,
        {
            headers: { 'Authorization': `Bearer ${token}` }
        }
    );

    const result = await response.json();

    // Display in iframe
    document.getElementById('fileViewer').src = result.data.previewUrl;

    // Schedule refresh before expiration (8 min for 10 min expiry)
    setTimeout(() => showFilePreview(documentId), 8 * 60 * 1000);
}
```

---

### 2. Content Endpoint (Download/Direct View)

**Use Case**: Download files or direct browser viewing (browser-renderable files like PDF/images)

```http
GET /api/documents/{documentId}/content
```

#### Request
```http
GET /api/documents/550e8400-e29b-41d4-a716-446655440000/content HTTP/1.1
Host: spe-api-dev-67e2xz.azurewebsites.net
Authorization: Bearer {token}
```

#### Response (Success - 200 OK)
```json
{
  "data": {
    "downloadUrl": "https://spaarke.sharepoint.com/.../download?...",
    "contentType": "application/pdf",
    "fileName": "document.pdf",
    "size": 102400,
    "expiresAt": "2025-01-20T15:25:00Z"
  },
  "metadata": {
    "requestId": "trace-def456",
    "timestamp": "2025-01-20T15:20:00Z"
  }
}
```

#### Usage Example (JavaScript)
```javascript
async function downloadFile(documentId) {
    const token = await getAccessToken();

    const response = await fetch(
        `https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/${documentId}/content`,
        {
            headers: { 'Authorization': `Bearer ${token}` }
        }
    );

    const result = await response.json();

    // Trigger download
    window.open(result.data.downloadUrl, '_blank');
}
```

---

### 3. Office Endpoint (Office Web Viewer/Editor)

**Use Case**: Open Word/Excel/PowerPoint files in Microsoft 365 web experiences

```http
GET /api/documents/{documentId}/office?mode={view|edit}
```

#### Request
```http
GET /api/documents/550e8400-e29b-41d4-a716-446655440000/office?mode=view HTTP/1.1
Host: spe-api-dev-67e2xz.azurewebsites.net
Authorization: Bearer {token}
```

#### Response (Success - 200 OK)
```json
{
  "data": {
    "viewerUrl": "https://word.office.com/...",
    "editorUrl": "https://word.office.com/.../edit",
    "fileType": "docx",
    "expiresAt": "2025-01-20T15:30:00Z"
  },
  "metadata": {
    "requestId": "trace-ghi789",
    "timestamp": "2025-01-20T15:20:00Z",
    "fileName": "document.docx",
    "mode": "view"
  }
}
```

#### Usage Example (JavaScript)
```javascript
async function openInOffice(documentId, mode = 'view') {
    const token = await getAccessToken();

    const response = await fetch(
        `https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/${documentId}/office?mode=${mode}`,
        {
            headers: { 'Authorization': `Bearer ${token}` }
        }
    );

    const result = await response.json();

    // Open in Office web viewer/editor
    window.open(result.data[mode === 'edit' ? 'editorUrl' : 'viewerUrl'], '_blank');
}
```

---

## 🎨 Integration Examples

### Example 1: Document Form File Viewer (Web Resource)

Add the provided `sprk_document_file_viewer.html` web resource to Document form:

**Form Configuration:**
1. Open Document form in Form Designer
2. Add **Web Resource** control
3. Select: `sprk_document_file_viewer.html`
4. Set size: Full width, 600px height
5. Save and publish

**Features:**
- ✅ Auto-loads file preview when form opens
- ✅ Auto-refreshes preview URL every 8 minutes
- ✅ Download button
- ✅ Office viewer button (for Word/Excel/PowerPoint)
- ✅ Error handling with user-friendly messages

---

### Example 2: Grid Command Button (Dataset Grid)

Add "View File" button to UniversalDatasetGrid:

```javascript
// In ribbon command handler
async function viewFile(selectedRecordId) {
    try {
        // Get access token
        const token = await Xrm.Utility.getGlobalContext().getAccessToken();

        // Call SDAP BFF preview endpoint
        const response = await fetch(
            `https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/${selectedRecordId}/preview`,
            {
                headers: { 'Authorization': `Bearer ${token}` }
            }
        );

        if (!response.ok) {
            throw new Error('Failed to get preview URL');
        }

        const result = await response.json();

        // Open file in modal dialog
        Xrm.Navigation.openAlertDialog({
            text: `<iframe src="${result.data.previewUrl}" style="width:100%;height:600px;border:none;"></iframe>`,
            title: 'File Preview'
        });

    } catch (error) {
        Xrm.Navigation.openErrorDialog({
            message: 'Failed to preview file: ' + error.message
        });
    }
}
```

---

### Example 3: PowerFx Formula (Canvas App)

```powerfx
// Get preview URL
Set(
    previewResponse,
    With(
        {
            url: "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/" & varDocumentId & "/preview",
            token: "Bearer " & Param("token")
        },
        JSON(
            IfError(
                ParseJSON(
                    HttpRequest(
                        url,
                        {
                            Headers: { Authorization: token }
                        }
                    )
                ),
                { error: true }
            )
        )
    )
);

// Set iframe source
Set(varPreviewUrl, previewResponse.data.previewUrl);
```

---

## 🔐 Security & Authorization

### Server-Side Enforcement

✅ **All endpoints require authentication** - Bearer token validation
✅ **Dataverse UAC enforcement** - User must have read access to Document record
✅ **OBO (On-Behalf-Of) tokens** - Graph API calls use user's delegated permissions
✅ **No app-only secrets exposed** - All Graph calls server-side with secure credentials
✅ **Audit trail** - All requests logged with trace IDs

### Token Management

```javascript
// Get access token from Xrm context
async function getAccessToken() {
    const globalContext = Xrm.Utility.getGlobalContext();
    const token = await globalContext.getAccessToken();
    return token;
}

// Alternative: Use Xrm.WebApi which handles auth automatically
async function callBffApi(endpoint) {
    const token = await getAccessToken();
    return fetch(endpoint, {
        headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
        }
    });
}
```

---

## ⚙️ Configuration

### SDAP BFF API Configuration

Update `appsettings.json` (or Azure App Service configuration):

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "{tenant-id}",
    "ClientId": "{sdap-api-client-id}",
    "ClientSecret": "{client-secret}"
  },
  "GraphApi": {
    "Scopes": [
      "https://graph.microsoft.com/.default"
    ]
  },
  "Dataverse": {
    "Url": "https://org.crm.dynamics.com",
    "ClientId": "{service-principal-id}",
    "ClientSecret": "{service-principal-secret}"
  }
}
```

### PCF Control Configuration

Update `sprk_universaldocumentupload_page.json` Custom Page:

```json
{
  "sdapApiBaseUrl": {
    "value": "https://spe-api-dev-67e2xz.azurewebsites.net/api"
  }
}
```

---

## 🧪 Testing Guide

### Test Case 1: Preview PDF File

**Steps:**
1. Open Document record with PDF file
2. Web resource should auto-load preview
3. Verify PDF displays in iframe
4. Wait 9 minutes, verify preview refreshes automatically

**Expected:**
- ✅ Preview loads successfully
- ✅ PDF renders in iframe
- ✅ Auto-refresh works before expiration

### Test Case 2: Download File

**Steps:**
1. Open Document record
2. Click "Download File" button
3. Verify file downloads to browser

**Expected:**
- ✅ Download URL generated successfully
- ✅ File downloads with correct filename
- ✅ File content matches original

### Test Case 3: Office File Viewer

**Steps:**
1. Open Document record with Word/Excel/PowerPoint file
2. Click "Open in Office" button
3. Verify file opens in Microsoft 365 web viewer

**Expected:**
- ✅ Office viewer URL generated
- ✅ File opens in new tab
- ✅ Office web viewer displays file correctly

### Test Case 4: Error Handling

**Steps:**
1. Open Document record with no SPE metadata (no file uploaded yet)
2. Web resource should display error message

**Expected:**
- ✅ Error message displayed
- ✅ No JavaScript errors in console
- ✅ User-friendly error message

---

## 🐛 Troubleshooting

### Issue: Preview URL Returns 404

**Possible Causes:**
1. Document record doesn't have `sprk_graphitemid` or `sprk_graphdriveid`
2. File was deleted from SharePoint Embedded
3. User lacks permissions to SharePoint container

**Debug Steps:**
```javascript
// Check Document record metadata
const doc = await Xrm.WebApi.retrieveRecord('sprk_document', documentId, '?$select=sprk_graphitemid,sprk_graphdriveid');
console.log('Document metadata:', doc);

// Verify fields are populated
if (!doc.sprk_graphitemid || !doc.sprk_graphdriveid) {
    console.error('Missing SPE metadata - file may not have been uploaded properly');
}
```

**Solution:**
1. Verify file was uploaded via PCF control (v3.0.6+)
2. Check Azure Service Bus logs for upload failures
3. Re-upload file if metadata is missing

---

### Issue: Preview URL Expired

**Symptom**: iframe shows error after 10+ minutes

**Cause**: Preview URLs expire after ~10 minutes

**Solution**: Implement auto-refresh
```javascript
// Refresh preview every 8 minutes
setInterval(() => loadFilePreview(documentId), 8 * 60 * 1000);
```

---

### Issue: CORS Error

**Symptom**: Browser console shows CORS policy error

**Cause**: SDAP BFF API CORS policy doesn't allow Dataverse origin

**Solution**: Update API CORS configuration
```csharp
// In Program.cs
app.UseCors(policy => policy
    .WithOrigins("https://org.crm.dynamics.com", "https://org.crm4.dynamics.com")
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials());
```

---

## 📊 Performance Considerations

### URL Caching Strategy

❌ **Do NOT cache preview/download URLs** - They expire quickly
✅ **Cache Document metadata** - Store driveId/itemId in local state
✅ **Implement refresh timers** - Auto-refresh before expiration

### Example Refresh Strategy
```javascript
class FilePreviewManager {
    constructor(documentId) {
        this.documentId = documentId;
        this.previewUrl = null;
        this.expiresAt = null;
        this.refreshTimer = null;
    }

    async load() {
        const result = await this.getPreviewUrl();
        this.previewUrl = result.data.previewUrl;
        this.expiresAt = new Date(result.data.expiresAt);

        // Schedule refresh 2 minutes before expiration
        const refreshIn = this.expiresAt - Date.now() - (2 * 60 * 1000);
        this.refreshTimer = setTimeout(() => this.load(), refreshIn);
    }

    async getPreviewUrl() {
        const token = await getAccessToken();
        const response = await fetch(`${API_BASE}/documents/${this.documentId}/preview`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        return response.json();
    }

    destroy() {
        if (this.refreshTimer) clearTimeout(this.refreshTimer);
    }
}
```

---

## 🚀 Deployment Checklist

- [ ] SDAP BFF API deployed with new file access endpoints
- [ ] Azure AD app registration configured for OBO flow
- [ ] CORS policy updated to allow Dataverse origins
- [ ] Web resource `sprk_document_file_viewer.html` uploaded to solution
- [ ] Web resource added to Document form
- [ ] Document form published
- [ ] Test preview endpoint with PDF file
- [ ] Test download endpoint
- [ ] Test Office viewer endpoint with Word/Excel file
- [ ] Verify auto-refresh works (wait 9 minutes)
- [ ] Test error handling (document with no file)
- [ ] Update user documentation

---

## 📚 Related Documentation

- [ENHANCEMENT-FILE-URL.md](./ENHANCEMENT-FILE-URL.md) - Original file URL enhancement (now deprecated approach)
- [SDAP API Architecture](../../../../dev/projects/sdap_project/Sprint%207_Dataset%20Grid%20to%20SDAP/SPRINT-7-MASTER-RESOURCE.md)
- [UniversalDatasetGrid Integration](../../UniversalDatasetGrid/docs/FILE-URL-FIELD.md)

---

**Last Updated**: 2025-01-20
**Version**: 1.0
**Status**: ✅ Ready for deployment
