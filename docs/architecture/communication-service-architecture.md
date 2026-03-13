# Communication Service Architecture

> **Last Updated**: March 12, 2026
> **Purpose**: Architecture documentation for the Communication Service module — outbound/inbound email via Microsoft Graph, Dataverse-managed mailbox accounts, SPE archival, and AI playbook integration.
> **Status**: Implemented (R2 Complete)
> **Branch**: `work/email-communication-solution-r2`

---

## Table of Contents

- [Overview](#overview)
- [Architecture Principles](#architecture-principles)
- [System Architecture](#system-architecture)
- [Component Inventory](#component-inventory)
- [Outbound Send Pipeline](#outbound-send-pipeline)
- [Inbound Pipeline](#inbound-pipeline)
- [API Endpoints](#api-endpoints)
- [Communication Accounts](#communication-accounts)
- [Sender Resolution](#sender-resolution)
- [Inbound Association Resolution](#inbound-association-resolution)
- [Attachment Handling](#attachment-handling)
- [EML Archival](#eml-archival)
- [Background Services](#background-services)
- [Dataverse Entity Schema](#dataverse-entity-schema)
- [Configuration](#configuration)
- [DI Registration](#di-registration)
- [Telemetry](#telemetry)
- [Error Handling](#error-handling)
- [Security](#security)
- [UI Integration](#ui-integration)
- [AI Playbook Integration](#ai-playbook-integration)
- [ADR Compliance](#adr-compliance)

---

## Overview

The Communication Service provides unified email send and receive via Microsoft Graph API, replacing both Dataverse email activities (outbound) and Server-Side Sync (inbound). Mailboxes are managed as `sprk_communicationaccount` records in Dataverse with send/receive capabilities, verification, and daily send quotas.

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Graph API over Dataverse email activities | Avoids complex activity party resolution, faster send, no plugin overhead |
| Graph subscriptions over Server-Side Sync | Real-time webhooks (<60s), backup polling (<5 min), no Exchange dependency |
| `sprk_communicationaccount` entity | Centralized mailbox management in Dataverse; replaces `appsettings.json`-only config |
| Best-effort tracking | Email send is the critical path; Dataverse record, SPE archival, and attachment records are non-fatal |
| Dual send modes (SharedMailbox + User) | Shared mailbox via app-only auth; individual user via OBO |
| Per-endpoint authorization filter | Follows ADR-008; avoids global middleware |
| Feature module DI pattern | `CommunicationModule` registers all services; ADR-010 compliant |

### R2 Changes from R1

| Area | R1 | R2 |
|------|----|----|
| Mailbox management | `appsettings.json` only | `sprk_communicationaccount` entity in Dataverse |
| Inbound email | Not implemented | Graph subscriptions + backup polling + `IncomingCommunicationProcessor` |
| Send modes | Shared mailbox only | Shared mailbox + individual user (OBO) |
| EML for inbound | N/A | `GraphMessageToEmlConverter` (pure transformation, no I/O) |
| Attachment adapter | N/A | `GraphAttachmentAdapter` maps Graph → existing `EmailAttachmentInfo` |
| Association resolution | Manual (outbound only) | Automatic 3-level cascade for inbound via `IncomingAssociationResolver` |
| Mailbox verification | N/A | `MailboxVerificationService` tests send/read capabilities |
| Daily send limits | N/A | `DailySendCountResetService` + quota check before send |
| Telemetry | `EmailTelemetry` | `CommunicationTelemetry` (renamed, expanded metrics) |
| Legacy components | `EmailFilterService`, `EmailRuleSeedService`, `EmailPollingBackupService`, `EmailToDocumentJobHandler`, `BatchProcessEmailsJobHandler` | All deleted |

---

## Architecture Principles

1. **Graph Send is Critical Path**: If Graph `sendMail` fails, the entire operation fails with `SdapProblemException`. No partial success.
2. **Best-Effort Tracking**: Dataverse record creation, SPE archival, attachment records, and AI analysis are wrapped in try/catch. Failures are logged as warnings.
3. **No Retry Logic**: Failures are immediate. Callers (UI or AI playbook) handle retry decisions.
4. **Multi-Layer Deduplication**: Inbound emails are deduplicated at four levels: in-memory webhook cache (keyed by message ID, not subscription ID) → Service Bus `MessageId` set to `IdempotencyKey` (with SHA-256 hashing for keys >128 chars) → Dataverse `sprk_graphmessageid` query → Dataverse duplicate detection rule.
5. **Sender Validation Before Send**: The approved sender list is validated synchronously before any Graph call.
6. **Correlation ID Tracing**: Every operation is tagged with a `correlationId` for end-to-end tracing.

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Model-Driven App (UCI)                              │
│  ┌────────────────────────┐  ┌──────────────────┐  ┌────────────────────┐  │
│  │ Communication Form     │  │ Send Button       │  │ Account Admin Form │  │
│  │ sprk_communication     │  │ (Ribbon)          │  │ sprk_comm_account  │  │
│  └────────────────────────┘  └────────┬─────────┘  └────────────────────┘  │
└───────────────────────────────────────┼─────────────────────────────────────┘
                                        │ POST /api/communications/send
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          BFF API (.NET 8)                                    │
│                                                                             │
│  ┌─── CommunicationEndpoints.cs ──────────────────────────────────────────┐ │
│  │  POST /send             → CommunicationService.SendAsync()             │ │
│  │  POST /send-bulk        → CommunicationService (sequential, 100ms gap) │ │
│  │  GET  /{id}/status      → Dataverse lookup                            │ │
│  │  POST /accounts/{id}/verify → MailboxVerificationService.VerifyAsync() │ │
│  │  POST /incoming-webhook → Graph notification → enqueue job (AllowAnon) │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│  ┌─── Outbound ──────────────────────┐  ┌─── Inbound ───────────────────┐  │
│  │ CommunicationService (sealed)     │  │ IncomingCommunicationProcessor│  │
│  │ ├── SharedMailbox: ForApp()       │  │ ├── Dedup check               │  │
│  │ ├── User (OBO): ForUserAsync()    │  │ ├── Fetch Graph message       │  │
│  │ ├── Daily limit check             │  │ ├── Create sprk_communication │  │
│  │ ├── Send → Track → Archive        │  │ ├── Resolve associations      │  │
│  │ └── Enqueue AI analysis           │  │ ├── Process attachments       │  │
│  └───────┬──────┬────────┬───────────┘  │ ├── Archive .eml              │  │
│          │      │        │              │ └── Mark as read               │  │
│          ▼      ▼        ▼              └────────┬──────────────────────┘  │
│  ┌──────┐ ┌──────┐ ┌──────────┐                 │                         │
│  │Graph │ │Dv Svc│ │SpeFile   │                 ▼                         │
│  │Client│ │      │ │Store     │  ┌──────────────────────────────────┐     │
│  │Factry│ │      │ │(ADR-007) │  │ IncomingAssociationResolver     │     │
│  └──────┘ └──────┘ └──────────┘  │ Thread > Sender > Subject      │     │
│                                   │ (3-level cascade)              │     │
│  ┌─── Background Services ────┐  └──────────────────────────────────┘     │
│  │ GraphSubscriptionManager   │                                           │
│  │   (30-min cycle)           │  ┌──────────────────────────────────┐     │
│  │ InboundPollingBackupService│  │ EML Converters                   │     │
│  │   (5-min cycle)            │  │ ├── EmlGenerationService (outbd) │     │
│  │ DailySendCountResetService │  │ └── GraphMessageToEmlConverter   │     │
│  │   (midnight UTC)           │  │     (inbound, pure transform)    │     │
│  └────────────────────────────┘  └──────────────────────────────────┘     │
│                                                                           │
│  ┌─── Shared Services ────────┐  ┌──────────────────────────────────┐     │
│  │ CommunicationAccountService│  │ CommunicationTelemetry           │     │
│  │   (Redis-cached Dv queries)│  │ Meter: Sprk.Bff.Api.Communication│     │
│  │ ApprovedSenderValidator    │  └──────────────────────────────────┘     │
│  │ MailboxVerificationService │                                           │
│  │ GraphAttachmentAdapter     │                                           │
│  └────────────────────────────┘                                           │
└─────────────┬────────────┬──────────────┬─────────────────────────────────┘
              │            │              │
              ▼            ▼              ▼
         ┌──────┐    ┌───────┐     ┌──────────┐
         │MS    │    │Dv Web │     │SharePoint│
         │Graph │    │API    │     │Embedded  │
         │API   │    │       │     │(SPE)     │
         └──────┘    └───────┘     └──────────┘
```

---

## Component Inventory

### Backend Services

| File | Type | Purpose |
|------|------|---------|
| `Api/CommunicationEndpoints.cs` | Minimal API | Route definitions: `/send`, `/send-bulk`, `/{id}/status`, `/accounts/{id}/verify`, `/incoming-webhook` |
| `Api/Filters/CommunicationAuthorizationFilter.cs` | Endpoint Filter | Per-endpoint auth (ADR-008) |
| `Services/Communication/CommunicationService.cs` | Service | Core outbound send pipeline (shared + OBO modes) |
| `Services/Communication/CommunicationAccountService.cs` | Service | `sprk_communicationaccount` CRUD with Redis caching |
| `Services/Communication/ApprovedSenderValidator.cs` | Service | Two-tier sender resolution (config + Dataverse accounts) |
| `Services/Communication/IncomingCommunicationProcessor.cs` | Service | Inbound email processing pipeline |
| `Services/Communication/IncomingAssociationResolver.cs` | Service | 3-level association cascade for inbound |
| `Services/Communication/GraphMessageToEmlConverter.cs` | Service | Pure transformation: Graph Message → RFC 2822 .eml (inbound) |
| `Services/Communication/GraphAttachmentAdapter.cs` | Static | Maps Graph `FileAttachment` → `EmailAttachmentInfo` |
| `Services/Communication/EmlGenerationService.cs` | Service | RFC 2822 .eml generation from request data (outbound) |
| `Services/Communication/MailboxVerificationService.cs` | Service | Tests send/read capabilities for communication accounts |
| `Services/Communication/GraphSubscriptionManager.cs` | BackgroundService | Graph webhook subscription lifecycle (create/renew/recreate) |
| `Services/Communication/InboundPollingBackupService.cs` | BackgroundService | Backup polling for missed webhooks (5-min interval) |
| `Services/Communication/DailySendCountResetService.cs` | BackgroundService | Resets `sprk_sendstoday` at midnight UTC |
| `Telemetry/CommunicationTelemetry.cs` | Telemetry | OpenTelemetry metrics and distributed tracing |
| `Infrastructure/DI/CommunicationModule.cs` | DI Module | Feature module registering all communication services |
| `Configuration/CommunicationOptions.cs` | Options | `Communication` config section binding |

### Models

| File | Type | Purpose |
|------|------|---------|
| `Models/CommunicationAccount.cs` | Entity | Account with send/receive config, verification, quotas |
| `Models/SendMode.cs` | Enum | `SharedMailbox` (app-only) or `User` (OBO) |
| `Models/AccountType.cs` | Enum | `SharedAccount`, `ServiceAccount`, `UserAccount` |
| `Models/AuthMethod.cs` | Enum | `AppOnly` or `OnBehalfOf` (derived from AccountType) |
| `Models/SendCommunicationRequest.cs` | DTO | POST /send request body |
| `Models/SendCommunicationResponse.cs` | DTO | POST /send response body |
| `Models/BulkSendRequest.cs` | DTO | POST /send-bulk request body |
| `Models/BulkSendResponse.cs` | DTO | POST /send-bulk response body |
| `Models/CommunicationStatusResponse.cs` | DTO | GET /status response body |
| `Models/CommunicationAssociation.cs` | DTO | Entity association for regarding lookup |
| `Models/CommunicationType.cs` | Enum | Email, TeamsMessage, SMS, Notification |
| `Models/CommunicationStatus.cs` | Enum | Draft, Queued, Send, Delivered, Failed, Bounded, Recalled |
| `Models/CommunicationDirection.cs` | Enum | Incoming, Outgoing |
| `Models/BodyFormat.cs` | Enum | HTML, PlainText |

### Frontend (Dataverse UI)

| File | Type | Purpose |
|------|------|---------|
| `WebResources/sprk_communication_send.js` | Web Resource | Send button click handler, form validation, BFF API call |
| Communication Ribbon XML | RibbonDiffXml | Send button in form command bar |

---

## Outbound Send Pipeline

`CommunicationService.SendAsync()` supports two modes based on `SendMode`:

### SharedMailbox Mode (App-Only Auth)

```
Step 1: ValidateRequest
  ├── To[] required (≥1 recipient)
  ├── Subject required
  └── Body required
  ↓ (throws SdapProblemException on failure)

Step 1b: DownloadAndBuildAttachments (conditional)
  ├── Validate count ≤ 150
  ├── For each attachmentDocumentId:
  │   ├── Get metadata from SpeFileStore
  │   ├── Validate total size ≤ 35MB
  │   └── Download content as byte[]
  └── Build List<FileAttachment>
  ↓ (throws SdapProblemException on failure)

Step 2: Resolve Approved Sender
  ├── fromMailbox == null → resolve default sender
  └── fromMailbox != null → validate against approved list
  ↓ (throws SdapProblemException if invalid)

Step 2b: Check Daily Send Limit  ◄── NEW IN R2
  ├── Query CommunicationAccountService.GetSendAccountByEmailAsync()
  ├── If SendsToday >= DailySendLimit → throw DAILY_SEND_LIMIT_REACHED (HTTP 429)
  └── Skip if no DailySendLimit configured
  ↓

Step 3: Build Graph Message
  ├── Map Subject, Body (HTML or PlainText)
  ├── Map From (resolved sender)
  ├── Map To[], Cc[], Bcc[] recipients
  └── Attach FileAttachments (if any)
  ↓

Step 4: Send via Graph API  ◄── CRITICAL PATH
  ├── graphClient = GraphClientFactory.ForApp()
  ├── graphClient.Users[sender].SendMail.PostAsync()
  └── SaveToSentItems = true
  ↓ (throws SdapProblemException on ODataError)

Step 4b: Increment Send Count  ◄── BEST-EFFORT, NEW IN R2
  └── CommunicationAccountService.IncrementSendCountAsync()

Step 5: Create Dataverse Record  ◄── BEST-EFFORT
  ├── Build sprk_communication entity (Direction=Outgoing)
  ├── Map association fields (regarding lookup + denormalized)
  ├── Set status to Send (659490002)
  └── dataverseService.CreateAsync()
  ↓ (catch: log warning, continue)

Step 6: Archive to SPE  ◄── BEST-EFFORT
  ├── Check ArchiveOutgoingOptIn on account (default true)
  ├── Generate .eml via EmlGenerationService
  ├── Upload to SPE at /communications/{id}/{filename}.eml
  ├── Create sprk_document record
  ├── Enqueue AI analysis job (best-effort)
  └── Archive outbound attachments as child sprk_document records
  ↓ (catch: set ArchivalWarning, continue)

Step 7: Create Attachment Records  ◄── BEST-EFFORT
  ├── For each attachmentDocumentId:
  │   └── Create sprk_communicationattachment record
  └── Link to sprk_communication + sprk_document
  ↓ (catch: set AttachmentRecordWarning, continue)

Return SendCommunicationResponse
```

### User Mode (OBO Auth) — NEW IN R2

When `request.SendMode == SendMode.User`:

```
Step 1: Validate (same as shared)
Step 1b: Download attachments (same as shared)
Step 2: Resolve user identity from JWT claims
  ├── Email from 'email' or 'preferred_username' claim
  ├── OID from 'oid' claim → sprk_sentby
  └── Skip ApprovedSenderValidator (user sends as themselves)
Step 2b: Daily limit check (same as shared)
Step 3: Build Graph Message (same as shared)
Step 4: Send via OBO Graph client
  ├── graphClient = GraphClientFactory.ForUserAsync(httpContext)
  └── graphClient.Me.SendMail.PostAsync()
Steps 5-7: Same as shared (best-effort tracking/archival)
```

---

## Inbound Pipeline

### Webhook Flow

```
Microsoft Graph
  │ POST /api/communications/incoming-webhook
  ▼
CommunicationEndpoints.HandleIncomingWebhookAsync()
  │
  ├── 1. Handle Graph validation (echo validationToken)
  ├── 2. Parse GraphChangeNotificationCollection
  ├── 3. Validate clientState on each notification
  ├── 4. Deduplicate (in-memory ConcurrentDictionary, 10-min window)
  │       Key: msg:{messageId}:{changeType}
  │       (Keyed by message ID to catch duplicates from multiple subscriptions)
  ├── 5. Extract mailbox + messageId from resource path
  ├── 6. Enqueue IncomingCommunication job via ServiceBus
  │       IdempotencyKey: Communication:{messageId}:Process
  │       Service Bus MessageId = IdempotencyKey (SHA-256 hashed if >128 chars)
  │       Payload includes subscriptionId for GUID→email resolution
  └── 7. Return 202 Accepted
```

### Processing Pipeline

`IncomingCommunicationProcessor.ProcessAsync(mailboxEmail, graphMessageId, subscriptionId?, ct)`:

```
Step 1: Deduplication check
  └── Query sprk_communication by sprk_graphmessageid
  ↓ (skip if already exists)

Step 2: Resolve account from mailbox identifier
  ├── Direct email match against receive-enabled accounts
  ├── If GUID (shared mailbox): 3-tier resolution:
  │   ├── 1. Query Graph subscription → extract email from resource path
  │   ├── 2. Match by stored subscriptionId on Dataverse accounts
  │   └── 3. Single-account fallback (if only one receive-enabled)
  └── Check AutoCreateRecords flag
  ↓

Step 3: Fetch full message from Graph
  ├── GraphClientFactory.ForApp()
  ├── Users[email].Messages[messageId].GetAsync()
  ├── Select: id, internetMessageId, from, toRecipients, ccRecipients, subject,
  │           body, uniqueBody, receivedDateTime, hasAttachments
  └── Expand: attachments
  ↓

Step 4: Create sprk_communication record
  ├── Direction = Incoming (100000000)
  ├── CommunicationType = Email (100000000)
  ├── StatusCode = Delivered (659490003)
  ├── sprk_internetmessageid = message.InternetMessageId
  └── Prefer uniqueBody over full body (strips reply chains)
  ↓

Step 4.5: Resolve associations  ◄── BEST-EFFORT
  └── Delegate to IncomingAssociationResolver (3-level cascade)
  ↓

Step 5: Process attachments  ◄── BEST-EFFORT (if AutoCreateRecords=true or account unresolved)
  ├── GraphAttachmentAdapter.ToAttachmentInfoList()
  ├── Filter signature images via AttachmentFilterService
  ├── Upload to SPE: /communications/{id}/attachments/{fileName}
  ├── Create sprk_document record per attachment
  │   ├── sprk_documenttype = Email (100000006)
  │   ├── sprk_sourcetype = Email Attachment (659490004)
  │   └── sprk_filename = original filename (for AI file type detection)
  ├── Enqueue AI analysis job per attachment (best-effort)
  ├── Enqueue RAG indexing job per attachment (best-effort)
  └── Create sprk_communicationattachment records (linked to sprk_document)
  ↓

Step 6: Archive .eml to SPE  ◄── BEST-EFFORT (if ArchiveIncomingOptIn != false)
  ├── GraphMessageToEmlConverter.ConvertToEml(graphMessage)
  ├── Upload to SPE: /communications/{id}/{filename}.eml
  ├── Create sprk_document record
  │   ├── sprk_documenttype = Email (100000006)
  │   ├── sprk_sourcetype = Email Archive (659490003)
  │   └── sprk_filename = .eml filename (for AI file type detection)
  ├── Enqueue AI analysis job (best-effort)
  └── Enqueue RAG indexing job (best-effort)
  ↓

Step 7: Mark message as read in Graph  ◄── BEST-EFFORT
  └── PATCH isRead = true
```

---

## API Endpoints

### Route Group

```
/api/communications  (RequireAuthorization, Tag: "Communications")
```

| Method | Route | Auth | Handler | Description |
|--------|-------|------|---------|-------------|
| POST | `/send` | `CommunicationAuthorizationFilter` | `SendCommunicationAsync` | Single email send (shared or OBO) |
| POST | `/send-bulk` | `CommunicationAuthorizationFilter` | `SendBulkCommunicationAsync` | Bulk send (1-50, sequential, 100ms delay) |
| GET | `/{id:guid}/status` | Group auth | `GetCommunicationStatusAsync` | Status lookup from Dataverse |
| POST | `/accounts/{id:guid}/verify` | `CommunicationAuthorizationFilter` | `VerifyCommunicationAccountAsync` | Mailbox verification |
| POST | `/incoming-webhook` | `AllowAnonymous` (clientState validation) | `HandleIncomingWebhookAsync` | Graph change notification receiver |

### POST /api/communications/send

**Request Body** (`SendCommunicationRequest`):

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `to` | `string[]` | Yes | — | Recipient email addresses (≥1) |
| `cc` | `string[]` | No | null | CC recipients |
| `bcc` | `string[]` | No | null | BCC recipients |
| `subject` | `string` | Yes | — | Email subject line |
| `body` | `string` | Yes | — | Email body content |
| `bodyFormat` | `BodyFormat` | No | `HTML` | `HTML` or `PlainText` |
| `fromMailbox` | `string` | No | null | Sender mailbox (null = default) |
| `sendMode` | `SendMode` | No | `SharedMailbox` | `SharedMailbox` (app-only) or `User` (OBO) |
| `communicationType` | `CommunicationType` | No | `Email` | Channel type |
| `associations` | `CommunicationAssociation[]` | No | null | Entity associations |
| `correlationId` | `string` | No | auto-generated | Tracing correlation ID |
| `archiveToSpe` | `bool` | No | `false` | Archive .eml to SPE |
| `attachmentDocumentIds` | `string[]` | No | null | SPE document IDs to attach |

**Status Codes**: `200 OK`, `400 Bad Request`, `403 Forbidden`, `429 Too Many Requests` (daily limit), `500 Internal Server Error`

### POST /api/communications/send-bulk

Send the same email to multiple recipients (1-50). Each recipient gets their own `sprk_communication` record.

**Status Codes**: `200 OK` (all succeeded), `207 Multi-Status` (partial), `400`, `403`, `500`

### POST /api/communications/accounts/{id}/verify

Tests send and/or read capabilities for a communication account.

**Response** (`VerificationResult`):

| Field | Type | Description |
|-------|------|-------------|
| `accountId` | `Guid` | Account record ID |
| `emailAddress` | `string` | Mailbox email tested |
| `status` | `VerificationStatus` | `Verified` or `Failed` |
| `verifiedAt` | `DateTimeOffset` | Verification timestamp |
| `sendCapabilityVerified` | `bool` | Send test passed |
| `readCapabilityVerified` | `bool` | Read test passed |
| `failureReason` | `string?` | Error detail if failed |

### POST /api/communications/incoming-webhook

Graph change notification endpoint. Anonymous access with `clientState` validation.

Returns `200 OK` (validation echo) or `202 Accepted` (notification processed).

---

## Communication Accounts

### sprk_communicationaccount Entity

Centralized mailbox management replacing `appsettings.json`-only configuration.

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String | Display name |
| `sprk_emailaddress` | String | Mailbox email address |
| `sprk_displayname` | String | Sender display name |
| `sprk_accounttype` | OptionSet | SharedAccount (100000000), ServiceAccount (100000001), UserAccount (100000002) |
| `sprk_sendenabled` | Boolean | Can send email |
| `sprk_isdefaultsender` | Boolean | Default sender for shared mailbox sends |
| `sprk_receiveenabled` | Boolean | Inbound monitoring enabled |
| `sprk_monitorfolder` | String | Graph mail folder to monitor (default: "Inbox") |
| `sprk_autocreaterecords` | Boolean | Auto-create communication records for inbound |
| `sprk_archiveincomingoptin` | Boolean | Archive inbound .eml to SPE |
| `sprk_archiveoutgoingoptin` | Boolean | Archive outbound .eml to SPE |
| `sprk_subscriptionid` | String | Graph subscription ID (auto-managed) |
| `sprk_subscriptionexpiry` | DateTime | Subscription expiration (auto-managed) |
| `sprk_subscriptionstatus` | OptionSet | Active, Expired, Failed |
| `sprk_securitygroupid` | String | Exchange Application Access Policy group ID |
| `sprk_securitygroupname` | String | Security group display name |
| `sprk_verificationstatus` | OptionSet | NotVerified, Pending, Verified, Failed |
| `sprk_lastverified` | DateTime | Last verification timestamp |
| `sprk_verificationmessage` | String | Verification result message |
| `sprk_sendstoday` | Integer | Emails sent today (auto-incremented, reset at midnight UTC) |
| `sprk_dailysendlimit` | Integer | Max sends per day (null = unlimited) |

### CommunicationAccountService

Redis-cached Dataverse queries for account data.

| Method | Cache Key | TTL | Description |
|--------|-----------|-----|-------------|
| `QuerySendEnabledAccountsAsync()` | `comm:accounts:send-enabled` | 5 min | All active send-enabled accounts |
| `QueryReceiveEnabledAccountsAsync()` | `comm:accounts:receive-enabled` | 5 min | All active receive-enabled accounts |
| `GetDefaultSendAccountAsync()` | (uses send-enabled cache) | — | Account with `IsDefaultSender=true`, fallback to first |
| `GetSendAccountByEmailAsync(email)` | (uses send-enabled cache) | — | Case-insensitive email match |
| `IncrementSendCountAsync(accountId)` | (invalidates send-enabled cache) | — | Read + increment + update `sprk_sendstoday` |
| `ResetAllSendCountsAsync()` | (invalidates cache) | — | Bulk reset `sprk_sendstoday` to 0 for all accounts |

### Auth Method Derivation

| AccountType | AuthMethod | Graph Client |
|-------------|------------|--------------|
| SharedAccount | AppOnly | `GraphClientFactory.ForApp()` |
| ServiceAccount | AppOnly | `GraphClientFactory.ForApp()` |
| UserAccount | OnBehalfOf | `GraphClientFactory.ForUserAsync(httpContext)` |

---

## Sender Resolution

The `ApprovedSenderValidator` uses a two-tier model:

### Tier 1: Configuration (Synchronous)

```json
{
  "Communication": {
    "ApprovedSenders": [
      { "Email": "noreply@spaarke.com", "DisplayName": "Spaarke Notifications", "IsDefault": true }
    ],
    "DefaultMailbox": "noreply@spaarke.com"
  }
}
```

### Tier 2: Dataverse Accounts + Redis Cache

1. `CommunicationAccountService.QuerySendEnabledAccountsAsync()` (Redis-cached, 5-min TTL)
2. Merge: config senders as base, Dataverse accounts overlay (Dataverse wins on email match)

### Resolution Priority

| `fromMailbox` | Behavior |
|---------------|----------|
| `null` | Return sender with `IsDefault=true`, else `DefaultMailbox` match, else first sender |
| `"legal@spaarke.com"` | Match against approved list (case-insensitive). If not found → `INVALID_SENDER` |

---

## Inbound Association Resolution

`IncomingAssociationResolver` uses a 3-level priority cascade. First match wins.

### Level 1: Thread Matching

- Fetches `In-Reply-To` header from Graph internet message headers
- Searches `sprk_internetmessageid` first (exact RFC 2822 match), falls back to `sprk_graphmessageid`
- Copies regarding fields from parent record

### Level 2: Sender Matching

- Queries `contact` by sender email address
- Queries `account` by sender email domain (skips common providers: gmail.com, outlook.com, etc.)

### Level 3: Subject Pattern Matching

Applies 4 regex patterns against subject line:

| Pattern | Entity Lookup |
|---------|---------------|
| `MAT-(\d+)` | `sprk_matter` by `sprk_referencenumber` |
| `Matter\s*#(\d+)` | `sprk_matter` by `sprk_referencenumber` |
| `SPRK-(\d+)` | `sprk_matter` by `sprk_referencenumber` |
| `[MATTER:(\d+)]` | `sprk_matter` by `sprk_referencenumber` |

### No Default-Matter Fallback

Shared mailboxes are not matter-specific, so there is no automatic default-matter assignment.
Unassociated emails remain unassociated and surface for manual review via Pending Review status.

### Association Status

| Status | Value | Set When |
|--------|-------|----------|
| Resolved | 100000000 | Match found at any level |
| Pending Review | 100000001 | No match found at any level |

---

## Attachment Handling

### Outbound Limits

| Limit | Value | Enforced At |
|-------|-------|-------------|
| Max attachment count | 150 | `DownloadAndBuildAttachmentsAsync` |
| Max total size | 35 MB (36,700,160 bytes) | Cumulative check during download |

### Inbound Attachment Processing

```
Graph Message.Attachments[]
  │
  ▼
GraphAttachmentAdapter.ToAttachmentInfoList()
  → Filters to FileAttachment only, excludes empty content
  → Maps: FileName, MimeType, Content (MemoryStream), SizeBytes, IsInline, ContentId
  │
  ▼
AttachmentFilterService.ShouldFilterAttachment()
  → Filters signature images (small inline images)
  │
  ▼
Upload to SPE: /communications/{id}/attachments/{fileName}
  │
  ▼
Create sprk_communicationattachment records in Dataverse
```

---

## EML Archival

### Outbound (EmlGenerationService)

- Builds .eml from `SendCommunicationRequest` data
- Uses **MimeKit** for RFC 2822 compliance
- Supports HTML/PlainText body formats and multipart/mixed for attachments

### Inbound (GraphMessageToEmlConverter) — NEW IN R2

- **Pure transformation** — no I/O, no Dataverse calls, no constructor dependencies
- Converts Microsoft Graph `Message` (with expanded attachments) directly to .eml
- Preserves `InternetMessageId`, `In-Reply-To`, `References` headers for thread continuity
- Handles three attachment layouts:
  - Only inline → `multipart/related`
  - Both inline and regular → `multipart/mixed` wrapping `multipart/related`
  - Only regular → `multipart/mixed`

### Storage Path

```
/communications/{communicationId:N}/{sanitized_subject}_{timestamp}.eml
```

### Dataverse Linkage

After upload, creates a `sprk_document` record:
- `sprk_documentname` = "Archived: {subject}" (outbound) or "{filename}" (attachment)
- `sprk_filename` = .eml filename or original attachment filename (used by AI analyzer for file type detection)
- `sprk_documenttype` = Email (100000006)
- `sprk_sourcetype` = Email Archive (659490003) for .eml, Email Attachment (659490004) for attachments
- `sprk_communication` = EntityReference to the communication record
- `sprk_graphitemid` / `sprk_graphdriveid` = SPE file identifiers
- `sprk_isemailarchive` = true (for .eml files)
- `sprk_emailsubject`, `sprk_emailfrom`, `sprk_emailto`, `sprk_emaildate`, `sprk_emaildirection` = email metadata

---

## Background Services

Three `BackgroundService` implementations following ADR-001.

### Startup Resilience Pattern (All Services)

All communication BackgroundServices follow the same startup pattern:

```
ExecuteAsync(CancellationToken stoppingToken)
  1. Log startup message
  2. await Task.Delay(startup_delay, stoppingToken)  ◄── Dependency warm-up
  3. try { await InitialCycleAsync(stoppingToken); }  ◄── Wrapped in try-catch
     catch (OperationCanceledException) → return
     catch (Exception) → log error, continue to loop
  4. while (timer.WaitForNextTickAsync()) { ... }     ◄── Standard loop with try-catch
```

**Why**: If the initial cycle throws (e.g., Dataverse/Graph not ready during app startup), an unhandled exception propagates from `ExecuteAsync` and .NET 8's default `BackgroundServiceExceptionBehavior.StopHost` stops the host. Wrapping the initial call in try-catch ensures the service retries on the next timer tick instead of dying silently.

### GraphSubscriptionManager

| Setting | Value |
|---------|-------|
| Tick interval | 30 minutes |
| Startup delay | 10 seconds |
| Renewal threshold | 24 hours before expiry |
| Subscription lifetime | 3 days (Graph maximum for mail) |

**Each cycle executes**:

1. **Orphan cleanup** — Lists all Graph subscriptions, compares with Dataverse-managed IDs, deletes untracked orphans whose `NotificationUrl` matches the configured webhook URL (safety filter to avoid touching subscriptions managed by other applications)
2. **Per-account lifecycle** — For each receive-enabled account:

| Condition | Action |
|-----------|--------|
| No SubscriptionId | CREATE new subscription |
| Expiry < 24h from now | RENEW (PATCH expiration) |
| Renewal fails (404/error) | DELETE old + CREATE new |
| Otherwise | SKIP (healthy) |

Subscription resource: `users/{email}/mailFolders/{monitorFolder}/messages` with `changeType=created`.

**Orphan cleanup** prevents duplicate webhook notifications caused by accumulated subscriptions from deployments or multi-instance race conditions. This was the root cause of duplicate email processing discovered in March 2026.

### InboundPollingBackupService

| Setting | Value |
|---------|-------|
| Polling interval | 5 minutes |
| Startup delay | 15 seconds |
| Initial lookback window | 15 minutes (on startup/restart) |
| Max messages per poll | 50 |

Queries `isRead eq false` messages filtered by `receivedDateTime`. Enqueues `IncomingCommunication` jobs with idempotency key `Communication:{messageId}:Process`.

### DailySendCountResetService

- Fires at midnight UTC daily
- Calls `CommunicationAccountService.ResetAllSendCountsAsync()`
- On error, retries after 1-minute delay (avoids tight error loops)
- `CalculateDelayUntilMidnightUtc()` is `internal static` for testability

---

## Dataverse Entity Schema

### sprk_communication

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String(200) | Auto-generated: "Email: {subject}" |
| `sprk_communicationtype` | OptionSet | Email (100000000), TeamsMessage, SMS, Notification |
| `statuscode` | OptionSet | Draft(1), Queued(659490001), Send(659490002), Delivered(659490003), Failed(659490004), Bounded(659490005), Recalled(659490006) |
| `statecode` | OptionSet | Active(0), Inactive(1) |
| `sprk_direction` | OptionSet | Incoming(100000000), Outgoing(100000001) |
| `sprk_bodyformat` | OptionSet | HTML(0), PlainText(1) |
| `sprk_to` | String | Semicolon-delimited recipient list |
| `sprk_cc` | String | Semicolon-delimited CC list |
| `sprk_bcc` | String | Semicolon-delimited BCC list |
| `sprk_from` | String | Sender email address |
| `sprk_subject` | String | Email subject |
| `sprk_body` | Multiline | Email body content |
| `sprk_graphmessageid` | String | Graph message ID (used for deduplication) |
| `sprk_internetmessageid` | String | RFC 2822 Internet Message-ID (for thread matching via In-Reply-To) |
| `sprk_sentat` | DateTime | Send/receive timestamp |
| `sprk_correlationid` | String | Tracing correlation ID |
| `sprk_sentby` | Lookup (systemuser) | Resolved from Azure AD OID via `QuerySystemUserByAzureAdOidAsync` |
| `sprk_hasattachments` | Boolean | Whether attachments were included |
| `sprk_attachmentcount` | Integer | Number of attachments |
| `sprk_associationcount` | Integer | Number of entity associations |
| `sprk_associationstatus` | OptionSet | Resolved(100000000), PendingReview(100000001) |
| `sprk_regardingrecordname` | String(100) | Denormalized: primary association name |
| `sprk_regardingrecordid` | String(100) | Denormalized: primary association ID |
| `sprk_regardingrecordurl` | String(200) | Denormalized: primary association URL |
| `sprk_regarding{entity}` | Lookup | 8 entity-specific lookup fields |

### sprk_communicationattachment

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String(200) | File display name |
| `sprk_communication` | Lookup | Parent communication record |
| `sprk_document` | Lookup | SPE document record |
| `sprk_attachmenttype` | OptionSet | File(100000000) |

---

## Configuration

### appsettings.json

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
    "DefaultMailbox": "noreply@spaarke.com",
    "ArchiveContainerId": "{spe-container-drive-id}",
    "WebhookNotificationUrl": "https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/incoming-webhook",
    "WebhookClientState": "{secret-value}"
  }
}
```

### CommunicationOptions

| Property | Type | Description |
|----------|------|-------------|
| `ApprovedSenders` | `ApprovedSenderConfig[]` | Config-based approved senders (required, min 1) |
| `DefaultMailbox` | `string?` | Fallback sender when no default configured |
| `ArchiveContainerId` | `string?` | SPE container/drive ID for .eml archival |

---

## DI Registration

`CommunicationModule.AddCommunicationModule()` registers all services per ADR-010:

```csharp
// Options
services.Configure<CommunicationOptions>(config.GetSection("Communication"));

// Singletons
services.AddSingleton<CommunicationAccountService>();
services.AddSingleton<ApprovedSenderValidator>();
services.AddSingleton<CommunicationService>();
services.AddSingleton<EmlGenerationService>();
services.AddSingleton<GraphMessageToEmlConverter>();
services.AddSingleton<MailboxVerificationService>();
services.AddSingleton<IncomingAssociationResolver>();
services.AddSingleton<IncomingCommunicationProcessor>();
services.AddSingleton<SendCommunicationToolHandler>();

// Scoped (job handler)
services.AddScoped<IJobHandler, IncomingCommunicationJobHandler>();

// Background Services (ADR-001)
services.AddHostedService<GraphSubscriptionManager>();
services.AddHostedService<InboundPollingBackupService>();
services.AddHostedService<DailySendCountResetService>();
```

---

## Telemetry

### CommunicationTelemetry

Meter: `Sprk.Bff.Api.Communication` (v1.0.0)

| Category | Counters | Histograms |
|----------|----------|------------|
| Conversion | `communication.conversion.requests`, `.successes`, `.failures` | `communication.conversion.duration` (ms) |
| Webhook | `communication.webhook.received`, `.enqueued`, `.rejected` | `communication.webhook.duration` (ms) |
| Polling | `communication.polling.runs`, `.emails_found`, `.emails_enqueued` | — |
| Filter | `communication.filter.evaluations`, `.matched`, `.default_action` | — |
| Job Processing | `communication.job.processed`, `.succeeded`, `.failed`, `.skipped_duplicate` | `communication.job.duration` (ms) |
| Files | `communication.attachments.processed` | `communication.eml.file_size` (bytes) |
| AI Jobs | `communication.ai_job.enqueued`, `.enqueue_failures` | — |
| RAG Jobs | `communication.rag_job.enqueued`, `.enqueue_failures`, `.skipped` | — |
| DLQ | `communication.dlq.list_operations`, `.redrive_attempts`, `.redrive_successes`, `.redrive_failures` | — |

**Dimensions**: `communication.trigger` (manual/webhook/polling), `communication.status`, `communication.error_code`, `communication.filter.action`, `communication.document_type`, `communication.skip_reason`

---

## Error Handling

All errors use `SdapProblemException` which produces RFC 7807 ProblemDetails responses (ADR-019).

### Error Codes

| Code | HTTP | Scenario |
|------|------|----------|
| `VALIDATION_ERROR` | 400 | Missing To, Subject, or Body |
| `INVALID_SENDER` | 400 | Sender not in approved list |
| `NO_DEFAULT_SENDER` | 400 | No sender configured |
| `DAILY_SEND_LIMIT_REACHED` | 429 | `SendsToday >= DailySendLimit` |
| `ATTACHMENT_LIMIT_EXCEEDED` | 400 | >150 attachments or >35MB total |
| `ATTACHMENT_NOT_FOUND` | 404 | SPE document not found |
| `ATTACHMENT_DOWNLOAD_FAILED` | 502 | Failed to download from SPE |
| `ATTACHMENT_CONFIG_ERROR` | 500 | ArchiveContainerId not configured |
| `GRAPH_SEND_FAILED` | 502/500 | Graph sendMail API error |
| `COMMUNICATION_NOT_FOUND` | 404 | Status lookup — record not found |
| `COMMUNICATION_NOT_AUTHORIZED` | 403 | User lacks valid identity |

### ProblemDetails Extensions

All error responses include:
- `correlationId`: For log tracing
- `graphErrorCode`: (Graph errors only) Original Graph error code
- `failedDocumentId` / `attachmentIndex`: (Attachment errors) Which attachment failed

---

## Security

### Authentication

- All endpoints require authentication via `RequireAuthorization()` except `/incoming-webhook` (anonymous with clientState validation)
- `CommunicationAuthorizationFilter` validates:
  - `user.Identity.IsAuthenticated == true`
  - Valid `oid` or `NameIdentifier` claim present
- Graph API calls: App-only via `GraphClientFactory.ForApp()` or OBO via `GraphClientFactory.ForUserAsync()`

### Webhook Security

- `clientState` from config is set on subscription creation and validated on each notification
- In-memory deduplication prevents replay (10-minute window)
- Webhook does not process messages directly — enqueues to ServiceBus for isolated processing

### Sender Controls

- SharedMailbox mode: Only mailboxes in the approved senders list can be used as `From`
- User mode: Sender is derived from JWT claims (no spoofing possible)
- All sender resolution is case-insensitive

### Exchange Application Access Policy

- Restricts which mailboxes the app registration can access
- Configured via security groups in Azure AD
- `sprk_securitygroupid` / `sprk_securitygroupname` on communication account track the policy group

### Data Protection

- Email body content is stored in Dataverse (subject to Dataverse RBAC)
- BCC recipients are stored on the Dataverse record (not exposed in form views by default)
- Archived .eml files in SPE inherit container-level permissions

---

## UI Integration

### Send Button (Ribbon)

The Send button is added to the `sprk_communication` main form command bar via RibbonDiffXml:

| Element | ID | Description |
|---------|----|-------------|
| CustomAction | `sprk.communication.send.CustomAction` | Places button in Actions group |
| Button | `sprk.communication.send.Button` | Visible button with `ModernImage="Send"` |
| Command | `sprk.communication.send.Command` | Links button to JavaScript handler |
| EnableRule | `sprk.communication.isStatusDraft.EnableRule` | Enabled only when `statuscode=1` (Draft) |

### Web Resource: sprk_communication_send.js

| Function | Purpose |
|----------|---------|
| `isStatusDraft(formContext)` | Enable rule: returns `true` only when Draft |
| `sendCommunication(executionContext)` | Button handler: validate → collect → send → update |
| `_buildRequest(formContext)` | Collects form fields into SendCommunicationRequest DTO |
| `_collectAssociations(formContext)` | Reads 8 regarding lookup fields into associations[] |
| `_sendRequest(formContext, request)` | POST to BFF with auth token |
| `_handleSuccess(formContext, response)` | Update status to Send, save, show notification |
| `_handleError(formContext, problemDetails)` | Parse ProblemDetails, show error notification |

### BFF URL Resolution

The web resource resolves the BFF API base URL in this order:
1. Dataverse environment variable `sprk_BffApiBaseUrl`
2. Hardcoded default: `https://spe-api-dev-67e2xz.azurewebsites.net`

---

## AI Playbook Integration

The `SendCommunicationToolHandler` implements `IAiToolHandler` (ADR-013) to allow AI playbooks to send communications programmatically.

### Tool Registration

Registered explicitly in `CommunicationModule` as singleton (not auto-discovered).

### Tool Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `to` | `string` | Yes | Comma-separated recipient emails |
| `cc` | `string` | No | Comma-separated CC recipient emails |
| `subject` | `string` | Yes | Email subject |
| `body` | `string` | Yes | Email body (HTML) |
| `fromMailbox` | `string` | No | Sender mailbox (null = default shared mailbox) |
| `regardingEntity` | `string` | No | Dataverse entity logical name for primary association (e.g., `sprk_matter`) |
| `regardingId` | `string` | No | Dataverse record GUID for primary association |
| `associations` | `object[]` | No | Additional entity associations |

### How AI Playbooks Send Email

The AI tool framework invokes `SendCommunicationToolHandler.HandleAsync()` which:
1. Parses tool parameters into a `SendCommunicationRequest`
2. Delegates to `CommunicationService.SendAsync()` (same pipeline as UI sends)
3. Returns a structured result with `communicationId`, `status`, and any warnings

This ensures AI-sent emails go through the same validation, sender resolution, archival, and tracking as UI-initiated sends.

---

## ADR Compliance

| ADR | Compliance | Implementation |
|-----|-----------|----------------|
| ADR-001 | ✅ | Minimal API endpoints + 3 BackgroundServices (GraphSubscriptionManager, InboundPollingBackupService, DailySendCountResetService) |
| ADR-007 | ✅ | `SpeFileStore` facade for all SPE operations (attachment download/upload, .eml upload) |
| ADR-008 | ✅ | `CommunicationAuthorizationFilter` as endpoint filter, not global middleware |
| ADR-010 | ✅ | All services registered via `CommunicationModule`; concrete types, singletons |
| ADR-013 | ✅ | `SendCommunicationToolHandler` extends BFF via `IAiToolHandler` |
| ADR-019 | ✅ | `SdapProblemException` for all error responses with ProblemDetails format |

---

*Architecture document for the Communication Service (R2). See also: [Admin Guide](../guides/COMMUNICATION-ADMIN-GUIDE.md) | [Deployment Guide](../guides/COMMUNICATION-DEPLOYMENT-GUIDE.md)*
