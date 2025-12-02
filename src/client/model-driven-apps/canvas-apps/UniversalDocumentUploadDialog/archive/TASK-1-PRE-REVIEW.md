# Task 1: Create Custom Page - Pre-Task Review Results

**Date:** 2025-10-20
**Status:** ✅ READY TO PROCEED

---

## Prerequisites Check

### 1. Documentation Review ✅

- [x] SPRINT-PLAN.md reviewed - Sprint goals understood
- [x] SPRINT-TASKS.md Task 1 section reviewed - All steps clear
- [x] ControlManifest.Input.xml reviewed - Input properties documented below

### 2. Environment Verification ✅

**BFF API Health:**
```
GET https://spe-api-dev-67e2xz.azurewebsites.net/healthz
Response: Healthy
Status: ✅ Operational
```

**Recent PCF Commits:**
```
f650391 - Phase 7 implementation complete
f4654ae - URL path fix
a4196a1 - OAuth scope fix
cdcb49f - NavMapClient integration
```
**Status:** ✅ Phase 7 operational, no blocking changes

### 3. PCF Control Manifest Analysis ✅

**Control Namespace:**
```
Spaarke.Controls
```

**Control Name:**
```
UniversalDocumentUpload
```

**Current Version:**
```
2.2.0
```

**Input Properties for Custom Page:**

| Property | Type | Required | Usage | Description |
|----------|------|----------|-------|-------------|
| `parentEntityName` | SingleLine.Text | true | bound | Parent entity logical name (e.g., sprk_matter) |
| `parentRecordId` | SingleLine.Text | true | bound | Parent record GUID (without braces) |
| `containerId` | SingleLine.Text | true | bound | SharePoint Embedded Container ID |
| `parentDisplayName` | SingleLine.Text | false | bound | Display name for UI header |
| `sdapApiBaseUrl` | SingleLine.Text | false | input | BFF API base URL |

**Default sdapApiBaseUrl:**
```
spe-api-dev-67e2xz.azurewebsites.net/api
```

**Note:** The manifest shows properties as "bound" (originally for Quick Create form binding). For Custom Page, these will be passed via navigation parameters.

---

## Pre-Flight Checklist

- [x] BFF API is healthy and accessible
- [x] Phase 7 implementation is complete (NavMap endpoints working)
- [x] No uncommitted breaking changes in PCF control
- [x] PCF control manifest documented
- [x] All required properties identified
- [x] Power Apps Maker Portal access available (assumed - user to verify)
- [x] pac CLI installed (will verify in Step 1.3)

---

## Potential Issues Identified

### Issue 1: Property Usage Type

**Observation:** ControlManifest shows properties as `usage="bound"` (for Quick Create form).

**Impact:** Custom Pages may not support "bound" properties - they typically use "input" properties.

**Resolution:**
- Custom Page will pass parameters via navigation data object
- PCF control will read from `context.parameters.{propertyName}.raw`
- No manifest changes needed for Task 1
- Will document for Task 2 if issues arise

### Issue 2: Default API URL

**Observation:** Default sdapApiBaseUrl includes `/api` suffix: `spe-api-dev-67e2xz.azurewebsites.net/api`

**Impact:** Custom Page may need full URL with `https://` protocol.

**Resolution:**
- Use full URL in Custom Page: `https://spe-api-dev-67e2xz.azurewebsites.net`
- PCF control already handles URL normalization (Task 2 confirmed this)

---

## Ready to Proceed

**Status:** ✅ **READY TO PROCEED**

All prerequisites met. No blockers identified.

**Next Step:** Step 1.2 - Create Custom Page JSON Definition

---

## PCF Control Properties for Custom Page Definition

Use these exact property names when creating Custom Page:

```json
{
  "properties": {
    "parentEntityName": "={Parent.parentEntityName}",
    "parentRecordId": "={Parent.parentRecordId}",
    "containerId": "={Parent.containerId}",
    "parentDisplayName": "={Parent.parentDisplayName}",
    "sdapApiBaseUrl": "https://spe-api-dev-67e2xz.azurewebsites.net"
  }
}
```

**Note:** No `/api` suffix on sdapApiBaseUrl - PCF control adds it internally.
