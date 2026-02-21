# Email Communication Solution R1

> **Status**: COMPLETE
> **Branch**: `work/email-communication-solution-r1`
> **Started**: 2026-02-20
> **Completed**: 2026-02-21

## Quick Links

- [Implementation Plan](plan.md)
- [AI Context](CLAUDE.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Specification](spec.md)
- [Original Design](design.md)
- [Data Schema](../../docs/data-model/sprk_communication-data-schema.md)

## Overview

Unified Communication Service that replaces heavyweight Dataverse email activities with Graph API-based sending through the BFF. Tracks all outbound communications in a custom `sprk_communication` entity with AssociationResolver pattern for multi-entity linking. Provides a single email pipeline for workspace UI, AI playbooks, background jobs, and future communication channels.

## Problem Statement

1. Dataverse email activities are heavyweight (~200 lines of workarounds in Create Matter wizard)
2. Server-side sync requires per-user mailbox configuration (doesn't scale)
3. Activity permissions are coarse-grained (no entity-level granularity)
4. No reusable email service (each feature implements its own sending logic)
5. Email-to-document archival tightly coupled to Dataverse email activities

## Solution Summary

- **BFF Communication Service**: Single `POST /api/communications/send` endpoint via Graph API (app-only, shared mailbox)
- **Custom Tracking Entity**: `sprk_communication` with fine-grained security (already exists in Dataverse)
- **AssociationResolver Pattern**: Same production-proven pattern as `sprk_event` for multi-entity linking
- **Document Integration**: SPE document attachments + .eml archival
- **AI Playbook Support**: `SendCommunicationToolHandler` as IAiToolHandler
- **Approved Sender Model**: Two-tier (BFF config + Dataverse override) for flexible sender management

## Graduation Criteria

### Functional
- [ ] Single BFF endpoint sends email via Graph API without per-user mailbox config
- [ ] `sprk_communication` record created for every outbound email with correct association
- [ ] Create Matter wizard uses BFF endpoint (Dataverse email activity code removed)
- [ ] Communication model-driven form supports compose and read modes
- [ ] AssociationResolver PCF works on communication form (8 entity types)
- [ ] SPE documents can be attached to outbound emails
- [ ] Sent emails archived as .eml in SPE with `sprk_document` record
- [ ] AI playbooks can send emails via `send_communication` tool
- [ ] Approved sender validation enforces configured mailbox list

### Quality
- [ ] All BFF endpoints return ProblemDetails on error (ADR-019)
- [ ] Endpoint authorization filters on all communication endpoints (ADR-008)
- [ ] Unit tests for CommunicationService, approved sender validation
- [ ] No Graph SDK types leaked above SpeFileStore facade (ADR-007)

### Performance
- [ ] Email send completes within 5 seconds (Graph API round-trip)
- [ ] Attachment download + send within 15 seconds for typical documents (<5MB)

## Scope

### In Scope
- Email sending via Microsoft Graph (app-only, shared mailbox)
- `sprk_communication` entity tracking with AssociationResolver
- Communication model-driven form (compose + read)
- Document attachment support (SPE documents)
- .eml archival to SPE
- Create Matter wizard rewire
- AI tool handler for playbooks
- Two-tier approved sender model (config + Dataverse)

### Out of Scope
- Multi-record association (Phase 6, future)
- Inbound email processing (keep existing EmailEndpoints.cs)
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
| `Mail.Send` app permission | Needs configuration | 1 |
| Shared mailbox | Needs creation/identification | 1 |

---

*Generated by project-pipeline on 2026-02-20*
