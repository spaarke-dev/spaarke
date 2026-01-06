# CLAUDE.md - AI Document Intelligence R1

> **Project**: AI Document Intelligence R1 - Core Infrastructure
> **Status**: Ready for Tasks
> **Last Updated**: 2025-12-25

---

## Project Context

**Purpose**: Establish the core infrastructure foundation for the AI Analysis feature by verifying existing code works, creating missing Dataverse entities, validating environment variables, and testing deployment pipelines.

**R1 Focus**: This is a **verification-first** release. Most code already exists - the goal is to validate it works correctly and fill in any gaps.

**Scope**:
- Verify 10 Dataverse entities exist (create if missing)
- Verify BFF API endpoints function correctly with SSE streaming
- Validate environment variable resolution
- Test AI Foundry Hub connections
- Create security roles and export solution
- Test deployment to external environment
- Create Phase 1 deployment guide

---

## Applicable ADRs

| ADR | Title | Key Constraint |
|-----|-------|----------------|
| [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) | Minimal API Pattern | All endpoints use Minimal API, no Azure Functions |
| [ADR-003](../../docs/adr/ADR-003-lean-authorization.md) | Lean Authorization | Endpoint filters for auth |
| [ADR-007](../../docs/adr/ADR-007-spefilestore-facade.md) | SpeFileStore Facade | All file access through facade |
| [ADR-008](../../docs/adr/ADR-008-authorization-endpoint-filters.md) | Authorization Filters | Per-resource authorization via endpoint filters |
| [ADR-010](../../docs/adr/ADR-010-di-minimalism.md) | DI Minimalism | Max 15 non-framework registrations |
| [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) | AI Architecture | AI features extend BFF API |

---

## Applicable Skills

| Skill | Purpose | When to Use |
|-------|---------|-------------|
| `adr-aware` | Load ADRs based on resource types | Auto-applied during implementation |
| `dataverse-deploy` | Deploy solutions to Dataverse | Entity creation, solution export |
| `task-execute` | Execute POML task files | Daily task execution |
| `script-aware` | Discover reusable scripts | Before writing new automation |

---

## Knowledge Resources

| Document | Path | Purpose |
|----------|------|---------|
| AI Architecture Guide | `docs/guides/SPAARKE-AI-ARCHITECTURE.md` | AI implementation patterns |
| AI Implementation Status | `docs/guides/AI-IMPLEMENTATION-STATUS.md` | Current AI status |
| BFF API CLAUDE.md | `src/server/api/Sprk.Bff.Api/CLAUDE.md` | API-specific context |

---

## Available Scripts

| Script | Purpose |
|--------|---------|
| `scripts/Test-SdapBffApi.ps1` | API validation and testing |
| `scripts/Deploy-PCFWebResources.ps1` | PCF control deployment |
| `scripts/Deploy-CustomPage.ps1` | Custom page deployment |

---

## Existing Code (DO NOT RECREATE)

### BFF API (COMPLETE)
- `src/server/api/Sprk.Bff.Api/Endpoints/Ai/AiAnalysisEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/*.cs` (all services)
- `src/server/api/Sprk.Bff.Api/Models/Ai/*.cs` (all models)

### Unit Tests (COMPLETE)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/*.cs`

### Infrastructure (PARTIAL)
- `infrastructure/bicep/` - Bicep templates exist
- `infrastructure/ai-foundry/` - Templates exist

### Dataverse Entities (STATUS UNKNOWN)
Verification required for these 10 entities:
1. sprk_analysis
2. sprk_analysisaction
3. sprk_analysisskill
4. sprk_analysisknowledge
5. sprk_knowledgedeployment
6. sprk_analysistool
7. sprk_analysisplaybook
8. sprk_analysisworkingversion
9. sprk_analysisemailmetadata
10. sprk_analysischatmessage

---

## Task Type Guidelines

| Existing Status | Task Type | Task Action |
|-----------------|-----------|-------------|
| COMPLETE | Verify only | Confirm working, no changes |
| EXISTS | Verify + Complete | Check state, finish if needed |
| TEMPLATE ONLY | Complete | Finish implementation |
| STATUS UNKNOWN | Verify + Create | Check if exists, create if missing |

---

## Quick Reference

| Action | Resource |
|--------|----------|
| Check project status | `README.md` |
| View implementation plan | `plan.md` |
| See task list | `tasks/TASK-INDEX.md` |
| Check design spec | `spec.md` |
| Review code inventory | `CODE-INVENTORY.md` |

---

*For Claude Code: This is R1 (Core Infrastructure). UI components are in R2, advanced features in R3.*
