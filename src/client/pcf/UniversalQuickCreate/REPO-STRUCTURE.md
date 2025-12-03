# Universal Quick Create - Repository Structure

Clean file structure for the Universal Document Upload PCF control and Custom Page solution (v3.0.5).

**Last Updated:** December 3, 2025 (Repository Restructure)

## 📂 Project Root

```
UniversalQuickCreate/
├── control/                        # PCF Control Source (TypeScript)
├── solution/                       # Dataverse Solution Package
├── docs/                           # Documentation
├── scripts/                        # Deployment Scripts
├── package.json                    # Node.js dependencies
├── tsconfig.json                   # TypeScript configuration
├── pcfconfig.json                  # PCF configuration
├── UniversalQuickCreate.pcfproj    # MSBuild project file
└── .gitignore                      # Git ignore rules
```

---

## 🎨 PCF Control Source (`control/`)

### Entry Point
- **`index.ts`** - Main PCF control (v3.0.5)
- `ControlManifest.Input.xml` - PCF manifest with input/output properties

### React Components
```
control/components/
├── DocumentUploadForm.tsx          # Main form container
├── FileSelectionField.tsx          # File picker UI
├── FilePickerField.tsx             # Alternative file picker
├── UploadProgressBar.tsx           # Upload progress display
└── ErrorMessageList.tsx            # Error message list
```

### Services Layer
```
control/services/
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
control/config/
├── EntityDocumentConfig.ts         # Entity-specific configuration
└── EntityFieldDefinitions.ts       # Field mapping definitions
```

### Type Definitions
```
control/types/
├── index.ts                        # Core types (ParentContext, etc.)
├── auth.ts                         # Authentication types
└── FieldMetadata.ts                # Metadata types
```

### Utilities
```
control/utils/
└── logger.ts                       # Logging utility (logInfo, logError, logWarn)
```

### Styles
```
control/css/
└── UniversalQuickCreate.css        # Component styles
```

### Localization
```
control/strings/
└── UniversalQuickCreate.1033.resx  # English resource strings
```

---

## 📦 Solution Package (`solution/`)

### Custom Pages
```
solution/src/canvaspages/
└── sprk_universaldocumentupload_page.json    # Custom Page definition
```

### Web Resources
```
solution/src/WebResources/
├── sprk_subgrid_commands.js                             # Ribbon button script (v3.0.4)
├── sprk_document_file_viewer.html                       # File viewer HTML
└── sprk_Spaarke.Controls.UniversalDocumentUpload/
    ├── bundle.js                                        # Compiled PCF control
    └── css/UniversalQuickCreate.css                     # Styles
```

### Solution Metadata
```
solution/src/Other/
├── Solution.xml                    # Solution metadata
├── Customizations.xml              # Customization metadata
└── Relationships.xml               # Entity relationships
```

### Project Files
- `solution/UniversalQuickCreateSolution.cdsproj` - Solution project file

---

## 📚 Documentation (`docs/`)

- **`DEPLOYMENT-GUIDE.md`** - Complete deployment guide
- **`QUICK-START-DEPLOYMENT.md`** - Quick start instructions
- **`RIBBON-LOCATIONS-GUIDE.md`** - Ribbon configuration reference
- `WEBRESOURCE-APPROACH.md` - Old approach (reference only)

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
PCF Control (control/index.ts)
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

## 🧹 Repository Restructure (December 2025)

The following changes were made to improve clarity:

### Folder Renames
- ✅ `UniversalQuickCreate/` → `control/` (eliminates ambiguous double-naming)
- ✅ `UniversalQuickCreateSolution/` → `solution/` (cleaner, consistent naming)

### Previously Deleted Files
- ❌ `UniversalDocumentUploadPCF.ts` (v2.0.0 - replaced by index.ts v3.0.5)
- ❌ `CustomPages/sprk_universaldocumentupload_page.json` (duplicate)
- ❌ `universal_document_upload.html` (deprecated HTML wrapper)

---

## 📋 Key Files Reference

| File | Purpose | Version |
|------|---------|---------|
| `control/index.ts` | PCF control entry point | v3.0.5 |
| `control/ControlManifest.Input.xml` | PCF manifest | v3.0.5 |
| `solution/src/canvaspages/sprk_universaldocumentupload_page.json` | Custom Page definition | v3.0.4 |
| `solution/src/WebResources/sprk_subgrid_commands.js` | Ribbon button script | v3.0.4 |
| `docs/DEPLOYMENT-GUIDE.md` | Deployment instructions | Current |

---

## 🎯 Development Guide

To enhance functionality with additional features:

1. **Review** the clean codebase structure
2. **Plan** new features (see feature requirements)
3. **Implement** in the appropriate layer:
   - UI changes → `control/components/`
   - Business logic → `control/services/`
   - Configuration → `control/config/`
   - Types → `control/types/`
4. **Update** documentation in `docs/`
5. **Test** end-to-end
6. **Deploy** via PAC CLI

---

## 📝 Notes

- **Build artifacts** (bin/, out/, *.zip) are gitignored
- **Node modules** are gitignored (run `npm install` to restore)
- **Generated files** (generated/, obj/) are gitignored
- Solution files use relative paths (`../*.pcfproj`) - no updates needed after rename

---

## 🔗 Related Documentation

- [SDAP Architecture Guide](../../../../docs/architecture/SDAP-ARCHITECTURE-GUIDE.md) - System-wide architecture
- [PCF Deployment Guide](docs/DEPLOYMENT-GUIDE.md) - Deployment instructions
