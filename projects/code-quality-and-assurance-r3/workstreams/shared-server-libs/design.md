# Shared Server Libs Cleanup & Remediation — Design Document

> **Surface**: Shared server libs — `src/server/shared/Spaarke.Core/`, `src/server/shared/Spaarke.Dataverse/`, `src/server/shared/Spaarke.Scheduling/` (a **surface workstream** of `code-quality-and-assurance-r3`, not a standalone project)
> **Status**: DRAFT — for owner review before `/design-to-spec`
> **Date**: 2026-08-14
> **Method**: quality-assessment workflow: 11-dimension fan-out + Fable adversarial verification. All findings below survived the mandatory Fable refutation pass (r3 spec NFR-05); one first-pass claim was refuted and is recorded in §4 as record-only.
> **Read-only statement**: This assessment modified NO code. Its sole outputs are this design and the SCORECARD row inputs (§8). Remediation is operator-gated and task-created separately.
> **Governance**: `Spaarke.Core` / `Spaarke.Dataverse` are consumed by the BFF ⇒ root `CLAUDE.md` §10 (BFF Hygiene) applies to every remediation task here. Hot-path declaration in §7.
> **Program links**: NG1 (two-Dataverse-stacks) assess-then-decide = r3 **task 011** (this document is its verified input). #3b ClientSecret→MI migration = same track. #3a app-reg drop = task 060. BFF workstream tasks 021/028 own the always-failing-cast fixes + the `UnwrapServiceClient` extension (§6 coordination).

---

## 0. Summary & verdict

The shared server libs split into two very different stories. **`Spaarke.Scheduling` and `Spaarke.Core` are fundamentally healthy** — coherent layering (Core has no Spaarke deps; Dataverse depends only on Core; Scheduling is isolated), fail-closed authorization (`DataverseAccessDataSource` returns `AccessRights.None` on any error), zero vulnerable or deprecated packages across all three libs, and a genuine behavior-test core. **`Spaarke.Dataverse` is the program's weakest architectural spot so far**: two ~2,800-LOC God-class twins implementing one 9-interface composite, held together by hand-maintained DI forwarding that has already mis-wired once in production; four divergent ClientSecret credential paths that all violate ADR-028's managed-identity MUST (the sanctioned #3b migration target); a leaked raw `ServiceClient` seam that 10 BFF consumers downcast to (3 of them incorrectly — a live always-throwing path); and a doc set that actively misdirects (the live Event/FieldMapping implementation is documented "not currently used"; the surface `CLAUDE.md` mandates registering a class that was deleted in 2025).

**Verdict: C+ surface.** Not rot — a concentrated, well-understood debt cluster whose two biggest items (NG1 stack unification, #3b credential migration) are already owner-tracked. The remediation below is dominated by small, low-contention fixes (Tranche A) plus the two wide/contested decisions (Tranche B) that must land in a quiet window on task 011's verified design.

### Per-dimension grade table (re-adjudicated — see §1)

| Dim | Area | Grade | One-line basis |
|---|---|---|---|
| D1 | Architecture & boundaries | **D+** | Two 2,8xx-LOC God classes on one 9-interface composite; ADR-028 §24 MI MUST violated on all outbound; facade defeated by raw `ServiceClient` leak (10 downcast consumers) |
| D2 | Correctness & reliability | **C+** | Always-failing `is ServiceClient` cast live on finance-rollup endpoint + job (root seam in-surface); latent `NotImplementedException` landmine on the composite binding whose DI-routing guard already regressed once |
| D3 | Security | **B–** | Fail-closed authz, KV-sourced secrets, no live injection; but all four outbound Dataverse clients use ClientSecret contra ADR-028 §24 (tracked #3b) + two LOW latent items |
| D4 | Performance & scalability | **B–** | Web-API reads silently truncate at page size (no `@odata.nextLink`); sequential 3-RTT auth path (Redis-mitigated); associate N+1; per-call metadata RTT; `Task.Run`-over-sync |
| D5 | DRY / dead code | **C+** | ~5,680 LOC of parallel `IDataverseService` twins with 37 unreachable throw-stubs (NG1 input); token-cache ×3 verbatim; credential blocks ×4; 461-LOC archive orphan |
| D6 | Consistency & conventions | **B–** | Same secret under two config keys (live constructor-throw trap); four credential patterns; swapped interface param names; per-record `LogWarning` |
| D7 | Testability & test quality | **C+** | 10 Skip-gated tests leave scheduler retry + enable/disable contracts dark; two whole test files exercise zero production code; ADR-038 §5 TimeProvider mandate unmet despite the seam existing |
| D8 | Dependency & supply-chain | **B+** | Zero vulnerable/deprecated packages verified; debts are the inert stale `Directory.Packages.props`, minor lib↔host pin drift, no lockfiles |
| D9 | Observability | **B–** | Correlation-gap claim REFUTED (OTel/Activity join works); remaining: live FullName PII at Information, latent full-email-payload log, diagnostic-tagged Information noise |
| D10 | ALM / build hygiene | **B–** | `Spaarke.Core.Tests` absent from `Spaarke.sln` ⇒ runs in NO CI job; Core+Dataverse not sln members; disabled-CPM contradiction; archive tracked in-tree |
| D11 | Knowledge/doc accuracy | **D+** | Live `DataverseWebApiService` documented "not currently used"; surface `CLAUDE.md` mandates a deleted class and labels the actual production DI pattern "WRONG" |
| — | **Surface (composed §4.2)** | **C+** | Mean 26.3/11 ≈ 2.39 → C+; gating cap min(C+, D2 C+, D3 B–) = **C+** (cap not binding) |

**Headline items (promote to first-class tasks):**
1. **NG1 decision input is now verified** (D5-01/D1-01/D1-02/D6-03/D2-02): two full parallel implementations of the same 9-interface composite, DI-segmented by convention, with mutual throw-stubs and one prior shipped mis-wire. Task 011 decides: unify to one stack, or compile-time-segregate the composite so each impl declares only what it serves.
2. **#3b credential migration is confirmed and precisely scoped** (D1-03, D3-01..04, D5-03, D6-01/02): all four outbound credential blocks verified secret-based; `DataverseAccessDataSource`'s DI-TokenCredential else-branch is the in-tree target pattern; the OBO ConfidentialClient half MUST survive (§3 KEEPs).
3. **The docs are a hazard, not just stale** (D11-01/02): they misdirect on exactly the surface the NG1/#3b decisions touch. Fix in Tranche A before B-work starts.
4. **A whole unit-test project is dark in CI** (D10-01): `Spaarke.Core.Tests` is in no `.sln` and no workflow invokes it.

---

## 1. Grade re-adjudication (synthesis vs first-pass)

Re-adjudicated against `docs/standards/CODE-QUALITY-RUBRIC.md` §3 using ONLY the Fable-verified findings (input discipline per r3 NFR-05).

| Dim | First-pass | Final | Change rationale |
|---|---|---|---|
| D1 | D+ | **D+** | All 3 HIGH findings confirmed in full (God classes, ISP/LSP split-brain, ADR-028 MUST violation). Material defects on the dimension per §3 D-band. |
| D2 | B– | **C+** | **Adjusted down.** §3's B-band requires "no latent broken path". The verified set carries a HIGH broken-path finding (D2-01 — always-failing cast, live via mapped endpoint + background job; bug sites are BFF files but the root seam is in-surface) AND a MEDIUM latent landmine (D2-02) whose only guard — narrow-interface DI routing — has already regressed once in production (GraphModule.cs:74-77 documents the shipped bug). C+ = notable debt, remediate soon; also consistent with the BFF row carrying the same bug at D2 C+. |
| D3 | B– | **B–** | All six confirmed. Four MEDIUM ADR-028 §24 violations are real posture debt but authenticated, KV-sourced, tracked (#3b) — no unauthenticated path, no exposed secret, no live injection. B-band holds. |
| D4 | B– | **B–** | All six confirmed (verification softened D4-02 slightly — Redis `CachedAccessDataSource` means the 3-RTT fires on cache miss, 60s TTL). Truncation + N+1 keep it at the low end of B. |
| D5 | C+ | **C+** | All four confirmed; verification refined the stub count (36 actual throws + 1 doc comment) and noted the deliberate fail-loud AssociateAsync seam — substance unchanged. |
| D6 | B– | **B–** | All eight confirmed; D6-01 (HIGH config-key trap) strengthened by `CONFIGURATION-MATRIX.md` contradiction. Real but non-blocking in current deployments. |
| D7 | C+ | **C+** | All seven confirmed, including both HIGHs (dark live contracts; zero-production-code test files). |
| D8 | B+ | **B+** | All three confirmed; the dominant A+ criterion (no HIGH CVEs) verifiably met. |
| D9 | C+ | **B–** | **Adjusted up.** One of the four pillars of the first-pass C+ (D9-03 correlation gap) was REFUTED — and the refutation is a positive: OTel/Azure Monitor + request Activity already join per-request logs, so "telemetry on critical paths" is substantially met. Remaining: 1 HIGH (live FullName PII), 2 MEDIUM (latent payload log; diagnostic noise at Information), 1 LOW — solid-with-debt, all S-effort fixes. |
| D10 | B– | **B–** | All seven confirmed. The dark test project is the worst item but has small blast radius (2 test files) and no broken build path exists. |
| D11 | D+ | **D+** | Both HIGHs confirmed in full — docs mandate a superseded anti-pattern naming a deleted class, and mislabel a live production component dormant. Material defect on the dimension. |

**Composition (§4.2)**: points = D1 1.3, D2 2.3, D3 2.7, D4 2.7, D5 2.3, D6 2.7, D7 2.3, D8 3.3, D9 2.7, D10 2.7, D11 1.3 → sum 26.3, equal-weight mean **2.39 → C+**. Gating cap = min(C+, D2 = C+, D3 = B–) = **C+**. The cap does not reduce the composed mean ⇒ **gating cap not binding**. **Surface grade: C+.**

---

## 2. Current-state inventory (verified findings)

Every finding below is Fable-CONFIRMED with independently re-checked file:line evidence. Effort: **S** ≤ ½ day · **M** 1–3 days · **L** task-cluster/multi-week. Risk = regression/contention risk of the remediation itself.

### 2.1 D1 — Architecture & boundaries

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D1-01 | HIGH | `DataverseServiceClientImpl` God class: 2,864 LOC, ~66-84 members, implements the full 9-interface composite | `Spaarke.Dataverse/DataverseServiceClientImpl.cs:18` | 2,864 | L | med | Split by the 9 already-defined narrow interfaces into focused impls registered independently; retire the monolith. **Fold into NG1 (task 011 / Phase B1).** |
| D1-02 | HIGH | `DataverseWebApiService` God class: 2,816 LOC with 37 `NotImplementedException` stubs; correctness rests on DI forwarding that already mis-wired once (GraphModule.cs:74-77) | `Spaarke.Dataverse/DataverseWebApiService.cs:2641` | 2,816 | M | med | Cheap compile-time fix independent of full NG1: narrow to `: IEventDataverseService, IFieldMappingDataverseService` (+ the concrete-only members its two wrapper consumers use) and delete the stubs — the compiler then enforces coverage. Phase B1. |
| D1-03 | HIGH | Dataverse outbound uses ClientSecret, not MI — ADR-028 §24 MUST violation (the #3b target) | `DataverseServiceClientImpl.cs:60`; `DataverseWebApiService.cs:56` | 10 | M | med | Add the ADR-028 MI cascade (DefaultAzureCredential when MI-enabled; ClientSecret local-dev only), mirroring `GraphClientFactory`; retire the `AuthType=ClientSecret` conn-string after MI validation. **Phase B2 (#3b).** |
| D1-04 | MED | `IDataverseService` facade leaks raw WCF `ServiceClient` via concrete-only property; **10** BFF consumers downcast (`is DataverseServiceClientImpl`) | `DataverseServiceClientImpl.cs:29` | 5 | M | med | Expose the needed generic ops through a narrow accessor interface (or the BFF-028 `UnwrapServiceClient` extension as interim); remove the concrete downcasts. Coordinate with **BFF task 028**. Phase B1. |
| D1-05 | LOW | `ScheduledJobHost` 917 LOC; scheduler-loop + manual-trigger stacks duplicate retry/dispatch (two retry methods, two `Task.Run` blocks) | `Spaarke.Scheduling/ScheduledJobHost.cs:47` | 917 | M | low | Extract one shared job-execution/retry helper used by both paths; optionally split manual-trigger orchestration into a collaborator. Phase A6 (optional) or backlog. |

### 2.2 D2 — Correctness & reliability

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D2-01 | HIGH | Always-failing `IDataverseService is ServiceClient` cast throws on EVERY call — live via finance-rollup HTTP endpoints + spend-snapshot job. Cross-surface: bug sites BFF; root seam in-surface | `Sprk.Bff.Api/Services/Finance/FinanceRollupService.cs:230` (+ `FinancialCalculationToolHandler.cs:140,206`) | 20 | S | low | Replace with the sibling idiom (`is DataverseServiceClientImpl impl → impl.OrganizationService`) — **owned by BFF tasks 021/028**. In-surface hardening: narrow accessor seam (D1-04) so the misuse can't compile. Phase A0 (cast fix, coordinate) + B1 (seam). |
| D2-02 | MED | Composite default binding throws `NotImplementedException` for the 6 Event/FieldMapping methods; correct behavior rests only on narrow-interface routing that regressed once before. No caller live-broken today (verified) | `DataverseServiceClientImpl.cs:1760` | 60 | M | med | Preferred: compile-time segregation (= D1-02). Interim (S/low): a DI-graph validation test asserting every narrow interface resolves to an impl with no throw-stub members. Phase A4 interim + B1 fix. |
| D2-03 | LOW | `GetDocumentsByContainerAsync` direct-indexes `data["sprk_documentid"]` while every sibling mapper guards with `TryGetValue` (no live throw — query has no `$select`) | `DataverseWebApiService.cs:783` | 2 | S | low | Use the sibling `TryGetValue` idiom. Phase A3. |
| D2-04 | INFO | Cross-dimension record: both shared-lib impls authenticate via ClientSecret — deterministic today (both config keys resolve to the same KV secret), so scored under D3, not D2 | `DataverseServiceClientImpl.cs:60` | 10 | S | low | No separate action — closes with Phase B2 (#3b). Recorded so the D2 pass isn't misread as clearing it. |

### 2.3 D3 — Security

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D3-01 | MED | `DataverseServiceClientImpl` `AuthType=ClientSecret` conn-string (prod `IDataverseService` singleton) — ADR-028 §24 violation, sanctioned #3b target | `DataverseServiceClientImpl.cs:60` | 25 | M | med | Under #3b: inject the DI TokenCredential (UAMI) / `AuthType=ManagedIdentity`; keep KV secret only for OBO. Phase B2. |
| D3-02 | MED | `DataverseWebApiService` bare `ClientSecretCredential`, no MI fallback — every outbound token secret-based | `DataverseWebApiService.cs:56` | 20 | M | med | Replace with DI-injected UAMI TokenCredential; drop the `Dataverse:ClientSecret` hard-requirement. Phase B2. |
| D3-03 | MED | `DataverseAccessDataSource` app-only branch prefers `ClientSecretCredential` (backs live SDAP authorization reads); the MI path exists only as the else-branch | `DataverseAccessDataSource.cs:55` | 15 | M | med | Split, don't flip: app-only branch → DI UAMI TokenCredential; retain the ClientSecret-backed CCA **solely** for OBO (§3 KEEP). Phase B2. |
| D3-04 | MED | `DataverseWebApiClient` prefers ClientSecret; its MI fallback only activates when `API_CLIENT_SECRET` is absent — which it never is (OBO) ⇒ MI branch dead in prod | `DataverseWebApiClient.cs:44` | 15 | S | med | Invert the branch: prefer injected UAMI TokenCredential; ClientSecret = explicit local-dev opt-in. Phase B2. |
| D3-05 | LOW | User AAD OID interpolated into OData `$filter` with no escaping/Guid guard (today's values are JWT-validated GUIDs — defense-in-depth gap, not live injection) | `DataverseAccessDataSource.cs:261` | 3 | S | low | `Guid.TryParse` + reject, or quote-escape (mirror `GetLatestAnalysisOutputByNameAsync`). Phase A3. |
| D3-06 | LOW | Stub access-check methods unconditionally return `DocumentAccessLevel.FullControl`; zero consumers (verified src+tests) — latent least-privilege landmine on a live interface | `DataverseServiceClientImpl.cs:1066` (+ `DataverseWebApiService.cs:786-796`) | 20 | S | low | Remove from the interface or make them throw `NotSupportedException`; all access checks stay on `IAccessDataSource`. Note: `GetDocumentAccessAsync` (:1059) is on NO interface (stale comment). Phase A3. |

### 2.4 D4 — Performance & scalability

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D4-01 | MED | Web-API list reads ignore `@odata.nextLink` ⇒ silent truncation at server page size (5,000). The private response DTO can't even observe a nextLink; SDK twin pages correctly (proving the "all rows" contract). Pattern live via Event/FieldMapping routings | `DataverseWebApiService.cs:780` (DTO :2621-2625; also `QueryFieldMappingProfilesAsync` :1745) | 25 | M | low | Add nextLink to the DTO + follow in a loop (or `Prefer: odata.maxpagesize` paging); at minimum `$top` + log-when-full. Phase A3 (or fold into B1 if the stack decision lands first). |
| D4-02 | MED | Authorization hot path: 3 independent Dataverse RTTs awaited sequentially; `DefaultRequestHeaders` mutation blocks safe parallelization (within-request hazard; Redis cache means this fires per cache-miss, 60s TTL) | `DataverseAccessDataSource.cs:203` (headers :92,:171) | 10 | M | med | First switch to per-request `HttpRequestMessage` auth headers (idiom already used at :324-327), then `Task.WhenAll` the three queries. Phase B4 (auth path = contested). |
| D4-03 | MED | `AssociateScopesAsync` N+1: one `AssociateAsync` RTT per id with a 1-element collection; SDK accepts full collections (3 calls total possible). Live path (IAnalysisDataverseService → SDK impl) | `DataverseServiceClientImpl.cs:452` | 20 | S | low | One `EntityReferenceCollection` per relationship; per-item retry only on batch fault (error aggregation already best-effort). Phase A3. |
| D4-04 | MED | Generic field update pays an uncached `RetrieveEntityRequest` metadata RTT per call (entity-set names are process-stable) | `DataverseServiceClientImpl.cs:1871` (helper :1079-1101) | 8 | S | low | Memoize logicalName→entitySetName in a bounded `ConcurrentDictionary`. Phase A3. |
| D4-05 | LOW | Six sites `Task.Run` over synchronous `ServiceClient.Execute` instead of `ExecuteAsync` — pins pool threads for network I/O (no deadlock; throughput debt) | `DataverseServiceClientImpl.cs:1091,1142,1242,1892,2127,2196` | 12 | S | low | Use the SDK async surface (already used elsewhere in the same class). Phase A3. |
| D4-06 | LOW | `InMemoryBackgroundJobStore` run history unbounded + O(n) scans; registered as the production singleton store with a live hourly job + manual triggers | `Spaarke.Scheduling/InMemoryBackgroundJobStore.cs:26` | 15 | S | low | Cap retained runs (ring buffer / per-job capped list, evict oldest completed). Phase A3. |
| D4-07 | LOW | Per-request `RequestCache` backed by plain `Dictionary` with unsynchronized check-then-set — double-invoke/corruption under intra-request fan-out; also blocks safely applying D4-02-style `WhenAll` patterns | `Spaarke.Core/Cache/RequestCache.cs:9` | 6 | S | low | `ConcurrentDictionary` + `Lazy<Task<T>>` `GetOrAdd`. **Do this BEFORE any intra-request parallelization work (B4 dependency).** Phase A3. |

### 2.5 D5 — DRY / dead code

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D5-01 | MED | Two full parallel `IDataverseService` impls (~5,680 LOC) with 37 dead throw-stubs on the Web-API stack; DI routes only 2 of 9 interfaces there. **The verified NG1 assess-then-decide input.** Two wrapper consumers of the concrete type call only concrete-only members (stubs remain unreachable) | `DataverseWebApiService.cs:2638` + `DataverseServiceClientImpl.cs:18` | 5,680 | L | high | NG1 decision (task 011): pick one stack, or narrow the Web-API class so the stubs delete compile-safely (= D1-02). Respect the deliberate fail-loud AssociateAsync seam comment (:2672-2689) — decide, don't blind-delete. **Phase B1, quiet window.** |
| D5-02 | MED | Double-checked-locking token cache copy-pasted line-for-line across three raw-HTTP clients (5-min skew, 30s semaphore, same scope derivation); `CreateAuthenticatedRequestAsync` also duplicated | `DataverseWebApiService.cs:75-109` = `DataverseWebApiClient.cs:69-103`; variant `DataverseAccessDataSource.cs:84-97` | 90 | M | med | Extract one `DataverseTokenProvider`/`AuthenticatedRequestFactory` in `Spaarke.Dataverse`; all three consume it. Natural companion to #3b. Phase B2. |
| D5-03 | MED | `ClientSecretCredential` construction duplicated across four types with inconsistent config keys (`API_CLIENT_SECRET` ×3 vs `Dataverse:ClientSecret` ×1); `DataverseAccessDataSource:72` already shows the target DI-TokenCredential pattern | `DataverseServiceClientImpl.cs:41` (+3 siblings) | 70 | M | med | Centralize credential construction in one factory with one canonical key set; fold #3b into that ONE place instead of four. Phase B2. |
| D5-04 | LOW | Superseded `DataverseService` orphan (461 LOC) retained under `_archive/` — build-excluded, zero references (verified src+tests+DI) | `Spaarke.Dataverse/_archive/DataverseService.cs.archived-2025-10-01:17` | 461 | S | low | Delete (git history preserves). Phase A5. |

### 2.6 D6 — Consistency & conventions

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D6-01 | HIGH | Same logical secret read under two config keys: `Dataverse:ClientSecret` (WebApiService, hard-required, constructor throws) vs canonical `API_CLIENT_SECRET` (3 siblings). No bridging code exists; `CONFIGURATION-MATRIX.md:128,134` even documents `Dataverse:ClientSecret` as "local dev only" — contradicting the unconditional requirement. A deployment setting only the canonical key breaks Event/FieldMapping at first use | `DataverseWebApiService.cs:40` (throw :52) | 4 | S | med | Standardize all four classes on `API_CLIENT_SECRET` (fold into #3b so keys are touched once). Deployment-coordinated change. Phase B2 (or a minimal A-tranche key-fallback shim if B2 slips). |
| D6-02 | MED | Four divergent credential-acquisition mechanisms for the identical concern (conn-string / bare CSC / CSC+MI-fallback / CSC-else-DI-TokenCredential) | `DataverseServiceClientImpl.cs:60` (+3 siblings) | 30 | M | med | Converge on one UAMI-pinned factory (= D5-03). Phase B2. |
| D6-03 | MED | Composite split into two disjoint partial impls with mutual `NotImplementedException` holes; correctness held by DI forwarding convention (structural root cause of D2-02; GraphModule itself labels the prior omission an anti-pattern) | `DataverseServiceClientImpl.cs:1760` | 60 | M | med | Each impl declares only the narrow interfaces it actually serves (= D1-02/D2-02 fix). Phase B1. |
| D6-04 | MED | `GetUserAccessAsync` parameter names swapped vs the interface contract (`(resourceId, userId)` vs declared `(userId, documentId)`); plus an orphan `GetDocumentAccessAsync` on no interface, zero refs | `DataverseServiceClientImpl.cs:1069` (orphan :1059) | 15 | S | low | Rename params to match the contract; drop the orphan overload (with D3-06). Phase A3. |
| D6-05 | MED | Routine per-record mapping trace at `LogWarning` (`[DATAVERSE-DEBUG] MapToDocumentEntity …` incl. matter/project/invoice GUIDs) on every list-read row; sibling impl logs the equivalent at Debug. *(Also reported as D9-04 — counted once.)* | `DataverseWebApiService.cs:833` | 3 | S | low | Demote to `LogDebug` or remove. Phase A2. |
| D6-06 | LOW | Ad-hoc bracket-prefix taxonomy (`[DATAVERSE]`/`[DATAVERSE-DEBUG]`/`[DATAVERSE-API]`/`[UAC-DIAG]`) + debug-semantic content at Information; no convention exists in CODING-STANDARDS.md | `DataverseServiceClientImpl.cs:102` | 20 | S | low | ILogger scopes/EventIds or one agreed prefix; Information reserved for lifecycle events. Phase A2 (with D9-05). |
| D6-07 | LOW | Misleading comments claim `ClientSecretCredential` but the class uses a `ServiceClient` conn-string — matters precisely for the #3b reader | `DataverseServiceClientImpl.cs:14` (+ :41) | 3 | S | low | Correct to "ServiceClient with AuthType=ClientSecret connection string". Phase A1. |
| D6-08 | LOW | 27 methods in WebApiService open with two blank lines; sibling impl has zero — pervasive cosmetic asymmetry | `DataverseWebApiService.cs:176` | 20 | S | low | Formatter/editorconfig pass. Phase A5. |

### 2.7 D7 — Testability & test quality

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D7-01 | HIGH | 10 Skip-gated tests ("CI cron-tick flake — needs TimeProvider refactor") leave the scheduler's live retry-success/retry-exhaustion and enable/disable-dispatch contracts with NO active coverage (all substitutes verified absent) | `tests/unit/Spaarke.Scheduling.Tests/ScheduledJobHostTests.cs:114` (+ `RetryAndIdempotencyTests.cs:41,81,117,296`) | 250 | M | low | Drive deterministically via the existing `TimeProvider` ctor seam (`ScheduledJobHost.cs:70`) + `FakeTimeProvider`; un-skip. Phase A4. |
| D7-02 | HIGH | `DataverseWebApiThreadSafetyTests` exercises ZERO production code — inline reimplementations + framework assertions, one self-described non-deterministic. False confidence about the NG1 raw-HTTP stack | `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DataverseWebApiThreadSafetyTests.cs:66` | 215 | S | low | Delete per ADR-038 scaffolding bans; if the concern needs protection, add an integration test driving the real service concurrently. Phase A4. |
| D7-03 | MED | `DataverseWebApiWireMockContractTests`: all 4 tests Skip'd AND exercise no production code (bare HttpClient → WireMock echoes its own config). Net: NG1 raw-HTTP stack has zero trustworthy contract coverage, in a KEEP path | `tests/integration/contract/Integration/DataverseWebApiWireMockContractTests.cs:29` | 115 | M | low | Delete, or replace with a contract test that drives `DataverseWebApiService` against a stubbed transport. Phase A4. |
| D7-04 | MED | ADR-038 §5 TimeProvider mandate unmet: Stopwatch + `Task.Delay` polling + CI 5× multipliers throughout Scheduling tests, while every host construction omits the existing provider param | `ScheduledJobHostTests.cs:225` (helper :563-576) | 40 | M | low | Same work as D7-01 — inject `FakeTimeProvider`, advance virtual time. Phase A4. |
| D7-05 | MED | The live outbound CRUD path (ClientSecret `ServiceClient`) has no behavioral test — sealed `ServiceClient` forces interface-boundary mocking; only the pure stager is covered. #3b lands on an untested path | `DataverseServiceClientImpl.cs:60` | 30 | M | med | Add a live/integration smoke (or thin seam over `ServiceClient`) as the #3b regression anchor — sequence WITH Phase B2. |
| D7-06 | LOW | Cache TTL test crosses a 50ms TTL with real-clock `Task.Delay(150)` (ADR-038 §5 flake pattern; `MemoryDistributedCache` has no TimeProvider injection point — localized nit) | `tests/unit/Spaarke.Core.Tests/Cache/DistributedCacheExtensionsTests.cs:105` | 10 | S | low | Injectable clock / advanceable double, or explicitly accept as low-risk timing. Phase A4. |
| D7-07 | LOW | `Register_NullJob_Throws` = ADR-038 §7 B4 banned null-check scaffolding; plus an archived test file retained in-tree (`JobProcessorTests.cs.archived-2025-10-14`, BFF tree — already in BFF task 020's scope) | `tests/unit/Spaarke.Scheduling.Tests/ScheduledJobRegistryTests.cs:56` | 8 | S | low | Delete the null-arg test; confirm the archived file goes with BFF 020. Phase A4. |

### 2.8 D8 — Dependency & supply-chain hygiene

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D8-01 | MED | `Directory.Packages.props` has CPM **disabled** (line 3) yet carries ~60 stale net8-era `PackageVersion` entries contradicting the real inline pins (Dataverse.Client 1.1.32 vs 1.2.26, Azure.Identity 1.16.0 vs 1.17.1, MSAL 4.77 vs 4.87, stale STJ CVE comment) — an inert manifest that reads authoritative. *(= D10-03.)* | `Directory.Packages.props:3` | 62 | M | med | Either enable CPM and move the real pins in (repo-wide restore change — quiet window, coordinate with **task 032**), or delete the file. Phase B3. |
| D8-02 | LOW | Lib↔host pin drift: libs pin Azure.Identity 1.17.1 / Extensions.* 10.0.1 while the composing BFF pins 1.21.0 / 10.0.3 (nearest-wins heals at composition; no NU1605) | `Spaarke.Dataverse.csproj:13` | 4 | S | low | Align lib pins to the host-resolved versions (or govern from one source via B3's CPM decision). Phase A5. |
| D8-03 | LOW | No NuGet lockfiles anywhere (`RestorePackagesWithLockFile` unset; CI does not enforce locked restore) — transitive graph floats between restores | `Directory.Build.props:2` | 0 | S | med | Set `RestorePackagesWithLockFile=true` + commit `packages.lock.json` for the shared libs; refresh on intentional changes. CI-behavior change → Phase B3, coordinate with task 032 + ci-cd owners. |

### 2.9 D9 — Observability

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D9-01 | HIGH | User full name (PII) logged at Information on the LIVE authorization path (every uncached access resolution) | `DataverseAccessDataSource.cs:282` | 2 | S | low | Drop `FullName` from the log (systemuserid + oid GUIDs suffice), or demote+redact. Phase A2. |
| D9-02 | MED | Full email payload (body/from/to/cc/subject) JSON-serialized and logged at Information in `UpdateDocumentAsync`. **Latent** — verified zero production callers route document writes to this impl today; one-line DI routing flip activates it | `DataverseWebApiService.cs:714` | 12 | S | low | Log field-name keys only (never values), demote to Debug, redact email*. Phase A2. |
| D9-05 | MED | Temporary diagnostic logging (`[DATAVERSE-DEBUG]`/`[UAC-DIAG]`/`[DATAVERSE-API]`) shipped at Information across the surface, incl. the live auth path and 20-attribute-key dumps | `DataverseAccessDataSource.cs:155` | 15 | S | low | Demote to `LogDebug`; drop ad-hoc prefixes for standard structured fields (with D6-06). Phase A2. |
| D9-06 | LOW | Raw Dataverse error response body logged verbatim at Error (can echo submitted attribute values incl. email PII); live variant on the auth path is low-PII ($select id only) | `DataverseWebApiService.cs:733` (+ `DataverseAccessDataSource.cs:334-338`) | 4 | S | low | Log bounded status + Dataverse error code; gate raw body behind Debug. Phase A2. |

> D9-03 (correlation gap) was **refuted** — see §4. Do not re-file; do not add `BeginScope` correlation plumbing as a "fix" (Activity/OTel already joins per-request logs; client `X-Correlation-Id` stamping is enhancement-only).

### 2.10 D10 — ALM / build hygiene

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D10-01 | HIGH | `Spaarke.Core.Tests` absent from `Spaarke.sln` ⇒ executes in **NO CI job** (all workflows verified sln-scoped or filtered elsewhere); sibling Scheduling.Tests does run | `Spaarke.sln:30` | 0 | S | low | Add to the sln (blocking-tier execution follows automatically via tier2's sln-scoped `dotnet test`); if a workflow edit is wanted, coordinate with `ci-cd-unit-test-remediation-r1` (owns `.github/workflows`). Phase A5. |
| D10-02 | MED | `Spaarke.Core` + `Spaarke.Dataverse` production projects are not sln members (compile only transitively) — solution-scoped ops silently omit 2 of the 3 surface libs | `Spaarke.sln:26` | 0 | S | low | Add both csprojs to the `shared` solution folder. Phase A5. |
| D10-03 | MED | Disabled-CPM manifest contradiction — same artifact as D8-01; a future `true` flip would silently downgrade the libs to net8-era packages | `Directory.Packages.props:3` | 60 | M | med | = D8-01 (counted once in effort). Phase B3. |
| D10-04 | MED | 461 lines of archived production source tracked in-tree (`_archive/DataverseService.cs.archived-2025-10-01`) — inert but invites resurrection. *(= D5-04.)* | `_archive/DataverseService.cs.archived-2025-10-01:1` | 461 | S | low | Delete; rely on git history. Phase A5. |
| D10-05 | LOW | Crypto pins (S.S.C.Pkcs/Xml 10.0.11) + rationale comments copy-pasted across all three csprojs — 3-place bump risk re-exposing the 8 HIGH Xml advisories if one is missed | `Spaarke.Core.csproj:22` | 12 | S | low | One shared MSBuild import (or B3's CPM) defines the security override once. **KEEP the pins themselves** (net10 handoff: non-web libs don't get the web shared framework). Phase A5. |
| D10-06 | LOW | Repo-wide `WarningsNotAsErrors` downgrades CS8601/CS8604/CS1998 on nullable-enabled libs — documented managed debt, but a partial retreat from analyzers-as-errors | `Directory.Build.props:33` | 1 | M | med | Fix per-site occurrences then remove the two nullable codes — **repo-wide scope**, sequence with task 041's analyzers baseline. Phase B3. |
| D10-07 | LOW | Five empty `.gitkeep` scaffolding folders imply structure that doesn't exist (e.g. `Interfaces/` while interfaces sit at project root) | `Spaarke.Core/Constants/.gitkeep:1` | 0 | S | low | Remove the placeholder folders. Phase A5. |

### 2.11 D11 — Knowledge/doc accuracy

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D11-01 | HIGH | `DataverseWebApiService` documented "not currently used" (README + TECHNICAL-OVERVIEW, 5 sites) while registered and serving Event + FieldMapping in production — a live ClientSecret consumer mislabeled dormant, directly material to #3b | `Spaarke.Dataverse/docs/TECHNICAL-OVERVIEW.md:152` | 5 | S | low | Rewrite: active production impl for Event/FieldMapping; second ClientSecret consumer pending #3b. Phase A1. |
| D11-02 | HIGH | Surface `CLAUDE.md` mandates registering concrete `DataverseService` (deleted 2025-10; exists only in `_archive`) and labels the interface registration production actually uses "❌ WRONG" | `src/server/shared/CLAUDE.md:63` | 8 | S | low | Rewrite the ADR-010 DI example to the real `IDataverseService` → `DataverseServiceClientImpl` factory pattern + 9 forwarders. Phase A1. |
| D11-03 | MED | 8 dangling links to `Spaarke.Core/docs/TECHNICAL-OVERVIEW.md` which does not exist (one doubly-broken relative path) | `Spaarke.Core/README.md:11` | 8 | S | low | Create the target or repoint/inline. Phase A1. |
| D11-04 | MED | Stale registration reference: `Spe.Bff.Api/Program.cs:269-274` (wrong project name + wrong file) vs real `Sprk.Bff.Api/Infrastructure/DI/GraphModule.cs:46` — repeated ×3 | `Spaarke.Dataverse/docs/TECHNICAL-OVERVIEW.md:106` | 3 | S | low | Correct path + anchor throughout. Phase A1. |
| D11-05 | MED | Doc describes a fictional flat "16-method" `IDataverseService` and claims "both implementations guarantee identical behavior" — the real interface is a 9-interface composite and the impls hold mutual throw-stubs | `Spaarke.Dataverse/docs/TECHNICAL-OVERVIEW.md:244` | 40 | S | low | Document the real ISP composite + the capability split (and its NG1 status). Phase A1. |
| D11-06 | MED | "Security Best Practices" endorses ClientSecret-in-KV as THE production pattern with zero Managed-Identity/ADR-028 mention — drifts from the canonical BFF auth doc and from code that already supports MI | `Spaarke.Dataverse/docs/TECHNICAL-OVERVIEW.md:716` | 20 | S | low | Add the ADR-028 MI-canonical note + #3b pending status; align with BFF `CLAUDE.md` auth section. Phase A1. |
| D11-07 | LOW | Surface `CLAUDE.md` omits `Spaarke.Scheduling` entirely and diagrams empty/nonexistent directories (`Services/`, `Models/`, `Extensions/`) | `src/server/shared/CLAUDE.md:9` | 15 | S | low | Add Scheduling to the module overview; correct the directory diagram to the real layout. Phase A1. |

---

## 3. Explicit KEEPs (verified — remediation MUST NOT act against these)

1. **The OBO ConfidentialClient half of `DataverseAccessDataSource`** (`:59-63`, `AcquireTokenOnBehalfOf` `:118`) — legitimately requires a client secret for user-context OBO token exchange. **#3b migrates only the app-only branch** (surface map §E.1 line 192: "MUST survive #3b").
2. **The `API_CLIENT_SECRET` KV secret (`BFF-API-ClientSecret`) is NEVER-REMOVE** — shared with OBO (`GraphClientFactory`); #3b retires code paths, not the secret.
3. **`DataverseAccessDataSource.cs:72` DI-TokenCredential else-branch** — this IS the target UAMI pattern; B2 extends it, does not replace it.
4. **The fail-loud `AssociateAsync` stub seam** (`DataverseWebApiService.cs:2672-2689`) — explicitly documented as a deliberate fail-loud seam; its fate is an NG1 (task 011) decision, not a blind deletion.
5. **`CachedAccessDataSource` Redis wrapper** (`SpaarkeCore.cs:59-67`) — the live mitigation for D4-02; any auth-path perf work preserves it.
6. **The deliberate mirroring comments in `ScheduledJobHost`** ("mirrors ExecuteWithRetryAsync's contract", "same pattern as DispatchAndAdvance") — D1-05 consolidates via a shared helper; it does not remove either execution path's semantics.
7. **The shared-lib crypto pins `System.Security.Cryptography.{Xml,Pkcs}` 10.0.11** — deliberate CVE closure (net10 handoff: KEEP; the 3 non-web libs don't receive the web shared framework). D10-05 centralizes them; nothing removes them. Do NOT re-add `NoWarn=NU1903`.
8. **Correlation plumbing** — per the D9-03 refutation, OTel/Azure Monitor + request Activity already join per-request logs. Do NOT add `BeginScope` scaffolding as a "fix".

## 4. Refuted by verification (record-only — do NOT act on; do NOT re-claim in future passes)

| ID | Claim | Why refuted |
|---|---|---|
| D9-03 | "Dataverse access stacks propagate no correlation ID / log scope, so logs from a single BFF request cannot be correlated" | Grep facts replicate, but the impact claim is falsified by host wiring: `Program.cs:33-35` wires `AddOpenTelemetry().UseAzureMonitor()` and `AzureMonitorGuard` fail-fasts in non-Development without the App Insights connection string. The Azure Monitor OTel distro stamps ILogger records with the ambient request Activity TraceId/SpanId (operation_Id join), and the Dataverse services log within that async flow — per-request join already works without `BeginScope`. The finding's own remediation option ("rely on Activity/W3C trace propagation") is already implemented. Residual client-supplied `X-Correlation-Id` stamping = enhancement, not a defect. |

(This section mirrors the BFF pass's KEEPs discipline: recorded so future passes don't re-claim it.)

## 5. Data-driven-dispatch pre-check list (NFR-08)

**No verified finding on this surface carries `requiresDataverseCheck=true` — zero live-Dataverse pre-checks are required before the remediations above.** Verified basis: implementation selection in this surface is **static DI** (`GraphModule.cs:46-81` — grep-provable), not `sprk_*` row dispatch; the archived `DataverseService` is not compiled into any assembly so no class-name dispatch can reach it; and D3-06 / D6-04 interface cleanups touch BFF-class methods, not handler-class rows.

Standing NFR-08 guardrails that still bind remediation:
- If any B1 refactor **renames a class or handler string reachable from BFF AI tool dispatch**, the BFF-side pre-check applies first (`sprk_analysistool.sprk_handlerclass` + the `/api/ai/tools/handlers` class-name discovery — see BFF design "Data-driven dispatch" section). No such rename is currently proposed.
- Never touch a `HandlerId` string.

## 6. Proposed workstreams → phases (A/B tranche split per r3 NFR-04)

**Tranche A — low-contention bugs/hygiene first.** Small, behavior-preserving or doc/test-only PRs off the r3 branch; `/conflict-check` each.

- **Phase A0 — Cast-fix coordination (S).** Confirm BFF tasks 021/028 cover all three broken `is/as ServiceClient` sites (D2-01) + the 10-consumer downcast consolidation (D1-04 interim). No duplicate work here; this surface's part is the B1 seam.
- **Phase A1 — Docs & knowledge accuracy (all S/low; do FIRST — the current docs misdirect the B-tranche work).** D11-01..07, D6-07. Rewrites `src/server/shared/CLAUDE.md` + `Spaarke.Dataverse/docs/*` + `Spaarke.Core/README.md`.
- **Phase A2 — Log hygiene & PII (all S/low).** D9-01 (live PII — first), D9-02, D9-05, D9-06, D6-05, D6-06.
- **Phase A3 — Defensive edges + micro-perf (S/low except D4-01 M).** D2-03, D3-05, D3-06, D6-04, D4-03, D4-04, D4-05, D4-06, **D4-07 (prerequisite for any later fan-out work)**, D4-01 (nextLink paging — fold into B1 instead if the stack decision lands first).
- **Phase A4 — Test trustworthiness (TEST-MODIFYING rigor override — code-review + adr-check unconditional).** D7-01 + D7-04 (FakeTimeProvider + un-skip 10 tests; note: `Microsoft.Extensions.TimeProvider.Testing` is a **test-only** package — no publish-size impact, flag per §10 rule anyway), D7-02 (delete scaffolding file), D7-03 (delete/replace WireMock echoes), D7-06, D7-07, D2-02-interim (DI-graph validation test asserting no narrow interface resolves to a throw-stub impl).
- **Phase A5 — Solution & repo hygiene (all S/low).** D10-01 (sln add ⇒ Spaarke.Core.Tests runs in tier2; workflow edits, if any, coordinate with `ci-cd-unit-test-remediation-r1`), D10-02, D10-04/D5-04 (delete archive), D10-05 (centralize crypto pins — pins themselves KEEP), D10-07, D8-02, D6-08.
- **Phase A6 (optional) — Scheduling host tidy (M/low).** D1-05 shared retry/dispatch helper. Defensible to defer to backlog.

**Tranche B — wide/contested edits, quiet window only.** Each rides an owner decision; sequence after Tranche A and after `/conflict-check` against the active-worktree registry.

- **Phase B1 — NG1 stack decision + composite segregation (L/high; owner decision on task 011).** D5-01, D1-01, D1-02, D6-03, D2-02 (permanent fix), D1-04 (narrow accessor seam replacing the concrete downcast idiom — supersedes the BFF-028 extension), D4-01 (surviving stack must page). Decision options: (a) one stack absorbs the other; (b) keep both but compile-time-segregate (each impl declares only what it serves; delete all 37+6 stubs; DI forwarding becomes type-checked). Either option kills the D2-02 landmine class.
- **Phase B2 — #3b ClientSecret→MI migration (M–L/med; same track as B1 per owner 2026-08-13).** D1-03, D3-01, D3-02, D3-03 (app-only branch only — OBO KEEP), D3-04, D5-02, D5-03, D6-01, D6-02; closes D2-04. **D7-05's integration smoke lands FIRST as the regression anchor.** One credential factory, one canonical config key (`API_CLIENT_SECRET`), UAMI (`mi-bff-api-{env}`); dev-only verification for now (only `spaarke-dev` live). Re-verify all file:line anchors at head before execution (`DataverseServiceClientImpl.cs` changed on net10 master).
- **Phase B3 — Repo-wide build governance (M/med; coordinate with task 032 + 041 + ci-cd owners).** D8-01/D10-03 (CPM: enable-and-reconcile, or delete the inert manifest), D8-03 (lockfiles + locked-mode restore), D10-06 (fix CS8601/CS8604 sites, re-arm).
- **Phase B4 — Auth hot-path perf (M/med; contested — authorization path).** D4-02: per-request auth headers, then `Task.WhenAll` the 3 queries. Depends on D4-07 (done in A3). Preserve `CachedAccessDataSource` (KEEP #5).
- **Wrap-up.** `090-wrapup` task → `/test-diet` gate (A4 touched `tests/**`) + doc-drift audit + SCORECARD row confirmation.

## 7. Coordination & governance

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- Spaarke.Core/Dataverse are BFF-consumed shared assemblies; B1/B2 change BFF-composed code -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows> <!-- A5 (sln/CI test membership) + B3 (locked restore) may touch workflows — coordinate with ci-cd-unit-test-remediation-r1, which owns .github/workflows -->
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

- **BFF §10 applies to every task** (shared libs are consumed by the BFF): publish-size check ≤60 MB (baseline 44.96 MB incl PDBs, net10) on each BFF-composed change; **no new production NuGet packages** (the one test-only package in A4 is flagged above); `/conflict-check` before every PR (19 active worktrees touch BFF; the `Spaarke.Dataverse` files are the highest-contention items in this design).
- **Ownership overlaps (do not duplicate):** broken casts + `UnwrapServiceClient` → BFF tasks **021/028**; NG1 + #3b decision → task **011** (this document is its verified input); #3a app-reg drop → task **060**; dependency/CVE + deferred-majors backlog → task **032** (B3 coordinates, does not re-pin — zero-vulnerable state is KEEP); analyzers baseline → task **041**; archived BFF test file (D7-07 note) → BFF task **020**.
- **DI changes (B1/B2) must keep the DI graph clean** — `DiGraphValidationTests` is MAINTAIN/KEEP; do not disable `ValidateOnBuild`/`ValidateScopes` (net10 H2).
- **Placement justification (§10/§11):** the only new surfaces proposed are (1) a narrow `ServiceClient`-accessor/generic-ops interface (B1) — replaces 10 concrete downcasts + kills a live bug class; (2) one `DataverseTokenProvider` credential/token factory (B2) — replaces 4 divergent credential blocks + 3 verbatim token caches; (3) a DI-graph validation test (A4) — compile/CI-time enforcement of the routing convention that already shipped one production bug. Each names the concrete failing behavior it prevents; everything else is deletion, doc-fix, or in-place consolidation.

## 8. SCORECARD row inputs (for `notes/SCORECARD.md` — appended by the invoking task, NOT by this document)

**Row**: Shared server libs (Spaarke.Core/Dataverse/Scheduling) — D1 **D+** · D2 **C+** · D3 **B–** · D4 **B–** · D5 **C+** · D6 **B–** · D7 **C+** · D8 **B+** · D9 **B–** · D10 **B–** · D11 **D+** → equal-weight mean 26.3/11 ≈ 2.39 → C+; gating cap min(C+, D2 C+, D3 B–) = **C+** (cap not binding). **Surface grade: C+.**

Evidence bullets:

- **D1 D+** — Two ~2,800-LOC God classes (`DataverseServiceClientImpl.cs:18` = 2,864 LOC; `DataverseWebApiService.cs` = 2,816 LOC) implement one 9-interface composite; ADR-028 §24 MI MUST violated on all Dataverse outbound (`DataverseServiceClientImpl.cs:60`); the facade is defeated by a raw `ServiceClient` leak downcast by 10 BFF consumers (D1-01/02/03/04).
- **D2 C+** — Always-failing `is ServiceClient` cast live on the finance-rollup endpoint + spend-snapshot job (`FinanceRollupService.cs:230`; root seam in-surface) plus a latent `NotImplementedException` landmine on the composite binding (`DataverseServiceClientImpl.cs:1760`) whose DI-routing guard already regressed once in production (`GraphModule.cs:74-77`) (D2-01/02).
- **D3 B–** — Fail-closed authorization and KV-sourced secrets verified, but all four outbound Dataverse clients authenticate via ClientSecret contra ADR-028 §24 (tracked #3b: D3-01..04); two LOW latent items (unescaped OID filter `DataverseAccessDataSource.cs:261`; FullControl stubs `DataverseServiceClientImpl.cs:1066`).
- **D4 B–** — Web-API reads cannot observe `@odata.nextLink` (silent truncation, `DataverseWebApiService.cs:780`/DTO :2621); sequential 3-RTT auth path (`DataverseAccessDataSource.cs:203`, Redis-mitigated at 60s TTL); per-id associate N+1 (`DataverseServiceClientImpl.cs:452`); per-call metadata RTT (:1871) (D4-01..04).
- **D5 C+** — ~5,680 LOC of parallel `IDataverseService` twins with 37 unreachable throw-stubs (`DataverseWebApiService.cs:2638` — the verified NG1 input); token-cache logic copy-pasted ×3; credential construction ×4; 461-LOC archived orphan retained (D5-01..04).
- **D6 B–** — Same logical secret under two config keys with a live constructor-throw trap (`DataverseWebApiService.cs:40` vs canonical `API_CLIENT_SECRET`); four divergent credential patterns; interface param names swapped in one impl; routine per-record trace at `LogWarning` (D6-01/02/04/05).
- **D7 C+** — 10 Skip-gated tests leave the scheduler's live retry + enable/disable contracts with no active coverage (`ScheduledJobHostTests.cs:114`); two whole test files exercise zero production code (`DataverseWebApiThreadSafetyTests.cs`, `DataverseWebApiWireMockContractTests.cs`); ADR-038 §5 TimeProvider mandate unmet despite the production seam existing (`ScheduledJobHost.cs:70`) (D7-01..04).
- **D8 B+** — `dotnet list package --vulnerable --include-transitive` = zero across all three libs (dominant A+ criterion met); debts: inert stale `Directory.Packages.props` (CPM off, `:3`), minor lib↔host pin drift, no lockfiles (D8-01..03).
- **D9 B–** — Up from first-pass C+: the correlation-gap claim was REFUTED (OTel/Azure Monitor + request Activity already join per-request logs); remaining: live FullName PII at Information on the auth path (`DataverseAccessDataSource.cs:282`), latent full-email-payload log (`DataverseWebApiService.cs:714`), diagnostic-tagged Information noise (D9-01/02/05).
- **D10 B–** — `Spaarke.Core.Tests` absent from `Spaarke.sln` and runs in NO CI job (`Spaarke.sln:30`); Core+Dataverse csprojs not sln members; disabled-CPM manifest contradicts real pins; 461-line archive tracked in-tree (D10-01..04).
- **D11 D+** — Live `DataverseWebApiService` documented "not currently used" while serving Event/FieldMapping in production (`TECHNICAL-OVERVIEW.md:152`); surface `CLAUDE.md:63` mandates registering a deleted class and labels the actual production DI pattern "WRONG"; 8 dangling doc links; fictional flat-interface description (D11-01..05).

---

*Assessment complete. Remediation is operator-gated: Tranche A tasks may be created immediately; Tranche B rides the task-011 (NG1/#3b) owner decision and quiet-window scheduling per r3 NFR-04.*
