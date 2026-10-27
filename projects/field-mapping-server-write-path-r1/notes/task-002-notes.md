# Task 002 notes: fill-only applier + pair/resolver target guard (FR-02, FR-05e) + DI-001

## Rigor declaration

- RIGOR LEVEL: FULL (POML `<rigor>`; modifies .cs in Sprk.Bff.Api, tags bff-api, adds tests under tests/).
- MODEL TIER: sonnet @ effort high. STEP MODE: directional.
- Context loaded: POML, spec FR-02 / FR-05(e), project CLAUDE.md, task-001 notes, defer-issues DI-001, GitHub #2124, ADR-024, polymorphic-resolver pattern, `OwnedChildWrite` pair code, `RecordCreationService` call sites, `.claude/constraints/testing.md`, `bff-extensions.md`, BFF CLAUDE.md.
- Worktree: the isolated worktree first handed to the agent was based on origin/master and later vanished; the work was done in a fresh worktree `C:\wt-fm002` (branch `task/fm-002`, created at `c4a2a7b81` on the coordinator's instruction).

## Files

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/FieldMapping/CreateTimeFieldMapping.cs` | Added `ApplyFillOnly`, the `RegardingPairAndResolverColumns` set, typed conversion of Concat/Template into non-text targets, and an optional `moneyAttributes` set on `Apply`/`ApplyFillOnly`. `Apply` behaviour and its existing call in `RecordCreationService` are unchanged (default `moneyAttributes = null`). |
| `tests/unit/domain/Dataverse/FieldMapping/CreateTimeFieldMappingFillOnlyTests.cs` | New: fill-only, Report Card regression, negative, guard tests. |
| `tests/unit/domain/Dataverse/FieldMapping/CreateTimeFieldMappingTests.cs` | The two DI-001 pinned tests updated to the fixed behaviour (renamed, see below); `Run` helper takes an optional `moneyAttributes`. No other assertion changed. |
| `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` | Semantics table S8 amended, S13 (typed expressions), S14 (fill-only), S15 (pair guard) added, matrix amended, "Pinned current behaviour" replaced by "Currency columns". All cited test names verified to exist and every test is cited. |
| `src/server/api/Sprk.Bff.Api/Services/Office/RecordCreationService.cs` | Not touched (see Decisions 1). |

Test location follows the main-session correction: `tests/unit/domain/Dataverse/FieldMapping/` (KEEP path), not the POML's `tests/unit/Sprk.Bff.Api.Tests/...`.

## Decisions

1. **Fill-only is a new entry point, not a change to `Apply`.** `ApplyFillOnly(rules, target, source, protectedAttributes, warnings, logger, moneyAttributes)` treats whatever `target` (the caller's payload) holds on entry as "supplied", snapshotted once, so one rule's output never blocks another and last-rule-wins is kept. The Office quick-create call (`RecordCreationService.ApplyFieldMappingsAsync`) keeps calling `Apply` and still overwrites, so no existing caller changes behaviour (escalation trigger 1 did not fire). Wiring the unified service onto `ApplyFillOnly` is task 014.
2. **"Not supplied" decided per rule `TargetFieldType`** (FR-02): scalars (Text/Memo/Number/DateTime) absent, null, or empty/whitespace string (the whitespace key is removed from the payload = normalised to null); lookups no bind (absent, null, or an `EntityReference` with an empty id); booleans and option sets absent only (a present `false`, option `0`, and a present key holding null all count as supplied). A supplied column is skipped with no warning.
3. **Resolver field set (escalation trigger 2 considered, not fired).** The five fields are ADR-024's list (type, id, name, url, number; ADR-024 "MUST populate ALL 5"). The id and type reuse `SecureRootInheritance.PairIdColumn` / `PairTypeColumn`; name, url, number are the same three `OwnedChildWrite.PairDisplayColumns` names (that array is `private`, and `OwnedChildWrite.cs` is task 011's file, so the three are literals with a cite). `.claude/patterns/dataverse/polymorphic-resolver.md` still says four fields (stale against ADR-024's 2026-07-02 amendment). No single code constant holds all five: if a reviewer wants one, make `PairDisplayColumns` internal when 011 lands and reference it.
4. **The pair guard is on `ApplyFillOnly` only, NOT on `Apply`.** First implementation put it in the shared rule path; 13 existing tests failed (`SecureRootInheritanceWriterTests.OfficeCreate_*`, `SecureRootCreateRouteTests.OfficeQuickCreate_*`). Cause: the Office Project-under-Matter create gets its ADR-024 pair from the Matter->Project mapping profile and `_rootFiling.PlanCreateAsync` reads that mapped pair to create the project into isolation (task 158). So the legacy path legitimately depends on a mapped pair. Kept `Apply` unchanged, pinned by `Apply_TheLegacyOverwriteEntryPoint_StillWritesAMappedPair_BecauseOfficeProjectFilingReliesOnIt`. **Implication for the main session:** FR-05(e) "a pair-targeting rule never writes" holds for the fill-only path; for the unified service (014, 016) the Project-under-Matter pair must come from the caller (or the service must derive it before the mapping step), otherwise secure-root filing loses its input. Worth a line in the 014/016 briefs.
5. **DI-001 / #2124 fix.**
   - Concat/Template into Number/OptionSet/DateTime/Boolean now converts the resolved text with the same conversion as a Default literal (`TryConvertLiteral`); a result that does not convert is skipped with one warning (`"... its result cannot be written to a field of that type."`), never written as a string. Unresolved-placeholder warnings are unchanged and may precede it.
   - **Default into Money needs a hint the rule cannot carry.** Whole number, decimal and money are one rule type ("Number"), and the pure engine has no attribute metadata. The fix is an optional `IReadOnlySet<string>? moneyAttributes` (target columns the caller knows are currency): a Default/Concat/Template into a Number target in the set writes a `Money`. With no set the engine behaves as before. **This is a design decision for the main session:** no caller supplies the set yet, so the defect is fixed in the engine but stays live for a rule configured against a money column until the unified service (014) fills `moneyAttributes` from the table's attribute metadata (or the rule schema gains a Money option, which is a schema change outside project scope). Recommend a line in the 014 brief.
   - Renamed the two pinned tests (doc table updated): `Default_MoneyTarget_WritesAPlainDecimalNotAMoney` -> `Default_MoneyTarget_WritesAMoney` (+ `_FromANonNumber_IsSkippedWithOneWarning`, `Default_NumberTarget_NotNamedAsMoney_StillWritesAPlainDecimal`); `Expression_NonTextNonLookupTarget_WritesTheResolvedStringWithoutConversion` -> five typed-conversion tests plus `Expression_NonTextNonLookupTarget_WhoseResultDoesNotConvert_IsSkippedWithOneWarningNeverAString`.
   - `#2124` is not closed from here: close it when this branch merges. `notes/defer-issues.md` DI-001 status left for the main session (not in my write list).

## Deviations from the POML

- Test path moved to the KEEP path (main-session correction).
- `RecordCreationService.cs` not edited (the POML says only if its call must pass the caller-supplied set; it does not, because it stays on `Apply`).
- TASK-INDEX / POML status not touched (main-session rule).

## Test counts

- New `CreateTimeFieldMappingFillOnlyTests`: 37 cases (24 methods incl. theories); `CreateTimeFieldMappingTests`: 97 cases (was 88); `CreateTimeFieldMapping*` together 134 passed, 0 failed.
- Fail-before / pass-after: the new members (`ApplyFillOnly`, `moneyAttributes`) did not exist before, so the new tests could not compile against the old engine. Behaviour proof by mutating the final engine and rerunning the 134 tests: fill-only skip disabled -> 11 fail; guard disabled -> 11; expression string write restored -> 10; Money flag ignored -> 3; option-set/boolean "absent only" broken -> 2; whitespace treated as supplied -> 4. Engine restored (hash-identical) and rebuilt afterwards. (The mutations were run against the first guard placement; the later move of the guard to fill-only changed only its condition.)
- Broad run, filter `Office|FieldMapping|RecordCreation` on `Sprk.Bff.Api.Tests`: **998 passed, 0 failed, 7 skipped (pre-existing)**, total 1005. A first run of this filter reported 17 failures: 13 from the guard placement (Decisions 4, fixed) and 4 from a stale mutated binary (the restored source file kept the older mtime, so MSBuild did not rebuild; fixed by touching the file).
- `tests/Spaarke.ArchTests`: 1152 passed, 0 failed.

## Quality gates

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings. `dotnet build Spaarke.sln`: 0 errors, 0 warnings.
- Publish size (bff-hygiene item 4): `origin/master` is still `2b07480ab`, so the master side is task 001's fresh measurement at that exact commit, 39,281,358 B, 192 files. Branch measured from a detached worktree at `08ca7f4ad` (`C:\tmp\f2a`, `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal` over `api-publish\*`, PDBs included): **39,282,233 B, 192 files, delta +875 B** (this includes task 001's +314 B already on the project branch). Far below +5 MB / 55 MB / 60 MB. Temp worktree removed with `Remove-WorktreeSafe.ps1`.
- CVE: `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package changes.
- BFF placement (CLAUDE.md sec 10): extension of an existing `internal static` engine; no DI registration, endpoint, package or AI dependency (ADR-010).
- Component justification (CLAUDE.md sec 11): existing = the engine and its `Apply`; extension = two methods + one static set on the same class, no new production class; cost of doing nothing = Report Card picks overwritten, pair corruptible, Default/expression writes that fail the SDK create.
- `/conflict-check`: `origin/master` unchanged (`2b07480ab`); no open PR lists `CreateTimeFieldMapping*` or the framework doc. Sibling worktrees (003, 004/005, 011) own different files. Hard warn: none.
- `/code-review` (scoped to the 4 files; self-run, FULL rigor, no separate reviewer agent): no F-class findings left.
  - Found and fixed in the loop: pair guard broke the Office secure-project create (F1, fixed by Decisions 4); stale-binary test false failures (process, fixed).
  - K-class, left: **K1** Concat/Template into a numeric target with unresolved placeholders yields two warnings (the placeholder one, then the "cannot be written" one); accurate, noisy. **K2** unrecognised `TargetFieldType` codes fall through to "literal as written" (existing client behaviour) so an expression into an unknown type still writes a string. **K3** the Money hint has no producer yet (Decisions 5; tracked for 014). **K4** the three display-column names are literals (Decisions 3).
  - AI smells: none (no new interface/abstraction, no log-and-rethrow, comments explain the Office-pair exception and the Money hint).
- `/adr-check`: ADR-010 (internal static, no DI) pass; ADR-002 (no plugin, engine only decides which mapped values are written, no authorization) pass; ADR-024 (pair and resolver fields never mapped on the new path; Office pair write is the documented legacy exception, ADR-024 Path C-style note in Decisions 4) pass; ADR-038 (KEEP path, no `Mock<HttpMessageHandler>`, no DI-registration tests, no reflection, `internal` access via the existing `InternalsVisibleTo`) pass. No violations.

## Acceptance criteria

1. Supplied value survives / unsupplied filled for scalar, lookup, boolean, option-set per FR-02: **met** (S14 tests).
2. Report Card regression named test: **met** (`ReportCard_LawFirm1_AssignedExternal_AssignedInternal_SuppliedByTheCaller_AreNotOverwritten_AndAnUnsuppliedSiblingIsFilled`).
3. Pair / resolver / protected target never writes, one warning: **met on the fill-only path**; the legacy `Apply` path intentionally still writes a mapped pair (Decisions 4).
4. Negative (everything supplied -> empty output, no warning): **met**.
5. Task 001 tests unchanged except the two DI-001 tests; build 0 errors; publish delta and CVE recorded: **met**.
6. DI-001 / #2124: Concat/Template **fixed**; Default->Money **fixed in the engine** via the `moneyAttributes` hint, no producer wired yet (Decisions 5); close #2124 on merge.

rework: none task 002
