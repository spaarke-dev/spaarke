# SDAP Instructions for Claude Code (Self‑Contained)

This file is the **single source of truth** for AI coding work on Spaarke SDAP. It embeds rules, prompts, checklists, and CI expectations so the agent can operate autonomously.

---

## 1) Load Order (Mandatory)
1. Open this file first.
2. Open the current task file under `docs/dev/tasks/` (Task A–G).
3. Open the ADRs below **by absolute path**.

## 2) Binding Knowledge Sources (Absolute Paths)
- ADR‑001 Minimal API & Workers: `C:\code_files\spaarke\docs\adr\ADR-001-minimal-api-and-workers.md`
- ADR‑002 No Heavy Plugins: `C:\code_files\spaarke\docs\adr\ADR-002-no-heavy-plugins.md`
- ADR‑003 Lean Authorization Seams: `C:\code_files\spaarke\docs\adr\ADR-003-lean-authorization-seams.md`
- ADR‑004 Async Job Contract: `C:\code_files\spaarke\docs\adr\ADR-004-async-job-contract.md`
- ADR‑005 Flat Storage (SPE): `C:\code_files\spaarke\docs\adr\ADR-005-flat-storage-spe.md`
- ADR‑006 Prefer PCF over WebResources: `C:\code_files\spaarke\docs\adr\ADR-006-prefer-pcf-over-webresources.md`
- ADR‑007 SPE Storage Seam Minimalism: `C:\code_files\spaarke\docs\adr\ADR-007-spe-storage-seam-minimalism.md`
- ADR‑008 Authorization via Endpoint Filters: `C:\code_files\spaarke\docs\adr\ADR-008-authorization-endpoint-filters.md`
- ADR‑009 Caching (Redis‑first): `C:\code_files\spaarke\docs\adr\ADR-009-caching-redis-first.md`
- ADR‑010 DI Minimalism: `C:\code_files\spaarke\docs\adr\ADR-010-di-minimalism.md`
- Code review (Spe.Bff.Api): `C:\code_files\spaarke\docs\api\Code Review Spe.Bff.Api.md`
- SDAP Architecture Simplification Guide: `C:\code_files\spaarke\docs\guides\SDAP_Architecture_Simplification_Guide.md`

**Conflict rule:** ADRs > Simplification Guide > code comments. If a change requires breaking an ADR, pause and propose a new ADR.

## 3) Global Guardrails (MUST)
- **Runtime:** Single ASP.NET Core app with Minimal API + `BackgroundService`. No Azure Functions/Durable Functions.
- **Seams:** SPE/Graph only inside `SpeFileStore` (no Graph types outside). Authorization only via endpoint filters + `AuthorizationService` + small `IAuthorizationRule` units.
- **Caching:** Redis for cross‑request inputs (e.g., UAC snapshots) + a scoped per‑request memoizer. **Never cache authorization decisions.**
- **Plugins:** Thin synchronous validators/projections only; enqueue work to Service Bus for I/O or long tasks.
- **DTO discipline:** Seams exchange DTOs only—never Graph/Dataverse entities.
- **Errors:** Shape as RFC7807 `ProblemDetails`. No stack traces in prod. Include correlation ID.
- **Async:** Every I/O method accepts `CancellationToken`. Retries only at integration edges via Polly (Graph/Service Bus); never stack retries.
- **Observability:** Structured logs at boundaries; correlation in/out; traces around external calls.
- **Minimal public API:** Prefer `internal` members; keep classes small; no speculative interfaces.

## 4) Agent Run Loop (execute verbatim)
1. Read this file and the current task `.md`.
2. Read the referenced ADRs for the task.
3. **Write or update tests first** to pin behavior.
4. Implement the smallest code change to make tests pass.
5. Run formatters/analyzers; fix issues.
6. Run ADR policy checks; fix violations.
7. Output only: unified git diffs (changed files) and **one** concise commit message.
8. Print `NEXT: <next task>` and stop.

## 5) Output Contract (strict)
- No explanations. Provide only file paths, unified diffs, and a single commit message: `"<task-id>: <short action> (ADR-###, ADR-###)"`.

## 6) Self‑Review Checklist (block merge if any fail)
- Any Graph SDK type escaping `SpeFileStore`? (Fail if yes)
- Any cross‑request `IMemoryCache` or hybrid L1? (Fail if yes; use Redis)
- Resource checks only in filters/rules, not middleware?
- All I/O methods accept `CancellationToken`?
- Errors shaped as `ProblemDetails` with correlation ID?
- New code covered by unit/integration tests and passing locally?
- Analyzers/formatter clean? ADR policy script green?
- Introduced any forbidden tech (Functions/Durable)? If yes, remove or author ADR first.

## 7) CI Expectations
- `dotnet build -warnaserror`
- `dotnet format --verify-no-changes`
- Unit + integration tests pass
- `scripts/adr_policy_check.ps1` passes
- No secrets committed

## 8) Senior “Vibe Coding” Norms
- Minimal public surface, explicit naming, small pure functions, guard clauses.
- Avoid premature abstractions; create interfaces only for true seams.
- Centralize constants (routes/claims/cache keys/metric names).
- Stream big payloads; avoid LINQ in hot paths; measure before optimizing.
- Keep commit size small (100–400 LoC). Delete dead code opportunistically.

## 9) Start Here
Proceed to `docs/dev/tasks/TaskA_DI_Pipeline.md`. After completing a task, follow the “Conclusion / Next Task” section at the bottom of that file.
