# SDAP Architecture Decisions and Guides

This repository segment packages Spaarke’s Architecture Decision Records (ADRs) and two implementation guides used by developers and the AI coding agent. ADRs are short, durable documents that explain **what** we decided, **why**, and **how** to operate those decisions in code and docs. The guides provide concrete steps, prompts, and code patterns to apply them.

## How to use this bundle

- In this repository, ADRs live at `docs/reference/adr/`.
- If you copy this bundle to another repo, place it at `./docs/adr/` (or update links consistently). Keep filenames and numbering intact.
- Link ADRs from your design documents in the **Runtime Model**, **Security Model**, and **Operations** sections.
- Treat ADRs as **source of truth** for architectural guardrails. New technology introductions or major deviations must add or supersede an ADR.
- The AI coding agent should read ADRs **before** making automated changes and use the prompts in the guides.

## Index of ADRs (001–020)

- [ADR-001: Minimal API as the single BFF runtime](./ADR-001-minimal-api-and-workers.md)
  Establishes the single BFF runtime: Minimal API for sync calls. Where background work runs is [ADR-052](./ADR-052-workload-placement.md); inside the BFF, queue work follows ADR-004 and scheduled work ADR-036 (Amendment A1, 2026-09-12).

- [ADR-002: Keep Dataverse plugins thin; no orchestration in plugins](./ADR-002-no-heavy-plugins.md)
  Plugins do validation/projection only. No HTTP/Graph calls or long-running logic; orchestration sits in the BFF/workers.

- [ADR-003: Lean authorization with two seams (UAC data and file storage)](./ADR-003-lean-authorization-seams.md)
  Concrete `AuthorizationService` + small rules; `IAccessDataSource` for Dataverse UAC; `SpeFileStore` for SPE operations.

- [ADR-004: Async job contract and uniform processing](./ADR-004-async-job-contract.md)
  One job envelope, idempotent handlers, Polly retries, poison-queue on exhaustion, consistent telemetry.

- [ADR-005: Flat storage model in SharePoint Embedded (SPE)](./ADR-005-flat-storage-spe.md)
  Flat storage with metadata-based associations; no deep folder trees; app-mediated access.

- [ADR-006: Prefer PCF controls over legacy JavaScript webresources](./ADR-006-prefer-pcf-over-webresources.md)
  Modern, typed UI components and better lifecycle on Power Platform.

- [ADR-007: SPE storage seam minimalism (single focused facade)](./ADR-007-spe-storage-seam-minimalism.md)
  Replace generic `IResourceStore` with a concrete `SpeFileStore` facade; no Graph SDK types leak above the facade.

- [ADR-008: Authorization execution model — endpoint filters over global middleware](./ADR-008-authorization-endpoint-filters.md)
  One context middleware; enforce resource-level checks via endpoint filters/policy handlers that call `AuthorizationService`.

- [ADR-009: Caching policy — Redis-first with per-request cache](./ADR-009-caching-redis-first.md)
  Distributed cache only for cross-request reuse; add L1 only if profiling proves a need; version keys and keep TTLs short.

- [ADR-010: Dependency Injection minimalism and feature modules](./ADR-010-di-minimalism.md)
  Register concretes unless a seam is required; feature-module registration; one typed client per upstream; Options for config.

- [ADR-011: Dataset PCF Controls Over Native Subgrids](./ADR-011-dataset-pcf-over-subgrids.md)
  Build custom Dataset PCF controls instead of using native Power Platform subgrids for list-based scenarios requiring custom UI, actions, or advanced interactions.

- [ADR-012: Shared Component Library for React/TypeScript Across Modules](./ADR-012-shared-component-library.md)
  Create a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` for reuse across PCF controls, future SPA, and Office Add-ins.

- [ADR-013: AI Architecture for Azure OpenAI, AI Search, and Document Intelligence](./ADR-013-ai-architecture.md)
  Extends Sprk.Bff.Api with AI capabilities following established ADR patterns; covers chat, semantic search, document indexing for Model 1 and Model 2 deployments.

- [ADR-014: AI Caching and Reuse Policy](./ADR-014-ai-caching-and-reuse-policy.md)
  AI-specific caching/reuse rules (keys, TTLs, tenant scoping) layered on ADR-009.

- [ADR-015: AI Data Governance (PII, Retention, Redaction, Logging)](./ADR-015-ai-data-governance.md)
  Rules for minimization, safe logging/telemetry, and retention for AI inputs/outputs.

- [ADR-016: AI Cost, Rate Limits, and Backpressure Strategy](./ADR-016-ai-cost-rate-limit-and-backpressure.md)
  Standard approach for throttling, bounded concurrency, and graceful degradation under load.

- [ADR-017: Async Job Status, Persistence, and Client Contract](./ADR-017-async-job-status-and-persistence.md)
  Uniform job status contract for clients/operators, complementing ADR-004.

- [ADR-018: Feature Flags and Kill Switches (Server + Client)](./ADR-018-feature-flags-and-kill-switches.md)
  Standard feature-flag approach for safe rollouts and rapid disablement.

- [ADR-019: API Errors and ProblemDetails Standard (including SSE)](./ADR-019-api-errors-and-problemdetails.md)
  Uniform error responses for HTTP and SSE streaming endpoints.

- [ADR-020: Versioning Strategy (APIs, Jobs, and Client Packages)](./ADR-020-versioning-strategy-apis-jobs-client-packages.md)
  SemVer and compatibility rules across endpoints, job payloads, and shared packages.

## Later ADRs (selected — the full list is [INDEX.md](./INDEX.md))

- [ADR-052: Workload placement — the BFF, Azure Functions, or Container Apps Jobs](./ADR-052-workload-placement.md)
  Where background, scheduled and event-driven work runs, decided per workload on stated signals and costs. The only full statement of the placement rule.

## Guides

- **[SDAP_Architecture_Simplification_Guide.md](SDAP_Architecture_Simplification_Guide.md)**  
  Consolidates the senior review, problem statements, accepted decisions, document edits, code changes, and AI-agent prompts.

- **[SDAP_Refactor_Playbook_v2.md](SDAP_Refactor_Playbook_v2.md)**  
  Step-by-step refactor plan that operationalizes ADR-007..010 (storage facade, endpoint-filter authorization, Redis-first caching, DI minimalism).

## Enforcement and guardrails

- CI fails on Azure Functions, WebJobs or Durable Task packages, or Function-attributed methods, **inside the BFF assembly** ([ADR-001]). Where background work runs is decided per workload ([ADR-052]); `WorkloadPlacementDocDriftTests` fails on contradicting placement phrasings.
- Controllers/handlers must not reference Graph SDK types; storage calls route through `SpeFileStore` ([ADR-007]).
- Authorization is performed via endpoint filters/policy handlers that call `AuthorizationService` ([ADR-008]).
- Redis is the only cross-request cache; do not introduce hybrid L1 unless profiling drives an ADR update ([ADR-009]).
- DI registrations should remain near the minimal block shown in the refactor guide ([ADR-010]).

## ADR structure for AI-directed coding (Recommended)

To make ADRs maximally useful for AI-assisted implementation and review, each ADR SHOULD include:

- **Decision rules**: concise “Do/Don’t” rules (tables are preferred)
- **Scope and non-goals**: what this ADR applies to, and what it explicitly does not
- **Operationalization**: where this shows up in code (key files/types) and how to enforce it
- **Failure modes**: what can go wrong if violated, and what telemetry/tests catch it
- **Compliance checklist**: short, reviewable checklist items

## Workflow for architectural change

- Propose a new ADR (or an update that supersedes an existing ADR) under `docs/adr/` using the ADR template in the simplification guide.
- Discuss impacts in PR description and label with `architecture`.
- Once merged, update the **Runtime Model** section of design docs and adjust code where the ADR specifies “Operationalization.”

## Validating ADR Compliance

**📋 Process Documentation:** See [ADR Validation Process](ADR-VALIDATION-PROCESS.md) for complete workflow, tools, issue tracking, and maintenance procedures.

### Quick Reference

**Automated Validation (CI/CD):**
```bash
dotnet test tests/Spaarke.ArchTests/
```
Runs automatically on every PR and push to master. Validates 6 core ADRs (001, 002, 007, 008, 009, 010) via NetArchTest.

**Interactive Validation (Local):**
```bash
/adr-check
```
Claude Code skill providing guidance for the ADR set with contextual explanations and suggested fixes.

## AI coding agent usage

- Read ADR-001..012 and the two guides before making changes.
- Run `/adr-check` before committing to validate compliance.
- Apply the prompts in **Guides → SDAP_Architecture_Simplification_Guide.md** and **SDAP_Refactor_Playbook_v2.md**.
- When unsure, prefer: Minimal API + workers, endpoint filters for authorization, Redis-first caching, concrete services with two seams, and the `SpeFileStore` facade for SPE.

## Change log

- 2026-09-12: ADR-001 amended (A1) — Minimal API as the single BFF runtime; where background work runs moved to the new ADR-052 (listed under Later ADRs).
- 2025-12-12: Added ADR-014..ADR-020 (AI caching, governance, backpressure, job status, feature flags, error standards, versioning).
- 2025-12-02: Implemented hybrid ADR validation (NetArchTest + Claude Code skill) covering the ADR set. Added ADR-011 (Dataset PCF) and ADR-012 (Shared components).
- 2025-09-27: Initial publication of consolidated ADRs (001–010) and guides with README.
