# Email Communication Solution R2 — Implementation Plan

> **Project**: email-communication-solution-r2
> **Created**: 2026-03-09
> **Phases**: 5 (A through E)
> **Guiding Principle**: Multi-tenant deployment readiness

## Architecture Context

### Discovered Resources

**ADRs (17 applicable)**:
- ADR-001 (Minimal API + BackgroundService) — all endpoints, background services
- ADR-002 (Thin Plugins) — any validation plugins on sprk_communicationaccount
- ADR-003 (Lean Authorization Seams) — endpoint authorization rules
- ADR-004 (Async Job Contract) — ProcessIncomingCommunication job type
- ADR-005 (Flat Storage in SPE) — .eml and attachment storage
- ADR-007 (SpeFileStore Facade) — all SPE operations
- ADR-008 (Endpoint Filters) — communication endpoint auth
- ADR-009 (Redis-First Caching) — idempotency keys, processing locks, sender cache
- ADR-010 (DI Minimalism) — CommunicationModule registration
- ADR-017 (Async Job Status) — job status persistence
- ADR-018 (Feature Flags) — kill switches for send/receive
- ADR-019 (ProblemDetails) — error responses
- ADR-020 (Versioning Strategy) — job/API evolution
- ADR-006 (PCF/Code Pages) — admin UI
- ADR-012 (Shared Component Library) — @spaarke/ui-components
- ADR-021 (Fluent UI v9) — all UI
- ADR-022 (PCF Platform Libraries) — React 16 for PCF

**Existing Code (R1 — Partial Implementations)**:
- `Services/Communication/CommunicationService.cs` — outbound send pipeline
- `Services/Communication/CommunicationAccountService.cs` — account CRUD
- `Services/Communication/ApprovedSenderValidator.cs` — sender validation
- `Services/Communication/GraphSubscriptionManager.cs` — subscription lifecycle
- `Services/Communication/IncomingCommunicationProcessor.cs` — inbound processing
- `Services/Communication/InboundPollingBackupService.cs` — backup polling
- `Services/Communication/EmlGenerationService.cs` — outbound EML
- `Services/Email/EmailAttachmentProcessor.cs` — attachment processing
- `Services/Email/AttachmentFilterService.cs` — attachment filtering
- `Services/Email/EmailToEmlConverter.cs` — Dataverse-based (TO BE RETIRED)
- `Services/Email/EmailFilterService.cs` — (TO BE RETIRED)
- `Services/Email/EmailRuleSeedService.cs` — (TO BE RETIRED)
- `Services/Jobs/EmailToDocumentJobHandler.cs` — (TO BE RETIRED)
- `Services/Jobs/EmailPollingBackupService.cs` — (TO BE RETIRED)
- `Infrastructure/Graph/GraphClientFactory.cs` — Graph client creation
- `Infrastructure/DI/CommunicationModule.cs` — DI registration
- `Api/CommunicationEndpoints.cs` — communication API routes
- `Api/EmailEndpoints.cs` — email webhook (TO BE RETIRED)
- `Configuration/CommunicationOptions.cs` — config options
- `Configuration/EmailProcessingOptions.cs` — (TO BE CONSOLIDATED)
- `Telemetry/EmailTelemetry.cs` — (TO BE RENAMED)
- `Api/Filters/CommunicationAuthorizationFilter.cs` — auth filter
- 20 model classes in `Services/Communication/Models/`

**Scripts**:
- `scripts/Register-EmailWebhook.ps1` — Graph subscription registration

**Knowledge Docs**:
- `docs/guides/COMMUNICATION-ADMIN-GUIDE.md`
- `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md`
- `docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md`
- `docs/architecture/communication-service-architecture.md`
- `docs/architecture/email-to-document-automation.md`
- `docs/data-model/sprk_communicationaccount.md`

**Constraints**:
- `.claude/constraints/api.md` — API endpoint constraints
- `.claude/constraints/auth.md` — authentication constraints
- `.claude/constraints/jobs.md` — job processing constraints
- `.claude/constraints/config.md` — configuration constraints
- `.claude/constraints/testing.md` — testing constraints

**Patterns**:
- `.claude/patterns/api/endpoint-definition.md`
- `.claude/patterns/api/endpoint-filters.md`
- `.claude/patterns/api/background-workers.md`
- `.claude/patterns/auth/obo-flow.md`
- `.claude/patterns/auth/token-caching.md`
- `.claude/patterns/caching/distributed-cache.md`
- `.claude/patterns/dataverse/entity-operations.md`

## Phase Breakdown

### Phase 1: Communication Account Entity + Outbound Config (Phase A)

**Goal**: Replace appsettings.json approved senders with Dataverse-managed accounts.

**R1 Status**: Entity deployed, `CommunicationAccountService` exists, `ApprovedSenderValidator` has Dataverse merge logic.

**Deliverables**:

1.1 **Assess R1 Implementation State**
- Audit existing `CommunicationAccountService.cs`, `ApprovedSenderValidator.cs`
- Audit existing Dataverse forms/views for `sprk_communicationaccount`
- Identify gaps between R1 code and R2 spec requirements
- Document what's complete vs. needs work

1.2 **Complete Account Entity Admin UX**
- Create/update Dataverse forms for `sprk_communicationaccount` (all field groups)
- Create admin views (Active Accounts, Send-Enabled, Receive-Enabled, By Type)
- Seed `mailbox-central@spaarke.com` record

1.3 **Complete ApprovedSenderValidator Migration**
- Ensure query uses `sprk_communicationaccount` (not `sprk_approvedsender`)
- Method renamed: `QueryCommunicationAccountsAsync`
- Filter: `sprk_sendenabled eq true AND statecode eq 0`
- Retain appsettings.json as fallback

1.4 **Exchange Access Policy Setup**
- Document mail-enabled security group creation
- Document application access policy setup
- Test with `Test-ApplicationAccessPolicy`

1.5 **End-to-End Outbound Test**
- Send email from Communication form
- Verify `sprk_communication` record created
- Verify config sourced from Dataverse entity

### Phase 2: Individual User Outbound (Phase B)

**Goal**: Users can send as themselves via OBO auth.

**R1 Status**: `SendMode` enum exists, `GraphClientFactory.ForUserAsync()` works, `SendEmailNodeExecutor` sends as user.

**Deliverables**:

2.1 **Assess R1 OBO Send Implementation**
- Audit `CommunicationService.SendAsync()` for existing OBO branch
- Audit `SendMode` model and request DTO
- Identify gaps

2.2 **Complete OBO Send Path**
- Ensure SendAsync branches correctly on SendMode.User
- Resolve user email from OBO token claims
- Skip approved sender validation for user send
- Set `sprk_from` to user's email

2.3 **Communication Form UX Update**
- Update web resource with send mode selection
- Add "Send as me" vs "Shared mailbox" dropdown
- Use `@spaarke/auth` for BFF API calls

2.4 **End-to-End Individual Send Test**
- Send as user → email from user's mailbox
- Verify `sprk_communication` record with correct sender

### Phase 3: Inbound Shared Mailbox Monitoring (Phase C)

**Goal**: Incoming emails auto-create `sprk_communication` records.

**R1 Status**: `GraphSubscriptionManager`, `IncomingCommunicationProcessor`, `InboundPollingBackupService`, `CommunicationWebhookEndpoint` exist in some form.

**Deliverables**:

3.1 **Assess R1 Inbound Pipeline**
- Audit `GraphSubscriptionManager.cs` (create/renew/recreate)
- Audit `IncomingCommunicationProcessor.cs` (message fetch, record creation)
- Audit `InboundPollingBackupService.cs` (polling logic)
- Audit webhook endpoint in `CommunicationEndpoints.cs`
- Identify gaps vs R2 spec

3.2 **Complete Graph Subscription Lifecycle**
- Ensure subscription creation for receive-enabled accounts
- Ensure renewal < 24h before expiry
- Ensure failed subscription recreation
- Update `sprk_communicationaccount` fields (subscriptionId, expiry, status)
- Webhook secret from per-environment config

3.3 **Complete Webhook Endpoint**
- Validate Graph subscription notifications
- Handle subscription validation handshake
- Extract message metadata, enqueue `ProcessIncomingCommunication` job
- Response < 3 seconds (NFR-01)

3.4 **Complete Incoming Communication Processor**
- Fetch full message via Graph `GET /users/{mailbox}/messages/{id}?$expand=attachments`
- Create `sprk_communication` record (Direction = Incoming)
- Map all fields (from, to, cc, subject, body, timestamps, graphMessageId)
- Deduplication via Redis idempotency key + `sprk_graphmessageid` uniqueness

3.5 **Implement Association Resolution**
- Email thread (In-Reply-To header) → existing communication chain
- Sender email → contact/account match
- Subject line → matter number patterns
- Mailbox context → purpose-specific associations
- Flag unresolved as `sprk_associationstatus = Pending Review`

3.6 **Complete Backup Polling Service**
- Poll every 5 minutes per receive-enabled mailbox
- Query Graph for messages since last poll
- Deduplicate against already-processed messages
- Enqueue missed messages

3.7 **Incoming Communication Views**
- Create Dataverse views for incoming communications
- Filter by direction, status, association status

3.8 **End-to-End Inbound Test**
- Send email TO monitored mailbox
- Verify webhook fires → communication record created
- Verify association resolution
- Verify backup polling catches missed messages

### Phase 4: Email-to-Document Migration (Phase E)

**Goal**: Replace Dataverse email-based archival with Graph-based pipeline.

**R1 Status**: `EmlGenerationService` works for outbound. `EmailToEmlConverter` (Dataverse-based) needs replacement.

**Deliverables**:

4.1 **Create GraphMessageToEmlConverter**
- Convert Graph Message → RFC 2822 .eml via MimeKit
- Map all headers (from, to, cc, bcc, subject, date, messageId, references)
- Map attachments: Graph FileAttachment → MimePart
- Handle inline vs. file attachments
- Pure transformation — no I/O

4.2 **Create GraphAttachmentAdapter**
- Map Graph `FileAttachment` → `EmailAttachmentInfo`
- Preserve compatibility with `AttachmentFilterService`
- Handle content type, size, inline flag, contentId

4.3 **Update Dataverse Schema**
- Add `sprk_communication` lookup to `sprk_document`
- Remove `sprk_email` lookup from `sprk_document`
- Retain all `sprk_email*` metadata fields

4.4 **Integrate Document Archival into Inbound Pipeline**
- After communication record creation:
  - Check `sprk_ArchiveIncomingOptIn` on account
  - Convert Graph message → .eml via `GraphMessageToEmlConverter`
  - Upload .eml to SPE (`/communications/{communicationId:N}/{filename}.eml`)
  - Create `sprk_document` with `sprk_communication` lookup
  - Process attachments via adapter + `AttachmentFilterService`
  - Create child `sprk_document` records for each attachment
  - Enqueue AI analysis for .eml and each attachment

4.5 **Enhance Outbound Archival**
- Check `sprk_ArchiveOutgoingOptIn` on account
- Link existing .eml archive to `sprk_communication` via new lookup
- Process outbound attachments as child documents
- Enqueue AI analysis

4.6 **Delete Retired Components**
- Delete: `EmailToEmlConverter`, `IEmailToEmlConverter`
- Delete: `EmailToDocumentJobHandler`
- Delete: `EmailPollingBackupService`
- Delete: `EmailFilterService`, `IEmailFilterService`
- Delete: `EmailRuleSeedService`
- Delete: `EmailEndpoints` webhook routes
- Delete: `EmailProcessingOptions` (consolidate into `CommunicationOptions`)
- Delete: `sprk_emailprocessingrule` entity from solution
- Delete: `sprk_approvedsender` entity from solution
- Delete: Dataverse Service Endpoint (email webhook registration)

4.7 **Rename/Consolidate**
- `EmailTelemetry` → `CommunicationTelemetry` (update metric names)
- Consolidate remaining email config into `CommunicationOptions`

4.8 **Disable Server-Side Sync**
- Disable SSS mailbox configuration
- Document retirement steps

4.9 **End-to-End Archival Tests**
- Inbound: email → communication record → .eml document → child attachments → AI analysis
- Outbound: send → communication record → .eml document → child attachments → AI analysis
- Verify no Dataverse email activities created

### Phase 5: Verification & Admin UX (Phase D)

**Goal**: Admins verify mailbox connectivity and manage accounts.

**R1 Status**: `VerificationResult` and `VerificationStatus` models exist.

**Deliverables**:

5.1 **Verification Endpoint**
- `POST /api/communications/accounts/{id}/verify`
- Test send access (try sending test email)
- Test read access (try reading inbox)
- Update `sprk_verificationstatus`, `sprk_lastverified`, `sprk_verificationmessage`

5.2 **Daily Send Count Tracking**
- Track `sprk_sendstoday` on send operations
- Daily reset mechanism (BackgroundService timer or job)
- Warn when approaching `sprk_dailysendlimit`

5.3 **Admin Form Enhancements**
- Verification status display on form
- Verification button (triggers endpoint)
- Send count display
- Subscription status display for receive-enabled accounts

5.4 **Admin Documentation**
- Update `COMMUNICATION-ADMIN-GUIDE.md`
- Update `COMMUNICATION-DEPLOYMENT-GUIDE.md`
- Include multi-tenant deployment instructions

5.5 **Project Wrap-Up**
- Update README status to Complete
- Create lessons-learned.md
- Archive project artifacts

## Phase Dependencies

```
Phase 1 (A): Foundation — no dependencies
    ├──→ Phase 2 (B): Individual Send (parallel with Phase 3)
    ├──→ Phase 3 (C): Inbound Monitoring (parallel with Phase 2)
    │        └──→ Phase 4 (E): Document Migration
    └──→ Phase 5 (D): Verification & Admin (after 1-3)
```

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| R1 code may not match R2 spec | Rework | Phase assessment tasks audit existing code first |
| Graph subscription webhook unreliable | Missed emails | Backup polling (5 min) |
| Graph subscription 3-day expiry | Monitoring stops | Auto-renewal in GraphSubscriptionManager |
| Exchange access policy delay (30 min) | Send/read fails | Verify before go-live |
| Large attachments exceed Graph limits | Missing documents | Individual failure isolation |
| Duplicate processing (webhook + polling) | Double records | Redis idempotency + sprk_graphmessageid uniqueness |

## References

- [Spec](spec.md) — Full requirements specification
- [Design](design-communication-accounts.md) — Original design document
- [Communication Service Architecture](../../docs/architecture/communication-service-architecture.md)
- [Email-to-Document Architecture](../../docs/guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md)
- [Email-to-Document Automation](../../docs/architecture/email-to-document-automation.md)
- [Communication Account Data Model](../../docs/data-model/sprk_communicationaccount.md)
- [Communication Admin Guide](../../docs/guides/COMMUNICATION-ADMIN-GUIDE.md)
- [Communication Deployment Guide](../../docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md)
