# Email Communication Solution R1

> **Status**: COMPLETE
> **Branch**: `work/email-communication-solution-r1`
> **Started**: 2026-02-20
> **Phases 1-5 Completed**: 2026-02-21
> **Phases 6-9 Completed**: 2026-02-22
> **Project Complete**: 2026-02-22

## Quick Links

- [Implementation Plan](plan.md)
- [AI Context](CLAUDE.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Specification](spec.md)
- [Original Design](design.md)
- [Extension Design — Communication Accounts](design-communication-accounts.md)
- [Data Schema](../../docs/data-model/sprk_communication-data-schema.md)

## Overview

Unified Communication Service that replaces heavyweight Dataverse email activities with Graph API-based sending through the BFF. Tracks all outbound communications in a custom `sprk_communication` entity with AssociationResolver pattern for multi-entity linking. Provides a single email pipeline for workspace UI, AI playbooks, background jobs, and future communication channels.

**Extension (Phases 6-9)**: Adds Communication Account management (`sprk_communicationaccount` entity) for Dataverse-managed mailbox configuration, individual user sending via OBO (On-Behalf-Of) flow, and inbound email monitoring through Graph subscription webhooks with backup polling. Replaces hardcoded `appsettings.json` sender configuration with a fully Dataverse-driven model.

## Problem Statement

1. Dataverse email activities are heavyweight (~200 lines of workarounds in Create Matter wizard)
2. Server-side sync requires per-user mailbox configuration (doesn't scale)
3. Activity permissions are coarse-grained (no entity-level granularity)
4. No reusable email service (each feature implements its own sending logic)
5. Email-to-document archival tightly coupled to Dataverse email activities
6. No central mailbox management (approved senders only in appsettings.json)
7. No incoming email processing into `sprk_communication` records
8. No individual user sending (only shared mailbox)

## Solution Summary

- **BFF Communication Service**: Single `POST /api/communications/send` endpoint via Graph API (app-only, shared mailbox)
- **Custom Tracking Entity**: `sprk_communication` with fine-grained security (already exists in Dataverse)
- **AssociationResolver Pattern**: Same production-proven pattern as `sprk_event` for multi-entity linking
- **Document Integration**: SPE document attachments + .eml archival
- **AI Playbook Support**: `SendCommunicationToolHandler` as IAiToolHandler
- **Approved Sender Model**: Two-tier (BFF config + Dataverse override) for flexible sender management
- **Communication Account Management**: `sprk_communicationaccount` entity for Dataverse-managed mailbox configuration, replacing hardcoded appsettings.json
- **Individual User Send**: OBO-based sending via `GraphClientFactory.ForUserAsync()` allowing users to send as themselves
- **Inbound Monitoring**: Graph subscription webhooks + backup polling for incoming email auto-creation of `sprk_communication` records
- **Mailbox Verification**: Admin verification endpoint for connectivity testing (send/read access confirmation)

## Graduation Criteria

### Functional (Phases 1-5 -- Complete)
- [x] Single BFF endpoint sends email via Graph API without per-user mailbox config
- [x] `sprk_communication` record created for every outbound email with correct association
- [x] Create Matter wizard uses BFF endpoint (Dataverse email activity code removed)
- [x] Communication model-driven form supports compose and read modes
- [x] AssociationResolver PCF works on communication form (8 entity types)
- [x] SPE documents can be attached to outbound emails
- [x] Sent emails archived as .eml in SPE with `sprk_document` record
- [x] AI playbooks can send emails via `send_communication` tool
- [x] Approved sender validation enforces configured mailbox list

### Functional (Phases 6-9 -- Extension -- Complete)
- [x] Communication accounts managed entirely through Dataverse UI
- [x] Individual user can send as themselves (OBO flow)
- [x] Incoming email to shared mailbox auto-creates `sprk_communication` record
- [x] Graph subscriptions auto-renew without human intervention
- [x] Backup polling catches missed webhooks within 15 minutes
- [x] Mailbox verification endpoint confirms send/read access

### Quality
- [x] All BFF endpoints return ProblemDetails on error (ADR-019)
- [x] Endpoint authorization filters on all communication endpoints (ADR-008)
- [x] Unit tests for CommunicationService, approved sender validation
- [x] No Graph SDK types leaked above SpeFileStore facade (ADR-007)

### Performance
- [x] Email send completes within 5 seconds (Graph API round-trip)
- [x] Attachment download + send within 15 seconds for typical documents (<5MB)

## Scope

### In Scope

**Phases 1-5 (Complete)**:
- Email sending via Microsoft Graph (app-only, shared mailbox)
- `sprk_communication` entity tracking with AssociationResolver
- Communication model-driven form (compose + read)
- Document attachment support (SPE documents)
- .eml archival to SPE
- Create Matter wizard rewire
- AI tool handler for playbooks
- Two-tier approved sender model (config + Dataverse)

**Phase 6 -- Communication Account Management**:
- `sprk_communicationaccount` entity integration (Dataverse-managed mailbox config)
- Replace hardcoded `appsettings.json` approved senders with Dataverse entity reads
- Admin mailbox verification endpoint (connectivity testing)
- Security group configuration per communication account

**Phase 7 -- Individual User Send**:
- OBO-based sending via `GraphClientFactory.ForUserAsync()`
- Individual user sends as themselves (delegated `Mail.Send` permission)
- Send endpoint routing: shared mailbox (app-only) vs. individual (OBO) based on request

**Phase 8 -- Inbound Email Monitoring**:
- Graph subscription webhooks for incoming shared mailbox email
- Automatic `sprk_communication` record creation for incoming messages
- Subscription auto-renewal (fully automated, no human in loop)
- Backup polling to catch missed webhooks within 15 minutes

**Phase 9 -- Integration and Hardening**:
- End-to-end integration testing across all communication flows
- Error handling and resilience for subscription lifecycle
- Documentation and deployment procedures

### Out of Scope
- Individual user inbound monitoring (only shared mailbox inbound)
- Association resolution for incoming email (AI process, separate project)
- Automated Exchange security group management
- SMS / Teams / notification channels
- Email templates engine (Liquid/Handlebars)
- Read receipts / delivery notifications
- Bulk marketing email
- Background retry on send failure

## Key Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Send mechanism | Graph API (app-only) | No per-user mailbox config, works from any context | — |
| Sender identity | Shared mailbox + approved sender list | Consistent "from" address, two-tier management | — |
| Tracking entity | `sprk_communication` (custom) | Fine-grained security, no activity baggage | — |
| Association pattern | AssociationResolver (entity-specific lookups) | Production-proven PCF, configuration-driven | — |
| Attachment model | Intersection entity (`sprk_communicationattachment`) | No file duplication, leverages existing SPE documents | — |
| Archival format | .eml in SPE | Consistent with existing email-to-document pipeline | — |
| Error handling | ProblemDetails (RFC 7807) | Standard error format with error codes | ADR-019 |
| Authorization | Endpoint filters | Per-endpoint resource authorization | ADR-008 |
| DI pattern | Concrete registrations via feature module | `AddCommunicationModule()` | ADR-010 |
| AI integration | IAiToolHandler auto-discovery | Consistent with existing tool framework | ADR-013 |
| Retry on failure | Fail immediately, return error | Simple for Phase 1; retry can be added later | — |
| Security group scope | Per-account on `sprk_communicationaccount` | Not BU-level; each account controls its own security group | — |
| Association resolution | Separate AI project | Not in scope for this project; AI-based matching is distinct concern | — |
| Subscription renewal | Fully automated | No human in loop; background service handles renewal before expiry | — |
| Individual send | OBO via `ForUserAsync()` | Included in scope; users send as themselves with delegated permission | — |

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Graph sendMail rate limits (10K/day per mailbox) | Bulk sends throttled | Multiple shared mailboxes, queue with backpressure |
| Large attachments (>35MB Graph limit) | Send failure | Validate size before send, sharing links for large files |
| Shared mailbox requires Exchange Online license | Blocked if no license | Verify license availability |
| `Mail.Send` permission is broad | Security concern | Application access policy to scope to specific mailbox |
| matterService.ts in separate worktree | Merge coordination | Coordinate with PR #186 (workspace) |

## Dependencies

| Dependency | Status | Phase |
|------------|--------|-------|
| Graph SDK in BFF | Available | 1 |
| `GraphClientFactory.ForApp()` | Available | 1 |
| `authenticatedFetch` in workspace | Available | 1 |
| `sprk_communication` entity | **Already exists** | 2 |
| AssociationResolver PCF | Available (production) | 2-3 |
| `SpeFileStore` facade | Available | 4 |
| AI Tool Framework | Available | 5 |
| `Mail.Send` app permission | Configured | 1 |
| Shared mailbox | Configured | 1 |
| `sprk_communicationaccount` entity | **Already exists** | 6 |
| `Mail.Read` app permission | Needs configuration | 8 |
| Delegated `Mail.Send` permission | Needs configuration | 7 |
| `mailbox-central@spaarke.com` | Confirmed exists | 6 |

## Implementation Summary

### Phases 1-5 (Complete -- 35 tasks)

| Phase | Description | Status |
|-------|-------------|--------|
| Phase 1 | BFF Email Service (CommunicationService, Graph send, approved senders) | Complete |
| Phase 2 | Dataverse Integration (sprk_communication records, association linking) | Complete |
| Phase 3 | Communication Form (model-driven form, compose/read modes, AssociationResolver PCF) | Complete |
| Phase 4 | Document Integration (SPE attachments, .eml archival) | Complete |
| Phase 5 | AI Integration (SendCommunicationToolHandler, playbook support) | Complete |

### Phases 6-9 (Extension -- 20 tasks, Complete)

| Phase | Description | Status |
|-------|-------------|--------|
| Phase 6 | Communication Account Management (sprk_communicationaccount, CommunicationAccountService, mailbox config) | Complete |
| Phase 7 | Individual User Send (OBO flow, ForUserAsync, SendMode selection, MSAL auth) | Complete |
| Phase 8 | Inbound Email Monitoring (GraphSubscriptionManager, webhooks, IncomingCommunicationProcessor, backup polling) | Complete |
| Phase 9 | Verification & Admin UX (MailboxVerificationService, admin guide, deployment guide) | Complete |

**Total**: 55 tasks complete (35 original + 20 extension)

---

*Generated by project-pipeline on 2026-02-20. Extended 2026-02-22.*
