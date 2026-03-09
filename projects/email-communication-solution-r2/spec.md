# Email Communication Solution R2 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-09
> **Source**: design-communication-accounts.md
> **Guiding Principle**: Build with ability to deploy in different environments/tenants

## Executive Summary

Replace the fragmented email infrastructure (appsettings.json-only approved senders, Server-Side Sync-dependent email-to-document pipeline) with a unified Communication Service built on the `sprk_communicationaccount` Dataverse entity and Microsoft Graph API. Delivers outbound shared/individual sending, inbound shared mailbox monitoring via Graph subscriptions, and a fully Graph-based email-to-document archival pipeline — retiring Server-Side Sync entirely. Pre-launch project with no backward compatibility requirements.

## Scope

### In Scope

- **Phase A**: Communication Account Entity + outbound config migration from `appsettings.json` to Dataverse
- **Phase B**: Individual user outbound sending via OBO auth (`SendMode.User`)
- **Phase C**: Inbound shared mailbox monitoring via Graph subscriptions (webhook + backup polling)
- **Phase D**: Mailbox verification endpoint + admin UX enhancements
- **Phase E**: Email-to-document pipeline migration from Dataverse email activities to Graph API
- Retirement of Server-Side Sync, `sprk_approvedsender`, `sprk_emailprocessingrule`, and legacy email processing components
- Multi-tenant deployment readiness for all components

### Out of Scope

- Individual user inbound monitoring (per-user consent, refresh token management, privacy controls)
- Automated Exchange group management (manual process for now, automation candidate for Phase D)
- Email templates with merge fields
- Email scheduling (delayed send)
- Conversation threading UI
- Migration of existing email-linked documents (pre-launch; orphaned data cleaned manually)
- Backward compatibility with Server-Side Sync (full cutover)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Communication/` — CommunicationService, ApprovedSenderValidator, EmlGenerationService
- `src/server/api/Sprk.Bff.Api/Services/Jobs/` — ServiceBusJobProcessor, new job handlers
- `src/server/api/Sprk.Bff.Api/Services/Email/` — EmailAttachmentProcessor, AttachmentFilterService (reuse); EmailToEmlConverter, EmailFilterService (retire)
- `src/server/api/Sprk.Bff.Api/Api/` — CommunicationEndpoints, EmailEndpoints (retire webhook routes)
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` — GraphClientFactory (reuse)
- `src/server/api/Sprk.Bff.Api/Configuration/` — EmailProcessingOptions → CommunicationOptions consolidation
- `src/solutions/` — Dataverse solution updates (entity, forms, views)
- `src/client/` — Web resources using `@spaarke/auth` for BFF API calls

## Requirements

### Functional Requirements

1. **FR-01**: Admin manages communication accounts (add/remove/configure mailboxes) entirely through Dataverse UI — no code deployments needed
   - Acceptance: Create, edit, deactivate `sprk_communicationaccount` records via model-driven form; BFF reads config at runtime

2. **FR-02**: Outbound shared mailbox sending via `sprk_communicationaccount` entity
   - Acceptance: Send email from `mailbox-central@spaarke.com` → email received → `sprk_communication` record created → config sourced from Dataverse entity

3. **FR-03**: `appsettings.json` ApprovedSenders retained as bootstrap/fallback until Dataverse entity is populated
   - Acceptance: BFF starts and can send email even before any `sprk_communicationaccount` records exist; once records exist, Dataverse takes precedence

4. **FR-04**: Individual user outbound sending via OBO auth
   - Acceptance: User selects "Send as me" → email sent from user's own mailbox via Graph `/me/sendMail` → `sprk_communication` record tracks sender

5. **FR-05**: Inbound shared mailbox monitoring via Graph subscriptions
   - Acceptance: Email sent TO `mailbox-central@spaarke.com` → Graph webhook fires → `sprk_communication` record auto-created with Direction = Incoming within 60 seconds

6. **FR-06**: Graph subscription lifecycle management
   - Acceptance: `GraphSubscriptionManager` BackgroundService creates subscriptions for receive-enabled accounts; renews when < 24h remaining (max 3-day lifetime); recreates on failure

7. **FR-07**: Backup polling for missed webhooks
   - Acceptance: If webhook fails, `CommunicationPollingBackupService` catches missed messages within 5 minutes via Graph query

8. **FR-08**: Association resolution for incoming email
   - Acceptance: Incoming email auto-linked to entities via priority cascade: (1) email thread In-Reply-To, (2) sender contact/account match, (3) subject line pattern match, (4) mailbox context

9. **FR-09**: Mailbox verification endpoint
   - Acceptance: `POST /api/communications/accounts/{id}/verify` → tests send and/or read access → updates `sprk_verificationstatus` and `sprk_lastverified`

10. **FR-10**: Inbound email-to-document archival via Graph API
    - Acceptance: Incoming email → `.eml` archived to SPE → `sprk_document` with `sprk_communication` lookup → child attachment documents → AI analysis enqueued

11. **FR-11**: Outbound email-to-document archival enhancement
    - Acceptance: Sent email → `.eml` archived to SPE → `sprk_document` linked to `sprk_communication` → child attachment documents created → AI analysis enqueued

12. **FR-12**: EML archival default ON for both directions with opt-out configuration
    - Acceptance: Both `sprk_ArchiveOutgoingOptIn` and `sprk_ArchiveIncomingOptIn` default to Yes on `sprk_communicationaccount`; toggling to No skips archival for that direction

13. **FR-13**: Server-Side Sync full retirement
    - Acceptance: All legacy components deleted; no Dataverse email activities created for communication purposes; all email processing through Graph API

14. **FR-14**: Deduplication for incoming messages
    - Acceptance: Same message received via webhook AND polling → only one `sprk_communication` record created; uses Redis idempotency key + `sprk_graphmessageid` uniqueness

### Non-Functional Requirements

- **NFR-01**: Graph webhook response time < 3 seconds (Graph requires < 3s for subscription notifications)
- **NFR-02**: Email processing (webhook → communication record) < 30 seconds end-to-end
- **NFR-03**: Backup polling interval ≤ 5 minutes
- **NFR-04**: Redis idempotency keys retained 7 days; processing locks 5 minutes
- **NFR-05**: All components must support multi-tenant deployment (no hardcoded tenant IDs, mailbox addresses, or environment-specific config)
- **NFR-06**: Document archival is best-effort (non-fatal) — communication record creation is the critical path
- **NFR-07**: Individual attachment failures isolated — other attachments continue processing

## Technical Constraints

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| **ADR-001** | Minimal API + BackgroundService | All endpoints + GraphSubscriptionManager + polling service |
| **ADR-002** | Thin Plugins | Any Dataverse plugin validation on sprk_communicationaccount |
| **ADR-003** | Lean Authorization Seams | Authorization on send, read, account management endpoints |
| **ADR-004** | Async Job Contract | ProcessIncomingCommunication job type on sdap-jobs queue |
| **ADR-005** | Flat Storage in SPE | .eml and attachment storage in SPE containers |
| **ADR-007** | SpeFileStore Facade | All SPE operations through facade |
| **ADR-008** | Endpoint Filters | Authorization filters on communication endpoints |
| **ADR-009** | Redis-First Caching | Idempotency keys, processing locks, approved sender cache |
| **ADR-010** | DI Minimalism | Feature module registration (AddCommunicationModule) |
| **ADR-017** | Async Job Status | Job status persistence for incoming email processing |
| **ADR-018** | Feature Flags | Kill switches for email send/receive features |
| **ADR-019** | ProblemDetails | Error responses on all communication endpoints |
| **ADR-020** | Versioning Strategy | Job contract evolution, API evolution |
| **ADR-006** | PCF / Code Pages | Admin UI for communication account management |
| **ADR-012** | Shared Component Library | @spaarke/ui-components for any admin UI |
| **ADR-021** | Fluent UI v9 | All UI components |
| **ADR-022** | PCF Platform Libraries | React 16 APIs for field-bound PCF controls |

### MUST Rules (from ADRs)

- ✅ MUST use Minimal API for all HTTP endpoints (ADR-001)
- ✅ MUST use BackgroundService + Service Bus for async email processing (ADR-001)
- ✅ MUST keep Dataverse plugins < 200 LoC, < 50ms, no HTTP calls (ADR-002)
- ✅ MUST implement authorization as `IAuthorizationRule` (ADR-003)
- ✅ MUST use Job Contract schema for ProcessIncomingCommunication (ADR-004)
- ✅ MUST implement job handlers as idempotent (ADR-004)
- ✅ MUST use deterministic IdempotencyKey: `Communication:{graphMessageId}:Process` (ADR-004)
- ✅ MUST store documents flat in SPE containers (ADR-005)
- ✅ MUST route all SPE operations through `SpeFileStore` (ADR-007)
- ✅ MUST use endpoint filters for resource authorization (ADR-008)
- ✅ MUST use `IDistributedCache` (Redis) for cross-request caching (ADR-009)
- ✅ MUST register concretes by default, use feature module extensions (ADR-010)
- ✅ MUST return ProblemDetails for all HTTP failures (ADR-019)
- ✅ MUST use `@spaarke/auth` for all client-side authentication
- ❌ MUST NOT use Azure Functions (ADR-001)
- ❌ MUST NOT make HTTP/Graph calls from Dataverse plugins (ADR-002)
- ❌ MUST NOT inject GraphServiceClient outside SpeFileStore/GraphClientFactory (ADR-007)
- ❌ MUST NOT use global middleware for resource authorization (ADR-008)
- ❌ MUST NOT cache authorization decisions (ADR-009)
- ❌ MUST NOT hardcode tenant IDs, environment URLs, or mailbox addresses (multi-tenant principle)

### Existing Patterns to Follow

- Outbound send pipeline: `CommunicationService.SendAsync()` 7-step pattern
- Job processing: `ServiceBusJobProcessor` + `IJobHandler` pattern
- Graph client creation: `GraphClientFactory.ForApp()` / `ForUserAsync()`
- Approved sender validation: `ApprovedSenderValidator` merge pattern (appsettings + Dataverse)
- EML generation: `EmlGenerationService` with MimeKit
- Attachment processing: `EmailAttachmentProcessor` + `AttachmentFilterService`
- Redis idempotency: `Email:{id}:Archive` key pattern → `Communication:{id}:Process`
- Backup polling: `EmailPollingBackupService` timer pattern

## Implementation Approach

### Phase A: Communication Account Entity + Outbound Config (Foundation)

**Blocks all other phases.**

1. Entity already deployed in Dataverse (`sprk_communicationaccount`)
2. Create admin form and views for account management
3. Seed `mailbox-central@spaarke.com` as default send-enabled account
4. Update `ApprovedSenderValidator` → query `sprk_communicationaccount` (rename query method)
5. Update `IDataverseService.QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync`
6. Retain `appsettings.json` as bootstrap/fallback
7. Configure Exchange application access policy
8. Deploy + test outbound send end-to-end

### Phase B: Individual User Outbound (Parallel with C)

**Depends on A.**

1. Add `SendMode` enum (`SharedMailbox`, `User`) to `SendCommunicationRequest`
2. Branch `CommunicationService.SendAsync()` for OBO path
3. Resolve user email from OBO token claims
4. Update web resource with send mode selection UI
5. Update Communication form with send mode choice
6. Test both modes end-to-end

### Phase C: Inbound Shared Mailbox Monitoring (Parallel with B)

**Depends on A.**

1. Add `Mail.Read` application permission to app registration
2. Create `GraphSubscriptionManager` BackgroundService (create/renew/recreate subscriptions)
3. Create `POST /api/communications/incoming-webhook` endpoint (validate + enqueue)
4. Create `IncomingCommunicationProcessor` job handler
5. Implement association resolution (thread → sender → subject → mailbox context)
6. Create `CommunicationPollingBackupService` (5-minute interval)
7. Create incoming communication views in Dataverse
8. Test end-to-end: send email → webhook → communication record

### Phase D: Verification & Admin UX

**Depends on A, B, C.**

1. Create `POST /api/communications/accounts/{id}/verify` endpoint
2. Implement verification logic (test send + test read)
3. Update form with verification status display
4. Add daily send count tracking + reset
5. Admin documentation

### Phase E: Email-to-Document Migration (Graph-Based Archival)

**Depends on C (inbound pipeline must exist).**

1. Create `GraphMessageToEmlConverter` (Graph Message → RFC 2822 .eml via MimeKit)
2. Create `GraphAttachmentAdapter` (Graph `FileAttachment` → `EmailAttachmentInfo`)
3. Add `sprk_communication` lookup to `sprk_document` entity
4. Remove `sprk_email` lookup from `sprk_document` entity
5. Integrate document archival into `IncomingCommunicationProcessor`
6. Enhance `CommunicationService.SendAsync()` outbound archival (link to communication, child docs, AI analysis)
7. Delete retired components (see retirement list below)
8. Delete `sprk_emailprocessingrule`, `sprk_approvedsender` entities
9. Disable Server-Side Sync
10. Rename `EmailTelemetry` → `CommunicationTelemetry`
11. Consolidate `EmailProcessingOptions` → `CommunicationOptions`
12. Test inbound + outbound archival end-to-end

### Phase Dependency Graph

```
Phase A (foundation)
    ├──→ Phase B (parallel with C)
    ├──→ Phase C (parallel with B)
    │        └──→ Phase E (depends on C)
    └──→ Phase D (depends on A-C)

Execution order: A → B+C (parallel) → E → D
```

## Component Inventory

### New Components

| Component | Type | Purpose |
|-----------|------|---------|
| `GraphSubscriptionManager` | BackgroundService | Create/renew Graph subscriptions for receive-enabled accounts |
| `IncomingCommunicationProcessor` | IJobHandler | Process incoming emails → communication records + document archival |
| `CommunicationWebhookEndpoint` | Minimal API Endpoint | Receive Graph subscription notifications |
| `GraphMessageToEmlConverter` | Service | Convert Graph Message → RFC 2822 .eml via MimeKit |
| `CommunicationPollingBackupService` | BackgroundService | Query Graph for missed messages (15-min interval) |
| `GraphAttachmentAdapter` | Static mapper | Map Graph FileAttachment → EmailAttachmentInfo |
| Mailbox verification endpoint | Minimal API Endpoint | Test connectivity and permissions |

### Modified Components

| Component | Change |
|-----------|--------|
| `ApprovedSenderValidator` | Query `sprk_communicationaccount` instead of `sprk_approvedsender` |
| `CommunicationService.SendAsync()` | Add OBO code path, enhanced archival (sprk_communication link, child docs, AI analysis) |
| `IDataverseService` | Rename `QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync` |
| `EmailAttachmentProcessor` | Accept Graph message attachments via `GraphAttachmentAdapter` |
| `EmailTelemetry` | Rename to `CommunicationTelemetry`, update metric names |
| `EmailProcessingOptions` | Consolidate into `CommunicationOptions` |

### Retired Components (Delete)

| Component | Replacement |
|-----------|-------------|
| `EmailToEmlConverter` + interface | `GraphMessageToEmlConverter` |
| `EmailToDocumentJobHandler` | `IncomingCommunicationProcessor` |
| `EmailPollingBackupService` | `CommunicationPollingBackupService` |
| `EmailEndpoints` webhook routes | `CommunicationWebhookEndpoint` |
| `EmailFilterService` + interface | Per-account `sprk_processingrules` JSON |
| `EmailRuleSeedService` | Not needed (defaults in account config) |
| `sprk_emailprocessingrule` entity | Delete from solution |
| `sprk_approvedsender` entity | Delete from solution |
| `sprk_document.sprk_email` lookup | Replaced by `sprk_document.sprk_communication` |
| Server-Side Sync mailbox config | Graph subscriptions |
| Dataverse Service Endpoint (email webhook) | Delete registration |

### Preserved Components (Reuse)

| Component | Reuse Pattern |
|-----------|---------------|
| `GraphClientFactory.ForApp()` / `ForUserAsync()` | As-is |
| `ServiceBusJobProcessor` | As-is (new job type) |
| `EmailAttachmentProcessor` | With GraphAttachmentAdapter |
| `AttachmentFilterService` | With adapted input |
| `EmlGenerationService` | As-is (outbound archival) |
| OBO token caching (Redis) | As-is |

## Dataverse Entity Schema

### `sprk_communicationaccount` (Already Deployed)

Full schema: `docs/data-model/sprk_communicationaccount.md`

Key fields:
- **Core**: `sprk_name`, `sprk_emailaddress`, `sprk_displayname`, `sprk_accounttype`, `sprk_desscription` (typo — use as-is)
- **Outbound**: `sprk_sendenabled`, `sprk_isdefaultsender`, `sprk_dailysendlimit`, `sprk_sendstoday`
- **Inbound**: `sprk_receiveenabled`, `sprk_monitorfolder`, `sprk_autocreaterecords`, `sprk_processingrules`
- **Graph**: `sprk_graphsubscriptionid`, `sprk_subscriptionid`, `sprk_subscriptionexpiry`, `sprk_subscriptionstatus`
- **Security**: `sprk_securitygroupid`, `sprk_securitygroupname`, `sprk_authmethod`, `sprk_lastverified`, `sprk_verificationstatus`, `sprk_verificationmessage`
- **Archival**: `sprk_ArchiveOutgoingOptIn` (default Yes), `sprk_ArchiveIncomingOptIn` (default Yes)

Known data issues:
- `sprk_desscription` has double 's' — use as-is
- Auth Method label "Apo-Only" is a typo — correct in Dataverse
- Subscription Status Active = `100000` (not `100000000`) — verify

### `sprk_document` Schema Changes

- **Add**: `sprk_communication` lookup → `sprk_communication` entity
- **Remove**: `sprk_email` lookup → email activity (pre-launch clean cutover)
- **Retain**: All `sprk_email*` metadata fields (subject, from, to, cc, date, messageid, direction, conversationindex)

### `sprk_communication` Entity

Existing entity with fields already created for inbound support:
- `sprk_direction` (Incoming/Outgoing)
- `sprk_graphmessageid` (unique Graph message ID)
- `sprk_sentat`, `sprk_receivedat`
- `sprk_from`, `sprk_to`, `sprk_cc`, `sprk_bcc`
- `sprk_subject`, `sprk_body`
- Association fields for entity linking

## Authentication Architecture

### Server-Side (BFF)

| Operation | Auth Pattern | Factory Method |
|-----------|-------------|----------------|
| Shared mailbox send | App-only (client credentials) | `GraphClientFactory.ForApp()` |
| Shared mailbox read (inbound) | App-only (client credentials) | `GraphClientFactory.ForApp()` |
| Individual user send | OBO (delegated) | `GraphClientFactory.ForUserAsync()` |
| Graph subscription management | App-only (client credentials) | `GraphClientFactory.ForApp()` |
| SPE file operations | Via `SpeFileStore` | Facade handles auth |

### Client-Side

All client components MUST use `@spaarke/auth` package:
- `initAuth()` — initialize at startup
- `authenticatedFetch(url, options)` — drop-in fetch with Bearer token
- 5-strategy cascade: bridge → cache → Xrm → MSAL silent → MSAL popup
- All Graph operations flow through BFF (client authenticates to BFF; BFF authenticates to Graph)
- Webpack alias: `'@spaarke/auth': path.resolve(__dirname, '../../shared/SpaarkeAuth/src')`

### Graph Permissions Required

| Permission | Type | Purpose |
|------------|------|---------|
| `Mail.Send` | Application | Outbound shared mailbox |
| `Mail.Read` | Application | Inbound shared mailbox monitoring |
| `Mail.Send` | Delegated | Individual user outbound (OBO) |

### Exchange Access Policy

- Mail-enabled security group restricts app-only access to approved mailboxes only
- One-time setup; adding new mailboxes = add to security group
- Policy applies to both `Mail.Send` and `Mail.Read`

## Service Bus Integration

- **Queue**: Existing `sdap-jobs` queue
- **New Job Type**: `ProcessIncomingCommunication`
- **Payload**: Graph message metadata (mailbox, messageId, receivedDateTime) — NOT message body or attachments
- **Handler**: `IncomingCommunicationProcessor`
- **Idempotency Key**: `Communication:{graphMessageId}:Process` (7-day retention)
- **Processing Lock**: Redis lock, 5-minute TTL
- **Follows**: Existing `ServiceBusJobProcessor` + `IJobHandler` pattern per ADR-004

## Webhook Secret Management

For multi-tenant deployment, Graph subscription `clientState` secrets should use the least complex approach:

- **Recommended**: Per-environment configuration in `appsettings.json` / Azure App Service settings
- **Why**: Single secret per environment is sufficient; Graph subscriptions are per-tenant; no cross-tenant secret sharing needed
- **Security**: Secret validated on every webhook callback to confirm notifications are from Graph
- **Pattern**: `Communication:WebhookSecret` in configuration, validated in `CommunicationWebhookEndpoint`

## SPE File Path Conventions

| Document Type | Path Pattern |
|---------------|-------------|
| Archived .eml | `/communications/{communicationId:N}/{filename}.eml` |
| Attachment | `/communications/attachments/{parentDocId:N}/{filename}` |

## Success Criteria

1. [ ] Outbound shared mailbox: send from `mailbox-central@spaarke.com` → email received → `sprk_communication` record - Verify: manual send test
2. [ ] Outbound individual: "Send as me" → email from user's mailbox → `sprk_communication` record - Verify: manual send test
3. [ ] Inbound monitoring: email TO shared mailbox → `sprk_communication` auto-created (< 60s) - Verify: send test email, check record
4. [ ] Association resolution: incoming from known contact → auto-linked - Verify: send from known contact, check associations
5. [ ] Admin management: CRUD on `sprk_communicationaccount` via Dataverse UI - Verify: create/edit/deactivate records
6. [ ] Verification: click Verify → confirms send/read access → updates status - Verify: run against configured mailbox
7. [ ] Resilience: missed webhooks caught by polling within 5 min - Verify: disable webhook, send email, verify polling catches it
8. [ ] Inbound archival: incoming → .eml to SPE → `sprk_document` linked to `sprk_communication` - Verify: check SPE + Dataverse records
9. [ ] Attachment processing: incoming attachments → filtered → SPE child documents → AI analysis enqueued - Verify: send email with attachments, check child documents
10. [ ] Outbound archival: sent email → .eml to SPE → linked document + child attachments - Verify: send email, check SPE + Dataverse
11. [ ] SSS retired: no Dataverse email activities for communications - Verify: confirm SSS disabled, no email entity records created
12. [ ] Legacy cleanup: retired components deleted from codebase and solution - Verify: grep for deleted class names, check solution

## Dependencies

### Prerequisites

- `sprk_communicationaccount` entity deployed in Dataverse (DONE)
- `sprk_communication` entity with inbound fields deployed (DONE)
- Exchange Online admin access for security group + access policy setup
- Azure AD admin access for Graph permission grants
- `@spaarke/auth` package available in shared library

### External Dependencies

- Microsoft Graph API (subscription lifecycle, message fetch, sendMail)
- Azure Service Bus `sdap-jobs` queue (existing)
- Redis cache (existing)
- SharePoint Embedded containers (existing)
- Exchange Online PowerShell (one-time setup)

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Webhook secrets | What is least complex for multi-tenant deployment? | Per-environment config; security implications acceptable for single-secret-per-tenant approach | Use appsettings.json / App Service settings for webhook clientState |
| Service Bus | New queue or reuse existing? | Use existing `sdap-jobs` queue with new job type `ProcessIncomingCommunication` | No new infrastructure; follows existing pattern |
| sprk_communication fields | Are inbound-support fields created? | Yes, fields have been created in the data model | No entity changes needed for inbound communication records |
| Archival behavior | Default on/off for EML archival? | Default ON for both directions; configurable via `sprk_ArchiveOutgoingOptIn` and `sprk_ArchiveIncomingOptIn` on sprk_communicationaccount | New fields on entity; archival pipeline checks before processing |
| Retry policy | Retry behavior for failed incoming processing? | Follow current policy | Same retry/dead-letter pattern as existing job handlers |
| Graph message fetch | Use $expand for attachments? | Yes, use `$expand=attachments` including attachment content | Single API call per message; no separate attachment fetch needed |
| Association matching | Full AI matching or simple binary? | Simple binary matching — AI playbook augmentation in next release | Implement pattern-based matching (thread, sender, subject); no AI integration now |
| Multi-tenant | Overall guiding principle | Build with ability to deploy in different environments/tenants | No hardcoded values; all environment-specific config externalized |

## Assumptions

*Proceeding with these assumptions (owner confirmed or did not override):*

- **Attachment extraction**: Always-on for initial implementation (controlled via `sprk_processingrules` later if needed)
- **Graph subscription lifetime**: 3-day maximum for mail resources; renew at < 24h remaining
- **Polling interval**: 5 minutes for backup polling service
- **Single Graph API call**: Use `$expand=attachments` to fetch message + attachments in one call
- **Pre-launch**: No migration of existing data, no backward compatibility, clean cutover

## Unresolved Questions

- [ ] `sprk_subscriptionstatus` Active value is `100000` (not `100000000`) — verify in Dataverse and correct if needed - Blocks: Phase C subscription status updates
- [ ] Auth Method label "Apo-Only" typo in Dataverse — should be corrected to "App-Only" - Blocks: nothing (cosmetic)
- [ ] `sprk_autoextractattachments` field not deployed — confirm always-on behavior is acceptable - Blocks: Phase E attachment processing logic

---

*AI-optimized specification. Original design: design-communication-accounts.md*
