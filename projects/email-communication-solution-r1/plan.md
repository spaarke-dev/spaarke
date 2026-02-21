# Email Communication Solution R1 — Implementation Plan

## Executive Summary

- **Purpose**: Replace Dataverse email activities with unified Communication Service via Graph API through BFF
- **Scope**: 5 phases — BFF endpoint, entity tracking, communication app, attachments/archival, playbook integration
- **Estimated Effort**: ~120 hours across 35 tasks
- **Critical Path**: 001 → 003 → 006 → 010 → 011 → 030 → 032 → 040 → 090

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
| Sender management | Two-tier: BFF config + Dataverse `sprk_approvedsender` | Phase 1 works without Dataverse entity |
| Status field | Standard Dataverse `statuscode` (not custom choice) | Use actual values: Draft=1, Send=659490002, Failed=659490004 |
| Regarding fields | `sprk_regardingorganization` + `sprk_regardingperson` | Differs from design (not account/contact) |
| Type field | `sprk_communiationtype` (typo in Dataverse) | Use actual logical name |
| Retry behavior | Fail immediately, return error | No background retry mechanism |
| fromMailbox | Any user can specify | BFF validates against approved sender list |

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

| Phase | Focus | Tasks | Estimated Hours | Parallel Groups |
|-------|-------|-------|----------------|-----------------|
| 1: BFF Email Service | Core send endpoint + wizard rewire | 001-008 | 32h | A (001,002), B (005,006), C (007,008) |
| 2: Entity + Tracking | Dataverse record creation + associations | 010-016 | 24h | D (012,013), E (014,015) |
| 3: Communication App | Model-driven form + views | 020-025 | 20h | F (020,021), G (023,024) |
| 4: Attachments + Archival | SPE integration + .eml | 030-038 | 28h | H (031,032), I (034,035), J (037,038) |
| 5: Playbook Integration | AI tool handler | 040-043 | 12h | K (041,042) |
| Wrap-up | Project completion | 090 | 4h | — |

### Critical Path

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

## Dependencies

### External
- Microsoft Graph API (`Mail.Send` application permission)
- Shared mailbox in Azure AD (Exchange Online license)
- Application access policy (scope Mail.Send to specific mailbox)

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

### UI Tests (Phase 3)
- Communication form compose mode
- AssociationResolver entity selection
- Send button executes BFF call
- Communication views filter correctly

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

## Next Steps

1. Review this plan and task index
2. Begin task execution with `work on task 001`
3. Tasks 001 and 002 can run in parallel (Group A)

---

*Generated by project-pipeline on 2026-02-20*
