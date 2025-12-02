# Archive - Historical Documentation

This folder contains documentation and scripts from **previous architectural approaches** that have been deprecated but are kept for historical reference.

## 📁 Contents

### Deprecated Approaches

1. **Form Dialog Approach (v2.1.0)**
   - `FORM-DIALOG-DEPLOYMENT-GUIDE.md` - Used sprk_uploadcontext utility entity
   - `PIVOT-TO-FORM-DIALOG-SUMMARY.md` - Documentation of pivot from Custom Page to Form Dialog
   - This approach was deprecated in favor of the current Custom Page with Timer-based close

2. **Manual Deployment Steps**
   - `MANUAL-DEPLOYMENT-STEPS.md` - Manual deployment instructions (pre-automation)
   - `MANUAL-ENTITY-CREATION-STEPS.md` - Manual entity setup steps
   - These have been superseded by automated PAC CLI deployment

3. **Cleanup Scripts**
   - `Delete-OldControl.ps1` - PowerShell script for removing old PCF versions from Dataverse

## 🎯 Current Approach (v3.0.5)

The **active** documentation is in the `../docs/` folder:
- `DEPLOYMENT-GUIDE.md` - Current deployment guide
- `QUICK-START-DEPLOYMENT.md` - Quick start instructions
- `RIBBON-LOCATIONS-GUIDE.md` - Ribbon configuration reference

## 🏗️ Current Architecture

**Custom Page Dialog with Timer-based Close:**
1. Ribbon button (sprk_subgrid_commands.js) opens Custom Page dialog
2. Custom Page hydrates PCF control with parameters via Param("data")
3. PCF control uploads files to SharePoint Embedded (SDAP BFF API)
4. PCF creates Document records in Dataverse (Xrm.WebApi)
5. PCF sets shouldClose output property to true
6. Custom Page Timer (Start bound to shouldClose) triggers Exit()
7. Dialog closes automatically after successful upload

## 📝 Version History

- **v3.0.5** (Current) - Custom Page with Timer-based close, clean implementation
- **v3.0.4** - Custom Page with shouldClose output property
- **v2.1.0** - Form Dialog approach with sprk_uploadcontext entity
- **v2.0.0** - Initial Custom Page approach (manual creation required)

## ⚠️ Important Note

**Do NOT use these archived documents for new implementations.** They are kept purely for historical reference and understanding the evolution of the solution.

For current implementation, see:
- `../docs/DEPLOYMENT-GUIDE.md`
- Source code in `../UniversalQuickCreate/index.ts`
