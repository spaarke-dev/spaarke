# Testing the Custom API

## Prerequisites
- Custom API `sprk_GetFilePreviewUrl` is registered and published ✅
- Plugin is linked to the Custom API ✅
- You need an existing `sprk_document` record with a file in SharePoint Embedded

## Test Method 1: Browser Console (Recommended)

### Step 1: Navigate to a Document Record
1. Open Power Apps (https://make.powerapps.com)
2. Open your app that uses `sprk_document` records
3. Open any existing document record, OR create a test record
4. Press **F12** to open browser developer tools
5. Go to **Console** tab

### Step 2: Get the Document Record ID
In the browser URL bar, you should see something like:
```
.../main.aspx?...&id={GUID}&...
```

Copy that GUID (the document record ID).

### Step 3: Call the Custom API
Paste this code in the console (replace `DOCUMENT_ID` with your GUID):

```javascript
// Replace with your actual document record ID
const documentId = "DOCUMENT_ID"; // Example: "12345678-1234-1234-1234-123456789abc"
const entityName = "sprk_document";

// Call the Custom API
Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": {
                    typeName: entityName,
                    structuralProperty: 5 // Entity type
                }
            },
            operationType: 1, // Function
            operationName: "sprk_GetFilePreviewUrl"
        };
    },
    entity: {
        entityType: entityName,
        id: documentId
    }
}).then(
    function success(result) {
        console.log("✅ Custom API Success!");
        console.log("Response:", result);
        console.log("---");
        console.log("Preview URL:", result.PreviewUrl);
        console.log("File Name:", result.FileName);
        console.log("File Size:", result.FileSize, "bytes");
        console.log("Content Type:", result.ContentType);
        console.log("Expires At:", result.ExpiresAt);
        console.log("Correlation ID:", result.CorrelationId);

        // Test opening the preview URL
        if (result.PreviewUrl) {
            console.log("\n🔗 Opening preview URL in new tab...");
            window.open(result.PreviewUrl, '_blank');
        }
    },
    function error(err) {
        console.error("❌ Custom API Error:", err.message);
        console.error("Full error:", err);
    }
);
```

### Expected Success Response
```json
{
  "PreviewUrl": "https://...sharepoint.com/.../preview?...",
  "FileName": "example-document.pdf",
  "FileSize": 245678,
  "ContentType": "application/pdf",
  "ExpiresAt": "2025-11-22T18:45:00Z",
  "CorrelationId": "abc-123-def-456"
}
```

The preview URL should open in a new tab and show the file viewer.

## Test Method 2: Web API Direct Call

If you don't have access to a Power App UI, you can test via Web API:

```powershell
# Get document ID first
$documentId = "YOUR_DOCUMENT_GUID"
$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

# Get token
$token = az account get-access-token --resource $dataverseUrl --query accessToken -o tsv

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# Call the Custom API
$apiUrl = "$dataverseUrl/api/data/v9.2/sprk_documents($documentId)/Microsoft.Dynamics.CRM.sprk_GetFilePreviewUrl()"

$response = Invoke-RestMethod -Uri $apiUrl -Method Get -Headers $headers

Write-Host "Success!" -ForegroundColor Green
$response | ConvertTo-Json -Depth 3
```

## Troubleshooting

### Error: "The plug-in execution failed"
- Check Plugin Trace Logs in XrmToolBox (Plugin Trace Viewer)
- Verify External Service Config record exists and has correct BFF API URL
- Check that BFF API is running and accessible

### Error: "Invalid function name"
- Verify customizations were published
- Refresh browser/clear cache
- Check Custom API unique name is exactly `sprk_GetFilePreviewUrl`

### Error: "Authentication failed" from plugin
- Verify External Service Config has correct:
  - Tenant ID
  - Client ID
  - Client Secret
  - Scope
- Check that the service principal has permissions on the BFF API

### Preview URL returns 401/403
- The preview URL is ephemeral and tied to user context
- Check SharePoint Embedded permissions
- Verify the BFF API can successfully call SharePoint Embedded

### No document record exists
Create a test record:
1. Go to your Power App
2. Create a new `sprk_document` record
3. Upload a file (this should trigger your existing document quick create logic)
4. Use that record's ID for testing

## Success Criteria
✅ Custom API executes without errors
✅ Returns all 6 output parameters
✅ PreviewUrl is a valid SharePoint Embedded URL
✅ Opening the URL shows the file viewer
✅ CorrelationId matches in both Dataverse and BFF API logs

## Next Steps After Successful Test
- Proceed to **Phase 3: PCF Control Development**
- Build the React-based file viewer PCF control
- Integrate the Custom API call into the PCF control
