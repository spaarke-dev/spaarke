# Universal Quick Create - Repository Structure

Clean file structure for the Universal Document Upload PCF control and Custom Page solution (v3.0.5).

## 📂 Project Root

```
UniversalQuickCreate/
├── UniversalQuickCreate/           # PCF Control Source (TypeScript)
├── UniversalQuickCreateSolution/   # Dataverse Solution Package
├── docs/                           # Current Documentation
├── archive/                        # Historical Documentation (deprecated approaches)
├── scripts/                        # Deployment Scripts
├── package.json                    # Node.js dependencies
├── tsconfig.json                   # TypeScript configuration
├── pcfconfig.json                  # PCF configuration
├── UniversalQuickCreate.pcfproj    # MSBuild project file
└── .gitignore                      # Git ignore rules
```

---

## 🎨 PCF Control Source (`UniversalQuickCreate/`)

### Entry Point
- **`index.ts`** - Main PCF control (v3.0.5)
- `ControlManifest.Input.xml` - PCF manifest with input/output properties

### React Components
```
components/
├── DocumentUploadForm.tsx          # Main form container
├── FileSelectionField.tsx          # File picker UI
├── UploadProgressBar.tsx           # Upload progress display
└── ErrorMessageList.tsx            # Error message list
```

### Services Layer
```
services/
├── auth/
│   ├── MsalAuthProvider.ts         # MSAL authentication provider
│   └── msalConfig.ts               # MSAL configuration
├── SdapApiClient.ts                # SharePoint Embedded API client
├── SdapApiClientFactory.ts         # API client factory
├── MultiFileUploadService.ts       # Batch upload orchestration
├── FileUploadService.ts            # Individual file upload logic
├── DocumentRecordService.ts        # Dataverse record creation
├── MetadataService.ts              # Entity metadata retrieval
└── NavMapClient.ts                 # Navigation map service
```

### Configuration
```
config/
├── EntityDocumentConfig.ts         # Entity-specific configuration
└── EntityFieldDefinitions.ts       # Field mapping definitions
```

### Type Definitions
```
types/
├── index.ts                        # Core types (ParentContext, etc.)
├── auth.ts                         # Authentication types
└── FieldMetadata.ts                # Metadata types
```

### Utilities
```
utils/
└── logger.ts                       # Logging utility (logInfo, logError, logWarn)
```

### Styles
```
css/
└── UniversalQuickCreate.css        # Component styles
```

---

## 📦 Solution Package (`UniversalQuickCreateSolution/`)

### Custom Pages
```
src/canvaspages/
└── sprk_universaldocumentupload_page.json    # Custom Page definition
```

### Web Resources
```
src/WebResources/
├── sprk_subgrid_commands.js                             # Ribbon button script (v3.0.4)
└── sprk_Spaarke.Controls.UniversalDocumentUpload/
    ├── bundle.js                                        # Compiled PCF control
    └── css/UniversalQuickCreate.css                     # Styles
```

### Solution Metadata
```
src/Other/
├── Solution.xml                    # Solution metadata
├── Customizations.xml              # Customization metadata
└── Relationships.xml               # Entity relationships
```

### Project Files
- `UniversalQuickCreateSolution.cdsproj` - Solution project file

---

## 📚 Documentation (`docs/`)

### Current Documentation
- **`DEPLOYMENT-GUIDE.md`** - Complete deployment guide
- **`QUICK-START-DEPLOYMENT.md`** - Quick start instructions
- **`RIBBON-LOCATIONS-GUIDE.md`** - Ribbon configuration reference
- `WEBRESOURCE-APPROACH.md` - Old approach (reference only)

---

## 🗃️ Archive (`archive/`)

Historical documentation from deprecated approaches. **Do NOT use for new implementations.**

See [archive/README.md](archive/README.md) for details.

Contents:
- Form Dialog approach documentation (v2.1.0)
- Manual deployment steps (pre-automation)
- Cleanup scripts

---

## 🛠️ Scripts (`scripts/`)

Deployment automation scripts:
- `Deploy-ToSolution.ps1` - Deploy PCF to solution
- `Upload-WebResources.ps1` - Upload web resources

---

## 🏗️ Current Architecture (v3.0.5)

### Flow Diagram
```
Ribbon Button Click
  ↓
sprk_subgrid_commands.js
  • Gets parent context (form)
  • Retrieves container ID
  • Opens Custom Page dialog
  ↓
Custom Page (sprk_documentuploaddialog_e52db)
  • Hydrates parameters via Param("data")
  • Binds to PCF control properties
  • Timer watches shouldClose property
  ↓
PCF Control (index.ts)
  • Authenticates with MSAL
  • Renders file picker UI
  • Uploads files to SPE (SDAP API)
  • Creates Document records (Xrm.WebApi)
  • Sets shouldClose = true
  ↓
Custom Page Timer
  • Detects shouldClose = true
  • Calls Exit() to close dialog
  ↓
Ribbon Script
  • Refreshes subgrid
```

---

## 🧹 Deleted Files (Cleanup 2025-01-20)

The following deprecated files were removed:

### Deprecated PCF Control
- ❌ `UniversalQuickCreate/UniversalDocumentUploadPCF.ts` (v2.0.0 - replaced by index.ts v3.0.5)

### Duplicate Files
- ❌ `UniversalQuickCreateSolution/CustomPages/sprk_universaldocumentupload_page.json` (duplicate of src/canvaspages version)

### Old Web Resource Approach
- ❌ `UniversalQuickCreateSolution/src/WebResources/universal_document_upload.html` (deprecated HTML wrapper)

### Build Artifacts
- ❌ `bin/Release/UniversalQuickCreate.zip` (build output - now gitignored)
- ❌ `archive/UniversalQuickCreate.zip` (build output - now gitignored)

---

## 📋 Key Files Reference

| File | Purpose | Version |
|------|---------|---------|
| `UniversalQuickCreate/index.ts` | PCF control entry point | v3.0.5 |
| `UniversalQuickCreate/ControlManifest.Input.xml` | PCF manifest | v3.0.5 |
| `UniversalQuickCreateSolution/src/canvaspages/sprk_universaldocumentupload_page.json` | Custom Page definition | v3.0.4 |
| `UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js` | Ribbon button script | v3.0.4 |
| `docs/DEPLOYMENT-GUIDE.md` | Deployment instructions | Current |

---

## 🎯 Next Steps

To enhance functionality with additional features:

1. **Review** the clean codebase structure
2. **Plan** new features (see feature requirements)
3. **Implement** in the appropriate layer:
   - UI changes → `components/`
   - Business logic → `services/`
   - Configuration → `config/`
   - Types → `types/`
4. **Update** documentation in `docs/`
5. **Test** end-to-end
6. **Deploy** via PAC CLI

---

## 📝 Notes

- **Build artifacts** (bin/, out/, *.zip) are gitignored
- **Node modules** are gitignored (run `npm install` to restore)
- **Generated files** (generated/, obj/) are gitignored
- **Archive folder** contains historical reference only

For questions about deprecated approaches, see [archive/README.md](archive/README.md).
