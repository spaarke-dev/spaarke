# Task 1: Quick Start Guide

**Status:** Ready for Execution
**Time Required:** 30-45 minutes
**Prerequisites:** Power Apps Maker Portal access, PCF control deployed

---

## What You're About To Do

You will create a Custom Page dialog in Power Apps that embeds the UniversalDocumentUpload PCF control. This replaces the Quick Create form approach.

---

## Quick Steps

### 1. Access Power Apps Maker Portal
- URL: https://make.powerapps.com/
- Environment: **SPAARKE DEV 1**
- Take screenshot: `task1-step1-environment.png`

### 2. Verify PCF Control Exists
- Navigate to Solutions → Find UniversalQuickCreate control
- Should see: **Spaarke.Controls.UniversalDocumentUpload** (v2.2.0 or v2.3.0)
- If NOT found: Run `cd /c/code_files/spaarke/src/controls/UniversalQuickCreate && pac pcf push --publisher-prefix sprk`
- Take screenshot: `task1-step2-pcf-control.png`

### 3. Create Custom Page
- Solutions → Click solution → + New → Page → Blank page with columns
- Name: **Document Upload Dialog**
- Layout: Single column
- Take screenshot: `task1-step3-blank-page.png`

### 4. Configure as Dialog
- Click ... → Settings → Display tab
  - Type: **Dialog**
  - Width: **800 px**
  - Height: **600 px**
  - Position: **Center**
- General tab → Name: `sprk_documentuploaddialog`
- Take screenshot: `task1-step4-dialog-settings.png`

### 5. Add Parameters (CRITICAL - Exact Names!)
Settings → Parameters tab → Add 4 parameters:

| Name | Type | Required |
|------|------|----------|
| `parentEntityName` | Text | Yes |
| `parentRecordId` | Text | Yes |
| `containerId` | Text | Yes |
| `parentDisplayName` | Text | No |

Take screenshot: `task1-step5-parameters.png`

### 6. Add PCF Control
- Insert → Code/Custom tab → Find "UniversalDocumentUpload"
- Drag onto canvas
- Take screenshot: `task1-step6-control-added.png`

### 7. Bind Properties (CRITICAL!)
Select control → Properties panel → Bind each:

| Property | Formula/Value |
|----------|---------------|
| parentEntityName | `parentEntityName` (fx formula) |
| parentRecordId | `parentRecordId` (fx formula) |
| containerId | `containerId` (fx formula) |
| parentDisplayName | `parentDisplayName` (fx formula) |
| sdapApiBaseUrl | `https://spe-api-dev-67e2xz.azurewebsites.net` (NO /api) |

Take screenshot: `task1-step7-bindings.png`

### 8. Save and Publish
- Click Save
- Click Publish
- Verify name: `sprk_documentuploaddialog`
- Take screenshot: `task1-step8-published.png`

---

## After Creating Custom Page

### Test Parameter Passing

1. Open a Matter record in Dataverse (SPAARKE DEV 1)
2. Open browser DevTools (F12) → Console tab
3. Copy entire contents of `test-custom-page-navigation.js`
4. Paste into console and press Enter
5. Dialog should open with PCF control
6. Take screenshot of test results

### Expected Results
- Dialog opens centered
- PCF control renders
- No errors (except expected MSAL/API calls)
- Can close dialog (X button)

---

## Reference Files

**Detailed Instructions:**
[TASK-1-CUSTOM-PAGE-CREATION.md](TASK-1-CUSTOM-PAGE-CREATION.md) - 500+ lines with troubleshooting

**Custom Page Spec:**
[custom-page-definition.json](custom-page-definition.json) - Complete JSON definition

**Test Script:**
[test-custom-page-navigation.js](test-custom-page-navigation.js) - Browser console test

**Pre-Task Review:**
[TASK-1-PRE-REVIEW.md](TASK-1-PRE-REVIEW.md) - Prerequisites verified

---

## Screenshots Location

Save all screenshots to:
`dev/projects/quickcreate_pcf_component/testing/task1-screenshots/`

---

## Troubleshooting

### PCF Control Not Found
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
pac pcf push --publisher-prefix sprk
```

### Can't Bind Parameters (fx grayed out)
- Use formula syntax with equals: `=parentEntityName`
- Verify parameters created in Step 5
- Try removing and re-adding control

### Page Won't Save/Publish
- Check for name conflicts
- Verify all required properties bound
- Review browser console for errors

---

## Critical Reminders

- Parameter names are **case-sensitive**: `parentEntityName` NOT `ParentEntityName`
- sdapApiBaseUrl: **NO /api suffix** - PCF adds it internally
- Page name must be: `sprk_documentuploaddialog` (lowercase, no spaces)
- All 5 properties must be configured (4 bindings + 1 URL)

---

**Created:** 2025-10-20
**Task:** Task 1, Quick Start
**Version:** 1.0.0
**Status:** Ready for Execution
