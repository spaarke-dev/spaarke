# Task 015 notes: record-first owner for every create; the owning business unit's search-index default (FR-09, FR-10)

## Rigor declaration

FULL. The task changes who owns created rows (security-relevant: owner team decides business-unit visibility) in the
unified `RecordCreationService`. Two adversarial review passes are required (security). Pass 1 (opus) was run by the
main session; pass 2 is the main session's (see "Adversarial review").

Commits on `task/fm-015` (cut from `58d1a3548d`):

| Commit | Subject |
|---|---|
| `e99922491b` | feat: record-first owner for every create and the owning BU's search-index default |
| `5935b5d371` | fix: no create path asks ResolveOwningTeamAsync |
| `f019b6bef0` | docs: task 015 notes |
| `1a93e23351` | fix: task 015 review round 1 — Office owner back to pre-015 (moves in 018); pair parent only a matter or project; guard widened |
| (this commit) | docs: task 015 fix round 1 — notes; DATAVERSE-WRITE-PATH-ARCHITECTURE I-5 / I-6 |

## Conflict check (/conflict-check)

- 32 open PRs at start. #2161 and #2150 touch `RecordOwnershipResolver.cs` (re-file / reparent hunks). This task does
  not edit that file.
- #2177 (`work/spaarkeai-word-add-in-r1`, task 130) touches `UploadFinalizationWorker.cs`: its hunks are at :14, :299,
  :325, :617-:733. This task changes :137 only, so no textual overlap.
- #2191 (security hotfix) edits `OfficeRecordCreationService.cs` around the source read (~1236-1290). After fix
  round 1 this branch's diff of that file against `58d1a3548d` is EMPTY (restored byte-identical), so no overlap.
- None of the open PRs touch `RecordCreationService.cs` or `SearchIndexNameResolver.cs`. `FieldMappingEndpoints.cs`
  (UAC-r2 #2169) was not touched.

## Placement Justification (CLAUDE.md §10)

No new endpoint, service, DI registration, package or background work. All changes sit in existing classes on the
create path. Publish-size delta is below.

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationService.PairParentAsync` (internal static) | `OwnedChildWrite.PairOf` reads the ADR-024 pair; `SecureRootInheritance` reads the pair for the plan | Extends `PairOf` with the one read `ForParents` needs (`sprk_recordtype_ref` → table name), returning only what the plan reads as a parent (`SecureRootInheritance.IsParent`). `OwnedChildWrite.cs` is off-limits for this task | A project filed only by its pair would be owned by the acting user's team, not by the Matter's business-unit team (FR-09, the four-input parity test) |
| `SearchIndexDefaultAsync` + private `SearchIndexFill` record | `SearchIndexNameResolver` reads the BU columns at query time; `DocumentRecordService` / the wizards set them on the client | Reuses the resolver's column and entity names (made `internal const`). The resolver answers "which index to query", not "what a new row stores", so it is not extended | Step 8 stays a no-op. A matter, project or document created through the unified path gets no index default (FR-10) |
| `SearchIndexNavigationProperty` / `SearchIndexEntitySet` consts | Matter, document and email clients hard-code `sprk_AI_Search_Index` / `sprk_aisearchindexes` | No server constant existed | The bind key would be a magic string in two places |
| `Decision.DefaultWarnings` | `MappingWarnings`, the step warnings | Same shape, one more list | A failed BU read would be silent (the brief requires a warning) |
| `CreateOwnerResolutionGuardTests` (ArchTests) | `RecordOwnerAssignmentCensusTests` (logic off-limits; it accepts either resolver call) | The census cannot be changed in this task | Nothing stops a new create path from using `ResolveOwningTeamAsync` again (criterion 1) |

## How it works

- **Step 7 (owner), unified service.** `DecideOwnerAsync` collects ownership parents from the merged payload as
  before, and also the parent that the ADR-024 pair names (`PairParentAsync`, only on tables where the pair names a
  parent on its own — project today — and only when the pair's record type is a matter or project, exactly as the
  secure-root plan reads it). It then calls `ResolveOwnerAsync(ForParents(parents, …) with { RequestedBy })`. If the
  pair parent cannot be determined (an id that is not text, no type, a type with no table), the create is refused with
  `OwnerUnresolved` / `record_owner_parent_undetermined` — on a pair-managed table only (round 1, K2). A fault still
  returns 503 `owner_unavailable`. The ownership-exception check (registry) still runs first, unchanged.
- **Step 8 (search-index default).**
  - Runs only on tables that `RecordTableRules` marks `SearchIndexed` (matter, project, document). Any other table
    gets NoOp: "the table is not search-indexed".
  - The owning team is the isolated team, else the resolved owner team. Its `businessunitid` is read, then that BU's
    `sprk_searchindexname` and `sprk_ai_search_index`.
  - Fill-only: each column is filled only if the body/lookups do not already supply it. A blank name counts as not
    supplied.
  - A failed read is non-fatal: nothing is filled, the step is marked Failed, a warning is logged and returned, and
    the create continues.
- **Office quick-create: UNCHANGED** (fix round 1, main-session decision). `OfficeRecordCreationService.cs` is
  byte-identical to `58d1a3548d`: the owner is the acting user's team through `ResolveOwningTeamAsync`, in the pre-015
  order; the search-index default is the caller's business unit. Office goes record-first in task 018, through the
  unified create.
- **Office upload worker (legacy fallback).** A payload with no carried team now asks `ResolveOwnerAsync` with the same
  target context and keeps only an owned answer. Behaviour-identical (`ResolveOwningTeamAsync` is that wrapper with no
  requester); the reviewer verified it.
- **Guard.** `CreateOwnerResolutionGuardTests` fails on ANY use of the name `ResolveOwningTeamAsync` under `src/server`
  (call, method group, delegate, a call split across lines, `nameof`), except the member's declaration in
  `RecordOwnershipResolver.cs` and the allow-listed Office file (keyed by repo-relative path, with its reason and expiry:
  "Office root owner moves to ResolveOwnerAsync in task 018 via the unified service; #1614. Expires at 018."). A second
  fact fails when an allow-list entry no longer uses the name, so the list only shrinks. The interface member stays:
  test doubles and the census name it.

## Decisions and deviations

1. **Office owner change deferred to task 018** (fix round 1, main-session decision on review pass 1 findings 1, 2, 4,
   5, 7). The 015 Office change followed mapped filing parents with no AppendTo check (#1614) and answered different
   parent states differently (missing → 403 `record_owner_parent_unresolved`, secure → 403, ordinary → 201: an
   existence / is-secure oracle); it also took the owner from the parent's unit while the search index stayed on the
   caller's, made Office differ from the live child route and chat, and its new 403 and post-plan ordering were
   untested. Task 018 routes Office through the unified create, whose step-5 AppendTo checks gate the filing before the
   owner follows it. **018 item:** remove the Office allow-list entry from `CreateOwnerResolutionGuardTests` (its stale
   check fails when the call goes) and update `TheLiveOfficeQuickCreate_KeepsTheActingUsersTeam_...` (it pins today's
   Office owner on purpose).
2. **Pair parent read app-only**, like the secure-root plan's read. It is a catalog lookup (`sprk_recordtype_ref`),
   not caller data.
3. **The pair parent is a matter or project only** (round 1, F3): the plan reads a project's pair typed to anything
   else as no filing; the owner now agrees, so a pair typed to a document neither takes the document's unit nor trips
   the "could not be decided consistently … Try again" 409.
4. **Unfiled communication keeps the task-013 refusal** (409 `ownership_exception`, zero creates). The criterion says
   "keeps the creator", but the unified create is app-only, so "the creator" would be the app — the ROOT-BU row I-6
   forbids. The registry exception is read and honoured as a refusal. Task 060 assesses the exceptions.
5. **Each search-index column is copied from its own BU counterpart.** No precedence between text and lookup was
   invented; if the BU's two values disagree, both are passed through. OWNER item.
6. **Step 8 also runs for the system principal.** It needs no metadata, only the owner team.
7. **Worker fallback uses the target-style context, not `ForParents`.** `ForParents` filters with `IsOwnershipParent`,
   which would drop the queued friendly spelling (`"matter"`) and silently change the owner to the caller's team.
8. **`PairParentAsync`'s undetermined branch is mostly defensive**: the caller checks and the secure-root plan refuse
   a malformed pair first; the system principal reaches it.
9. **Tests at KEEP paths** `tests/integration/data-mutation/RecordOwnership/**` (as the POML names them) and
   `tests/integration/seam/RecordCreation/**`, allowed by `.claude/constraints/testing.md:132`. The guard is in
   `tests/Spaarke.ArchTests`.

## Tests

New / extended:
- `tests/integration/data-mutation/RecordOwnership/CrossSurfaceOwnerTests.cs`, 12 cases, the real resolver and the
  real secure-root gate over one `CreateWorld` directory:
  - The same Project under the same Matter gets the same owner (ChildTeam) from the unified create given the Office
    quick-create's input (its mapping profile's pair, as 018 will hand it over), the browser route's, the chat
    create's and a decision follow-on's.
  - The LIVE Office quick-create keeps the acting user's team for a project under a matter until 018 (pinned on purpose).
  - Secure ancestor → named Secure Record Owners team. Ordinary filed-under record → its BU team. Unfiled → acting
    user's BU team (unified and Office).
  - Teamless BU → `record_owner_no_default_team`, zero creates. Unknown caller → `record_owner_acting_user_unresolved`
    / OFFICE_022, zero creates.
  - `PairParentAsync` undetermined. A project whose pair names a document (ordinary, and in the Secure BU) is filed
    under nothing: no pair parent, created, owned by the acting user's team, no "Try again" 409 (F3).
  - A malformed pair on an event (pair not a parent) is not `record_owner_parent_undetermined` (K2).
  - Unfiled communication → `ownership_exception` 409, zero creates, the resolver never asked.
- `tests/integration/data-mutation/RecordOwnership/SearchIndexDefaultTests.cs`, 14 cases: matter unfiled → General BU
  defaults; project and document under a matter → that matter's BU defaults; explicit both kept (×3, no BU read);
  explicit name kept and lookup filled (×3); blank name filled (×3); event untouched; BU read fault → created, nothing
  filled, one warning.
- `tests/integration/seam/RecordCreation/RecordCreationServiceSecureRootSeamTests.cs`: +1 — a project under a secure
  matter is created into isolation and takes the Secure BU's index values (F6; it needs the provisioning fixture's
  plan, which `CreateWorld` does not carry). The harness's app-only create now also seeds a project.
- `tests/Spaarke.ArchTests/CreateOwnerResolutionGuardTests.cs`: 15 cases — the rule, the stale-entry check, 6 must-fire
  (call, call with a space, method group, line split, `nameof`, a declaration outside the declaring file), 2
  declaration exclusions, 1 call in the declaring file fires, 4 must-not-fire (another method, a doc comment, a line
  comment, a longer name).

Changed: `RecordCreationServicePipelineSeamTests` answers the step-8 team/BU reads. The 015 changes to the five Office
test hosts, `SecureRootInheritanceWriterTests` and the census inventory wording were REVERTED with the Office service
(their files equal `58d1a3548d`).

### Fail-before evidence (mutants; each applied alone, built, run, restored; tree clean after)

| Mutant | Tests that fail |
|---|---|
| M1 pair parent not added to the parents | 3 |
| M3 step 8 a no-op | 10 |
| M4 fill-only dropped (overwrite supplied values) | 6 |
| M5 caller's BU instead of the owning BU | 5 |
| M6 non-indexed tables filled | 1 |
| M7 BU read failure fatal | 1 |
| R1-F3 any table the pair names is a parent | 2 |
| R1-F6 an isolated create gets no fill | 1 |
| R1-K2 a malformed pair refused on every table | 1 |
| G1 Office allow-list entry dropped | 1 (guard) |
| G2 worker back on `ResolveOwningTeamAsync`, call split across lines | 1 (guard) |
| G3 worker takes a method group | 1 (guard) |
| G4 stale allow-list entry | 1 (guard) |

All killed. (M2, the Office caller-only mutant, is gone with the Office change.)

Census check: replacing `_ownership.ResolveOwnerAsync(` in `RecordCreationService` fails 2 census tests ("Task 146:
every writer the scanner cannot see still calls the resolver", "Task 146 r2: Routed owner writes sit in files that call
the resolver…"), so the census still fires for the new service.

### Runs (fix round 1, after commit `1a93e23351` and its lint-staged `dotnet format`)

- `Spaarke.sln` build: 0 warnings, 0 errors.
- `Spaarke.ArchTests`: 1229/1229 passed (incl. the census, `RouteAuthorizationGuardTests` and the guard).
- `Sprk.Bff.Api.Tests`, filtered: 2359 passed, 7 skipped, 0 failed. Filter: RecordCreation | Office | FieldMapping |
  SecureRoot | SecureChildOwnershipAiToolTests | RefileWriterAfterRefileGuardTests | RouteAuthorizationGuardTests |
  AssignedAccessWriterTrigger | RecordCreatorPerson | CrossSurface | SearchIndex | RecordOwnership | UploadFinalization.
- One interim ArchTests failure (the guard's stale-entry fact naming `Program.cs`) was a binary left built from guard
  mutant G4; a rebuild from the restored source passed, and the final run above is from a clean build.
- `SecureChildOwnershipAiToolTests*`, `RefileWriterAfterRefileGuardTests`, `RouteAuthorizationGuardTests` and
  `RecordOwnerAssignmentCensusTests` pass unchanged (the census file equals `58d1a3548d`).

## Build, publish, CVE

- Publish (measured on `e99922491b`, which included the Office change): Release, `Compress-Archive -CompressionLevel
  Optimal`, PDBs included. Fresh master `2a38e6ce6c`: 39,354,765 B, 192 files; branch: 39,381,999 B, 192 files; delta
  **+27,234 B (~+0.03 MB)**, total ~39.4 MB (under 55 MB). Fix round 1 removes the Office change and adds a few lines,
  so the delta is at most that; not re-measured. The measurement worktrees were removed with
  `scripts/worktree/Remove-WorktreeSafe.ps1`.
- CVE: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` reports "no vulnerable
  packages". No package was added.

## Adversarial review

- **Pass 1 (opus, main session, over `58d1a3548d..f019b6bef0`).** Triage and fixes (fix round 1):
  - F1, F2, F4, F5, F7 (live Office path: owner followed mapped filing parents with no AppendTo check; existence /
    is-secure oracle; search index from the caller's unit vs owner from the parent's; Office differing from the child
    route and chat; new 403 and ordering untested) → fixed by reverting the Office owner change (main-session decision);
    Office moves in 018.
  - F3 (pair typed to a non-parent table) → fixed: `PairParentAsync` returns only `SecureRootInheritance.IsParent`
    tables; test + mutant.
  - F6 (isolated search-index default untested) → test + mutant.
  - F8 (stale Office remarks) → moot: the Office file is restored byte-identical, its remarks are its pre-015 ones.
  - K1 (document index precedence) → OWNER item, to the owner before 017.
  - K2 (malformed pair refused on tables where the pair is not a parent) → fixed: gated on
    `PairNamesAParentOnItsOwn`; test + mutant.
  - K3 (guard missed method groups, line splits, `nameof`; bare-filename allow-list) → fixed: name matched anywhere,
    allow-list by repo-relative path, stale-entry check; controls + mutants.
  - UploadFinalizationWorker change verified behaviour-identical; kept.
- **Pass 2**: the main session's, over the fix diff plus direct callers and callees.

## For UAC-r2 review

Condition (b): the owner and search-index behaviour before and after, per path and per table. After fix round 1 the
live Office paths are unchanged; only the unified service changes, and it has no live caller until 017/018.

| Path | Table | Before | After |
|---|---|---|---|
| Office quick-create | matter / project (any filing) | caller's BU team; no team → OFFICE_022 | **unchanged** (file byte-identical; moves in 018) |
| Office quick-create | matter / project search index | caller's BU defaults | unchanged |
| Office upload worker (payload with no team) | document | `ResolveOwningTeamAsync` (target, caller) | `ResolveOwnerAsync`, same context, same answer |
| Unified `RecordCreationService` (no live caller) | project with only a pair naming a matter or project | acting user's team | the pair parent's BU default Owner team; secure → named team / isolation as task 013 |
| Unified | project whose pair names another table (e.g. a document) | acting user's team | unchanged (filed under nothing, as the plan reads it) |
| Unified | malformed pair on a table whose pair is not a parent | unchanged refusal path | unchanged (no owner refusal added) |
| Unified | matter / project / document | step 8 a no-op | fill-only from the **owning** BU (isolated project → the Secure BU) |
| Unified | any other table | — | no search-index value (NoOp) |
| Unified | unfiled communication | 409 `ownership_exception` (task 013) | unchanged |
| Unified | system principal / secure ancestor | as task 013 | unchanged |

No new refusal codes. Reused: `record_owner_parent_undetermined`, `record_owner_no_default_team`,
`record_owner_acting_user_unresolved`, `record_owner_no_source`; unified `OwnerUnresolved` → 409.

## Release-note items

- None ship with task 015 (no live behaviour changes; the worker change is behaviour-identical).
- **Ships with task 018:** an Office Project created under a Matter will be owned by the Matter's business-unit default
  Owner team (today: the creating user's team), once Office goes through the unified create.

## OWNER items

1. **Closed:** the navigation property is `sprk_AI_Search_Index` — the solution XML confirms it
   (`sprk_AISearchIndex.xml:231`); the live relationship `sprk_sprk_aisearchindex_AI_Search_Index_sprk_project` agrees.
   A live create check is still advised before 017/018 point callers at the unified create.
2. **K1, document index precedence — to the owner before 017.** The unified create takes a document's default from
   the owning BU. Today `DocumentRecordService` (client) takes it from the parent's explicit lookup, then the parent's
   BU. Is the owning BU right, or should a parent's explicit index win?
3. **BU text/lookup disagreement**: both are passed through unchanged. Is that acceptable?
4. **Office**: owner and search index stay on the caller's unit until 018 (deviation 1).
5. **Census gap**: Office owner writes are `OwnerWriteKind.Root`, which the census does not tie to a resolver call, so
   a change of resolver call there is not caught by the census (census logic off-limits here). The guard covers only
   `ResolveOwningTeamAsync`; 018 should decide whether Root writers get a resolver assertion.
6. **Unfiled communication**: the registry exception is honoured as a refusal, not as "keeps the creator" (deviation 4).
7. Noticed, not changed: the `RecordOwnerAssignmentCensusTests` inventory text for `InvoiceReviewService.cs` (:212)
   still says "ResolveOwningTeamAsync"; that file no longer calls it.

`DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-5 and I-6 now describe the unified create (record-first; search index from
the owning BU) and say Office follows in 018.

## POML acceptance criteria

| # | Criterion | Status |
|---|---|---|
| 1 | Every create resolves via `ResolveOwnerAsync(ForParents(...), RequestedBy)`; no create path calls `ResolveOwningTeamAsync` | Met for the unified create and the worker. **Not met for the Office quick-create until 018** (main-session decision); the guard allow-lists it with an expiry |
| 2 | Same Project under the same Matter through four input styles → identical owner | Met through the unified create (`CrossSurfaceOwnerTests`); the live Office route joins in 018 |
| 3 | Owner order covered: secure ancestor → named team; filed-under BU team; acting user's BU team | Met |
| 4 | Explicit search index survives; unset takes the owning BU default (matter, project, document) | Met (unified, incl. isolated). Office in 018 |
| 5 | Negative: unresolved owner → typed refusal, zero creates | Met |
| 6 | Negative: registry exception keeps its behaviour; non-indexed table gets no value | Non-indexed: met. Exception: honoured as the task-013 refusal (deviation 4) |
| 7 | Release-note line in task notes | Met (now a task-018 item) |
| 8 | Build 0 errors; named suites pass unchanged; publish, CVE and conflict-check recorded | Met |

Not done here (coordinator-owned): `TASK-INDEX.md`, `current-task.md`, POML status, the 018 POML item.

rework: fix-round-1 task 015
