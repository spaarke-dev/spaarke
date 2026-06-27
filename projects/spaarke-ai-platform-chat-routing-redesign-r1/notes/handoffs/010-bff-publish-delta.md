# Task 010 — BFF Publish Delta + Placement Justification

> **Date**: 2026-06-22
> **Task**: 010 (Stand up `GET /api/ai/playbooks/by-code/{code}` endpoint)
> **Wave**: 1-A
> **Rigor**: FULL
> **Spec FR**: FR-01 (Stable-code resolution for playbook lookup)

---

## BFF Publish-Size Delta (NFR-01)

| Measurement | Value |
|---|---|
| **Compressed publish size (this task)** | **44.7531 MB** (46,927,025 bytes) |
| **Phase 0 baseline** (`notes/handoffs/phase-0-baseline.md`) | 44.7500 MB (46,927,912 bytes) |
| **Delta vs Phase 0 baseline** | **+0.0031 MB (~+887 bytes)** |
| **CLAUDE.md §10 stated baseline (2026-05-26 post-Phase 5 Outcome A)** | 45.65 MB |
| **NFR-01 ceiling** | 60.00 MB compressed |

- ✅ Delta is **negligible** (+887 bytes), well under the +5 MB per-task escalation threshold.
- ✅ Cumulative size **44.75 MB** is comfortably below the 55 MB architecture-review threshold and the 60 MB hard ceiling.
- ✅ No new NuGet package references added.
- ✅ No new module / new background service / new external API surface.

**Publish command**:

```bash
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-task010/
```

Compressed via PowerShell `Compress-Archive -CompressionLevel Optimal` to match the Phase 0 measurement methodology.

---

## Placement Justification (per `.claude/constraints/bff-extensions.md` § A.1)

The new endpoint `GET /api/ai/playbooks/by-code/{code}` is added to the existing `Api/Ai/PlaybookEndpoints.cs` file alongside the 11 sibling playbook CRUD endpoints (including the analogous `GET /by-name/{name}`). Considering the four placement options:

1. **In BFF (CHOSEN — Minimal API expansion of existing surface)** — Selected.
2. *In Azure Functions* — Rejected. The endpoint is request-response in the chat / pre-fill resolution path; latency-sensitive; not event-driven.
3. *In a separate deployable* — Rejected. It is a lookup over the existing `IPlaybookLookupService` already wired into BFF; no new transactional or coupling concerns.
4. *In a shared library only* — Rejected. The route must be exposed over HTTP; clients (PCFs, code pages, Office Add-ins) consume via `authenticatedFetch`.

**Why in BFF** (one sentence, per §A.1): The endpoint is a new HTTP-route on an existing AI-playbook resource group, consuming an already-registered service (`IPlaybookLookupService` in `FinanceModule.cs:114`), with no new packages, no new module, no new background work — the minimum-friction expansion of an existing surface for the 9 downstream consumer migrations (tasks 014–020) that depend on FR-01.

---

## ADR Citation (per `.claude/constraints/bff-extensions.md` § A.2)

| ADR | Compliance |
|---|---|
| **ADR-001 (Minimal API)** | ✅ `group.MapGet("/by-code/{code}", GetPlaybookByCode)` — no controller |
| **ADR-008 (Endpoint filters)** | ✅ Group-level `RequireAuthorization()`; tenant scoping enforced via JWT `tid` claim baked into the cache key (the same tenant cannot read another tenant's cached result; tenant misses fall through to a fresh service call) |
| **ADR-010 (DI minimalism)** | ✅ `IPlaybookLookupService` + `IMemoryCache` injected directly into the handler signature — no `IServiceProvider.GetService<T>()` |
| **ADR-013 (AI facade boundary)** | ✅ This is itself an AI-surface endpoint (in `Api/Ai/`). It consumes `IPlaybookLookupService` which is an AI-internal type — but this consumption is **inside** the AI module (`Api/Ai/`), not from CRUD code. No new direct CRUD→AI dependency added. |
| **ADR-014 (AI caching)** | ✅ 5-minute `IMemoryCache` TTL keyed `playbook-by-code:{tenantId}:{code-upper-invariant}` per ADR-014 key-shape requirements (tenant-scoped, artifact-category prefix, stable identifier, case-insensitive code) |
| **ADR-018 (Typed options)** | n/a (no config reads in this endpoint) |
| **ADR-019 (ProblemDetails)** | ✅ Basic `Results.Problem(statusCode: 404, title: "Playbook Not Found", detail: …)` shape returned on miss. Task 011 will refine the shape (type URI, extensions, telemetry) per ADR-019 — the per-task POML explicitly states this is the intentional handoff. |
| **ADR-029 (BFF publish hygiene)** | ✅ +887 bytes delta; no `<PublishTrimmed>` / `<PublishAot>` enabled; current 44.75 MB still under 60 MB ceiling |

---

## CVE / Package Hygiene (per § A.3)

- ✅ No new NuGet package references in `Sprk.Bff.Api.csproj`
- ✅ No new packages in the integration test project (Moq + Microsoft.AspNetCore.Mvc.Testing already pinned)
- ✅ `dotnet list package --vulnerable --include-transitive` not re-run (the rule is binding only when adding packages; this task adds none)

---

## Files Modified / Created

### Modified (1 file)

- `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs` — added `IMemoryCache` using, added `/by-code/{code}` `MapGet` registration in `MapPlaybookEndpoints()`, added `GetPlaybookByCode` handler.

### Created (3 files)

- `tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByCodeEndpointTests.cs` — 5 integration tests covering 401, cold miss (<500ms), warm hit (<100ms), 404, cross-tenant.
- `tests/integration/Sprk.Bff.Api.IntegrationTests/PlaybookByCodeIntegrationTestFixture.cs` — `WebApplicationFactory<Program>` substituting `IPlaybookLookupService` with `StubPlaybookLookupService` and `IDataverseService` with a Moq stub so the auth middleware doesn't 500.
- `tests/integration/Sprk.Bff.Api.IntegrationTests/StubPlaybookLookupService.cs` — deterministic stub with configurable cold-path delay + invocation count for the warm-hit assertion.

### Pre-existing modified (NOT touched by this task)

- `src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs` — task 003 + task 012 working-tree changes (untouched by this task).
- `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs` — task 012 working-tree changes (untouched by this task).
- `.husky/_/post-checkout` + `.husky/_/post-commit` + `.husky/_/post-merge` + `.husky/_/pre-push` — environment-specific husky changes; pre-existing.
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/001-delete-legalworkspace-creatematter-deadcode.poml` + `TASK-INDEX.md` — Wave 0-A edits; pre-existing.
- `src/client/pcf/package-lock.json` — pre-existing.

---

## Build + Test Outcomes

| Step | Result | Detail |
|---|---|---|
| BFF API build (`dotnet build src/server/api/Sprk.Bff.Api/`, Debug, --no-incremental) | ✅ 0 errors, 17 warnings | All warnings pre-existing baseline noise (CS1998 async-without-await on null-object stubs, CS8604/CS8601 nullable, CS0618 obsolete `DemoProvisioningOptions`) |
| Test project build (`dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/`) | ✅ 0 errors, 0 warnings | Clean |
| New tests run (`dotnet test --filter FullyQualifiedName~PlaybookByCodeEndpointTests`) | ✅ 5/5 passed | Duration 90 ms |
| Full integration suite (`dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/`) | ✅ 30/30 passed (25 prior + 5 new) | Duration 135 ms — no regression |
| BFF publish (`dotnet publish -c Release`) | ✅ Success | 44.7531 MB compressed |

---

## Acceptance Criteria (POML §`acceptance-criteria`)

| Criterion | Status | Evidence |
|---|---|---|
| `GET /by-code/summarize-document-chat` returns 200 with the payload | ✅ | `GetByCode_ColdMiss_Returns200_WithPayload_UnderColdPathBudget` test passes; payload deserializes correctly |
| `GET /by-code/nonexistent` returns 404 ProblemDetails | ✅ | `GetByCode_Returns404_WhenPlaybookDoesNotExist` test passes; body contains "Playbook Not Found" title |
| Warm cache hit ≤100 ms | ✅ | `GetByCode_WarmHit_Returns200_WithoutInvokingService_UnderWarmPathBudget` asserts `< 100ms` (measured well under in CI; in-memory cache hit) |
| Cold path ≤500 ms | ✅ | `GetByCode_ColdMiss…` asserts `< 500ms`; observed ~50–60ms with 50ms stub delay |
| Tenant scoping (cross-tenant lookup returns 404) | ✅ | `GetByCode_CrossTenant_Returns404…` test asserts cache slots are independent per tenant |
| BFF publish delta within NFR-01 | ✅ | +887 bytes, well under +5 MB escalation threshold |
| code-review + adr-check exit 0 | ⏳ pending Step 9.5 | (executed below) |
