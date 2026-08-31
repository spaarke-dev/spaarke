# Multi-agent review of completed Phase 0 work — 2026-08-24

> **Requested by** the owner after task 017, on the concern that "we have a number of potential
> unresolved issues… are we missing any potential issues?"
> **Scope chosen**: full re-review of all 13 completed tasks + cross-cutting lenses.
> **Method**: 8 Fable-tier agents, read-only, adversarially prompted ("what did these tasks NOT cover?"),
> under a coverage-first contract (report everything with severity AND confidence; no self-filtering).
> Four re-reviewed task families; four applied lenses per-task gates structurally cannot see.

**Answer to the question: yes.** Two Critical disclosure/destroy surfaces that the 20-finding investigation
never examined, one Critical regression this project introduced, and one High cross-task defect created by
the interaction of two of our own fixes. Plus a consistent pattern in the test estate.

---

## 1. The pattern, stated once

Per-task verification here has been good at proving **the thing I changed works**, and blind to **whether
the test proves it**. Nearly every finding below is one of three shapes:

| Shape | Instances |
|---|---|
| **N-of-M** — a class was fixed at some call sites, not enumerated | task 002 gated 4 of ~15 document routes; task 003's gate misses a whole mechanism; my own e2e edit missed the error-case block |
| **Seam-shadowing** — the test substitutes the method whose internals are the thing under test | the A-13 matcher, the delegation probe, task 007's call-site wiring, the propagate-tests, the provisioning projection |
| **Stale schema names** — silent, because a wrong name reads as "nothing to act on" | now **five** instances, one introduced by this project |

The gates could not see these because they are per-task. That is the actual lesson.

---

## 2. Ranked findings

### 🛑 CRITICAL

| # | Finding | Evidence | State |
|---|---|---|---|
| **C1** | **`POST /api/documents/bulk-download` has no authorization.** Its filter checks a `tid` claim exists, logs *"Bulk download authorization granted"*, and calls `next()`. **500 GUIDs per request**, streamed **app-only**, with failures listed in a `_FAILED.txt` manifest while zipping continues — so one call both exfiltrates and enumerates. The comment justifying the absent check cites `preview-url` as its model; `preview-url` is itself unfiltered. | `Api/DocumentsBulkEndpoints.cs:42`, `Api/Filters/BulkDownloadAuthorizationFilter.cs:58-90` | **task 022** |
| **C2** | **`DELETE /api/documents/{id}` destroys any document for any authenticated caller.** `DeleteAsync(Guid, string, CancellationToken)` takes **no user identity**; deletes the Dataverse row *and* the SPE file app-only. Reachable from a shipped client hook. | `Api/DocumentOperationsEndpoints.cs:23,57`, `Services/DocumentCheckoutService.cs:729` | **task 022** |
| **C3** | **`DELETE /api/v1/documents/{id}`** — a second app-only destroy path, no filter, while its sibling `/{id}/download` got one. | `Api/DataverseDocumentsEndpoints.cs:230` | **task 022** |
| **C4** | ✅ **FIXED `4c364e47d`** — the provisioning `$select` named two nonexistent columns, breaking `/provision-project` entirely and rendering its own 409 guard inert. Introduced by this project (`95d3f0f68`). | `Api/ExternalAccess/ProvisionProjectEndpoint.cs` | **done** |
| **C5** | **The provisioning stamping PATCH writes three nonexistent names and swallows the 400 while returning 200.** Since 2026-03, provisioning has created a real BU, container and account and left the project pointing at none of them. | `ProvisionProjectEndpoint.cs:495-499` | **task 021** |

### 🔴 HIGH

| # | Finding | Evidence | State |
|---|---|---|---|
| **H1** | **The grant upsert never writes `sprk_expiresdate` on its match path.** With task 007's read filter this yields three silent outcomes: re-granting to *add* an expiry is a no-op (access stays unbounded — **A-5's shape resurrected on the write path**); re-granting over an *expired* row returns 200 + a record id while the grantee still has nothing; extending an expiry is ignored. `RowSelect` doesn't even select the column. `ExpiryDate` appears **once** in the whole grant-lifecycle suite, as `null`. | `GrantExternalAccessEndpoint.cs:168-179`, `ExternalGrantLifecycle.cs:132-135` | **task 023** |
| **H2** | **`PUT` / `GET /api/v1/documents/{id}`** — app-only tamper and metadata disclosure by GUID; the GET returns `GraphDriveId`/`GraphItemId`, which feed C1/C2. | `DataverseDocumentsEndpoints.cs:108,163` | **task 022** |
| **H3** | **`/checkout` `/checkin` `/discard` `/analyze` `/checkout-status`** — app-only state ops on any document; `/checkout` returns an **editable** URL. | `DocumentOperationsEndpoints.cs:30-74` | **task 022** |
| **H4** | **`/share-link` mints a non-expiring anonymous link with no per-record check.** Known as A-14 (task 012) for being untracked/unrevocable — the *missing authorization* is the new half. | `FileAccessEndpoints.cs:89,667` | **task 012** (constraint) |
| **H5** | **`ExternalDataService` orders the accessible-projects query by `sprk_name`**, absent from `sprk_project` → 400 → caught → `[]`. The external SPA's project list renders empty and reads as "you have no grants". | `ExternalDataService.cs:171` | **task 022** |
| **H6** | **Four load-bearing methods are executed by no test.** `FindPermissionByEmail` (the A-13 matcher), `CallerRecordAccessProbe.GetCallerRightsAsync` (the project's central gate), task 007's query **call sites**, and the real Graph-call error path. Each has a one-line perturbation that fails **zero** tests. | see §3 | **task 025** |

### 🟠 MEDIUM

| # | Finding | State |
|---|---|---|
| M1 | **Neither SPE read handles Graph paging** — single `.GetAsync()` on `permissions?.Value`. `container_not_cleared` can report clean while page-2 members keep file access, defeating the guard task 016/017 built. | task 024 |
| M2 | **`/revoke` returns 200 when the SPE removal failed** (body says `Failed`, status says OK) while closure returns 500 for the identical shape — and `AccessGrantModal` ignores the body. | task 024 |
| M3 | **Task 003's completeness gate misses a whole mechanism**: operation strings passed directly to `HasRequiredRights(...)` are invisible to the source scan, and `PermissionsEndpoints.cs` now has **16**. Also, the const-indirection mechanism the test documents matches **zero** call-sites — `entity.associate_document` is found only by an unanchored regex accidentally matching `AssociateOperation = "…"`. | task 025 |
| M4 | **`views-schema.md` still documents `sprk_contactid`, `sprk_projectid`, `sprk_expirydate`** (16+ refs). Task 016 recorded it as wrong and did not fix it — against root CLAUDE.md §2. This is the plausible seed of all five recurrences. `secure-project-fields-schema.md` is the seed for C4/C5. | task 026 |
| M5 | **Docs are inverted or absent**: `external-access-spa-architecture.md:110` still says "expiry is NOT ENFORCED anywhere"; the delegation rule is documented **nowhere** outside code — none of the four deny codes appear in the admin guide's troubleshooting table. | task 026 |
| M6 | **The e2e tier pins stale contracts and cannot run.** `project-closure-cascade.spec.ts` targets routes without `/v1`, asserts a response shape whose four fields have never existed, and filters on `sprk_projectid`/`sprk_contactid` (**instances four and five**). `revocation.spec.ts` still expects 400/404 where the delegation rule now answers 403 — I edited that file twice and missed the error-case block. Both suites' arrange steps create projects with `sprk_name`/`sprk_issecureproject`, neither of which exists. | task 027 |
| M7 | **`ExternalAccessEndpointTests.cs`** — ~25 non-discriminating tests, several asserting **false** claims about code tasks 010/017 changed (e.g. "empty ContactId returns 400" — it's the documented org-grant signal, and returns 200). | task 025 |
| M8 | **`AccessGrantModal.postJson` never checks `res.ok`** — a 403 parses as success. Masked only because the one shipped host passes `authenticatedFetch`; the declared prop type is a raw fetch. Failure notice says "Please try again", wrong for `delegation_write_required`. | task 065 (constraint) |

### 🟡 LOW / accepted

`sdap:auth:roles|teams` keys lack auth-mode and tenant but are **dead writes** (never read) · `DataverseAccessDataSource` mutates `DefaultRequestHeaders` — safe only by DI lifetime · `EntityAccessFilter` allows on a null target, asymmetric with the deny-on-unresolved default · `MapProjectClosureEndpoint` is dead code · task 017's H-8b left the web-role text in the class summary **and** the Swagger description · `/revoke` `.ProducesProblem(404)` is now unreachable · `sprk_granteddate` written with a full timestamp against a DATE ONLY column · a third orphaned filter (`AddWorkspaceLayoutAuthorizationFilter`, not a hole) and a referenced-but-nonexistent `AddWorkforceCallerAuthorizationFilter`.

**Out of this project**: `ContainerItemEndpoints` (all routes app-only, no resource filter, includes DELETE — SPE-admin owner) · `BulkRagIndexingJobHandler` `_sprk_matterid_value` (AI owner) · `sprk_eventtypes` entity set does not exist (events owner) · `ProjectLiveFactResolver` reads `sprk_name` (Insights owner) · `external-spa/web-api-client.ts` stale names (SPA plane).

---

## 3. The four untested seams (H6), with their perturbations

| Seam | Perturbation | Tests that fail |
|---|---|---|
| `FindPermissionByEmail` | `string.Equals(upn, email, …)` → `false` | **0** |
| `CallerRecordAccessProbe.GetCallerRightsAsync` | return `Read \| Write` unconditionally | **0** — A-6 silently reopened |
| Task 007 call sites | inline the pre-fix filter string at `ExternalParticipationService.cs:471` | **0** — expiry enforcement gone |
| `ListExternalMembersAsync` Graph call | wrap only the `GetAsync` in `try { } catch { return []; }` | **0** — the original bug, restored |

The provisioning projection was a fifth, now closed: restoring the broken names fails **7 of 7** where it
previously passed 5 of 5.

---

## 4. Clean bills worth recording

- **Fail-open: none.** Every authorization path — filter, `AuthorizationService`, `DataverseAccessDataSource`, the rights mapper, the cache — fails closed. `CachedAccessDataSource`'s "fail-open" comment means *recompute*, not *grant*.
- **§10 F.1 asymmetric registration: clean.** No conditionally-registered service backs an unconditionally-mapped route.
- **Expiry predicate: complete and correct** on all six server-side sites, with the revocation paths correctly excluded. Bare `yyyy-MM-dd`, `ge`, `eq null` present everywhere.
- **Logical-grant key: consistent** across write, read, delegation and cascade. Task 010's invariant survived 016 and 017.
- **Production `$filter`/`$select` in `src/server` external-access: all correct** against live metadata.
- **Cross-worktree merge risk: none.** `sdap-SPE-admin-app-r2` (+14,850 lines) references no changed member; `dataverse-access-unification-r1` and `teams-app-r1` are at parity with master.
- **ADR-038 banned shapes** in anything tasks 001/003/019 authored: none.
- The `ExternalCollaborationTestFixture` 500-vacuity hazard has been correctly handled everywhere.

---

## 5. Immediate actions taken

1. **C4 fixed and pushed** (`4c364e47d`), with the `$select`-validating fake ported from task 016 and two direct assertions. Perturbation-verified.
2. **Task 009's POML corrected** — it instructed flipping a characterization that was never authored and named a dead path that is also task 011's file. Under literal execution that would have weakened the fail-closed gate it was meant to strengthen.
3. **Notification owed to `code-quality-and-assurance-r3`**: their dead-`catch` inventory says 4 for `SpeContainerMembershipService`; master has 4, post-merge it is 3, in a file whose methods are now `virtual` with new return types — a line-based mechanical pass would mis-target.

## 6. Proposed new tasks

| Task | Covers | Priority |
|---|---|---|
| **021** | Provisioning stamping PATCH — verify all three names against `$metadata`, then make the failure loud. Both together. | Critical |
| **022** | Document-surface authorization sweep — C1, C2, C3, H2, H3, H5 and the six OBO routes. Enumerate the class, gate every member. | Critical |
| **023** | Grant upsert must write expiry (H1) + FR-09 acceptance amended to include it | High |
| **024** | SPE honesty — Graph paging (M1) + `/revoke` status parity with closure (M2) | Medium |
| **025** | Test-integrity remediation — the four untested seams (H6), the gate's missing mechanism (M3), the misleading legacy tests (M7) | High |
| **026** | Schema-truth doc repair — `views-schema.md`, `secure-project-fields-schema.md`, the inverted expiry claim, delegation-rule documentation (M4, M5) | Medium |
| **027** | e2e tier reconciliation (M6) — or an explicit decision to retire the tier, since no workflow runs it | Medium |
