# Task 015 notes: record-first owner for every create; the owning business unit's search-index default (FR-09, FR-10)

## Rigor declaration

FULL. The task changes who owns created rows (security-relevant: owner team decides business-unit visibility) in
the unified `RecordCreationService` and in the Office quick-create, which has live callers. Two adversarial
review passes were required (see "Adversarial review" — not run, escalated).

Commits on `task/fm-015` (cut from `58d1a3548d`):

| Commit | Subject |
|---|---|
| `e99922491b` | feat: record-first owner for every create and the owning BU's search-index default |
| `5935b5d371` | fix: no create path asks ResolveOwningTeamAsync |
| (this notes commit) | docs: task 015 notes |

## Conflict check (/conflict-check)

- 32 open PRs at start. #2161 and #2150 touch `RecordOwnershipResolver.cs` (re-file / reparent hunks). This task
  does not edit that file.
- #2177 (`work/spaarkeai-word-add-in-r1`, task 130) touches `UploadFinalizationWorker.cs`: its hunks are at
  :14, :299, :325, :617-:733. This task changes :137 only, so no textual overlap.
- None of the open PRs touch `RecordCreationService.cs`, `OfficeRecordCreationService.cs` or
  `SearchIndexNameResolver.cs`. `FieldMappingEndpoints.cs` (UAC-r2 #2169) was not touched.

## Placement Justification (CLAUDE.md §10)

No new endpoint, service, DI registration, package or background work. All changes sit in existing classes on
the create path. Publish-size delta is below.

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationService.PairParentAsync` (internal static) | `OwnedChildWrite.PairOf` reads the ADR-024 pair; `SecureRootInheritance` reads the pair for the plan | Extends `PairOf` with the one read `ForParents` needs (`sprk_recordtype_ref` → table name). `OwnedChildWrite.cs` is off-limits for this task. Static, so the Office service can share it without a second copy | A project filed only by its pair would be owned by the acting user's team, not by the Matter's business-unit team. That breaks FR-09 and the four-surface parity test |
| `SearchIndexDefaultAsync` + private `SearchIndexFill` record | `SearchIndexNameResolver` reads the BU columns at query time; `DocumentRecordService` / the wizards set them on the client | Reuses the resolver's column and entity names (made `internal const`). The resolver answers "which index to query", not "what a new row stores", so it is not extended | Step 8 stays a no-op. A matter, project or document created through the unified path gets no index default (FR-10) |
| `SearchIndexNavigationProperty` / `SearchIndexEntitySet` consts | Matter, document and email clients hard-code `sprk_AI_Search_Index` / `sprk_aisearchindexes` | No server constant existed | The bind key would be a magic string in two places |
| `Decision.DefaultWarnings` | `MappingWarnings`, the step warnings | Same shape, one more list | A failed BU read would be silent (the brief requires a warning) |
| `CreateOwnerResolutionGuardTests` (ArchTests) | `RecordOwnerAssignmentCensusTests` (logic off-limits; it accepts either resolver call) | The census cannot be changed in this task | Nothing stops a new create path from calling `ResolveOwningTeamAsync` again (criterion 1) |

## How it works

- **Step 7 (owner), unified service.** `DecideOwnerAsync` collects ownership parents from the merged payload as
  before, and also the parent that the ADR-024 pair names (`PairParentAsync`, only on tables where the pair names
  a parent on its own — project today). It then calls `ResolveOwnerAsync(ForParents(parents, …) with { RequestedBy })`.
  If the pair parent cannot be determined (bad id, no type, a type with no table), the create is refused with
  `OwnerUnresolved` / `record_owner_parent_undetermined`. A fault still returns 503 `owner_unavailable`. The
  ownership-exception check (registry) still runs first, unchanged.
- **Office quick-create.** `ResolveOwnerTeamAsync` replaces the call to `ResolveOwningTeamAsync` for the caller.
  Parents are the row's ownership-parent `EntityReference`s (not citations) plus the pair parent. It calls
  `ResolveOwnerAsync(ForParents(...) with { RequestedBy })`. Refusal handling:
  - No parents and not owned: the existing OFFICE_022 refusal, with the same wording.
  - A filed row that is not owned: the resolver's code (403).
  - A secure owner for a filed row: refused with "Create it from the secure record itself". The Office path does
    not isolate a matter.
  - Matter: the owner is resolved at the same place as before.
  - Project: the owner is resolved after the secure-root plan, and only when the project is not isolated. An
    isolated project keeps `IsolateProjectCreate`.
- **Step 8 (search-index default).**
  - Runs only on tables that `RecordTableRules` marks `SearchIndexed` (matter, project, document). Any other table
    gets NoOp: "the table is not search-indexed".
  - The owning team is the isolated team, else the resolved owner team. Its `businessunitid` is read, then that
    BU's `sprk_searchindexname` and `sprk_ai_search_index`.
  - Fill-only: each column is filled only if the body/lookups do not already supply it. A blank name counts as
    not supplied.
  - A failed read is non-fatal: nothing is filled, the step is marked Failed, a warning is logged and returned,
    and the create continues.
- **Office upload worker (legacy fallback).** A payload with no carried team now asks `ResolveOwnerAsync` with the
  same target context and keeps only an owned answer. Behaviour is identical (`ResolveOwningTeamAsync` is that
  wrapper with no requester). No create path in `src/server` calls `ResolveOwningTeamAsync` now, and
  `CreateOwnerResolutionGuardTests` enforces this. The interface member stays, because test doubles and the census
  name it.

## Decisions and deviations

1. **Pair parent read app-only**, like the secure-root plan's read. It is a catalog lookup (`sprk_recordtype_ref`),
   not caller data.
2. **Office project owner after the plan, and only when not isolated.** Before, the owner was resolved first, so a
   caller with no BU team got OFFICE_022 even for a project the plan would isolate. Now plan refusals come before
   owner refusals (see UAC-r2 table).
3. **Office search-index default stays on the caller's BU** (`ApplyBusinessUnitDefaultsAsync` unchanged). The POML
   allowed only the owner call site in the Office class. The unified service uses the owning BU, so the two
   diverge until 018 re-points Office callers. Its remark is now stale. OWNER item.
4. **Unfiled communication keeps the task-013 refusal** (409 `ownership_exception`, zero creates). The criterion
   says "keeps the creator", but the unified create is app-only, so "the creator" would be the app. That is the
   ROOT-BU row I-6 forbids. The registry exception is read and honoured as a refusal. Task 060 assesses the
   exceptions. Deviation, recorded for the reviewer.
5. **Each search-index column is copied from its own BU counterpart.** No precedence between text and lookup was
   invented. If the BU's two values disagree, both are passed through. OWNER item; the escalation trigger did not
   fire.
6. **Step 8 also runs for the system principal.** It needs no metadata, only the owner team.
7. **Worker fallback uses the target-style context, not `ForParents`.** `ForParents` filters with
   `IsOwnershipParent`, which would drop the queued friendly spelling (`"matter"`) and silently change the owner to
   the caller's team. The resolver normalises the target spelling itself.
8. **`PairParentAsync`'s undetermined branch is mostly defensive** in the unified service: the secure-root plan
   refuses a malformed pair first. It is reachable from the Office service, where a non-string pair id is
   undetermined.
9. **Tests at KEEP path `tests/integration/data-mutation/RecordOwnership/**`**, as the POML names them, allowed by
   `.claude/constraints/testing.md:132` (data-mutation is a KEEP category). The guard is in `tests/Spaarke.ArchTests`.

## Tests

New:
- `tests/integration/data-mutation/RecordOwnership/CrossSurfaceOwnerTests.cs`, 8 tests. Uses the real resolver and
  the real secure-root gate over one `CreateWorld` directory.
  - The same Project under the same Matter gets the same owner (ChildTeam) through Office-style, browser-route-style,
    chat-style and decision-follow-on-style inputs.
  - Secure ancestor → named Secure Record Owners team.
  - Ordinary filed-under record → its BU team.
  - Unfiled → acting user's BU team (unified and Office).
  - Teamless BU → `record_owner_no_default_team` on both surfaces, zero creates.
  - Unknown caller → `record_owner_acting_user_unresolved` / OFFICE_022, zero creates.
  - `PairParentAsync` undetermined.
  - Unfiled communication → `ownership_exception` 409, zero creates, the resolver never asked.
- `tests/integration/data-mutation/RecordOwnership/SearchIndexDefaultTests.cs`, 14 cases:
  - Matter unfiled → General BU defaults. Project and document under a matter → that matter's BU defaults.
  - Explicit both kept (×3 tables, no BU read). Explicit name kept and lookup filled (×3). Blank name filled (×3).
  - Event untouched (NoOp, no BU read).
  - BU read fault → created, nothing filled, one warning.
- `tests/Spaarke.ArchTests/CreateOwnerResolutionGuardTests.cs`: 1 rule, 3 must-fire and 5 must-not-fire controls.

Changed (mocks only, no assertion changes):
- 5 Office test hosts now answer `ResolveOwnerAsync`, because Office no longer asks `ResolveOwningTeamAsync`.
- `SecureRootInheritanceWriterTests` answers the catalog read.
- `RecordCreationServicePipelineSeamTests` answers the step-8 team/BU reads.
- `RecordOwnerAssignmentCensusTests` inventory wording for Office `CreateMatterAsync` / `CreateProjectAsync` only.

### Fail-before evidence (mutants; each applied alone, built, run, restored; tree clean after)

| Mutant | Tests that fail |
|---|---|
| M1 pair parent not added to the parents | 3 |
| M2 Office resolves for the caller only (no parents) | 2 |
| M3 step 8 a no-op | 10 |
| M4 fill-only dropped (overwrite supplied values) | 6 |
| M5 caller's BU instead of the owning BU | 5 |
| M6 non-indexed tables filled | 1 |
| M7 BU read failure fatal | 1 |
| Worker reverted to `.ResolveOwningTeamAsync(` | 1 (guard) |

All killed.

Census check: replacing `_ownership.ResolveOwnerAsync(` in `RecordCreationService` fails 2 census tests ("Task 146:
every writer the scanner cannot see still calls the resolver", "Task 146 r2: Routed owner writes sit in files that
call the resolver…"). So the census still fires for the new service. The same mutation in
`OfficeRecordCreationService` fails no census test: Office owner writes are `OwnerWriteKind.Root`, which the census
does not tie to a resolver call. That is a pre-existing census design and the census logic is off-limits here
(known-limit, OWNER item).

### Runs (after commit `5935b5d371`, after lint-staged `dotnet format`)

- `Spaarke.sln` build: 0 warnings, 0 errors.
- `Spaarke.ArchTests`: 1223/1223 passed. This includes the census and `RouteAuthorizationGuardTests`.
  `RefileWriterAfterRefileGuardTests` is in the filtered unit run below.
- `Sprk.Bff.Api.Tests`, filtered: 2314 passed, 7 skipped, 0 failed. Filter: RecordCreation | Office | FieldMapping |
  SecureRoot | SecureChildOwnershipAiToolTests | RefileWriterAfterRefileGuardTests | RouteAuthorizationGuardTests |
  AssignedAccessWriterTrigger | RecordCreatorPerson | CrossSurface | SearchIndexDefault | RecordOwnership |
  SearchIndexNameResolver | UploadFinalization.
- `SecureChildOwnershipAiToolTests*`, `RefileWriterAfterRefileGuardTests`, `RouteAuthorizationGuardTests` and
  `RecordOwnerAssignmentCensusTests` pass unchanged; only census inventory wording was changed.

## Build, publish, CVE

- Publish: Release, `Compress-Archive -CompressionLevel Optimal`, PDBs included.
  - Fresh master `2a38e6ce6c`: 39,354,765 B, 192 files.
  - Branch `e99922491b`: 39,381,999 B, 192 files.
  - Delta: **+27,234 B (~+0.03 MB)**. Total ~39.4 MB, under the 55 MB review line.
  - Commit `5935b5d371` changes 4 lines of the BFF and adds an ArchTest only (not published).
  - The measurement worktrees (`C:\tmp\f15m`, `C:\tmp\f15a`) were removed with
    `scripts/worktree/Remove-WorktreeSafe.ps1`; the publish folders and zips were deleted.
- CVE: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` reports "no vulnerable
  packages". No package was added.
- Live schema check (read-only Dataverse MCP `describe tables/sprk_project`): `sprk_project.sprk_ai_search_index` is
  a lookup to `sprk_aisearchindex` via relationship `sprk_sprk_aisearchindex_AI_Search_Index_sprk_project`. That is
  the same schema-name pattern (`sprk_AI_Search_Index`) as matter and document. The navigation-property casing used
  in the bind key was not read directly. OWNER live-verify item below.

## Adversarial review

**Not run.** Two opus `adversarial-reviewer` passes were required (security work): one over the full change set,
one over the fix diff plus direct callers and callees. Both launches from this implementer agent were refused by the
harness with "Concurrent subagent limit reached … Do not retry", and they were not retried. Escalated to the
coordinator, who must run both passes over `58d1a3548d..task/fm-015`.

Self-review notes for the reviewers to check first:
- Office secure-owner refusal for a filed matter/project that is not isolated: it is a new 403 path on a live route.
  Confirm that the kind/code (`SecureFilingRefused` / `record_owner_no_source`) is the right reuse.
- The `sprk_AI_Search_Index` bind key on `sprk_project`: a wrong casing would 400 every unified project create
  that gets a lookup default. The unified service has no live caller until 017/018.
- The step-8 BU read runs app-only (S2S). It reads only the owner team's BU, never caller data.
- The pair parent is read before the resolver, so a `sprk_recordtype_ref` row naming a non-parent table is filtered
  by `ForParents` (citation or non-ownership table) and does not change the owner.

## For UAC-r2 review

Condition (b): the owner and search-index behaviour before and after, per path and per table.

| Path | Table | Before | After |
|---|---|---|---|
| Office quick-create | matter, unfiled | caller's BU team; no team → OFFICE_022 | unchanged (same wording) |
| Office quick-create | matter, with a mapped ownership-parent lookup | caller's BU team | that parent's BU default Owner team; parent's BU without a team → 403 `record_owner_no_default_team`; secure parent → 403 refused (`SecureFilingRefused` maps to 403 in `OfficeService`) |
| Office quick-create | project, unfiled | caller's BU team; no team → OFFICE_022 | unchanged |
| Office quick-create | project under an ordinary matter (pair or lookup) | caller's BU team | **the matter's BU default Owner team** (release note) |
| Office quick-create | project under a secure matter | owner resolved first: a caller with no BU team got OFFICE_022 before isolation | isolated by the plan as before; no longer needs a caller BU team |
| Office quick-create | project, refusal order | owner refusal before plan refusal | plan refusal (the secure-filing refusals) before owner refusal |
| Office quick-create | matter / project search index | caller's BU defaults | unchanged (caller's BU) — diverges from the unified path until 018 |
| Office upload worker (payload with no team) | document | `ResolveOwningTeamAsync` (target, caller) | `ResolveOwnerAsync`, same context, same answer |
| Unified `RecordCreationService` (no live caller until 017/018) | project with only a pair | acting user's team (as `OwnedChildWrite` does today) | the pair parent's BU default Owner team; secure → named team / isolation as task 013 |
| Unified | matter / project / document | step 8 a no-op | fill-only from the **owning** BU (isolated project → the Secure BU) |
| Unified | any other table | — | no search-index value (NoOp) |
| Unified | unfiled communication | 409 `ownership_exception` (task 013) | unchanged |
| Both | system principal | refused | unchanged |
| Both | secure ancestor | named Secure Record Owners team; the two secure-filing refusals (409 unified, 403 Office) | unchanged |

No new refusal codes. Reused: `record_owner_parent_undetermined`, `record_owner_no_default_team`,
`record_owner_acting_user_unresolved`, `record_owner_no_source`, OFFICE_022 (`OfficeErrorCodes.RecordOwnerUnresolved`,
403); unified `OwnerUnresolved` → 409.

## Release-note items

- An Office Project created under a Matter is now owned by the Matter's business-unit default Owner team (it was
  the creating user's team).
- An Office Matter or Project created with a mapped filing lookup is owned by that record's business-unit team. If
  that record is secure, the create is refused and must be made from the secure record itself.
- An Office Project under a secure Matter no longer needs the creating user to have a business-unit team.

## OWNER items

1. **Live-verify the `sprk_AI_Search_Index` navigation property on `sprk_project`** (and on the other indexed
   tables) before 017/018 point callers at the unified create. A wrong casing returns 400 on create.
2. **Document default precedence.** The unified create takes the document's default from the owning BU. Today
   `DocumentRecordService` (client) takes it from the parent's explicit lookup, then the parent's BU. Is the owning
   BU right, or should a parent's explicit index win?
3. **BU text/lookup disagreement**: both are passed through unchanged. Is that acceptable?
4. **Office search-index divergence** (caller's BU vs the owning BU) until 018.
5. **Census**: Office `Root` owner writes do not assert a resolver call, so a mutation there is not caught by the
   census. The new guard covers only `ResolveOwningTeamAsync`.
6. **Unfiled communication**: the registry exception is honoured as a refusal, not as "keeps the creator" (deviation 4).
7. **Docs**: `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-5/I-6 tables are not updated for the record-first unified
   create or the step-8 default.
8. Noticed, not changed: the `RecordOwnerAssignmentCensusTests` inventory text for `InvoiceReviewService.cs` (:212)
   still says "ResolveOwningTeamAsync"; that file no longer calls it.

## POML acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | Every create resolves via `ResolveOwnerAsync(ForParents(...), RequestedBy)`; no create path calls `ResolveOwningTeamAsync` | Met. The unified service and Office use `ForParents` + `RequestedBy`. The worker fallback uses `ResolveOwnerAsync` with a target context (deviation 7). The guard enforces "no call" |
| 2 | Same Project under the same Matter through four input styles → identical owner | Met (`CrossSurfaceOwnerTests`) |
| 3 | Owner order covered: secure ancestor → named team; filed-under BU team; acting user's BU team | Met |
| 4 | Explicit search index survives; unset takes the owning BU default (matter, project, document) | Met (unified). Office stays on the caller's BU (deviation 3) |
| 5 | Negative: unresolved owner → typed refusal, zero creates | Met (both surfaces) |
| 6 | Negative: registry exception keeps its behaviour; non-indexed table gets no value | Non-indexed: met. Exception: honoured as the task-013 refusal, not "keeps the creator" (deviation 4) |
| 7 | Release-note line in task notes | Met |
| 8 | Build 0 errors; named suites pass unchanged; publish, CVE and conflict-check recorded | Met |

Not done here (coordinator-owned): `TASK-INDEX.md`, `current-task.md`, POML status. The two adversarial review
passes are pending (escalated).

rework: none task 015
