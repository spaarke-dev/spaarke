# CLAUDE.md — Email Communication Solution R1

## Project Status

- **Status**: In Progress
- **Phase**: 1 (BFF Email Service)
- **Branch**: `work/email-communication-solution-r1`
- **Tasks**: 0/35 completed
- **Next Action**: Execute task 001

## Quick Reference

### Key Files

| File | Purpose |
|------|---------|
| [spec.md](spec.md) | Full specification with requirements and ADR constraints |
| [plan.md](plan.md) | Implementation plan with WBS and parallel groups |
| [design.md](design.md) | Original design document |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task registry with dependencies and parallel groups |
| [current-task.md](current-task.md) | Active task state (for context recovery) |
| [../../docs/data-model/sprk_communication-data-schema.md](../../docs/data-model/sprk_communication-data-schema.md) | Actual Dataverse entity schema (SOURCE OF TRUTH) |

### Project Metadata

| Key | Value |
|-----|-------|
| Entity | `sprk_communication` (ALREADY EXISTS in Dataverse) |
| Type field | `sprk_communiationtype` (note: typo is intentional — actual Dataverse name) |
| Status field | `statuscode` (standard Dataverse, NOT custom choice) |
| Status values | Draft=1, Queued=659490001, Send=659490002, Delivered=659490003, Failed=659490004 |
| Direction values | Incoming=100000000, Outgoing=100000001 |
| Regarding fields | `sprk_regardingorganization` (not account), `sprk_regardingperson` (not contact) |
| Graph auth | App-only via `GraphClientFactory.ForApp()` |
| Sender model | Two-tier: BFF config (`CommunicationOptions.ApprovedSenders[]`) + Dataverse `sprk_approvedsender` |

### Context Loading Rules

| When | Load |
|------|------|
| Starting any task | This file + current-task.md + task POML file |
| API endpoint work | `.claude/constraints/api.md` + `.claude/patterns/api/endpoint-definition.md` |
| Graph sendMail work | `Infrastructure/Graph/GraphClientFactory.cs` + `Services/Ai/Nodes/SendEmailNodeExecutor.cs` |
| Dataverse record work | `.claude/patterns/dataverse/entity-operations.md` + `docs/data-model/sprk_communication-data-schema.md` |
| Association/regarding | `.claude/patterns/dataverse/polymorphic-resolver.md` + `Models/RegardingRecordType.cs` |
| Attachment/archival | `.claude/constraints/data.md` + `Services/Spe/SpeFileStore.cs` |
| AI tool work | `Services/Ai/Tools/DataverseUpdateToolHandler.cs` (example pattern) |
| Authorization | `.claude/patterns/api/endpoint-filters.md` |

## Task Execution Protocol

When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

**Trigger phrases**: "work on task X", "continue", "next task", "keep going", "resume task X"

**Why**: Ensures knowledge files loaded, checkpointing occurs, quality gates run, progress is recoverable.

## Key Technical Constraints

### Entity Schema (USE ACTUAL NAMES)
- Type: `sprk_communiationtype` (NOT `sprk_communicationtype`)
- Status: `statuscode` (NOT `sprk_communicationstatus`)
- Account lookup: `sprk_regardingorganization` (NOT `sprk_regardingaccount`)
- Contact lookup: `sprk_regardingperson` (NOT `sprk_regardingcontact`)
- Direction: Incoming=100000000, Outgoing=100000001 (NOT Outbound/Inbound)

### BFF Service
- Register as concrete: `services.AddSingleton<CommunicationService>()`
- Use feature module: `AddCommunicationModule()`
- Endpoint group: `/api/communications`
- Authorization: Endpoint filter (not global middleware)
- Errors: ProblemDetails with `errorCode` extension

### Approved Senders
- Phase 1: `CommunicationOptions.ApprovedSenders[]` in appsettings.json
- Phase 2+: Merge with `sprk_approvedsender` Dataverse entity
- Any authenticated user can specify fromMailbox
- Validate against approved list; return `INVALID_SENDER` on mismatch

### Graph Integration
- Use `GraphClientFactory.ForApp()` for shared mailbox
- App-only `Mail.Send` application permission
- On failure: return error immediately (no retry)
- Attachment limits: 150 max, 35MB total

## Applicable ADRs

| ADR | Key Rule |
|-----|----------|
| ADR-001 | Minimal API endpoints. No Azure Functions. |
| ADR-007 | SpeFileStore facade for all SPE operations. |
| ADR-008 | Endpoint filters for authorization. |
| ADR-010 | Concrete DI. Feature module. ≤15 registrations. |
| ADR-013 | IAiToolHandler for AI tool integration. |
| ADR-019 | ProblemDetails for all errors. |

## Decisions Made

| Decision | Choice | Date |
|----------|--------|------|
| Phase scope | All 5 phases | 2026-02-20 |
| Wizard changes | Include in this project (workspace worktree) | 2026-02-20 |
| Retry strategy | Fail immediately, return error | 2026-02-20 |
| Sender control | BFF config + Dataverse override (two-tier) | 2026-02-20 |
| fromMailbox access | Any authenticated user can specify | 2026-02-20 |
| sprk_sentby type | Lookup (systemuser) — all callers resolve to systemuser | 2026-02-20 |

---

*Project-specific AI context. Last updated: 2026-02-20*
