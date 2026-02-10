# Events Custom Page - Deployment Guide

> **Version**: 1.0.0
> **Created**: 2026-02-04
> **Project**: Events Workspace Apps UX R1

---

## Overview

This guide documents how to deploy the Events Custom Page to Dataverse. The page replaces the OOB Events entity grid with a custom Calendar + Grid + Filters interface.

**Target Environment**: https://spaarkedev1.crm.dynamics.com

---

## Prerequisites

1. **Built assets available** in `dist/` folder:
   - `index.html` - Entry point
   - `assets/index-BvSqcEPg.js` - Main bundle (525 KB)

2. **PAC CLI authenticated**:
   ```powershell
   pac auth list  # Verify active auth to spaarkedev1
   ```

3. **Solution access**: Spaarke Core solution or equivalent

---

## Deployment Steps

### Step 1: Create Custom Page in Power Apps

1. Navigate to https://make.powerapps.com
2. Select environment: **SPAARKE DEV 1**
3. Go to **Solutions** > **Spaarke Core** (or your solution)
4. Click **+ New** > **More** > **Custom page**
5. Enter name: `sprk_eventspage`
6. Click **Create**

### Step 2: Upload Built Assets

The Custom Page is a Canvas App that hosts our React application.

**Option A: Embed as Web Resource (Recommended)**

1. In the Custom Page designer, add a **HTML text** control
2. Set the control's `HtmlText` property to:
   ```
   "<!DOCTYPE html>
   <html lang='en'>
   <head>
     <meta charset='UTF-8' />
     <style>
       html, body, #root { margin:0; padding:0; width:100%; height:100%; overflow:hidden; }
     </style>
   </head>
   <body>
     <div id='root'></div>
     <script type='module'>
       // Inline the bundle or load from web resource
       " & LookUp(WebResources, Name = 'sprk_EventsPage').Content & "
     </script>
   </body>
   </html>"
   ```

**Option B: Deploy as Web Resource First**

1. Deploy the JS bundle as a web resource:
   ```powershell
   # Create web resource for the bundle
   pac webresource push --path "dist/assets/index-BvSqcEPg.js" --name "sprk_eventspage_bundle" --type "Script"
   ```

2. Deploy the HTML as a web resource:
   ```powershell
   # Modify index.html to reference the web resource
   pac webresource push --path "dist/index.html" --name "sprk_eventspage" --type "Webpage"
   ```

3. In Custom Page, reference via HTML text control pointing to the web resource URL.

**Option C: Use iframe to Web Resource**

1. Add an **Image** control to the Custom Page
2. Set `Image` property to `""`
3. Use a **HTML text** control with iframe:
   ```
   "<iframe src='/webresources/sprk_eventspage.html' style='width:100%;height:100%;border:none;'></iframe>"
   ```

### Step 3: Configure Custom Page Layout

1. Size the HTML/iframe control to fill the screen:
   - `Width`: `Parent.Width`
   - `Height`: `Parent.Height`
   - `X`: `0`
   - `Y`: `0`

2. Remove any default controls/screens

3. Save the Custom Page

### Step 4: Publish the Custom Page

1. In Power Apps Studio, click **Publish**
2. Confirm the publish action
3. Wait for publish to complete

### Step 5: Update Sitemap Navigation

Follow the instructions in `infrastructure/dataverse/solutions/EventsSitemap/README.md`:

1. Open the Model-Driven App in App Designer
2. Find the "Events" navigation entry
3. Change Page type from "Entity" to "Custom page"
4. Select `sprk_eventspage`
5. Verify title is "Events" (not "My Events")
6. Save and Publish the app

### Step 6: Verify Deployment

1. Open the Model-Driven App
2. Click "Events" in the left navigation
3. Verify the Custom Page loads:
   - [ ] Calendar component visible on left
   - [ ] Grid component visible in main area
   - [ ] Filters toolbar visible in header
   - [ ] Version footer shows "v1.5.0"
4. Test dark mode:
   - [ ] Toggle browser dark mode preference
   - [ ] Verify colors adapt appropriately

---

## Build Artifacts

| File | Size | Description |
|------|------|-------------|
| `dist/index.html` | 662 bytes | Entry HTML |
| `dist/assets/index-BvSqcEPg.js` | 525.7 KB | Main React bundle |
| `dist/assets/index-BvSqcEPg.js.map` | 8.4 MB | Source map (debug) |

---

## Dark Mode Compliance (ADR-021)

All components verified to use Fluent UI v9 design tokens:

| Component | Token Usage | Dark Mode Support |
|-----------|-------------|-------------------|
| App.tsx | All tokens | FluentProvider with theme |
| CalendarSection.tsx | All tokens | Inherited from provider |
| GridSection.tsx | All tokens | Inherited from provider |
| AssignedToFilter.tsx | All tokens | Inherited from provider |
| RecordTypeFilter.tsx | All tokens | Inherited from provider |
| StatusFilter.tsx | All tokens | Inherited from provider |

**No hard-coded colors found in any component.**

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Blank page | JS not loading | Check web resource path |
| Console errors | Xrm not defined | Normal in dev, ignore |
| Theme not changing | Cache | Clear browser cache |
| Filters not working | Mock data | Normal outside Dataverse |

---

## Related Files

- **Source Code**: `src/solutions/EventsPage/src/`
- **Sitemap Config**: `infrastructure/dataverse/solutions/EventsSitemap/`
- **Task File**: `projects/events-workspace-apps-UX-r1/tasks/068-deploy-phase6.poml`

---

*Created for Events Workspace Apps UX R1 - Phase 6 Deployment*
