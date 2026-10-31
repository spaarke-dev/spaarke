# Task 001 notes: promote CreateTimeFieldMapping (FR-01)

## Rigor declaration

- RIGOR LEVEL: FULL (POML `<rigor>`; modifies .cs in Sprk.Bff.Api, tags bff-api, adds tests under tests/).
- MODEL TIER: sonnet @ effort high. STEP MODE: directional.
- Context loaded: POML, spec FR-01, design 4.2, design-review-2026-10-19 sec 2, testing.md, bff-hygiene rule, azure-deployment publish-size rule, framework doc, engine source.

## Files modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/FieldMapping/CreateTimeFieldMapping.cs` | Moved from `Services/Office/` (old file deleted). Namespace now `Sprk.Bff.Api.Services.Dataverse.FieldMapping`; still `internal static`; only the doc-comment crefs to `RecordCreationService` were fully qualified. No code change (diff vs HEAD is the namespace line plus those comments). |
| `src/server/api/Sprk.Bff.Api/Services/Office/RecordCreationService.cs` | Added `using Sprk.Bff.Api.Services.Dataverse.FieldMapping;` |
| `tests/unit/domain/Dataverse/FieldMapping/CreateTimeFieldMappingTests.cs` | New: 57 test methods (88 cases with theories). |
| `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` | New "Canonical Engine Semantics" section (rule -> behaviour -> test table S1-S12, mapping type x target type matrix, pinned non-designed behaviours); component-inventory row; two stale "engine only on word-add-in branch, not on master" statements corrected; Last Updated. |

`InternalsVisibleTo Sprk.Bff.Api.Tests` already exists in `Sprk.Bff.Api.csproj` (line 43), so no csproj change.
All 57 test names are cited in the doc table and every cited name exists (checked by script).

## Decisions

- Namespace `Sprk.Bff.Api.Services.Dataverse.FieldMapping` follows the sibling sub-folder convention (`...Dataverse.Privileges`, `.FetchXml`, `.Models`).
- WholeNumber / Decimal / Money are one engine type ("Number", 3). They are told apart by the CLR type written, which is how the tests cover them.
- Moved with a filesystem move (no `git mv`, nothing staged); git will see the rename at commit.
- The POML mandates the test path `tests/unit/Sprk.Bff.Api.Tests/...`. `.claude/constraints/testing.md` says a new test should live under one of the eight KEEP categories, and the spec says "KEEP path". `tests/unit/Sprk.Bff.Api.Tests/` is not one of the eight (it is where e.g. `ParentLineageTests` already live). Followed the POML; flagged for the main session (see escalation 3).

## Escalations / observations (behaviour pinned, NOT changed)

No current behaviour contradicts a listed FR-01 rule (typed Default, option-set label / ISO-8601 / Yes-No into text, null source skipped, one `{field}` resolver, protected columns skipped with a warning, execution order). Three things the FR does not cover, pinned and flagged:

1. **Default into a money column writes a plain `decimal`, not `Money`** (`Default_MoneyTarget_WritesAPlainDecimalNotAMoney`). The rule type has no Money value, so "typed Default conversion to the target type" is incomplete for money. Dataverse would reject the value. No seeded profile does this.
2. **Concat/Template into a Number / OptionSet / DateTime / Boolean target writes the resolved string without conversion** (`Expression_NonTextNonLookupTarget_WritesTheResolvedStringWithoutConversion`). Unlike Default there is no typed conversion for expressions; Dataverse would reject it at create.
3. **Test location**: see Decisions.

Also noticed (not changed): the Default literal for Text/Memo is written untrimmed, and an unresolved placeholder repeated N times yields N warnings. Both are pinned or incidental; neither contradicts FR-01.

## Quality gates

- BFF build: `dotnet build src/server/api/Sprk.Bff.Api/` 0 errors, 0 warnings. `dotnet build Spaarke.sln` 0 errors, 0 warnings.
- New tests: 88 cases passed, 0 failed. Proof the tests bite: temporarily mutated the engine (Yes/No rendering, ISO format, OrderBy removed, protected guard disabled) -> 16 of 88 new tests plus 2 existing Office contract tests failed; engine restored byte-identical (hash checked) and rebuilt. Pre-change "fails before" is by construction: the type did not exist in the new namespace, so the tests did not compile; this task is a characterization of unchanged behaviour.
- Existing tests referencing the engine / Office record creation (`Services.Office`, `Api.Office`, `FieldMapping`, `RecordCreation` filter on `Sprk.Bff.Api.Tests`, which includes the Office quick-create contract tests globbed into that project): 729 passed, 0 failed, 7 skipped (pre-existing skips), total 736. Existing tests unchanged.
- `tests/Spaarke.ArchTests`: 1152 passed, 0 failed.
- Publish size (bff-hygiene item 4): `dotnet publish -c Release` from fresh detached worktrees of `origin/master` @ `2b07480ab` at `C:\tmp\f1m` (master) and `C:\tmp\f1a` (master + this task's source changes); zipped both with PowerShell `Compress-Archive -CompressionLevel Optimal` over `api-publish\*`, PDBs included, framework-dependent as the csproj builds. Master 39,281,358 B (37.46 MiB), branch 39,281,672 B (37.46 MiB), **delta +314 bytes**; file count 192 vs 192. Far below +5 MB / 55 MB / 60 MB thresholds. Temp worktrees removed with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- CVE: `dotnet list ... package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package changes.
- BFF placement (CLAUDE.md sec 10): a pure move within the BFF (it stays `internal static`, no DI registration added or removed, no endpoint, no AI dependency); in the BFF because it is the Dataverse write-path engine shared by W2 `RecordCreationService` and W4 push (design 4.2).
- `/conflict-check` (BFF hot path): 26 open PRs, none lists the touched files; `origin/master` has 0 commits ahead of the worktree base. Another worktree (`C:/wvs160`, detached at an old commit, ~19.8k staged entries) shows these paths as staged deletions; that is a stale sparse-style tree, not live work on them. Hard warn: none. Soft: other projects own the BFF surface generally (projects/INDEX.md), no file overlap.
- `/code-review` (scoped: 1 move, 1 using line, 1 test file, 1 doc): no Critical/Warning findings in the engine (no code change) or the using line. Test file cohesive (one engine, behaviour-per-test, no mocks other than a hand-written throwing set as the only way to make one rule fail; no mirror/all-mock tests). Findings: K-class only. K4 test-path vs "KEEP category" ambiguity (above, left to the main session). No F-class findings. AI smells: none (no new interfaces, no log-rethrow, comments explain pinned decisions).
- `/adr-check`: ADR-010 compliant (internal static, no DI change), ADR-002 compliant (no plugin; the engine is the server-side owner of mapping), ADR-038 compliant for banned patterns (no `Mock<HttpMessageHandler>`, no DI-registration tests, no ctor-null tests, no reflection; `internal` access via the existing `InternalsVisibleTo`, Amendment A2: the engine is the contract under test). ADR-001/007/008/013/028: not touched. No violations.

## Not done (by instruction)

TASK-INDEX.md, `current-task.md`, POML `<status>`, `projects/INDEX.md`, git commit: main session owns them.

rework: none task 001

## Main-session follow-up (2026-10-20)

- **Test location fixed (F2):** `.claude/constraints/testing.md:132` forbids new tests outside the eight KEEP paths.
  The file moved to `tests/unit/domain/Dataverse/FieldMapping/CreateTimeFieldMappingTests.cs`; that glob is compiled
  into Sprk.Bff.Api.Tests. Namespace is now `Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping`, and the doc reference
  is updated. 88/88 pass from the new path, and the Office plus CreateTimeFieldMapping filter passes 735, with 7
  skipped as before.
- **Escalations 1–2 (wrong CLR type for Default→Money and expression→non-text):** these are real F1 defects. They
  are filed as DI-001 / #2124 (notes/defer-issues.md) and assigned to task 002 (acceptance criterion added). Not fixed
  here because 001 is zero-semantic-change.
