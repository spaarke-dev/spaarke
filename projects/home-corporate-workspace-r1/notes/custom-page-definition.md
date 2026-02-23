# Custom Page Definition — Legal Operations Workspace

> **Task**: 040 — Solution Packaging for Dataverse
> **Purpose**: Specification for deploying the LegalWorkspace as a Power Apps Custom Page
> **Environment**: https://spaarkedev1.crm.dynamics.com

---

## Overview

The LegalWorkspace is a **standalone HTML web resource** deployed as a Power Apps Custom Page
within a Dataverse Model-Driven App (MDA). The app is built with Vite + React 18 and bundled
into a single `corporateworkspace.html` file using `vite-plugin-singlefile`.

> **Architecture Decision**: Per ADR-026, full-page surfaces use standalone HTML web resources,
> NOT PCF controls. The PCF scaffold (`src/client/pcf/LegalWorkspace/`) was removed on
> 2026-02-18 because the Vite-built HTML IS the production artifact.

---

## Custom Page Metadata

| Field | Value |
|-------|-------|
| **Name** | `sprk_LegalOperationsWorkspace` |
| **Display Name** | `Legal Operations Workspace` |
| **Description** | `Home Corporate Legal Operations dashboard — matters, events, to-do items, and AI briefings.` |
| **Type** | Custom Page (Power Apps) |
| **Web Resource** | `sprk_corporateworkspace` (HTML web resource) |
| **Solution** | `SpaarkeLegalWorkspace` (unmanaged) |
| **Source** | `src/solutions/LegalWorkspace/` |
| **Build Output** | `src/solutions/LegalWorkspace/dist/corporateworkspace.html` |

---

## Page Dimensions

| Setting | Value | Reason |
|---------|-------|--------|
| **Width** | Responsive fill (`Flexible width`) | Workspace adapts to MDA viewport |
| **Height** | Responsive fill (`Flexible height`) | Full-page layout with no scrollbars |
| **Orientation** | Landscape | Required for 2-column grid layout |
| **Scale to fit** | Off | App controls its own scaling via CSS `100vw/100vh` |

> The Custom Page iframe passes its full dimensions to the embedded web resource.
> `WorkspaceGrid.tsx` uses a CSS media query at 1024px for its 1-column/2-column layout switch.

---

## Deployment

### Build the HTML Bundle

```powershell
cd src/solutions/LegalWorkspace
npm run build
# Output: dist/corporateworkspace.html (single file, ~800 KB)
```

### Deploy as Web Resource

```powershell
# Push the HTML web resource to Dataverse
pac webresource push --path dist/corporateworkspace.html --name sprk_corporateworkspace
pac solution publish-all
```

### Updates

After code changes, rebuild and re-push the web resource:

```powershell
cd src/solutions/LegalWorkspace
npm run build
pac webresource push --path dist/corporateworkspace.html --name sprk_corporateworkspace
pac solution publish-all
```

No need to recreate the Custom Page or update the sitemap — the web resource is updated in place.

---

## Sitemap SubArea XML

Add to the Model-Driven App sitemap to expose the Custom Page as a navigation item:

```xml
<SubArea Id="sprk_legal_workspace"
         Title="Legal Operations Workspace"
         Icon="$webresource:sprk_workspace_icon_16"
         Type="PageType"
         PageType="custom"
         CustomPage="sprk_LegalOperationsWorkspace"
         CheckSecurity="false">
  <Titles>
    <Title LCID="1033" Title="Legal Workspace" />
  </Titles>
  <Descriptions>
    <Description LCID="1033" Description="Home Corporate Legal Operations dashboard" />
  </Descriptions>
</SubArea>
```

---

## Navigation Area

| Field | Value |
|-------|-------|
| **Area** | Legal Operations (existing `sprk_legal_operations` area) |
| **Group** | Workspace (new `sprk_workspace_group` group, or add to existing "Home" group) |
| **Order** | 1000 (first item in the Workspace group) |

---

## Verification Steps

After deployment:

| Check | Command / Action |
|-------|-----------------|
| Web resource exists | `pac webresource list \| Select-String sprk_corporateworkspace` |
| Custom Page accessible | Navigate to the SubArea in the MDA — workspace loads |
| Dark mode works | Toggle MDA theme — workspace responds with matching theme |
| Responsive layout | Resize browser — grid switches between 1-column and 2-column |
| No console errors | Open DevTools → Console → verify zero errors |

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Shows old version | Browser cache | Ctrl+Shift+R hard refresh |
| Custom Page blank | JavaScript error in bundle | Check browser console |
| Custom Page not in sitemap | Sitemap not updated | Update sitemap XML and republish app |
| Data missing | BFF not deployed | Deploy BFF via `Deploy-WorkspaceBff.ps1` |

---

*Updated 2026-02-18 — Reflects ADR-026 architecture (standalone HTML web resource, PCF removed)*
