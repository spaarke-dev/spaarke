# MDA Sitemap Configuration — Legal Operations Workspace Custom Page

> **Task**: 041 — Custom Page Deployment to MDA
> **Purpose**: SubArea XML and sitemap structure for registering the Legal Operations Workspace
>              Custom Page in the Spaarke Model-Driven App navigation
> **Environment**: https://spaarkedev1.crm.dynamics.com

---

## Overview

The Legal Operations Workspace Custom Page (`sprk_LegalOperationsWorkspace`) must be registered
as a navigation item in the Model-Driven App (MDA) sitemap. This is done by adding a `<SubArea>`
element to the MDA's sitemap XML using Power Apps App Designer or by editing the sitemap via
the XML editor in make.powerapps.com.

Custom Pages in Dataverse sitemaps use `Type="PageType"` and `PageType="custom"` with the
`CustomPage` attribute pointing to the Custom Page's unique name.

---

## SubArea Element — Ready to Paste

```xml
<SubArea Id="sprk_legal_workspace"
         Title="Legal Operations Workspace"
         Icon="$webresource:sprk_workspace_icon_16"
         Type="PageType"
         PageType="custom"
         CustomPage="sprk_LegalOperationsWorkspace"
         CheckSecurity="false"
         Order="1000">
  <Titles>
    <Title LCID="1033" Title="Legal Workspace" />
  </Titles>
  <Descriptions>
    <Description LCID="1033" Description="Home Corporate Legal Operations dashboard — matters, events, and AI briefings." />
  </Descriptions>
</SubArea>
```

### Attribute Reference

| Attribute | Value | Meaning |
|-----------|-------|---------|
| `Id` | `sprk_legal_workspace` | Unique identifier for this nav item |
| `Title` | `Legal Operations Workspace` | Displayed in nav (fallback if Titles element is missing) |
| `Icon` | `$webresource:sprk_workspace_icon_16` | 16x16 icon webresource (see Icon Options below) |
| `Type` | `PageType` | Required for Custom Page navigation items |
| `PageType` | `custom` | Tells MDA this is a Custom Page (not an entity or URL) |
| `CustomPage` | `sprk_LegalOperationsWorkspace` | Unique name of the Custom Page in Dataverse |
| `CheckSecurity` | `false` | All users who can access the MDA can see this item |
| `Order` | `1000` | Position within the navigation group (lower = higher) |

> **Custom Page type code**: `10085` was an older API type code. The current sitemap XML approach
> uses `Type="PageType"` and `PageType="custom"` — this is the correct format for Power Apps
> sitemap XML as of 2024+. Do not use numeric type codes in sitemap XML.

---

## Icon Options

The `Icon` attribute references a webresource by name. Use one of these options:

### Option 1: Use existing Spaarke briefcase/workspace icon (preferred)
```xml
Icon="$webresource:sprk_workspace_icon_16"
```
Check if this webresource exists:
```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/webresourceset?$filter=name eq 'sprk_workspace_icon_16'
```

### Option 2: Use a similar existing Spaarke icon
```xml
Icon="$webresource:sprk_icon_briefcase_16"
```
or any other 16x16 PNG/SVG icon in the `sprk_` namespace.

### Option 3: Omit the Icon attribute (uses default page icon)
```xml
<SubArea Id="sprk_legal_workspace"
         Title="Legal Operations Workspace"
         Type="PageType"
         PageType="custom"
         CustomPage="sprk_LegalOperationsWorkspace"
         CheckSecurity="false"
         Order="1000">
```
This is acceptable for dev — the default Power Apps page icon will be shown.

---

## Full Sitemap Context

The SubArea must be placed inside an `<Area>` and `<Group>` in the sitemap.
Place it in the existing Legal Operations area, or create a new area:

### Placement in Existing Legal Operations Area

```xml
<Area Id="sprk_legal_operations" Title="Legal Operations" ShowGroups="true">
  <Group Id="sprk_workspace_group" Title="Workspace">
    <SubArea Id="sprk_legal_workspace"
             Title="Legal Operations Workspace"
             Type="PageType"
             PageType="custom"
             CustomPage="sprk_LegalOperationsWorkspace"
             CheckSecurity="false"
             Order="1000">
      <Titles>
        <Title LCID="1033" Title="Legal Workspace" />
      </Titles>
    </SubArea>
  </Group>
  <!-- existing navigation items below -->
</Area>
```

### Alternative: Add to Existing Home or Dashboard Group

If the MDA already has a home/overview group, add the SubArea there:

```xml
<Group Id="sprk_home_group" Title="Home">
  <!-- existing items -->
  <SubArea Id="sprk_legal_workspace"
           Title="Legal Operations Workspace"
           Type="PageType"
           PageType="custom"
           CustomPage="sprk_LegalOperationsWorkspace"
           CheckSecurity="false"
           Order="100" />
</Group>
```

---

## Sitemap Edit Methods

There are two ways to edit the sitemap:

### Method 1: Power Apps App Designer (Recommended — GUI)

1. Go to [make.powerapps.com](https://make.powerapps.com)
2. Select the **Spaarke Dev** environment
3. Click **Apps** in the left nav
4. Find your Model-Driven App → click the `...` menu → **Edit**
5. In the App Designer, click **Navigation** (sitemap icon in left panel)
6. Click **+ Add** → **Subarea**
7. Set:
   - **Type**: URL → then switch to **Custom Page**
   - **Custom Page**: Select `Legal Operations Workspace` from the dropdown
   - **Title**: Legal Workspace
   - **ID**: sprk_legal_workspace
8. Drag the SubArea into the desired Group/Area
9. Click **Save** → **Publish**

### Method 2: Sitemap Designer XML Editor

1. Go to [make.powerapps.com](https://make.powerapps.com) → **Solutions**
2. Open `SpaarkeLegalWorkspace` (or the MDA's owning solution)
3. Find the **Site Map** component → click to open the Sitemap Designer
4. Click the **XML Editor** button (top right of Sitemap Designer)
5. Paste the `<SubArea>` XML from above into the appropriate location
6. Click **Save** → **Publish**

### Method 3: Direct XML Export/Import (Advanced)

Export the solution containing the sitemap, edit `customizations.xml`, re-import:

```powershell
# Export solution (replace MySolution with the MDA's solution name)
pac solution export --path MySolution.zip --name MySolution --environment https://spaarkedev1.crm.dynamics.com

# Extract ZIP, edit customizations.xml, re-zip, re-import
pac solution import --path MySolution.zip --environment https://spaarkedev1.crm.dynamics.com --publish-changes
```

---

## Navigation Order and Hierarchy

Recommended hierarchy for the Spaarke MDA:

```
[Area] Legal Operations  (Id: sprk_legal_operations)
  [Group] Workspace  (Id: sprk_workspace_group)
    [SubArea] Legal Operations Workspace  ← NEW (Id: sprk_legal_workspace, Order: 1000)
  [Group] Matters
    [SubArea] My Matters
    [SubArea] All Matters
  [Group] Events
    [SubArea] My Events
    [SubArea] Events Calendar
  [Group] Documents
    [SubArea] Documents
```

The workspace is the entry point for legal operations managers and should appear
first (Order: 1000 = lowest within the group = first position).

---

## Page Title in MDA Header

The MDA displays the SubArea `<Title>` in the page header when the user navigates to the
Custom Page. Set a clear, short title:

- **Nav item label**: "Legal Workspace" (short, fits sidebar)
- **MDA page header**: "Legal Operations Workspace" (full name from `Title` attribute)

The Custom Page itself renders its own header bar — this MDA title appears above it.
To avoid duplicate headers, the Custom Page's internal header should complement rather
than repeat the MDA header.

---

## CORS Configuration for Custom Page Origin

The BFF API at `https://spe-api-dev-67e2xz.azurewebsites.net` must accept requests from
the Custom Page. The Custom Page origin in the MDA iframe is:

```
https://spaarkedev1.crm.dynamics.com
```

Verify the BFF `appsettings.json` (or App Service CORS configuration) allows this origin:

```json
// appsettings.json or App Service Configuration
"AllowedOrigins": [
  "https://spaarkedev1.crm.dynamics.com",
  "https://apps.powerapps.com",
  "https://*.powerapps.com"
]
```

> Custom Pages run in an iframe hosted under `*.powerapps.com` or the Dataverse org URL.
> The exact iframe origin depends on how the MDA loads the Custom Page. Adding both
> `spaarkedev1.crm.dynamics.com` and `*.powerapps.com` covers all scenarios.

---

## Authentication Token Flow

The Custom Page (PCF) acquires an MSAL token from the MDA's Azure AD session:

1. MDA authenticates the user with Azure AD via MSAL (handled by the MDA host)
2. PCF calls `context.client.getClient()` or uses the `Xrm.WebApi` auth context
3. For BFF calls, the PCF uses `msalInstance.acquireTokenSilent()` with the BFF app's
   client ID / scope
4. The token is sent as `Authorization: Bearer <token>` in BFF API requests
5. The BFF's `WorkspaceAuthorizationFilter` validates the token (ADR-008)

The BFF Azure AD app registration must include:
- **Redirect URI**: `https://spaarkedev1.crm.dynamics.com` (for token refresh in iframe)
- **Exposed API scope**: `api://<bff-client-id>/access_as_user` (or equivalent)

---

## Post-Sitemap-Update Verification

After updating the sitemap and publishing:

1. Hard refresh the MDA browser tab: `Ctrl+Shift+R`
2. The "Legal Workspace" item should appear in the MDA left navigation
3. Click it — the Custom Page loads in the main content area
4. Open browser DevTools (F12) → Console → no errors
5. Open DevTools → Network → filter by `api/workspace` → confirm 200 responses

---

*Created by Task 041 — Custom Page Deployment to MDA*
*See also: custom-page-registration.md, deployment-verification-checklist.md, custom-page-definition.md*
