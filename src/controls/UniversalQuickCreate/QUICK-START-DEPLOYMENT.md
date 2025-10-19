# Quick Start Deployment - Form Dialog v2.1.0

## 5-Minute Deployment Guide

### Prerequisites
- PAC CLI authenticated to your environment
- Publisher prefix: `sprk`

---

## Step 1: Deploy Entity & Form (2 min)

```bash
cd c:\code_files\spaarke\src
pac solution pack --folder . --zipfile ..\UniversalQuickCreate_v2.1.0.zip
pac solution import --path ..\UniversalQuickCreate_v2.1.0.zip
```

**Wait for**: "Solution imported successfully"

---

## Step 2: Deploy PCF Control (1 min)

```bash
cd c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreate
npm run build
pac pcf push --publisher-prefix sprk
```

**Wait for**: "Successfully pushed control to Dataverse"

---

## Step 3: Deploy Web Resource (1 min)

```bash
cd c:\code_files\spaarke\scripts
bash deploy-all-components.sh
```

**Wait for**: "Solution deployed successfully"

---

## Step 4: Verify Form Configuration (1 min)

1. Power Apps → Solutions → Open your solution
2. Open `sprk_uploadcontext` entity → **Forms**
3. Open "Upload Documents" form
4. **Verify**: PCF control is present
5. **Verify**: Hidden fields section exists (sprk_parententityname, sprk_parentrecordid, sprk_containerid, sprk_parentdisplayname)
6. Click **Publish**

---

## Step 5: Test (<1 min)

1. Open existing **Matter** record (with sprk_containerid)
2. Scroll to **Documents** subgrid
3. Click **"Quick Create: Document"** button
4. **Expected**: Form Dialog opens with PCF control
5. Select files → Click Upload
6. **Expected**: Files upload, documents created, subgrid refreshes

---

## Troubleshooting

| Issue | Quick Fix |
|-------|-----------|
| Entity not found | Run Step 1 again, wait for import to complete |
| PCF shows "Missing parameters" | Verify form has hidden fields and PCF property bindings |
| Button doesn't appear | Re-publish customizations in Power Apps |
| Form doesn't open | Check console for errors, verify entity deployed |

---

## What Was Deployed

✅ **Entity**: `sprk_uploadcontext` (utility for parameter passing)
✅ **Form**: "Upload Documents" with PCF control
✅ **PCF**: UniversalDocumentUpload v2.0.1 (bound properties)
✅ **Web Resource**: sprk_subgrid_commands.js v2.1.0 (openForm)
✅ **Ribbon**: "Quick Create: Document" button (unchanged)

---

## Console Verification

When clicking button, you should see:
```
[Spaarke] AddMultipleDocuments: Starting v2.1.0 - FORM DIALOG APPROACH
[Spaarke] Parent Entity: sprk_matter
[Spaarke] Parent Record ID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
[Spaarke] Container ID: b!yLRdWEOAdkaWXskuRfByI...
[Spaarke] Form Options: {entityName: "sprk_uploadcontext", ...}
[Spaarke] Form Parameters: {sprk_parententityname: "sprk_matter", ...}
```

---

## Next Steps After Deployment

- Test on Account, Contact, Project, Invoice entities
- Add support for additional entities (see FORM-DIALOG-DEPLOYMENT-GUIDE.md)
- Configure form ID for more reliable opening (optional)

---

**Full Documentation**: [FORM-DIALOG-DEPLOYMENT-GUIDE.md](./FORM-DIALOG-DEPLOYMENT-GUIDE.md)
