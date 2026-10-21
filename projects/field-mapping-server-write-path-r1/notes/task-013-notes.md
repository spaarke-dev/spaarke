# Task 013 notes: the unified RecordCreationService, Create mode (FR-06, FR-07, FR-12)

## Rigor declaration

- RIGOR LEVEL: FULL (POML `<rigor>`; tags bff-api, security, testing; new `.cs` in `Sprk.Bff.Api`; tests at KEEP paths).
- MODEL TIER: opus @ xhigh (POML). Branch `task/fm-013`, cut from `bad3195920` (project branch head).
- Context loaded: POML, spec FR-06/07/08/09/11/12 and NFR-01..08, design §4, `notes/task-011-notes.md`,
  `notes/task-012-notes.md` (pre-write recipe and the ebilling-r1 corrections), `notes/w2-coordination.md`, ADR-008/010/
  019/028/038, `.claude/constraints/bff-extensions.md`, the porting source (`OwnedChildWrite.CreateAsync`), the
  child-record route (`ChildRecordEndpoints.CreateAsync`, `CompleteRootCreateAsync`, `PlanRefused`, `MirrorAsync`) and
  the Office create (`CreateNumberedAsync`, `WarnIfNumberMissingAsync`).

## Conflict check (POML step 1)

29 open PRs. #2161 and #2150 touch `OwnedChildWrite.cs` only in `RefileAsync`; #2110 (UAC-r2) merged 2026-10-19 and is
in this branch's base; `origin/master` was 0 commits ahead of the base at the check. Re-checked before commit: `origin/master` is now 80 commits ahead, and none of them changes a file this task touches or ports from (`git log bad3195920..origin/master` over `Services/Dataverse/`, `Services/Office/`, `Services/RecordCreation/`, `ChildRecordEndpoints.cs`, `OfficeModule.cs`, the census and the seam README: one merge commit with no change in those paths). No open PR rewrites `OwnedChildWrite.CreateAsync`,
so the POML escalation trigger does not fire. Hot-path result: soft warn (other projects own BFF hot paths; no file
overlap with this task's files), no escalation.

## Placement Justification (CLAUDE.md §10)

BFF, `Services/RecordCreation/`. The create is a synchronous request-path write (spec "Placement"): the caller waits for
the id, the checks run under the caller's own token (ADR-028), and every existing create core it merges is already in
the BFF. No background work, no new package, no new endpoint.

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationService` (sealed, scoped) | `OwnedChildWrite.CreateAsync` (children, G5) and Office `OfficeRecordCreationService` (roots) | Merges the two cores (spec §11 "Yes: merges the two existing cores"); both stay until 017/018 re-point callers | The same row under the same parent gets a different owner, stamp and checks by surface; mapping reaches 2 of about 30 create paths |
| `RecordCreationRefusal` + `RecordCreationRefusalKind` | `OwnedChildWrite.Outcome` (arms, no kind), Office `RecordCreationResult` error codes | `Outcome` is the G5 core's shape, also used by `RefileAsync` and external callers; changing it would touch the re-file half (UAC (b)) | FR-12 has no typed `(Kind, Code, Detail)` for the three adapters to share; each would map its own |
| `RecordCreationProblemMap` (static) | `ChildRecordEndpoints.Problem`/`CallerFailure`/`PlanRefused`, `ProblemDetailsHelper.RecordOwnerRefused` | Those are route-private and pass upstream text through; FR-12 needs ONE map for route, Office and chat | ADR-019 violation persists (raw Dataverse text in bodies) and each adapter maps differently |
| `RecordCreationRequest`, `RecordCreationPayload`, `RecordCreationPrincipal`, `RecordCreationOutcome`, `RecordCreationStep*` | none (the cores take positional arguments) | Value types of the service's own contract, in its file | FR-07's two principals and the per-step record (NFR-04, the seam test) have no carrier |
| DI: `services.AddScoped<RecordCreationService>()` in `OfficeModule.cs` | `AddScoped<OfficeRecordCreationService>()` | Added beside it; the Office registration goes with the class in 018 (POML step 6) | The service cannot be resolved by the adapters 017/018 add |
| `TableRules` init property (test seam) | `RecordTableRules.Find` | Defaults to it; only a test sets it | No way to prove the pre-write runner without the invoice-family rule, which is not on this branch (ebilling-r1) |
| ArchTest `OwnedChildWriteCallerWriteAuthorizationNameGuardTests` | none | — | K2: a same-named member in either class silently rebinds the other's calls |
| `ChildRecordEndpoints.IsUnresolvedCaller` private → internal | — | Modification only (the service reuses the route's unresolved-caller rule instead of copying it) | — |

No interface (ADR-010): one implementation; the static engines stay static.

## ADR-008, path A

Payload-dependent authorization (every lookup, the regarding pair, field security, privileges) stays in
`CallerWriteAuthorization` and is CALLED by the service (steps 1 and 5, and the plan's AppendTo callback). It is not an
endpoint filter. Route-level authorization stays in the routes' filters. Cite path A in the PR description.

## Decisions

1. **Office class rename (POML constraint):** renamed to `OfficeRecordCreationService` (preferred option); the file name
   `Services/Office/RecordCreationService.cs` is kept (task 018 deletes it). 45 references updated in src and tests; prose
   mentions in comments left as they were.
2. **Seam test folder:** `tests/integration/seam/RecordCreation/` (POML output said `seam/Office/`, "or the folder the
   README prescribes"). The csproj rule is `seam/{Module}/`; the module is RecordCreation, not Office (task 018 deletes the
   Office class). README sub-category added.
3. **Step order.** Steps 1, 2, 4, 5, 6, 7, 9, 11 are `OwnedChildWrite.CreateAsync`'s and the route's, in their order.
   Step 3 (mapping) and step 8 (search index) are recorded `NoOp` seams; step 7 is ONE private method
   (`DecideOwnerAsync`) and step 8 ONE method (`SearchIndexDefault`), for task 015 to swap.
4. **Step 10 runs after the create (ESCALATION, see below).** Recorded `Deferred` in its place; executed inside step 12
   in the route's position.
5. **Step 2 enforces the table's own server-owned columns for every caller path** (owner-review item 4, #1608): the work
   assignment's container, secure BU and access permission are refused for the chat path too once 017 re-points it. The
   route refuses them today; the chat tool does not. The owner may still pick the other behaviour before 017.
6. **Pre-write rules (ebilling-r1 recipe)** run after step 5 (as the caller, on the payload as it will be written) and
   before steps 6-11. Corrections: (a) does not apply on this branch (`EffectiveLookups` is not here; the runner passes the
   mapped item, and 014 must pass the merged item); (b) the not-found detail is `NotAvailableToFileUnder`, verbatim;
   (c) the rule's own `PreWriteRefusal` object is carried by reference in `RecordCreationRefusal.PreWrite`. A rule's
   `Refused` renders 400 with the rule's code (ebilling's 404/500 split by code is not reproduced; reconcile when the
   invoice-family rule lands). `PreWriteRefusal` carries only Code/Detail/ClientFailure/ParentUnavailable, so any richer
   "decided object" ebilling needs is a field to add at reconciliation.
7. **System principal and pre-write rules:** a table with a pre-write rule refuses a system create
   (`record_creation.system_principal_unsupported`, 403) — the rule is checked as a caller and there is none (task 012's
   open point; fail closed).
8. **System principal and filing under a record (fix round 1, F4/F5; main-session decision, fail closed):** a system
   create of a work assignment or project that is filed under a record (it writes one of the table's filing columns,
   `SecureRootInheritance.FilingColumnsOf`) is refused at step 6, BEFORE `SecureRootFilingGate.PlanCreateAsync` runs:
   403 `record_creation.system_principal_unsupported`, nothing read, nothing created. Why: under a secure parent the plan
   creates the row secure and shares it to the person it is made for, and the only check that the person may file under
   that parent is the caller's own AppendTo, which a system create does not have. Before the plan reads the parent
   nothing says whether it is secure, so the refusal also covers a system create filed under an ORDINARY record. A system
   create of either table filed under nothing proceeds (tested). The round-0 wording ("refused when the plan would
   isolate") was wrong: that branch was unreachable, because the plan itself refuses an empty creator first
   (`ProvisionProjectEndpoint.ReasonCreatorUnresolved`, 409), and with a named person it isolated the row and shared the
   secure root to that person unchecked (the reviewer's F4).
9. **Ownership exception:** an unfiled communication (`UnfiledCommunicationKeepsCreator`, E1) is refused
   (`record_creation.ownership_exception`, 409): an app-only create cannot keep a creator. (`OwnedChildWrite.CreateAsync`
   has no such check; its callers never pass an unfiled communication — `email.draft` creates those as the user.)
10. **Fail closed (NFR-01):** any exception before the create refuses with 503 `record_creation.check_failed`; an owner
    decision that faults is 503 `record_creation.owner_unavailable` (UAC (b)). Before: both propagated as a 500.
11. **Autonumber retry (NFR-03):** only for a table with an `AutonumberColumn`, only on an alternate-key duplicate,
    only when the row does not name its own number, retried on the SAME id (a PATCH upsert of the same id cannot
    double-create), 3 attempts, then 409 `record_number_unavailable` (the Office code). Fix round 1 (F3): a CALLER payload
    that names the autonumber column is refused at step 2 as `BlockedColumn` 403 `record_creation.blocked_column`, the
    same refusal shape as the other server-owned columns ("Column 'sprk_matternumber' of a matter is set by Spaarke —
    omit it."). This matches the Office core's own rule (`sprk_matternumber` / `sprk_projectnumber` are never written
    there), and the child-record route does not create matters or projects, so no existing path changes. A server
    writer's mapped item that names a number is not retried: a retry cannot change a number the row itself names. Detection: HTTP 412 from `UpdateRecordFieldsAsync` or
    `DataverseServiceClientImpl.IsAlternateKeyDuplicate`. **Live check needed:** that a Web API PATCH upsert answers a
    duplicate autonumber with 412 and that a retry draws the next number (measured live only for a POST, by
    `ContactIdentityStore`). Any other create fault propagates, as before.
12. **Membership event** is published for matter, event and document creates (the four existing sites' tables), with the
    owner team the create bound (no read). Task 017/018 must REMOVE the existing publish at each re-pointed site
    (`OfficeEndpoints.cs:1844` for the matter, `EventEndpoints.cs:770`, `DataverseDocumentsEndpoints.cs:861`), or the event
    is published twice. The child-record route's events start publishing when 017 re-points it (today it does not).
13. **Upstream failures** keep their meaning but not their text: 400→400, 401/403→403, 404→the uniform 404, 429→429,
    408/504→504, anything else→503. The route today passes Dataverse's status and message through (and 502 for a
    non-HTTP status).
14. **Left in `OwnedChildWrite`** (task 011 asked 013 to decide): `CheckCallerMayRefileByPairAsync` and
    `CallerMayFileRootUnderSecure` stay — they are the re-file half's (UAC (b): untouched); K2 is closed by the ArchTest.
15. **NFR-04 counters** (refused by kind) are not added: the per-step logs and the step record are; a counter is new
    surface whose design (meter name, dimensions) belongs with 014/015's mapped/skipped counters. Known limit, flagged.

## OwnedChildWrite.CreateAsync vs the new pipeline (UAC-r2 condition (b))

| # | `OwnedChildWrite.CreateAsync` / route today | New pipeline | Order | Refusal code |
|---|---|---|---|---|
| 1 | route: WhoAmI; unresolved → 403 `sdap.access.deny.caller_unresolved` | step 1, same rule (`IsUnresolvedCaller`) | same | same |
| 2 | route: map payload; work-assignment server-owned columns → 403 | step 2 (+ the common server-owned columns the G5 core checks first) | same | `record_creation.blocked_column` (was the route's denied code) |
| 3 | `CheckCallerMayCreateAsync` (server-owned, privileges, field security, pair, AppendTo) | step 5, the same call | same | ParentUnavailable → uniform 404; Denied → 403; Invalid → 400 |
| 4 | secure-root plan with the caller's AppendTo callback (same `checkedRecords` rule) | step 6, same call and callback | same | plan codes unchanged (AccessDenied → 404; creator-no-access/secure-flag/server-only → 403; unverifiable/team unresolved → 500; else 409) |
| 5 | isolated plan → `CreateIntoIsolationAsync` (named team bind, `sprk_issecure`, creator person) | `IsolatedCreateFields`, then step 11 | same | — |
| 6 | `ResolveOwnerAsync(ForParents(...) with { RequestedBy })` | step 7 `DecideOwnerAsync`, same context | same | resolver codes unchanged; not owned → `NoOwnerSource` as before |
| 7 | Secure owner on an inheriting table / a non-reparentable child → `SecureFilingRefused` (route: 409 `NoOwnerSource`) | same checks, same words, 409 `NoOwnerSource` | same | same |
| 8 | owner bind `/teams(...)` + `StampCreatorOn` | `OwnedCreateFields`, unchanged | same | — |
| 9 | ONE `UpdateRecordFieldsAsync` on a fresh id | step 11, same call | same | (retry only for autonumber tables) |
| 10 | route: isolated completion (`CompleteIsolatedCreateAsync`; removed → 500 its reason code) → "Assigned *" | step 12 b, c | same | same |
| 11 | route: I-1 re-stamp → I-16 → sharee mirror | step 12 d, e (step 10 deferred here) | same | — |
| — | owner fault: exception (500) | 503 `record_creation.owner_unavailable` | — | **changed** (UAC (b) wording) |
| — | check exception: exception (500) | 503 `record_creation.check_failed` | — | **changed** (NFR-01) |

`RefileAsync`, `ReparentAsync` and `AfterRefileAsync` are untouched; `OwnedChildWrite.cs` is not edited.

## Code correspondence for 017/018 (route codes → service codes)

| Case | Route today | Service (`RecordCreationProblemMap`) |
|---|---|---|
| Table not accepted | 400 `child_record.table_unsupported` (route accept-list) | Invalid 400 `record_creation.table_unsupported` (not in the registry; the route keeps its own accept-list) |
| Caller unresolved | 403 `sdap.access.deny.caller_unresolved` | PrincipalUnresolved 403, same code |
| WhoAmI / mapping / check client failure | Dataverse status + code + raw message (502 if not HTTP) | Upstream: mapped status, failure's code, fixed words |
| Payload not mappable / bad pair | 400 `child_record.invalid_payload` | Invalid 400 `record_creation.invalid_payload` |
| Server-owned column | 403 `child_record.denied` | BlockedColumn 403 `record_creation.blocked_column` |
| Privilege / field-secured | 403 `child_record.denied` | Forbidden 403 `record_creation.denied` |
| Parent missing or not appendable | 404 `child_record.not_found` | NotFoundOrDenied 404 `record_creation.not_found`, one body |
| Plan refusals | 404 / 403 / 500 / 409 by code | the same split, the plan's codes |
| Owner refusal / secure filing | 409 resolver code / `NoOwnerSource` | OwnerUnresolved 409, same codes, same "The {noun} was not saved: …" detail |
| No decision | 500 `child_record.no_decision` | Undecided 500 `record_creation.no_decision` |
| Isolated row removed | 500 its reason code | Undecided 500, same code and words |
| Autonumber exhausted (Office) | 409 `record_number_unavailable` | Conflict 409, same code |
| Owner / check fault | 500 (unhandled) | Unavailable 503 `record_creation.owner_unavailable` / `check_failed` |
| Caller names the autonumber column | (no route creates a matter or project; Office never writes it) | BlockedColumn 403 `record_creation.blocked_column` |
| System create, table with a pre-write rule | (no system path today) | Forbidden 403 `record_creation.system_principal_unsupported`, before the rule runs |
| System create, work assignment / project filed under a record | (no system path today) | Forbidden 403 `record_creation.system_principal_unsupported`, before the plan reads anything |

Every body carries `errorCode` and `reasonCode` (same value; the route's clients read `reasonCode`) and `correlationId`.
NFR-07: the Office adapter (018) must keep its own Office error codes — map them in the adapter, not here.

## ESCALATION — step 10 order (POML trigger 1)

Spec FR-06 step 10 says "I-1 stamp + I-16 derived **into the payload**" (before the create). Every existing path derives
both AFTER the create (`CoreAncestorRestamper.AfterWriteAsync`, `RefreshInheritedAccessPermissionAsync`), and UAC-r2
condition (b) requires the I-1 stamp to keep its ORDER. The two cannot both hold.

- Ordering A (implemented, behaviour-preserving): create, then re-stamp and I-16 in step 12 (route order). Cost: a
  window in which the row exists without its stamp; the stamp job repairs a failed stamp within one cycle.
- Ordering B (spec as written): derive the stamp and I-16 into the create payload. Changes the I-1 order (UAC (b)), and
  needs a pre-create resolver read path the restamper does not expose today.

Recommendation: keep A and amend FR-06 step 10 to "derived after the create, in the post-create steps". Owner to choose.

## OWNER/017-018 — a system create filed under a record

Decision 8 refuses every system create of a work assignment or project filed under a record, secure or not. No caller
needs one today (the three adapters 017/018 add all act for a caller). If a server adapter later needs to file one, the
alternative is an app-only access check on `RequestedBy`: before the plan, check as the app that the person named in
`RequestedBy` holds AppendTo on the parent (the check the caller's own token makes today), and pass that check as the
plan's `callerMayFileUnder` callback. It needs an owner decision: an app-only read of another person's access is a new
kind of check (ADR-028), and it must fail closed on an unresolved person exactly as the caller path does.

## Files

- New: `Services/RecordCreation/RecordCreationService.cs`, `RecordCreationRefusal.cs`, `RecordCreationProblemMap.cs`;
  `tests/integration/seam/RecordCreation/RecordCreationServicePipelineSeamTests.cs`;
  `tests/unit/domain/Dataverse/RecordCreation/RecordCreationProblemMapTests.cs`;
  `tests/Spaarke.ArchTests/OwnedChildWriteCallerWriteAuthorizationNameGuardTests.cs`.
- Modified: `Infrastructure/DI/OfficeModule.cs` (registration), `Api/ChildRecordEndpoints.cs` (`IsUnresolvedCaller`
  internal), the Office rename set (`Services/Office/RecordCreationService.cs`, `OfficeService.cs`, `OfficeEndpoints.cs`,
  `CreateTimeFieldMapping.cs` crefs, and eight test files), `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`
  (inventory additions below), `RecordTableRulesRegistryGuardTests.cs` (remark), `tests/integration/seam/README.md`.

### Census inventory additions (RecordOwnerAssignmentCensusTests)

The census keys files by NAME, so the new `RecordCreationService.cs` is read together with the Office file of the same
name. Added: owner writes `OwnedCreateFields` (Routed) and `IsolatedCreateFields` (Root); an `UnscannedWriter` for the
service (its table is a parameter, so the scanner sees no create site); `PersonBearingWriter` `DecideOwnerAsync`. No
existing entry changed.

## K2 (UAC-r2 011 review)

New ArchTest: `OwnedChildWrite` and `CallerWriteAuthorization` declare no common static member or nested-type name
(compiler-generated and special-name members excluded). Must-fire: two fixtures sharing a method, a constant and a nested
type are all reported. Must-not-fire: two fixtures sharing only compiler-generated names (`<>c`) report nothing. The real
pair shares none today. The new service imports neither class statically; every call is qualified.

## Tests

New tests (all at KEEP paths, all pass):

| Suite | Tests | Result |
|---|---|---|
| `tests/integration/seam/RecordCreation/RecordCreationServicePipelineSeamTests.cs` | 29 (18 + 11 in fix round 1) | 29 pass |
| `tests/integration/seam/RecordCreation/RecordCreationServiceSecureRootSeamTests.cs` (fix round 1) | 4 | 4 pass |
| `tests/unit/domain/Dataverse/RecordCreation/RecordCreationProblemMapTests.cs` | 33 | 33 pass |
| `tests/Spaarke.ArchTests/OwnedChildWriteCallerWriteAuthorizationNameGuardTests.cs` (K2) | 3 | 3 pass |

The secure-root seam class runs over the REAL `SecureRootFilingGate` and `SecureRootInheritance`
(`ProvisionProjectTestFixture`, the fixture `SecureRootInheritanceWriterTests` uses), not
`SecureRootFilingGateFixtures.Over(...)`: `Over` builds the gate without the No Access guard and the provisioning
dependencies, so an isolated create cannot be completed through it. The pipeline seam class keeps its fakes and adds a
secure matter to its ownership, stamp and share worlds.

Fail-before proofs (mutation: each change applied to the production code, the named tests run, the code restored;
script and log in the session scratchpad). 13 of 14 killed:

| Mutant | Change | Killed by |
|---|---|---|
| M1 | an unresolved WhoAmI is not refused | the 403-before-the-payload test |
| M2 / M2b | the common / the table's server-owned column check dropped | the blocked-column tests |
| M3 | a check fault propagates instead of refusing | the 503 `check_failed` test |
| M4 | an owner fault propagates | the 503 `owner_unavailable` test |
| M5 | the 404 is not uniform | the problem-map one-body test |
| M6a | a read-back fault propagates | the read-back test |
| M7 | the pre-write refusal copied, not carried by reference | the `BeSameAs` test |
| M8 | pre-write rules skipped | 4 pre-write tests |
| M9 | two creates | 2 step-order tests (exactly one create) |
| M10 | the system principal records nobody | 2 system tests |
| M11 | a refusal kind with no HTTP mapping | 2 problem-map tests |
| M12 | `CallerWriteAuthorization` declares a name `OwnedChildWrite` has | the K2 guard |
| **M6b (survived)** | a post-create step's fault propagates | none — every post-create callee catches its own faults (`CoreAncestorRestamper` `when (!IsCallerCancellation)`, `RefreshInheritedAccessPermissionAsync`, `SyncChildAsync` returns a status) and runs with `CancellationToken.None`, so no real fault reaches `AfterCreateStepAsync`'s catch. Known limit (K): a defence-in-depth catch; the mirror test proves the Failed-status log path instead. |

Fix round 1 mutants (script `mutations-013-r1.ps1`, same method). 17 of 17 killed:

| Mutant | Change | Killed by |
|---|---|---|
| R1-01 | the plan's AccessDenied answers 403, not the uniform 404 | the secure-matter 404 body test |
| R1-02 | no AppendTo callback is passed to the plan for a caller | the secure-matter 404 body test |
| R1-03 | the isolated create drops `sprk_issecure` | the isolated-create test |
| R1-04 | a Secure owner on an inheriting table is accepted | the owner-finds-secure 409 test |
| R1-05 | a Secure owner on a non-reparentable table is accepted | the `sprk_signal` 409 test |
| R1-06 | a removed isolated row is reported created | the removed-row 500 test |
| R1-07 | the "Assigned *" grants run before the isolated completion | the isolated-create test (creator share 0 at the grants) |
| R1-08 | the number retry draws a new id | the taken-twice test |
| R1-09 | any table retries | the no-autonumber test |
| R1-10 | any create fault retries | the other-fault test |
| R1-11a / R1-11b | one attempt too many / too few | the taken-every-time / taken-twice tests |
| R1-12 | a caller may name the autonumber column | the blocked-number test |
| R1-13 | a row naming its own number is retried | the system-named-number test |
| R1-14 | the system filing refusal is removed | both system filing tests (pipeline and secure-root) |
| R1-15 | the system refusal fires for every system create | the system filed-under-nothing test (must not fire) |
| R1-16 | the table's own server-owned column check is dropped | the work-assignment blocked-column test |

Existing suites, unchanged and passing:

- `tests/unit/Sprk.Bff.Api.Tests` with `~SecureChildOwnershipAiToolTests|~RefileWriterAfterRefileGuardTests|~RouteAuthorizationGuardTests|~OwnedChildWrite|~ChildRecord|~CreateRecord|~Office|~RecordCreation|~RecordTableRules`
  (this includes the Office quick-create contract tests): 1488 passed, 7 skipped (the existing `Skip` facts in
  `OfficeEndpointsContractTests`), 0 failed.
- The rename's other test classes (`~RecordCreatorPersonStampTests|~AssignedAccessWriterTriggerTests|~SecureRootInheritanceWriterTests|~FieldMapping`): 401 passed.
- `DiGraphValidationTests` (the BFF DI graph with ValidateOnBuild + ValidateScopes, so the new registration resolves): 1 passed.
- `tests/Spaarke.ArchTests` (full): 1214 passed, 0 failed (1211 before + the 3 K2 tests; the census passes 38/38 with the
  additions below, `RecordOwnerAssignmentCensusTests` otherwise unchanged).

## Build, publish, CVE, gates

- Build: `dotnet build Spaarke.sln`: 0 warnings, 0 errors. `tests/Spaarke.ArchTests` (not in the solution) builds clean.
- Publish size (`dotnet publish -c Release`, zipped Optimal with PDBs, from two detached short-path worktrees removed
  afterwards with `scripts/worktree/Remove-WorktreeSafe.ps1`):

  | Build | Commit | Files (PDBs) | Zip bytes |
  |---|---|---|---|
  | `origin/master` | `44063b3473` | 192 (4) | 39,314,378 |
  | `task/fm-013` | `c09c73c1f7` | 192 (4) | 39,353,786 |
  | **Delta** | | 0 | **+39,408 (+38.5 KB)** |

  `origin/master` is about 80 commits ahead of this branch's base, so the delta also nets out master's own changes; it
  is far under the +5 MB per-task threshold either way. No package added.
- CVE: `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages.
- Commits use the repo's lint-staged pre-commit hook (`dotnet format` on staged files); it backs up and restores its own
  state through a stash entry of its own. No `git stash` was run by hand.

## Step 9.5 — code-review and adr-check

Code review (F/K triage). No F-class finding. Known limits and notes for 017/018:

| # | Finding | Class | Disposition |
|---|---|---|---|
| CR-1 | `AfterCreateStepAsync`'s catch is unreachable by today's callees (M6b) | K2 | Kept as defence in depth; documented above |
| CR-2 | An owner refusal whose reason ends in "." renders "…Try again.." | K4 | Same text as `ProblemDetailsHelper.RecordOwnerRefused` today (parity) |
| CR-3 | The route's bodies carry `traceId`; the service's map carries `correlationId` (FR-12) | K3 | 017's route adapter decides whether to keep `traceId` too |
| CR-4 | The chat create today passes `grantorOid: null` to the "Assigned *" step and runs no I-1/I-16/mirror after an owned create; through the service it gets all three and the caller as grantor | K3 | 017 decides (pass `ObjectId: null` to keep the grantor as it is) |
| CR-5 | `ServerSetLookupColumns` skips AppendTo for the columns it names | K3 | Internal contract: adapters pass only columns the SERVER set (the email draft's sender), as `OwnedChildWrite.CreateAsync`'s `serverSet` today |
| CR-6 | `IMembershipEventPublisher` lives under `Services/Ai/Membership` (ADR-013 facade question) | K4 | Not an AI model dependency; the same publisher three CRUD sites call today |
| CR-7 | Constructor null guards on non-nullable parameters; the create fault logs and rethrows | K4 | The Office core's idiom; the rethrow adds the step record and correlation id |

Metrics: `RecordCreationService.cs` is about 1,000 lines with 9 constructor parameters. Cohesive: one pipeline and its own
contract types; each parameter is one step's collaborator.

ADR check: no violation. Compliant — ADR-001 (no endpoint), ADR-002 (no plugin; one owner per invariant: the
server-owned columns come from the registry the route also reads; fail closed), ADR-007 (no Graph), ADR-008 (path A,
above), ADR-010 (one concrete scoped registration in the module the POML names), ADR-019 (`errorCode`, `correlationId`,
429/503/504 mapping, no upstream text), ADR-028 (checks under the caller's own token; no credential change), ADR-029
(size measured), ADR-032 (every dependency unconditional; the publisher has its Null peer), ADR-034 (fire-and-forget
publish through the existing publisher), ADR-038 (KEEP paths), ADR-044 (GUIDs formatted by `Guid`, bare lowercase),
ADR-052 (no background work). Warning only: ADR-013, CR-6.

## Adversarial review

Two passes (security work). Pass 1 (opus) over `bad3195920..c09c73c1f7`; fix round 1 closed every F-class finding.

| # | Pass-1 finding | Class | Disposition (fix round 1) |
|---|---|---|---|
| A1-1 | No test drove the secure-root port (plan refusal map, the AppendTo callback, the isolated fields, the Secure-owner arms) | F | Fixed: 4 secure-root seam tests + 3 pipeline seam tests; mutants R1-01 to R1-07 killed |
| A1-2 | No test drove the NFR-03 number retry | F | Fixed: 4 retry tests; mutants R1-08 to R1-11b killed |
| A1-3 | A caller could set the autonumber column (latent: no path passes one today) | F | Fixed: step 2 BlockedColumn; retry only for the platform's own number (decision 11); R1-12, R1-13 killed |
| A1-4 | A system create naming a person isolated a row and shared a secure root to that person, unchecked | F (security) | Fixed: decision 8, refused before the plan; R1-14, R1-15 killed |
| A1-5 | Decision 8 was false and its branch unreachable | F | Fixed with A1-4; the dead guard removed; decision 8 rewritten |
| A1-6 | The census entries for the new file cannot fail (K1 below) | K | Known limit, below |
| A1-7 | Two post-create calls sit outside `AfterCreateStepAsync` (K2 below) | K | Known limit, below |

Known limits:

- **K1 — census name join (expiry: task 018).** `RecordOwnerAssignmentCensusTests.ServerFiles()` keys files by name, so
  `Services/RecordCreation/RecordCreationService.cs` and `Services/Office/RecordCreationService.cs` are read as one, and
  the census entries this task added for the new service cannot fail on their own. The census is UAC-owned and is not
  edited here. Task 018 deletes the Office file; its POML notes now say to re-verify the join then and key entries on the
  relative path.
- **K2 — never-throw callees outside `AfterCreateStepAsync`.** `SecureRootFilingGate.CompleteIsolatedCreateAsync` and
  `AssignedAccessMaterializer.RunAfterWriteAsync` are called directly in step 12, not through the step wrapper. Both are
  documented "Never throws" (the gate catches and answers a failed result; the materializer leaves a fault to its job),
  and the child-record route calls them the same way today, so the exposure is the route's too. If either promise ever
  breaks, the create has landed and the caller sees a 500 for a row that exists.

rework: fix-round-1 task 013
