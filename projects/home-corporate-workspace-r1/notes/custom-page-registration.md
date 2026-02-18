# Custom Page Registration Guide — Legal Operations Workspace

> **Task**: 041 — Custom Page Deployment to MDA
> **Purpose**: Step-by-step guide for registering the Legal Operations Workspace
>              as a Power Apps Custom Page in the Dataverse dev environment
> **Environment**: https://spaarkedev1.crm.dynamics.com

---

## Overview

The Legal Operations Workspace is hosted as a **Power Apps Custom Page** — a React 18 app
running in its own iframe within the Model-Driven App (MDA). The PCF control
(`sprk_Spaarke.Controls.LegalWorkspace`) is embedded into the Custom Page, which provides
the React 18 runtime exception needed for the workspace's concurrent React features.

Custom Pages cannot be created programmatically via PAC CLI or the Dataverse Web API.
They must be **created once** through the Power Apps Maker Portal. Subsequent PCF updates
(new bundles) are deployed via solution import — the Custom Page itself is not recreated.

---

## Prerequisites

Before following this guide, ensure:

- [ ] Task 040 (Solution Packaging) is complete
- [ ] `SpaarkeLegalWorkspace` solution ZIP exists in `src\client\pcf\LegalWorkspace\Solution\bin\`
- [ ] The solution has been imported to Dataverse:
  ```powershell
  scripts\Deploy-LegalWorkspaceCustomPage.ps1
  ```
- [ ] You have Power Apps Maker portal access with System Administrator or System Customizer role
- [ ] The PCF control `sprk_Spaarke.Controls.LegalWorkspace` is visible in
  make.powerapps.com → Solutions → SpaarkeLegalWorkspace → Custom Controls

---

## Step 1: Open the Solution in Power Apps Maker Portal

1. Navigate to [make.powerapps.com](https://make.powerapps.com)
2. In the top-right environment selector, choose **Spaarke Dev** (`spaarkedev1.crm.dynamics.com`)
3. Click **Solutions** in the left navigation
4. Find and click **SpaarkeLegalWorkspace** to open it

> The solution should contain the custom control `Spaarke.LegalWorkspace` under
> **Custom controls**. If it's missing, re-import the solution ZIP first.

---

## Step 2: Create the Custom Page (One-Time Setup)

> **This step is only needed once.** If `sprk_LegalOperationsWorkspace` already exists
> in the solution, skip to Step 3.

1. Inside the `SpaarkeLegalWorkspace` solution, click **+ New**
2. Select **App** → **Page** → **Custom page**
3. The Custom Page editor (Power Apps Studio) opens in a new tab

### Configure Page Properties

In the Custom Page editor:

1. In the left panel, click the **App** object → Properties panel on the right:
   - **Name**: `sprk_LegalOperationsWorkspace`
   - **Display Name**: `Legal Operations Workspace`
   - **Description**: `Home Corporate Legal Operations dashboard — matters, events, to-do items, and AI briefings.`

2. Set **Page Size / Dimensions**:
   - Click **File** → **Settings** → **Screen size + orientation**
   - **Orientation**: Landscape
   - **Size**: Custom
   - **Width**: Leave blank or set to `0` for flexible/fill (see below)
   - **Height**: Leave blank or set to `0` for flexible/fill
   - **Scale to fit**: Off (unchecked)

   > For fill behavior: In the Screen properties panel, set `Width = App.Width` and
   > `Height = App.Height`. This causes the Custom Page to fill the entire iframe.
   > The PCF receives `allocatedWidth` and `allocatedHeight` from the MDA runtime,
   > which drives the responsive grid breakpoint in `WorkspaceGrid.tsx`.

---

## Step 3: Insert the PCF Control

1. In the Custom Page editor, ensure the `Screen1` is selected in the tree view
2. Click **+ Insert** in the top toolbar
3. Scroll to or search for **Code components**
4. Select `Spaarke.LegalWorkspace` (from the SpaarkeLegalWorkspace solution)
   - If not visible, click **Get more components** → switch to **Import components** → find it

5. The PCF control is inserted onto the canvas

### Size the PCF to Fill the Page

1. Select the PCF control on the canvas
2. In the right Properties panel, set:
   - **X**: `0`
   - **Y**: `0`
   - **Width**: `App.Width` (formula — type this in the formula bar)
   - **Height**: `App.Height` (formula)
3. Verify the PCF fills the entire canvas area

> The workspace uses `context.mode.allocatedWidth` to determine if it should render
> in 1-column (< 1200px) or 2-column (>= 1200px) grid layout. The Custom Page must
> pass its full width to the PCF for this to work correctly.

---

## Step 4: Configure Page Display Properties

### Page Title (shown in MDA header)

The MDA displays the SubArea title in its header — not the Custom Page's internal title.
The Custom Page's own header renders inside the iframe.

In the Custom Page editor App properties:
- The `DisplayName` set in Step 2 becomes the MDA header text when this page is open
- Set it to: `Legal Operations Workspace`

### Responsive Dimensions Configuration

The workspace is designed for these viewport widths:
- **Minimum**: 1024px (single-column layout)
- **Optimal**: 1440px+ (two-column layout)
- **Maximum**: 1920px+ (two-column, max-width capped at 1800px by CSS)

The Custom Page passes `allocatedWidth` to the PCF. No fixed dimensions should be set
in the Custom Page — use flexible fill formulas (`App.Width`, `App.Height`).

---

## Step 5: Save and Publish the Custom Page

1. Click **File** → **Save** (or `Ctrl+S`)
2. Confirm the save dialog
3. Click **File** → **Publish** (or `Ctrl+Shift+S`)
4. Wait for the publish confirmation toast
5. Close the Custom Page editor tab

### Back in the Solution:
1. Return to the `SpaarkeLegalWorkspace` solution
2. Verify `sprk_LegalOperationsWorkspace` appears under **Apps** in the solution
3. Click the `...` menu on the app → **Publish** (if a separate publish option appears)

---

## Step 6: Run pac solution publish-all

After the Custom Page is created and published in the portal, run:

```powershell
pac solution publish-all --environment https://spaarkedev1.crm.dynamics.com
```

This ensures all customizations (sitemap, Custom Page registration, PCF) are fully published
and visible in the MDA.

---

## Step 7: Register in MDA Sitemap

The Custom Page must be added to the Model-Driven App's sitemap so users can navigate to it.

### Via App Designer (Recommended)

1. Go to [make.powerapps.com](https://make.powerapps.com) → **Apps**
2. Find the Spaarke MDA app → click `...` → **Edit**
3. In the App Designer, click the **Navigation** icon (sitemap) in the left panel
4. Click **+ Add** → **Subarea**
5. In the SubArea properties:
   - **Type**: Custom Page
   - **Custom Page**: Select `Legal Operations Workspace` from the dropdown
   - **Title**: Legal Workspace
   - **Subtitle**: Home Corporate Dashboard (optional)
   - **Icon**: Select or upload a 16x16 icon
   - **ID**: `sprk_legal_workspace`
   - **Order**: 1000
6. Drag the SubArea to the desired position in the navigation hierarchy
7. Click **Save** → **Publish App**

### Sitemap XML Reference

See `mda-sitemap-config.md` for the full `<SubArea>` XML element and sitemap hierarchy.

---

## Step 8: Verify the Custom Page Loads in MDA

1. Open the Model-Driven App in a browser (or refresh the existing MDA tab)
2. Hard refresh: `Ctrl+Shift+R` (clears cached resources)
3. Navigate to the **Legal Workspace** item in the MDA left navigation
4. The Legal Operations Workspace should load in the main content area

### Expected Loading Sequence

1. MDA navigation item clicked
2. MDA loads the Custom Page iframe
3. Custom Page loads Power Apps runtime + PCF bundle
4. PCF `init()` is called → React 18 root mounts
5. `FluentProvider` wraps the app with MDA theme tokens
6. `WorkspaceGrid` renders 7-block layout
7. Async data loads (Xrm.WebApi queries + BFF API calls)
8. All 7 blocks populated within ~2-3 seconds

---

## Authentication Flow Verification

### MSAL Token from MDA Context

The Custom Page acquires authentication from the MDA's Azure AD session:

```
User logs into MDA
    → Azure AD (MSAL) issues an access token for Dataverse
    → Custom Page runs in MDA iframe (same Azure AD session)
    → PCF calls BFF API using Xrm OAuth token or acquireTokenSilent
    → BFF WorkspaceAuthorizationFilter validates the Bearer token
    → BFF returns data with 200 OK
```

### Verifying the Auth Flow

In browser DevTools (F12) after the Custom Page loads:

1. **Network tab**: Filter by `api/workspace`
2. Check requests to `https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/*`
3. Verify:
   - Request headers include `Authorization: Bearer <token>`
   - Response status is `200 OK`
   - Response body contains expected JSON shape

4. **Console tab**: No `401 Unauthorized` or `CORS` errors

### If Auth Fails (401 from BFF)

Check in order:
1. **CORS**: BFF must allow `https://spaarkedev1.crm.dynamics.com` origin
   - See `appsettings.json` in `src/server/api/Sprk.Bff.Api/`
   - Set `AllowedOrigins` to include the Dataverse org URL

2. **Azure AD App Registration**:
   - The BFF app registration must have `https://spaarkedev1.crm.dynamics.com` as a
     redirect URI (for silent token acquisition in the iframe)
   - The PCF must request the correct audience/scope for the BFF

3. **WorkspaceAuthorizationFilter** (ADR-008):
   - Must accept the MSAL token format from Power Apps (not just Dataverse OBO tokens)
   - Verify the filter extracts the user OID from the token claims correctly

4. **Token Audience**:
   - The token issued for Dataverse may not be valid for the BFF
   - The PCF should request a token specifically scoped to the BFF API (separate acquireTokenSilent call)

---

## CORS Configuration for BFF

The BFF API at `https://spe-api-dev-67e2xz.azurewebsites.net` must allow cross-origin
requests from the Custom Page. Custom Pages can run under:

- `https://spaarkedev1.crm.dynamics.com` (Dataverse org URL)
- `https://apps.powerapps.com` (Power Apps platform)
- `https://*.powerapps.com` (wildcard for Power Apps subdomains)

In the BFF `appsettings.json`:

```json
"Cors": {
  "AllowedOrigins": [
    "https://spaarkedev1.crm.dynamics.com",
    "https://apps.powerapps.com",
    "https://make.powerapps.com"
  ]
}
```

Or in Azure App Service Configuration (preferred for secrets):
- Add an **Application Setting**: `Cors__AllowedOrigins` with the comma-separated origins

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Custom Page doesn't appear in "New > App > Page" | PCF not imported | Import SpaarkeLegalWorkspace solution first |
| PCF control not in Insert panel | Solution not active in environment | Check SpaarkeLegalWorkspace solution is imported |
| Custom Page blank (white screen) | PCF bundle error | Open DevTools → Console → check for JavaScript errors |
| "Could not load component" | PCF manifest mismatch | Verify bundle.js matches ControlManifest.xml version |
| BFF returns 401 | CORS or auth token issue | See Authentication Flow Verification above |
| BFF returns 403 | WorkspaceAuthorizationFilter rejecting token | Check token audience and OID claim extraction |
| Custom Page not in sitemap dropdown | Page not published | Edit + Save + Publish the Custom Page in Power Apps Studio |
| Layout doesn't fill viewport | Width/Height not set to App.Width/App.Height | Update formulas in Custom Page editor |
| 1-column layout always | allocatedWidth not passed correctly | Verify PCF's `context.mode.allocatedWidth` > 0 in debug |
| Fluent UI tokens missing | FluentProvider not wrapping app | Check `index.tsx` wraps root with `<FluentProvider theme={...}>` |

---

## Updates After Initial Registration

Once the Custom Page is registered, subsequent PCF bundle updates are deployed via solution import:

```powershell
# Build new bundle, pack ZIP, import to Dataverse — one command:
scripts\Package-LegalWorkspace.ps1 -Deploy

# After import, republish the Custom Page in Power Apps Maker Portal:
# Solutions > SpaarkeLegalWorkspace > Pages > sprk_LegalOperationsWorkspace
# → Edit → File → Save → File → Publish
```

This pattern:
1. Solution import updates the PCF bundle in Dataverse
2. Custom Page republish picks up the new PCF version
3. No need to recreate the Custom Page or update the sitemap

---

*Created by Task 041 — Custom Page Deployment to MDA*
*See also: mda-sitemap-config.md, deployment-verification-checklist.md, custom-page-definition.md*
*Reference script: scripts/Deploy-LegalWorkspaceCustomPage.ps1*
