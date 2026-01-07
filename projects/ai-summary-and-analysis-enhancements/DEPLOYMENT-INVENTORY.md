# Deployment Inventory: UniversalQuickCreate PCF v3.10.0

> **Project:** AI Summary and Analysis Enhancements
> **Task:** 022 - Identify and Update Forms/Pages Using AI Summary
> **Date:** 2026-01-07
> **Version:** v3.10.0 (updated from v3.9.0)

---

## Executive Summary

The UniversalQuickCreate PCF control (v3.10.0) has been updated to use the new unified AI analysis endpoint. This document inventories all forms, custom pages, and deployment locations affected by this update.

**Breaking Change:** PCF now calls `/api/ai/analysis/execute` instead of `/api/ai/document-intelligence/analyze`. This requires coordinated deployment with the BFF API (Task 024).

---

## 1. Forms Using UniversalQuickCreate Control

The PCF control is accessed via command buttons ("Quick Create: Document") on Documents subgrids across multiple entity forms.

### Entity Forms Inventory

| Entity | Logical Name | Form Type | Subgrid Name (Typical) | Command Button Location |
|--------|--------------|-----------|------------------------|------------------------|
| **Matter** | `sprk_matter` | Main Form | `Subgrid_Documents` or `Documents` | Documents subgrid ribbon |
| **Project** | `sprk_project` | Main Form | `Subgrid_Documents` or `Documents` | Documents subgrid ribbon |
| **Invoice** | `sprk_invoice` | Main Form | `Subgrid_Documents` or `Documents` | Documents subgrid ribbon |
| **Account** | `account` | Main Form | `Subgrid_Documents` or `Documents` | Documents subgrid ribbon |
| **Contact** | `contact` | Main Form | `Subgrid_Documents` or `Documents` | Documents subgrid ribbon |

### Command Button Configuration

Each entity form has a command button with the following configuration:

- **Button Label**: "Quick Create: Document"
- **Web Resource**: `sprk_subgrid_commands.js`
- **JavaScript Function**: `Spaarke_AddMultipleDocuments(selectedControl)`
- **Pass Execution Context**: Yes
- **Deployment Method**: Ribbon Workbench OR Command Designer (Modern UI)

### Verification Steps (Per Entity)

1. Open entity record (e.g., Matter)
2. Scroll to **Documents** subgrid
3. Verify "Quick Create: Document" button is visible
4. Click button
5. Verify Custom Page dialog opens
6. Verify PCF control loads with version footer: **"v3.10.0 • Built 2026-01-07"**
7. Test file upload with AI Summary enabled
8. Verify AI Summary calls new endpoint successfully

---

## 2. Custom Pages Using the Control

### Custom Page: sprk_universaldocumentupload_page

| Property | Value |
|----------|-------|
| **Name** | `sprk_universaldocumentupload_page` |
| **Display Name** | "Universal Document Upload" |
| **Type** | Dialog |
| **Width** | 600px or 50% |
| **Height** | 80% |
| **Title** | "Quick Create: Document" |
| **PCF Control** | `sprk_Spaarke.Controls.UniversalDocumentUpload` v3.10.0 |

### Custom Page Parameters

The Custom Page defines these input parameters (passed from command button):

| Parameter Name | Data Type | Required | Description |
|---|---|---|---|
| `parentEntityName` | Text | Yes | Logical name of parent entity (e.g., sprk_matter, account) |
| `parentRecordId` | Text | Yes | GUID of parent record (without curly braces) |
| `containerId` | Text | Yes | SharePoint Embedded Container ID |
| `parentDisplayName` | Text | No | Display name for UI header (e.g., "Matter #12345") |

### PCF Control Property Bindings

The PCF control properties are bound to Custom Page parameters:

| PCF Property | Bound To | Example Value |
|--------------|----------|---------------|
| `parentEntityName` | `Parameters.parentEntityName` | "sprk_matter" |
| `parentRecordId` | `Parameters.parentRecordId` | "a1b2c3d4-..." |
| `containerId` | `Parameters.containerId` | "container-guid" |
| `parentDisplayName` | `Parameters.parentDisplayName` | "Matter #12345" |
| `sdapApiBaseUrl` | Static value | `"https://spe-api-dev-67e2xz.azurewebsites.net/api"` |

### Custom Page Deployment Notes

- **Deployment Method**: Manual creation in Power Apps Studio
  - Cannot be fully automated via PAC CLI
  - PCF control bundle must be copied manually to Custom Page
- **Version Location**: Embedded in Canvas App `.msapp` file
- **Critical**: Custom Page must be updated AFTER PCF is deployed to Dataverse Registry

---

## 3. Control Configuration Summary

### PCF Control Details

| Property | Value |
|----------|-------|
| **Control Name** | `sprk_Spaarke.Controls.UniversalDocumentUpload` |
| **Current Version** | v3.9.0 (deployed) |
| **New Version** | v3.10.0 (to deploy) |
| **Publisher Prefix** | `sprk` |
| **Deployment Method** | `pac pcf push --publisher-prefix sprk` |
| **Solution** | UniversalQuickCreate (unmanaged) |

### Version Locations (Updated in Task 021)

| Location | File | Line/Element | Updated? |
|----------|------|--------------|----------|
| 1. Source Manifest | `ControlManifest.Input.xml` | `version="3.10.0"` | ✅ Yes |
| 2. Solution Version | `Solution.xml` | `<Version>3.10.0</Version>` | ✅ Yes |
| 3. UI Footer | `DocumentUploadForm.tsx` | `v3.10.0 • Built 2026-01-07` | ✅ Yes |
| 4. Extracted Manifest | `out/controls/control/ControlManifest.xml` | Generated by build | ✅ Yes |

### API Endpoint Changes (Task 021)

| Aspect | Old Value (v3.9.0) | New Value (v3.10.0) |
|--------|-------------------|---------------------|
| **Endpoint URL** | `/api/ai/document-intelligence/analyze` | `/api/ai/analysis/execute` |
| **Request Format** | `{ documentId, driveId, itemId }` | `{ documentIds: [], playbookId, actionId, additionalContext }` |
| **Playbook Resolution** | N/A (hardcoded) | `GET /api/ai/playbooks/by-name/Document%20Profile` |
| **Response Chunks** | `type: "token"` | `type: "metadata", "chunk", "done", "error"` |
| **New Fields** | N/A | `analysisId`, `partialStorage`, `storageMessage` |

---

## 4. External Dependencies

### Azure Services

| Service | Endpoint | Purpose | Impact of PCF Update |
|---------|----------|---------|---------------------|
| **BFF API** | `https://spe-api-dev-67e2xz.azurewebsites.net` | API host for file upload and AI analysis | **Critical** - Must deploy API first |
| **Azure OpenAI** | `https://spaarke-openai-dev.openai.azure.com/` | AI Summary generation | None - API internal |
| **Document Intelligence** | `https://westus2.api.cognitive.microsoft.com/` | Document text extraction | None - API internal |

### Dataverse Components

| Component | Dependency Type | Impact |
|-----------|----------------|--------|
| **sprk_analysisplaybook** entity | Data dependency | PCF resolves "Document Profile" playbook by name |
| **sprk_aioutputtype** entity | Data dependency | API stores analysis outputs |
| **sprk_analysisoutput** entity | Data dependency | API stores generic outputs |
| **sprk_document** entity | Data dependency | API maps outputs to document fields |

### Web Resources

| Web Resource | Purpose | Update Required? |
|--------------|---------|-----------------|
| `sprk_subgrid_commands.js` | Command button logic | **No** - Does not call AI API directly |

---

## 5. Deployment Checklist

### Pre-Deployment Verification

- [ ] Task 020 complete: Old DocumentIntelligenceService removed from API
- [ ] Task 021 complete: PCF updated to new endpoint (v3.10.0)
- [ ] API build successful: `dotnet build src/server/api/Sprk.Bff.Api/`
- [ ] PCF build successful: `npm run build` (from UniversalQuickCreate folder)
- [ ] Version verified in 4 locations (all show 3.10.0)
- [ ] Document Profile playbook exists in Dev environment
- [ ] PAC CLI authenticated to Dev environment: `pac auth list`

### Deployment Steps (Coordinated - Task 024)

#### Step 1: Deploy API (FIRST - Removes Old Endpoint)

- [ ] Deploy BFF API to Azure App Service
  - Method: Azure DevOps pipeline OR `az webapp deploy`
  - Target: `spe-api-dev-67e2xz.azurewebsites.net`
  - Verification: `curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz`
- [ ] Verify old endpoint removed: `GET /api/ai/document-intelligence/analyze` → 404 Not Found
- [ ] Verify new endpoint exists: `GET /api/ai/playbooks/by-name/Document%20Profile` → 200 OK

#### Step 2: Deploy PCF to Dataverse (SECOND - Uses New Endpoint)

- [ ] Navigate to PCF directory
  ```bash
  cd /c/code_files/spaarke/src/client/pcf/UniversalQuickCreate
  ```
- [ ] Build production bundle
  ```bash
  npm run build:prod
  ```
- [ ] Disable Central Package Management
  ```bash
  mv /c/code_files/spaarke/Directory.Packages.props{,.disabled}
  ```
- [ ] Deploy to Dataverse
  ```bash
  pac pcf push --publisher-prefix sprk
  ```
- [ ] If file lock error, import directly
  ```bash
  pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
  ```
- [ ] Restore Central Package Management
  ```bash
  mv /c/code_files/spaarke/Directory.Packages.props{.disabled,}
  ```

#### Step 3: Verify PCF Version in Dataverse

- [ ] Check deployed version
  ```bash
  pac solution list | grep -i "UniversalQuickCreate"
  ```
- [ ] Expected: Version 3.10.0 or higher

#### Step 4: Update Custom Page (If Needed)

⚠️ **Custom Page Update Decision:**

**Option A: Do NOT update Custom Page bundle (Recommended for Quick Deploy)**
- Custom Page will use the updated PCF from Dataverse Registry
- Faster deployment (no Power Apps Studio required)
- **Risk**: Dataverse Registry may serve cached version briefly (~5-10 minutes)
- **Mitigation**: Hard refresh browser, wait for propagation

**Option B: Update Custom Page bundle (Full Deployment)**
- Ensures immediate availability of v3.10.0
- Requires Power Apps Studio manual steps
- Adds 15-20 minutes to deployment
- **See**: `docs/guides/PCF-CUSTOM-PAGE-DEPLOY.md`

**For Task 022/024: Use Option A** (no Custom Page update required)

#### Step 5: End-to-End Verification

Test on each entity form:

- [ ] **Matter Form**
  1. Open any Matter record
  2. Click "Quick Create: Document" button in Documents subgrid
  3. Verify Custom Page opens
  4. Verify footer shows: **"v3.10.0 • Built 2026-01-07"**
  5. Select 1 file, enable AI Summary
  6. Upload and verify AI Summary completes successfully
  7. Check browser console: Verify calls to `/api/ai/analysis/execute`
  8. Verify no errors in console

- [ ] **Project Form** (repeat above steps)
- [ ] **Invoice Form** (repeat above steps)
- [ ] **Account Form** (repeat above steps)
- [ ] **Contact Form** (repeat above steps)

#### Step 6: Monitor Application Insights

- [ ] Check Application Insights for errors
  - Filter: `timestamp > ago(30m)`
  - Filter: `customDimensions.Endpoint contains "analysis/execute"`
- [ ] Verify no 404 errors on old endpoint (expected)
- [ ] Verify no 500 errors on new endpoint

### Downtime Window

**Expected Downtime**: 5-10 minutes

- During API deployment: Old endpoint unavailable (404)
- During PCF deployment: Control may use old version briefly
- **User Impact**: Users who attempt AI Summary during this window will see error
- **Mitigation**: Deploy during low-usage hours (recommended: 8-10 AM ET)

---

## 6. Rollback Plan

### Scenario 1: API Deployment Fails

**Symptoms:**
- New endpoint `/api/ai/analysis/execute` returns 500 errors
- API healthcheck fails

**Rollback Steps:**
1. Revert API deployment to previous version
   ```bash
   az webapp deployment slot swap --slot staging --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
   ```
2. Verify old endpoint restored: `GET /api/ai/document-intelligence/analyze` → 200 OK
3. Do NOT deploy PCF (wait for API fix)

**Impact:** No user impact (PCF still on v3.9.0 calling old endpoint)

### Scenario 2: PCF Deployment Fails

**Symptoms:**
- `pac pcf push` fails with errors
- PCF not visible in Dataverse control registry

**Rollback Steps:**
1. API is already deployed (cannot easily rollback)
2. **Option A**: Fix PCF build and retry deployment
3. **Option B**: Emergency rollback
   - Export previous solution version (if available)
   - Import previous solution: `pac solution import --path UniversalQuickCreate_v3.9.0.zip`

**Impact:** Users cannot upload documents with AI Summary until fixed

### Scenario 3: PCF Deployed But Broken

**Symptoms:**
- PCF control loads but errors when calling API
- Browser console shows 404 on playbook resolution
- AI Summary fails for all users

**Diagnosis:**
1. Check browser console for errors
2. Verify API endpoint: `GET https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/playbooks/by-name/Document%20Profile`
3. Check if "Document Profile" playbook exists in Dev environment

**Rollback Steps:**

**Critical**: If this occurs, we have a backward compatibility issue. The old endpoint was removed in Task 020.

**Option A: Fix Forward (Preferred)**
1. Verify "Document Profile" playbook exists
   ```bash
   curl -H "Authorization: Bearer TOKEN" https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/playbooks/by-name/Document%20Profile
   ```
2. If missing, restore playbook from seed data
   ```bash
   # Run seed data script (if available)
   # OR manually create via Dataverse UI
   ```
3. Verify PCF works after playbook restored

**Option B: Emergency Rollback (Complex)**

This requires rolling back BOTH API and PCF:
1. Rollback API to previous version (before Task 020)
   - Re-deploy git commit before Task 020: `b21971a` (checkpoint before Task 020)
   - This restores old endpoint `/api/ai/document-intelligence/analyze`
2. Rollback PCF to v3.9.0
   - Import previous solution
3. Verify old flow works

**Duration**: 30-45 minutes for full rollback

### Rollback Decision Matrix

| Issue | Rollback Complexity | Recommended Action |
|-------|--------------------|--------------------|
| API 500 errors | Low | Rollback API slot swap |
| PCF build fails | Low | Fix and retry |
| Playbook not found | Low | Fix forward (create playbook) |
| PCF + API both broken | High | Fix forward or full rollback |

---

## 7. Communication Plan

### Before Deployment

**Notify:**
- Development team (Slack #dev-notifications)
- QA team for post-deployment testing
- Users (if deploying during business hours)

**Message Template:**
```
🚀 Deployment Notice: AI Summary Enhancement

What: Upgrading UniversalQuickCreate PCF control to v3.10.0
When: [Date/Time]
Duration: 5-10 minutes
Impact: AI Summary feature may be unavailable during deployment
Action Required: None - refresh browser after deployment

Questions? Contact #dev-support
```

### During Deployment

**Status Updates:**
- API deployed: ✅
- PCF deployed: ✅
- Verification complete: ✅

### After Deployment

**Success Message:**
```
✅ Deployment Complete: AI Summary v3.10.0

The AI Summary feature has been upgraded successfully.
- Enhanced playbook-based analysis
- Improved error handling
- Soft failure support

Please refresh your browser (Ctrl+Shift+R) to load the new version.

Known Issues: None
```

---

## 8. Post-Deployment Monitoring

### First 24 Hours

- [ ] Monitor Application Insights for increased error rates
- [ ] Check `/api/ai/analysis/execute` endpoint success rate
- [ ] Monitor AI Summary completion rate
- [ ] Review user feedback in #support channel

### Key Metrics to Track

| Metric | Target | Alert Threshold |
|--------|--------|----------------|
| AI Analysis Success Rate | > 95% | < 90% |
| Average Analysis Duration | < 30 seconds | > 60 seconds |
| Playbook Resolution Success | 100% | < 100% |
| PCF Load Time | < 3 seconds | > 5 seconds |

---

## 9. References

### Related Documentation

- `projects/ai-summary-and-analysis-enhancements/ARCHITECTURE-CHANGES.md` - Architecture overview
- `projects/ai-summary-and-analysis-enhancements/DECISION-BACKWARD-COMPATIBILITY.md` - Why no backward compatibility
- `src/client/pcf/UniversalQuickCreate/docs/DEPLOYMENT-GUIDE.md` - PCF deployment details
- `docs/guides/PCF-CUSTOM-PAGE-DEPLOY.md` - Custom Page deployment (if needed)
- `.claude/skills/dataverse-deploy/SKILL.md` - PAC CLI deployment procedures

### Task Files

- Task 020: Remove Old DocumentIntelligenceService (completed)
- Task 021: Update PCF to New Unified Endpoint (completed)
- **Task 022**: Identify and Update Forms/Pages (this task)
- Task 023: Integration Tests for New Endpoint
- Task 024: Deploy API + PCF Together (coordinated deployment)

---

**Document Status:** Draft - Ready for Task 024 deployment
**Last Updated:** 2026-01-07
**Owner:** AI Summary and Analysis Enhancements Project
