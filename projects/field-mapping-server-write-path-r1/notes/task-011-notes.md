# Task 011 notes: extract OwnedChildWrite caller checks into CallerWriteAuthorization (FR-08 move part)

## Rigor declaration

- RIGOR LEVEL: FULL (POML `<rigor>`; tags security, authorization, testing; modifies .cs in Sprk.Bff.Api; touches the
  security suites' files).
- MODEL TIER: opus @ effort xhigh. STEP MODE: prescriptive.
- Branch `task/fm-011`, cut from `c4a2a7b81` (project branch head; the isolated worktree first came up on
  `origin/master` 2b07480ab and was re-cut per the main session's note). The byte-for-byte "before" is
  `OwnedChildWrite.cs` at `c4a2a7b81`.
- Gate: task 010 sign-off and the 2026-10-20 owner decisions in `notes/decisions.md` ("011 UNBLOCKED"). UAC-r2
  conditions (a), (b) and (d) bind this task; (c) is task 014.
- Context loaded: POML, spec FR-08 and ADR Tensions (ADR-008 path A), decisions.md, w2-coordination.md, ADR-002/008/010/
  013/024/028/038, bff-hygiene rule, DATAVERSE-WRITE-PATH-ARCHITECTURE.md, the source of every caller.

## What moved

New `src/server/api/Sprk.Bff.Api/Services/Dataverse/CallerWriteAuthorization.cs`, `internal static`, no DI, no
interface. 20 members, in their original order, with their doc comments (original line ranges in `OwnedChildWrite.cs`
at `c4a2a7b81`):

| Member | Lines | Member | Lines |
|---|---|---|---|
| `CheckCallerMayCreateAsync` | 431-522 | `CallerMayAppendToOrganizationOwnedAsync` | 1250-1271 |
| `LogicalName` (regex) | 532-533 | `BusinessUnitColumnOf` | 1273-1282 |
| `RepeatsTypedFilingLookup` | 587-603 | `CallerMayAppendToBusinessOwnedAsync` | 1284-1346 |
| `PairTarget` (record struct) | 857-858 | `IsBusinessUnitUnderAsync` | 1348-1368 |
| `PairTargetAsync` | 899-998 | `PrivilegeGrantsIn` | 1370-1413 |
| `EntitySetOfAsync` | 1000-1014 | `GuidIn` | 1415-1423 |
| `CheckCallerMayAppendToRecordAsync` | 1016-1030 | `RightsOnAsync` | 1425-1444 |
| `CheckCallerMayFileRootUnderAsync` | 1128-1153 | `WhoAmIAsync` | 1630-1646 |
| `CheckCallerMayAppendToAsync` | 1185-1217 | `PrivilegeNamed` | 1656-1684 |
| `CallerMayAppendToByPrivilegeAsync` | 1219-1248 | `SecuredColumnIn` | 1686-1706 |

`LogicalName`, `RepeatsTypedFilingLookup`, `EntitySetOfAsync`, `GuidIn`, `PrivilegeNamed`, `SecuredColumnIn` and
`BusinessUnitColumnOf` are private helpers used only by the moved checks; `PrivilegeGrantsIn` is the rights evaluation's
reader. They moved with their only callers.

**Stays in `OwnedChildWrite`** (1707 -> 1100 lines): `Outcome` and `RefileOutcome` (also used by `CreateAsync`,
`RefileAsync` and external callers, so POML step 2 "else leave and reference" applies), `ServerOwnedColumns`,
`ForPersonColumns` (task 012), `CreateAsync`, `CreateIntoIsolationAsync`, `RefileAsync`, `CallerWriteFailedException`,
the pair readers (`PairOf`, `PairWrite`, `BffManagedPairColumnsOf`, `PairAgreementColumns`, `IsPairType`),
`PatchWithBffManagedPairAsync`, `KeepPairInStepAsync`, `Merge`, `BodyFields`, `WritesOf`, `ParentsOf`, `PathFor`.
`ReparentAsync` and `AfterRefileAsync` are not members of this file and are untouched.

**Deliberately not moved:** `CheckCallerMayRefileByPairAsync` and `CallerMayFileRootUnderSecure`. They are caller checks,
but they are the re-file half's (UAC condition (b): the re-file path stays in `OwnedChildWrite` untouched) and the POML's
prescriptive list does not name them. Consequence (K2, below): `CallerWriteAuthorization.CheckCallerMayFileRootUnderAsync`
calls `OwnedChildWrite.CheckCallerMayRefileByPairAsync`, which calls back into `PairTargetAsync`, `WhoAmIAsync` and
`CheckCallerMayAppendToAsync`. Task 013 should decide where they live.

### How the bodies stay byte-identical

- The two files import each other: `OwnedChildWrite.cs` gained `using static Sprk.Bff.Api.Services.Dataverse.CallerWriteAuthorization;`
  and the new file has `using static Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite;`. Every unqualified name in a
  moved or remaining body resolves to the same member as before.
- Three `OwnedChildWrite` members read by the moved code went `private` -> `internal` (assembly + InternalsVisibleTo
  test assemblies only): `RecordTypeEntitySet`, `RecordTypeLogicalNameColumn`, `IsPairType`.
- Name-resolution check: no type in `Sprk.Bff.Api.Services.Dataverse` / `.Services` / `Sprk.Bff.Api` / `Sprk` / global is
  named like any member referenced across the two classes; `AssignedToDefaults.Outcome` and
  `PersonalToDoAssignee.Outcome` are nested types and never in scope. The adversarial review confirmed this at IL level
  (below).

### External callers: qualifier only, no forwarding shim

27 tokens `OwnedChildWrite.X` -> `CallerWriteAuthorization.X`, X always a moved member:

| File | Lines |
|---|---|
| `Api/ChildRecordEndpoints.cs` | 229 (`WhoAmIAsync`), 387 (cref) |
| `Services/Ai/Handlers/DataverseCreateRecordHandler.cs` | 502, 663 (`WhoAmIAsync`), 652 (cref), 677 (`PairTargetAsync`), 681, 694 (`CheckCallerMayAppendToAsync`) |
| `Services/Ai/Handlers/DataverseUpdateRecordHandler.cs` | 247 (`CheckCallerMayFileRootUnderAsync`) |
| `Services/Ai/Handlers/EmailDraftToolHandler.cs` (BOM kept) | 366, 420 (`WhoAmIAsync`), 373 (`CheckCallerMayAppendToRecordAsync`) |
| `Services/Ai/Handlers/GridOverviewHandler.cs` | 118 |
| `Services/Signals/Actions/BudgetInquiryExecutor.cs` | 149, 153, 192 (cref) |
| `Services/Signals/Actions/DecisionActionExecutors.cs` | 1162 (`<c>` in a comment) |

`OwnedChildWrite.Outcome` references (e.g. `DataverseCreateRecordHandler.cs:682`) correctly stay. `git grep` finds no
`OwnedChildWrite.<moved member>` left in `src/` or `tests/`; only other projects' historical notes mention them (not
edited). `docs/` has no reference to a moved member.

### Test files touched (qualifier only; no expectation changed)

ADR-038 / UAC (a) "no edited assertions": four test files changed, each only by the class qualifier of a moved member.

- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.BusinessOwnedAppendTo.cs`:
  - line 85, inside the `AppendToAsTheCaller` helper (a call);
  - lines 403, 406, 407: `CallerWriteAuthorization.PrivilegeGrantsIn(...)` inside assertion statements. Only the
    qualifier changed, because the method under test moved. The expected values (`.Should().Equal(...)`,
    `.Should().BeEmpty()`) are unchanged.
- `SecureChildOwnershipAiToolTests.DecisionCommit.cs:22` and `SecureChildOwnershipAiToolTests.WorkAssignmentCreate.cs:171, 187`:
  comments only.
- `tests/integration/Sprk.Bff.Api.IntegrationTests/ChildRecords/BusinessOwnedAppendToLiveTests.cs:183, 218, 235`: calls
  (live Dataverse test; compiles in Spaarke.sln, not run).

No test was added. Every moved member keeps its existing direct coverage, because the same tests call it under its
new qualifier.

### ArchTest change: ADR013 D-66 guard extended (scope kept, not grown)

`tests/Spaarke.ArchTests/ADR013_AiBoundaryTests.cs`: `CallerWriteAuthorization` is added to `DataverseWriteCoreTypes` and
to the NetArchTest `HaveNameStartingWith` selector. Without this, the moved code would have dropped out of the
"write core must not depend on Services/Ai" guard (a silent scope shrink). The DisplayName is unchanged, so the test
identity stays stable.

- Must-fire: with `internal static Sprk.Bff.Api.Services.Ai.IToolHandler? MustFireProbe;` added to
  `CallerWriteAuthorization`, the extended guard FAILED, naming `CallerWriteAuthorization`. The pre-change guard PASSED
  with the same probe, which is the gap. The probe was removed.
- Must-not-fire: the extended guard passes on the final tree (2/2 in the ADR013 filter, 1152/1152 full ArchTests).

## Byte-for-byte proof

1. **Member-by-member comparison** (`prove-move.ps1`, a layout-based splitter independent of the generator's line
   numbers; it compares every class member of `OwnedChildWrite.cs@c4a2a7b81` with the member of the same declaration
   now in `OwnedChildWrite.cs` or `CallerWriteAuthorization.cs`, doc comments included). Re-run on the final HEAD:
   - 55 members before; now 35 in `OwnedChildWrite` + 20 in `CallerWriteAuthorization`.
   - **52 identical, 3 accessibility-only** (`RecordTypeEntitySet`, `RecordTypeLogicalNameColumn`, `IsPairType`:
     private -> internal), **0 DIFFERENT**.
   - No member lost, duplicated or added.
2. **`git diff --color-moved=plain c4a2a7b81 HEAD`** over the two files: 608 removed lines and 612 added lines are
   coloured as moved. Every line *not* coloured as moved is one of:
   - the new file's usings, namespace, class doc and braces;
   - three section-header comments between members;
   - the added `using static` in `OwnedChildWrite.cs`;
   - the three `private` -> `internal` lines.

   No log template, refusal code, status mapping or check order is in a non-moved line.
3. **`git diff --stat -M c4a2a7b81 a88ed6743`**: 14 files, 689 insertions, 640 deletions.
   - `CallerWriteAuthorization.cs` +652; `OwnedChildWrite.cs` 615 (-612/+3 lines changed).
   - The 12 caller and test files: 2-12 lines each, all qualifier lines.
4. **IL comparison (adversarial review, independent).** The BFF was built at both commits outside the worktree and every
   type, field and method body was dumped with Cecil, with `CallerWriteAuthorization` renamed to `OwnedChildWrite` and
   compiler-generated numbering normalised. Of 184,728 vs 184,734 blocks, the only differences are:
   - the three accessibility changes;
   - the new type's header and its lambda-cache class;
   - the static-constructor split: the base `.cctor` equals the new `OwnedChildWrite.cctor` plus the new
     `CallerWriteAuthorization.cctor` (the `LogicalName` and `BusinessUnitColumnOf` initialisers), segment for segment.
     Neither reads the other's static fields, so the initialisation order cannot change.

   `RefileAsync`, the owner resolution, the Secure owner bind, the I-1 stamp, the isolation read, the share mirror,
   refusal codes and messages all have identical IL.

## Placement Justification (CLAUDE.md §10, bff-extensions)

A move within the BFF: `Services/Dataverse/` already holds the write core, and the new file sits beside it. There is no
new endpoint, DI registration, package, background work or Dataverse column. The ADR-008 **path A** exception is in the
spec's "ADR Tensions": route-level authorization stays in endpoint filters, and these payload-dependent checks are
called by the write cores and non-HTTP callers. **Cite path A in the PR description.**

## Component justification (CLAUDE.md §11)

1. **Existing:** `CheckCallerMayCreateAsync`, `PairTargetAsync`, `WhoAmIAsync` and the other moved members were defined
   only in `OwnedChildWrite.cs` (git grep). They had no other copy.
2. **Extension:** this *is* the extension: the members are moved, not copied (spec §11 row `CallerWriteAuthorization`:
   "Yes, moved unchanged"). A separate file is the only way task 013's service can share them without a circular
   reference once the create half leaves `OwnedChildWrite`.
3. **Cost of doing nothing:** task 013's unified `RecordCreationService` would either reference `OwnedChildWrite`
   (circular after the create half moves) or copy the checks. Copying would leave two diverging authorization cores on
   the security boundary.

## Tests: before (c4a2a7b81, `C:\tmp\f11b`) vs after (task/fm-011, a88ed6743 bins)

| Suite | Before | After |
|---|---|---|
| `tests/Spaarke.ArchTests` (full) | 1152 / 1152 passed | 1152 / 1152 passed |
| ArchTests `~RecordOwnerAssignmentCensusTests` | 38 / 38 | 38 / 38 |
| ArchTests `~RefileWriterAfterRefileGuardTests` | 2 / 2 | 2 / 2 |
| ArchTests `~RouteAuthorizationGuardTests` | 88 / 88 | 88 / 88 |
| ArchTests `~ADR013_AiBoundaryTests` | 2 / 2 | 2 / 2 |
| Unit `~SecureChildOwnershipAiToolTests` | 310 / 310 | 310 / 310 |
| Unit `~OwnedChildWrite\|~ChildRecord\|~CreateRecord` | 410 / 410 | 410 / 410 |
| Unit full (`Sprk.Bff.Api.Tests`, 25437 tests, 22 skipped), first run | 25215 passed, 200 failed | 25108 passed, 307 failed |
| Same, after re-running the 162 classes that failed in either run (3068 tests) | **25415 / 25415 passed** | **25415 / 25415 passed** |

**Full unit suite.** The two full runs were made at the same time on a machine that was also running two other agents'
suites (8 test hosts). Every failure was a load timeout in `WebApplicationFactory` HTTP tests: `TimeoutException`, or
`TaskCanceledException` / "Error while copying content to a stream". Most of these are in Office, Insights, Memory and
Documents contract tests.

- The failure sets were not stable between the two runs: 93 tests failed in both, 107 only in the baseline run and 214
  only in the after run.
- The 162 classes holding a failure in either run (3068 tests) were then re-run, without a rebuild, first in the after
  tree and then in the baseline tree. Both re-runs gave **3068 / 3068 passed**.
- Every one of the 307 after-run failures and every one of the 200 baseline failures is among those passes.
- So both trees pass the same 25415 executed tests and skip the same 22.
- None of the security or write-path filters above failed in either run.

**Census and guards:** `RecordOwnerAssignmentCensusTests` and `RefileWriterAfterRefileGuardTests` are unchanged files
with unchanged baselines. The census allow-lists are not touched; no rename is needed, because the census keys on
`CreateAsync`, `CreateIntoIsolationAsync` and `RefileAsync`, which all stayed. Their must-fire controls are in the
unchanged files and pass. The adversarial review listed every guard keyed on `OwnedChildWrite.cs` or the type (census
`:321`, `:523`, `:546`, `:1721`, `:2297`; refile guard `:58`; `RouteAuthorizationGuardTests.Ledger.cs:267,2047,2052`;
ADR013 D-66). No moved code fell out of any guard's scope, apart from D-66, which is now extended.

## Build, publish size, CVE

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors (at a88ed6743 and again at the final HEAD).
- `dotnet build Spaarke.sln` at a88ed6743: 0 warnings, 0 errors. This includes the IntegrationTests project with
  `BusinessOwnedAppendToLiveTests`.
- Publish size (bff-hygiene item 4), same method as task 001:
  - Method: `dotnet publish -c Release` of `Sprk.Bff.Api` from a fresh detached worktree `C:\tmp\f11a` at a88ed6743,
    zipped with `Compress-Archive -CompressionLevel Optimal`, PDBs included.
  - Branch: **39,281,998 B, 192 files**. Master (origin/master 2b07480ab, measured by task 001): 39,281,358 B, 192 files.
  - **Delta +640 B, file count unchanged.** This includes task 001's +314 B already on the project branch.
  - Far below the +5 MB / 55 MB / 60 MB thresholds.
- CVE: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` (at a88ed6743 and again at
  e4d66da20): "no vulnerable packages given the current sources". No package change.

## Quality gates

- **/conflict-check:**
  - origin/master is still 2b07480ab; no master change since the merge-base.
  - None of the 26 open PRs touches any of the 14 files.
  - Coordination flags:
    - ebilling #1911 (`work/ebilling-r1`) edits `OwnedChildWrite.CreateAsync`/`RefileAsync`. Both stay in
      `OwnedChildWrite`, and unqualified calls inside it still compile through the `using static`. The second lander
      reconciles, per decisions.md.
    - Task 003 (parallel) may call `OwnedChildWrite.WhoAmIAsync` or `RightsOnAsync` in new files. After 011 merges, those
      must be `CallerWriteAuthorization.*`, or that branch will not compile.
    - Many local `worktree-agent-*` and UAC branches differ from master in `OwnedChildWrite.cs`. None has an open PR.
      Any that is still live must re-point moved members after 011.
- **code-review (scoped):**
  - Metrics: `OwnedChildWrite.cs` 1707 -> 1100 lines, internal 34 -> 28, private 21 -> 7, branch keywords 138 -> 86
    (Improved). `CallerWriteAuthorization.cs` is a new file: 654 lines, 52 branch keywords; cohesive, its one
    responsibility being "may the caller do this, asked as the caller".
  - AI smells: none (no interface, no DI, no log-rethrow, no new null checks).
  - Security: the caller token path is unchanged (ADR-028); every check still fails closed.
  - No F-class findings.
- **adr-check:**
  - Compliant: ADR-001, ADR-002 (no plugin; one server-side owner), ADR-008 (path A documented), ADR-010 (static, no DI,
    no interface), ADR-013 (D-66 extended to the new type), ADR-024 (pair resolution unchanged), ADR-028 (caller token,
    no MI move), ADR-038 (no edited expectation; no banned test patterns), ADR-052 (no background work).
  - bff-extensions: Placement Justification above; publish delta and CVE recorded; no new endpoint.
  - No violations.
- **Adversarial review** (adversarial-reviewer, opus, one full pass over `c4a2a7b81..a88ed6743`). Verdict: the move is
  behaviour-preserving, with IL proof as above. Findings and triage:
  - **F4 (records missing):** the counts, publish delta, CVE and path A citation were not yet recorded. Fixed: this file.
    The PR description must cite path A (main session). TASK-INDEX is not updated by this agent (see Deviations).
  - **K2 (mutual dependency; two re-file caller checks stayed in `OwnedChildWrite`):** a known limit under UAC
    condition (b) and the POML list. Latent hazard: with the mutual `using static`, a member declared in a class wins
    over an imported one, so a future same-named member would rebind calls silently. Fixed in part: the class doc now
    states that rule. Task 013 should either move the two checks or write the cross-class calls fully qualified.
  - **K4 (doc wording):**
    - `CallerWriteAuthorization`'s remarks called `CheckCallerMayRefileByPairAsync` a "payload reader". Fixed in
      commit e4d66da20 (comment only; the proof re-run is still 52/3/0).
    - "see the class remarks" in `CheckCallerMayCreateAsync`'s moved doc is explained by the new class doc. It is not
      edited, because condition (a) allows no comment change inside moved bodies.
    - `OwnedChildWrite`'s own class remarks still describe the four caller questions. They are left unchanged, and
      they remain true of the create path.
    - The D-66 DisplayName still names only `OwnedChildWrite` and `DataverseWriteItemMapper`. It is kept unchanged so
      the test identity stays stable.
  - **Found in passing (pre-existing, K4):** `Sprk.Bff.Api` does not set `GenerateDocumentationFile`, so `<see cref>`
    targets are never compiler-checked. Not filed; it is a repo-wide documentation-tooling choice, not a defect on a
    path.
  - A second pass was not needed, because the only fix was comment-only and did not change the approach.

## Deviations

- **TASK-INDEX.md, current-task.md and the POML `<status>` were not updated** (POML step 6). The main session's brief
  reserves those files for itself.
- **Pre-commit hook:** lint-staged's backup uses `git stash` (create + drop of its own entry; `git stash list` is
  unchanged afterwards). This is pre-existing and tracked as #1948 (DI-91). No stash was run by hand.
- **UAC condition (d):** UAC adversarially reviews the 011 diff before merge. That is for the main session or the PR.

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Dataverse/CallerWriteAuthorization.cs` (new)
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs`
- `src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseUpdateRecordHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/EmailDraftToolHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GridOverviewHandler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Signals/Actions/BudgetInquiryExecutor.cs`
- `src/server/api/Sprk.Bff.Api/Services/Signals/Actions/DecisionActionExecutors.cs`
- `tests/Spaarke.ArchTests/ADR013_AiBoundaryTests.cs`
- `tests/integration/Sprk.Bff.Api.IntegrationTests/ChildRecords/BusinessOwnedAppendToLiveTests.cs`
- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.BusinessOwnedAppendTo.cs`
- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.DecisionCommit.cs`
- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.WorkAssignmentCreate.cs`
- `projects/field-mapping-server-write-path-r1/notes/task-011-notes.md` (this file)

rework: fix-round-1 task 011
