# ECS-005: End-to-End Outbound Shared Mailbox Test Plan

> **Task**: ECS-005 — End-to-End Outbound Shared Mailbox Test
> **Phase**: 1 - Communication Account Entity (Phase A)
> **Created**: 2026-03-09
> **Status**: Ready for execution
> **Prerequisites**: Tasks 001-004 complete (field name fixes, cache key update, 68/68 tests passing)

---

## 1. Pre-deployment Checklist

### 1.1 Build Verification (DONE)

- [x] `dotnet build src/server/api/Sprk.Bff.Api/` completes with 0 errors, 0 warnings
- [x] `dotnet test` passes 68/68 communication-related tests
- [x] Field name corrections applied: `sprk_sendenabled`, `sprk_communicationtype`
- [x] Legacy `QueryApprovedSendersAsync` method removed
- [x] Cache key updated to `communication:accounts:merged`

### 1.2 BFF API Deployment

Deploy the updated BFF API to the dev environment using the deployment script:

```powershell
# Full build + deploy (recommended for first deployment after code changes)
.\scripts\Deploy-BffApi.ps1

# Or skip build if already published
.\scripts\Deploy-BffApi.ps1 -SkipBuild
```

**What the script does** (4 steps):
1. `dotnet publish -c Release` into `src/server/api/Sprk.Bff.Api/publish/`
2. Creates `publish.zip` package
3. `az webapp deploy` to App Service `spe-api-dev-67e2xz` in resource group `spe-infrastructure-westus2`
4. Polls health check with 6 retries (30 seconds total wait)

**Manual alternative** (if script is unavailable):
```bash
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o ./publish
# Zip the publish folder
az webapp deploy --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --src-path ./publish.zip --type zip --async false
```

### 1.3 Post-deployment Health Check

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy"

curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected: 200 OK
```

**If health check fails**: Check App Service logs via `az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz` for startup errors (e.g., missing config, DI registration failures).

---

## 2. Pre-test Verification

These checks confirm the Dataverse environment is correctly configured before running send tests.

### 2.1 Verify `sprk_communicationaccount` Record Exists

Query Dataverse for the test shared mailbox account:

```
Dataverse: https://spaarkedev1.crm.dynamics.com
Entity: sprk_communicationaccount
Filter: sprk_emailaddress contains the test shared mailbox address
```

**Verify these fields on the record:**

| Field | Expected Value | Notes |
|-------|---------------|-------|
| `sprk_name` | (meaningful name) | e.g., "Central Mailbox" or "Test Shared Mailbox" |
| `sprk_emailaddress` | (test mailbox address) | Must match Exchange shared mailbox |
| `sprk_displayname` | (display name) | Used in email From header |
| `sprk_accounttype` | 100000000 (Shared Account) | Shared mailbox type |
| `sprk_sendenabled` | `true` | **CRITICAL**: Uses corrected field name (was `sprk_sendenableds` in R1 code) |
| `sprk_communicationtype` | 100000000 (Email) | **CRITICAL**: Uses corrected field name (was `sprk_communiationtype` in R1 code) |
| `sprk_isdefaultsender` | `true` | At least one account must be default |
| `sprk_authenticationmethod` | 100000000 (App-Only) | Shared mailbox uses app-only auth |
| `statecode` | 0 (Active) | Must be active |

**If record does not exist**: Create one manually in Dataverse with the values above before proceeding.

### 2.2 Verify Exchange Security Group Membership

The shared mailbox must be a member of the **"SDAP Mailbox Access"** security group in Entra ID (Azure AD). This group is referenced in the Exchange Application Access Policy that grants the BFF API app-only permission to send mail on behalf of group members.

**Verification steps:**
1. Open Entra ID (Azure AD) portal
2. Navigate to Groups > search for "SDAP Mailbox Access"
3. Confirm the test shared mailbox is listed as a member
4. If not a member, add the shared mailbox to the group

**Why this matters**: Without group membership, Graph `sendMail` will return `ErrorAccessDenied` even though the app registration has `Mail.Send` permission. The Application Access Policy scopes the permission to group members only.

### 2.3 Verify Redis Cache Reachability

After deployment, the BFF API should be able to connect to Redis. Check for Redis connection errors in the App Service logs:

```bash
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --filter "Redis"
```

**Expected**: No Redis connection errors. The `ApprovedSenderValidator` caches the merged sender list at key `communication:accounts:merged` with a 5-minute TTL.

### 2.4 Verify appsettings.json Baseline Senders

The BFF API `Communication` config section should contain at least one approved sender as a baseline. This is Tier 1 of the two-tier sender resolution:

```json
{
  "Communication": {
    "ApprovedSenders": [
      {
        "Email": "noreply@spaarke.com",
        "DisplayName": "Spaarke Notifications",
        "IsDefault": true
      }
    ],
    "DefaultMailbox": "noreply@spaarke.com"
  }
}
```

The `ApprovedSenderValidator` merges these config senders with `sprk_communicationaccount` records from Dataverse (Dataverse wins on email match). This merged list is what gets cached in Redis.

---

## 3. Test Cases

### TC-01: Send Email via API (Shared Mailbox)

**Objective**: Verify the full 7-step send pipeline works end-to-end with a shared mailbox account sourced from `sprk_communicationaccount`.

**Pipeline stages covered**:
1. Validate Request
2. (Skip attachments - no attachments in this test)
3. Resolve Approved Sender (from `sprk_communicationaccount` via `ApprovedSenderValidator`)
4. Build Graph Message
5. Send via Graph `sendMail` (CRITICAL PATH)
6. Create Dataverse `sprk_communication` record (best-effort)
7. (Skip EML archival - `archiveToSpe=false` for this test)

**Steps:**

1. Obtain a valid bearer token for the BFF API (authenticated user with `oid` claim)
2. Send a POST request:

```bash
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/send \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "to": ["{recipient-email}"],
    "subject": "ECS-005 E2E Test - Shared Mailbox Send",
    "body": "<p>This is an automated test email sent via the Communication Service BFF API.</p><p>Test timestamp: {ISO-timestamp}</p>",
    "bodyFormat": "HTML",
    "fromMailbox": "{shared-mailbox-email}",
    "communicationType": "Email"
  }'
```

3. Verify response

**Expected Result:**

| Field | Expected |
|-------|----------|
| HTTP Status | `200 OK` |
| `communicationId` | Non-null GUID |
| `graphMessageId` | Non-null string |
| `status` | `Send` (value 659490002) |
| `sentAt` | Recent timestamp (within last minute) |
| `from` | Matches the shared mailbox email |
| `correlationId` | Non-null string |
| `archivalWarning` | `null` (archival not requested) |

**Failure modes to watch for:**
- `400 INVALID_SENDER` — mailbox not in merged approved sender list (check `sprk_communicationaccount` record, `sprk_sendenabled`)
- `403 COMMUNICATION_NOT_AUTHORIZED` — bearer token missing or invalid
- `502 GRAPH_SEND_FAILED` — Exchange policy not applied, or mailbox not in "SDAP Mailbox Access" group

---

### TC-02: Verify Dataverse Record Created

**Objective**: Confirm that a `sprk_communication` record was created in Dataverse after a successful send (Step 6 of the pipeline).

**Steps:**

1. After TC-01 succeeds, note the `communicationId` from the response
2. Query Dataverse:
   - Navigate to `https://spaarkedev1.crm.dynamics.com`
   - Open `sprk_communication` entity (Advanced Find or direct URL)
   - Search for the record by ID or by `sprk_subject` = "ECS-005 E2E Test - Shared Mailbox Send"

3. Alternatively, use the status endpoint:
```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/{communicationId}/status \
  -H "Authorization: Bearer {token}"
```

**Expected Record Fields:**

| Field | Expected Value |
|-------|---------------|
| `sprk_name` | "Email: ECS-005 E2E Test - Shared Mailbox Send" |
| `sprk_communicationtype` | Email (uses corrected field name) |
| `statuscode` | Send (659490002) |
| `statecode` | Active (0) |
| `sprk_direction` | Outgoing (1) |
| `sprk_bodyformat` | HTML (0) |
| `sprk_to` | `{recipient-email}` |
| `sprk_from` | `{shared-mailbox-email}` |
| `sprk_subject` | "ECS-005 E2E Test - Shared Mailbox Send" |
| `sprk_graphmessageid` | Matches response `graphMessageId` |
| `sprk_sentat` | Matches response `sentAt` |
| `sprk_correlationid` | Matches response `correlationId` |
| `sprk_hasattachments` | `false` |
| `sprk_attachmentcount` | 0 |

**If record is missing** (communicationId was null in TC-01 response): Check BFF API logs for Dataverse errors. The record creation is best-effort, so the send may have succeeded even without a tracking record. Look for warnings in logs.

---

### TC-03: Verify Config Source (Dataverse, Not Just appsettings)

**Objective**: Confirm that the `ApprovedSenderValidator` is resolving senders from `sprk_communicationaccount` records in Dataverse (Tier 2), not solely from `appsettings.json` (Tier 1).

**Steps:**

1. **Option A — Use a Dataverse-only sender**: If a `sprk_communicationaccount` record exists for a mailbox that is NOT in `appsettings.json` `ApprovedSenders`, send from that mailbox. If the send succeeds, the config came from Dataverse.

2. **Option B — Check API logs**: After TC-01, review BFF API logs for sender resolution entries:
```bash
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
```
Look for log entries from `ApprovedSenderValidator` indicating:
   - Cache miss → Dataverse query → merge → cache write
   - Or cache hit on key `communication:accounts:merged`

3. **Option C — Clear Redis cache**: Force a Dataverse query by clearing the cached sender list:
   - Wait 5 minutes for TTL expiry (or manually delete key `communication:accounts:merged`)
   - Send again and verify logs show Dataverse query executed

**Expected Result:**
- Sender resolution includes accounts from `sprk_communicationaccount` entity
- Merged list is cached at Redis key `communication:accounts:merged`
- Dataverse `sprk_communicationaccount` records with `sprk_sendenabled = true` appear in the merged list

---

### TC-04: Default Sender Fallback

**Objective**: Verify that when `fromMailbox` is null/omitted, the service uses the account marked as `sprk_isdefaultsender = true`.

**Steps:**

1. Send a POST request WITHOUT specifying `fromMailbox`:

```bash
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/send \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "to": ["{recipient-email}"],
    "subject": "ECS-005 E2E Test - Default Sender Fallback",
    "body": "<p>This email should be sent from the default sender account.</p>",
    "bodyFormat": "HTML",
    "communicationType": "Email"
  }'
```

2. Verify the `from` field in the response

**Expected Result:**

| Field | Expected |
|-------|----------|
| HTTP Status | `200 OK` |
| `from` | Email address of the `sprk_communicationaccount` record where `sprk_isdefaultsender = true` (or `DefaultMailbox` from config if no Dataverse default) |

**Resolution priority** (per `ApprovedSenderValidator`):
1. Sender with `IsDefault=true` in merged list
2. `DefaultMailbox` config match
3. First sender in list

**If 400 `NO_DEFAULT_SENDER` returned**: No sender is configured as default. Set `sprk_isdefaultsender = true` on at least one active `sprk_communicationaccount` record.

---

### TC-05: Invalid Sender Rejection

**Objective**: Verify that sending from a non-approved mailbox is rejected before any Graph API call.

**Steps:**

1. Send a POST request with a mailbox that does NOT exist in either `appsettings.json` or `sprk_communicationaccount`:

```bash
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/send \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "to": ["{recipient-email}"],
    "subject": "ECS-005 E2E Test - Invalid Sender",
    "body": "<p>This should not be sent.</p>",
    "bodyFormat": "HTML",
    "fromMailbox": "not-a-real-mailbox@nonexistent-domain.com",
    "communicationType": "Email"
  }'
```

**Expected Result:**

| Field | Expected |
|-------|----------|
| HTTP Status | `400 Bad Request` |
| Response format | ProblemDetails (RFC 7807) |
| Error code | `INVALID_SENDER` |
| `correlationId` | Present in ProblemDetails extensions |

**Verify additionally:**
- No email was sent (check recipient inbox)
- No `sprk_communication` record was created in Dataverse
- Sender validation occurred before Graph call (Step 2 in pipeline, before Step 4)

---

### TC-06: Send with Disabled Account

**Objective**: Verify that a `sprk_communicationaccount` with `sprk_sendenabled = false` is excluded from the approved sender list.

**Steps:**

1. Identify or create a `sprk_communicationaccount` record with `sprk_sendenabled = false`
2. Wait for cache expiry (5 minutes) or clear Redis key `communication:accounts:merged`
3. Attempt to send from that mailbox

**Expected Result:**
- `400 INVALID_SENDER` — the disabled account is filtered out during merge
- No email sent, no Dataverse record created

---

### TC-07: Verify Email Delivery

**Objective**: Confirm the email actually arrived in the recipient's inbox (not just that Graph accepted the request).

**Steps:**

1. After TC-01 succeeds, check the recipient mailbox
2. Verify:
   - Email arrived in inbox (not spam/junk)
   - Subject matches: "ECS-005 E2E Test - Shared Mailbox Send"
   - Body renders as HTML
   - From address shows the shared mailbox with correct display name
   - Reply-to is the shared mailbox

**Expected Result:**
- Email delivered within 1-2 minutes of send
- From header shows `{display-name} <{shared-mailbox-email}>`

**If email not delivered:**
- Check Exchange admin center for mail flow issues
- Verify shared mailbox is not blocked or rate-limited
- Check recipient spam filters

---

## 4. Post-test Verification

### 4.1 Check API Logs for Errors

```bash
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
```

**Look for:**
- Any `Error` or `Critical` level log entries during the test window
- Warnings from Dataverse record creation (acceptable for best-effort steps)
- Redis connection issues
- Graph API errors with error codes

### 4.2 Verify No Stale Cache Issues

After deployment, the first sender resolution request should trigger a fresh Dataverse query (cache miss on new key format). Verify:

- [ ] No errors related to old field names (`sprk_sendenableds`, `sprk_communiationtype`)
- [ ] Cache key `communication:accounts:merged` is being used (not old key `communication:approved-senders`)
- [ ] Cache TTL is 5 minutes (subsequent requests within 5 min hit cache)

If errors reference old field names, the deployment may not have applied correctly. Redeploy and verify the published DLLs contain the updated code.

### 4.3 Verify Redis Cache Keys

If Redis CLI or monitoring is available:

```
KEYS communication:*
```

**Expected:**
- Key `communication:accounts:merged` exists after first send
- Old key `communication:approved-senders` does NOT exist (or has expired)

### 4.4 Verify Correlation ID Tracing

Pick the `correlationId` from any TC-01 response and search API logs:

```bash
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --filter "{correlationId}"
```

Confirm the correlation ID appears in log entries across the full pipeline: validation, sender resolution, Graph send, Dataverse record creation.

---

## 5. Manual Steps Required

These steps require human credentials and access that cannot be automated:

| Step | Requires | Who |
|------|----------|-----|
| Deploy BFF API to Azure | Azure CLI credentials (`az login`) | Developer with Contributor role on `spe-infrastructure-westus2` |
| Create/verify `sprk_communicationaccount` record | Dataverse admin access | System admin on `spaarkedev1.crm.dynamics.com` |
| Verify "SDAP Mailbox Access" group membership | Entra ID (Azure AD) admin access | Entra ID admin or group owner |
| Obtain bearer token for API calls | Authenticated user session | Any licensed Dataverse user |
| Verify email delivery | Recipient mailbox access | Owner of the test recipient mailbox |
| Check Redis cache keys | Redis CLI or Azure Cache monitoring | Developer with Azure cache access |
| Review App Service logs | Azure portal or `az webapp log` | Developer with Reader role |

---

## 6. Test Results Template

Copy this section after executing tests and fill in results:

```markdown
## Test Execution Results

**Date**: ____
**Deployed Build**: ____
**Tester**: ____

| Test | Result | Notes |
|------|--------|-------|
| TC-01: Send via API | PASS / FAIL | |
| TC-02: Verify Dataverse record | PASS / FAIL | |
| TC-03: Config source verification | PASS / FAIL | |
| TC-04: Default sender fallback | PASS / FAIL | |
| TC-05: Invalid sender rejection | PASS / FAIL | |
| TC-06: Disabled account exclusion | PASS / FAIL | |
| TC-07: Email delivery verification | PASS / FAIL | |

### Issues Found
- (list any issues discovered)

### Follow-up Actions
- (list any required fixes or investigations)
```

---

## 7. Pipeline Stage Coverage Matrix

Maps each test case to the 7-step send pipeline stages (from `communication-service-architecture.md`):

| Pipeline Stage | TC-01 | TC-02 | TC-03 | TC-04 | TC-05 | TC-06 | TC-07 |
|---------------|-------|-------|-------|-------|-------|-------|-------|
| Step 1: Validate Request | x | | | x | x | x | |
| Step 1b: Download Attachments | | | | | | | |
| Step 2: Resolve Approved Sender | x | | x | x | x | x | |
| Step 3: Build Graph Message | x | | | x | | | |
| Step 4: Send via Graph (critical) | x | | | x | | | x |
| Step 5: Create Dataverse Record | x | x | | x | | | |
| Step 6: Archive EML to SPE | | | | | | | |
| Step 7: Create Attachment Records | | | | | | | |

**Not covered by this test plan** (covered by separate tasks):
- Step 1b: Attachment download from SPE — tested in attachment-specific tests
- Step 6: EML archival to SPE — tested in ECS-037 (archival E2E test)
- Step 7: Attachment record creation — tested in attachment-specific tests

---

*End of test plan. Output results to `projects/email-communication-solution-r2/notes/research/phase-a-e2e-results.md` per task POML spec.*
