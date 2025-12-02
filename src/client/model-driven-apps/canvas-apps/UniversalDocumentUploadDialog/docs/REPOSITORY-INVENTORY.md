# Universal Quick Create - Repository Inventory
**Generated:** 2025-10-23
**Purpose:** Complete file structure inventory to identify duplicates and canonical sources

---

## 🚨 CRITICAL DUPLICATES IDENTIFIED

### sprk_subgrid_commands.js - **3 LOCATIONS**
1. **ROOT (Working Directory)** - `./sprk_subgrid_commands.js` - Modified: 2025-10-23 13:33
2. **Solution WebResources (Deployed)** - `./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/WebResources/sprk_subgrid_commands.js` - Modified: 2025-10-23 13:33
3. **Solution Source (Canonical)** - `./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js` - Modified: 2025-10-23 13:33

**RECOMMENDATION:**
- **Canonical source:** `./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js`
- **Delete:** Root copy (working directory)
- **Build artifact:** `WebResources/` copy is generated during build

### Custom Page Definition - **2 LOCATIONS**
1. `./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/CustomPages/sprk_universaldocumentupload_page.json` - Modified: 2025-10-10 14:34
2. `./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/canvaspages/sprk_universaldocumentupload_page.json` - Modified: 2025-10-10 15:57

**RECOMMENDATION:**
- **Canonical source:** `./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/canvaspages/sprk_universaldocumentupload_page.json` (newer)
- `CustomPages/` may be a build output

### Solution Package Zips - **3 ROOT LEVEL FILES**
1. `./UniversalQuickCreate_1_0_0_0_OLD.zip` - 2025-10-21 00:34 (explicitly marked OLD)
2. `./UniversalQuickCreate_3_0_0_0.zip` - 2025-10-21 00:36
3. `./UniversalQuickCreate_WithWebResource.zip` - 2025-10-21 00:57

**RECOMMENDATION:**
- Move to `./releases/` or `./dist/` folder
- Delete old versions or archive separately

---

## 📁 CANONICAL SOURCE STRUCTURE

### PCF Control Source (`./src/controls/UniversalQuickCreate/`)

#### Configuration Files (Root of PCF)
```
UniversalQuickCreate.pcfproj          2025-10-16 16:01  [MSBuild project]
package.json                           2025-10-21 00:14  [npm config]
package-lock.json                      2025-10-22 16:23  [npm lockfile]
tsconfig.json                          2025-10-06 23:46  [TypeScript config]
pcfconfig.json                         2025-10-06 23:46  [PCF specific config]
.gitignore                             2025-10-06 23:46
```

#### PCF Control Code (`./UniversalQuickCreate/`)
```
UniversalQuickCreate/
├── index.ts                           2025-10-21 21:25  [PCF entry point - LATEST EDIT]
├── UniversalDocumentUploadPCF.ts      2025-10-22 15:10  [Main PCF class - LATEST EDIT]
├── ControlManifest.Input.xml          2025-10-21 16:17  [PCF manifest - LATEST EDIT]
│
├── components/
│   ├── DocumentUploadForm.tsx         2025-10-21 00:10  [Main form component]
│   ├── FileSelectionField.tsx         2025-10-10 15:13
│   ├── FilePickerField.tsx            2025-10-07 00:00
│   ├── UploadProgressBar.tsx          2025-10-10 14:10
│   └── ErrorMessageList.tsx           2025-10-10 14:10
│
├── services/
│   ├── FileUploadService.ts           2025-10-07 00:14
│   ├── MultiFileUploadService.ts      2025-10-10 13:15
│   ├── SdapApiClient.ts               2025-10-08 12:37
│   ├── SdapApiClientFactory.ts        2025-10-16 16:13
│   ├── MetadataService.ts             2025-10-19 22:27
│   ├── NavMapClient.ts                2025-10-20 10:16
│   ├── DocumentRecordService.ts       2025-10-20 10:16
│   └── auth/
│       ├── MsalAuthProvider.ts        2025-10-15 10:11
│       └── msalConfig.ts              2025-10-18 11:07
│
├── config/
│   ├── EntityFieldDefinitions.ts     2025-10-07 00:29
│   └── EntityDocumentConfig.ts        2025-10-20 14:09
│
├── types/
│   ├── index.ts                       2025-10-10 13:12
│   ├── auth.ts                        2025-10-06 23:48
│   └── FieldMetadata.ts               2025-10-07 00:32
│
├── utils/
│   └── logger.ts                      2025-10-10 15:12
│
├── strings/
│   └── UniversalQuickCreate.1033.resx 2025-10-07 17:31
│
├── css/
│   └── UniversalQuickCreate.css       2025-10-06 23:47
│
└── generated/
    └── ManifestTypes.d.ts             2025-10-22 16:30  [Auto-generated]
```

#### Build Output (`./out/`)
```
out/controls/UniversalQuickCreate/
├── bundle.js                          2025-10-22 16:30  [Latest build]
├── bundle.js.LICENSE.txt              2025-10-22 16:30
├── ControlManifest.xml                2025-10-22 16:30
└── css/UniversalQuickCreate.css       2025-10-06 23:47
```

---

### Solution Project (`./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/`)

#### Solution Configuration
```
UniversalQuickCreateSolution.cdsproj   2025-10-16 12:53  [CDS project file]
.gitignore                             2025-07-31 16:13
```

#### Solution Source Files (`./src/`)
```
src/
├── canvaspages/
│   └── sprk_universaldocumentupload_page.json  2025-10-10 15:57  [✅ CANONICAL Custom Page]
│
├── WebResources/
│   ├── sprk_subgrid_commands.js                2025-10-23 13:33  [✅ CANONICAL Command Bar JS]
│   └── sprk_Spaarke.Controls.UniversalDocumentUpload/
│       ├── bundle.js                           2025-10-16 15:50
│       └── css/UniversalQuickCreate.css        2025-10-15 23:58
│
└── Other/
    ├── Solution.xml                            2025-10-19 22:11
    ├── Customizations.xml                      2025-10-16 00:08
    └── Relationships.xml                       2025-07-31 16:13
```

#### Build Output / Duplicates (`./WebResources/` and `./CustomPages/`)
```
WebResources/
├── sprk_subgrid_commands.js           2025-10-23 13:33  [⚠️ BUILD OUTPUT - DO NOT EDIT]
└── universal_document_upload.html     2025-10-11 09:07

CustomPages/
└── sprk_universaldocumentupload_page.json  2025-10-10 14:34  [⚠️ BUILD OUTPUT - DO NOT EDIT]
```

---

## 📚 DOCUMENTATION FILES

### Deployment & Operations (`./src/controls/UniversalQuickCreate/`)
```
DEPLOYMENT-GUIDE.md                    2025-10-10 16:07
MANUAL-DEPLOYMENT-STEPS.md             2025-10-10 16:29
RIBBON-LOCATIONS-GUIDE.md              2025-10-10 16:47
WEBRESOURCE-APPROACH.md                2025-10-11 09:08
FORM-DIALOG-DEPLOYMENT-GUIDE.md        2025-10-11 09:44
PIVOT-TO-FORM-DIALOG-SUMMARY.md        2025-10-11 09:46
QUICK-START-DEPLOYMENT.md              2025-10-11 09:46
MANUAL-ENTITY-CREATION-STEPS.md        2025-10-11 10:06
```

### PowerShell Scripts (`./src/controls/UniversalQuickCreate/`)
```
Delete-OldControl.ps1                  2025-10-15 09:15
Deploy-ToSolution.ps1                  2025-10-15 09:15
Upload-WebResources.ps1                2025-10-15 09:42
```

### Project Documentation (`./dev/projects/quickcreate_pcf_component/`)
```
SPRINT-PLAN.md                         2025-10-20 16:43
SPRINT-TASKS.md                        2025-10-20 16:48
SPRINT-TASKS-INDEX.md                  2025-10-20 17:44
SDAP-UI-CUSTOM-PAGE-ARCHITECTURE.md    2025-10-20 16:00
```

### Task Reports (Most Recent First)
```
FINAL-SOLUTION-CHECKLIST.md            2025-10-22 10:34
Custom-Page-Dialog-Diagnostic-and-Fix-10-22-2025.md  2025-10-22 11:47  [✅ LATEST DIAGNOSTIC]
EXPERT-CONSULT-PARAM-NOT-WORKING.md    2025-10-21 23:50
V3.0.2-FINAL-DEPLOYMENT-GUIDE.md       2025-10-21 22:52
V3.0.2-DEPLOYMENT-GUIDE.md             2025-10-21 21:31
EXPERT-REVIEW-FILES.md                 2025-10-21 21:10
CUSTOM-PAGE-CONFIGURATION-FIX.md       2025-10-21 16:23
FINAL-DIAGNOSTIC-CHECKLIST.md          2025-10-21 16:15
EXPERT-CONSULTATION-CUSTOM-PAGE-PARAMETERS.md  2025-10-21 13:47
PCF-VERSION-VERIFICATION.js            2025-10-21 13:11
PHASE-2-CORRECTED-FORMULAS.md          2025-10-21 12:25
CUSTOM-PAGE-PARAMETER-DEBUG.md         2025-10-21 12:23
REMEDIAL-PLAN-CHECKLIST.md             2025-10-21 11:49
PHASE-3-4-DEPLOYMENT-INSTRUCTIONS.md   2025-10-21 11:48
DEPLOYMENT-ISSUE-SUMMARY.md            2025-10-21 09:27
VERIFICATION-TEST-SCRIPT.md            2025-10-21 00:54
DEPLOYMENT-PACKAGE.md                  2025-10-21 00:38
TASK-4-COMPLETION-REPORT.md            2025-10-21 00:40
TASK-3-COMPLETION-REPORT.md            2025-10-21 00:30
TASK-2-COMPLETION-REPORT.md            2025-10-21 00:21
TASK-1-COMPLETION-REPORT.md            2025-10-20 23:33
```

### Archived Documentation (`./dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate/`)
```
README.md                              2025-10-07 11:12
FIELD-INHERITANCE-FLOW.md              2025-10-07 10:06
TASK-7B-3-CONFIGURABLE-FIELDS-UPDATED.md  2025-10-07 00:27
TASK-7B-4-TESTING-DEPLOYMENT.md        2025-10-06 23:31
TASK-7B-1-QUICK-CREATE-SETUP.md        2025-10-06 23:27
TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md    2025-10-05 22:29
```

---

## 🎯 CANONICAL FILE MAP (What to Edit)

| File Type | Canonical Location | ⚠️ DO NOT EDIT |
|-----------|-------------------|----------------|
| **PCF Entry Point** | `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts` | - |
| **PCF Main Class** | `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalDocumentUploadPCF.ts` | - |
| **PCF Manifest** | `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml` | - |
| **Command Bar Script** | `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js` | `./sprk_subgrid_commands.js` (root), `UniversalQuickCreateSolution/WebResources/sprk_subgrid_commands.js` |
| **Custom Page** | `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/canvaspages/sprk_universaldocumentupload_page.json` | `UniversalQuickCreateSolution/CustomPages/...` |
| **PCF Components** | `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/*.tsx` | - |
| **Services** | `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/**/*.ts` | - |

---

## 🧹 CLEANUP RECOMMENDATIONS

### Files to Delete (Root Level)
```bash
# Delete duplicate at root
rm ./sprk_subgrid_commands.js

# Move or archive solution packages
mkdir -p ./releases
mv ./UniversalQuickCreate_1_0_0_0_OLD.zip ./releases/
mv ./UniversalQuickCreate_3_0_0_0.zip ./releases/
mv ./UniversalQuickCreate_WithWebResource.zip ./releases/
```

### Build Artifacts (Auto-Generated - Don't Commit)
```
./src/controls/UniversalQuickCreate/out/
./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/WebResources/
./src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/CustomPages/
./src/controls/UniversalQuickCreate/UniversalQuickCreate/generated/
./src/controls/UniversalQuickCreate/UniversalQuickCreate.zip
```

---

## 📊 File Count Summary

| Category | Count | Last Modified |
|----------|-------|---------------|
| **PCF Source Files (TS/TSX)** | 17 | 2025-10-22 15:10 |
| **Solution Source Files** | 5 | 2025-10-23 13:33 |
| **Configuration Files** | 6 | 2025-10-22 16:23 |
| **Documentation Files** | 35+ | 2025-10-22 11:47 |
| **Build Scripts** | 3 | 2025-10-15 09:42 |
| **Archived Docs** | 6 | 2025-10-07 11:12 |

---

## ⚡ CURRENT WORK FOCUS

Based on modification dates, the most recent work has been on:

1. **Command Bar Script** (`sprk_subgrid_commands.js`) - 2025-10-23 13:33
2. **PCF Main Class** (`UniversalDocumentUploadPCF.ts`) - 2025-10-22 15:10
3. **Build Output** (bundle.js) - 2025-10-22 16:30
4. **Diagnostic Doc** (`Custom-Page-Dialog-Diagnostic-and-Fix-10-22-2025.md`) - 2025-10-22 11:47

**Issue:** Custom Page parameter hydration failure - PCF not receiving context parameters when dialog opens.

---

## 🔗 Key File References

### To Fix Current Issue (Parameter Hydration)
- Command Bar: [sprk_subgrid_commands.js](../../../src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js)
- PCF Control: [UniversalDocumentUploadPCF.ts:1-300](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalDocumentUploadPCF.ts#L1-L300)
- PCF Entry: [index.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts)
- Manifest: [ControlManifest.Input.xml](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml)
- Custom Page: [sprk_universaldocumentupload_page.json](../../../src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/canvaspages/sprk_universaldocumentupload_page.json)

### Diagnostic Documentation
- Latest: [Custom-Page-Dialog-Diagnostic-and-Fix-10-22-2025.md](Custom-Page-Dialog-Diagnostic-and-Fix-10-22-2025.md)
- Expert Consult: [EXPERT-CONSULT-PARAM-NOT-WORKING.md](EXPERT-CONSULT-PARAM-NOT-WORKING.md)
- Solution Checklist: [FINAL-SOLUTION-CHECKLIST.md](FINAL-SOLUTION-CHECKLIST.md)
