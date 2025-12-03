# SDAP Architecture Decisions and Guides

This repository segment packages Spaarke’s Architecture Decision Records (ADRs) and two implementation guides used by developers and the AI coding agent. ADRs are short, durable documents that explain **what** we decided, **why**, and **how** to operate those decisions in code and docs. The guides provide concrete steps, prompts, and code patterns to apply them.

## How to use this bundle

- Place the `docs/adr` folder in your repository at `./docs/adr/`. Keep filenames and numbering intact.
- Link ADRs from your design documents in the **Runtime Model**, **Security Model**, and **Operations** sections.
- Treat ADRs as **source of truth** for architectural guardrails. New technology introductions or major deviations must add or supersede an ADR.
- The AI coding agent should read ADRs **before** making automated changes and use the prompts in the guides.

## Index of ADRs (001–012)

- [ADR-001: Minimal API + BackgroundService; do not use Azure Functions](docs/adr/ADR-001-minimal-api-and-workers.md)
  Establishes the single runtime: Minimal API for sync calls, BackgroundService workers for async via Service Bus.

- [ADR-002: Keep Dataverse plugins thin; no orchestration in plugins](docs/adr/ADR-002-no-heavy-plugins.md)
  Plugins do validation/projection only. No HTTP/Graph calls or long-running logic; orchestration sits in the BFF/workers.

- [ADR-003: Lean authorization with two seams (UAC data and file storage)](docs/adr/ADR-003-lean-authorization-seams.md)
  Concrete `AuthorizationService` + small rules; `IAccessDataSource` for Dataverse UAC; `SpeFileStore` for SPE operations.

- [ADR-004: Async job contract and uniform processing](docs/adr/ADR-004-async-job-contract.md)
  One job envelope, idempotent handlers, Polly retries, poison-queue on exhaustion, consistent telemetry.

- [ADR-005: Flat storage model in SharePoint Embedded (SPE)](docs/adr/ADR-005-flat-storage-spe.md)
  Flat storage with metadata-based associations; no deep folder trees; app-mediated access.

- [ADR-006: Prefer PCF controls over legacy JavaScript webresources](docs/adr/ADR-006-prefer-pcf-over-webresources.md)
  Modern, typed UI components and better lifecycle on Power Platform.

- [ADR-007: SPE storage seam minimalism (single focused facade)](docs/adr/ADR-007-spe-storage-seam-minimalism.md)
  Replace generic `IResourceStore` with a concrete `SpeFileStore` facade; no Graph SDK types leak above the facade.

- [ADR-008: Authorization execution model — endpoint filters over global middleware](docs/adr/ADR-008-authorization-endpoint-filters.md)
  One context middleware; enforce resource-level checks via endpoint filters/policy handlers that call `AuthorizationService`.

- [ADR-009: Caching policy — Redis-first with per-request cache](docs/adr/ADR-009-caching-redis-first.md)
  Distributed cache only for cross-request reuse; add L1 only if profiling proves a need; version keys and keep TTLs short.

- [ADR-010: Dependency Injection minimalism and feature modules](docs/adr/ADR-010-di-minimalism.md)
  Register concretes unless a seam is required; feature-module registration; one typed client per upstream; Options for config.

- [ADR-011: Dataset PCF Controls Over Native Subgrids](docs/adr/ADR-011-dataset-pcf-over-subgrids.md)
  Build custom Dataset PCF controls instead of using native Power Platform subgrids for list-based scenarios requiring custom UI, actions, or advanced interactions.

- [ADR-012: Shared Component Library for React/TypeScript Across Modules](docs/adr/ADR-012-shared-component-library.md)
  Create a shared TypeScript/React component library at `src/shared/Spaarke.UI.Components/` for reuse across PCF controls, future SPA, and Office Add-ins.

## Guides

- **[SDAP_Architecture_Simplification_Guide.md](SDAP_Architecture_Simplification_Guide.md)**  
  Consolidates the senior review, problem statements, accepted decisions, document edits, code changes, and AI-agent prompts.

- **[SDAP_Refactor_Playbook_v2.md](SDAP_Refactor_Playbook_v2.md)**  
  Step-by-step refactor plan that operationalizes ADR-007..010 (storage facade, endpoint-filter authorization, Redis-first caching, DI minimalism).

## Enforcement and guardrails

- CI should fail on reintroduction of Azure Functions/WebJobs packages or attributes ([ADR-001]).
- Controllers/handlers must not reference Graph SDK types; storage calls route through `SpeFileStore` ([ADR-007]).
- Authorization is performed via endpoint filters/policy handlers that call `AuthorizationService` ([ADR-008]).
- Redis is the only cross-request cache; do not introduce hybrid L1 unless profiling drives an ADR update ([ADR-009]).
- DI registrations should remain near the minimal block shown in the refactor guide ([ADR-010]).

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
Claude Code skill providing guidance for all 12 ADRs with contextual explanations and suggested fixes.

## AI coding agent usage

- Read ADR-001..012 and the two guides before making changes.
- Run `/adr-check` before committing to validate compliance.
- Apply the prompts in **Guides → SDAP_Architecture_Simplification_Guide.md** and **SDAP_Refactor_Playbook_v2.md**.
- When unsure, prefer: Minimal API + workers, endpoint filters for authorization, Redis-first caching, concrete services with two seams, and the `SpeFileStore` facade for SPE.

## Change log

- 2025-12-02: Implemented hybrid ADR validation (NetArchTest + Claude Code skill) covering all 12 ADRs. Added ADR-011 (Dataset PCF) and ADR-012 (Shared components).
- 2025-09-27: Initial publication of consolidated ADRs (001–010) and guides with README.
