# Task 135 — one rule set for every contact sign-in (defect C1)

> **Date**: 2026-10-01 · **Branch**: `task/uac-r2-135` (from `task/uac-r2-109-f1`) · **GitHub**: #1058
> **Status**: code complete; manual live gate PENDING (this run forbade live writes and deploys); publish size
> measured by the main session after merge.

## 1. What changed

Before this task a CIAM (ciamlogin.com) caller's record scope was built by `CiamContactPrincipalStrategy`
straight from `ExternalParticipationService.GetGrantSetAsync`, on each grant's all-sources `AccessLevel`.
None of FR-21 (Restricted), FR-22 (Secure direct-only) or FR-23 (No Access List) ran on that path.

Now:

- `IAccessibleRecordSetService.ComposeForCiamContactAsync(contactId, entityType, ct)` is the CIAM entry.
- It and the workforce contact-only path (`ComposeAsync` → `ComposeForContactAsync`) both call ONE private
  method, `ComposeContactPlaneAsync(contactId, entityType, includeDerivedMemberTerms, ct)`. That method holds
  the only contact-plane copy of the grant read, the junction read, the batched flag read, the Secure-suppressed
  grant term (`GrantedRightsFor(..., IsSecure)`), the deny resolution and the `ApplyVetoPipeline` call.
- `CiamContactPrincipalStrategy` resolves identity exactly as before, then composes the three root types through
  the evaluator and maps `Rights` onto `ProjectAccess` / `MatterAccess` / `WorkAssignmentAccess`, as
  `WorkforcePrincipalStrategy` does. `RightsFromGrants` is deleted (no caller left). The strategy no longer reads
  a grant row or a level.
- The false parity claim on `CallerProjectAccess` (was `CallerPrincipalResolver.cs:48-50`) and the CIAM class doc
  are corrected; the file header's plane table now says CIAM goes through the evaluator.

`grep ApplyVetoPipeline(` → exactly two call sites: the systemuser composition and the shared contact-plane
composition. `CallerPrincipalResolver.cs` contains no veto, flag or deny logic.

## 2. Step 1 — the entry-point decision (§11: extend, do not duplicate)

**Chosen: a refactor of `ComposeForContactAsync` into one private core plus a new interface method.**

- **Existing**: `ComposeForContactAsync` already composed exactly "grant term + the three rules" for a contact,
  plus two derived-member terms.
- **Extension**: the core was extended with one boolean, not copied. A copy in the strategy, or a second veto
  implementation, is what the POML's first constraint forbids.
- **Cost of doing nothing**: a contact on the No Access List, or granted on a Restricted record, or inheriting an
  organization grant on a Secure record, keeps full access on a CIAM token (defect C1, live on `/api/v1/external`).

**Why a contact id and not a `WorkforcePrincipal`.** Step 1's warning was checked: passing a CIAM caller as a
`WorkforcePrincipal` would hand its `Kind`, `TenantId` and `Oid` to code written for workforce identities. A grep
today finds no direct `principal.TenantId` / `principal.Oid` reader inside `src/`, but the type exists to carry a
workforce identity and any future reader would silently treat a CIAM token as one. The new method takes a resolved
contact id only, so no CIAM caller is ever represented as a workforce identity, and the question cannot recur. The returned set's
`PrincipalKind` is `ContactOnly` (it is a contact's set); nothing in `src/` reads `AccessibleRecordSet.PrincipalKind`.

**The junction read on CIAM** is deferred until after the candidate list and skipped when there are no candidates
(the systemuser plane's NFR-02 gate, task 043). With no candidates the veto returns before consulting the subject,
so `ActiveOrgMemberships.None` there is not a fail-open. With candidates the one read happens and its `Unreadable`
outcome denies every candidate. The value is a nullable "not read yet", never a default of "no organizations".

## 3. The one plane difference — owner-signed exception (A2), escalation option (ii)

`includeDerivedMemberTerms` gates the standing-grant membership term and the organization-expansion term, and
nothing else. It is named in one code comment at the branch point in `ComposeContactPlaneAsync`.

The POML's third escalation asked the owner to choose (i) CIAM gains the derived terms, or (ii) the workforce plane
keeps them as an owner-signed exception, recommending retirement via task 142 escalation (d). The owner answered
before this run, in **decision A2 (round 3, 2026-09-30, REVERSED, binding)**: *"Standing grants and organization
access STAY, as they work today, on both sign-in types. Assigned-To grants are ADDED alongside them. Nothing is
retired."* So:

- CIAM does NOT gain the derived terms (as today). A test pins their absence, and the same test pins that the
  workforce contact plane still derives them from the same world.
- The workforce contact plane keeps them (as today).
- The plane-difference comment stays, recorded as the owner-signed exception (option ii), not a pending retirement.
- No access is added or retired by this task beyond applying the three rules to CIAM.

## 4. Step 0 — anchors and preconditions (re-verified 2026-10-01)

| Check | Result |
|---|---|
| Task 131 merged (DirectAccessLevel through the cache) | ✅ `CacheVersion = 5`; `CachedParticipation`/`CachedRootGrant` carry `int? DirectAccessLevel` |
| Task 109 merged (junction query fault → Unreadable) | ✅ on the base branch: `QueryOrganizationMembershipsAsync` returns `ActiveOrgMemberships.Failed` on non-success/timeout/exception |
| Task 138 (Limited) | ❌ not merged → the single FR-22 predicate (`IsSecure` into `GrantedRightsFor`) stays the only suppression point, now shared by both contact planes; no Limited case in the parity test |
| Task 141 (CIAM deny codes) | ❌ not merged (on `task/uac-r2-141*`). Identity resolution untouched here; expect a mechanical merge conflict in the CIAM strategy's constructor and the block after contact resolution |
| Task 142 escalation (d) | answered by owner A2 (above) |
| TASK-INDEX 037/039 drift | reconciled — both rows read 🔲 [open] |
| Anchors | `CallerPrincipalResolver.cs` anchors matched exactly. `AccessibleRecordSetService.cs` anchors had moved with 109: `GrantedRightsFor` :292-314, `ApplyVetoPipeline` :412-462, `ResolveDenyVetoAsync` :613-690, `ComposeForContactAsync` :1132-1347, `ReadActiveOrgMembershipsAsync` :506 (pre-change numbering) |

## 5. Access-removal comparison (escalation trigger 4) — READ-ONLY, dev (spaarkedev1), 2026-10-01

Method: every active `sprk_externalrecordaccess` row, the `sprk_issecure`/`sprk_accesspermission` of every record
those rows target, every `sprk_noaccessentry`, and the org memberships behind the organization rows.

- Active grant rows: **31** (27 contact rows, one of which also names an organization; 4 organization-only rows,
  all for organization `67577f8c-4301-f111-8407-7ced8d1dc988`, whose one active member is the CIAM Test User
  contact `394fda9f-ab95-f111-b8dc-7ced8ddc4cc6`). All expire 2026-12-10 / 2026-12-21 / 2026-12-31.
- Contacts reached: **8** (6 are oid-bound, i.e. CIAM-capable). Records reached: **14** (4 projects, 9 matters,
  1 work assignment).
- **Restricted among them: 0.** The only Restricted root in dev is matter `dc091784-fd63-f111-ab0c-7ced8ddc4a05`,
  which no grant targets.
- **Secure among them: 0.** The only secure root in dev is project `65a3fab2-77a5-f111-aaad-70a8a590c51c`, which no
  grant targets. (Several targets have `sprk_issecure = NULL`, read as not secure — the Q1 cleanup decision.)
- **No Access entries: 0.**
- Every target record is readable, so no fail-closed `Unreadable` removal either.

**Result: 0 contacts and 0 records change their CIAM answer in dev.** The main session decides before merge.

Heads-up for task 138 (not this task): matter `2444af6d-e1f2-f011-8406-7ced8d1dc988` is **Limited (100000001)** and
carries contact grants (52bb55e7 Collaborate, 8e9918a9 View Only, and 394fda9f via a row naming both a contact and
an organization). When 138 makes Limited real, that is where the first CIAM narrowing will show.

## 6. Tests

All at KEEP paths. No `Mock<HttpMessageHandler>`, no DI-registration or constructor null-check tests.

**`tests/integration/seam/ExternalAccess/UnifiedEvaluatorSeamTests.cs`** — every case drives the REAL
`CiamContactPrincipalStrategy` over the real evaluator, `SubjectStandingGrantReader` and `NoAccessListReader`:

| Criterion | Test |
|---|---|
| FR-21, one per root type | `Ciam_FR21_DirectFullAccessGrantOnARestrictedRoot_IsAbsentFromTheCiamPrincipal` ×3 |
| FR-23, the four key shapes | `Ciam_FR23_NoAccessEntryOfEachKeyShape_RemovesTheFullAccessRecordFromTheCiamPrincipal` ×4 |
| FR-23 targeted (task 039 amended negative control) | `Ciam_FR23_AContactByRecordEntryNamingOneContact_LeavesAnotherContactOfTheSameOrganizationUnaffected` |
| FR-22 org-only + pre-max | `Ciam_FR22_OnASecureRoot_AnOrgOnlyGrantConfersNothing_AndDirectViewOnlyPlusOrgCollaborateIsExactlyRead` ×3 |
| FR-22 survivor | `Ciam_FR22_ADirectCollaborateGrantOnASecureRoot_ConfersCollaborate` ×3 |
| Owner model (no derived terms on CIAM) | `Ciam_OwnerModel_StandingGrantAndOrgExpansionContributeNothing_WhileTheWorkforceContactPlaneKeepsThem` |
| Plane parity | `PlaneParity_SameGrantsFlagsAndNoAccessEntries_CiamAndWorkforceContactPrincipalsCarryIdenticalRights` ×4 (org grant on an open root, restricted, secure, No Access entry) — all three root types each |
| Fail closed (flag non-success, deny reader throws, subject orgs unreadable) | `Ciam_FailClosed_EachFault_RemovesEveryCandidateFromTheCiamPrincipal` ×3, each with a healthy control first |
| Fail closed (flag read throws) | `Ciam_FailClosed_RootFlagReadThrows_TheCallerIsNotResolved_NeverTheGrantsOnlyAnswer` |
| Regression | `Ciam_Regression_PlainDirectGrantsOnOpenRoots_KeepExactlyTheirGrantedRights` ×3 |

**`tests/integration/auth/UnifiedAccessControl/OrganizationMembershipReadTests.cs`** (one Theory added, ×2):
`CiamStrategy_JunctionQueryFaults_RemovesEveryCandidateFromTheCiamPrincipal` (500, 403). Justification: the
criterion names the junction QUERY failure specifically, and this file's in-memory Dataverse server is the only
harness that drives the real query; a double returning `Failed` would assert the double (the lesson of #998). The
fake server gained a `contacts` collection that answers only the bound oid, so identity resolution is real.

**`tests/unit/.../CallerPrincipalResolverTests.cs`**: the CIAM strategy's own contract —
`CiamStrategy_ResolvedContact_TakesAllThreeRootScopesFromTheEvaluatorForThatContact` (also verifies the strategy
never calls `GetGrantSetAsync`); the two identity-deny tests gained the new constructor argument and a Strict
evaluator mock that must never be reached. The CIAM identity tests proper (oid-first, first-login bind, no-hijack —
`WorkforceEmailNoHijackTests`, `ResolveExternalContactAsync`) are untouched.

**Fixture change, `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs`.** Found
empirically (§F.3): after the change, `DownloadDocument_WhenAuthorizedAndDocumentInProject_Returns200_AndStreamsBytes`
returned 403, because the CIAM path now reads the junction, the referenced organizations and the No Access List, and
offline those fail CLOSED. The stub participation service now answers "no organizations / references none", and the
fixture swaps `INoAccessListReader` for the shared never-deny reader. That is the honest default for contract tests
of an entitled caller; the vetoes are owned by the seam suite. No assertion changed.

**Overlap, justified**: the parity scenarios and the regression test both assert exact rights. Parity asserts the
two planes agree on the vetoed and org-granted shapes; regression asserts CIAM keeps exactly its direct grants on
open records (criterion 8). They share no scenario.

**Beyond the closed set, justified**: the targeted-control test answers task 039's amended criterion ("a different
contact of the same organization is unaffected"), which names this task as its completing work. Task 039's other
amended criterion — a deny-then-Restricted ORDER proof on CIAM — is deliberately not written: 039 recorded that the
two slots commute for pure removals, so an ordering test can never fail; the order on CIAM is the single shared
`ApplyVetoPipeline`, which is now the only implementation either contact plane runs.

### Perturbations (each applied, run, then reverted; the evaluator file was byte-restored from a backup)

| Perturbation | Result |
|---|---|
| P1 — skip `ApplyVetoPipeline` on the CIAM path | **14 red** (FR-21 ×3, FR-23 ×4, parity restricted + No Access, fail-closed ×3, wire junction ×2) |
| P2 — CIAM reads `AccessLevel` on secure roots (`isSecure: _ => false`) | **4 red** (FR-22 ×3, parity secure) |
| P3 — CIAM composes the derived terms (`includeDerivedMemberTerms: true`) | **1 red** (owner model) |
| P4 — blanket organization deny (any wall organization denies every candidate) | **5 red** (the four key shapes on their control records, and the targeted-control test) |

### Runs

- BFF build: 0 warnings, 0 errors (warnings are errors in this repo).
- Affected classes (seam, resolver, membership-read, contract, evaluator unit): **171 / 171 passed** at the final state.
- Full BFF unit suite at the final state: **13,240 passed, 0 failed, 54 skipped (13,294)**. An earlier full run
  (before the targeted-control test existed) had one failure,
  `PinnedMemoryEndpointsContractTests.DeletePin_Authenticated_Returns204AndEmitsCounter` after 35 s — unrelated (no
  ExternalAccess path), green on rerun in isolation and in the final full run.
- `Spaarke.ArchTests`: **338 / 338 passed**.
- Format: the two changed source files are clean under `dotnet format whitespace --verify-no-changes`; the
  formatter's remaining complaints in `ExternalAccessContractTests.cs` are on pre-existing lines.

### Step 9.5 quality gates

**code-review** — 0 Critical. Findings, all accepted without change:
- W1 (availability, intended): a transient fault in the flag, junction or No Access read now fails a CIAM request
  closed, where before nothing was read. That is NFR-01, the same as the workforce contact plane.
- W2 (cost): each of the three compositions calls `GetGrantSetAsync` (60 s cache, fire-and-forget write), so a cold
  cache can cost up to three grant reads per request — the workforce contact plane's existing budget. A follow-up
  could compose all three roots in one evaluator call for both planes; out of scope (hot files, workforce strategy).
- S1: the composition log line keeps its `[WF-AUTHZ]` tag on CIAM; the message now names the plane.
- S2: `includeDerivedMemberTerms &&` in the org-expansion condition is redundant with the null junction value on
  CIAM; kept as an explicit guard against a future pre-read.
- S3: the contract fixture reuses the unit folder's `AccessibleRecordSetTestFactory.NeverDeniesReader()` (same
  assembly) rather than adding a fourth "inert reader".
- Metrics: `AccessibleRecordSetService.cs` 1,348 → 1,464 lines (mostly documentation), +1 public / +1 private
  method; a pre-existing large but cohesive evaluator, no new responsibility. `CallerPrincipalResolver.cs`
  549 → 563, one private method removed.

**adr-check** — 0 violations. Compliant: ADR-003 (vetoes after the max in order, Secure before it, fail closed, no
decision cached), ADR-028 A1/A3 (identity unchanged, app-only, no OBO, handlers plane-agnostic), ADR-010 (no new
registration; a method on an existing seam), ADR-007/008/009/013/052 (not touched), ADR-038 (KEEP paths; diff grep
finds no `Mock<HttpMessageHandler>`, DI-registration test, constructor null-check test, reflection or clock use).
Low-confidence note: `IAccessibleRecordSetService` has one production implementation; it is a pre-existing,
documented ADR-010 testing seam and this task adds no interface.

## 7. Placement (CLAUDE.md §10, `.claude/constraints/bff-extensions.md`)

- **In the BFF, in the existing evaluator.** It is the per-request authorization decision on `/api/v1/external`
  (latency-bound, same request lifecycle); ADR-052 places nothing here elsewhere.
- **New surface**: one interface method on the existing `IAccessibleRecordSetService` (an ADR-010 seam that already
  exists for this evaluator) and one new constructor dependency on `CiamContactPrincipalStrategy`. No new service,
  endpoint, option, job, DI registration or NuGet package. `ExternalAccessModule.cs` is NOT edited: both the
  strategies and the evaluator are already scoped registrations, so the dependency resolves as is.
- **ADRs**: ADR-003 (vetoes after the max, Secure before it, fail closed), ADR-028 A1/A3 (identity unchanged,
  broker-only, app-only, handlers plane-agnostic), ADR-010 (no new registration), ADR-038 (KEEP paths, no bans).

## 8. Request cost (NFR-02)

Per CIAM request, before: one grant-set read (60 s cache). After: the workforce contact plane's budget — per root
type with candidates, one grant-set read (cache), one batched flag read, one junction read, one referenced-org read
and one deny-list read; a root type with no candidates costs only the cached grant-set read. That is the budget the
POML's NFR-02 constraint names.

## 9. Known residue (not this task's)

- **C2 / task 136**: a Secure organization-only grant now yields a None-rights key on CIAM (as on the workforce
  plane) instead of a full-level grant. The presence-only read gates still admit it until 136 makes them rights-based.
- **C12 / task 132**: grant sets built over a faulted read can still be cached; unchanged here.
- `CallerProjectAccess.FromLevel` has no production caller any more (test fixtures only); its doc now says it must
  not be used to build a principal from grant rows.
- `ExternalAccessModule.cs:174` comment still says only `WorkforcePrincipalStrategy` depends on the evaluator —
  stale now, but that file is task 134's to edit.

## 10. MANUAL live gate — PENDING (dev, no CI)

Signed into the external SPA as an existing CIAM test contact (e.g. **CIAM Test User**, contact
`394fda9f-ab95-f111-b8dc-7ced8ddc4cc6`), after this branch is deployed to the dev BFF:

| Step | Setup (operator) | Expected |
|---|---|---|
| (a) | A project the contact holds FullAccess on (today it holds only View Only directly, on project `7524864d…`; give it a FullAccess grant through Grant Access, or use an existing one); then set the project's Access Permission to **Restricted** | Disappears from `/api/v1/external/me`; `GET /api/v1/external/projects/{id}/documents` and `/documents/{doc}/content` return 403 |
| (b) | Restore Standard; add the contact to the project's No Access List | Same as (a) |
| (c) | A Secure project provisioned through `/provision-project` (never by editing `sprk_issecure`), reached only through organization `67577f8c…`'s grant | Not accessible (rights None on `/me`; reads are closed fully only once task 136 lands) |
| (d) | A direct grant to the contact on a Secure, non-Restricted project | Accessible at the granted level |

Restore every test record afterwards. Evidence (screenshots or `/me` JSON per step) goes into this note.
