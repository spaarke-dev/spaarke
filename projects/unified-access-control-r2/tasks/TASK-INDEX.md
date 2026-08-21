# TASK-INDEX — `unified-access-control-r2`

> **58 tasks** across 6 phases · generated 2026-08-21 · `scripts/Validate-TaskPoml.ps1`: **PASS** (57 clean, 0 errors, 1 benign WARN on 090)
> Source: [`spec.md`](../spec.md) 32 FRs / 7 NFRs · [`plan.md`](../plan.md) · [`CLAUDE.md`](../CLAUDE.md)
> Status legend: 🔲 pending · 🔄 needs retry · ✅ complete

Number gaps (020–029, 045–049, 059, 070–079, 084–089) are intentional insertion room.

---

## ⚠️ Read before executing anything

| Rule | Why |
|---|---|
| **001 blocks every Phase 0 code task** | NFR-07 — the access-path test baseline is near-zero. Characterize before changing behaviour |
| **034 is a blocking merge gate for 036** | NFR-04 — if impersonation is inert the query silently returns org-wide rows. Equality between impersonated and app-only = fail |
| **008 (delegation) must ship before 063/065** | Otherwise the PCF "+ User" button is one-click privilege escalation on a confidential matter |
| **030 before 032 · 031 before 035/036 · 040 before 041** | ADR amendments sanction the shape; path B means the amendment merges before or alongside dependent code |
| **030 / 031 / 040 are main-session-only** | They edit `.claude/**`. Sub-agents cannot write there (root CLAUDE.md §3) — "Edit denied" is the boundary working |
| **`/conflict-check` before EVERY BFF PR** | Surface shared with shipped `SPA-external-access-platform-r1/r2` + `teams-app-r1`; draft `SPA-r3` must be notified |

---

## Phase 0 — Enforcement remediation (19 tasks)

| # | Task | FR / finding | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| ✅ 001 | Access-path characterization + negative suite | NFR-07 | — | **P0-W0** | ✅ | sonnet | high |
| 🔲 002 | Authorize document download | FR-01 / A-1 | 001 | — | ❌ | sonnet | high |
| ✅ 003 | `OperationAccessPolicy` keys + completeness test | FR-03 / A-3,A-20 | 001 | — | ❌ | sonnet | high |
| ✅ 004 | `AuthorizationService` caller-scoped | FR-02 / A-2 | 001,003,014 | — | ❌ | **opus** | **xhigh** |
| 🔲 005 | Lift the Read ceiling | FR-04 / A-20 | 001,004 | — | ❌ | sonnet | high |
| ✅ 006 | Caller-scoped `PermissionsEndpoints` | FR-05 / A-4 | 001,004 | — | ✅ | sonnet | high |
| 🔲 007 | Enforce grant expiry in the read filter | FR-06 / A-5 | 001 | — | ❌ | sonnet | high |
| 🔲 008 | Delegation rule — Write-on-target | FR-07 / A-6 | 001 | — | ❌ | sonnet | high |
| 🔲 009 | Scope-check external To Do PATCH (+H-8a) | FR-08 / A-7 | 001 | — | ❌ | sonnet | high |
| 🔲 010 | Idempotent grant + revoke-all | FR-09 / A-11 | 001 | — | ❌ | **opus** | **xhigh** |
| 🔲 011 | Reject same-entity self-join | FR-10 / A-17 | 001 | — | ❌ | sonnet | high |
| 🔲 012 | Track or disable anonymous share links | FR-11 / A-14 | 002 | — | ❌ | sonnet | high |
| 🔲 013 | Workforce email `oid` no-hijack | FR-12 / A-18 | 001 | — | ❌ | sonnet | high |
| ✅ 014 | Cache key includes auth mode | FR-13 / A-19 | 001 | **P0-B** | ✅ | sonnet | high |
| 🔲 015 | Deterministic + complete membership paging | FR-14 / A-10 | 001 | — | ❌ | sonnet | high |
| 🔲 016 | Close-project cascade (contact + org) | FR-15 / A-12 | 001 | — | ❌ | sonnet | high |
| 🔲 017 | SPE revoke matcher + H-8b relic | FR-16 / A-13 | 001,010 | — | ❌ | sonnet | high |
| 🔲 018 | Remove dead filter + bound `in`-clause | FR-17 / A-15,A-16 | 001 | — | ❌ | sonnet | high |
| ✅ 019 | Fix `LookupUserMembership` `["*"]` | FR-17 / A-22 | 001 | **P0-B** | ✅ | sonnet | high |

**Critical path**: 001 → {003, 014} → 004 → {005, 006} · plus 001 → 010 → 017 · plus 002 → 012

> **Task 001 outcome (2026-08-21)**: 62 tests green at `tests/integration/auth/UnifiedAccessControl/`
> (the ADR-038 §2 security-auth KEEP path — **first backfill**; it had zero compiled files and was
> globbed by no csproj). **9 of 20 Phase 0 findings pinned, 1 partial, 10 not reachable offline.**
> Tasks 002/003/004/005/006/008/010/011/014 have their baseline and are unblocked. Tasks
> **007, 012, 013, 015, 016, 017, 018, 019** must supply their own coverage — see
> [`notes/task-001-untestable-findings.md`](../notes/task-001-untestable-findings.md) §2–3 for why and
> the recommended approach (extract a query-builder seam inside each fix task).
> ⚠️ Any task testing `/api/v1/external` MUST use `ExternalCollaborationTestFixture` — the shared
> fixtures make that group return 500, which silently turns "not 403" assertions into vacuous passes.

> **Wave P0-B outcome (2026-08-21)** — 014 + 019 executed **in parallel** (the only file-disjoint pair in
> Phase 0). No file overlap; post-wave build + full suite verified by the orchestrator.
> **014**: key is now `sdap:auth:access:{authMode}:{userId}:{resourceId}` (`sp`/`obo`), never the raw token.
> Escalation evaluated and correctly did NOT fire — `userId` IS the caller's `oid`, so two OBO callers
> already differ (verified independently). 3 characterizations flipped + 1 new test.
> **019**: `IncludeRelated` is now always `null`; `includeRelated: true` is a **logged-warning no-op**, not a
> silent one. Escalation FIRED and is resolved-but-open: the flag is visible in the Playbook Builder canvas
> and does nothing. **No playbook sets it today** (verified), so this is latent. 019 also corrected a
> pre-existing test that had pinned the buggy `Contain("*")` behaviour.
> Follow-ups filed: register **I-4** (no tenant segment in `sdap:auth:*` keys → design task 035's per-user
> cache tenant-aware from the start); stale "task 054 implements" comments remain in `MembershipEndpoints.cs`
> + `IMembershipResolverService.cs` → **task 015** owns that directory.

> **Task 003 outcome (2026-08-21)**: 4 keys registered; 15-test source-scanning completeness gate added
> (`OperationAccessPolicyCompletenessTests`). 8 task-001 characterizations flipped. Sweep **confirmed
> A-20's list complete** (22 `Add*Filter` extensions exist; only 7 consult the policy) and filed **A-23**
> (a second orphaned filter → task 018). Two new obligations recorded as POML constraints:
> **task 005 MUST map Dataverse `AppendToAccess`** (else `POST /api/office/save` is permanently 403 while
> *looking* fixed), and **task 018 deletes `AddOfficeDocumentAccessFilter`** alongside A-15. Rationale:
> [`notes/task-003-operation-rights-decisions.md`](../notes/task-003-operation-rights-decisions.md).

> **Task 004 outcome (2026-08-21)**: token rides on `AuthorizationContext.UserAccessToken`
> (**`required string?`** — forces every construction site to declare intent, so app-only is a visible
> `= null`, never a default; produced 7 compile errors across 11 sites). Missing token → DENY with
> `sdap.access.deny.no_caller_token`, data source **never consulted**. `IHttpContextAccessor` was rejected
> — `Spaarke.Core` has no ASP.NET Core dep and `LayerDependencyTests` guards that. POML **Step 3 was
> vacuous** (zero app-only consumers), not skipped.
> ⚠️ **FR-02's criterion is NOT closed by 004 alone** — `PermissionsEndpoints.cs:76,:159` still pass
> `userAccessToken: null` because they call `IAccessDataSource` **directly**, bypassing
> `AuthorizationService`. That is A-4 → **task 006**, which should route them THROUGH the service rather
> than re-plumb the token. Rationale: [`notes/task-004-caller-scoped-design.md`](../notes/task-004-caller-scoped-design.md).

> **Task 006 outcome (2026-08-21)**: ✅ **FR-02's criterion is now CLOSED** alongside FR-05 — a repo grep
> for `userAccessToken: null` returns **zero** production call-sites. `AuthorizationService` gained
> `GetCallerAccessAsync(userId, resourceId, userAccessToken, ct)` — **no default** on the token param,
> because A-4's root cause was the `= null` *default* on `IAccessDataSource.GetUserAccessAsync`, not a
> missing null check. `AuthorizeAsync` routes through it, so the service now has **exactly one** member
> touching `_accessDataSource` (verified by grep + a test pinning that both paths present identical
> arguments) — acceptance criterion 5 is structural, not asserted. Fourteen capabilities project from ONE
> snapshot rather than fourteen `AuthorizeAsync` calls (the batch route would otherwise be 1,400
> rule-chain evaluations per 100-doc request). No-access shape = **200 + all-false**, not 403.
> **Second disclosure found + closed**: the batch handler honoured a `UserId` from the request BODY.
> `DataverseAccessDataSource.cs:184-199` treats `userId` and `userAccessToken` as INDEPENDENT, so that
> would have queried a different principal under the caller's OBO token and written task 014's cache key
> under the **victim's** oid. `BatchPermissionsRequest.UserId` is removed (wire-compatible).
> Escalation trigger evaluated and correctly did NOT fire — **zero clients** call either route (two
> independent greps); the endpoint has been shipping a disclosure nothing consumed.
> ⚠️ Until **task 005** lifts the Read ceiling, eleven of the fourteen capabilities are false for
> everyone in production — the honest interim state, not a regression.
> Rationale: [`notes/task-006-capability-rights-mapping.md`](../notes/task-006-capability-rights-mapping.md).

## Phase 1 — One evaluator (10 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 030 | ADR-003 amendment — two-surface authorization | FR-19 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 031 | ADR-028 A2 amendment — impersonated derivation | FR-20 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 032 | Evaluator spine — `(recordId→rights)` + max + veto seams | FR-19 | 030 | — | ❌ | **opus** | **xhigh** |
| 🔲 033 | Consumer propagation · **delete the `Collaborate` stamp** | FR-19 | 032 (+009 soft) | — | ❌ | opus | high |
| 🔲 034 | **Negative canary — NFR-04 merge gate** | FR-20 | — | **P1-A** | ✅ | sonnet | **xhigh** |
| 🔲 035 | `ImpersonatedRootSetSource` + per-user cache | FR-20 | 031 | — | ❌ | opus | high |
| 🔲 036 | Flag-gated swap + truncation + runbook | FR-20, NFR-02/03/04 | 032, **034**, 035 | — | ❌ | **opus** | **xhigh** |
| 🔲 037 | Restricted veto + Secure pre-max suppression | FR-21, FR-22 | 032 | — | ❌ | sonnet | high |
| 🔲 038 | Deny-list store — schema + fail-closed reader | FR-23 | — | **P1-A** | ✅ | sonnet | high |
| 🔲 039 | Deny veto wiring + ordered-pipeline tests | FR-23, FR-19 | 032,037,038 | — | ❌ | sonnet | high |

## Phase 2 — One definition of member (5 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 040 | ADR-034 amendment — registry first-class | FR-24 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 041 | Access-conferring column registry (contact **+ org**) | FR-24 | 040 | **P2-A** | ✅ | sonnet | high |
| 🔲 042 | Standing-grant baseline levels (contact + org) | FR-25 | 032 | — | ❌ | sonnet | high |
| 🔲 043 | Org-expansion term + fallback registry filter | FR-24/25/22 | 037,041,042 | — | ❌ | sonnet | high |
| 🔲 044 | Unified-evaluator seam suite (Phase 1–2 contract) | FR-19…25 | 039,041–043 | **P2-A** | ✅ | sonnet | high |

## Phase 3 — Child inheritance (9 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 050 | Core-ancestor derivation in the shared resolver | FR-26 | — | **P3-W1** | ✅ | **opus** | **xhigh** |
| 🔲 051 | `RegardingResolver` re-stamp on set/reparent/clear | FR-26 | 050 | **P3-W2** | ✅ | opus | high |
| 🔲 052 | Server-writer audit + C# `CoreAncestorResolver` | FR-26 | 050 soft | **P3-W1** | ✅ | sonnet | high |
| 🔲 053 | Ancestor-stamp backfill script | FR-26 | 050,052 | **P3-W2** | ✅ | sonnet | medium |
| 🔲 054 | Root-set generalization (`sprk_servicerequest` 4th root) | FR-27 | 032,035,036 | — | ❌ | sonnet | high |
| 🔲 055 | Evaluator child-inheritance term | FR-27 | 054,032,037,038 | — | ❌ | sonnet | high |
| 🔲 056 | Child-module registration (todo/event/communication) | FR-27 | 055,**009**,**018** | — | ❌ | sonnet | high |
| 🔲 057 | Phase-3 seam tests | FR-26/27 | 052,055,056 | **P3-W6** | ✅ | sonnet | high |
| 🔲 058 | Taxonomy + inheritance docs (**Matter ≠ Project**) | FR-26/27 | 056 | **P3-W6** | ✅ | sonnet | medium |

## Phase 4 — Secure Project · Manage Access · wizard (10 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 060 | POA seam consolidation (2→1, +revoke) | FR-28/29 pre | **010** | — | ❌ | **opus** | **xhigh** |
| 🔲 061 | Secure provisioning rework — svc-acct owner, share-only | FR-28 | 060,**008** | **P4-W2** | ❌ | sonnet | high |
| 🔲 062 | **NFR-05 role-depth standing assertion** | FR-28 | 034 | **P4-W2** | ✅ | sonnet | high |
| 🔲 063 | Internal system-user share endpoints (delegation-gated) | FR-29 | 060,**008**,010 | **P4-W3** | ❌ | sonnet | high |
| 🔲 064 | Provenance read + deny-list endpoints | FR-30, FR-23 | 063,060,032,038,041,042 | **P4-W4** | ❌ | sonnet | high |
| 🔲 065 | `AccessGrantModal` "+ User" picker | FR-29 | **063** | **P4-W4** | ✅ | sonnet | high |
| 🔲 066 | Modal provenance rows + suppressed rendering | FR-30 | 064,065 | — | ❌ | sonnet | high |
| 🔲 067 | Modal deny-list UI + standing-grant levels | FR-23/25 UI | 064,066 | — | ❌ | sonnet | high |
| 🔲 068 | Wizard Secure step + copy fixes (Power Pages) | FR-31 | 061 | **P4-W3** | ✅ | sonnet | medium |
| 🔲 069 | Phase-4 seam tests | FR-28/29/30 | 061,063,064 | — | ✅ | sonnet | high |

## Phase 5 — Attestation (4 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 080 | `sprk_accessevent` schema + data-model doc | FR-32 | 032,038 | — | ✅ | sonnet | medium |
| 🔲 081 | Append hooks at every grant/deny choke point | FR-32 | 080,060,063,064 | — | ❌ | sonnet | high |
| 🔲 082 | Evaluator versioning + point-in-time replay | FR-32 | 081,032 | — | ❌ | sonnet | **xhigh** |
| 🔲 083 | Attestation seam tests + docs | FR-32 | 081,082 | — | ✅ | sonnet | medium |

## Wrap-up

| # | Task | Deps | Safe |
|---|---|---|---|
| 🔲 090 | `/test-diet` · H-8a/H-8b closeout · lessons-learned · README → Complete | all | ❌ |

---

## Parallel execution groups

| Group | Tasks | Prerequisite | File-disjointness |
|---|---|---|---|
| **P0-W0** | 001 | — | Tests only (`tests/AccessControl/**`, `Spaarke.Core.Tests/Auth/**`). Blocks all Phase 0 code work |
| **P0-B** | 014, 019 | 001 | `Infrastructure/Caching/CachedAccessDataSource.cs` vs `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — the only two Phase 0 code tasks outside all four contended directories |
| **P1-A** | 034, 038 | — | `tests/integration/auth/**` (new) vs new deny-list schema/reader + an append-only DI block |
| **P2-A** | 041, 044 | 040 / 039 | `Services/Ai/Membership/**` vs `tests/integration/seam` (new files only) |
| **P3-W1** | 050, 052 | — | TS shared lib (`Spaarke.UI.Components/src/services/`) vs C# `Services/Communication/**` |
| **P3-W2** | 051, 053 | 050, 052 | `RegardingResolver` PCF vs `scripts/` |
| **P3-W6** | 057, 058 | 055, 056 | `tests/integration/seam` vs `docs/architecture` |
| **P4-W2** | 061, 062 | 060, 008, 034 | `src/**` provisioning vs `tests/integration` |
| **P4-W3** | 063, 068 | 060, 008, 061 | `Api/ExternalAccess/**` vs `CreateProjectWizard/**` |
| **P4-W4** | 064, 065 | 063 | `Api/ExternalAccess/**` + Infrastructure vs `AccessGrantModal` + `TrackingFieldTrio` |

**Max concurrency 6 agents/wave.** Build verification between waves is mandatory: `dotnet build src/server/api/Sprk.Bff.Api/` after any `.cs` change; `npm run build:prod` for PCF (**never** `npm run build` — root CLAUDE.md §12).

### Honest note on parallelism

**Phase 0 barely parallelizes**, and that is a property of the codebase, not a planning shortcut. Seventeen of its nineteen tasks cluster in four contended directories — `Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**`, `Spaarke.Core/Auth/**`, `Spaarke.Dataverse/DataverseWebApiService.cs`. Only `{014, 019}` are genuinely file-disjoint. Task 006 is disjoint and safe but has no co-schedulable partner in its wave.

Two agents editing an authorization path concurrently produces a silent merge mess, so these run in dependency-ordered waves and merge serially with `/conflict-check`. Phases 3 and 4 parallelize better because PCF, scripts, docs and BFF work are genuinely separable.

### Cross-phase collision audit (verified 2026-08-21, not assumed)

| Potential collision | Verdict |
|---|---|
| 044 / 057 / 069 / 083 all write `tests/integration/seam` and are all `safe:true` | **Benign** — serialized by phase dependencies, and each creates distinct files. Preserve this ordering if phases are ever resequenced |
| 015 (P0) and 041 (P2) both touch `MembershipResolverService.cs` | **Safe** — 015 is `parallel-safe:false`, so it never co-runs |
| 038 (`safe:true`) shares `ExternalAccessModule.cs` with 035/036/042/056 | **Safe** — those are all `safe:false`; 038's only partner is 034 (tests-only) |
| 065 touches `TrackingFieldTrio`; 050 touches `Spaarke.UI.Components` | **Disjoint** — `components/AccessGrantModal/` vs `src/services/`; no Phase 0 task touches either |
| 065 / 066 / 067 all edit `AccessGrantModal.tsx` | **Serialized** by the 065→066→067 dependency chain |

## Escalation triggers (legitimate stops, not failures)

Tasks carrying `<escalation><trigger>` for genuine judgment boundaries — a task that stops here is behaving correctly (root CLAUDE.md §6 / §6.5):

- **018** — spec FR-17's "bound the in-clause per FR-25" cross-reference is ambiguous (FR-25 is Phase 2); does not guess
- **042** — default when a subject's `sprk_accesspermissiongrant` baseline is empty but the standing flag is set
- **043** — level for a non-standing derived term ("default-on" names no level source)
- **037** — matter / work-assignment lack `sprk_issecure` / `sprk_accesspermission` columns
- **038** — deny-list subgrid storage shape

## Deferred / out of scope

**FR-18 (BU restructure) has no tasks** — reclassified to UAT/environment work. Tasks 061/062 fail closed when the topology is unconfigured and loud-skip pre-UAT; live-dev acceptance is recorded in `notes/phase4-uat-acceptance.md`. Also out: AI-search trimming for contacts (A-21), field-level visibility, break-glass, organization-hierarchy cascade, GDPR erasure.
