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

## 12. Live gate, workforce half (2026-10-14, owner-approved dev writes)


**Target and build.** spaarke-bff-dev, Dataverse spaarkedev1. Kudu deployment `7e142006…` (20:14:11Z). `Sprk.Bff.Api.dll` SHA-256 `8f2d5138…`, InformationalVersion `1.0.0+4e23edeb2…`, the same build as the 187 doc-leg re-run. Task 136's code is present in the DLL: `HoldsReadOnProject`, `ReadableProjects`, `FromReadBearing` and `RemoveEntriesWithoutRead`.

**Callers.** These are the same token mechanism as gate187 (`common.py`).
- admin: the `az` default profile, systemuser `1d02f31c…`. `can-manage-access` on P returned 200 true, and the grant ceiling was FullAccess.
- testuser1: `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, contact `ac6d7b68…`, linked from systemuser `8d7bad7a…`. It is on the workforce plane, and `/me` returns its systemUserId.

Tokens were held in memory only. No Key Vault was read. Document content was hashed in memory and discarded.

**Record P.** `31e232ae-55c2-f111-a05c-0022482913fc` is "UAC gate 171 2026-10-07 PS (secure)": sprk_issecure true, accesspermission Standard, active. Secure project `65a3fab2…` was not touched (it was only listed in the sprk_issecure query).

**Files.** All in scratchpad `gate136/`:
- `before.py` / `before.json`: step 0
- `gate.py`: the steps
- `matrix-*.json`: the route results
- `writes.jsonl`: the write ledger
- `ai*.kql`: the App Insights queries
- `gate136.log`: the full log

| G | Step | Verdict | Evidence |
|---|---|---|---|
| G0 | Before-state (read-only) | **DONE** | **testuser1's contact** `ac6d7b68…` is active. Its parentcustomer is account `826f48e9…`. It has **0** `sprk_contactorganizations` memberships, so it has no organization in the access model. It has 2 active direct grants: `144b5110…` (project `edb87d10…`, ViewOnly) and `4fe8227c…` (project `b12496d1…`, Collaborate). No organization grants reach it. **P:** **0** `sprk_externalrecordaccess` rows in any state. The `no-access` read says secure applies, noAccess doesNotApply, entries []. The Assigned-To ledger on P has 0 rows. **Documents stamped to P:** `2b97180e…` has SPE pointers; `b02ebe33…` has none. **`/me`:** 200, 20 projects, P absent, no "None" level. `GET /projects` returned 20, P absent. **No Access entries:** 1 in total (`33a4e845…`, subject systemuser `d6f8f439…`, object project `{EDB87D1…` with the id in braces). None names testuser1. **Restricted roots:** matters `232852f8…`, `b68299c6…`, `dc091784…` and work assignment `b10b7dab…`. testuser1 holds no contact or organization grant on any of them. **Limited:** 0. |
| G1 | Organization grant on P, then deny | **NOT RUN as specified; substitute PASS** | **Why the precondition cannot be built through the BFF:** (a) testuser1 has no organization membership. The only membership source is the `sprk_contactorganizations` junction (`ExternalParticipationService.BuildOrganizationMembershipFilter`), so no organization grant can reach it without a membership write, which was not approved. (b) `/grant` refuses an organization-wide grant on a Secure root before any write: `ExternalGrantLifecycle.DecideGrantPolicy` (`:693`) returns `OrgGrantOnDirectOnly`, which is 422 `org_grant_direct_only_record`. So the org-only-on-Secure state can come only from rows written before task 138 or outside the BFF, and step 0 found none. No organization grant was attempted. **Substitute (testuser1 holds nothing on P, 21:58:52Z):** P was absent from `/me` and `GET /projects`. These routes each returned **403** "You do not have access to this project": `GET /projects/P`, `/documents`, `/documents/2b97180e…/versions`, `/events`, `/contacts`, `/organizations`, `/todos`, and `/documents/{d}/content` for both documents. **Deny before pointer resolution (App Insights dependencies):** on the three denied content requests and the denied versions request, only principal-building reads ran: `contacts(ac6d7b68…)`, `sprk_projects`, `sprk_matters`, `sprk_contactorganizations`, `sprk_noaccessentries`, Redis HMGET and one Organization.svc call. There was **no `sprk_documents` read and no Graph or SPE dependency.** |
| G2 | Direct ViewOnly grant | **PASS, with one caveat (O-136-1)** | **Write W1:** `POST /api/v1/external-access/grant` {contact testuser1, P, ViewOnly} returned 200 at 21:59:22Z. It created `e8f85a7f…` with level 100000000 and expiry 2027-01-12. invite was NotNeeded (no email), notificationFailed false, narrowed false. **Effect:** visible at once, because the write invalidates the cache (3 Redis UNLINKs). It was re-checked after the 60 s grant-cache TTL (`ExternalParticipationService.CacheTtl`) at 22:02Z, with identical results. **`/me`:** 21 projects, P **ViewOnly**, 0 "None". `GET /projects` lists P. **Reads that answered 200:** `/projects/P`, `/documents` (2), `/events` (0), `/contacts` (1), `/organizations` (1) and `/todos` (0). **Content and versions:** `2b97180e…` returned **409** `document_storage_unverified` on both. This refusal comes after authorization: the request read `sprk_documents(2b97180e…)`, and `SpeContainerOwnershipGuard` refused P's container `b!OKn3KsSgN0iB…` ("not one of this stamp's containers") with no Graph call. `b02ebe33…` returned 409 "No file attached". **So "content streams" could not be shown on P (no byte count or hash).** **Mutation refused:** testuser1 `POST /projects/P/todos {}` returned **403** `sdap.access.deny.insufficient_rights`. The body deliberately had no `sprk_name`, so the handler would have answered 400 with no write even if the Create gate were missing. |
| G3 | No Access entry add / remove (039 c5) and Restricted root (037 c5(i)) | **NOT RUN: needs a write outside the approval** | **No Access:** the BFF has no endpoint that creates or removes a `sprk_noaccessentry`. The model-driven form writes it directly to Dataverse. The BFF only reads it (`NoAccessListReader`, `GET /api/v1/records/{type}/{id}/no-access`), and `POST /no-access/enforce` only removes POA shares for an existing entry. Adding one would be a direct Dataverse create, which is outside "BFF endpoints only", so this step stopped before any write. No read-only substitute exists, because the one existing entry walls another systemuser. **Restricted:** no Restricted record carries a grant for testuser1. A new contact grant on a Restricted root is refused by policy (422 `record_restricted`), and no record's flag was changed. Note: per G2' of 187, testuser1 does read Restricted matter `b68299c6…` through its systemuser membership. That is FR-21 behaviour: term 1 is kept, and only contact-grant terms are vetoed. It is not the contact-grant case 037 c5(i) asks about. |
| G4 | Clean-up | **PASS** | **Write W2:** `POST /api/v1/external-access/revoke` {accessRecordId `e8f85a7f…`, contactId empty} returned 200 at 22:02:41Z. deactivatedCount was 1 and speContainerOutcome NotAttempted. The empty ContactId was deliberate, so that no SPE permission call was made. The only Dataverse write was a PATCH to statecode 1/2. **Read-back:** P has 1 row, inactive (`e8f85a7f…`, 1/2), and **0 active**, the same as before. Immediately and again after the TTL (22:05Z), every P route returned 403 and P was absent from `/me` and `/projects`. **`/me` equals step 0 exactly:** the same 20 (project, level) pairs, contactId and systemUserId. testuser1's direct grants are unchanged (`144b5110…`, `4fe8227c…`). The Assigned-To ledger on P is still empty. |
| G5 | Regression, App Insights since T0 (21:59:18Z) | **PASS** | **5xx:** 0. **Exceptions:** `SdapProblemException spe_container_not_owned` ×4, which are the four admitted P content and versions calls. The same class and message occurred ×4 in the prior 7 days, and the guard has refused this same container **1,710 times since 2026-10-08 23:00Z**, including background traffic about every 5 minutes. Also Graph `ODataError` "Invalid hostname for this tenancy" ×2, the known orphan-container baseline (2,004 in 7 days). **New exception class:** none. **Grant and revoke dependencies:** 0 failed. There was no Graph call on any P route (0 rows). **IDX10214 error traces:** 38 since T0, against 1,028 in the prior 7 days; this is pre-existing noise. |

**Write and clean-up ledger** (`writes.jsonl`)

| # | Write (through the BFF) | Result | Side effects | Clean-up |
|---|---|---|---|---|
| W1 | `POST /external-access/grant`, contact `ac6d7b68…`, P `31e232ae…`, ViewOnly (21:59:22Z) | 200, row `e8f85a7f…` created | Redis grant-cache UNLINK ×3. In-app **appnotification `88f30e84…`** for systemuser `8d7bad7a…` (testuser1): "You were given access to the project 'UAC gate 171 2026-10-07 PS (secure)'". No invite or email (NotNeeded). No Assigned-To ledger row. | Row deactivated by W2. The appnotification **remains**: no BFF endpoint removes it, and a direct delete was not approved. It is harmless (testuser1's own bell). |
| — | testuser1 `POST /external/projects/P/todos {}` | **403** `insufficient_rights`; nothing written | none | n/a |
| W2 | `POST /external-access/revoke` `e8f85a7f…`, contactId empty (22:02:41Z) | 200, deactivatedCount 1, SPE NotAttempted | Redis UNLINK ×3. PATCH statecode 1 / statuscode 2. | n/a: this is the clean-up. P's active set is 0, as before. |

No other write was made: no organization grant, no membership, no No Access entry, and no flag, Entra, Key Vault, app-setting or role change. There was no SPE write and no process kill.

**Observation and defect candidate (not caused by this gate; report and file)**
- **O-136-1:** Secure project P's own container `b!OKn3KsSgN0iB…` is not recognised by `SpeContainerOwnershipGuard`. It is neither a configured stamp container nor carries this customer's marker. So every app-only content or versions read of P's documents answers 409 `document_storage_unverified`, even for an authorized caller. The guard has logged this refusal 1,710 times since 2026-10-08 23:00Z, including recurring background traffic.
  - Likely cause: P was provisioned on 2026-10-07, before the D28/D29 marker shipped, and was never backfilled. Only `ContainerOperations` and `SpeAdminGraphService` call `MarkOwnedAsync`, and only at create time.
  - Effect: on dev, no Secure-project document can be streamed until P's container gets the marker.
  - Not a task 136 defect: authorization admitted the request. But it blocks the "content streams" half of 136 criterion 4 and of 037 c5(iii) on this record.
  - Filed as #1889.

**What this evidences**

| Criterion | Evidenced here | Still open |
|---|---|---|
| 136 c1 (WF, org-only on Secure → absent + 7×403 + no SPE call on content) | Partly. The live route-level deny for a caller holding nothing on Secure P: 7 routes 403, P absent from `/me` and `/projects`, and no pointer read or Graph call on the denied content and versions. It also shows, by code and policy, that the org-only-on-Secure state cannot be written through `/grant`. | The org-grant-to-None-key path itself, live. It needs an organization membership for testuser1 plus a pre-138 or out-of-band organization row. Neither is approved, and it stays covered by the seam and contract tests. |
| 136 c4 (direct ViewOnly on Secure admits reads, refuses mutations) | Yes, workforce plane. `/me` ViewOnly, listed, 6 read routes 200, todo create 403 `insufficient_rights`. | Content and versions bytes: blocked by O-136-1 (409 after authorization). |
| 136 c12 (MANUAL CIAM gate) | No. This is the workforce plane. | **CIAM sign-in (owner):** ciamlogin.com token, organization grant on Secure → not listed, `/me` without the id, content 403; then a direct ViewOnly grant → read-only access. The org-grant leg hits the same two obstacles as G1 (membership, and the 422 policy), so the owner needs a CIAM contact with an active `sprk_contactorganizations` row and must decide how an org-only grant on a Secure root is to exist. |
| 037 c5 (i) Restricted no list or download; (ii) org-only Secure; (iii) direct grant readable | (iii) on the workforce plane only (G2, without bytes). | (i) and (ii), and the whole CIAM-token half. (i) needs a contact grant on a Restricted root, which policy refuses (422) unless the grant predates the flag. |
| 039 c5 (No Access entry blocks; removal restores after TTL) | None. | All of it: it needs a `sprk_noaccessentry` create and deactivate, which the BFF does not expose (form or direct Dataverse). It is owner-approved or owner-performed, with a CIAM token. The deny list is read live per request (`NoAccessListReader`, no TTL), and the grant set is cached for 60 s. |
| 135 gate / amendment n=2 "session A item 3" | The workforce observations above. | The combined CIAM session. 136, 037, 039 and 135's gate stay **pending** (amendment n=2: they close together on that session). |
