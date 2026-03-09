# Deployment Status — Email Communication Solution R1
> **Date**: 2026-02-23
> **Branch**: `work/email-communication-solution-r1` (merged to `origin/master`)
> **Session**: Picking up after BFF API deployment failure

---

## Overall Project Status

**All 55 tasks complete.** Code merged to master. This session is about first-time deployment and E2E testing.

---

## What Was Done This Session

### 1. Repo Cleanup — CommunicationRibbons Structure Created ✅

`sprk_communication_send.js` was in the wrong location (tied to LegalWorkspace). Moved to correct canonical structure:

```
infrastructure/dataverse/ribbon/CommunicationRibbons/
├── [Content_Types].xml
├── Other/
│   ├── Solution.xml                  ← UniqueName: CommunicationRibbons, v1.0.0
│   └── Customizations.xml            ← WebResource definition (GUID: 0ea67dad-500f-f111-8342-7ced8d1dc988)
├── Entities/
│   └── sprk_communication/
│       ├── Entity.xml
│       └── RibbonDiff.xml            ← Send button ribbon XML
└── WebResources/
    └── sprk_communication_send.js    ← 44KB — canonical copy
```

**Removed**: `src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js`
**Kept**: `src/client/webresources/js/sprk_communication_send.js` (developer working copy)

> These changes are **NOT yet committed** to the branch. Need to commit and push.

---

### 2. BFF API Package — Correct Build Ready ✅

**File**: `src\server\api\Sprk.Bff.Api\publish.zip`
- Size: **61.1 MB** (constraint says ~60MB ✅)
- Entries: **277** (constraint says ~240 ✅)
- Published to `./publish/` (correct path per `Deploy-BffApi.ps1`)
- `web.config` has `stdoutLogEnabled="true"` ✅
- No `appsettings.json` in package (correct — config comes from App Service settings)

**Previous broken zip**: `src\server\api\Sprk.Bff.Api\bff-api-deploy.zip` — DO NOT USE
- Was published to wrong folder (`./deploy/` instead of `./publish/`)
- Had `stdoutLogEnabled="false"`
- Only 151 entries (missing ~126 entries vs correct build)

---

### 3. Root Cause of API Failure

Uploading `bff-api-deploy.zip` broke the API. Two causes:

**Cause 1 — CRITICAL (startup crash)**:
`GraphSubscriptionManager` constructor throws `InvalidOperationException` if these App Service settings are absent:
- `Communication__WebhookNotificationUrl`
- `Communication__WebhookClientState`

Since it's registered as `AddHostedService`, .NET resolves the constructor at `IHost.StartAsync()` — the app **cannot start** without these values.

**Cause 2 — Contributing**:
Old deployment archive (`deployment.tar.gz`, 15.5MB) had `appsettings.json` baked in with base config. New deployment correctly has none (config from App Service settings per constraints). But if App Service is missing `TENANT_ID`, `API_APP_ID`, etc., those will also be missing.

---

## Next Steps (In Order)

### Step 1 — Add Missing App Service Settings ⚠️ BLOCKING
Go to: **Azure Portal → App Services → `spe-api-dev-67e2xz` → Configuration → Application settings**

Add these new settings (Communication module — Phase 6-9):

| Key | Value |
|-----|-------|
| `Communication__WebhookNotificationUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/incoming-webhook` |
| `Communication__WebhookClientState` | *(any secret GUID — generate one, store it safely)* |
| `Communication__DefaultMailbox` | `mailbox-central@spaarke.com` |

> `WebhookClientState` is a shared secret for validating Graph webhook notifications. Choose any unguessable string (GUID recommended). Use same value everywhere.

Also verify these existing settings are still present (from old `appsettings.json`):

| Key | Expected |
|-----|---------|
| `TENANT_ID` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `API_APP_ID` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `ASPNETCORE_ENVIRONMENT` | `Development` |
| `Cors__AllowedOrigins__0` | `https://spaarkedev1.crm.dynamics.com` |
| `Cors__AllowedOrigins__1` | `https://spaarkedev1.api.crm.dynamics.com` |
| `ConnectionStrings__ServiceBus` | `@Microsoft.KeyVault(...)` |
| `Dataverse__ServiceUrl` | `@Microsoft.KeyVault(...)` |
| `Dataverse__ClientSecret` | `@Microsoft.KeyVault(...)` |

### Step 2 — Upload publish.zip via Kudu ⚠️ AFTER Step 1
1. Go to: `https://spe-api-dev-67e2xz.scm.azurewebsites.net/ZipDeployUI`
2. Drag and drop: `src\server\api\Sprk.Bff.Api\publish.zip`
3. Wait for deployment to complete

### Step 3 — Verify API Health
```
GET https://spe-api-dev-67e2xz.azurewebsites.net/healthz  → "Healthy"
GET https://spe-api-dev-67e2xz.azurewebsites.net/ping     → "pong"
```

### Step 4 — Deploy Web Resource to Dataverse
Upload `src\client\webresources\js\sprk_communication_send.js` to Dataverse:
1. make.powerapps.com → Solutions → find `sprk_communication_send` web resource
2. Upload file → Save → Publish all customizations

### Step 5 — Commit Repo Changes
The CommunicationRibbons structure and web resource reorganization from this session need to be committed:
```bash
git add infrastructure/dataverse/ribbon/CommunicationRibbons/
git add src/client/webresources/js/sprk_communication_send.js
git rm src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js
git commit -m "feat(communication): add CommunicationRibbons solution structure"
git push origin HEAD --force-with-lease
```

### Step 6 — E2E Testing
Two test plans ready in `projects/email-communication-solution-r1/notes/`:
- `e2e-individual-send-test-plan.md` — 8 scenarios (shared mailbox + OBO user send)
- `e2e-inbound-monitoring-test-plan.md` — inbound pipeline tests

Exchange Online app access policy also needed before inbound monitoring tests work.

---

## Key File Locations

| Item | Path |
|------|------|
| Ready-to-upload zip | `src\server\api\Sprk.Bff.Api\publish.zip` |
| Web resource JS | `src\client\webresources\js\sprk_communication_send.js` |
| CommunicationRibbons solution | `infrastructure\dataverse\ribbon\CommunicationRibbons\` |
| E2E test plan (send) | `projects\email-communication-solution-r1\notes\e2e-individual-send-test-plan.md` |
| E2E test plan (inbound) | `projects\email-communication-solution-r1\notes\e2e-inbound-monitoring-test-plan.md` |
| Admin guide | `docs\guides\COMMUNICATION-ADMIN-GUIDE.md` |
| Deployment guide | `docs\guides\COMMUNICATION-DEPLOYMENT-GUIDE.md` |
| Old working archive (pre-Phase 6-9) | `src\server\api\Sprk.Bff.Api\deployment.tar.gz` |

---

## Known Issues / Watch Points

| Issue | Status | Notes |
|-------|--------|-------|
| API broken on dev | 🔴 Broken | Old code restored? Or still broken? Verify `/healthz` |
| Missing App Service settings | 🔴 Blocking | Must add before uploading new zip |
| CommunicationRibbons not committed | 🟡 Pending | Need `git add / commit / push` |
| Exchange Online app access policy | 🟡 Pending | Required for inbound monitoring; see COMMUNICATION-ADMIN-GUIDE.md |
| `publish/` and `deploy/` folders in project | 🟡 Cleanup | Both are gitignored but take up space locally |

---

## Context: What the New Code Does (Phases 6-9)

If needed for troubleshooting context:

| Phase | New Services | Config Required |
|-------|-------------|-----------------|
| 6 | `CommunicationAccountService` — reads `sprk_communicationaccount` from Dataverse | None (reads Dataverse) |
| 7 | `CommunicationService.SendAsUserAsync` — OBO Graph path | None at startup; needs user token at runtime |
| 8 | `GraphSubscriptionManager` (hosted) — manages Graph webhooks | `Communication__WebhookNotificationUrl`, `Communication__WebhookClientState` |
| 8 | `InboundPollingBackupService` (hosted) — backup polling | None (reads Dataverse, uses GraphClientFactory.ForApp) |
| 8 | `IncomingCommunicationProcessor` — creates incoming records | None at startup |
| 9 | `MailboxVerificationService` — verifies mailbox send/read | None at startup |

---

*Created: 2026-02-23 | Session recovery document*
