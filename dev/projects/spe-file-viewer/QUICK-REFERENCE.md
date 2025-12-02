# SPE File Viewer - Quick Reference

**Quick lookup for file paths, commands, and key information during implementation**

---

## 📁 File Paths

### Backend API (SDAP BFF)

```
src/api/Spe.Bff.Api/
├── Api/
│   └── FileAccessEndpoints.cs              # UPDATE: Add /preview-url endpoint
├── Services/
│   ├── SpeFileStore.cs                     # UPDATE: Add GetPreviewUrlAsync()
│   └── DataverseService.cs                 # UPDATE: Add ValidateDocumentAccessAsync()
├── Models/
│   └── SpeFileStoreDtos.cs                 # ✅ Already exists
└── Program.cs                              # Verify DI registration
```

### Dataverse Plugin

```
src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/
├── BaseProxyPlugin.cs                      # ✅ No changes needed
├── GetDocumentFileUrlPlugin.cs             # RENAME to GetFilePreviewUrlPlugin.cs
└── Spaarke.Dataverse.CustomApiProxy.csproj
```

**Plugin DLL Output**:
```
src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/bin/Release/net462/Spaarke.Dataverse.CustomApiProxy.dll
```

### PCF Control (NEW)

```
src/controls/SpeFileViewer/
├── SpeFileViewer/                          # Control code
│   ├── components/
│   │   ├── FileViewer.tsx
│   │   ├── LoadingSpinner.tsx
│   │   └── ErrorMessage.tsx
│   ├── services/
│   │   └── CustomApiService.ts
│   ├── types/
│   │   └── types.ts
│   ├── generated/
│   ├── ControlManifest.Input.xml
│   └── index.ts
├── SpeFileViewerSolution/                  # Dataverse solution
├── docs/
├── package.json
└── SpeFileViewer.pcfproj
```

**PCF Solution Output**:
```
src/controls/SpeFileViewer/bin/Release/SpeFileViewer_1_0_0_0.zip
```

---

## 🔧 Common Commands

### Backend API

```bash
# Navigate to API project
cd c:/code_files/spaarke/src/api/Spe.Bff.Api

# Build
dotnet build

# Build Release
dotnet build -c Release

# Run tests
dotnet test

# Publish for deployment
dotnet publish -c Release -o ./publish
```

### Dataverse Plugin

```bash
# Navigate to plugin project
cd c:/code_files/spaarke/src/dataverse/Spaarke.CustomApiProxy

# Disable Directory.Packages.props (if needed)
mv ../../Directory.Packages.props ../../Directory.Packages.props.disabled

# Build Release
dotnet build Plugins/Spaarke.Dataverse.CustomApiProxy/Spaarke.Dataverse.CustomApiProxy.csproj -c Release

# Restore Directory.Packages.props
mv ../../Directory.Packages.props.disabled ../../Directory.Packages.props

# DLL location
ls Plugins/Spaarke.Dataverse.CustomApiProxy/bin/Release/net462/Spaarke.Dataverse.CustomApiProxy.dll
```

### PCF Control

```bash
# Navigate to controls directory
cd c:/code_files/spaarke/src/controls

# Create new PCF project
pac pcf init --namespace Spaarke --name SpeFileViewer --template field --framework react --run-npm-install

# Navigate to project
cd SpeFileViewer

# Install dependencies
npm install @fluentui/react-components @fluentui/react-icons date-fns

# Create directory structure
cd SpeFileViewer
mkdir components services types

# Build PCF control
npm run build

# Start test harness
npm start watch

# Create solution package
cd ..
mkdir solutions
cd solutions
pac solution init --publisher-name Spaarke --publisher-prefix sprk
pac solution add-reference --path ..
msbuild /t:restore
msbuild /p:Configuration=Release
```

---

## 🌐 Azure Deployment

### Deploy SDAP BFF API

```bash
# Navigate to API project
cd c:/code_files/spaarke/src/api/Spe.Bff.Api

# Publish
dotnet publish -c Release -o ./publish

# Package
cd publish
tar -czf ../deploy.tar.gz *
cd ..

# Deploy to Azure
az webapp deploy \
    --resource-group {your-rg} \
    --name spe-api-dev-67e2xz \
    --src-path deploy.tar.gz \
    --type targz \
    --async true

# Restart app service
az webapp restart \
    --resource-group {your-rg} \
    --name spe-api-dev-67e2xz
```

---

## 🔑 Key Names & IDs

### Dataverse

| Component | Name/Value |
|-----------|------------|
| **Custom API Unique Name** | `sprk_GetFilePreviewUrl` |
| **Custom API Display Name** | `Get File Preview URL` |
| **Entity** | `sprk_document` |
| **Plugin Assembly** | `Spaarke.Dataverse.CustomApiProxy` |
| **Plugin Class** | `GetFilePreviewUrlPlugin` |
| **Message** | `sprk_GetFilePreviewUrl` |
| **External Service Config Name** | `SDAP_BFF_API` |

### Custom API Parameters

**Output Parameters** (6 total):
1. `PreviewUrl` (String) - Ephemeral preview URL
2. `FileName` (String) - File name
3. `FileSize` (Integer) - File size in bytes
4. `ContentType` (String) - MIME type
5. `ExpiresAt` (DateTime) - URL expiration time
6. `CorrelationId` (String) - Request tracking ID

### PCF Control

| Property | Value |
|----------|-------|
| **Namespace** | `Spaarke` |
| **Name** | `SpeFileViewer` |
| **Bound Property** | `documentId` (sprk_documentid) |
| **Height** | `600` (pixels) |
| **Show File Name** | `true` |

---

## 🧪 Testing Commands

### Test Custom API (Browser Console)

```javascript
const documentId = Xrm.Page.data.entity.getId().replace(/[{}]/g, '');

Xrm.WebApi.online.execute({
    getMetadata: () => ({
        boundParameter: "entity",
        parameterTypes: {
            "entity": {
                "typeName": "mscrm.sprk_document",
                "structuralProperty": 5
            }
        },
        operationType: 1,
        operationName: "sprk_GetFilePreviewUrl"
    }),
    entity: {
        entityType: "sprk_document",
        id: documentId
    }
}).then(
    result => console.log("✅ Success!", result),
    error => console.error("❌ Error:", error.message)
);
```

### Query Audit Logs

```javascript
Xrm.WebApi.retrieveMultipleRecords(
    "sprk_proxyauditlog",
    "?$filter=sprk_operation eq 'GetFilePreviewUrl'&$orderby=sprk_executiontime desc&$top=10"
).then(
    result => console.log("📋 Audit Logs:", result.entities),
    error => console.error("Error:", error)
);
```

---

## 🗄️ PowerShell Commands

### Create External Service Config

```powershell
Import-Module Microsoft.Xrm.Data.PowerShell
$conn = Get-CrmConnection -InteractiveMode

$config = @{
    "sprk_name" = "SDAP_BFF_API"
    "sprk_baseurl" = "https://spe-api-dev-67e2xz.azurewebsites.net/api"
    "sprk_isenabled" = $true
    "sprk_authtype" = 1
    "sprk_tenantid" = "{your-tenant-id}"
    "sprk_clientid" = "{client-id}"
    "sprk_clientsecret" = "{client-secret}"
    "sprk_scope" = "https://spe-api-dev-67e2xz.azurewebsites.net/.default"
    "sprk_timeout" = 300
    "sprk_retrycount" = 3
    "sprk_retrydelay" = 1000
}

New-CrmRecord -conn $conn -EntityLogicalName "sprk_externalserviceconfig" -Fields $config
```

### Create Custom API

```powershell
$customApi = @{
    "uniquename" = "sprk_GetFilePreviewUrl"
    "name" = "Get File Preview URL"
    "displayname" = "Get File Preview URL"
    "description" = "Server-side proxy for getting SharePoint Embedded preview URLs"
    "bindingtype" = 1
    "boundentitylogicalname" = "sprk_document"
    "isfunction" = $true
    "isprivate" = $false
    "allowedcustomprocessingsteptype" = 0
}

$customApiId = New-CrmRecord -conn $conn -EntityLogicalName "customapi" -Fields $customApi
```

### Publish Customizations

```powershell
Publish-CrmAllCustomization -conn $conn
```

---

## 🔍 Debugging

### Check Plugin Trace Logs

1. Navigate to **Settings** → **Plug-in Trace Log** in Dataverse
2. Filter by message: `sprk_GetFilePreviewUrl`
3. Check for errors and correlation IDs

### Check Azure Application Insights

```bash
# Query API logs
az monitor app-insights query \
    --app {app-insights-name} \
    --analytics-query "requests | where timestamp > ago(1h) | where url contains 'preview-url'"
```

### Common File Paths to Check

```bash
# Verify files exist before building
ls c:/code_files/spaarke/src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs
ls c:/code_files/spaarke/src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetFilePreviewUrlPlugin.cs
ls c:/code_files/spaarke/src/controls/SpeFileViewer/package.json
```

---

## 📊 Validation Checklist

Quick checklist for each phase:

### Phase 1: Backend Updates
- [ ] SpeFileStore.GetPreviewUrlAsync() exists
- [ ] FileAccessEndpoints route is `/preview-url`
- [ ] Plugin renamed to GetFilePreviewUrlPlugin.cs
- [ ] Plugin DLL builds successfully
- [ ] No build errors

### Phase 2: Custom API Registration
- [ ] External Service Config exists (SDAP_BFF_API)
- [ ] Plugin assembly registered
- [ ] Custom API created (sprk_GetFilePreviewUrl)
- [ ] All 6 output parameters created
- [ ] Plugin step registered
- [ ] Customizations published
- [ ] Browser console test passes

### Phase 3: PCF Control Development
- [ ] Directory `src/controls/SpeFileViewer/` created
- [ ] All subdirectories created (components, services, types)
- [ ] All files created (7 files total)
- [ ] npm build succeeds
- [ ] Test harness works
- [ ] Solution package created

### Phase 4: Deployment
- [ ] SDAP BFF API deployed to Azure
- [ ] App service running
- [ ] PCF solution imported to Dataverse
- [ ] Control added to Document form
- [ ] Form published

### Phase 5: Testing
- [ ] 25+ test cases executed
- [ ] All tests passed
- [ ] Performance < 3 seconds
- [ ] Security validation passed

---

## 🎯 Time Estimates

| Phase | Duration |
|-------|----------|
| Phase 1: Backend | 2 hours |
| Phase 2: Registration | 1 hour |
| Phase 3: PCF Control | 3 hours |
| Phase 4: Deployment | 1.5 hours |
| Phase 5: Testing | 2 hours |
| **TOTAL** | **~10 hours** |

---

## 📞 Quick Links

- **[REPOSITORY-STRUCTURE.md](REPOSITORY-STRUCTURE.md)** - Complete repo organization
- **[SPE-FILE-VIEWER-IMPLEMENTATION-GUIDE.md](SPE-FILE-VIEWER-IMPLEMENTATION-GUIDE.md)** - Master guide
- **[STEP-1-BACKEND-UPDATES.md](STEP-1-BACKEND-UPDATES.md)** - Backend instructions
- **[STEP-2-CUSTOM-API-REGISTRATION.md](STEP-2-CUSTOM-API-REGISTRATION.md)** - Registration instructions
- **[STEP-3-PCF-CONTROL-DEVELOPMENT.md](STEP-3-PCF-CONTROL-DEVELOPMENT.md)** - PCF development
- **[STEP-4-DEPLOYMENT-INTEGRATION.md](STEP-4-DEPLOYMENT-INTEGRATION.md)** - Deployment guide
- **[STEP-5-TESTING.md](STEP-5-TESTING.md)** - Testing procedures

---

**Keep this page handy during implementation!** 📌
