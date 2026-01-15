# SDAP Troubleshooting Guide

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (Critical Issues & Resolutions section)
> **Last Updated**: January 9, 2026
> **Applies To**: Debugging SDAP issues, error resolution

---

## TL;DR

Six documented issues: (1) AADSTS500011 - missing app user, (2) Wrong OAuth scope format, (3) Double `/api` path, (4) Wrong relationship name, (5) ManagedIdentity failure, (6) Kiota assembly binding error. Most issues are auth/config related.

---

## Applies When

- SDAP upload/preview not working
- Seeing specific error codes
- Troubleshooting new entity setup
- Debugging after deployment

---

## Issue 1: AADSTS500011 - Resource Principal Not Found

### Symptom
```
AADSTS500011: The resource principal named https://spaarkedev1.api.crm.dynamics.com/...
was not found in the tenant
```

### Root Cause
- BFF API App Registration not registered as Application User in Dataverse
- OR: Dynamics CRM API permission not granted

### Resolution

**Step 1: Verify Dynamics CRM Permission**
```
Azure Portal → App registrations → spe-bff-api → API permissions
Check for: Dynamics CRM (user_impersonation) with Admin consent ✓

If missing:
  Add a permission → Dynamics CRM → user_impersonation
  Grant admin consent for [Tenant]
```

**Step 2: Verify Application User in Dataverse**
```
Power Platform Admin Center:
  Environments → SPAARKE DEV 1 → Settings
  Users + permissions → Application users
  Look for: 1e40baad-e065-4aea-a8d4-4b7ab273458c

If missing:
  + New app user
  Application ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
  Security Role: System Administrator
```

**Step 3: Restart and Test**
```bash
az webapp restart --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
sleep 30
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

---

## Issue 2: OAuth Scope Error - Wrong Format

### Symptom
```
AADSTS500011: The resource principal named api://spe-bff-api was not found
```

### Root Cause
PCF using friendly name instead of full Application ID URI.

### Resolution

```typescript
// WRONG
const token = await authProvider.getToken(['api://spe-bff-api/user_impersonation']);

// CORRECT
const token = await authProvider.getToken([
    'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
]);
```

**Files to check**:
- `src/controls/UniversalQuickCreate/control/index.ts`
- `src/controls/SpeFileViewer/index.ts`

---

## Issue 3: 404 Not Found - Double /api Path

### Symptom
```
GET https://spe-api-dev-67e2xz.azurewebsites.net/api/api/navmap/... 404
```

### Root Cause
NavMapClient received baseUrl with `/api` suffix, then added `/api/navmap` internally.

### Resolution

```typescript
// In PCF initialization
const rawApiUrl = context.parameters.sdapApiBaseUrl?.raw;

// NavMapClient needs base URL WITHOUT /api suffix
const navMapBaseUrl = rawApiUrl.endsWith('/api')
    ? rawApiUrl.substring(0, rawApiUrl.length - 4)  // Strip /api
    : rawApiUrl;

const navMapClient = new NavMapClient(navMapBaseUrl, tokenProvider);
```

---

## Issue 4: Relationship Not Found

### Symptom
```
[NavMapClient] Failed to get lookup navigation
Error: Metadata not found. The entity or relationship may not exist in Dataverse.
```

### Root Cause
PCF config uses assumed relationship name, but Dataverse has different actual name.

### Resolution

**Step 1: Find correct relationship name**
```
Power Apps Maker Portal:
  Tables → {parent_entity} → Relationships
  Find relationship to sprk_document
  Note exact "Relationship name" field
```

**Step 2: Update EntityDocumentConfig.ts**
```typescript
'sprk_project': {
    entityName: 'sprk_project',
    lookupFieldName: 'sprk_project',
    relationshipSchemaName: 'sprk_Project_Document_1n',  // EXACT name from Dataverse
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_projectname',
    entitySetName: 'sprk_projects'
}
```

**Step 3: Rebuild and deploy**
```bash
npm run build
pac pcf push --publisher-prefix sprk
```

---

## Issue 5: ManagedIdentityCredential Failed

### Symptom
```
ManagedIdentityCredential authentication failed: No User Assigned or Delegated Managed Identity found
```

### Root Cause
Attempted to use ManagedIdentityCredential for Dataverse, which is unreliable.

### Resolution
Use connection string authentication (Microsoft recommended):

```csharp
// BEFORE (failed)
var credential = new ManagedIdentityCredential(clientId);
_serviceClient = new ServiceClient(instanceUrl, tokenProviderFunction);

// AFTER (works)
var connectionString = $"AuthType=ClientSecret;Url={dataverseUrl};" +
                      $"ClientId={clientId};ClientSecret={clientSecret}";
_serviceClient = new ServiceClient(connectionString);
```

---

## Quick Diagnostic Commands

### Check BFF API Health
```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy"
```

### View Live Logs
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

### Test NavMap Endpoint
```bash
# Need valid token - use browser dev tools to copy from PCF request
curl -H "Authorization: Bearer {token}" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/sprk_document/sprk_matter_document_1n/lookup"
```

### Verify Application User
```bash
pac admin list-service-principals --environment https://spaarkedev1.crm.dynamics.com
# Look for: 1e40baad-e065-4aea-a8d4-4b7ab273458c
```

### Clear Redis Cache
```bash
# Azure Portal → Redis Cache → Console
> KEYS navmap:*
> DEL navmap:lookup:sprk_document:sprk_matter_document_1n
```

---

## Symptom → Cause Quick Reference

| Symptom | Likely Cause | First Check |
|---------|--------------|-------------|
| 401 on all BFF calls | Token expired or wrong scope | Browser console for MSAL errors |
| 500 with AADSTS500011 | Missing Application User | Power Platform Admin Center |
| 500 with Kiota.Abstractions | Kiota package version mismatch | Check csproj for all Kiota refs |
| 404 on NavMap | Wrong relationship name or double `/api` | EntityDocumentConfig.ts |
| 400 on record create | Navigation property case mismatch | NavMap response in browser |
| Upload works, record fails | Lookup binding wrong | Payload in browser Network tab |
| Preview iframe blank | Preview URL wrong or CORS | Check iframe src in Elements tab |

---

## Issue 6: Kiota Assembly Binding Error

### Symptom
```
FileNotFoundException: Could not load file or assembly 'Microsoft.Kiota.Abstractions, Version=1.17.1.0'
```
API returns 500 on `/healthz` after deployment.

### Root Cause
Microsoft.Graph SDK pulls transitive Kiota package dependencies at older versions than direct references. When direct refs are updated (e.g., 1.21.1), the transitive deps stay at older versions (e.g., 1.17.1), causing assembly binding conflicts.

**Typical mismatch:**
| Package | Direct Ref | Transitive (from Graph) |
|---------|------------|-------------------------|
| Microsoft.Kiota.Abstractions | 1.21.1 | — |
| Microsoft.Kiota.Http.HttpClientLibrary | — | 1.17.1 ❌ |
| Microsoft.Kiota.Serialization.* | — | 1.17.1 ❌ |

### Resolution

**Add explicit package references** to `Sprk.Bff.Api.csproj` for ALL Kiota packages:

```xml
<!-- Kiota packages - all must be same version -->
<PackageReference Include="Microsoft.Kiota.Abstractions" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Authentication.Azure" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Http.HttpClientLibrary" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Form" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Json" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Multipart" Version="1.21.1" />
<PackageReference Include="Microsoft.Kiota.Serialization.Text" Version="1.21.1" />
```

**Then rebuild and redeploy:**
```bash
dotnet build src/server/api/Sprk.Bff.Api -c Release
dotnet publish src/server/api/Sprk.Bff.Api -c Release -o ./publish
# Deploy to Azure...
```

### Prevention
When updating ANY Kiota package, update ALL Kiota packages to the same version.

---

## Email-to-Document Issues

**Full Documentation**: See [email-to-document-automation.md](email-to-document-automation.md) for complete architecture.

### Issue 7: Attachments Not Created as Child Documents

#### Symptom
Parent .eml document created successfully, but no child documents for attachments.

#### Root Causes

**A. Race Condition (webhook fires before attachments exist)**:
Dataverse webhooks fire on email Create, but attachments are added via separate API calls.

**Diagnosis**:
```kusto
traces
| where message contains "[AttachmentProcessDebug]"
| where message contains "No attachments found"
| where timestamp > ago(1h)
```

**Resolution**: Retry logic implemented in `FetchAttachmentsAsync` (2-second delay + retry).

**B. All Attachments Filtered Out**:
Signature images, tracking pixels, and small images are filtered.

**Diagnosis**:
```kusto
traces
| where message contains "filtered out"
| where timestamp > ago(1h)
```

**Resolution**: Check `AttachmentFilterService` rules and `EmailProcessingOptions` settings.

---

### Issue 8: Child Document SPE Fields Empty

#### Symptom
Child attachment documents created but `sprk_graphitemid`, `sprk_graphdriveid`, `sprk_filepath` are null.

#### Error Message
```
Entity Key Email Activity Key violated. A record with the same value for Email already exists.
```

#### Root Cause
The `sprk_document` entity has an alternate key on `sprk_email` field. When the code tried to set `EmailLookup = emailId` on child documents, it violated this constraint because the parent already uses that email lookup.

#### Resolution
Child documents must NOT set `EmailLookup`. They relate to the email through their `ParentDocumentLookup` instead.

```csharp
// CORRECT - in EmailToDocumentJobHandler.ProcessSingleAttachmentAsync
var updateRequest = new UpdateDocumentRequest
{
    ParentDocumentLookup = parentDocumentId,
    // EmailLookup is NOT set for child documents
};
```

---

### Issue 9: AI Analysis Not Running on Email Documents

#### Symptom
Documents created successfully, but AI analysis status (`sprk_aianalysisstatus`) remains empty.

#### Error Message (in logs)
```
Warning: Playbook has no tools configured
Warning: Playbook resolution not yet implemented, returning empty scopes
```

#### Root Cause
`ScopeResolverService.ResolvePlaybookScopesAsync` is a placeholder that returns empty scopes. Manual analysis works because UI passes `ToolIds[]` directly.

#### Workaround
Users can manually trigger AI analysis from the Analysis Builder UI.

#### Resolution
See [ai-analysis-integration-issue.md](../../projects/email-to-document-automation-r2/notes/ai-analysis-integration-issue.md) for implementation plan.

---

### Issue 10: Webhook Validation Failing

#### Symptom
401 Unauthorized or 400 Bad Request on webhook endpoint.

#### Root Causes

**A. Secret Mismatch**:
```kusto
traces
| where message contains "Webhook signature validation failed"
| project timestamp, message
```

**Resolution**: Verify `EmailProcessing:WebhookSecret` matches Dataverse Service Endpoint.

**B. Webhook Disabled**:
```kusto
traces
| where message contains "Webhook processing is disabled"
```

**Resolution**: Set `EmailProcessing:EnableWebhook = true` in configuration.

---

### Issue 11: Continuous Document Creation (Polling Issue)

#### Symptom
Many duplicate test records being created continuously.

#### Root Cause
`EnablePolling = true` with unprocessed emails in lookback period. Polling service runs every 5 minutes.

#### Resolution
1. Set `EmailProcessing:EnablePolling = false` during testing
2. Or increase `PollingIntervalMinutes`
3. Or ensure `sprk_documentprocessingstatus` is set after processing

---

### Email Diagnostic Commands

**View Email Job Logs**:
```kusto
traces
| where message contains "[AttachmentProcessDebug]" or message contains "EmailToDocument"
| where timestamp > ago(1h)
| project timestamp, message
| order by timestamp desc
```

**Check Job Queue Health**:
```bash
# View Service Bus queue metrics
az servicebus queue show --name sdap-jobs \
  --namespace-name {namespace} \
  --resource-group {rg} \
  --query "{ActiveMessages:countDetails.activeMessageCount, DeadLetter:countDetails.deadLetterMessageCount}"
```

**Test Email Processing Manually**:
```bash
# Trigger manual save-as-document
curl -X POST "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/emails/{emailId}/save-as-document" \
  -H "Authorization: Bearer {token}"
```

---

## Email Post-Deployment Checklist

- [ ] Webhook endpoint responds to test request
- [ ] Service Bus queue receiving messages
- [ ] EmailPollingBackupService running (if enabled)
- [ ] Email-to-.eml conversion successful
- [ ] Parent document created with email metadata
- [ ] SPE fields populated (GraphItemId, GraphDriveId, FilePath)
- [ ] Attachments filtered correctly
- [ ] Child documents created for attachments
- [ ] Parent-child relationship established
- [ ] AI analysis job enqueued (if AutoEnqueueAi = true)
- [ ] No duplicate processing (idempotency working)

---

## Post-Deployment Checklist

- [ ] Health endpoint returns 200
- [ ] NavMap API returns metadata
- [ ] PCF control loads in form
- [ ] Single file upload works
- [ ] Large file upload works
- [ ] Document record created correctly
- [ ] Subgrid shows new document
- [ ] Preview displays file

---

## Related Articles

- [sdap-overview.md](sdap-overview.md) - System architecture
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - Authentication details
- [sdap-pcf-patterns.md](sdap-pcf-patterns.md) - PCF control code
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) - API code
- [email-to-document-automation.md](email-to-document-automation.md) - Email-to-Document automation architecture

---

*Condensed from troubleshooting sections of architecture guide*
*Email-to-Document issues added January 2026*
