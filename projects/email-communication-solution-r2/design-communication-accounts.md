# Communication Accounts & Email Processing Design

## Design Document

> **Author**: Ralph Schroeder (with Claude Code)
> **Date**: February 22, 2026 (Updated: March 9, 2026)
> **Status**: Draft
> **Project**: email-communication-solution-r2
> **Priority**: High — required for send/receive testing

---

## Problem Statement

The Communication Service currently has a gap between the implemented code and operational readiness:

1. **No central mailbox management**: Approved senders are defined only in `appsettings.json`. There's no admin-facing way to add/remove/configure mailboxes without a BFF redeployment. Administrative email settings (approved senders, inbound monitoring config, verification status) should be managed within Dataverse by system admin users — not through code deployments or config files.

2. **No incoming email processing for Communications**: The existing `EmailEndpoints.cs` webhook processes Dataverse email activities into `.eml` documents — but not into `sprk_communication` records. There's no way to track incoming emails as Communication records alongside outbound ones.

3. **No individual user sending**: The service only supports shared mailbox sending (app-only auth). Users can't send as themselves when that's more appropriate (e.g., personal follow-ups vs. firm-wide notifications).

4. **Fragmented configuration**: Exchange access policies, Graph permissions, and BFF config are managed independently with no visibility in Dataverse. Admins can't see what's configured or verify it's working.

5. **Email-to-document pipeline coupled to Server-Side Sync**: The existing email-to-document automation (`EmailToDocumentJobHandler`, `EmailToEmlConverter`, `EmailPollingBackupService`) is tightly coupled to Dataverse email activities and Server-Side Sync. It reads email data and attachments from the Dataverse `email` entity and `activitymimeattachments`, uses a Dataverse webhook on `email.Create`, and links documents to email activities via `sprk_email` lookup. This entire pipeline must be migrated to use Graph API as the data source and `sprk_communication` as the tracking entity — eliminating the dependency on Server-Side Sync entirely.

---

## Proposed Solution

### Unified `sprk_communicationaccount` Entity

Replace `appsettings.json`-only approved sender configuration with a Dataverse table that manages **all communication mailbox configuration** — outbound sending, inbound monitoring, and permission tracking.

### Four Capabilities (One Table)

| Capability | Account Type | Auth Pattern | Status |
|---|---|---|---|
| **Outbound: Shared Mailbox** | Shared Mailbox / Service Account | App-only (`ForApp()`) | Existing code, needs config entity |
| **Outbound: Individual User** | User Mailbox | OBO delegated (`ForUserAsync()`) | New code path needed |
| **Inbound: Shared Mailbox** | Shared Mailbox | App-only (`ForApp()`) + Graph subscriptions | New processing pipeline |
| **Inbound: Individual User** | User Mailbox | OBO delegated | Deferred — significant scope |

---

## Entity Schema: `sprk_communicationaccount` (Deployed)

**Display Name**: Communication Account
**Logical Name**: `sprk_communicationaccount`
**Ownership**: User/Team-owned
**Description**: Configures mailbox accounts for the Communication Service — controls which mailboxes can send email, which are monitored for incoming, and how they connect to Exchange/Graph API. Managed by system admin users within Dataverse.
**Data Model Reference**: `docs/data-model/sprk_communicationaccount.md`

> **Note**: This entity is already created in Dataverse. The schema below reflects the actual deployed state. Choice values use Dataverse standard numbering (100000000+).

### Core Identity

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Name | `sprk_name` | Single Line (Primary, max 850) | Yes | Human-readable name (e.g., "Central Mailbox", "Billing Department") |
| Email Address | `sprk_emailaddress` | Single Line (max 100) | Yes | Mailbox email address (e.g., `mailbox-central@spaarke.com`) |
| Display Name | `sprk_displayname` | Single Line (max 100) | Yes | From header display name (e.g., "Spaarke Legal") |
| Account Type | `sprk_accounttype` | Choice | Yes | See choices below |
| Description | `sprk_desscription` | Single Line (max 2000) | No | Admin notes about this account's purpose |

> **Note**: Description field has a typo in the schema name (`sprk_desscription`). This is the deployed name — use as-is.

**Account Type Choices** (`sprk_accounttype`):

| Value | Label | Description |
|---|---|---|
| 100000000 | Shared Account | Exchange shared mailbox (no license required) |
| 100000001 | Service Account | Licensed service account |
| 100000002 | User Account | Individual user's mailbox (OBO auth) |
| 100000003 | Distribution List | Distribution group (send-only, no receive) |

### Outbound Configuration

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Send Enabled | `sprk_sendenabled` | Two Options (Yes/No) | Yes | Can this account send outbound email (default: No) |
| Is Default Sender | `sprk_isdefaultsender` | Two Options (Yes/No) | Yes | Use when no sender specified; only one should be true (default: No) |
| Daily Send Limit | `sprk_dailysendlimit` | Whole Number | No | Override Graph default (10,000/day). Null = no limit tracking |
| Sends Today | `sprk_sendstoday` | Whole Number | No | Rolling count, reset daily by BFF (system-managed) |

### Inbound Configuration

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Receive Enabled | `sprk_receiveenabled` | Two Options (Yes/No) | Yes | Monitor this mailbox for incoming email (default: No) |
| Monitor Folder | `sprk_monitorfolder` | Single Line (max 100) | No | Folder to watch (default: "Inbox") |
| Auto-Create Records | `sprk_autocreaterecords` | Two Options (Yes/No) | No | Auto-create `sprk_communication` records for incoming (default: No) |
| Processing Rules | `sprk_processingrules` | Multi-line (max 10000) | No | JSON rules for incoming email routing |

> **Note**: `sprk_autoextractattachments` was in the original design but is not deployed. Attachment extraction behavior will be controlled via `sprk_processingrules` JSON configuration or always-on for the initial implementation.

### Graph Integration (System-Managed)

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Graph Subscription Id | `sprk_graphsubscriptionid` | Single Line (max 1000) | No | Graph webhook subscription ID (system-managed) |
| Subscription Id | `sprk_subscriptionid` | Single Line (max 100) | No | Secondary subscription reference (system-managed) |
| Subscription Expiry | `sprk_subscriptionexpiry` | DateTime | No | When subscription needs renewal (system-managed) |
| Subscription Status | `sprk_subscriptionstatus` | Choice | No | Active / Expired / Failed / Not Configured |

**Subscription Status Choices** (`sprk_subscriptionstatus`):

| Value | Label |
|---|---|
| 100000 | Active |
| 100000001 | Expired |
| 100000002 | Failed |
| 100000003 | Not Configured |

> **Note**: Active value is `100000` (not `100000000`) — this appears to be a data entry variance in the deployed entity. Verify and correct if needed.

### Security & Permissions

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Security Group Id | `sprk_securitygroupid` | Single Line (max 100) | No | Azure AD mail-enabled security group object ID |
| Security Group Name | `sprk_securitygroupname` | Single Line (max 100) | No | Security group display name (for reference) |
| Auth Method | `sprk_authmethod` | Choice | Yes | How BFF authenticates to this mailbox |
| Last Verified | `sprk_lastverified` | DateTime | No | Last time BFF confirmed mailbox access works |
| Verification Status | `sprk_verificationstatus` | Choice | No | See choices below |
| Verification Message | `sprk_verificationmessage` | Multi-line (max 4000) | No | Last verification result details |

**Auth Method Choices** (`sprk_authmethod`):

| Value | Label | Description |
|---|---|---|
| 100000000 | App-Only (Client Credentials) | BFF service principal with Mail.Send/Mail.Read application permission |
| 100000001 | OBO (On-Behalf-Of) | User's delegated token exchanged via OBO flow |

> **Note**: The deployed label reads "Apo-Only" (typo). Should be corrected to "App-Only" in Dataverse.

**Verification Status Choices** (`sprk_verificationstatus`):

| Value | Label |
|---|---|
| 100000000 | Verified |
| 100000001 | Failed |
| 100000002 | Pending |
| 100000003 | Not Checked |

---

## Existing Infrastructure Inventory

Before designing new components, here's what already exists and can be reused:

### Already Built (Reuse)

| Component | File | What It Does |
|---|---|---|
| `GraphClientFactory.ForApp()` | `Infrastructure/Graph/GraphClientFactory.cs` | App-only Graph client for shared mailbox operations |
| `GraphClientFactory.ForUserAsync()` | Same file | OBO-based Graph client for user-delegated operations |
| `CommunicationService.SendAsync()` | `Services/Communication/CommunicationService.cs` | Outbound email via Graph sendMail (shared mailbox) |
| `ApprovedSenderValidator` | `Services/Communication/ApprovedSenderValidator.cs` | Sender validation with Dataverse merge + Redis cache |
| `ServiceBusJobProcessor` | `Services/Jobs/ServiceBusJobProcessor.cs` | Generic job processor (BackgroundService) |
| `EmailPollingBackupService` | `Services/Jobs/EmailPollingBackupService.cs` | Backup polling for missed webhooks |
| `EmailToDocumentJobHandler` | `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Email → .eml → SPE → Dataverse document pipeline |
| `EmailAttachmentProcessor` | `Services/Email/EmailAttachmentProcessor.cs` | Attachment filtering, SPE upload, Dataverse records |
| `EmailEndpoints` webhook | `Api/EmailEndpoints.cs` | Dataverse webhook trigger endpoint |
| OBO token caching | `GraphClientFactory.cs` (Redis) | 55-minute TTL token cache for OBO tokens |
| `SendEmailNodeExecutor` | `Services/Ai/Nodes/SendEmailNodeExecutor.cs` | OBO-based email sending (sends as current user) |

### Needs Modification

| Component | Change | Why |
|---|---|---|
| `ApprovedSenderValidator` | Query `sprk_communicationaccount` instead of `sprk_approvedsender` | New table name, additional fields |
| `CommunicationService.SendAsync()` | Add OBO code path for user mailbox sending | Individual user send support |
| `IDataverseService` | Rename `QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync` | Broader entity scope |
| `EmailAttachmentProcessor` | Adapt to accept Graph message attachment objects instead of `activitymimeattachments` | New data source |
| `AttachmentFilterService` | Adapt input model for Graph attachment structure | Same filtering logic, different input shape |

### Needs to Be Built

| Component | Purpose |
|---|---|
| `GraphSubscriptionManager` (BackgroundService) | Create/renew Graph subscriptions for receive-enabled accounts |
| `IncomingCommunicationProcessor` | Process incoming emails into `sprk_communication` records + document archival |
| `CommunicationWebhookEndpoint` | Receive Graph subscription notifications for incoming mail |
| `GraphMessageToEmlConverter` | Convert Graph message objects to RFC 2822 .eml (replaces `EmailToEmlConverter`) |
| `CommunicationPollingBackupService` | Query Graph for missed messages (replaces `EmailPollingBackupService`) |
| Mailbox verification endpoint | Test connectivity and permissions for a communication account |

### Being Retired (No Backward Compatibility)

Since this is a pre-launch product, the following components are fully replaced — no migration or coexistence needed:

| Component | Replacement | Why |
|---|---|---|
| `EmailToEmlConverter` | `GraphMessageToEmlConverter` | Reads from Dataverse `emails({id})` — replaced by Graph `/messages/{id}` |
| `EmailToDocumentJobHandler` | `IncomingCommunicationProcessor` | Coupled to Dataverse email entity; new handler does communication + document in one pipeline |
| `EmailPollingBackupService` | `CommunicationPollingBackupService` | Queries Dataverse `email` entity; new service queries Graph API |
| `EmailEndpoints.cs` webhook | `CommunicationWebhookEndpoint` | Receives Dataverse `email.Create` webhook; replaced by Graph subscription notifications |
| `EmailFilterService` + `sprk_emailprocessingrule` | `sprk_communicationaccount` processing rules | Filter rules migrate to per-account JSON config (`sprk_processingrules`) |
| `EmailRuleSeedService` | Not needed | Default rules embedded in account configuration |
| Server-Side Sync (Exchange → Dataverse) | Graph subscriptions (Exchange → BFF directly) | Eliminates Dataverse email activity as intermediary |
| `sprk_document.sprk_email` lookup | `sprk_document.sprk_communication` lookup | Documents link to communication records, not email activities |
| `sprk_approvedsender` entity | `sprk_communicationaccount` entity | Superseded by unified account entity |

---

## Capability 1: Outbound Shared Mailbox (Existing + Config Change)

### Current Flow (No Code Changes)

```
User fills Communication form → clicks Send
    │
    ▼
sprk_communication_send.js → POST /api/communications/send
    │
    ▼
CommunicationService.SendAsync()
    ├── ApprovedSenderValidator.Resolve(fromMailbox)
    │   ├── Check appsettings.json ApprovedSenders[]        ← Phase 1
    │   └── Merge with sprk_communicationaccount query      ← Phase 2 (rename)
    ├── GraphClientFactory.ForApp()                         ← App-only auth
    ├── POST /users/{mailbox}/sendMail                      ← Graph API
    ├── Create sprk_communication record in Dataverse
    └── Archive .eml to SPE (optional)
```

### Changes Required

1. **Rename Dataverse query**: `QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync`
   - Filter: `sprk_sendenabled eq true and statecode eq 0`
   - Map: `sprk_emailaddress` → Email, `sprk_displayname` → DisplayName, `sprk_isdefaultsender` → IsDefault

2. **Update `appsettings.json`** for `mailbox-central@spaarke.com`:
   ```json
   {
     "Communication": {
       "ApprovedSenders": [
         {
           "Email": "mailbox-central@spaarke.com",
           "DisplayName": "Spaarke Central",
           "IsDefault": true
         }
       ],
       "DefaultMailbox": "mailbox-central@spaarke.com"
     }
   }
   ```
   This serves as the **bootstrap/fallback** until the Dataverse entity is created and populated.

3. **Exchange access policy** (one-time setup):
   ```powershell
   # Create mail-enabled security group "SDAP Communication Senders"
   # Add mailbox-central@spaarke.com to the group
   # Create application access policy:
   New-ApplicationAccessPolicy `
     -AppId "{BFF-App-Client-Id}" `
     -PolicyScopeGroupId "{Security-Group-ObjectId}" `
     -AccessRight RestrictAccess `
     -Description "Restrict BFF to approved communication senders"
   ```

### Graph Permissions Required

| Permission | Type | Purpose |
|---|---|---|
| `Mail.Send` | Application | Send email from shared mailboxes |

---

## Capability 2: Outbound Individual User (New Code Path)

### How It Works

When a user wants to send as themselves rather than from a shared mailbox, the BFF uses the OBO flow (already implemented in `GraphClientFactory.ForUserAsync()`) to send via Graph's `/me/sendMail` endpoint with the user's own delegated token.

### Flow

```
User fills Communication form → selects "Send as me" → clicks Send
    │
    ▼
sprk_communication_send.js → POST /api/communications/send
    │  (includes user's bearer token + sendMode: "user")
    ▼
CommunicationService.SendAsync()
    ├── IF sendMode == "user":
    │   ├── GraphClientFactory.ForUserAsync(httpContext)     ← OBO auth
    │   ├── POST /me/sendMail                               ← Graph API (as user)
    │   └── sprk_from = user's email address
    │
    ├── ELSE (shared mailbox - existing flow):
    │   ├── ApprovedSenderValidator.Resolve(fromMailbox)
    │   ├── GraphClientFactory.ForApp()
    │   └── POST /users/{mailbox}/sendMail
    │
    ├── Create sprk_communication record
    └── Archive .eml to SPE (optional)
```

### Changes Required

1. **Add `SendMode` to request DTO**:
   ```csharp
   public enum SendMode { SharedMailbox, User }

   public sealed record SendCommunicationRequest
   {
       // ... existing fields ...
       public SendMode SendMode { get; init; } = SendMode.SharedMailbox;
   }
   ```

2. **Branch in `CommunicationService.SendAsync()`**:
   - `SendMode.User` → use `ForUserAsync()`, skip approved sender validation, set `sprk_from` to user's email
   - `SendMode.SharedMailbox` → existing flow unchanged

3. **Update web resource** to pass send mode and bearer token

4. **Communication form UX**: Add a choice field or toggle for "Send from" with options:
   - Shared mailbox (dropdown of enabled accounts)
   - My mailbox (sends as authenticated user)

### Graph Permissions Required

| Permission | Type | Purpose |
|---|---|---|
| `Mail.Send` | Delegated | Send email as the authenticated user |

**Note**: Delegated `Mail.Send` is already available through the OBO flow's `.default` scope, provided it's been granted on the app registration.

### Scope Assessment

| Item | Effort | Notes |
|---|---|---|
| Add SendMode to DTO + CommunicationService branch | Small | 2-3 hours, straightforward branching |
| Update web resource for mode selection | Small | Add dropdown/toggle to Send dialog |
| Update Communication form UX | Small | Add choice field |
| Resolve user email from OBO token | Small | `SendEmailNodeExecutor` already does this |
| **Total** | **~8 hours** | Not a significant scope increase |

**Recommendation**: Include in this project. The OBO infrastructure already exists (`ForUserAsync()`, `SendEmailNodeExecutor`), so this is mostly a branching exercise in `CommunicationService` and a UX addition.

---

## Capability 3: Inbound Shared Mailbox Monitoring (New Pipeline)

### Architecture

```
mailbox-central@spaarke.com receives email
    │
    ▼
Graph subscription fires webhook
    │
    ▼
POST /api/communications/incoming-webhook          ← NEW endpoint
    ├── Validate Graph subscription notification
    ├── Extract message metadata (from, subject, mailbox)
    └── Enqueue IncomingCommunicationJob to Service Bus
         │
         ▼
IncomingCommunicationProcessor (BackgroundService)  ← NEW handler
    ├── GET /users/{mailbox}/messages/{id}          ← Fetch full message
    ├── Create sprk_communication record
    │   ├── Direction = Incoming
    │   ├── sprk_from = sender email
    │   ├── sprk_to = mailbox address
    │   ├── sprk_subject, sprk_body
    │   ├── sprk_receivedat = message receivedDateTime
    │   └── sprk_graphmessageid = message.id
    ├── Process attachments → SPE (reuse EmailAttachmentProcessor)
    ├── Resolve associations (match to Matter/Project)
    │   ├── Check sender → known contacts/accounts
    │   ├── Check subject → matter numbers, project references
    │   └── Check email thread → existing communication chain
    └── Archive .eml to SPE (reuse EmlGenerationService)
```

### Graph Subscription Management

A new `GraphSubscriptionManager` BackgroundService manages Graph webhook subscriptions for all receive-enabled accounts:

```
On startup + every 30 minutes:
    │
    ▼
Query sprk_communicationaccount
    WHERE sprk_receiveenabled = true AND statecode = 0
    │
    ▼
FOR EACH account:
    │
    ├── IF no subscription exists (sprk_graphsubscriptionid is null):
    │   └── POST /subscriptions (create new)
    │       ├── changeType: "created"
    │       ├── resource: "/users/{email}/mailFolders/{folder}/messages"
    │       ├── notificationUrl: "{BFF}/api/communications/incoming-webhook"
    │       ├── expirationDateTime: now + 3 days (Graph max for mail)
    │       └── clientState: {HMAC validation secret}
    │
    ├── IF subscription exists AND expiry < 24 hours from now:
    │   └── PATCH /subscriptions/{id} (renew)
    │       └── expirationDateTime: now + 3 days
    │
    ├── IF subscription exists AND status = Failed:
    │   └── DELETE old + POST new subscription
    │
    └── UPDATE sprk_communicationaccount:
        ├── sprk_graphsubscriptionid = subscription.id
        ├── sprk_subscriptionexpiry = subscription.expirationDateTime
        └── sprk_subscriptionstatus = Active/Failed
```

### Backup Polling (Resilience)

Reuse the `EmailPollingBackupService` pattern — a periodic timer that queries Graph for recent messages missed by the webhook:

```
Every 5 minutes:
    Query each receive-enabled mailbox
    GET /users/{email}/mailFolders/inbox/messages?$filter=receivedDateTime ge {lastPoll}
    For each message not already tracked (check sprk_graphmessageid):
        Enqueue IncomingCommunicationJob
```

### Association Resolution for Incoming Email

When an incoming email arrives, the system attempts to link it to existing entities:

| Signal | Resolution | Priority |
|---|---|---|
| Email thread (In-Reply-To header) | Find existing `sprk_communication` with matching `sprk_graphmessageid` → copy associations | 1 (highest) |
| Sender email address | Match to `contact.emailaddress1` or `account` email → associate | 2 |
| Subject line patterns | Regex match matter numbers (`MAT-\d+`), project references | 3 |
| Recipient mailbox context | If mailbox is purpose-specific (e.g., `billing@`) → associate with billing-related entities | 4 |

Unresolved associations are flagged for manual review — the Communication record is created with `sprk_associationstatus = Pending Review`.

### Graph Permissions Required

| Permission | Type | Purpose |
|---|---|---|
| `Mail.Read` | Application | Read incoming email from monitored mailboxes |
| `Mail.ReadBasic` | Application | Alternative (less privileged) — headers only, no body |

**Recommendation**: Use `Mail.Read` (full access) since we need body content for the communication record and association resolution.

### Exchange Access Policy Update

The existing security group needs `Mail.Read` in addition to `Mail.Send`. The application access policy restricts both permissions to the same mailbox group — no additional policy needed, just add the permission to the app registration.

---

## Capability 4: Inbound Individual User (Deferred)

### Why Defer

Monitoring individual user mailboxes is significantly more complex:

| Aspect | Shared Mailbox | Individual User |
|---|---|---|
| Auth | Single app-only credential | Per-user OBO token (must be refreshed) |
| Consent | Admin consent once | Each user must consent |
| Subscription | One per shared mailbox | One per user (N subscriptions) |
| Scale | 1-10 mailboxes | Potentially hundreds |
| Privacy | Firm-owned mailbox, no privacy concern | Personal mailbox, privacy implications |
| Token lifecycle | App credential never expires (rotated) | User tokens expire, require re-auth |

### What It Would Require (Future)

1. User opt-in flow: User authorizes the app to read their mailbox
2. Refresh token storage: Securely store per-user refresh tokens
3. Per-user subscription management: N Graph subscriptions (one per opted-in user)
4. Privacy controls: What emails to capture (all? only those matching entity patterns?)
5. Consent revocation handling: When a user removes consent

**Recommendation**: Defer to a separate project. The shared mailbox pattern covers the primary use case (firm communications). Individual user monitoring is a fundamentally different consent and privacy model.

---

## Implementation Phases

### Phase A: Communication Account Entity + Outbound Config (Immediate)

**Goal**: Replace `appsettings.json` approved senders with Dataverse-managed accounts. Get `mailbox-central@spaarke.com` sending.

**Tasks**:
1. Create `sprk_communicationaccount` entity in Dataverse (core + outbound fields)
2. Create form, views for admin management
3. Seed `mailbox-central@spaarke.com` as default send-enabled account
4. Update `ApprovedSenderValidator` to query new entity name
5. Update `IDataverseService.QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync`
6. Configure `appsettings.json` with `mailbox-central@spaarke.com` as fallback
7. Set up Exchange application access policy
8. Deploy BFF + test outbound send end-to-end

**Estimate**: ~16 hours
**Blocks**: Everything else (must work before incoming or individual send)

### Phase B: Individual User Outbound (Can Parallel with C)

**Goal**: Users can choose "Send as me" in addition to shared mailbox.

**Tasks**:
1. Add `SendMode` enum and field to `SendCommunicationRequest`
2. Branch `CommunicationService.SendAsync()` for OBO path
3. Resolve user email from OBO token claims
4. Update web resource with send mode selection UI
5. Update Communication form with send mode choice
6. Test: Send as shared mailbox vs. send as individual user

**Estimate**: ~8 hours
**Dependencies**: Phase A complete

### Phase C: Inbound Shared Mailbox Monitoring (Can Parallel with B)

**Goal**: Incoming emails to `mailbox-central@spaarke.com` automatically create `sprk_communication` records.

**Tasks**:
1. Add `Mail.Read` application permission to app registration
2. Create `GraphSubscriptionManager` BackgroundService
3. Create `POST /api/communications/incoming-webhook` endpoint
4. Create `IncomingCommunicationProcessor` job handler
5. Implement association resolution (thread, sender, subject)
6. Add inbound fields to `sprk_communicationaccount` (subscription tracking)
7. Create backup polling service for missed webhooks
8. Create incoming communication views in Dataverse
9. Test: Send email to `mailbox-central@spaarke.com` → verify communication record created

**Estimate**: ~24 hours
**Dependencies**: Phase A complete

### Phase D: Verification & Admin UX

**Goal**: Admins can verify mailbox connectivity and manage accounts.

**Tasks**:
1. Create `POST /api/communications/accounts/{id}/verify` endpoint
2. Implement mailbox verification logic (test send + test read)
3. Update `sprk_communicationaccount` form with verification status
4. Add daily send count tracking + reset
5. Admin documentation: "How to add a new communication account"

**Estimate**: ~8 hours
**Dependencies**: Phases A-C complete

### Phase E: Email-to-Document Migration (Graph-Based Archival Pipeline)

**Goal**: Replace the Server-Side Sync / Dataverse email-based document archival pipeline with a Graph API-based pipeline that archives emails as `.eml` documents linked to `sprk_communication` records. Retire all dependencies on Dataverse email activities.

#### Background

The existing email-to-document system (`EmailToDocumentJobHandler`, `EmailToEmlConverter`, `EmailPollingBackupService`) was built around Dataverse email activities created by Server-Side Sync. It:

1. Triggers on Dataverse `email.Create` webhook
2. Reads email data from Dataverse Web API (`emails({id})`, `activitymimeattachments`)
3. Converts to `.eml` using MimeKit
4. Uploads to SPE and creates `sprk_document` records linked via `sprk_email` lookup
5. Processes attachments from `activitymimeattachments` as child documents
6. Enqueues AI analysis (Document Profile + RAG indexing)

With the migration to Graph subscriptions (Phase C), the data source changes from Dataverse to Graph API. Since this is pre-launch, we do a clean cutover with no backward compatibility.

#### Architecture

```
OUTBOUND EMAIL (from CommunicationService.SendAsync):
    │
    ▼
CommunicationService.SendAsync() (existing R1 pipeline)
    ├── Send via Graph sendMail                          ← CRITICAL PATH
    ├── Create sprk_communication record                 ← best-effort
    ├── Archive .eml to SPE via EmlGenerationService     ← best-effort (existing)
    ├── Create sprk_document linked to sprk_communication ← NEW linkage
    ├── Create sprk_communicationattachment records       ← best-effort (existing)
    └── Enqueue AI analysis for .eml document             ← NEW


INBOUND EMAIL (from IncomingCommunicationProcessor — Phase C):
    │
    ▼
IncomingCommunicationProcessor
    ├── GET /users/{mailbox}/messages/{id}?$expand=attachments  ← Graph API
    ├── Create sprk_communication record (Direction = Incoming)
    │
    ├── Convert Graph message → .eml via GraphMessageToEmlConverter
    │   ├── Map Graph Message headers → MimeMessage (MimeKit)
    │   ├── Map Graph FileAttachment[] → MIME attachments
    │   └── Output: MemoryStream (.eml)
    │
    ├── Upload .eml to SPE
    │   └── Path: /communications/{communicationId:N}/{subject}_{timestamp}.eml
    │
    ├── Create sprk_document record
    │   ├── sprk_documenttype = Email (100000006)
    │   ├── sprk_isemailarchive = true
    │   ├── sprk_communication = sprk_communication lookup  ← NEW (replaces sprk_email)
    │   ├── sprk_emailsubject, sprk_emailfrom, sprk_emailto, sprk_emailcc
    │   ├── sprk_emaildate, sprk_emailmessageid, sprk_emaildirection
    │   └── sprk_graphitemid, sprk_graphdriveid, sprk_filepath, etc.
    │
    ├── Process attachments → child documents
    │   ├── Filter via AttachmentFilterService (reuse filtering logic)
    │   ├── For each Graph FileAttachment:
    │   │   ├── Upload to SPE: /communications/attachments/{parentDocId:N}/{filename}
    │   │   ├── Create child sprk_document record
    │   │   │   ├── sprk_documenttype = Email Attachment (100000007)
    │   │   │   ├── sprk_parentdocument = parent .eml document
    │   │   │   └── sprk_communication = NOT SET (parent already uses it)
    │   │   └── Enqueue AI analysis for attachment
    │   └── Result: AttachmentProcessingResult (extracted/filtered/uploaded/failed)
    │
    ├── Resolve associations (thread, sender, subject — from Phase C)
    └── Enqueue AI analysis for .eml document
```

#### New Component: `GraphMessageToEmlConverter`

Replaces `EmailToEmlConverter`. Instead of fetching email data from Dataverse, it accepts a Graph `Message` object directly.

```csharp
public interface IGraphMessageToEmlConverter
{
    /// <summary>
    /// Convert a Graph Message (with expanded attachments) to RFC 2822 .eml format.
    /// </summary>
    Task<EmlConversionResult> ConvertAsync(
        Message graphMessage,
        CancellationToken cancellationToken = default);
}
```

**Input**: Microsoft.Graph `Message` object (fetched via `GET /users/{mailbox}/messages/{id}?$expand=attachments&$select=...`)

**Mapping**:

| Graph Message Property | MimeMessage Field | Notes |
|---|---|---|
| `from.emailAddress` | `message.From` | Single address |
| `toRecipients[]` | `message.To` | Multiple addresses |
| `ccRecipients[]` | `message.Cc` | Multiple addresses |
| `bccRecipients[]` | `message.Bcc` | Multiple addresses (outbound only) |
| `subject` | `message.Subject` | Direct map |
| `body.content` | Body part | HTML or PlainText based on `body.contentType` |
| `receivedDateTime` | `message.Date` | For inbound |
| `sentDateTime` | `message.Date` | For outbound |
| `internetMessageId` | `message.MessageId` | RFC 2822 Message-ID |
| `internetMessageHeaders` | Custom headers | `In-Reply-To`, `References` for threading |
| `conversationId` | Custom header | Graph conversation tracking |
| `attachments[]` (FileAttachment) | MIME attachments | Base64 → binary → MimePart |

**Attachment handling within EML**:

```csharp
foreach (var attachment in graphMessage.Attachments.OfType<FileAttachment>())
{
    var mimePart = new MimePart(attachment.ContentType)
    {
        Content = new MimeContent(new MemoryStream(attachment.ContentBytes)),
        ContentDisposition = new ContentDisposition(
            attachment.IsInline == true
                ? ContentDisposition.Inline
                : ContentDisposition.Attachment),
        ContentTransferEncoding = ContentEncoding.Base64,
        FileName = attachment.Name
    };

    if (attachment.IsInline == true && attachment.ContentId != null)
        mimePart.ContentId = attachment.ContentId;

    multipart.Add(mimePart);
}
```

**Key difference from `EmailToEmlConverter`**: No Dataverse authentication needed. No HTTP calls to fetch data. The Graph message is already fetched by the caller (`IncomingCommunicationProcessor`) and passed in directly. This makes the converter a pure transformation — fast, testable, no I/O.

#### `sprk_document` Schema Changes

**Remove** (no longer used — pre-launch, clean cutover):

| Field | Reason |
|---|---|
| `sprk_email` (Lookup to email activity) | Replaced by `sprk_communication` lookup |

**Add**:

| Display Name | Logical Name | Type | Description |
|---|---|---|---|
| Communication | `sprk_communication` | Lookup → `sprk_communication` | Links archived document to communication record |

**Retain unchanged** (reuse existing email metadata fields):

| Field | Still Used For |
|---|---|
| `sprk_isemailarchive` | Flag for email-sourced documents |
| `sprk_documenttype` | Email (100000006), Email Attachment (100000007) |
| `sprk_emailsubject` | Subject line |
| `sprk_emailfrom` | Sender address |
| `sprk_emailto` | Recipients |
| `sprk_emailcc` | CC recipients |
| `sprk_emaildate` | Email timestamp |
| `sprk_emailmessageid` | RFC 2822 Message-ID (for dedup + threading) |
| `sprk_emaildirection` | Received (100000000) / Sent (100000001) |
| `sprk_emailconversationindex` | Conversation tracking (now from Graph `conversationId`) |
| `sprk_parentdocument` | Parent .eml → child attachment relationship |
| `sprk_parentfilename` | Parent document name |
| `sprk_relationshiptype` | Email Attachment (100000000) |

#### Outbound Document Archival Enhancement

The existing `CommunicationService.SendAsync()` already archives `.eml` to SPE via `EmlGenerationService` (Step 6-7 of the send pipeline). The enhancement:

1. **Link document to communication**: Set `sprk_communication` lookup (instead of no email linkage)
2. **Process outbound attachments as child documents**: Reuse the same child document pattern from inbound
3. **Enqueue AI analysis**: Same `AppOnlyDocumentAnalysis` job pattern

This is a small addition to the existing send pipeline — the `.eml` generation and SPE upload already work.

#### Graph Attachment → `EmailAttachmentInfo` Adapter

The existing `AttachmentFilterService` and child document creation logic work on `EmailAttachmentInfo` objects. Rather than rewriting the filter service, create an adapter:

```csharp
public static class GraphAttachmentAdapter
{
    public static EmailAttachmentInfo ToAttachmentInfo(FileAttachment graphAttachment)
    {
        return new EmailAttachmentInfo
        {
            AttachmentId = Guid.NewGuid(), // Graph attachments use string IDs
            FileName = graphAttachment.Name,
            MimeType = graphAttachment.ContentType ?? "application/octet-stream",
            Content = new MemoryStream(graphAttachment.ContentBytes),
            SizeBytes = graphAttachment.Size ?? graphAttachment.ContentBytes?.Length ?? 0,
            IsInline = graphAttachment.IsInline ?? false,
            ContentId = graphAttachment.ContentId,
            ShouldCreateDocument = true // Let AttachmentFilterService decide
        };
    }
}
```

This preserves all existing filtering logic (blocked extensions, signature image detection, tracking pixel filtering, size limits) without modification.

#### SPE File Path Convention Change

| Document Type | Old Path | New Path |
|---|---|---|
| Archived .eml | `/emails/{filename}.eml` | `/communications/{communicationId:N}/{filename}.eml` |
| Attachment | `/emails/attachments/{parentDocId:N}/{filename}` | `/communications/attachments/{parentDocId:N}/{filename}` |

The new paths use `communications/` to reflect the unified model. Since pre-launch, no migration of existing files needed.

#### Deduplication Strategy

Both the Graph subscription webhook and the backup polling service may attempt to process the same message. Deduplication uses:

1. **Graph Message ID**: `sprk_graphmessageid` on `sprk_communication` is unique — check before creating record
2. **Redis idempotency key**: `Communication:{graphMessageId}:Process` with 7-day retention
3. **Processing lock**: 5-minute Redis lock prevents concurrent processing of the same message

This mirrors the existing `Email:{emailId}:Archive` idempotency pattern.

#### Retirement Checklist

The following components are **deleted** (not deprecated) as part of this phase:

| Component | File | Status |
|---|---|---|
| `EmailToEmlConverter` | `Services/Email/EmailToEmlConverter.cs` | Delete |
| `IEmailToEmlConverter` | `Services/Email/IEmailToEmlConverter.cs` | Delete |
| `EmailToDocumentJobHandler` | `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Delete |
| `EmailPollingBackupService` | `Services/Jobs/EmailPollingBackupService.cs` | Delete |
| `EmailFilterService` | `Services/Email/EmailFilterService.cs` | Delete |
| `IEmailFilterService` | `Services/Email/IEmailFilterService.cs` | Delete |
| `EmailRuleSeedService` | `Services/Email/EmailRuleSeedService.cs` | Delete |
| `EmailEndpoints` webhook routes | `Api/EmailEndpoints.cs` (webhook-related routes only) | Delete |
| `EmailProcessingOptions` webhook/polling config | `Configuration/EmailProcessingOptions.cs` | Delete or consolidate |
| `sprk_emailprocessingrule` entity | Dataverse | Delete from solution |
| `sprk_approvedsender` entity | Dataverse | Delete from solution |
| `sprk_document.sprk_email` lookup | Dataverse | Remove field |
| Dataverse Service Endpoint (email webhook) | Dataverse | Delete registration |
| Server-Side Sync mailbox configuration | Exchange/Dataverse | Disable |

**Preserve** (still useful for manual save-as-document or other document operations):
| Component | File | Reason |
|---|---|---|
| `AttachmentFilterService` | `Services/Email/AttachmentFilterService.cs` | Reused with adapter for Graph attachments |
| `EmailAttachmentProcessor` | `Services/Email/EmailAttachmentProcessor.cs` | Reused for child document creation |
| `EmlGenerationService` | `Services/Communication/EmlGenerationService.cs` | Reused for outbound archival |
| `EmailTelemetry` | `Telemetry/EmailTelemetry.cs` | Rename to `CommunicationTelemetry`, update event names |

#### Tasks

1. Create `GraphMessageToEmlConverter` (implements `IGraphMessageToEmlConverter`)
2. Create `GraphAttachmentAdapter` (maps Graph `FileAttachment` → `EmailAttachmentInfo`)
3. Add `sprk_communication` lookup to `sprk_document` entity in Dataverse
4. Remove `sprk_email` lookup from `sprk_document` entity
5. Integrate document archival into `IncomingCommunicationProcessor` (Phase C handler)
   - After creating `sprk_communication` record, run document archival pipeline
   - Convert Graph message → .eml via `GraphMessageToEmlConverter`
   - Upload .eml to SPE, create `sprk_document` with `sprk_communication` lookup
   - Process attachments as child documents via adapter + `AttachmentFilterService`
   - Enqueue AI analysis for .eml and each attachment
6. Enhance `CommunicationService.SendAsync()` outbound pipeline
   - Link existing .eml archive step to `sprk_communication` via new lookup
   - Process outbound attachments as child documents
   - Enqueue AI analysis for outbound .eml
7. Delete retired components (see retirement checklist)
8. Delete `sprk_emailprocessingrule` and `sprk_approvedsender` entities from Dataverse solution
9. Disable Server-Side Sync mailbox configuration
10. Rename `EmailTelemetry` → `CommunicationTelemetry`, update metric names
11. Update `EmailProcessingOptions` → consolidate into `CommunicationOptions` (single config section)
12. Test: Inbound email → communication record + archived .eml document + child attachment documents + AI analysis enqueued
13. Test: Outbound email → communication record + archived .eml document + child attachment documents + AI analysis enqueued

**Estimate**: ~20 hours
**Dependencies**: Phase C complete (inbound processing pipeline must exist before integrating document archival)

---

## Exchange & Azure AD Setup Guide

### One-Time Setup

#### 1. App Registration Permissions

```
Azure AD → App Registrations → {BFF App (b36e9b91-...)}
  → API Permissions → Add a permission → Microsoft Graph
    → Application permissions:
      ✅ Mail.Send          (outbound email)
      ✅ Mail.Read          (inbound email monitoring)
      ✅ Mail.ReadBasic     (optional, lighter permission)
    → Delegated permissions:
      ✅ Mail.Send          (individual user outbound)
  → Grant admin consent
```

#### 2. Mail-Enabled Security Group

```powershell
# Connect to Exchange Online
Connect-ExchangeOnline -UserPrincipalName admin@spaarke.com

# Create mail-enabled security group
New-DistributionGroup `
  -Name "SDAP Mailbox Access" `
  -Type "Security" `
  -PrimarySmtpAddress "sdap-mailbox-access@spaarke.com" `
  -ManagedBy "admin@spaarke.com"

# Add mailbox-central@spaarke.com
Add-DistributionGroupMember `
  -Identity "SDAP Mailbox Access" `
  -Member "mailbox-central@spaarke.com"
```

#### 3. Application Access Policy

```powershell
# Get the security group's object ID from Azure AD
$groupId = (Get-AzureADGroup -SearchString "SDAP Mailbox Access").ObjectId

# Restrict BFF app to only this group's mailboxes
New-ApplicationAccessPolicy `
  -AppId "b36e9b91-ee7d-46e6-9f6a-376871cc9d54" `
  -PolicyScopeGroupId $groupId `
  -AccessRight RestrictAccess `
  -Description "Restrict BFF Communication Service to approved mailboxes"

# Verify (may take 30 minutes to propagate)
Test-ApplicationAccessPolicy `
  -Identity "mailbox-central@spaarke.com" `
  -AppId "b36e9b91-ee7d-46e6-9f6a-376871cc9d54"
# Expected: AccessCheckResult = Granted

# Verify an unauthorized mailbox is denied
Test-ApplicationAccessPolicy `
  -Identity "random-user@spaarke.com" `
  -AppId "b36e9b91-ee7d-46e6-9f6a-376871cc9d54"
# Expected: AccessCheckResult = Denied
```

#### 4. Adding New Mailboxes (Ongoing)

When a new `sprk_communicationaccount` record is created in Dataverse:

```powershell
# Add to the security group
Add-DistributionGroupMember `
  -Identity "SDAP Mailbox Access" `
  -Member "new-mailbox@spaarke.com"

# The application access policy automatically covers all group members
# No policy changes needed
```

**Future automation**: Phase D could include a BFF endpoint that calls Graph API to add/remove group members when `sprk_communicationaccount` records are created/deactivated.

---

## Client-Side Authentication: `@spaarke/auth`

All client-side components (web resources, code pages, UI controls) in this project **MUST** use the shared `@spaarke/auth` package for authentication. Do not implement custom MSAL flows or token acquisition.

### Package Overview

`@spaarke/auth` provides a unified authentication layer with a 5-strategy cascade:

```typescript
import { initAuth, getAuthProvider, authenticatedFetch } from '@spaarke/auth';
```

| Export | Purpose |
|---|---|
| `initAuth(config?)` | Initialize auth provider (MSAL + Xrm + bridge strategies) — call once at startup |
| `getAuthProvider()` | Returns singleton `SpaarkeAuthProvider` instance |
| `authenticatedFetch(url, options?)` | Drop-in `fetch()` replacement with auto-attached Bearer tokens |
| `SpaarkeAuthProvider` | Core class: `getAccessToken()`, `getTenantId()`, `clearCache()` |

### Token Acquisition Strategy (5-Strategy Cascade)

1. **Bridge** → Check for bridged token (cross-window communication)
2. **Cache** → Check cached token (not expired)
3. **Xrm** → Acquire from Xrm.Utility (Dataverse context)
4. **MSAL Silent** → Silent token acquisition via MSAL.js
5. **MSAL Popup** → Interactive popup (last resort)

Includes proactive token refresh on a 4-minute interval.

### Usage in This Project

**For BFF API calls** (recommended pattern — BFF handles Graph auth server-side):

```typescript
// Communication form web resource (sprk_communication_send.js)
import { initAuth, authenticatedFetch } from '@spaarke/auth';

await initAuth();

// Send email via BFF — BFF uses GraphClientFactory.ForApp() or ForUserAsync() server-side
const response = await authenticatedFetch('/api/communications/send', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request)
});
```

**For code pages** (React 18 standalone dialogs):

```typescript
// In code page entry point (e.g., index.tsx)
import { initAuth } from '@spaarke/auth';

await initAuth();
createRoot(document.getElementById('root')!).render(<App />);
```

### Graph API Access Pattern

**Important**: The default `bffApiScope` targets the BFF API (`api://1e40baad.../user_impersonation`). For Microsoft Graph operations (Mail.Read, Mail.Send):

| Approach | When to Use | How |
|---|---|---|
| **Call Graph through BFF** (recommended) | All shared mailbox operations, server-side processing | Client calls BFF endpoints → BFF uses `GraphClientFactory.ForApp()` with application permissions |
| **OBO via BFF** | Individual user send ("Send as me") | Client sends bearer token → BFF exchanges via `GraphClientFactory.ForUserAsync()` → Graph `/me/sendMail` |
| **Direct Graph from client** | Not recommended for this project | Would require additional MSAL scopes configured client-side |

All Graph operations in this project flow through the BFF API. The client authenticates to the BFF; the BFF authenticates to Graph. This avoids exposing Graph permissions to the client and follows the existing SDAP architecture pattern.

### Webpack Alias Configuration

The `@spaarke/auth` alias is already configured in multiple code pages. New code pages in this project should follow the same pattern:

```javascript
// webpack.config.js
resolve: {
    alias: {
        '@spaarke/auth': path.resolve(__dirname, '../../shared/SpaarkeAuth/src')
    }
}
```

### 401 Handling

On `401 Unauthorized` from the BFF:
1. Call `getAuthProvider().clearCache()` to force re-acquisition
2. Retry the request once
3. If still 401, show user-friendly error: "Session expired. Please refresh the page."

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Graph subscription webhook unreliable | Missed incoming emails | Backup polling service (every 5 min) |
| Graph subscription expires (3-day max for mail) | Monitoring stops | `GraphSubscriptionManager` renews when < 24h remaining |
| Exchange access policy propagation delay | Send/read fails for 30 min after setup | Verify with `Test-ApplicationAccessPolicy` before going live |
| OBO token expired during individual send | User gets error | Clear error message: "Please refresh the page and try again" |
| Graph rate limits (10,000/day per mailbox) | Send failures | `sprk_dailysendlimit` tracking + multiple mailboxes for load distribution |
| Dataverse query failure for account config | Can't resolve senders | Retain `appsettings.json` as fallback (existing pattern) |
| Individual user revokes consent | OBO flow fails for that user | Graceful error: "Mail permission required. Please re-authorize." |
| Graph message fetch fails during archival | Communication record created but no .eml document | Document archival is best-effort (non-fatal); communication record still tracks the email; backup polling will retry |
| Large attachments exceed Graph download limits | Attachment child documents not created | Individual attachment failures are isolated; other attachments continue; log warning with attachment details |
| Duplicate processing (webhook + polling race) | Same message processed twice | Redis idempotency key (`Communication:{graphMessageId}:Process`) + `sprk_graphmessageid` uniqueness check |

---

## Out of Scope

- **Individual user inbound monitoring**: Monitoring personal mailboxes requires per-user consent, refresh token management, and privacy controls. Deferred to future project.
- **Automated Exchange group management**: Automatically adding/removing mailboxes from the security group when Dataverse records change. Manual process for now, automation in Phase D.
- **Email templates**: Predefined email templates with merge fields. Separate feature.
- **Email scheduling**: Delayed send / send-at-time functionality. Separate feature.
- **Conversation threading UI**: Displaying email threads in a conversation view. Separate feature (could enhance the Communication form later).
- **Migration of existing email-linked documents**: Since this is pre-launch, existing `sprk_document` records linked via `sprk_email` to Dataverse email activities are not migrated. They will be orphaned or cleaned up manually.
- **Backward compatibility with Server-Side Sync**: No dual-pipeline coexistence. Server-Side Sync is fully retired for email processing.

---

## Success Criteria

1. **Outbound shared mailbox**: Send email from `mailbox-central@spaarke.com` via Communication form → email received by recipient → `sprk_communication` record tracks it
2. **Outbound individual**: User selects "Send as me" → email sent from user's own mailbox → `sprk_communication` record tracks it
3. **Inbound monitoring**: Email sent TO `mailbox-central@spaarke.com` → `sprk_communication` record auto-created with Direction = Incoming
4. **Association resolution**: Incoming email from known contact auto-linked to their Account/Contact record
5. **Admin management**: Add/remove/configure communication accounts entirely through Dataverse UI
6. **Verification**: Admin can click "Verify" on any account → confirms send and/or read access works
7. **Resilience**: Missed webhooks caught by backup polling within 5 minutes
8. **Inbound document archival**: Incoming email → `.eml` document archived to SPE → `sprk_document` record linked to `sprk_communication` via lookup
9. **Inbound attachment processing**: Incoming email attachments → filtered → uploaded to SPE as child documents → AI analysis enqueued
10. **Outbound document archival**: Sent email → `.eml` document archived to SPE → `sprk_document` record linked to `sprk_communication` → child attachment documents created
11. **Server-Side Sync retired**: No Dataverse email activities created for communication purposes; all email processing flows through Graph API
12. **Legacy cleanup**: `EmailToEmlConverter`, `EmailToDocumentJobHandler`, `EmailPollingBackupService`, and related Dataverse entities (`sprk_emailprocessingrule`, `sprk_approvedsender`) fully removed

---

## Appendix: Existing Email Infrastructure Reuse Map

| Existing Component | Used By (Current) | Reuse In (R2) | Action |
|---|---|---|---|
| `GraphClientFactory.ForApp()` | Outbound shared send | Inbound shared read, Graph message fetch | Reuse as-is |
| `GraphClientFactory.ForUserAsync()` | `SendEmailNodeExecutor` | Outbound individual send | Reuse as-is |
| `ServiceBusJobProcessor` | Email-to-document jobs | Incoming communication jobs | Reuse as-is |
| `EmailAttachmentProcessor` | Email-to-document | Incoming communication attachments | Reuse with Graph adapter |
| `AttachmentFilterService` | Email-to-document | Filter Graph attachments | Reuse with `GraphAttachmentAdapter` |
| `EmlGenerationService` | Outbound archival | Outbound archival (unchanged) | Reuse as-is |
| OBO token caching (Redis) | Graph client | Individual user send | Reuse as-is |
| `ApprovedSenderValidator` | Outbound shared send | Renamed, queries `sprk_communicationaccount` | Modify |
| `EmailTelemetry` | Email processing metrics | Communication processing metrics | Rename to `CommunicationTelemetry` |
| `EmailToEmlConverter` | Email-to-document | — | **Delete** (replaced by `GraphMessageToEmlConverter`) |
| `EmailToDocumentJobHandler` | Email-to-document | — | **Delete** (replaced by `IncomingCommunicationProcessor`) |
| `EmailPollingBackupService` | Email activity polling | — | **Delete** (replaced by `CommunicationPollingBackupService`) |
| `EmailEndpoints` webhook | Dataverse webhook | — | **Delete** (replaced by `CommunicationWebhookEndpoint`) |
| `EmailFilterService` + `EmailRuleSeedService` | Email filtering | — | **Delete** (replaced by per-account `sprk_processingrules`) |

## Appendix: Phase Dependency Graph

```
Phase A: Communication Account Entity + Outbound Config
    │
    ├──→ Phase B: Individual User Outbound (can parallel with C)
    │
    ├──→ Phase C: Inbound Shared Mailbox Monitoring (can parallel with B)
    │        │
    │        └──→ Phase E: Email-to-Document Migration (depends on C)
    │
    └──→ Phase D: Verification & Admin UX (depends on A-C)

Suggested execution order: A → B+C (parallel) → E → D
```
