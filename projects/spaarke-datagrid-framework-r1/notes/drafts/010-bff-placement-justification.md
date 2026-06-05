# 010 — BFF Placement Justification for `Api/Dataverse/` + `Services/Dataverse/` (Phase B)

> **Source ADRs**: ADR-001 (Minimal API), ADR-007 (SpeFileStore facade pattern), ADR-008 (endpoint filters), ADR-010 (DI minimalism), ADR-013 (refined 2026-05-20 — AI placement criteria), ADR-019 (ProblemDetails), ADR-028 (Spaarke Auth v2), ADR-029 (BFF publish hygiene)
> **Source constraint**: [`.claude/constraints/bff-extensions.md`](../../../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist
> **Source assessment**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md)
> **Scope of addition**: 5 endpoints (`/api/dataverse/savedquery/{id}`, `/api/dataverse/savedqueries/{entity}`, `/api/dataverse/metadata/{entity}`, `/api/dataverse/fetch`, `/api/dataverse/record/{entity}/{id}`) + supporting services + a canonical authorization filter
> **Tasks blocked by this doc**: 011, 012, 013, 014, 015, 016, 017
> **Status**: Draft (ready for review; will be copy-pasted into the Phase B PR description per FR-BFF-08)

---

## 1. Plain-English Summary

We are adding **5 thin Dataverse-passthrough HTTP endpoints** to the BFF so that non-MDA Spaarke clients (Code Pages, SpaarkeAi workspace widgets, MCP App widgets, future external SPAs) can read `savedquery` metadata, entity metadata, FetchXML results, and individual records via the same authentication contract (`@spaarke/auth.authenticatedFetch` per ADR-028) they already use for every other BFF call. Inside MDA, callers continue to use `Xrm.WebApi` via `XrmDataverseClient`. The two impls share the `IDataverseClient` contract — host-swap is zero per-entity TypeScript code.

No business logic. No AI. No write operations in R1. Pure projection + cache + auth.

---

## 2. Decision Criteria Table (per `bff-extensions.md`)

> Constraint quotes the decision criteria table verbatim. Answers below cite the row per criterion.

| Criterion (`bff-extensions.md` §Decision Criteria) | Answer | One-sentence justification |
|---|---|---|
| Does it have a latency/TTFB budget against BFF state (<500ms)? | **BFF** | FR-BFF-04 requires <500ms p50 roundtrip; achievable only with server-side `IDistributedCache` (Redis) sitting next to the Dataverse `ServiceClient` pool. |
| Does it write to BFF-managed session/audit/safety state in the same request lifecycle? | **BFF** | Per-call privilege check is logged to the BFF audit pipeline (`AuditEnrichmentMiddleware`) alongside `oid`/`appid`/`correlationId`; this is the same observability surface every other BFF endpoint uses. |
| Does it require retroactive annotation of a streaming response? | N/A | Endpoints are request/response JSON, not SSE. |
| Is it event-driven (timer, queue, webhook) with no synchronous user wait? | **No → BFF** | All 5 endpoints are invoked synchronously by the grid framework during user-driven view rendering, filter changes, and lazy paging; no event-driven trigger exists. ADR-001 keeps this in BFF (Functions is the wrong placement). |
| Is it a thin facade exposing capabilities to EXTERNAL consumers (e.g., MCP for M365 Copilot)? | **No** | Consumers are Spaarke-owned Code Pages, PCF controls, and workspace widgets. No external/3rd-party consumer is in scope for R1. |

**Decision**: All applicable criteria point to BFF. Belongs in BFF.

---

## 3. WHY here? (the affirmative case)

1. **Cross-cutting Dataverse projection used by 3+ surfaces (and growing)** — Custom Pages (`sprk_kpiassessmentspage`, `sprk_invoicespage`), SpaarkeAi workspace widgets (Calendar widget per FR-MIG-05, future entity-aware widgets), MCP App widgets, future external SPAs. Each would otherwise replicate auth + Dataverse client + cache. One BFF surface, N clients.
2. **Tight coupling to existing `ServiceClient` infrastructure** — `Spaarke.Dataverse` already pins `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32; `DataverseServiceClientImpl` singleton is registered in `GraphModule.cs:46-52` with managed identity per ADR-028. The new endpoints reuse this pool. No new connection management.
3. **Cache effectiveness collapse outside BFF** — Per design.md §9: one server-side `IDistributedCache` (existing Redis instance) yields a system-wide >95% hit rate after warmup for savedquery (1h TTL) and entity metadata (6h TTL). Per-browser caches do not — the same view is opened thousands of times across users; each browser would cold-miss.
4. **MSAL surface reduction** — Outside-MDA browsers calling Dataverse directly require per-tab MSAL provisioning for `dynamics.com` scope, doubling token-acquisition complexity. BFF passthrough lets clients keep one scope (the BFF API scope via `@spaarke/auth.authenticatedFetch`).
5. **Authorization filter is shared infrastructure** — A single `DataverseAuthorizationFilter` (designed in `010-authorization-filter-shape.md`) protects all 5 endpoints uniformly; the alternative would be reimplementing the privilege-check logic in every client surface (incorrectly, eventually).

## 4. WHY not elsewhere?

| Alternative | Verdict | Reason |
|---|---|---|
| Azure Functions (out-of-band per ADR-001) | **No** | All 5 endpoints are synchronous, user-driven, latency-bounded; ADR-001 explicitly puts this class of work in BFF. |
| Separate microservice / deployable per refined ADR-013 | **No** | Refined ADR-013 (2026-05-20) requires all four extraction-exception criteria to be met; none are. The projection logic is thin (~300-500 LOC est.), stateless, reuses existing managed identity + Redis infra. No independent deployment cadence. No separate team. No isolation requirement. |
| Direct browser → Dataverse Web API call (no BFF) | **No** | Doubles client auth surface (extra MSAL scope per tab), no cross-client cache, no centralized authorization filter, no centralized telemetry, exposes the Dataverse Web API to network-level mistakes (e.g., browser sends `$expand` causing a slow query and a 30s grid stall). |
| Inside `Spaarke.Dataverse` shared library directly (no BFF endpoint) | **N/A** | That's the wrong layer — `Spaarke.Dataverse` is consumed by .NET callers (BFF, plugins). The clients here are TypeScript browser code. The BFF endpoint IS the layer that bridges them. |

**Decision recorded**: Place in `src/server/api/Sprk.Bff.Api/Api/Dataverse/` and `src/server/api/Sprk.Bff.Api/Services/Dataverse/` (the `Services/Dataverse/` folder already exists — tasks 011-014 will extend it).

---

## 5. `bff-extensions.md` Pre-Merge Checklist — Item-by-Item

> Every checklist item must have an explicit answer (no skipping) per acceptance criterion #1 of this task.

### §Why this constraint exists — operative implication

The 2026-05-19 publish-size jump (65 → 75+ MB) and the 20 inbound CRUD→AI direct deps are the failure mode this constraint prevents from continuing. **Answer**: This addition is ~150-250KB compiled (§7 below); CRUD→AI facades are N/A (no AI in scope); we are deliberate, evidenced, and quality-preserving.

### §When to Load This File — applicability

- Adding new endpoint to `src/server/api/Sprk.Bff.Api/`? **Yes** — 5 endpoints in `Api/Dataverse/`.
- Adding new service to `src/server/api/Sprk.Bff.Api/`? **Yes** — `IDataverseProjectionService` or similar in `Services/Dataverse/` (final naming in task 011).
- Adding new DI module? **Yes** — `Infrastructure/DI/DataverseProjectionModule.cs` extension method (per ADR-010 feature-module pattern).
- Adding new background service? **No.**
- Adding new NuGet package reference to `Sprk.Bff.Api.csproj`? **No** — see §A.2 below.
- Planning a new AI feature? **No.**

### §MUST Rules — A. Pre-Merge Checklist (Binding)

#### A.1 — Considered whether functionality belongs OUTSIDE the BFF? Placement decision stated?

**YES.** Answered in §3 and §4 above. Decision: BFF (cross-cutting projection, latency budget, cache effectiveness, MSAL surface reduction, shared authorization filter). Alternatives (Functions, separate service, browser-direct) explicitly considered and rejected with reasons.

#### A.2 — Cite the relevant ADRs that bind the design?

**YES.** Bound by:
- **ADR-001** — Minimal API with explicit handlers; not MVC controllers.
- **ADR-007 (pattern)** — `SpeFileStore`-style facade: endpoints inject the concrete projection service, which wraps the Dataverse `ServiceClient`.
- **ADR-008** — Endpoint-filter authorization (`DataverseAuthorizationFilter`); not global middleware. See `010-authorization-filter-shape.md`.
- **ADR-010** — DI registered via a focused `AddDataverseProjectionModule()` extension; concretes preferred unless testing seam is required.
- **ADR-013** (refined 2026-05-20) — N/A; not an AI feature.
- **ADR-019** — All errors as `ProblemDetails` (RFC 7807) with stable `errorCode` extension and correlation ID.
- **ADR-028** — Clients use `@spaarke/auth.authenticatedFetch` (managed by Spaarke Auth v2). Server side validates JWT via standard middleware; OBO exchange to Dataverse uses existing `DataverseAccessDataSource` OBO path or app-only `ServiceClient` with `CallerId` impersonation (final choice in task 011).
- **ADR-029** — Publish hygiene maintained: framework-dependent linux-x64, sourcemap exclusion, no new HIGH-severity CVE, size baseline ratchet honored.

#### A.3 — Does the addition regress the publish baseline?

**NO.** Per §7 size estimate below: ~150-250KB compiled, well under the +2MB owner-ack threshold (`bff-extensions.md` §Project-Level Imperative #2). No new NuGet packages (§A.4-style verification below). Reuses existing `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32 (already in `Spaarke.Dataverse.csproj`), existing `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.1 for `IDistributedCache`, existing `System.Xml.Linq` (in-box) for FetchXML parse.

**Verification step (task 016 owner)**: run `dotnet publish --runtime linux-x64 --output deploy/api-publish/` before merging Phase B and diff the compressed package size vs. master pre-Phase-B baseline. Record the delta in the PR description.

#### A.4 — Adding new direct CRUD→AI dependency?

**NO.** No AI dependency added. Nothing in `Services/Ai/PublicContracts/` is touched. The new services consume only Dataverse + cache + existing auth.

#### A.5 — Follows feature-module DI conventions (ADR-010)?

**YES.** Will register through `services.AddDataverseProjectionModule(configuration)` extension method in `Infrastructure/DI/DataverseProjectionModule.cs`. NOT a flat blob in `Program.cs`.

### §MUST Rules — B. New Package References (Binding)

#### B.1 — `dotnet list package --vulnerable --include-transitive` — no HIGH-severity CVE introduced?

**N/A (no new packages added)** but the verification step is still mandatory for the Phase B PR per §F.1 anti-drift discipline. Task 016 owner runs the command and pastes output into the PR description even if zero new packages are added (to document the snapshot at time of merge).

#### B.2 — Package version compatibility with pinned chains (Graph/Kiota/AI)?

**N/A (no new packages added).** The existing `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32 pin remains untouched.

#### B.3 — No pre-release packages added without inline csproj comment?

**N/A (no new packages added).** Existing pre-release packages in `Sprk.Bff.Api.csproj` (`Azure.AI.Projects beta.8`, `Microsoft.Agents.AI rc1`, `Azure.AI.OpenAI 2.8.0-beta.1`) are unrelated to this addition.

### §MUST Rules — C. New Endpoints (Binding per ADR-001, ADR-008)

#### C.1 — Use Minimal API (`MapPost`/`MapGet` with explicit handlers)?

**YES.** Pattern from `Api/Ai/SemanticSearchEndpoints.cs` — `app.MapGroup("/api/dataverse").RequireAuthorization().WithTags("Dataverse Projection")` + 5 explicit `Map…` calls with handler methods.

#### C.2 — Endpoint-filter-based authorization (`.AddEndpointFilter<…>()`)?

**YES.** All 5 endpoints will use `.AddDataverseAuthorizationFilter(entityNameSource: …)` extension (canonical shape in `010-authorization-filter-shape.md`). No global middleware for resource auth.

#### C.3 — `Results.Problem(...)` (RFC 7807) for error responses?

**YES.** Per ADR-019: all errors return `ProblemDetails` with stable `errorCode` extension (`DV_PRIVILEGE_DENIED`, `DV_ENTITY_NOT_FOUND`, `DV_SAVEDQUERY_NOT_FOUND`, `DV_FETCHXML_MALFORMED`, `DV_UPSTREAM_TIMEOUT`, `DV_INTERNAL_ERROR`) and `correlationId` from `HttpContext.TraceIdentifier`.

#### C.4 — Rate limiting applied where appropriate (`.RequireRateLimiting("…")`)?

**YES.** All 5 endpoints will require the existing `standard` rate-limit policy (1k/min/user; reuse — not a new policy). The `/api/dataverse/fetch` endpoint additionally guards against pathological FetchXML via a per-request `<row count>` ceiling enforced in the handler (default 5000; configurable via `Dataverse:MaxFetchRowCount`).

#### C.5 — NOT add new endpoints to `Program.cs` directly — register through `Map{Feature}Endpoints` extension?

**YES.** All 5 endpoints registered via `app.MapDataverseProjectionEndpoints()` extension defined in `Api/Dataverse/DataverseProjectionEndpoints.cs`.

### §MUST Rules — D. New Background Work

**N/A** — no new background work added. No new `IJobHandler<T>` or hosted service.

### §MUST Rules — E. AI Feature Additions

**N/A** — not an AI feature.

### §MUST Rules — F. Test Update Obligation (Binding per FR-22 / D-05)

#### F.1 — Unit tests added in `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/`?

**YES — task 016 owns this.** Per spec.md `tests/TASK-INDEX.md`, task 016 (`integration tests against dev BFF`) and a sibling unit-test task will add the test surface. PRs touching `Services/Dataverse/` MUST include unit tests in the matching test folder; PRs touching `Api/Dataverse/` MUST include endpoint tests OR an integration fixture covering them.

#### F.2 — Endpoints map unconditionally; service registration is unconditional?

**YES.** No feature flag gates either endpoints OR service registration. Both map unconditionally. This avoids the RB-T028-03/04/05/06 anti-pattern (HIGH × 4, filed 2026-05-31 by `sdap-bff.api-test-suite-repair`) where endpoints mapped unconditionally but their service registration was flagged conditionally → 37 integration tests failed silently.

**Authorization filter handles deny cases**, not feature flags. If the caller lacks Read privilege, the filter returns 403 ProblemDetails — the endpoint is still mapped and still reachable for callers who do have privilege.

#### F.3 — Test fixture inherits from `IntegrationTestFixture` OR copies canonical config keys?

**YES — task 016 owner enforces this.** New fixture (`DataverseProjectionIntegrationFixture` or similar) MUST inherit from `IntegrationTestFixture` per the canonical pattern (RB-T044, RB-T028, and the 5 sibling-fixture sites identified by tasks 018/060/062/027/071). NOT a one-off `WebApplicationFactory`. NO custom config dictionary that diverges from `CosmosPersistence:*`, `SpeAdmin:KeyVaultUri`, `AgentService:*` baselines. NEW config keys for this addition (`Dataverse:MaxFetchRowCount`, `Dataverse:PrivilegeCacheTtlHours`, `Dataverse:DefaultPageSize`) MUST be added to the canonical fixture config dict, not stubbed locally.

#### F.4 — Tests tagged with `[Trait("status", …)]` per §6.2 taxonomy?

**YES — task 016 owner enforces this.** All new tests tagged `repaired` (initial state) per §6.2; any quarantined test gets a ledger entry per §F.4 with severity, fix-by date, and owner-TBD slot.

#### F.5 — No tests left in `Skip` state without ledger entry?

**YES — task 016 owner enforces this.** Project ledger files: `projects/spaarke-datagrid-framework-r1/ledgers/real-bug-ledger.md` and `flaky-ledger.md` (created on-demand if a Skip becomes necessary). Default: NO Skips at Phase B exit.

#### F.6 — Asymmetric-Registration Tier 1.5 anti-pattern avoided (§F.1)?

**YES.** No `*Module.cs` `if (flag) { … }` block introduced. The new `AddDataverseProjectionModule()` registers unconditionally. If any future requirement introduces a kill-switch flag, the consumer-scan recipe in `bff-extensions.md §F.1` step 1 will be applied (`rg -t cs -n "[\s,(]IDataverseProjection\w*\s+\w+[,)]" src/server/api/Sprk.Bff.Api/Api/`) and the resulting cases handled per ADR-030 patterns (P1/P2/P3). Not applicable at task 010 design time because no flag is planned.

#### F.7 — Fixture-Config-FIRST Inspection Protocol acknowledged (§F.2)?

**YES — task 016 owner acknowledged.** If any test is Skip'd during Phase B execution due to suspected DI issue, the owner will FIRST inspect fixture config, THEN auth state, THEN production code — not collapse fixture-config gaps into "upstream cluster fix subsumes it."

#### F.8 — Empirical-Reproduction-FIRST Protocol acknowledged (§F.3)?

**YES — task 016 owner acknowledged.** If a ledger entry's recommended fix is referenced, the owner will reproduce the failure empirically before applying the fix; if the actual root cause differs, file a path-b decision record at `projects/spaarke-datagrid-framework-r1/decisions/D-XX-{ledger-id}-resolution.md`.

### §MUST NOT Rules

- **MUST NOT add new code to BFF without considering "should this go elsewhere?"** — §3 and §4 satisfy this rule explicitly.
- **MUST NOT add new direct CRUD→AI dependencies** — N/A; no AI dependency.
- **MUST NOT add packages with known HIGH-severity CVEs** — N/A; no new packages.
- **MUST NOT add `<PublishTrimmed>true</PublishTrimmed>` or `<PublishAot>true</PublishAot>`** — N/A; not touching csproj publish settings.
- **MUST NOT publish from `/tmp` or any directory outside `deploy/api-publish/`** — Task 016 owner uses `bff-deploy` skill which enforces this.
- **MUST NOT bypass the `bff-deploy` skill for deploys** — Task 016 owner uses `bff-deploy` skill.

---

## 6. Project-Level Imperative — `bff-extensions.md` §Project-Level Imperative

The constraint requires the project's `design.md` to include:

1. **Placement justification section** — ✅ Present in design.md §9 (existing — written at spec time); this `010-bff-placement-justification.md` is the detailed expansion required by FR-BFF-08 acceptance.
2. **Size impact estimate** — ✅ §7 below.
3. **Boundary preservation statement** — ✅ §8 below.
4. **Reference to `.claude/constraints/bff-extensions.md`** — ✅ Cited in design.md §9 (existing) and spec.md "Binding Constraints" section.

---

## 7. Size Impact Estimate (per ADR-029, `azure-deployment.md` ratchet)

| Component | Estimated compiled size |
|---|---|
| `Api/Dataverse/DataverseProjectionEndpoints.cs` | ~6-10 KB |
| `Services/Dataverse/IDataverseProjectionService.cs` + `DataverseProjectionService.cs` | ~25-40 KB |
| `Services/Dataverse/Cache/DataversePrivilegeCache.cs` + `EntityMetadataCache.cs` | ~15-25 KB |
| `Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs` (parser for link-entity discovery) | ~10-15 KB |
| `Services/Dataverse/Models/*.cs` (request/response DTOs × ~10) | ~20-35 KB |
| `Api/Filters/DataverseAuthorizationFilter.cs` + extension | ~10-15 KB |
| `Infrastructure/DI/DataverseProjectionModule.cs` | ~3-5 KB |
| **Total compiled** | **~90-145 KB raw → ~150-250 KB after deps tree-shake** |

**Compressed publish-size delta**: well under the +2MB owner-ack threshold; estimated **+0.05-0.15 MB** on the `~60 MB` baseline. No re-baselining of the ADR-029 ratchet needed.

**Verification at PR time**: task 016 owner runs `dotnet publish --runtime linux-x64 --output deploy/api-publish/`, captures `du -sh` before and after, and records the delta in the PR description per `bff-extensions.md §A.3`.

---

## 8. Boundary Preservation Statement (per `bff-extensions.md` §Project-Level Imperative #3)

Confirmation that new code follows the binding patterns:

- ✅ **No new direct CRUD→AI deps** — Zero AI dependency added. Verified by inspection: no `IOpenAiClient`, `IPlaybookService`, no `Services/Ai/` imports in the new code.
- ✅ **Feature-module DI** — `services.AddDataverseProjectionModule(configuration)` extension in `Infrastructure/DI/DataverseProjectionModule.cs`. No `Program.cs` registrations.
- ✅ **Endpoint-filter auth** — `DataverseAuthorizationFilter` per ADR-008; no global middleware for resource checks.
- ✅ **Facade pattern (ADR-007 style)** — Endpoints inject `IDataverseProjectionService` (concrete facade); not `IOrganizationService` or `ServiceClient` directly.
- ✅ **`Results.Problem(...)` errors** — ADR-019 ProblemDetails throughout; no custom error shapes.
- ✅ **Rate-limit policy reused** — Existing `standard` policy; no new policy added.
- ✅ **Spaarke Auth v2 contract** — Server side: standard JWT middleware validates token; OBO exchange (when needed) or app-only `ServiceClient` with `CallerId` impersonation (final decision in task 011 — see open-questions §1).

---

## 9. Quick Reference Snippets (for PR description copy-paste)

```csharp
// ✅ Endpoint registration (pattern modeled on SemanticSearchEndpoints.cs)
public static class DataverseProjectionEndpoints
{
    public static IEndpointRouteBuilder MapDataverseProjectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataverse")
            .RequireAuthorization()
            .WithTags("Dataverse Projection");

        group.MapGet("/savedquery/{savedQueryId:guid}", GetSavedQuery)
            .AddDataverseAuthorizationFilter(entitySource: EntitySource.FromSavedQueryEntity)
            .RequireRateLimiting("standard")
            .Produces<SavedQueryDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // ... 4 more endpoints, same shape
        return app;
    }
}
```

```csharp
// ✅ DI registration (ADR-010 feature-module)
public static class DataverseProjectionModule
{
    public static IServiceCollection AddDataverseProjectionModule(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddSingleton<IDataverseProjectionService, DataverseProjectionService>();
        services.AddSingleton<DataversePrivilegeCache>();
        services.AddSingleton<EntityMetadataCache>();
        services.AddSingleton<FetchXmlEntityExtractor>();
        services.Configure<DataverseProjectionOptions>(cfg.GetSection("Dataverse"));
        return services;
    }
}
```

---

## 10. Acceptance — `bff-extensions.md` Checklist Coverage Summary

| Section | Items Total | Items Answered | Items Skipped |
|---|---|---|---|
| §Why this constraint exists (operative implication) | 1 | 1 | 0 |
| §When to Load This File (applicability) | 6 | 6 | 0 |
| §MUST Rules — A. Pre-Merge Checklist | 5 | 5 | 0 |
| §MUST Rules — B. New Package References | 3 | 3 | 0 |
| §MUST Rules — C. New Endpoints | 5 | 5 | 0 |
| §MUST Rules — D. New Background Work | 1 (N/A) | 1 | 0 |
| §MUST Rules — E. AI Feature Additions | 4 (N/A) | 1 (rolled into "not an AI feature") | 0 |
| §MUST Rules — F. Test Update Obligation | 8 | 8 | 0 |
| §MUST NOT Rules | 6 | 6 | 0 |
| §Project-Level Imperative | 4 | 4 | 0 |
| **TOTAL** | **43** | **43** | **0** |

**N/A items**: 1 (D. Background work — no background work added), 1 (E. AI feature — not an AI feature), 3 (B. Package references — no new packages); each N/A is annotated with the reason inline, not skipped. ✅ Per the project constraint: "every checkbox must be answered (Yes / No / N/A with reason). No skipping."

---

## 11. Open Questions Surfaced

See [`010-open-questions.md`](010-open-questions.md) — 3 questions for review before tasks 011-014 dispatch.

---

## 12. Sign-Off Required Before Phase B Dispatch

- [ ] Project owner reviews this doc + `010-authorization-filter-shape.md` + `010-open-questions.md`.
- [ ] Open questions §1-3 resolved (privilege check mechanism, FetchXML parser ownership, cache instance choice).
- [ ] Phase B Wave 1 (tasks 011, 012, 013, 014) dispatched in parallel — they share this placement justification.

---

**Lines**: ~280 (within FR-BFF-08 documentation budget).
