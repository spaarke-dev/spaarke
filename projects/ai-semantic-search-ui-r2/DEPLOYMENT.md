# SemanticSearchControl Deployment Guide

> **Version**: 1.0.0
> **Last Updated**: 2026-01-21
> **Target Environment**: Dataverse (Power Platform)

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Build Process](#build-process)
3. [Solution Import](#solution-import)
4. [Control Configuration](#control-configuration)
5. [External Domain Requirements](#external-domain-requirements)
6. [Authentication Setup](#authentication-setup)
7. [Adding Control to Forms/Pages](#adding-control-to-formspages)
8. [Troubleshooting](#troubleshooting)
9. [Version Management](#version-management)

---

## Prerequisites

### Development Environment

| Requirement | Version | Notes |
|-------------|---------|-------|
| Node.js | 18.x or 20.x | For building PCF |
| npm | 9.x+ | Package management |
| PAC CLI | 1.46+ | `pac --version` |
| PowerShell | 5.1+ or Core | For pack.ps1 script |

### PAC CLI Installation

```powershell
# Install via dotnet tool
dotnet tool install --global Microsoft.PowerApps.CLI.Tool

# Verify installation
pac --version
```

### PAC CLI Authentication

```bash
# Authenticate to Dataverse environment
pac auth create --environment "https://spaarkedev1.crm.dynamics.com"

# Verify active profile
pac auth list
# Look for: Active = *, Environment URL = https://spaarkedev1.crm.dynamics.com/
```

---

## Build Process

### Step 1: Install Dependencies

```bash
cd src/client/pcf/SemanticSearchControl
npm install
```

### Step 2: Build Production Bundle

```bash
npm run build:prod

# Verify bundle size (should be < 1MB, typically ~27KB with platform libraries)
ls -la out/controls/SemanticSearchControl/bundle.js
```

### Step 3: Copy Build Artifacts to Solution

```bash
# Copy all 3 files to Solution folder
cp out/controls/SemanticSearchControl/bundle.js Solution/Controls/sprk_Sprk.SemanticSearchControl/
cp out/controls/SemanticSearchControl/ControlManifest.xml Solution/Controls/sprk_Sprk.SemanticSearchControl/
```

### Step 4: Pack Solution

```powershell
cd Solution
powershell -ExecutionPolicy Bypass -File pack.ps1
```

Output: `Solution/bin/SpaarkeSemanticSearch_v1.0.0.zip`

---

## Solution Import

### Standard Import

```bash
pac solution import --path "Solution/bin/SpaarkeSemanticSearch_v1.0.0.zip" --publish-changes
```

### Verify Import

```bash
# Check solution was imported
pac solution list | grep -i SpaarkeSemanticSearch

# Expected output:
# SpaarkeSemanticSearch  Spaarke Semantic Search  1.0.0  False
```

### Force Overwrite (if needed)

```bash
pac solution import --path "Solution/bin/SpaarkeSemanticSearch_v1.0.0.zip" --force-overwrite --publish-changes
```

---

## Control Configuration

### Control Properties

Configure these properties when adding the control to a form or Custom Page:

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `apiBaseUrl` | Text | No | (none) | BFF API base URL (e.g., `https://spe-api-dev-67e2xz.azurewebsites.net`) |
| `tenantId` | Text | No | (none) | Azure AD tenant ID for authentication |
| `searchScope` | Enum | No | `all` | Search scope: `all`, `matter`, or `custom` |
| `scopeId` | Text | No | (none) | ID for scoped search (bind to form field for matter-scoped) |
| `showFilters` | Boolean | No | `true` | Show/hide the filter panel |
| `resultsLimit` | Number | No | `25` | Number of results per load |
| `placeholder` | Text | No | "Search documents..." | Search input placeholder |
| `compactMode` | Boolean | No | `false` | Enable compact layout for form sections |

### Property Binding Examples

**Form Section (Matter-Scoped)**:
```
apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net"
tenantId: "your-tenant-id"
searchScope: "matter"
scopeId: [Bind to spe_matterid field]
showFilters: true
compactMode: true
```

**Custom Page (Full Experience)**:
```
apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net"
tenantId: "your-tenant-id"
searchScope: "all"
showFilters: true
compactMode: false
```

---

## External Domain Requirements

### Manifest Allowlist

The control's `ControlManifest.Input.xml` must allowlist all external domains the control calls:

```xml
<external-service-usage enabled="true">
  <domain>spe-api-dev-67e2xz.azurewebsites.net</domain>
  <domain>login.microsoftonline.com</domain>
</external-service-usage>
```

### Current Allowed Domains

| Domain | Purpose |
|--------|---------|
| `spe-api-dev-67e2xz.azurewebsites.net` | BFF API (Dev) |
| `login.microsoftonline.com` | Azure AD authentication |

### Adding New Environments

To support additional environments (test, prod):

1. Edit `ControlManifest.Input.xml`:
```xml
<external-service-usage enabled="true">
  <domain>spe-api-dev-67e2xz.azurewebsites.net</domain>
  <domain>spe-api-test-*.azurewebsites.net</domain>
  <domain>spe-api-prod-*.azurewebsites.net</domain>
  <domain>login.microsoftonline.com</domain>
</external-service-usage>
```

2. Rebuild the control
3. Update version numbers
4. Redeploy solution

### Future: Stable Custom Domain

For enterprise deployments, use a stable custom domain:

```xml
<external-service-usage enabled="true">
  <domain>api.spaarke.io</domain>
  <domain>login.microsoftonline.com</domain>
</external-service-usage>
```

Configure routing in Azure Front Door or APIM to route `/dev`, `/test`, `/prod` paths.

---

## Authentication Setup

### Prerequisites

1. Azure AD App Registration for BFF API
2. User must be authenticated to Dataverse
3. MSAL configured in control

### MSAL Configuration

The control uses MSAL for token acquisition:

1. **Silent Token**: Attempts `acquireTokenSilent()` first
2. **Interactive Fallback**: Falls back to `acquireTokenPopup()` if needed
3. **Token Refresh**: MSAL handles automatic refresh

### Required Permissions

The user's Azure AD account must have:

- Access to the BFF API app registration
- Appropriate Dataverse security roles for document access

### Tenant ID

Pass the Azure AD tenant GUID via the `tenantId` property:

```
tenantId: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
```

---

## Adding Control to Forms/Pages

### Option 1: Form Section

1. Open the entity form in Form Designer
2. Add a new section (or use existing)
3. Insert "Custom component"
4. Select "SemanticSearchControl" from the component library
5. Configure properties (see [Control Configuration](#control-configuration))
6. For matter-scoped search, bind `scopeId` to the matter ID field
7. Save and publish form

### Option 2: Custom Page

1. Create a new Custom Page (Canvas App)
2. Insert "Custom component"
3. Select "SemanticSearchControl"
4. Configure properties for full experience (`compactMode: false`)
5. Save and publish Custom Page
6. Add navigation button to open Custom Page (use ribbon-edit skill)

### Option 3: Dialog (Command Bar)

1. Create Custom Page for dialog mode
2. Configure command bar button using ribbon-edit skill
3. Use `Xrm.Navigation.navigateTo` with `target: 2` for modal dialog

---

## Troubleshooting

### Control Not Visible in Component Library

**Symptom**: Control doesn't appear after solution import

**Solutions**:
1. Verify solution import succeeded: `pac solution list`
2. Publish customizations: `pac solution publish`
3. Hard refresh browser: `Ctrl+Shift+R`
4. Clear browser cache

### Control Shows Old Version

**Symptom**: UI shows old version after update

**Solutions**:
1. **Critical**: Update `ControlManifest.Input.xml` version FIRST
2. Rebuild with updated version
3. Copy fresh bundle.js to Solution folder
4. Update solution.xml version
5. Reimport solution

**Nuclear option** (if still cached):
```bash
pac solution delete --solution-name SpaarkeSemanticSearch
pac solution import --path "Solution/bin/SpaarkeSemanticSearch_vX.Y.Z.zip" --publish-changes
```

### API Calls Fail

**Symptom**: Network errors when searching

**Solutions**:
1. Verify domain is in manifest allowlist
2. Check `apiBaseUrl` property is set correctly
3. Verify BFF API is running and accessible
4. Check browser console for CORS errors
5. Verify user is authenticated

### Authentication Errors

**Symptom**: Token acquisition fails

**Solutions**:
1. Verify `tenantId` property is correct
2. Check browser allows popups (for interactive auth)
3. Verify user has access to BFF API app registration
4. Check browser console for MSAL errors

### Solution Import Empty (0 Components)

**Symptom**: Solution imports but shows no components

**Root Cause**: `customizations.xml` missing component references

**Solution**:
```bash
# Use pac pcf push as fallback
cd src/client/pcf/SemanticSearchControl/SemanticSearchControl
pac pcf push --publisher-prefix sprk

# Then sync Solution folder with generated Customizations.xml
```

### Build Errors

**Symptom**: npm run build fails

**Solutions**:
1. Delete `node_modules` and reinstall: `npm ci`
2. Clear build output: `rm -rf out/ bin/`
3. Check Node.js version: `node --version` (18.x or 20.x)
4. Check for TypeScript errors in code

---

## Version Management

### Version Locations (5 places)

| # | Location | File |
|---|----------|------|
| 1 | Control Manifest | `SemanticSearchControl/ControlManifest.Input.xml` |
| 2 | UI Footer | `SemanticSearchControl/SemanticSearchControl.tsx` |
| 3 | Solution XML | `Solution/solution.xml` |
| 4 | Extracted Manifest | `Solution/Controls/sprk_Sprk.SemanticSearchControl/ControlManifest.xml` |
| 5 | Pack Script | `Solution/pack.ps1` |

### Version Update Order

**Critical**: Always update in this order:

1. `ControlManifest.Input.xml` (source) - **This is the cache key!**
2. `SemanticSearchControl.tsx` (UI display)
3. Rebuild: `npm run build:prod`
4. Copy to Solution folder (creates new ControlManifest.xml)
5. `solution.xml`
6. `pack.ps1`
7. Pack and import

### Semantic Versioning

```
MAJOR.MINOR.PATCH

1.0.0 → 1.0.1  (bug fix)
1.0.1 → 1.1.0  (new feature)
1.1.0 → 2.0.0  (breaking change)
```

---

## Quick Reference Commands

```bash
# Build
npm run build:prod

# Pack solution
cd Solution && powershell -File pack.ps1

# Import solution
pac solution import --path "bin/SpaarkeSemanticSearch_v1.0.0.zip" --publish-changes

# Verify import
pac solution list | grep SpaarkeSemanticSearch

# Delete solution (for clean reimport)
pac solution delete --solution-name SpaarkeSemanticSearch

# Publish customizations
pac solution publish

# Check PAC auth
pac auth list
```

---

*For additional help, see `.claude/skills/dataverse-deploy/SKILL.md`*
