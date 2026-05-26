# BFF Extensions — Governance Constraints

> **Domain**: Adding new features, endpoints, services, modules, or background work to `src/server/api/Sprk.Bff.Api/`
> **Source ADRs**: ADR-001, ADR-007, ADR-008, ADR-010, ADR-013 (refined 2026-05-20), ADR-029 (forthcoming — publish hygiene)
> **Source Assessment**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)
> **Last Updated**: 2026-05-20
> **Status**: Active (binding)

---

## Why This Constraint Exists

The BFF (`Sprk.Bff.Api`) is the single backend for **every** Spaarke client surface (PCFs, Code Pages, External SPA, Office Add-ins, M365 Copilot plugin, Dataverse plugins). It currently hosts ~120 endpoints, ~244 DI registrations across ~30 feature modules, and ~16 hosted services. The 2026-05-20 BFF AI extraction assessment found the codebase is structurally AI-dominant (69% of `Services/` LOC) and operationally well-justified — but it also surfaced **process debt**: multiple projects (R1, R2, R3, Insights Engine, etc.) have each added features without holistic consideration of BFF quality. The 2026-05-19 publish-size jump (65 → 75 MB) and the 20 inbound CRUD→AI direct dependencies are downstream consequences of that pattern.

**This constraint exists to make additions to the BFF deliberate, evidenced, and quality-preserving.** It applies to every PR that adds new endpoints, services, DI registrations, packages, background work, or feature modules to the BFF.

---

## When to Load This File

Load when:
- Adding any new endpoint, service, DI module, or background service to `src/server/api/Sprk.Bff.Api/`
- Adding a new NuGet package reference to `Sprk.Bff.Api.csproj` (or to `Spaarke.Core` / `Spaarke.Dataverse` if consumed by BFF)
- Planning a new AI feature, capability, tool, or playbook
- Designing a new project that will add code to the BFF (any `projects/*` with BFF in scope)
- Reviewing a PR that touches `src/server/api/Sprk.Bff.Api/` and adds material code

---

## MUST Rules

### A. Before ANY BFF Addition (Pre-Merge Checklist — Binding)

Every PR that adds material new code/dependencies to the BFF MUST be able to answer YES to all of these:

1. **MUST** have considered whether the new functionality belongs OUTSIDE the BFF (Azure Functions for out-of-band work per ADR-001; a separate deployable per refined ADR-013 if all four exception criteria are met). The PR description MUST state the placement decision with one-sentence justification — even if the answer is "obviously in BFF."

2. **MUST** cite the relevant ADRs (and any constraints) that bind the design. ADR-001 (Minimal API), ADR-007 (SpeFileStore), ADR-008 (endpoint filters), ADR-010 (DI minimalism), ADR-013 (AI architecture) are the most common. If unsure which apply, load [`.claude/adr/INDEX.md`](../adr/INDEX.md).

3. **MUST** verify the addition does not regress the publish baseline (currently ~60 MB compressed, ~240 entries per [`azure-deployment.md`](azure-deployment.md)). New direct package references are the most common bloat source. Run `dotnet publish --runtime linux-x64` locally and inspect output size before merging if adding packages.

4. **MUST NOT** add a new direct CRUD→AI dependency. If CRUD code (Finance, Workspace, Jobs handlers outside `Services/Ai/`, etc.) needs AI capability, it MUST consume through `Services/Ai/PublicContracts/` facade types — not by injecting `IOpenAiClient`, `IPlaybookService`, or other AI-internal interfaces directly. The 2026-05-20 extraction assessment found 20 existing direct deps; the BFF remediation project is migrating them. New code MUST NOT add to that backlog.

5. **MUST** verify the addition follows feature-module DI conventions per ADR-010 — register through a focused `Add{Feature}Module()` extension, not as a flat blob of `Program.cs` registrations.

### B. New Package References (Binding)

- **MUST** check `dotnet list package --vulnerable --include-transitive` before adding any package. New packages MUST NOT introduce HIGH-severity CVEs into the transitive graph.
- **MUST** verify package version compatibility with the pinned chains documented inline in [`Sprk.Bff.Api.csproj`](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj) — particularly Microsoft.Graph + Kiota (all Kiota packages MUST stay version-matched), Microsoft.Extensions.AI chain, Azure.AI.OpenAI chain.
- **MUST NOT** add pre-release packages (`-beta`, `-rc`, `-preview`) without an inline csproj comment justifying the chain-compat reason. Pre-release packages are a known risk surface; three already exist (`Azure.AI.Projects beta.8`, `Microsoft.Agents.AI rc1`, `Azure.AI.OpenAI 2.8.0-beta.1`).

### C. New Endpoints (Binding per ADR-001, ADR-008)

- **MUST** use Minimal API (`MapPost`/`MapPut`/`MapGet` with explicit handlers) — never MVC controllers
- **MUST** apply endpoint-filter-based authorization (`.AddEndpointFilter<...>()`) per ADR-008 — not global middleware
- **MUST** use `Results.Problem(...)` (RFC 7807) for error responses
- **MUST** apply rate limiting where appropriate (`.RequireRateLimiting("policy-name")`)
- **MUST NOT** add new endpoints to `Program.cs` directly — register through a `Map{Feature}Endpoints` extension method in the appropriate `Api/{Feature}/` folder

### D. New Background Work

- **MUST** use the ADR-004 Job Contract pattern (`IJobHandler<T>`) for new async work — not a free-form `IHostedService`
- **MUST** keep AI-coupled job handlers in `Services/Ai/Jobs/` (post-Outcome E reorganization) — NOT in `Services/Jobs/Handlers/`
- **MUST NOT** add new direct LLM/Azure-OpenAI calls outside `Services/Ai/`

### E. AI Feature Additions

- **MUST** check refined ADR-013 decision criteria before assuming "AI work goes in BFF" — the default is yes, but the criteria are now explicit, not categorical
- **MUST** keep AI synthesis/chat/orchestration in BFF (latency + transactional coupling)
- **MUST** consider whether new AI work is event-driven (sync, scheduled, webhook-triggered) — if yes, it belongs in Azure Functions per ADR-001, not the BFF request pipeline
- **MUST NOT** propose extracting existing AI code into a separate service without a successor ADR amending ADR-013 + fresh extraction-assessment evidence

---

## MUST NOT Rules

- **MUST NOT** add new code to the BFF without considering "should this go elsewhere?" — even one sentence in the PR description satisfies the rule; absence does not
- **MUST NOT** add new direct CRUD→AI dependencies (use `Services/Ai/PublicContracts/` facades)
- **MUST NOT** add packages that introduce known HIGH-severity CVEs
- **MUST NOT** add `<PublishTrimmed>true</PublishTrimmed>` or `<PublishAot>true</PublishAot>` — the BFF's reflection-heavy stack (Graph SDK, Identity.Web, EF, DI, JSON serializers) breaks silently under trimming
- **MUST NOT** publish from `/tmp` or any directory outside `deploy/api-publish/` (per [`azure-deployment.md`](azure-deployment.md) — produces incomplete ~22 MB packages, missing DLLs, silent 404s)
- **MUST NOT** bypass the `bff-deploy` skill for deploys (it enforces hash-verify, health-check window, slot-swap rollback)

---

## Decision Criteria: Does This Belong in the BFF?

Use this table when designing new functionality. **All four "BFF" answers → BFF. Three or four "elsewhere" answers + concrete justification → write a design doc and consult before proceeding.**

| Question | Answer → BFF | Answer → Elsewhere |
|---|---|---|
| Does it have a latency/TTFB budget against BFF state (<500ms)? | YES | NO (consider Functions) |
| Does it write to BFF-managed session/audit/safety state in the same request lifecycle? | YES | NO |
| Does it require retroactive annotation of a streaming response? | YES | NO |
| Is it event-driven (timer, queue, webhook) with no synchronous user wait? | NO | YES (Functions per ADR-001) |
| Is it a thin facade exposing capabilities to EXTERNAL consumers (e.g., MCP for M365 Copilot)? | (consider) | (consider — needs successor ADR per refined ADR-013) |

---

## Project-Level Imperative

If you are scoping a new project that will add code to the BFF, the project's `design.md` MUST include:

1. **Placement justification section**: which new code lives in BFF, which lives in Functions, which (if any) lives in a future separate deployable. Cite the decision criteria above with a one-sentence answer per row for each major component.
2. **Size impact estimate**: rough estimate of compressed publish-size delta. If >2 MB, requires explicit owner ack before merging.
3. **Boundary preservation statement**: confirmation that new code follows facade patterns where applicable (no new direct CRUD→AI deps); follows feature-module DI; follows endpoint-filter auth.
4. **Reference to this file**: cite `.claude/constraints/bff-extensions.md` in the project's design.md as a binding constraint.

This is non-negotiable. Projects that skip these sections will be flagged in code review.

---

## Quick Reference

```csharp
// ✅ CORRECT: CRUD code (Workspace) consuming AI through facade
public class BriefingService
{
    private readonly IBriefingAi _ai;  // facade in Services/Ai/PublicContracts/
    public BriefingService(IBriefingAi ai) { _ai = ai; }
}

// ❌ WRONG: CRUD code (Workspace) directly injecting AI-internal type
public class BriefingService
{
    private readonly IOpenAiClient _openAi;  // AI-internal; do not inject here
    public BriefingService(IOpenAiClient openAi) { _openAi = openAi; }
}

// ✅ CORRECT: New endpoint via extension method
public static class MyFeatureEndpoints
{
    public static IEndpointRouteBuilder MapMyFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/my-feature", Handle)
           .AddEndpointFilter<MyAuthFilter>()
           .RequireAuthorization()
           .RequireRateLimiting("standard");
        return app;
    }
}

// ❌ WRONG: Endpoint directly in Program.cs
app.MapPost("/api/my-feature", async (HttpContext ctx, ...) => { ... });  // do not do this
```

---

## Pattern Files (Complete Examples)

- [`.claude/patterns/api/endpoint-definition.md`](../patterns/api/endpoint-definition.md) — how to add endpoints
- [`.claude/patterns/api/service-registration.md`](../patterns/api/service-registration.md) — feature-module DI patterns
- [`.claude/patterns/api/endpoint-filters.md`](../patterns/api/endpoint-filters.md) — authorization patterns
- [`.claude/patterns/api/background-workers.md`](../patterns/api/background-workers.md) — BackgroundService and Job Contract patterns

---

## Source ADRs (Full Context)

- [ADR-001 Minimal API and Workers](../../docs/adr/ADR-001-minimal-api-and-workers.md) — single BFF + Functions for narrow out-of-band
- [ADR-007 SpeFileStore Facade](../../docs/adr/ADR-007-spefilestore.md) — file access via facade
- [ADR-008 Endpoint Filters](../../docs/adr/ADR-008-endpoint-filters.md) — authorization model
- [ADR-010 DI Minimalism](../../docs/adr/ADR-010-di-minimalism.md) — feature-module DI
- [ADR-013 AI Architecture (refined 2026-05-20)](../../docs/adr/ADR-013-ai-architecture.md) — decision criteria for AI placement; supersedes the prior categorical rejection of separate AI services
- ADR-029 (forthcoming, from `projects/sdap-bff-api-remediation-fix/`) — publish hygiene + CI guards

---

## Related

- [`.claude/constraints/api.md`](api.md) — endpoint-level constraints (load alongside this file)
- [`.claude/constraints/azure-deployment.md`](azure-deployment.md) — deploy-time constraints (publish location, baseline size)
- [`.claude/constraints/ai.md`](ai.md) — AI-specific MUST/MUST NOT
- [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — the evidence base behind this constraint

---

**Lines**: ~160
**Purpose**: Make additions to the BFF deliberate. Prevent the "many projects each adding without holistic consideration" pattern from continuing.
