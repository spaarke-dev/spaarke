# Email Communication Solution R1 — Implementation Plan

## Executive Summary

- **Purpose**: Replace Dataverse email activities with unified Communication Service via Graph API through BFF. Extended to include Dataverse-managed communication accounts, individual user sending, and inbound shared mailbox monitoring.
- **Scope**: 9 phases — BFF endpoint, entity tracking, communication app, attachments/archival, playbook integration, communication accounts, individual send, inbound monitoring, verification/admin
- **Estimated Effort**: ~176 hours across 55 tasks (120h original + ~56h extension)
- **Original Critical Path**: 001 → 003 → 006 → 010 → 011 → 030 → 032 → 040 → 043 (Phases 1-5, COMPLETE)
- **Extension Critical Path**: 050 → 051 → 055 → 070 → 072 → 076 → 080 → 090 (Phases 6-9)

## Architecture Context

### Design Constraints

| Source | Constraint |
|--------|-----------|
| ADR-001 | Minimal API for all endpoints. No Azure Functions. |
| ADR-007 | SpeFileStore facade for all SPE/Graph file operations. No Graph SDK type leaks. |
| ADR-008 | Endpoint filters for authorization. No global middleware. |
| ADR-010 | Concrete DI registrations via feature module. ≤15 non-framework lines. |
| ADR-013 | Extend BFF for AI tools. IAiToolHandler auto-discovery. |
| ADR-019 | ProblemDetails (RFC 7807) for all errors. Include errorCode + correlationId. |

### Key Technical Decisions

| Decision | Choice | Impact |
|----------|--------|--------|
| Graph auth model | App-only via `GraphClientFactory.ForApp()` | No per-user mailbox config |
| Sender management | Two-tier: BFF config + Dataverse `sprk_communicationaccount` | appsettings.json as fallback, Dataverse preferred |
| Status field | Standard Dataverse `statuscode` (not custom choice) | Use actual values: Draft=1, Send=659490002, Failed=659490004 |
| Regarding fields | `sprk_regardingorganization` + `sprk_regardingperson` | Differs from design (not account/contact) |
| Type field | `sprk_communiationtype` (typo in Dataverse) | Use actual logical name |
| Retry behavior | Fail immediately, return error | No background retry mechanism |
| fromMailbox | Any user can specify | BFF validates against approved sender list |
| Security group scope | Per-account (`sprk_securitygroupid` on record) | More pragmatic than BU-level |
| Association resolution | Separate AI project (NOT this project) | Incoming emails have empty regarding fields |
| Subscription renewal | Fully automated, no human in loop | `GraphSubscriptionManager` BackgroundService |
| Individual user send | Included in scope (~8h) | OBO infrastructure already exists |
| Auth method derivation | From `sprk_accounttype` (no field) | Shared/Service → App-Only, User → OBO |
| Subscription status derivation | From `sprk_subscriptionid` + expiry | No `sprk_subscriptionstatus` field exists |

### Discovered Resources

#### ADRs (6 applicable)
- `.claude/adr/ADR-001-minimal-api.md` — Minimal API + BackgroundService
- `.claude/adr/ADR-007-spefilestore.md` — SpeFileStore facade
- `.claude/adr/ADR-008-endpoint-filters.md` — Endpoint filters for auth
- `.claude/adr/ADR-010-di-minimalism.md` — DI minimalism
- `.claude/adr/ADR-013-ai-architecture.md` — AI architecture
- `.claude/adr/ADR-019-problemdetails.md` — ProblemDetails errors

#### Constraints (3 files)
- `.claude/constraints/api.md` — BFF endpoint MUST/MUST NOT rules
- `.claude/constraints/data.md` — SPE, SpeFileStore, Redis constraints
- `.claude/constraints/auth.md` — OAuth, OBO, authorization seams

#### Patterns (key files)
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoint structure
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter pattern
- `.claude/patterns/api/error-handling.md` — ProblemDetails pattern
- `.claude/patterns/api/service-registration.md` — Feature module DI pattern
- `.claude/patterns/auth/service-principal.md` — App-only Graph auth
- `.claude/patterns/dataverse/entity-operations.md` — Dataverse CRUD
- `.claude/patterns/dataverse/polymorphic-resolver.md` — AssociationResolver pattern

#### Canonical Implementations (existing code to follow)
- `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` — Endpoint structure reference
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — Graph client creation
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/DataverseUpdateToolHandler.cs` — IAiToolHandler pattern
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/SendEmailNodeExecutor.cs` — Graph sendMail pattern
- `src/server/api/Sprk.Bff.Api/Models/RegardingRecordType.cs` — Regarding field mapping
- `src/server/api/Sprk.Bff.Api/Services/Spe/SpeFileStore.cs` — SPE file operations

#### Scripts
- `scripts/Test-SdapBffApi.ps1` — API endpoint testing
- `scripts/Deploy-PCFWebResources.ps1` — PCF deployment (Phase 3)

#### Data Schema (source of truth)
- `docs/data-model/sprk_communication-data-schema.md` — Actual Dataverse entity schema

## Implementation Approach

### Phase Structure

| Phase | Focus | Tasks | Estimated Hours | Parallel Groups | Status |
|-------|-------|-------|----------------|-----------------|--------|
| 1: BFF Email Service | Core send endpoint + wizard rewire | 001-008 | 32h | A (001,002), B (005,006), C (007,008) | ✅ COMPLETE |
| 2: Entity + Tracking | Dataverse record creation + associations | 010-016 | 24h | D (012,013), E (014,015) | ✅ COMPLETE |
| 3: Communication App | Model-driven form + views | 020-025 | 20h | F (020,021), G (023,024) | ✅ COMPLETE |
| 4: Attachments + Archival | SPE integration + .eml | 030-038 | 28h | H (031,032), I (034,035), J (037,038) | ✅ COMPLETE |
| 5: Playbook Integration | AI tool handler | 040-043 | 12h | K (041,042) | ✅ COMPLETE |
| 6: Communication Accounts | Dataverse-managed accounts + outbound config | 050-055 | 16h | L (052,053) | 🔲 PENDING |
| 7: Individual User Outbound | OBO send path + form UX | 060-064 | 12h | M (062,063) | 🔲 PENDING |
| 8: Inbound Monitoring | Graph subscriptions + webhook + processing | 070-077 | 24h | N (073,074), O (075,076) | 🔲 PENDING |
| 9: Verification & Admin | Mailbox verification + admin docs | 080-082 | 8h | — | 🔲 PENDING |
| Wrap-up | Project completion (reset) | 090 | 4h | — | 🔲 PENDING |

### Critical Path (Original — Phases 1-5, COMPLETE)

```
001 (Models) → 003 (Service) → 006 (Module Registration)
                                        ↓
                               010 (Dataverse Integration)
                                        ↓
                               011 (Association Mapping) → 030 (Attachment Fields)
                                                                    ↓
                                                           032 (Attachment Send)
                                                                    ↓
                                                           040 (AI Tool Handler)
                                                                    ↓
                                                           043 (E2E Testing)
```

### Critical Path (Extension — Phases 6-9)

```
050 (Account Service) → 051 (Validator Update) → 055 (E2E Test)
                                                       ↓
                                        ┌──────────────┼──────────────┐
                                        ↓              ↓              ↓
                              060 (SendMode)    070 (Subscriptions)   |
                                        ↓              ↓              |
                              064 (E2E Test)    072 (Processor)       |
                                        ↓              ↓              |
                                        └──────────────┼──────────────┘
                                                       ↓
                                              080 (Verification)
                                                       ↓
                                              090 (Wrap-up)
```

## Phase Breakdown (WBS)

### Phase 1: BFF Email Service

**Objectives**: Ship the core send endpoint so emails can be sent via Graph API. Rewire Create Matter wizard.

**Deliverables**:
1. `SendCommunicationRequest` / `SendCommunicationResponse` models
2. `CommunicationOptions` configuration class
3. `CommunicationService` with Graph sendMail integration
4. `CommunicationEndpoints` with POST /api/communications/send
5. Approved sender validation
6. Communication authorization filter
7. `AddCommunicationModule()` DI registration
8. Unit tests for service and validation
9. Simplified `matterService.ts` in workspace

**Inputs**: spec.md, existing GraphClientFactory, existing EmailEndpoints pattern
**Outputs**: Working send endpoint, passing tests, simplified wizard code

### Phase 2: Dataverse Integration

**Objectives**: Create `sprk_communication` records after send. Support primary association.

**Deliverables**:
1. Dataverse record creation after successful Graph send
2. Primary association mapping (8 entity types + denormalized fields)
3. GET /api/communications/{id}/status endpoint
4. `sprk_approvedsender` Dataverse entity (optional)
5. Two-tier sender merge logic
6. Communication subgrid on Matter form
7. Unit tests for Dataverse integration

**Inputs**: Phase 1 complete, sprk_communication entity (already exists)
**Outputs**: Tracked communications in Dataverse, Matter form subgrid

### Phase 3: Communication Application

**Objectives**: Standalone compose/view experience for communications.

**Deliverables**:
1. Model-driven form (compose + read modes)
2. AssociationResolver PCF configuration on form
3. "Send" command bar button
4. Communication views (My Sent, By Matter, By Project, Failed, All)
5. Communication subgrids on entity forms
6. End-to-end form testing

**Inputs**: Phase 2 complete, AssociationResolver PCF (production-ready)
**Outputs**: Working communication form, views, and subgrids

### Phase 4: Attachments + Archival

**Objectives**: Full document integration — attach SPE documents, archive sent emails.

**Deliverables**:
1. `sprk_hasattachments` and `sprk_attachmentcount` fields on entity
2. `sprk_communicationattachment` intersection entity
3. Attachment download from SPE → Graph sendMail payload
4. `sprk_communicationattachment` record creation
5. .eml generation from communication data
6. .eml archival to SPE with `sprk_document` creation
7. Document attachment picker on form
8. POST /api/communications/send-bulk endpoint
9. Tests for attachment and archival flows

**Inputs**: Phase 2 complete, SpeFileStore facade, existing email-to-document patterns
**Outputs**: Full attachment support, archival pipeline, bulk send

### Phase 5: Playbook Integration

**Objectives**: AI tools can send communications.

**Deliverables**:
1. `SendCommunicationToolHandler : IAiToolHandler`
2. Tool registration verification
3. Playbook scenario testing
4. End-to-end integration test

**Inputs**: Phase 1+ complete, AI Tool Framework
**Outputs**: Working AI tool, playbook email scenarios

### Phase 6: Communication Account Management

**Objectives**: Replace `appsettings.json`-only approved sender config with Dataverse-managed `sprk_communicationaccount` entity. Configure `mailbox-central@spaarke.com`. Set up Exchange access policy.

**Deliverables**:
1. `CommunicationAccountService` with Dataverse query + Redis cache
2. Updated `ApprovedSenderValidator` to use new service
3. Updated `IDataverseService` interface (`QueryCommunicationAccountsAsync`)
4. `sprk_communicationaccount` admin form and views
5. `appsettings.json` configured with `mailbox-central@spaarke.com`
6. Exchange access policy setup documentation
7. End-to-end outbound send testing

**Inputs**: Phases 1-5 complete, `sprk_communicationaccount` entity (already exists in Dataverse)
**Outputs**: Dataverse-managed sender accounts, working outbound send from `mailbox-central@spaarke.com`

### Phase 7: Individual User Outbound

**Objectives**: Users can choose "Send as me" to send from their own mailbox via OBO flow.

**Deliverables**:
1. `SendMode` enum + updated `SendCommunicationRequest`
2. OBO branch in `CommunicationService.SendAsync()`
3. Updated `sprk_communication_send.js` with send mode selection
4. Updated Communication form UX
5. Unit and E2E tests

**Inputs**: Phase 6 complete, `GraphClientFactory.ForUserAsync()` (existing), `SendEmailNodeExecutor` (OBO pattern reference)
**Outputs**: Users can send as themselves or from shared mailbox

### Phase 8: Inbound Shared Mailbox Monitoring

**Objectives**: Incoming emails to monitored mailboxes automatically create `sprk_communication` records. Association resolution is NOT in scope (separate AI project).

**Deliverables**:
1. `GraphSubscriptionManager` BackgroundService (auto-create/renew subscriptions)
2. `POST /api/communications/incoming-webhook` endpoint
3. `IncomingCommunicationProcessor` job handler
4. Backup polling service for missed webhooks
5. Incoming communication views in Dataverse
6. Unit and E2E tests

**Inputs**: Phase 6 complete, `Mail.Read` app permission, existing patterns (EmailPollingBackupService, ServiceBusJobProcessor, EmailAttachmentProcessor)
**Outputs**: Incoming emails tracked as `sprk_communication` records with Direction=Incoming

**Important**: Association resolution (matching incoming email to entities) is explicitly OUT OF SCOPE — this is an AI-driven process that belongs in the AI Analysis Enhancements project. Incoming communication records are created with empty regarding fields.

### Phase 9: Verification & Admin UX

**Objectives**: Admin tooling for verifying mailbox connectivity and managing accounts.

**Deliverables**:
1. `POST /api/communications/accounts/{id}/verify` endpoint
2. Verification logic (test send + test read)
3. Updated `sprk_communicationaccount` form with verification UI
4. Admin documentation

**Inputs**: Phases 6-8 complete
**Outputs**: Admin can verify and manage communication accounts

## Dependencies

### External
- Microsoft Graph API (`Mail.Send` + `Mail.Read` application permissions)
- Delegated `Mail.Send` permission (for OBO individual user send)
- Shared mailbox `mailbox-central@spaarke.com` in Azure AD (Exchange Online license)
- Application access policy (scope permissions to security group)
- Mail-enabled security group "SDAP Communication Accounts"

### Internal
- `GraphClientFactory.ForApp()` — Available
- `IDataverseService` / `DataverseWebApiService` — Available
- `SpeFileStore` — Available
- `IAiToolHandler` / `IToolHandlerRegistry` — Available
- `RegardingRecordType` mapping — Available
- AssociationResolver PCF — Production-ready
- `authenticatedFetch` — Available in workspace SPA

## Testing Strategy

### Unit Tests
- `CommunicationService` — Mock Graph client, verify sendMail call
- Approved sender validation — Config-only and merged scenarios
- Association field mapping — Entity type to lookup field resolution
- .eml generation — Verify correct format output

### Integration Tests
- POST /api/communications/send — End-to-end with test mailbox
- Dataverse record creation — Verify sprk_communication fields
- Attachment download + send — SpeFileStore → Graph payload
- Communication account query — Verify sprk_communicationaccount OData filter
- Individual send via OBO — Verify /me/sendMail flow
- Incoming webhook — Verify Graph notification processing
- Backup polling — Verify missed message detection

### UI Tests (Phase 3)
- Communication form compose mode
- AssociationResolver entity selection
- Send button executes BFF call
- Communication views filter correctly
- Send mode selection (shared vs individual) — Phase 7
- Communication account admin form — Phase 6

## Acceptance Criteria

Reference: spec.md Success Criteria (G1-G9)

## Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|-----------|
| R1 | Graph rate limits (10K/day) | Low | Medium | Multiple shared mailboxes |
| R2 | Large attachments (>35MB) | Medium | Low | Validate before send, sharing links |
| R3 | Shared mailbox license needed | Low | High | Verify before Phase 1 |
| R4 | Mail.Send permission too broad | Medium | Medium | Application access policy |
| R5 | matterService.ts merge conflict | Medium | Low | Coordinate with PR #186 |

## Phases 1-5 Implementation Summary (Completed 2026-02-21)

### Tasks Completed (Original Scope)
- **Total Tasks**: 35/35 (100%)
- **Phase 1 (BFF Service)**: 8/8 ✅
- **Phase 2 (Dataverse Integration)**: 7/7 ✅
- **Phase 3 (Communication App)**: 6/6 ✅
- **Phase 4 (Attachments + Archival)**: 9/9 ✅
- **Phase 5 (Playbook Integration)**: 4/4 ✅
- **Wrap-up (Original)**: 1/1 ✅

### What Was Delivered (Phases 1-5)
1. **BFF Communication Service** — Single endpoint for Graph API email sending
2. **Dataverse Integration** — sprk_communication record tracking with 8-entity association support
3. **Communication App** — Model-driven form with compose/read modes, views, and subgrids
4. **Attachment Support** — SPE document attachment, Graph payload construction, intersection entity
5. **Email Archival** — .eml generation and archival to SPE with sprk_document linkage
6. **Bulk Send** — POST /api/communications/send-bulk with multi-recipient support
7. **AI Playbook Integration** — SendCommunicationToolHandler for playbook email scenarios
8. **Comprehensive Tests** — 30+ unit/integration tests validating all phases

## Extension Scope (Phases 6-9, Added 2026-02-22)

### Why Extend

During operational testing of the completed Phases 1-5, the following gaps were identified:

1. **No central mailbox management** — Approved senders only in appsettings.json, no admin UI
2. **No incoming email processing** — No way to track incoming emails as Communication records
3. **No individual user sending** — Only shared mailbox sending (app-only auth)
4. **Shared mailbox confirmed** — `mailbox-central@spaarke.com` now ready for configuration

### Extension Tasks (20 new tasks)
- **Phase 6 (Communication Accounts)**: 6 tasks, ~16h
- **Phase 7 (Individual User Outbound)**: 5 tasks, ~12h
- **Phase 8 (Inbound Monitoring)**: 8 tasks, ~24h
- **Phase 9 (Verification & Admin)**: 3 tasks, ~8h
- **Wrap-up (Reset)**: 1 task, ~4h

### Key Design Decisions (Extension)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Security group scope | Per-account (`sprk_securitygroupid` on record) | Different accounts may use different security groups |
| Association resolution | Separate AI project | AI-driven process, not deterministic code |
| Subscription renewal | Fully automated, no human in loop | Reliability requirement |
| Individual user send | Included (~8h) | OBO infrastructure already exists |
| Inbound individual | Deferred to separate project | Fundamentally different consent model |

### Source Documents
- `projects/email-communication-solution-r1/design-communication-accounts.md` — Full design for Phases 6-9
- `docs/data-model/sprk_communication-data-schema.md` — Actual Dataverse schema (source of truth)
- `projects/email-communication-solution-r1/notes/project-completion-summary.md` — Phases 1-5 lessons learned

---

*Generated by project-pipeline on 2026-02-20*
*Updated with completion summary on 2026-02-21*
*Extended with Phases 6-9 on 2026-02-22*
