# CLAUDE.md — Email Communication Solution R2

> **Project**: email-communication-solution-r2
> **Branch**: work/email-communication-solution-r2

## Project Context

Unified Communication Service: Dataverse-managed mailbox accounts, Graph subscriptions for inbound, OBO for individual send, Graph-based email-to-document archival. Pre-launch — no backward compatibility. Multi-tenant deployment readiness required.

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

**Trigger phrases**: "work on task X", "continue", "next task", "keep going", "resume task X"

**Why**: task-execute ensures knowledge files are loaded, checkpointing occurs, quality gates run, and progress is recoverable.

## Applicable ADRs

| ADR | Key Constraint |
|-----|---------------|
| ADR-001 | Minimal API + BackgroundService; no Azure Functions |
| ADR-002 | Thin plugins < 200 LoC, < 50ms; no HTTP calls |
| ADR-003 | Authorization via IAuthorizationRule; no cached decisions |
| ADR-004 | Job Contract on sdap-jobs queue; idempotent handlers |
| ADR-005 | Flat SPE storage; associations via Dataverse |
| ADR-007 | All SPE through SpeFileStore; no Graph SDK leaks |
| ADR-008 | Endpoint filters for auth; no global middleware |
| ADR-009 | Redis-first caching; short TTL for security data |
| ADR-010 | DI minimalism; feature modules; ≤15 registrations |
| ADR-017 | Job status persistence; 202 + status URL |
| ADR-018 | Feature flags with kill switches |
| ADR-019 | ProblemDetails for all errors; correlation IDs |
| ADR-020 | SemVer; tolerant readers; no silent breaking changes |
| ADR-021 | Fluent UI v9 only; semantic tokens; dark mode |
| ADR-022 | PCF: React 16, platform-provided; no createRoot |

## Key Architecture Decisions

- **Graph subscriptions** replace Server-Side Sync (no SSS)
- **Existing `sdap-jobs` queue** with new `ProcessIncomingCommunication` job type
- **EML archival default ON** for both directions (opt-out via `sprk_ArchiveOutgoingOptIn`/`sprk_ArchiveIncomingOptIn`)
- **Simple binary association matching** (AI augmentation deferred)
- **`@spaarke/auth`** for all client-side authentication
- **Per-environment webhook secret** in config (not Key Vault per-account)
- **Best-effort document archival** — communication record is critical path

## Existing R1 Code to Assess

Critical: Many components already exist from R1. Every phase starts with an assessment task.

**Reuse as-is**: GraphClientFactory, ServiceBusJobProcessor, EmlGenerationService, OBO caching
**Reuse with adapter**: EmailAttachmentProcessor, AttachmentFilterService (via GraphAttachmentAdapter)
**Modify**: ApprovedSenderValidator, CommunicationService.SendAsync()
**Complete/enhance**: GraphSubscriptionManager, IncomingCommunicationProcessor, InboundPollingBackupService
**Delete**: EmailToEmlConverter, EmailToDocumentJobHandler, EmailPollingBackupService, EmailFilterService, EmailRuleSeedService, EmailEndpoints webhook

## File Paths (Quick Reference)

### API
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` (retire webhook routes)
- `src/server/api/Sprk.Bff.Api/Api/Filters/CommunicationAuthorizationFilter.cs`

### Services
- `src/server/api/Sprk.Bff.Api/Services/Communication/` — main service directory
- `src/server/api/Sprk.Bff.Api/Services/Email/` — attachment/filter services (some retiring)
- `src/server/api/Sprk.Bff.Api/Services/Jobs/` — job handlers

### Infrastructure
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs`

### Configuration
- `src/server/api/Sprk.Bff.Api/Configuration/CommunicationOptions.cs`
- `src/server/api/Sprk.Bff.Api/Configuration/EmailProcessingOptions.cs` (consolidate)

### Models
- `src/server/api/Sprk.Bff.Api/Services/Communication/Models/` — 20 model classes

### Tests
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Email/`

### Documentation
- `docs/guides/COMMUNICATION-ADMIN-GUIDE.md`
- `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md`
- `docs/architecture/communication-service-architecture.md`

## Dataverse Entities

| Entity | Status | Notes |
|--------|--------|-------|
| `sprk_communicationaccount` | Deployed | `sprk_desscription` typo; Auth "Apo-Only" typo |
| `sprk_communication` | Deployed | Inbound fields added |
| `sprk_document` | Needs update | Add `sprk_communication` lookup, remove `sprk_email` |
| `sprk_approvedsender` | To delete | Replaced by communicationaccount |
| `sprk_emailprocessingrule` | To delete | Replaced by per-account processingrules |

## Constraints

- `.claude/constraints/api.md` — API endpoint patterns
- `.claude/constraints/auth.md` — auth patterns
- `.claude/constraints/jobs.md` — job processing
- `.claude/constraints/config.md` — configuration
- `.claude/constraints/testing.md` — test requirements
