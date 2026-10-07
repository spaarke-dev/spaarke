# Task 136 — rights-based external read gates (defect C2, GitHub #1059)

> **Date**: 2026-10-01 · **Branch**: `task/uac-r2-136` (from `task/uac-r2-135`) · **Rigor**: FULL
> **POML**: `tasks/136-rights-based-external-read-gates.poml`

## 1. What changed, in one paragraph

The external read routes asked "is this record id in the caller's map?" instead of "does the caller hold
Read on it?". Two live paths put ids in that map with no rights: a Secure root reached only through an
organization grant (FR-22 suppression enters it at `None`), and a matter or work-assignment grant row with no
`sprk_accesslevel`. Absence is now the only representation of no access, enforced in **three independent
layers**: (1) the evaluator removes every entry without Read at the end of each composition
(`AccessibleRecordSetService.RemoveEntriesWithoutRead`, after the veto pipeline, on the systemuser path and the
shared contact path); (2) both plane strategies build the principal only from Read-bearing entries
(`CallerPrincipal.FromReadBearing`), and every principal view — `HasProjectAccess`, `GetAccessibleProjectIds`,
`AccessibleMatterIds` / `AccessibleWorkAssignmentIds`, the new `ReadableProjects` — and every
`AccessibleRecordSet` view (`Contains`, `RecordIds`, `Count`) requires Read on its own; (3) every
`/api/v1/external` project read route tests Read through one helper (`HoldsReadOnProject`, the same
`RightsForRoot` expression the to-do list already used). `/me` lists only readable projects and never emits
`"None"`.

## 2. Step 1 before-state — dev, READ-ONLY (Dataverse MCP `read_query`, 2026-10-01)

Escalation trigger 1 (task 131 merged?) did **not** fire: `bb4dd61aa` (DirectAccessLevel through the grant
cache, CacheVersion 5) is merged into the base (`a75a7d598`), and task 135 is the commit this branch starts from.

**(a) matter/WA grant rows with a null `sprk_accesslevel`, active and unexpired** — and, wider, in any state:

| Query | Result |
|---|---|
| `SELECT … FROM sprk_externalrecordaccess WHERE statecode = 0 AND sprk_accesslevel IS NULL` | **0 rows** |
| `SELECT COUNT(…) FROM sprk_externalrecordaccess WHERE sprk_accesslevel IS NULL` (any state) | **0** |
| Active rows by level (control that the query reads the table) | View Only 13 · Collaborate 7 · Full Access 11 = **31** |

→ Escalation trigger 2 (owner decision on non-test null-level rows) does **not** fire: there are none, test
data or otherwise. The owner rule "no level = not granted" removes nothing live in dev.

**(b) organization-only grants on Secure roots:**

| Query | Result |
|---|---|
| Secure projects (`sprk_issecure = 1`) | **1**: `65a3fab2-77a5-f111-aaad-70a8a590c51c` "Test New Matter via Workspace" (Standard permission) |
| Secure matters / work assignments | **0 / 0** |
| Grant rows of ANY kind on that Secure project | **0** |
| Active organization grant rows (all targets) | 5, all on organization `67577f8c-…`, targeting matters `b68299c6…`, `2444af6d…` (also has a contact), `0d8df610…`, `042f4462…` and project `656fe858…` — **none on a Secure root** |

→ **0** records lose read access in dev through either path. Nothing to list per contact.

## 3. Consumer enumeration (Step 1 grep over `src/server/**`)

Each consumer of `AccessibleRecordSet.Contains` / `.RecordIds` / `.Rights`, `IsRecordAccessibleAsync`,
`CallerPrincipal.HasProjectAccess` and the `GetAccessible*Ids` / `Accessible*Ids` views, with the test that covers
its new Read semantics or why it is unaffected.

| # | Consumer (file:line after this task) | Semantics now | Covered by / why unaffected |
|---|---|---|---|
| 1 | `AccessibleRecordSet.Contains` ← `IsRecordAccessibleAsync` (`AccessibleRecordSetService.cs`) | holds Read | `AccessibleRecordSet_BuiltDirectlyWithANoneRightsEntry_ItsReadViewsExcludeIt` (unit); `C2_SecureRootReachedOnlyThroughAnOrganizationGrant_…` asserts `IsRecordAccessibleAsync == false` (seam) |
| 2 | `AccessibleRecordSet.RecordIds` / `Count` | Read-filtered views | Same unit test. **No production reader** of `RecordIds` (grep: doc comments only) |
| 3 | `AccessibleRecordSet.Rights` ← `CiamContactPrincipalStrategy` and `WorkforcePrincipalStrategy` (`CallerPrincipalResolver.cs`) | copied through `FromReadBearing` | `CiamStrategy_/WorkforceStrategy_EvaluatorAnswerWithNoneRightsEntries_BuildsAPrincipalWithoutThem` (unit) |
| 4 | `IsRecordAccessibleAsync` | holds Read | **No production caller** (grep). Pinned by the seam test above and `StandingGrantRuntimeUnionSeamTests` |
| 5 | `IsOperationPermittedAsync` (`RightsFor(...).HasFlag(required)`) | unchanged | Already rights-based; no production caller. Unaffected |
| 6 | `CallerPrincipal.HasProjectAccess` ← `UploadDocument` (`ExternalProjectDataEndpoints.cs:589`), `CreateEvent` (`:818`) | holds Read, then Create | Mutating routes keep their Create check after it; a Create-bearing level always carries Read. `MutatingRoute_DirectViewOnlyGrantOnTheSecureProject_Returns403InsufficientRights` (contract, both planes) + the existing upload/event contract tests, unmodified |
| 7 | `GetAccessibleProjectIds` ← `GetProjects` (`:266`) | Read-bearing only | `ProjectListAndMe_SecureProjectReachedOnlyThroughAnOrganizationGrant_AreNotListed` (both planes), `ProjectListAndMe_PrincipalHoldingTheProjectAtNoneRights_ListNothing` |
| 8 | `GetAccessibleProjectIds` ← module dimensions: collaboration (`ExternalAccessModule.cs:247`), documents (`:274`), invoices (`:300`) | Read-bearing only | Pure reads of the view; `ReadViews_PrincipalBuiltDirectlyWithNoneRightsEntries_…` pins the view. File not edited (task 134 owns it) |
| 9 | `GetAccessibleMatterIds` ← documents (`:275`), invoices (`:299`), matters (`:343`) | Read-bearing only | `ModuleFetchAndRecord_MatterAndWorkAssignmentGrantRowsWithNoLevel_ConferNothing` (contract, both planes): /fetch documents + invoices → 0 rows, /record matter → 403; control `ModuleRecord_MatterGrantRowWithALevel_PassesTheTier2Gate` |
| 10 | `GetAccessibleWorkAssignmentIds` ← documents (`:276`), work-assignments (`:322`) | Read-bearing only | Same contract test: /record work assignment → 403 |
| 11 | `ExternalModuleRegistry.IsRecordAccessible` / `ScopeRows` (consume the dimension delegates) | inherit 8–10 | Same module contract tests. File not edited |
| 12 | `CallerPrincipal.ReadableProjects` (new) ← `ExternalUserContextEndpoint.Handle` | Read-bearing only | `Me_ProjectTheCallerHoldsNothingOn_IsNotReturned_AndNoneIsNeverEmitted` + `Me_ReadableProject_…` (real handler, unit) and the /me assertions in the contract tests |
| 13 | `ExternalCallerContext.HasProjectAccess` / `GetAccessibleProjectIds` | holds Read | **No production consumer** (grep: no handler reads `ExternalCallerContext`). Aligned so no presence copy survives to be imitated; a participation always carries a level, so the answer is unchanged |
| 14 | To-do routes (`ListTodosForRoot`, `CreateTodoForRoot`, `UpdateTodo`) via `RightsForRoot` | unchanged | Already rights-based. `ExternalTodoScopeTests` pass unmodified |
| 15 | `service-requests` module (`:364`, keyed on `ContactId`), `grid-configuration` (`:392`, static ids) | unchanged | Do not read any rights view |
| 16 | `MeEntitlementsEndpoint` | unchanged | Reads `Plane`/`Email` and App Roles; no record scope |
| 17 | Semantic search (`SemanticSearchAuthorizationFilter`, `SemanticSearchEndpoints`) | unchanged | Does not consume `CallerPrincipal`; gates on a Dataverse Read snapshot (§7). Negative grep = 0 |

## 4. Escalation triggers

| Trigger | Fired? |
|---|---|
| 1 — task 131 not merged | No (`bb4dd61aa` in base) |
| 2 — non-test null-level rows | No (0 rows in any state, §2) |
| 3 — a read route needs a right other than Read | No. The seven routes are plain reads (`GET`); the upload/event `POST`s already carried their own Create check and are mutating, not disguised reads |

## 5. Tests (KEEP paths; ADR-038 bans respected)

New:
- **Unit** `CallerPrincipalTests`: `Me_ReadableProject_IsReturnedWithTheSameLevelStringAsTheLegacyHandler` (×3) and
  `Me_ProjectTheCallerHoldsNothingOn_IsNotReturned_AndNoneIsNeverEmitted` drive the REAL `ExternalUserContextEndpoint.Handle`;
  they **replace** `MeProjection_CiamAccessLevel_MapsToSameStringAsLegacyHandler`, which re-implemented the handler's
  lambda and asserted on its own copy. `ReadViews_PrincipalBuiltDirectlyWithNoneRightsEntries_…` (defence in depth),
  `FromReadBearing_KeepsOnlyEntriesCarryingRead`.
- **Unit** `CallerPrincipalResolverTests`: construction pruning on both strategies.
- **Unit** `AccessibleRecordSetServiceTests`: `AccessibleRecordSet_BuiltDirectlyWithANoneRightsEntry_ItsReadViewsExcludeIt`.
- **Seam** `UnifiedEvaluatorSeamTests`: `C2_SecureRootReachedOnlyThroughAnOrganizationGrant_IsAbsentFromTheSetAndFromBothPrincipals`
  (×3 roots), `C2_GrantRowWithNoLevel_ConfersNothing_OnBothContactSignIns` (matter, WA),
  `C2_NoComposedSetCarriesAnEntryWithoutRead_OnTheSystemUserOrTheContactPlane` (five compositions).
- **Contract** `ExternalAccessContractTests` (real handlers, real evaluator, both planes):
  7 routes × 2 planes 403-before-any-read (and no SPE pointer / content / version call),
  7 × 2 admitted with a direct ViewOnly grant on the Secure project, 3 mutating routes × 2 planes 403
  `insufficient_rights`, /projects + /me not listing the Secure project (×2), and the route layer isolated with a
  hand-built None-rights principal (7 routes + list/me).
- **Contract** `ExternalModuleDataContractTests`: null-level matter/WA (×2 planes) and the levelled control (×2).

Changed (absence added where a test asserted only `RightsFor == None`, or where `Contains`/`RecordIds` — now
Read-gated views — no longer prove raw absence): `AccessibleRecordSetServiceTests` (8 sites incl. the anchors
:625, :1377, :1524), `UnifiedEvaluatorSeamTests` (:167, :302, :337, :399, :632, the CIAM FR-22 test, and the
plane-parity test now compares unfiltered), `StandingGrantRuntimeUnionSeamTests` (:163),
`GrantCacheRoundTripSeamTests` (the null-level criterion reversed: confers nothing on miss AND hit — the cache
property "a null level restores as null" is still what it checks).

**Verifier round 1 (2026-10-01, branch `task/uac-r2-136-f1`)** — criterion 8 had one missed site:
`GrantCacheRoundTripSeamTests.ComposeAsync_OrgInheritedOnlyGrantOnSecureRoot_ComposesNoneOnMissAndOnHit` (3 roots)
asserted only `RightsFor == None` on a Secure-suppressed, organization-only record, and stayed green with both
`RemoveEntriesWithoutRead` calls disabled. It now also asserts `Contains == false` and `Rights` has no key for the
record on the miss AND the hit, and is renamed `..._IsAbsentOnMissAndOnHit`. Seeded the same violation (both
pruning calls commented out): 3 of 3 cases red at the miss `NotContainKey` line (`Contains` alone stays green,
because it is a Read-gated view — the key assertion is the one that bites); restored, 19 / 19. Re-swept every
`RightsFor(...).Should().Be(AccessRights.None` in `tests/` (16 sites in 4 files: 8 evaluator unit, 5 evaluator seam, 1 standing-grant seam, 2 grant-cache seam): all now carry an absence
assertion. The remaining `AccessRights.None` assertions (`CallerPrincipalTests` on random unknown ids, standing-grant
reader state, delegation probes, access-cache snapshots) are not composed-set records, so criterion 8 does not
reach them.

Fixture seams (test-only, at existing extension points): a header-selected workforce plane
(`HeaderWorkforcePrincipalResolver` at `IWorkforcePrincipalResolver`, `NoStandingGrantReader` at
`ISubjectStandingGrantReader`), Secure flags / direct level / null-level rows on the stub participation service,
recorded data reads on the stub data service, and `PowerlessProjectCiamStrategy` at `ICallerPrincipalStrategy`.
Production change for testability: six `ExternalDataService` read methods made `virtual` (the class already
exposes `virtual` seams for this fixture since task 030), so the positive route tests assert **200**, not
"anything but 403".

**Test scope beyond the closed set, justified:** the workforce-plane route tests (AC1 names the workforce plane;
the routes are plane-agnostic but the principal is built by a different strategy); the isolated route-layer
tests (without them perturbation 5 below was invisible — a route reverted to presence would have passed every test).

## 6. Perturbations (each applied alone, run, restored with `git checkout` from a WIP commit)

| # | Perturbation | Red |
|---|---|---|
| 1 | `HasProjectAccess` back to presence (`ProjectAccess.Any(...)`) | 1 |
| 2 | Construction pruning removed (`FromReadBearing` copies everything) | 3 |
| 3 | Evaluator end-of-composition pruning removed (`RemoveEntriesWithoutRead` no-op) | 14 |
| 4 | /me back to every entry with a `"None"` fallback | 1 |
| 5 | `HoldsReadOnProject` back to presence | 0 on first run → **7** after adding the isolated route-layer tests |
| 6 | `AccessibleRecordSet.Contains` back to `ContainsKey` | 1 |

Perturbations 1–3 together prove the layers independent: each is caught by a test the other two layers cannot
satisfy. Perturbation 5's first result (0) is why the route-layer tests exist.

## 7. Task 070 amendment re-verified

The existing 2026-09-30 amendments block was re-checked against code after this task (no second block added):
amendment n=1's anchors are unchanged — `SemanticSearchAuthorizationFilter.cs:413` (per-row flag), `:497-498`
(`GetCallerRecordAccessAsync`), `:500` (Read), `:604-607` (per-document Read); `SemanticSearchEndpoints.cs:171-178`
and `:292`; `DataverseAccessDataSource.cs:429` (`caller_not_a_dataverse_user`). The verdict's anchors moved and were
corrected in place (`HasProjectAccess` :161 → :203; read routes → :283, :300, :334, :721, :792, :849, :866 via
`HoldsReadOnProject` :499; mutating :589, :818). The amended negative grep (`HasProjectAccess`, `Accessible*Ids…Contains`,
`GetAccessible*Ids` under `Api/Ai`, `Services/Ai/SemanticSearch`, `Api/Filters/SemanticSearchAuthorizationFilter.cs`)
returns **zero**. A dated `<delivered>` line was added.

`notes/task-029-external-todo-parity.md` §5: the premise "for a project that is exactly the HasProjectAccess
membership test it replaces" is struck through and corrected in place.

## 8. Tasks 037 and 039 — evidence, and why they stay pending

| Amended criterion | 037 | 039 |
|---|---|---|
| 1 — task 135 behaviour on CIAM | Met by 135's seam tests (`Ciam_FR21_*`, `Ciam_FR22_*`), green here | Met (`Ciam_FR23_*` four key shapes) |
| 2 — negative control / targeted | Met (`Ciam_FR22_ADirectCollaborateGrantOnASecureRoot_ConfersCollaborate`, `Ciam_Regression_*`) | Met (`Ciam_FR23_AContactByRecordEntryNamingOneContact_…`) |
| 3 — fail closed on CIAM | Met (`Ciam_FailClosed_*`) | Met |
| 4 — task 136 / order | Met by this task (§5, both planes, real handlers) | Met (`PlaneParity_*` "restricted" + "no access entry") |
| 5 — **MANUAL LIVE GATE** (CIAM token, dev) | **Not met** | **Not met** |
| 6 — "only then" set completed | Not done | Not done |

Neither task 135 nor this task could run the live gate: the run rules allow read-only live checks only, and the
gate needs a Restricted flag, a Secure project provisioned through the endpoint and a No Access entry. Setting 037
and 039 to completed now would contradict their own criterion 6, so **both stay `pending`**; a dated completion
line for amendment n=2 was added to each. TASK-INDEX.md rows are owned by the main session in this run and were
not edited. **This is a deviation from task 136's criterion "set both POML statuses to completed"**, recorded here
and in the structured result.

## 9. Placement and justification (CLAUDE.md §10 / §11, `bff-extensions.md`)

- **Placement: in the BFF, inside the existing evaluator and principal** — per-request authorization on
  `/api/v1/external` that has to run before an app-only read; it cannot live anywhere else. No new service,
  endpoint, DI registration, option, job or package. `ExternalAccessModule.cs` and `ExternalModuleRegistry.cs`
  not edited (task 134 owns them): the module dimensions became rights-based through the `CallerPrincipal` views
  they already read.
- **New surface**: `CallerPrincipal.ReadableProjects` and `CallerPrincipal.FromReadBearing` (public members on an
  existing type); private `RemoveEntriesWithoutRead` and `HoldsReadOnProject`; `virtual` on six existing
  `ExternalDataService` read methods.
  1. *Existing* — `RightsForRoot` (the to-do list's Read test) and the veto slots' key removal; both reused, not
     copied. 2. *Extension* — yes: one helper over `RightsForRoot`; one step after `ApplyVetoPipeline`; one
     static filter both strategies share. 3. *Cost of doing nothing* — a Secure project reached only through an
     organization grant lists, shows documents and streams their content app-only to a caller holding nothing (C2).
- **Publish size**: not measured here (the main session measures after merging, per the run rules). No package
  change, so no new CVE.

## 10. MANUAL LIVE GATE — pending (dev, not CI)

Not executed (no live writes in this run). Exact procedure for the operator, with an existing CIAM test contact
(no user created or relocated):

1. Provision a Secure project through the endpoint: `POST /api/v1/external-access/provision-project`
   (`ProvisionProjectEndpoint.cs:207`; never a direct `sprk_issecure` edit — owner round 2 item 2).
2. Grant the contact's **organization** (not the contact) Full Access on it via `POST /api/v1/external-access/grant`
   (`GrantExternalAccessEndpoint.cs:48`) with the organization as grantee.
3. Sign in to the external SPA as the contact (ciamlogin.com). Expect: the project is not listed; `GET /api/v1/external/me`
   does not contain its id; `GET /api/v1/external/projects/{id}/documents/{docId}/content` → **403**.
4. Add a **direct** ViewOnly grant to the contact. Expect: listed, /me shows `ViewOnly`, content 200; `POST …/todos` → 403
   `insufficient_rights`.
5. Restore: deactivate both grants (and the test project if created for this). Record ids and observations here.

Read-only verification queries to accompany it: the §2 queries, plus
`SELECT sprk_externalrecordaccessid, sprk_contact, sprk_organization, sprk_accesslevel, statecode FROM sprk_externalrecordaccess WHERE sprk_project = '<id>'`.

## 11. Runs and gates

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| Affected classes (ExternalAccess, CallerPrincipal*, AccessibleRecordSet*, UnifiedEvaluatorSeam, StandingGrantRuntimeUnion, GrantCacheRoundTrip, ExternalTodoScope) | 617 / 617 passed (before the isolated route-layer tests); ExternalAccess + CallerPrincipal 566 / 566 after |
| Full BFF unit suite `dotnet test tests/unit/Sprk.Bff.Api.Tests` | **13,300 passed / 0 failed / 54 skipped (13,354)** |
| `dotnet test tests/Spaarke.ArchTests` | **338 / 338** |
| code-review (Step 9.5) | 0 Critical · 1 Warning (low confidence: the contract fixture re-registers `ICallerPrincipalStrategy`, so a future third production strategy would be dropped from those tests — commented at the registration) · 4 Suggestions (`ExternalCallerContext.GetAccessibleProjectIds` O(n²) on a class with no production consumer; `ThrowIfNull` on a non-nullable parameter, house style; a pre-existing unused `using` in the /me endpoint; `virtual` added as a test seam) |
| adr-check (Step 9.5) | 0 violations · 1 Warning (ADR-003 deny codes: the read-route 403s carry no `reasonCode` — pre-existing, kept per this task's "existing 403 ProblemDetails" constraint) |
| `check-task-status-drift.ps1` | 109, 135, 136: POML completed, INDEX behind (main session owns TASK-INDEX). 037 and 039: no drift (both pending) |
| Publish size | Not measured here (main session, after merge). No package change → no new CVE |
