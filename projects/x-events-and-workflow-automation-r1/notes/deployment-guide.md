# Events and Workflow Automation R1 - Deployment Guide

## Overview

This guide covers deployment of all components for the Events and Workflow Automation R1 project to development and other environments. Since this project consists of multiple interconnected components (Dataverse solution, PCF controls, and BFF API), all components must be deployed together to function correctly.

**Document Version**: 1.0
**Last Updated**: 2026-02-01
**Applicable Environments**: dev, test, staging, production

---

## Components to Deploy

### 1. Dataverse Solution (sprk_EventsWorkflowAutomation)

**Description**: Unmanaged Dataverse solution containing all custom tables and configuration.

**Contents**:
- Custom fields on sprk_event entity (Status, Notes, Regarding Record Type, etc.)
- sprk_eventtype entity and seed records (Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget)
- sprk_fieldmappingprofile entity (stores field mapping configurations)
- sprk_fieldmappingrule entity (individual mapping rules within profiles)
- sprk_eventlog entity (audit trail of state transitions)

**Solution Details**:
- Publisher: sprk (Spaarke)
- Unmanaged (per ADR-022)
- Size: ~500KB (approx.)
- Components: 5 tables, 1 form, 3 views, 0 plugins (validation in API/PCF only)

### 2. PCF Controls (5 Controls)

All PCF controls are built with React 16 APIs and Fluent UI v9 (per ADR-022, ADR-021).

| Control | Version | Target Entity | Purpose | Deployment Method |
|---------|---------|---------------|---------|-------------------|
| AssociationResolver | 1.0.0 | sprk_event | Select parent record, populate associated fields | pac pcf push or solution |
| EventFormController | 1.0.0 | sprk_event | Dynamic field visibility based on event type | pac pcf push or solution |
| RegardingLink | 1.0.0 | sprk_event (grid) | Navigate to related parent record | pac pcf push or solution |
| UpdateRelatedButton | 1.0.0 | parent forms | Push field mappings to child records | pac pcf push or solution |
| FieldMappingAdmin | 1.1.0 | sprk_fieldmappingrule | Manage field mapping rules | pac pcf push or solution |

**Common Deployment Requirements**:
- React 16 compatible platform (Dataverse)
- Fluent UI v9 design system
- Dark mode support (ADR-021)
- Bundle size: <2MB each (typical: 400-800KB)

### 3. BFF API Updates

Backend API hosted in Azure App Service (spe-api-dev-67e2xz).

**Endpoints Added**:

**Field Mapping Endpoints** (4 endpoints):
- `GET /api/v1/field-mappings?sourceEntity={entity}&targetEntity={entity}` - Get applicable mappings
- `GET /api/v1/field-mappings/{id}` - Get specific profile
- `POST /api/v1/field-mappings/validate` - Validate mapping rules before save
- `POST /api/v1/field-mappings/{id}/push` - Execute mapping push to child records

**Event Endpoints** (8 endpoints):
- `GET /api/v1/events` - List events
- `GET /api/v1/events/{id}` - Get specific event
- `POST /api/v1/events` - Create new event
- `PUT /api/v1/events/{id}` - Update event
- `DELETE /api/v1/events/{id}` - Delete event
- `POST /api/v1/events/{id}/complete` - Mark event complete
- `POST /api/v1/events/{id}/cancel` - Cancel event
- `GET /api/v1/events/{id}/log` - Get event audit log

**API Requirements**:
- .NET 8 Minimal API
- Authentication: Dataverse Bearer token (OnBehalfOf flow via app user)
- Endpoint filters for authorization (ADR-008)
- ProblemDetails error responses (ADR-019)

---

## Prerequisites

### Environment Requirements

| Component | Requirement | Details |
|-----------|-------------|---------|
| Dataverse | Cloud instance | https://spaarkedev1.crm.dynamics.com |
| Azure | App Service | spe-api-dev-67e2xz (westus2) |
| Azure | Key Vault | spaarke-spekvcert (for secrets) |
| Network | Connectivity | Both from deployment machine |

### CLI & Tools

| Tool | Version | Purpose | Install Command |
|------|---------|---------|-----------------|
| Power Platform CLI (pac) | Latest | Deploy solution, PCF controls | `dotnet tool install --global microsoft.powerapps.cli` |
| Azure CLI (az) | Latest | Deploy API, manage App Service | `az extension add -n storage-preview` |
| .NET SDK | 8.0+ | Build API | `dotnet --version` |
| npm | 16+ | Build PCF controls | `npm --version` |
| Node.js | 18+ | PCF build tooling | `node --version` |

### Authentication & Credentials

**Required Access**:
- [ ] Dataverse system administrator role (or custom role with import solution privilege)
- [ ] Azure subscription access (Contributor role on App Service)
- [ ] Power Platform environment admin access for environment-level settings
- [ ] Dataverse app user credentials for OnBehalfOf auth (BFF API)

**Credentials File** (create locally, never commit):
```json
{
  "dataverseEnvironmentUrl": "https://spaarkedev1.crm.dynamics.com",
  "appUserId": "{UUID of app user in Dataverse}",
  "appUserPassword": "[from Key Vault: bff-api-app-user-password]",
  "azureSubscriptionId": "YOUR_SUBSCRIPTION_ID",
  "azureResourceGroup": "spe-infrastructure-westus2",
  "azureApiAppService": "spe-api-dev-67e2xz"
}
```

---

## Deployment Steps

### Phase 1: Pre-Deployment Verification

```bash
# Check all prerequisites are met
pac auth list
# Expected: Multiple authentication profiles listed

az account show
# Expected: Correct subscription ID and environment

dotnet --version
# Expected: 8.0 or higher

npm --version
# Expected: 16.0 or higher
```

**Checklist**:
- [ ] Dataverse environment accessible via pac auth
- [ ] Azure subscription logged in
- [ ] All required CLIs installed and versions correct
- [ ] Network connectivity to both environments confirmed
- [ ] Credentials and secrets available in Key Vault

---

### Phase 2: Deploy Dataverse Solution

The Dataverse solution must be deployed first since PCF controls reference it.

#### Step 2.1: Export Solution (if updating existing)

```bash
# If updating existing environment, export current for backup
pac solution export \
  --environment-url https://spaarkedev1.crm.dynamics.com \
  --solution-input-file solutions/SprkEventsWorkflowAutomation.xml \
  --solution-output-file backups/SprkEventsWorkflowAutomation-$(date +%Y%m%d_%H%M%S).zip

# Unzip for inspection
unzip backups/SprkEventsWorkflowAutomation-*.zip -d backups/extracted/
```

#### Step 2.2: Import Unmanaged Solution

```bash
# Authenticate to target environment
pac auth create \
  --environment-url https://spaarkedev1.crm.dynamics.com \
  --username [admin@org.onmicrosoft.com] \
  --password [password from Key Vault]

# Import the solution
pac solution import \
  --environment-url https://spaarkedev1.crm.dynamics.com \
  --path solutions/SprkEventsWorkflowAutomation.zip \
  --run-asynchronously true \
  --force-overwrite true

# Monitor import progress
# In Dataverse: Settings → Solutions → Import → Check status
# Wait for: "Waiting for Organization to Import Selected Solution"
```

**Verification**:
- [ ] Solution imported successfully (no errors in Dataverse UI)
- [ ] All 5 tables visible in solution
- [ ] Form configuration preserved
- [ ] Event Type seed records present (8 records)

#### Step 2.3: Verify Dataverse Components

```powershell
# Navigate to https://spaarkedev1.crm.dynamics.com
# Check Solutions → sprk_EventsWorkflowAutomation

# Verify tables exist:
# - sprk_event (custom fields: Status, Notes, Regarding Record Type, etc.)
# - sprk_eventtype (should have 8 seed records)
# - sprk_fieldmappingprofile
# - sprk_fieldmappingrule
# - sprk_eventlog

# Verify form exists:
# - sprk_event main form (note: PCF controls will be added in Phase 3)
```

---

### Phase 3: Deploy PCF Controls

PCF controls must be deployed after the solution is imported (they reference solution entities).

#### Step 3.1: Build All PCF Controls

```bash
# Navigate to PCF controls directory
cd src/client/pcf

# Build each control (order doesn't matter for build, but for version bump see Step 3.2)
for control in AssociationResolver EventFormController RegardingLink UpdateRelatedButton FieldMappingAdmin; do
  cd $control
  npm run build
  # Expected: Successfully compiled to [control]/out/controls/[control]/[version]/bundle.js
  cd ..
done

# Verify all builds successful (no errors, warnings acceptable if non-critical)
```

**Build Verification**:
- [ ] AssociationResolver: out/controls/.../bundle.js ~500KB
- [ ] EventFormController: out/controls/.../bundle.js ~450KB
- [ ] RegardingLink: out/controls/.../bundle.js ~380KB
- [ ] UpdateRelatedButton: out/controls/.../bundle.js ~420KB
- [ ] FieldMappingAdmin: out/controls/.../bundle.js ~520KB

#### Step 3.2: Deploy Controls via PAC PCF Push

```bash
# For each control, navigate and deploy
cd src/client/pcf/AssociationResolver

# Deploy to dev environment
pac pcf push \
  --environment-url https://spaarkedev1.crm.dynamics.com \
  --publisher-prefix sprk

# Expected output: Control published successfully

# Repeat for other controls
cd ../EventFormController
pac pcf push --environment-url https://spaarkedev1.crm.dynamics.com --publisher-prefix sprk

cd ../RegardingLink
pac pcf push --environment-url https://spaarkedev1.crm.dynamics.com --publisher-prefix sprk

cd ../UpdateRelatedButton
pac pcf push --environment-url https://spaarkedev1.crm.dynamics.com --publisher-prefix sprk

cd ../FieldMappingAdmin
pac pcf push --environment-url https://spaarkedev1.crm.dynamics.com --publisher-prefix sprk
```

**Verification** (in Dataverse):
- [ ] All 5 controls appear in Solutions → sprk_EventsWorkflowAutomation → Controls
- [ ] Version numbers visible: All should be 1.0.0 (or 1.1.0 for FieldMappingAdmin)
- [ ] No import errors or warnings

#### Step 3.3: Configure Controls on Forms

Navigate to Dataverse web UI and configure forms:

**Event Main Form** (`sprk_event`):
```
Form Layout:
├─ General Tab
│  ├─ Event Type (field)
│  ├─ Name (field)
│  ├─ Description (field)
│  └─ Status (field)
├─ Parent Record Tab
│  ├─ Regarding Record Type (dropdown - AssociationResolver)
│  ├─ Regarding Record (lookup - auto-populated by AssociationResolver)
│  └─ Refresh from Parent (button - part of AssociationResolver)
├─ Mapped Fields Tab
│  ├─ [Dynamic fields shown/hidden by EventFormController based on Event Type]
│  └─ Dynamic field set shows/hides mapped fields
├─ Push to Children Tab
│  └─ Update Related (button - UpdateRelatedButton, pushes mappings to child records)
└─ Other Details Tab
   ├─ CreatedOn (system field)
   └─ Owner (system field)
```

**Steps**:
1. Open form editor: Dataverse → Tables → sprk_event → Forms → Main
2. Add control to "Regarding Record Type" section:
   - Component: AssociationResolver
   - Control Binding: sprk_regardingrecordtype (field)
3. Add control to dynamic fields section:
   - Component: EventFormController
   - Control Binding: Form context
4. Add control to "Push to Children" section:
   - Component: UpdateRelatedButton
   - Control Binding: sprk_event entity
5. Save and publish

**Field Mapping Rule Form** (`sprk_fieldmappingrule`):
1. Open form editor: Dataverse → Tables → sprk_fieldmappingrule → Forms → Main
2. Add control to form:
   - Component: FieldMappingAdmin
   - Control Binding: sprk_fieldmappingrule entity
3. Save and publish

---

### Phase 4: Deploy BFF API

#### Step 4.1: Build API

```bash
cd src/server/api/Sprk.Bff.Api

# Restore and build
dotnet restore
dotnet build --configuration Release

# Expected: ✅ Build succeeded. 0 Warning(s)

# Run tests to verify endpoints
dotnet test --configuration Release

# Expected: ✅ All tests passed
```

#### Step 4.2: Publish API

```bash
# Publish to release folder
dotnet publish --configuration Release --output ./publish

# Create deployment package
cd publish
zip -r ../Sprk.Bff.Api.zip . -x "*.pdb"
cd ..

# File size should be ~30-50MB (including dependencies)
```

#### Step 4.3: Deploy to Azure App Service

```bash
# Option A: Using Deploy-BffApi.ps1 script (recommended)
.\scripts\Deploy-BffApi.ps1 -Environment dev

# Script will:
# 1. Build and publish
# 2. Deploy to spe-api-dev-67e2xz
# 3. Run health check
# 4. Report success/failure

# Option B: Manual Azure CLI deployment
az webapp deployment source config-zip \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src Sprk.Bff.Api.zip

# Wait for deployment
# Check status: az webapp deployment list --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
```

**Verification**:
- [ ] Deployment succeeded (no errors in Azure portal)
- [ ] API running: `curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz`
- [ ] Expected response: `{"status": "healthy"}`

---

### Phase 5: Verify All Components Deployed

#### Step 5.1: Verify Event Form Configuration

In Dataverse web UI:
1. Create new Event record
2. Observe:
   - [ ] Event Type dropdown shows all 8 types
   - [ ] Regarding Record Type dropdown appears (AssociationResolver)
   - [ ] Selecting Matter populates Regarding Record (association resolves)
   - [ ] Dynamic fields show/hide based on Event Type (EventFormController)
   - [ ] Save form works without errors

#### Step 5.2: Verify Field Mapping Profile Form

In Dataverse web UI:
1. Create new Field Mapping Profile
2. Add Field Mapping Rules
3. Observe:
   - [ ] FieldMappingAdmin control loads without errors
   - [ ] Type compatibility validation works (text→lookup blocked)
   - [ ] Rules save successfully
   - [ ] Dark mode works (if dark mode enabled in Dataverse)

#### Step 5.3: Verify All Events View

In Dataverse web UI:
1. Navigate to All Events view
2. Observe:
   - [ ] RegardingLink control visible in grid
   - [ ] Clicking link navigates to parent record
   - [ ] No console errors

#### Step 5.4: Verify API Endpoints

```bash
# Test each endpoint category

# 1. Field Mapping GET
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/field-mappings?sourceEntity=account&targetEntity=sprk_event" \
  -H "Authorization: Bearer {token}"
# Expected: 200 OK with profiles array

# 2. Event GET list
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/events" \
  -H "Authorization: Bearer {token}"
# Expected: 200 OK with events array

# 3. Event CREATE
curl -X POST "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/events" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"name": "Test Event", "eventTypeId": "{guid}"}'
# Expected: 201 Created with event object

# 4. Field Mapping VALIDATE
curl -X POST "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/field-mappings/validate" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"sourceEntity": "account", "targetEntity": "sprk_event", "rules": [...]}'
# Expected: 200 OK with validation results

# 5. Field Mapping PUSH
curl -X POST "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/field-mappings/{id}/push" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"parentRecordId": "{guid}"}'
# Expected: 200 OK with push results
```

**Endpoint Verification Checklist**:
- [ ] All endpoints return 200/201 status (not 404, 500)
- [ ] Response times < 500ms
- [ ] Authentication works (Bearer token required)
- [ ] ProblemDetails error responses for invalid requests

---

### Phase 6: Smoke Test - Complete Event Workflow

Execute the complete workflow to verify all components work together:

#### Step 6.1: Create Event with Mapped Fields

```
1. In Dataverse, open Event form
2. Fill in basic fields:
   - Event Type: "Matter" (select from dropdown)
   - Name: "Test Event - Matter Case"
   - Description: "Smoke test for field mapping"
3. Select Parent Record:
   - Regarding Record Type: "Matter" (select from AssociationResolver)
   - Click "Find Matter" button
   - Select a Matter record
   - Click "Refresh from Parent" button
4. Observe:
   - [ ] Matter-specific fields appear (EventFormController shows fields)
   - [ ] 3-5 fields auto-populated from Matter record
   - [ ] Toast notification: "Applied X field mappings from Matter"
5. Save Event record
   - [ ] Save succeeds
   - [ ] Event log created (Dataverse audit)
```

#### Step 6.2: Verify Event Log

```
1. In Dataverse, open Event record created in Step 6.1
2. Check Related → Event Log
3. Observe:
   - [ ] Log entry for "Create" action
   - [ ] Timestamp correct
   - [ ] Status fields captured
```

#### Step 6.3: Test Update Related (Push to Children)

```
1. Assume Matter record has child records
2. On Event form, click "Update Related" button
3. In dialog, verify:
   - [ ] Identify source and target entities
   - [ ] Show field mapping rules
   - [ ] Display which child records will be updated
4. Click "Apply Mappings"
5. Observe:
   - [ ] Child records receive mapped field values
   - [ ] API logs show POST to /api/v1/field-mappings/{id}/push
   - [ ] Toast shows "Updated X child records"
```

#### Step 6.4: Test Dark Mode (if applicable)

```
1. In Dataverse, enable dark mode (Settings → Accessibility → Dark Mode)
2. Navigate to Event form and Field Mapping Profile form
3. Observe:
   - [ ] All controls respect dark mode theme
   - [ ] No hard-coded colors visible
   - [ ] Text contrast is readable
   - [ ] Fluent UI tokens applied correctly
```

**Smoke Test Result**:
- [ ] All steps complete without errors
- [ ] No console errors or warnings
- [ ] Performance is acceptable (< 3 seconds per operation)
- [ ] Event workflow end-to-end functional

---

## Post-Deployment Configuration

### 1. Create Initial Field Mapping Profiles (Optional)

For testing purposes, create sample profiles:

```
Profile: "Matter to Event"
  Rule 1: Matter.Description → Event.Description (Text → Text)
  Rule 2: Matter.MatterType → Event.Status (OptionSet → OptionSet)
  Rule 3: Matter.Owner → Event.Owner (User → User)

Profile: "Project to Event"
  Rule 1: Project.ProjectName → Event.Name (Text → Text)
  Rule 2: Project.Budget → Event.Notes (Text → Text)
```

### 2. Configure Event Type Options (if needed)

In Dataverse, verify Event Type records:
- [ ] 8 types created (Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget)
- [ ] Each type has logical name and display name
- [ ] Types used by EventFormController for field visibility

### 3. Set Up Event Log Retention (Optional)

For production environments, consider event log cleanup:
```csharp
// In API startup, add cleanup job:
services.AddHostedService<EventLogCleanupService>();
// Retains last 90 days of logs, archives older entries
```

---

## Verification Checklist

**Before declaring deployment complete, verify:**

| Component | Verification | Status |
|-----------|--------------|--------|
| **Dataverse Solution** | Imported successfully, 5 tables visible | ☐ |
| **Event Table** | Custom fields and form visible | ☐ |
| **EventType Records** | 8 seed records created | ☐ |
| **AssociationResolver** | Loads on Event form, allows parent selection | ☐ |
| **EventFormController** | Shows/hides fields based on event type | ☐ |
| **RegardingLink** | Visible in grid, navigation works | ☐ |
| **UpdateRelatedButton** | Visible on forms, push workflow works | ☐ |
| **FieldMappingAdmin** | Loads on Field Mapping Rule form | ☐ |
| **BFF API** | Responds to all 12 endpoints | ☐ |
| **Authentication** | Bearer token required, OnBehalfOf flow works | ☐ |
| **Event Creation** | End-to-end workflow completes | ☐ |
| **Field Mapping** | Mappings apply on parent selection | ☐ |
| **Event Log** | Audit trail created on state changes | ☐ |
| **Dark Mode** | All controls respect theme settings | ☐ |

---

## Troubleshooting

### Issue: Solution Import Fails

**Symptoms**: "Solution import failed" error in Dataverse

**Resolution**:
1. Check solution dependencies:
   ```bash
   pac solution list --environment-url https://spaarkedev1.crm.dynamics.com
   ```
2. Ensure all prerequisite solutions installed (if any)
3. Try importing with force-overwrite:
   ```bash
   pac solution import --path solutions/SprkEventsWorkflowAutomation.zip --force-overwrite true
   ```

### Issue: PCF Controls Not Visible After Deploy

**Symptoms**: Control not available in form editor

**Resolution**:
1. Verify deployment succeeded: Check pac pcf push output for errors
2. Reload form editor (F5 or refresh browser)
3. Verify control publisher is "sprk":
   ```bash
   pac pcf list --environment-url https://spaarkedev1.crm.dynamics.com
   ```
4. Re-deploy if needed:
   ```bash
   pac pcf push --environment-url https://spaarkedev1.crm.dynamics.com --publisher-prefix sprk --force-overwrite true
   ```

### Issue: API Endpoints Return 401 Unauthorized

**Symptoms**: Bearer token authentication fails

**Resolution**:
1. Verify app user exists in Dataverse:
   ```
   Dataverse → Settings → Security → Users → Find app user
   ```
2. Check app user has required role (typically System Administrator for R1)
3. Verify token is valid (not expired)
4. Test with postman using OnBehalfOf flow:
   ```
   GET /token?onBehalfOf={appUserId}
   ```

### Issue: Field Mappings Not Applying

**Symptoms**: Fields don't populate when parent record selected

**Resolution**:
1. Verify Field Mapping Profile exists:
   ```
   Dataverse → Tables → Field Mapping Profile → Check for records
   ```
2. Verify Field Mapping Rules are configured correctly
3. Check Event Type matches profile source entity
4. Verify type compatibility (use /validate endpoint to debug)
5. Check browser console for JavaScript errors
6. Check API logs for field-mappings/validate or push errors

### Issue: Dark Mode Not Working

**Symptoms**: Colors hard-coded, don't respect theme setting

**Resolution**:
1. Verify Fluent UI v9 is installed: `npm list @fluentui/react-components`
2. Check control uses `makeStyles` with design tokens (not hard-coded colors)
3. Verify control respects FluentProvider theme context
4. Rebuild and redeploy control:
   ```bash
   npm run build
   pac pcf push --environment-url https://spaarkedev1.crm.dynamics.com --publisher-prefix sprk
   ```

### Issue: API Deployment Fails

**Symptoms**: Azure deployment error, app service doesn't start

**Resolution**:
1. Check .NET version: `dotnet --version` (must be 8+)
2. Build locally first: `dotnet build` (debug any build errors)
3. Check App Service logs:
   ```bash
   az webapp log config --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --application-logging true
   az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
   ```
4. Verify Key Vault secrets are configured in App Service
5. Re-deploy:
   ```bash
   .\scripts\Deploy-BffApi.ps1 -Environment dev
   ```

---

## Rollback Procedure

If deployment needs to be rolled back:

### Rollback Dataverse Solution

```bash
# Restore from backup (created in Phase 2 Step 2.1)
pac solution import \
  --environment-url https://spaarkedev1.crm.dynamics.com \
  --path backups/SprkEventsWorkflowAutomation-YYYYMMDD_HHMMSS.zip \
  --force-overwrite true
```

### Rollback PCF Controls

```bash
# Redeploy previous version from git
git checkout {previous-commit}
cd src/client/pcf/{control-name}
pac pcf push --environment-url https://spaarkedev1.crm.dynamics.com --publisher-prefix sprk
```

### Rollback BFF API

```bash
# Deploy previous version to App Service
git checkout {previous-commit}
.\scripts\Deploy-BffApi.ps1 -Environment dev
```

---

## Next Steps After Deployment

1. **Schedule UAT** (User Acceptance Testing) - Task 071
2. **Create User Documentation** - Task 072
3. **Create Admin Documentation** - Task 073
4. **Mark Project Complete** - Task 074

---

## Support & Questions

For deployment issues or questions:
1. Check Troubleshooting section above
2. Review Project CLAUDE.md for architecture decisions
3. Contact project owner: {Owner Name}

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-02-01 | Initial comprehensive deployment guide |

