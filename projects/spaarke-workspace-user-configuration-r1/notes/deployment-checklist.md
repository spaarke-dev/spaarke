# Deployment Checklist — Workspace User Configuration R1

> **Created**: 2026-03-29
> **Tasks**: WKSP-061, WKSP-062, WKSP-063
> **Environment**: Dev (spaarkedev1.crm.dynamics.com / spe-api-dev-67e2xz)

---

## Pre-Deployment

- [ ] All feature tasks complete (001-053)
- [ ] Integration tests passing (task 060)
- [ ] Code review passed (/code-review)
- [ ] ADR compliance verified (/adr-check)
- [ ] Branch merged to master or deployment branch ready

---

## 1. Deploy Workspace Layout Wizard Code Page (Task 061)

**Web Resource**: `sprk_workspacelayoutwizard`
**Source**: `src/solutions/WorkspaceLayoutWizard/`
**Skill**: `code-page-deploy` (two-step build pipeline)

### Build Steps
- [ ] `cd src/solutions/WorkspaceLayoutWizard`
- [ ] `npm install`
- [ ] `npm run build` (Vite + vite-plugin-singlefile produces `dist/index.html`)
- [ ] Verify `dist/index.html` exists and is a single HTML file with all JS/CSS inlined
- [ ] Verify no external asset references in the built file

### Deploy Steps
- [ ] Deploy web resource `sprk_workspacelayoutwizard` to Dataverse
- [ ] Use pattern from `scripts/Deploy-SpeAdminApp.ps1` (or code-page-deploy skill)
- [ ] Publish customizations in Dataverse after upload

### Verification
- [ ] Open wizard from workspace settings button via `Xrm.Navigation.navigateTo`
- [ ] Wizard Step 1 (template selection) renders correctly
- [ ] Wizard Step 2 (section selection) renders correctly
- [ ] Wizard Step 3 (drag-and-drop arrange) renders correctly
- [ ] Save flow completes successfully (calls BFF API)

---

## 2. Deploy BFF API (Task 062)

**App Service**: `spe-api-dev-67e2xz`
**Endpoint**: `https://spe-api-dev-67e2xz.azurewebsites.net`
**Script**: `scripts/Deploy-BffApi.ps1`

### Build Steps
- [ ] `dotnet build src/server/api/Sprk.Bff.Api/`
- [ ] `dotnet test` (all tests pass)

### Deploy Steps
- [ ] Run `scripts/Deploy-BffApi.ps1` to deploy to Azure App Service

### Verification
- [ ] Health check: `GET /healthz` returns 200
- [ ] Workspace layouts: `GET /api/workspace/layouts` returns 200 (may be empty array for new user)
- [ ] Sections catalog: `GET /api/workspace/sections` returns 200 with 5 registered sections
- [ ] Layout CRUD: Create, read, update, delete a workspace layout
- [ ] Authorization filter: Verify users can only access their own layouts

---

## 3. Deploy Corporate Workspace (Task 063)

**Web Resource**: `sprk_corporateworkspace`
**Source**: `src/solutions/LegalWorkspace/`
**Script**: `scripts/Deploy-CorporateWorkspace.ps1`

### Build Steps
- [ ] `cd src/solutions/LegalWorkspace`
- [ ] `npm install`
- [ ] `npm run build` (Vite build output)
- [ ] Verify build completes without errors

### Deploy Steps
- [ ] Run `scripts/Deploy-CorporateWorkspace.ps1` to upload `sprk_corporateworkspace` web resource
- [ ] Publish customizations in Dataverse after upload

### Verification
- [ ] Workspace loads in Dataverse without errors
- [ ] WorkspaceHeader component renders with layout dropdown
- [ ] Sections render dynamically based on active configuration
- [ ] Loading states (skeleton placeholders) display during fetch

---

## Post-Deployment End-to-End Verification

### Core Flows
- [ ] Create new workspace layout via wizard (all 3 steps complete)
- [ ] Switch between workspace layouts via header dropdown
- [ ] System workspace "Save As" creates a user copy
- [ ] Default workspace loads automatically on page navigation
- [ ] URL deep-linking with `?workspaceId=` parameter navigates to correct layout

### Section Behavior
- [ ] SmartToDo section works without ActivityFeed section present (no crash)
- [ ] All 5 registered sections render correctly: Get Started, Quick Summary, Latest Updates, My To Do List, My Documents
- [ ] Section visibility matches layout configuration

### UI/UX
- [ ] Dark mode renders correctly across wizard and workspace header
- [ ] Fluent UI v9 theming consistent throughout
- [ ] sessionStorage caching reduces redundant API calls on navigation
- [ ] Loading skeleton banners display during initial fetch
- [ ] Error fallback UI displays on API failure

---

## Rollback Plan

If deployment issues occur:
1. **BFF API**: Redeploy previous build via Azure App Service deployment slots or `Deploy-BffApi.ps1` with previous artifact
2. **Web Resources**: Re-upload previous version of `sprk_workspacelayoutwizard` / `sprk_corporateworkspace` and publish
3. **Database**: No schema migrations required; `sprk_workspacelayout` entity was created in earlier tasks

---

*Generated as deployment documentation for tasks WKSP-061, WKSP-062, WKSP-063.*
