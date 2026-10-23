# Task 003 notes: mapping-source resolver + Read-as-caller source probe (FR-03, FR-04)

## Rigor declaration

- RIGOR LEVEL: FULL (POML `<rigor>`; tags security, bff-api, testing; modifies .cs in Sprk.Bff.Api; adds tests).
- MODEL: Opus @ high (POML `<model-tier>` opus). STEP MODE: directional.
- Context loaded: POML; project CLAUDE.md (FR-03, FR-04, FR-08 filing-lookup amendment); spec FR-03/FR-04/NFR-01;
  design §4.1–4.3 and §6; w2-coordination.md; testing.md; bff-hygiene; BFF module CLAUDE.md; task 001 notes;
  `QuickCreateSourceAccessFilter`, `CallerRecordAccessProbe` (incl. the task 064 gate variant), `EntityAccessFilter`,
  `OwnedChildWrite.PairTargetAsync`/`ParentsOf`, `RecordOwnershipContext.ParentsOf`, Office
  `RecordCreationService.ApplyFieldMappingsAsync`; tasks 013/014 POMLs (consumer expectations).

## Files

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/FieldMapping/MappingSourceReadProbe.cs` | New. `CheckCallerReadAsync` (the ONE caller-Read decision: entity set → probe → `HasRequiredRights(…,"read")` + exception handling, returning a `CallerReadDecision`); `ReadAsync` (caller: probe first, app-only read only after Read; system: app-only, no probe; every failure = `MappingSourceRead.Unavailable`). Small types: `MappingSourceReader` (caller/system), `MappingSource`, `MappingSourceRead`, `CallerReadDecision`, `CallerReadCheck`. |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/FieldMapping/MappingSourceResolver.cs` | New. `ResolveAsync`: explicit → pair → single typed parent; `MappingSourceOrigin`, `MappingSourceResolution`. |
| `src/server/api/Sprk.Bff.Api/Api/Filters/QuickCreateSourceAccessFilter.cs` | Source half AND assignee half now call `CheckCallerReadAsync` and switch on the decision. Same 403s, same reason codes/details, same order, same log lines; `ReadOperation` now aliases the shared const. |
| `tests/unit/domain/Dataverse/FieldMapping/MappingSourceResolverTests.cs` | New, 13 methods / 16 cases (pure; catalog is an in-memory delegate). |
| `tests/integration/auth/FieldMapping/MappingSourceReadProbeTests.cs` | New, 12 tests (authorization decisions through the production probe with one virtual seam substituted; `IGenericEntityService` via Moq). |
| `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` | Two component-inventory rows. |

## Decisions

- **Shared helper returns a decision, not a bool.** The filter needs its three distinct 403s (`entity_type_not_authorizable`,
  `access_check_failed`, `insufficient_rights`); the mapping read collapses all three to one outcome. One sequence, two
  renderings. OCE propagation (`when ct.IsCancellationRequested`) is identical because the filter passes `RequestAborted`.
- **Assignee half re-pointed too.** It was a second copy of the same sequence inside the same filter; acceptance
  criterion 3 ("no second copy") would otherwise fail by grep. Its one constant refusal is unchanged.
- **Timing.** The filter keeps `GetCallerRightsAsync` (`equalizeUnknownAndDenied: false`) — its behaviour must not change.
  The mapping read uses `GetCallerRightsForAuthorizationGateAsync` (`true`) so a missing and a denied source take the
  same work (task 064); a test asserts identical asks and waits.
- **Profile before probe is the owner's (014) order**: no profile → silent, no probe (NFR-02: no cost without a profile);
  profile without rules → warning; then `ReadAsync`. Profile existence depends on the table pair only, not the record,
  so it is no oracle. `ReadAsync` with no columns (Default-only profile) still probes the caller: denied = no mapping at
  all, including Defaults (FR-04 "no mapping").
- **Resolver input is format-neutral**: SDK attributes (`IEnumerable<KeyValuePair<string, object>>`), so typed parents come
  from `RecordOwnershipContext.ParentsOf(fields, table)` — the existing definition (ownership parents, citations
  excluded); no third copy of that predicate. JSON callers (child-records route) convert their lookups to
  `EntityReference`s. The record-type catalog read is a delegate the owner supplies (014; same catalog read as
  `PairTargetAsync`), keeping the resolver I/O-free.
- **A named-but-unusable pair never falls through** to a typed lookup (it may name a different record): Undetermined +
  warning. A half-named or malformed explicit source likewise never triggers inference.
- **Principal**: task 013 owns the principal model; `MappingSourceReader.ForCaller(probe, token)` / `.System` is the seam
  it maps onto. It is a sealed class (not a record) so the token has no generated `ToString`/equality (review fix).
- **Test location (main-session correction)**: resolver tests are pure → `tests/unit/domain/Dataverse/FieldMapping/`
  (namespace `Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping`). Probe tests exercise an authorization decision through
  the production `CallerRecordAccessProbe` (substituted seam) and a Moq module boundary → not "no mocks, no I/O" domain
  logic, so they go to the security-auth KEEP path `tests/integration/auth/FieldMapping/` (testing.md §1;
  namespace `Sprk.Bff.Api.Tests.Auth.FieldMapping`, the `Auth.*` convention used by `auth/Office`, `auth/SpeAdmin`).

## Deviations from the POML

- Test paths per the main-session correction (above), not `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/FieldMapping/`.
- TASK-INDEX/status not updated (main session owns it).
- The "create-time mapping result" for a denied source is asserted at the `ReadAsync` level (`Readable=false`, one
  warning, no read, no throw); wiring that result into the create (no rule applied) is task 014's step 3.

## Tests

- New: 28 cases pass (resolver 16, probe 12), 0 fail.
- Fails-before: by construction the types did not exist (tests did not compile). Mutation/seeding proofs, each run then
  restored (hash-checked):
  - M1 `CheckCallerReadAsync` always Allowed → 4 probe tests fail AND 2 existing quick-create contract tests fail
    (`Post_Matter_WhenCallerCannotReadTheSourceRecord_Returns403_ReadsNothing_AndCreatesNothing`,
    `Post_Matter_AnUnreadableSourceIsRefusedFirst_AndNoPrivilegeIsAsked`) — proves the filter now runs through the helper.
  - M2 mapping read with `equalizeUnknownAndDenied: false` → `ReadAsync_MissingAndDeniedSources_GiveTheIdenticalOutcomeAfterTheSameWork` fails (timing oracle caught).
  - M3 untyped pair ignored (falls through) → 4 resolver tests fail.
  - Gotcha hit: `Copy-Item` restores keep the backup's old mtime, so the incremental build kept the mutated binaries; the
    restored files were touched and rebuilt with `--no-incremental` before the final runs.
- Existing `QuickCreateSourceAccessFilter` tests (Office quick-create / todo-source / save / assigned-internal contract
  filter): 120 passed, 3 skipped (pre-existing skips) before AND after; assertions unedited.
- Broader `Office|FieldMapping|MappingSource` filter on Sprk.Bff.Api.Tests (after the review fix, clean rebuild): 901
  passed, 0 failed, 7 skipped (pre-existing skips).

## Quality gates

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings. `dotnet build Spaarke.sln`: 0 errors, 0 warnings.
- ArchTests: 1152 passed, 0 failed (re-run after the review fix on a `--no-incremental` solution build).
- Publish size: origin/master still `2b07480ab`, so task 001's fresh-master measurement is the master side: 39,281,358 B,
  192 files. Branch: `git worktree add --detach C:\tmp\f3a f2524adf1`, `dotnet publish -c Release`, Compress-Archive
  Optimal over `api-publish\*` (PDBs included): **39,288,234 B, 192 files → delta +6,876 B** (includes task 001's +314 B;
  the later record→class change is size-neutral). Far below +5 MB / 55 MB / 60 MB. Temp worktree removed with
  `scripts/worktree/Remove-WorktreeSafe.ps1`.
- CVE: `dotnet list package --vulnerable --include-transitive` on Sprk.Bff.Api: no vulnerable packages. No package change.
- Placement (§10): CRUD write-path authorization + mapping in the BFF, the single server owner of creation (design §6);
  static helpers, no endpoint, no DI registration, no AI dependency (ADR-013 not triggered).
- `/conflict-check`: origin/master unchanged (`2b07480ab`); 26 open PRs, none touches the filter, `Services/Dataverse/FieldMapping/`,
  the framework doc or the new test folders. Sibling branches: `task/fm-002` no diff yet; `task/fm-011` touches
  `OwnedChildWrite.cs`/`CallerWriteAuthorization.cs` and AI handlers — no overlap (this task calls
  `RecordOwnershipContext.ParentsOf` and `SecureRootInheritance` constants, which 011 does not move).
- `/code-review` (scoped): metrics — probe file 258 lines, resolver 194, filter 380→374; all cohesive (one decision + the
  read it gates; one resolution rule). Findings and triage:
  - F3 (fixed): `MappingSourceReader` was a `record` carrying the caller's bearer token — generated `ToString`/equality
    are a latent token-leak path. Now a sealed class.
  - K4: `ArgumentNullException.ThrowIfNull` on non-nullable params — repo idiom (filter ctor), kept.
  - K2 (for 014): a caller-principal source whose table is not in `EntityAccessFilter`'s entity-set map (e.g.
    `sprk_communication`, `sprk_agreement`, `sprk_analysis`) never maps — fail closed by design (NFR-01). If 014 needs
    mapping from those tables, the map must grow (it is shared and lockstep-guarded), not the probe.
  - K2 (for 014): a typed pair whose table disagrees with a typed filing lookup (#1616) is still resolved here; step 5
    (`PairTargetAsync`) refuses that create, so nothing mapped persists.
  - **Found in passing (pre-existing, NOT changed — contract scope):** the quick-create source half, its assignee half and
    `FieldMappingEndpoints.AuthorizePushSourceAsync` ask through the non-equalized `GetCallerRightsAsync`, so a missing id
    is re-asked (~1.6 s) while a denied one answers at once — a response-time existence oracle behind otherwise uniform
    denials (the assignee half's remarks promise "not a contact-existence oracle"). Fix is one argument
    (`equalizeUnknownAndDenied: true` / the gate variant). Main session to file per `/project-defer-issue-tracking`.
  - Found in passing: `FieldMappingEndpoints.AuthorizePushSourceAsync` and `TodoSourceAccessFilter` carry further copies of
    the same Read sequence; the W4 push task can reuse `CheckCallerReadAsync`.
  - AI smells: none (no interface, no log-rethrow, no restating comments).
- `/adr-check`: ADR-008 (route authz stays in the filter; contract unchanged), ADR-010 (static, no DI, no interface),
  ADR-024 (pair first, then typed lookup, ambiguous → none + warning), ADR-028 (Read under the caller's OBO token;
  system app-only; MI never answers a caller's Read), ADR-002 (no plugin), ADR-038 (KEEP paths, no
  `Mock<HttpMessageHandler>`, no DI-registration tests, no `Task.Delay`). No violations.

## §11 justification (new types)

- `MappingSourceReadProbe` (+ `CallerReadDecision`/`CallerReadCheck`): Existing — the Read sequence lived only inline in
  `QuickCreateSourceAccessFilter` (private, HTTP-shaped); Extension — extracted from it and shared, not forked; Cost of
  nothing — a server-side mapping read without a Read-as-caller check is an existence oracle / AppendTo bypass (NFR-01).
- `MappingSourceReader`/`MappingSource`/`MappingSourceRead`: the inputs/outputs of that read; without them the caller vs
  system path (FR-04) has no explicit, testable switch.
- `MappingSourceResolver` (+ origin/resolution): Existing — Office `ApplyFieldMappingsAsync` takes an explicit source only;
  `RecordOwnershipContext.ParentsOf` is reused for the typed parents; Extension — no existing home for pair/typed/ambiguity
  inference callable from non-HTTP creators; Cost of nothing — non-wizard creators get no mapping source (FR-03).

rework: fix-round-1 task 003
