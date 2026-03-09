# Custom Page Registration Guide — Legal Operations Workspace

> **Task**: 041 — Custom Page Deployment to MDA
> **Purpose**: Step-by-step guide for registering the Legal Operations Workspace
>              as a Power Apps Custom Page in the Dataverse dev environment
> **Environment**: https://spaarkedev1.crm.dynamics.com

---

## Overview

The Legal Operations Workspace is hosted as a **Power Apps Custom Page** — a standalone React 18
HTML web resource running in its own iframe within the Model-Driven App (MDA). The app is built
with Vite and bundled into a single `corporateworkspace.html` file.

> **Architecture**: Per ADR-026, full-page surfaces use standalone HTML web resources, NOT PCF
> controls. The Vite-built `corporateworkspace.html` in `src/solutions/LegalWorkspace/dist/`
> is the production artifact.

---

## Prerequisites

Before following this guide, ensure:

- [ ] `src/solutions/LegalWorkspace/` has been built (`npm run build`)
- [ ] The HTML web resource has been pushed to Dataverse:
  ```powershell
  pac webresource push --path dist/corporateworkspace.html --name sprk_corporateworkspace
  ```
- [ ] You have Power Apps Maker portal access with System Administrator or System Customizer role

---

## Step 1: Open the Solution in Power Apps Maker Portal

1. Navigate to [make.powerapps.com](https://make.powerapps.com)
2. In the top-right environment selector, choose **Spaarke Dev** (`spaarkedev1.crm.dynamics.com`)
3. Click **Solutions** in the left navigation
4. Find and click **SpaarkeLegalWorkspace** to open it

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
   - **Scale to fit**: Off (unchecked)
   - Set `Width = App.Width` and `Height = App.Height` for fill behavior

---

## Step 3: Add the HTML Web Resource

1. In the Custom Page editor, add an **HTML text** control or **iframe** control
2. Configure it to reference the `sprk_corporateworkspace` web resource
3. Set the control to fill the entire page:
   - **X**: `0`
   - **Y**: `0`
   - **Width**: `App.Width`
   - **Height**: `App.Height`

> The workspace uses CSS media queries to determine if it should render
> in 1-column (< 1024px) or 2-column (>= 1024px) grid layout.

---

## Step 4: Save and Publish the Custom Page

1. Click **File** → **Save** (or `Ctrl+S`)
2. Confirm the save dialog
3. Click **File** → **Publish** (or `Ctrl+Shift+S`)
4. Wait for the publish confirmation toast
5. Close the Custom Page editor tab

### Back in the Solution:
1. Return to the `SpaarkeLegalWorkspace` solution
2. Verify `sprk_LegalOperationsWorkspace` appears under **Apps** in the solution
3. Run `pac solution publish-all`

---

## Step 5: Register in MDA Sitemap

### Via App Designer (Recommended)

1. Go to [make.powerapps.com](https://make.powerapps.com) → **Apps**
2. Find the Spaarke MDA app → click `...` → **Edit**
3. In the App Designer, click the **Navigation** icon (sitemap) in the left panel
4. Click **+ Add** → **Subarea**
5. In the SubArea properties:
   - **Type**: Custom Page
   - **Custom Page**: Select `Legal Operations Workspace` from the dropdown
   - **Title**: Legal Workspace
   - **ID**: `sprk_legal_workspace`
   - **Order**: 1000
6. Drag the SubArea to the desired position in the navigation hierarchy
7. Click **Save** → **Publish App**

---

## Step 6: Verify the Custom Page Loads in MDA

1. Open the Model-Driven App in a browser (or refresh the existing MDA tab)
2. Hard refresh: `Ctrl+Shift+R` (clears cached resources)
3. Navigate to the **Legal Workspace** item in the MDA left navigation
4. The Legal Operations Workspace should load in the main content area

### Expected Loading Sequence

1. MDA navigation item clicked
2. MDA loads the Custom Page iframe
3. Custom Page loads the `corporateworkspace.html` web resource
4. React 18 root mounts via `createRoot`
5. `FluentProvider` wraps the app with MDA theme tokens
6. `WorkspaceGrid` renders 60/40 two-column layout
7. Async data loads (Xrm.WebApi queries + BFF API calls)
8. All blocks populated within ~2-3 seconds

---

## Authentication Flow Verification

### MSAL Token from MDA Context

The Custom Page acquires authentication from the MDA's Azure AD session:

```
User logs into MDA
    → Azure AD (MSAL) issues an access token for Dataverse
    → Custom Page runs in MDA iframe (same Azure AD session)
    → Web resource accesses Xrm.WebApi (same session context)
    → BFF API calls use acquired Bearer token
    → BFF WorkspaceAuthorizationFilter validates the Bearer token
    → BFF returns data with 200 OK
```

---

## Updates After Initial Registration

Once the Custom Page is registered, subsequent updates are deployed by rebuilding and pushing the web resource:

```powershell
# Build new bundle and push to Dataverse
cd src/solutions/LegalWorkspace
npm run build
pac webresource push --path dist/corporateworkspace.html --name sprk_corporateworkspace
pac solution publish-all
```

This pattern:
1. Rebuilds the single HTML file with all code inlined
2. Updates the web resource in Dataverse
3. No need to recreate the Custom Page or update the sitemap

---

*Updated 2026-02-18 — Reflects ADR-026 architecture (standalone HTML web resource, PCF removed)*
*See also: custom-page-definition.md, deployment-verification.md*
