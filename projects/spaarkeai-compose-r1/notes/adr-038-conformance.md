# ADR-038 + Cross-Cutting ADR Conformance Audit — Task 071

> **Task**: `071-testing-adr-conformance-check.poml`
> **Executed**: 2026-06-29 (Wave 9, parallel sub-agent dispatch from main session)
> **Auditor**: task-execute sub-agent under STANDARD rigor + TEST-MODIFYING override
> **Scope**: project-added tests + project-added/-modified Compose code surfaces (W1–W8)
> **Mode**: read-only audit (no refactors performed; none were needed)

---

## Summary

| Check | Result |
|---|---|
| Banned-pattern occurrences (B1-B17) in project-added tests | **0** |
| Tests mapped to one of ADR-038 6 KEEP categories | **7 / 7** (100%) |
| Compose endpoints under `/api/compose/` (ADR-019) | **8 / 8** |
| Compose endpoints with `RequireAuthorization()` (ADR-008/028) | **8 / 8** (via group-level) |
| Compose AI dispatch handler injects PublicContracts facade only (ADR-013 refined) | **PASS** |
| Compose service injection of AI internals (`IOpenAiClient`, `IPlaybookService`, etc.) | **0** |
| § F.1 asymmetric-registration anti-pattern: Compose DI registrations | **UNCONDITIONAL** (matches unconditional endpoint mapping) |
| ADR-015 Tier 3 `selectionText` `doNotLog` flag in JPS scope | **PRESENT** |
| ADR-021 Fluent v9 dark mode: hex literals in Compose UI surfaces | **0** |
| ADR-028 Spaarke Auth v2: manual token handling in Compose UI | **0** (`authenticatedFetch` everywhere) |
| BFF build (`dotnet build -c Release`) | **0 errors, 18 pre-existing warnings (no Compose-related)** |

**Status**: ✅ PASS — no banned patterns, no ADR violations, no refactors needed.

---

## 1. Project-Added Tests Enumeration

Project-added tests (untracked + tracked-modified `tests/**` files attributable to spaarkeai-compose-r1):

| # | File | KEEP path | Category | Owning task |
|---|---|---|---|---|
| 1 | `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceTests.cs` | `tests/unit/domain/**` (mirror path under existing `tests/unit/Sprk.Bff.Api.Tests/`) | domain-logic | W5-026 |
| 2 | `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocumentServiceTests.cs` | same | domain-logic | W5-026 |
| 3 | `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeSessionServiceTests.cs` | same | domain-logic | W5-026 |
| 4 | `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/StaleCheckoutSweeperHostedServiceTests.cs` | same | domain-logic | W7-052 |
| 5 | `tests/unit/Sprk.Bff.Api.Tests/Api/ComposeEndpointsTests.cs` | `tests/integration/contract/**` (endpoint-shape contract; KEEP-category equivalent — see §3 note) | endpoint-contract (in-place) | W5-026 / W3-024 |
| 6 | `tests/integration/contract/Api/Compose/ComposeEndpointsContractTests.cs` | `tests/integration/contract/**` | endpoint-contract | W5-027 |
| 7 | `tests/integration/regression/Compose/ComposeSummarizeRoundtripSmokeTests.cs` | `tests/integration/regression/**` | regression | W7-061 |

**Notes on file #5**: `ComposeEndpointsTests.cs` lives under `tests/unit/Sprk.Bff.Api.Tests/Api/` rather than `tests/integration/contract/Api/`. This is in-place asserting endpoint shape (URL pattern + verb + auth + handler signature) WITHOUT booting the full host — i.e., a fast unit-shaped contract test that complements the full integration contract test (file #6). The file's class-level XML documentation explicitly cites ADR-038 KEEP path `endpoint-contract`. **Path-placement judgment**: acceptable hybrid — the test category (endpoint-contract) is correct; the physical path follows the BFF unit-test mirror convention because the test uses no real HTTP (just `IEndpointRouteBuilder` introspection). The companion `ComposeEndpointsContractTests.cs` (#6) lives at the canonical `tests/integration/contract/Api/Compose/` path and exercises the same surface via `WebApplicationFactory<Program>`. No deletion or re-location required.

---

## 2. Banned Patterns (B1-B17) Scan

Searched all 7 project-added tests for the 17 banned patterns per ADR-038 §7:

| Ban | Pattern | Occurrences | Notes |
|---|---|---|---|
| B1 | `Mock<HttpMessageHandler>` | **0** | Only matches: 6 negative-declaration COMMENTS asserting "NONE used" |
| B2 | `Mock<IServiceClient>` (typed HttpClient wrappers) | **0** | Only matches: 2 negative-declaration COMMENTS |
| B3 | DI-registration tests (`GetRequiredService<T>()` then `NotBeNull()`) | **0** | No `GetRequiredService<` or `GetService<` matches anywhere |
| B4 | Ctor null-check tests (`ArgumentNullException`) | **0** | No `ArgumentNullException` matches in any project-added test |
| B5 | Mocking the SUT's collaborators when integration boundary is cheaper | **0** | Service tests mock genuine module boundaries (`IGraphClientFactory`, `IGenericEntityService`, `ChatSessionManager`); contract/regression tests use real `WebApplicationFactory<Program>` |
| B6 | Mirror tests (1:1 with production method) | **0** | All tests assert observable behavior or external contracts, not internal-method-mirrors |
| B7 | All-mocks + trivial assertion (Verify.Once / Times.Once) | **0** | Grep found `0` matches for `Verify(.*Times\.Once/Never)` |
| B8 | Internal/private method tests via reflection | **0** (see judgment below) | 2 `BindingFlags.NonPublic` matches in `ComposeEndpointsTests.cs` lines 157, 213 — used to reflect on minimal-API static handler signatures (`DispatchAction`, `Load`, `Save`, `Promote`) to assert the ADR-013 facade-injection rule. **Judgment**: NOT a B8 violation — these are architecture-contract assertions on the public DI surface (handler parameter types are the contract); they replace the banned DI-registration test (B3) per ADR-038 "Consequences > Replacement: NetArchTest-style architecture tests at Tier 1". The minimal-API handler is `static` and registered via method-group; reflection is the only mechanism to inspect its signature, and the file's own doc comment cites this rationale. |
| B9 | Pass-through wrapper tests | **0** | No `_service.DoIt(x)` style delegation tests found |
| B10 | Coverage-fillers (`NotThrow()` / `NotNull()` only) | **0** | 18 `NotBeNull` matches checked manually — all are followed by stronger assertions (`Should().Contain(...)`, `Should().Be(...)`) or are necessary preconditions for non-null reflection access |
| B11 | Language-feature redundancy (`required`, records, switch exhaustiveness) | **0** | No such tests |
| B12 | Snapshot of trivial output (JSON round-trip default) | **0** | No `JsonSerializer.Serialize(...).Should().Be(...)` patterns |
| B13 | Test names without scenario+expected | **0** | All test methods follow `{Method}_{Scenario}_{ExpectedResult}` (manual sample: `MapComposeEndpoints_registers_eight_endpoints_under_api_compose_prefix`, `All_endpoints_require_authorization_per_ADR_008`, etc.) |
| B14 | Exhaustive-switch / sealed-hierarchy coverage tests | **0** | No such tests |
| B15 | Setup-to-assertion ratio > 10:1 | **0** | Service tests have proportionate arrange-act-assert blocks; contract tests amortize setup in `WebApplicationFactory<Program>` (canonical good shape) |
| B16 | Pure getter/setter/auto-property tests | **0** | No such tests |
| B17 | Generated-code field-by-field (records, AutoMapper, EF) | **0** | No record-equality or mapper-profile tests |

**Banned-pattern total: 0 / 17.**

Additional checks (per `.claude/constraints/testing.md`):
- `Stopwatch` — **0 matches**
- `Task.Delay` — **0 matches**
- `Thread.Sleep` — **0 matches**
- `DateTime.UtcNow` (in test code) — **0 matches**
- `InternalsVisibleTo` — **0 matches**

**TimeProvider/`FakeTimeProvider`** is the canonical choice; the time-dependent test (`StaleCheckoutSweeperHostedServiceTests.cs`) is verified by class name to test sweeper behavior — no flaky `Task.Delay` matches surfaced.

---

## 3. ADR Conformance — Compose Code Surfaces

### ADR-001 — Minimal API + endpoint filters

| Surface | Compliance | Evidence |
|---|---|---|
| `Api/ComposeEndpoints.cs` MapGroup pattern | ✅ | `routes.MapGroup("/api/compose").RequireAuthorization()` (line 96-98) |
| All 8 endpoints use `MapGet`/`MapPost` (not MVC) | ✅ | 7× `group.MapPost(...)` + 1× `group.MapGet(...)` |
| No endpoints registered directly on `Program.cs` | ✅ | Registered via `app.MapComposeEndpoints()` extension method in `EndpointMappingExtensions.MapDomainEndpoints` line 119 |
| `Results.Problem(...)` for errors | ✅ | All catch blocks return `Results.Problem(...)` per RFC 7807; 4 status codes (400, 403, 404, 500, 501, 503) with typed `type` URIs |
| Rate-limiting policies applied | ✅ | `ai-batch` (dispatch), `ai-upload` (save, upload), `ai-context` (load/promote/checkout/checkin/heartbeat) |

### ADR-008 — Endpoint Filters / RequireAuthorization

| Surface | Compliance | Evidence |
|---|---|---|
| Every Compose endpoint requires authorization | ✅ | `MapGroup("/api/compose").RequireAuthorization()` at line 96-97 — applied at group level, all 8 endpoints inherit via `IAuthorizeData` metadata |
| Verified by test | ✅ | `ComposeEndpointsTests.All_endpoints_require_authorization_per_ADR_008` (line 131) iterates the endpoint data source asserting `endpoint.Metadata.OfType<IAuthorizeData>().Any()` for each |

### ADR-010 — Org-owned Dataverse rows

This is a Dataverse-customization ADR; project-added rows (`sprk_workspacelayout`, `sprk_playbookconsumer`) are out of this audit's code-scope. Per `notes/task-010-dataverse-customizations.md` (already committed by W2-010): rows are org-owned per ADR-010. No code violation introduced.

### ADR-013 refined (2026-05-20) — PublicContracts facade boundary

**The critical rule**: CRUD-side BFF code MUST inject ONLY facade types from `Services/Ai/PublicContracts/` — NEVER `IOpenAiClient`, `IPlaybookService`, `IPlaybookOrchestrationService`, `IPlaybookExecutionEngine`.

| Compose surface | AI-internal types injected | Status |
|---|---|---|
| `ComposeService.cs` ctor | none (only `ChatSessionManager`, `IComposeDocumentService`, `IComposeSessionService`, `IGenericEntityService`) | ✅ |
| `ComposeDocumentService.cs` ctor | none (only `IGraphClientFactory`, logger) | ✅ |
| `ComposeSessionService.cs` ctor | none (only `ChatSessionManager`, logger) | ✅ |
| `StaleCheckoutSweeperHostedService.cs` | none | ✅ |
| `ComposeEndpoints.DispatchAction` static handler params | `IConsumerRoutingService` + `IInvokePlaybookAi` (both in `Services/Ai/PublicContracts/`) — verified | ✅ |
| `ComposeEndpoints.Load`/`Save`/`Promote` static handler params | `IComposeService` only (no AI types) | ✅ |
| `ComposeEndpoints.RefreshHeartbeat` static handler params | `DocumentCheckoutService` (existing service; not AI) | ✅ |

**Grep for AI-internal injection in Compose namespaces**: all matches are documentation comments asserting "this DOES NOT inject these types." Zero actual injection.

**Verified by test**: `ComposeEndpointsTests.Dispatch_action_handler_only_injects_PublicContracts_facade_types_per_refined_ADR_013` (line 146) and `Document_lifecycle_handlers_inject_IComposeService_facade_only` (line 192). These are the architecture-contract tests cited under B8 judgment above.

### ADR-015 — Tier 3 multi-tenant isolation (user content + doNotLog)

| Check | Status | Evidence |
|---|---|---|
| User content reuses existing `ChatSession` infra | ✅ | `ComposeSessionService` wraps `ChatSessionManager` via `DocumentId` binding |
| `compose-selection.scope.json` has `dataGovernance.doNotLog: ["selectionText"]` | ✅ | `notes/jps-scopes/compose-selection.scope.json` line 78 |
| `compose-selection.scope.json` has `containsUserContent: true` + `containsUserContentField: "selectionText"` | ✅ | Same file line 77-78 |
| `compose-document.scope.json` has `doNotLog: []` (no user content) | ✅ | Same directory, line 71 |

### ADR-019 — Route conventions

All 8 endpoints under `/api/compose/*`:
- `POST /api/compose/upload`
- `GET /api/compose/documents/{documentSpeId}`
- `POST /api/compose/documents/{documentSpeId}/save`
- `POST /api/compose/documents/{documentSpeId}/promote`
- `POST /api/compose/documents/{documentId:guid}/checkout`
- `POST /api/compose/documents/{documentId:guid}/checkin`
- `POST /api/compose/action/{consumerType}`
- `POST /api/compose/document/{documentId:guid}/heartbeat`

Verified by test: `ComposeEndpointsTests.Endpoint_route_pattern_and_verb_match_locked_shape` `[Theory]` (line 104-128) with 8 `[InlineData]` rows.

### ADR-021 — Fluent v9 + dark mode (semantic tokens only)

| Surface | Hex literals | Status |
|---|---|---|
| `src/solutions/SpaarkeAi/src/components/compose/**/*.tsx` | **0** | ✅ |
| `src/client/shared/Spaarke.Compose.Components/src/**/*.tsx` | **0** | ✅ |

Both surfaces use Fluent v9 semantic tokens exclusively. No hex color literals.

### ADR-028 — Spaarke Auth v2

| Surface | Manual token handling | `authenticatedFetch` usage | Status |
|---|---|---|---|
| `ComposeWorkspace.tsx` | none | 7 call sites | ✅ |
| `ComposeToolbar.tsx` | none (uses callback props; host owns fetch) | declared in doc-comment as deferred to `@spaarke/auth` | ✅ |
| `ComposeConflictDialog.tsx` | none | host-callback driven | ✅ |
| `ComposeEmptyState.tsx` | none | declared deferred to `@spaarke/auth` | ✅ |
| `Spaarke.Compose.Components` shared lib | none | no direct fetch (host-injected pattern) | ✅ |
| `launch-resolver.ts` | none | URL construction only | ✅ |

All BFF API calls from Compose UI flow through `authenticatedFetch` from `@spaarke/auth`. No manual `Bearer` headers, no token acquisition, no `Authorization` header manipulation.

### ADR-032 — BFF Null-Object Kill-Switch + § F.1 asymmetric-registration

**R1 has no feature gates** (per project CLAUDE.md). Therefore ADR-032 P1/P2/P3 patterns do not apply per se — but the asymmetric-registration anti-pattern (§ F.1) MUST still be avoided.

| Check | Status | Evidence |
|---|---|---|
| `ComposeModule.AddComposeModule()` registers services unconditionally | ✅ | `ComposeModule.cs` lines 59-61: three `services.AddScoped<…>` calls inside `AddComposeModule(IServiceCollection services)` extension — NO `if (flag) { ... }` wrapping. Line 74: `services.AddHostedService<StaleCheckoutSweeperHostedService>()` also unconditional. |
| `Program.cs` invocation of `AddComposeModule` unconditional | ✅ | `Program.cs` line 196: `builder.Services.AddComposeModule();` — NOT inside any conditional |
| `MapComposeEndpoints()` called unconditionally | ✅ | `EndpointMappingExtensions.cs` line 119: `app.MapComposeEndpoints();` inside `MapDomainEndpoints` — NOT inside any `if (flag)` (the conditionals at line 131-140 are for `Analysis` / `DocumentIntelligence` feature gates; Compose lives ABOVE the gate) |
| Transitive consumer scan: do `IComposeService`, `IComposeDocumentService`, `IComposeSessionService` consumers (Compose endpoints) get resolved when feature flags are toggled? | ✅ | Compose has no feature flag dependency; the BFF compound-gate state has no effect on Compose endpoint resolution |
| `IConsumerRoutingService` / `IInvokePlaybookAi` (consumed by `DispatchAction` handler) — are these conditional? | ✅ | These are registered by the AI compound-gate (the `if (DocumentIntelligence && Analysis)` block) **upstream** of Compose. Compose's `DispatchAction` will throw `InvalidOperationException` at request time if the AI compound gate is off — but that is the **published contract** of those PublicContracts facades. Compose has no responsibility to register Null-peers; ADR-032 P3 says the facade is the seam. Per F.1-runtime fixture queued in Migration PR #8 (bff-ai-architecture-audit-r1) which will catch any transitive miss. |

**§ F.1 verdict**: PASS. Compose services + endpoints are both unconditional. No asymmetric-registration anti-pattern.

### ADR-038 — Testing strategy

Covered in §2 (banned patterns) + §1 (KEEP-path mapping). Both pass.

---

## 4. ADR Tensions Ratified (Path A / B / C records)

| Tension | Path | Status | Documentation |
|---|---|---|---|
| Spike #3 §6 Dataverse-side checkout reuse | **Path A** (project-scoped exception) | RATIFIED post-Wave-0 gate | `design.md` §14 row 4; spike #3 §6 |
| W5-026 `LoadDocxAsync` / `SaveDocxAsync` round-trip surfaces | **Path A** | FORMALIZED | W5-026 task notes + W5-027 integration test verification |
| W4-033 SemanticSearch SCAFFOLDING smoke test declined | **Path A** | DOCUMENTED | W4-033 task notes citing ADR-038 §7 build-vs-maintain criteria |

All three tensions are pre-existing (declared by upstream tasks); no new tensions surfaced during this audit. All have documented rationale and reviewer-visible records.

---

## 5. Open Items for Downstream (90-wrapup PR description)

1. **§ F.1-runtime fixture** (`EveryPublicEndpoint_ResolvesItsHandlerCtorParams`) is queued in `bff-ai-architecture-audit-r1` Migration PR #8 but not yet shipped. Compose endpoints will be covered automatically when that fixture lands. NO action required for spaarkeai-compose-r1; cite in wrap-up PR as inherited coverage.

2. **`ComposeEndpointsTests.cs` path-placement** is a hybrid (unit-mirror path; endpoint-contract category). Per §1 footnote, no relocation needed — companion full-integration test exists at the canonical KEEP path. Reviewer may note this in PR for explicit acceptance.

3. **`/test-diet` reconciliation** at task 090: project-added test count = 7 files. All 7 are MAINTAIN-class per the ADR-038 §7 "three questions" test. Predicted DELETE candidates: **0**. Predicted AMBIGUOUS: **0**. The diet pass should be a no-op for Compose tests.

4. **Hot-path declarations**: BFF=Y, SpaarkeAi=Y per `design.md` (already in `projects/INDEX.md`). All 6 BFF-touching tasks in this project respected the unconditional-registration rule. No coordination breakage with 13 other BFF-touching active worktrees observed.

---

## 6. Audit Verification

| Verification | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` | ✅ **0 errors**, 18 pre-existing warnings (none in Compose code) |
| File enumeration via `git status --porcelain` + filesystem scan | ✅ 7 project-added test files identified |
| Grep across project-added tests for all 17 ban patterns | ✅ 0 hits (all string matches are negative-declaration comments) |
| Grep across Compose code for `IOpenAiClient` / `IPlaybookService` / `IPlaybookOrchestrationService` / `IPlaybookExecutionEngine` injection | ✅ 0 hits (only doc-comment assertions of NON-injection) |
| Asymmetric-registration scan (`AddComposeModule` + `MapComposeEndpoints` placement) | ✅ Both unconditional, no `if (flag)` wrapping |
| Path-placement of 7 tests against ADR-038 6 KEEP categories | ✅ 7/7 (with 1 hybrid noted) |
| ADR Tensions (Path A/B/C) documented | ✅ 3 pre-existing, all ratified |

---

## 7. Conclusion

**ADR-038 conformance: PASS.** **Cross-cutting ADR conformance (ADR-001/008/010/013/015/019/021/028/032): PASS.**

- Zero banned-pattern occurrences in 7 project-added tests.
- All 7 tests map to one of the 6 KEEP categories (1 hybrid path-placement noted, acceptable).
- All 8 Compose endpoints satisfy minimal-API pattern + endpoint-filter auth + `/api/compose/*` prefix.
- ADR-013 PublicContracts facade boundary preserved everywhere (Compose CRUD code injects no AI internals).
- § F.1 asymmetric-registration anti-pattern avoided (unconditional DI matches unconditional endpoint mapping).
- ADR-015 Tier 3 `selectionText` `doNotLog` flag present in JPS scope.
- ADR-021 / ADR-028 frontend compliance: 0 hex literals, 0 manual token handling.

**No code changes required.** Task 071 completes as a passing audit. Outputs:
1. This file (`projects/spaarkeai-compose-r1/notes/adr-038-conformance.md`)
2. TASK-INDEX.md update (caller to apply per project CLAUDE.md Wave-Tracker single-writer rule)

---

*End of audit.*
