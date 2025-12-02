# Step 2: Manual PowerShell Commands (No Module Required)

Instead of using the automated script, you can create the records manually using the Dataverse Web API.

## Prerequisites

- Authentication token (we'll get this first)
- Dataverse URL: `https://spaarkedev1.api.crm.dynamics.com`

---

## Option 1: Use Power Platform CLI (Recommended)

This is simpler and doesn't require the PowerShell module.

### Step 1: Authenticate

```bash
pac auth create --url https://spaarkedev1.crm.dynamics.com
```

### Step 2: Create External Service Config

```bash
pac data create --entity sprk_externalserviceconfig --data '{
  "sprk_name": "SDAP_BFF_API",
  "sprk_baseurl": "https://spe-api-dev-67e2xz.azurewebsites.net/api",
  "sprk_isenabled": true,
  "sprk_authtype": 1,
  "sprk_tenantid": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "sprk_clientid": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "sprk_clientsecret": "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj",
  "sprk_scope": "https://spe-api-dev-67e2xz.azurewebsites.net/.default",
  "sprk_timeout": 300,
  "sprk_retrycount": 3,
  "sprk_retrydelay": 1000
}'
```

### Step 3: Register Plugin Assembly (Manual via PRT)

```bash
pac tool prt
```

Follow the GUI steps:
1. Connect to SPAARKE DEV 1
2. Register → Register New Assembly
3. Browse to: `c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll`
4. Isolation: Sandbox, Location: Database
5. Register Selected Plugins

### Step 4: Create Custom API

```bash
pac data create --entity customapi --data '{
  "uniquename": "sprk_GetFilePreviewUrl",
  "name": "Get File Preview URL",
  "displayname": "Get File Preview URL",
  "description": "Server-side proxy for getting SharePoint Embedded preview URLs",
  "bindingtype": 1,
  "boundentitylogicalname": "sprk_document",
  "isfunction": true,
  "isprivate": false,
  "allowedcustomprocessingsteptype": 0
}'
```

**Save the ID from the output!** You'll need it for the next step.

### Step 5: Create Output Parameters

Replace `{CUSTOM_API_ID}` with the ID from Step 4:

```bash
# Parameter 1: PreviewUrl
pac data create --entity customapiresponseproperty --data '{
  "uniquename": "PreviewUrl",
  "name": "PreviewUrl",
  "displayname": "Preview URL",
  "description": "Ephemeral preview URL (expires in ~10 minutes)",
  "type": 10,
  "customapiid@odata.bind": "/customapis({CUSTOM_API_ID})"
}'

# Parameter 2: FileName
pac data create --entity customapiresponseproperty --data '{
  "uniquename": "FileName",
  "name": "FileName",
  "displayname": "File Name",
  "description": "File name for display",
  "type": 10,
  "customapiid@odata.bind": "/customapis({CUSTOM_API_ID})"
}'

# Parameter 3: FileSize
pac data create --entity customapiresponseproperty --data '{
  "uniquename": "FileSize",
  "name": "FileSize",
  "displayname": "File Size",
  "description": "File size in bytes",
  "type": 6,
  "customapiid@odata.bind": "/customapis({CUSTOM_API_ID})"
}'

# Parameter 4: ContentType
pac data create --entity customapiresponseproperty --data '{
  "uniquename": "ContentType",
  "name": "ContentType",
  "displayname": "Content Type",
  "description": "MIME type",
  "type": 10,
  "customapiid@odata.bind": "/customapis({CUSTOM_API_ID})"
}'

# Parameter 5: ExpiresAt
pac data create --entity customapiresponseproperty --data '{
  "uniquename": "ExpiresAt",
  "name": "ExpiresAt",
  "displayname": "Expires At",
  "description": "When the preview URL expires (UTC)",
  "type": 8,
  "customapiid@odata.bind": "/customapis({CUSTOM_API_ID})"
}'

# Parameter 6: CorrelationId
pac data create --entity customapiresponseproperty --data '{
  "uniquename": "CorrelationId",
  "name": "CorrelationId",
  "displayname": "Correlation ID",
  "description": "Request tracking ID for tracing and debugging",
  "type": 10,
  "customapiid@odata.bind": "/customapis({CUSTOM_API_ID})"
}'
```

### Step 6: Register Plugin Step (Manual via PRT)

In Plugin Registration Tool:
1. Expand `Spaarke.Dataverse.CustomApiProxy` assembly
2. Right-click `GetFilePreviewUrlPlugin` → Register New Step
3. Message: `sprk_GetFilePreviewUrl`
4. Primary Entity: `sprk_document`
5. Stage: PostOperation (30)
6. Mode: Synchronous
7. Register New Step

### Step 7: Publish Customizations

```bash
pac solution publish
```

OR manually in Power Apps:
- Settings → Customizations → Publish All Customizations

---

## Option 2: Use Power Apps Maker Portal (Fully Manual)

If you prefer a GUI approach:

1. **External Service Config**:
   - Go to https://make.powerapps.com
   - Select SPAARKE DEV 1 environment
   - Tables → All → Search "External Service Config"
   - Click "+ New row" and fill in the fields manually

2. **Custom API**:
   - In maker portal, search for "Custom API" table
   - Create new record with the values above

3. **Parameters**:
   - After creating Custom API, add related records for each output parameter

---

## Which Option Do You Prefer?

1. **Install PowerShell module** and run the automated script
2. **Use `pac` CLI commands** (Option 1 above) - simpler, no module needed
3. **Use Power Apps Maker Portal** (Option 2 above) - fully manual, GUI-based

Let me know which you'd like to proceed with!
