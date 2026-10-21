# Task 014 notes: field mapping inside the unified create; caller checks on the merged payload (FR-06 step 3, FR-08)

## Rigor declaration

- RIGOR LEVEL: FULL (POML `<rigor>`; tags bff-api, security, authorization, testing; `.cs` in `Sprk.Bff.Api`; tests
  at KEEP paths).
- MODEL TIER: opus @ xhigh (POML). Branch `task/fm-014`, cut from `fad68a797b` (task 013 complete, fix round 1).
- Context loaded: POML, spec FR-02..FR-08 and NFR-01, design §4, `notes/task-013-notes.md`, `notes/w2-coordination.md`,
  the task 001-003 outputs (`CreateTimeFieldMapping`, `MappingSourceResolver`, `MappingSourceReadProbe`), the Office
  core's `ApplyFieldMappingsAsync` (the behaviour superseded), `CallerWriteAuthorization`, `DataverseWriteItemMapper`,
  ADR-008/010/019/028/038, `.claude/constraints/bff-extensions.md`, and the coordinator's two binding additions
  (ebilling-r1's review of 013; UAC-r2's review of 013, K-2), both 2026-10-21.

## Conflict check (POML step 1)

32 open PRs. #2184 (this project's PR) overlaps, as expected. #2161 (`fix/uac-2159-refile-relocation`) and #2150
(`fix/uac-2143-reparent-owner-race`) touch `OwnedChildWrite.cs` only in their `RefileOutcome` / `RefileAsync` hunks; no
open PR changes `CallerWriteAuthorization` or the create authorization helpers, so the POML escalation trigger does not
fire. Branch-level overlaps with #2182, #2177, #2169, #1911 and #935 (1-4 files each) involve none of this task's files.
`origin/master` is unchanged at `44063b3473` and is an ancestor of HEAD. Task 013 fix round 2 (`task/fm-013-r2`, in
parallel) was avoided: no edit to `Services/Office/RecordCreationService.cs`, to `RecordOwnerAssignmentCensusTests`, or
to the `RecordCreationServicePipelineSeamTests` harness. Hot path: BFF, soft warn, no escalation.

**Re-checked at the end of the task.** `origin/master` had moved to `2a38e6ce6c`, which contains this branch's base
`fad68a797b` and task 013 fix round 2. Since the base, master changes only:
- `RecordCreatorPerson.cs` (two lines);
- the Office file rename;
- the census rows;
- the 013-r2 seam tests: the pipeline harness, a new `RecordCreationServicePreWriteRuleSeamTests.cs`, and one new
  secure-root test;
- `RecordTableRulesPreWriteRatchetTests.cs`.

No file is changed on both sides. So this branch's nine files laid over `2a38e6ce6c` equal the merge result for them. I
did that in a throwaway detached worktree (`C:\tmp\f14m`, removed with `Remove-WorktreeSafe.ps1`); no merge was run on
this branch.
- The required suites: 1596 passed, 7 skipped, 0 failed. This includes 013-r2's pre-write seam tests and its new
  secure-root test.
- ArchTests: 1214 passed.

## Placement Justification (CLAUDE.md §10)

BFF, `Services/RecordCreation/` (the service task 013 created). Mapping runs inside the synchronous create request
(spec WP-1, WP-4): the caller waits for the id, the source Read probe and every merged-payload check run under the
caller's own token (ADR-028), and the engine, resolver and probe it calls are already in the BFF. No endpoint, no
background work, no package, no DI registration.

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationService.Mapping.cs` (a partial of the SAME class) | `RecordCreationService` (013), Office `ApplyFieldMappingsAsync` | Extends the class; a partial file keeps steps 3-5's mapping half readable (the POML said "no new production file": deviation 1) | Step 3 stays a no-op; mapped lookups bypass AppendTo (#1617) and Office Project AppendTo is checked for secure parents only (#1614) |
| `RecordCreationRequest.Mapping` + `RecordCreationMapping` (private ctor, `ForCaller`) | `MappingSourceReader` (003) | Wraps the 003 reader with the explicit `mappingSource`; a class, not a record, so the caller's token has no generated `ToString` | No way for an adapter to ask for mapping or pass the explicit source; every create would map, breaking 013's tests and the chat path |
| `RecordCreationWarning` (code, column, detail) + `RecordCreationOutcome.MappingWarnings` | `RecordCreationOutcome.Warnings` (strings) | `Warnings` keeps the strings both responses carry today; the codes and column need a typed carrier (ADR-019) | FR-08/ADR-019: a dropped value would have no stable code or column |
| `RecordCreationRefusal.ServerSetFilingLookupCode` | the 013 refusal codes | One constant beside them | K-2: a server writer marking a filing lookup server-set files the row with no AppendTo check |
| `CreateTimeFieldMapping.ApplyFillOnly(..., mappedColumns)` | `ApplyFillOnly` (002) | An optional parameter on the existing method; callers that omit it are unchanged (tested) | Origin would have to be guessed by value comparison (POML: forbidden) |
| `RecordTableRules`: `MappingProtectedColumns` on `sprk_invoice`, `sprk_billingevent`, `sprk_document` | the property (012), set on matter/project | Entries only | ebilling-r1: a second mapped core lookup fails a valid invoice create; UAC-r2 D7: mapping could write the document's access record |

No interface, no registration (ADR-010): every collaborator is static or already the service's.

## ADR-008, path A

Every merged-payload check is `CallerWriteAuthorization.CheckCallerMayCreateAsync` (and, after the plan,
`CheckCallerMayAppendToAsync`), unchanged, CALLED by the service. No endpoint filter sees mapped values.
`CallerWriteAuthorization.cs` and `OwnedChildWrite.cs` are not modified. Cite path A in the PR description.

## How it works

- **Opt-in per request.** `RecordCreationRequest.Mapping` (null = step 3 records `NoOp` "no field mapping was asked
  for", and step 5 is task 013's single check, unchanged). `MappingExclusion` still records `Skipped`.
- **Step 3** (`MapFieldsAsync`, never refuses):
  1. Resolve the source: explicit, else the regarding pair, else the single typed parent. A half-named or ambiguous
     source gives `mapping_source_undetermined`.
  2. Read the profile app-only. It is keyed by the two TABLES only.
  3. Normalize each rule's target (trimmed, lower-cased, on a copy).
  4. Probe Read AS THE CALLER, then read the source. A missing, denied, unmapped-type, failed-probe or failed-read
     source is one outcome: `mapping_source_unavailable`, the 003 probe's one text, nothing read.
  5. Read the target columns' metadata as the caller (type and `IsValidForCreate`). The Money columns become
     `moneyAttributes`.
  6. Call `ApplyFillOnly` with the protected set and `mappedColumns`, which is the ORIGIN set.
  7. Check each written column against its target: it exists and is valid for create, it `Fits` the type, and its date
     behaviour is known. It is then converted to the item form. A column that fails is dropped with
     `mapped_value_dropped` plus the column.
  8. A fault anywhere in step 3 maps nothing and reports ONE warning (`mapping_unavailable`, the `MappingFaulted` text).
     The caller's row goes on.
- **Protected from mapping:**
  - the common and table server-owned columns;
  - the autonumber and the creator person;
  - the registry's `MappingProtectedColumns`;
  - an inheriting root's access record and secure flag;
  - every server-set lookup;
  - the regarding pair and resolver columns (enforced in the engine).
- **Step 4** (`MapMergedAsync`): the caller's item and the mapped values are mapped TOGETHER as the caller
  (`DataverseWriteItemMapper.MapAsync`). So every mapped lookup has its `RelatedEntitySet` and is bound by the table's
  own navigation property. If the whole does not map, each value is mapped alone, and the ones that do not map are
  dropped.
- **Step 5** (`CheckMergedPayloadAsync`, only when something was mapped):
  1. The caller's OWN item alone, exactly task 013's check. A failure refuses unchanged; a fault propagates (503).
  2. The merged row whole.
  3. On a failure or fault, the caller's values plus every mapped FILING lookup, when there is one. A failure refuses
     with the uniform outcome; a fault propagates.
  4. Every other mapped value alone. A failure or fault drops it, named.
  5. The row as written, once more. A failure refuses.

  `item` is reassigned to what survived: the SAME local reaches the pre-write rules (5½), the plan and the create.
- **K-2 guard** (before step 3 since fix round 3; recorded as a `CallerChecks` refusal): a lineage or ownership-parent
  lookup in `ServerSetLookupColumns` is refused with `record_creation.server_set_filing_lookup` (500, nothing written).
  Since fix round 3 this also covers any lookup the owner decision files the row under (`OwnedChildWrite.ParentsOf`),
  on every table except a work assignment or project.
- **Step 6, after the plan** (fix round 1): a work assignment's or project's own filing lookup in
  `ServerSetLookupColumns` is asked AppendTo as the caller. The plan asks AppendTo only on a secure record. A refusal is
  step 5's uniform answer.
- **Outcome:** `MappingWarnings` carries code, column and detail. `Warnings` lists their details first, then the
  post-create warnings.

## Decisions and deviations

1. **Partial file instead of "no new production file".** `RecordCreationService.Mapping.cs` is the same class
   (`partial`) and holds the mapping half of steps 3-5, about 720 lines. It belongs with the service. It is kept out of
   the 013 file so the parallel 013 fix round's edits merge cleanly. It is one cohesive responsibility (§11.5).
2. **Test paths (coordinator override of the POML output).**
   - The POML named `tests/integration/data-mutation/RecordOwnership/MappedValueAuthorizationTests.cs`.
   - The coordinator's brief allows only `tests/unit/domain/**` and `tests/integration/seam|auth/**`.
   - The #1614 and #1617 regression tests are in `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.cs`,
     beside 013's seam suites.
3. **Mapping is opt-in** (`RecordCreationRequest.Mapping`). This keeps every 013 test and the not-yet-re-pointed callers
   unchanged. Tasks 017 and 018 pass it when they re-point the route and the Office create.
4. **A system principal with a mapping is an `ArgumentException`.** This deviates from the POML's ADR-028 note and from
   FR-07 ("a system create yields the same ... mapping"). A system mapping needs an app-only reader for the target's
   column metadata and the merged-item mapper, and no system creator exists yet. OWNER item for task 033.
5. **Mapping never refuses.** Every step-3 failure maps nothing, with a warning. The only mapping-related refusals are
   in step 5:
   - a mapped filing lookup that fails AppendTo (the FR-08 amendment);
   - a final re-check of the row as written that fails (fail closed: no single value can be blamed).
6. **Mapped values are type-checked against the target's metadata** before they are merged. One that cannot be written
   is dropped, named, instead of failing the create at Dataverse.
7. **Rule targets are normalized** (trimmed, lower-cased copy). A rule spelled `"  SPRK_Name "` sees the caller's
   `sprk_name` (fill-only), and a protected column is protected whatever its spelling.
8. **The caller's own values are judged alone first** (fix round 1, review K-3). No mapped value can make a payload
   the caller could not send on its own pass, and a caller refusal is byte-identical with or without mapping. It costs
   one extra `CheckCallerMayCreateAsync` per create that actually mapped something.
9. **A server-set root filing lookup is ASKED, not refused** (fix round 1, review F-1). Refusing it at the K-2 guard
   would change task 013's `RecordCreationServiceSecureRootSeamTests.CreateAsync_WhenTheCallerMayNotFileUnderTheSecureMatter_TheBodyIsTheMissingParents_404`,
   which marks the matter server-set and expects the PLAN's refusal. So the plan runs first, as in 013, and the
   AppendTo it skips for an ordinary record is asked right after it.
10. **Step 5 asks Create/Append once more per mapped value** on the failure path. The "alone" check is the same
    `CheckCallerMayCreateAsync`. This keeps `CallerWriteAuthorization` unchanged.

## Tests (ADR-038 KEEP paths)

| File | Tests | What |
|---|---|---|
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.cs` (new) | 32 | The real service, resolver, probe, engine, caller checks and item mapper; doubles only at the module edges (the caller's scripted Dataverse plus target metadata, the probe's wire, the app-only profile reader, create and source read) |
| `tests/unit/domain/Dataverse/RecordCreation/RecordCreationMappedValueTests.cs` (new) | 8 methods | `ToItemValue`, `Fits`, `NormalizedRule` copies every property, `IsFilingLookup` |
| `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs` | +3 | Invoice-family core lookups, D7, the tables that declare protected columns |
| `tests/unit/domain/Dataverse/FieldMapping/CreateTimeFieldMappingFillOnlyTests.cs` | +2 | `mappedColumns` names exactly what a rule wrote; optional and does not change the values |

The four classes: 134 passed. No `Mock<HttpMessageHandler>`, and no DI-registration test.

### Seeding proof (POML)

Mutant M1 makes the merged-payload check a no-op (`whole.Succeeded || mappedValues.Count > 0`). The #1617 test
`CreateAsync_AMappedLookupTheCallerCannotAppendTo_IsDroppedWithAWarningNamingTheColumn_AndTheRowIsCreated` FAILS under
it, as do the field-security, filing-refuse, work-assignment filing and mapped-fault tests. After restoring the file it
passes. This was run on the initial commit and again after fix round 1.

### Fail-before evidence (mutants; each applied alone, built, run, restored; tree clean after)

| Mutant | Change | Killed by |
|---|---|---|
| M1 | merged check a no-op (seeding) | #1617 drop, field-security drop/refuse, mapped filing refuse, WA filing refuse, mapped-check fault |
| M2 | step 3 never runs | 19 mapping tests |
| M3 | server-set columns not protected from mapping | `ARuleTargetingAServerSetLookup_IsSkipped…` |
| M4 | K-2 guard removed | `AFilingLookupMarkedServerSet_IsRefusedBeforeAnyCheck…` |
| M5 | mapped filing lookup dropped, not refused | event and WA filing-refuse tests |
| M6 | registry before this task | 3 registry tests (4 cases), D7 seam, invoice seam |
| M7 | a lookup copy not added to the origin set | the `MappedColumns` unit test and 7 mapped-lookup seam tests |
| M8 | pre-write rules see the caller's item | `ThePreWriteRuleSeesExactlyTheMergedItem…` |
| M9 | a step-3 fault not caught | `WhenMappingFaultsAfterTheSourceRead…` |
| M10 | rule target not normalized | `NormalizedRule…`, spelling, D7 |
| M11 | a mapped value's check fault propagates | `WhenTheCheckOfAMappedValueThrows…` |
| M12 | `moneyAttributes` not passed | **survives: equivalent on this path** (below) |
| M13 | the probe reads on any right | missing-vs-denied (the pre-fix test SURVIVED it: review F-2) |
| M14 | `IsFilingLookup` → child filing only | WA mapped filing refuse |
| M15 | the caller's own check skipped | untyped-pair test |
| M16 | the server-set root filing refusal removed | the ordering test and the ordinary-record test |
| M17 | a faulted step keeps its earlier warnings | the K-8 test |
| M18 | the date `$filter` unfiltered | the K-9 test |

The fix-round tests were also run against the pre-fix production files. The F-1, K-3, K-8 and K-9 tests fail on
`43365bcb47`, and the ordering test fails on `36ec35d62e`.

**M12 is an equivalent mutant on the Web API path.** The review confirmed it both ways. `TryConvertLiteral` turns the
same numeric text into an int or decimal, or into a `Money`. `Fits("Money", …)` accepts all three, and `ToItemValue`
writes the same JSON number. `moneyAttributes` is passed (DI-001) and matters on the SDK path, where a decimal in a
money column is rejected. The money-default test proves the Web API body. Prove #2124 on the SDK path in task 018,
which deletes the Office core.

## Build, publish, CVE

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors. ArchTests (`tests/Spaarke.ArchTests`): 1214 passed.
- Required suites, unchanged (RecordCreation incl. the 013 seam suites, FieldMapping, Office, SecureChildOwnershipAiToolTests*,
  RefileWriterAfterRefileGuardTests, RouteAuthorizationGuardTests, SecureRootInheritanceWriterTests incl. `OfficeCreate_*`,
  SecureRootCreateRouteTests incl. `OfficeQuickCreate_*`, RecordOwnerAssignmentCensusTests): 1584 passed, 7 skipped,
  0 failed on `7a09762cdc`.
- Publish size (`dotnet publish -c Release`, zipped Optimal with PDBs, from detached worktrees under `C:\tmp` removed
  with `scripts/worktree/Remove-WorktreeSafe.ps1`):

  | Build | Commit | Files (PDBs) | Zip bytes |
  |---|---|---|---|
  | `origin/master` (baseline) | `44063b3473` | 192 (4) | 39,314,273 |
  | `task/fm-014` | `7a09762cdc` | 192 (4) | 39,374,668 |
  | **Delta** | | 0 | **+60,395 (+59.0 KB)** |

  The delta covers the whole project branch (tasks 011-014). It is far under the +5 MB per-task threshold. No package was
  added.
- CVE: `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages.

## Step 9.5: code review and ADR check

Self-review against the `code-review` checklist (security, ADRs, BFF hygiene, component justification) and `adr-check`,
alongside the two opus adversarial passes below.

**ADR check: no violation.**
- ADR-001: no endpoint.
- ADR-002: no plugin, and one owner per invariant. The protected columns come from the registry.
- ADR-008: path A.
- ADR-010: no registration.
- ADR-019:
  - every warning has a stable code and a column, and carries no source id, name or value;
  - no exception text reaches a warning (tested with an IP in the fault message);
  - refusals use the 013 problem map.
- ADR-028: every check and metadata read runs as the caller; the create is app-only; the source read is app-only only
  after the caller's Read probe.
- ADR-029: size measured.
- ADR-038: KEEP paths.
- ADR-052: no background work.

**Metrics.** `RecordCreationService.Mapping.cs` is 727 lines and `RecordCreationService.cs` 1,203. Both are one class
with one pipeline.

## Adversarial review

There were two opus passes (security work). Every F-class finding was fixed.

**Pass 1** (full branch, `fad68a797b..43365bcb47`): no path was found where a mapped value is written past an as-caller
check, and no fail-open path.

| # | Finding | Class | Disposition |
|---|---|---|---|
| F-1 | A K-2 gap: a work assignment's or project's filing lookup marked server-set skipped AppendTo under an ORDINARY record (the plan asks only on a secure one), and the guard's comment said otherwise | F | Fixed in `36ec35d62e` (asked after the plan), then moved before it in `7a09762cdc` (pass 2, finding A). Comment corrected |
| F-2 | The missing-vs-denied test could not tell the two apart (identical doubles) | F (test) | Fixed: the denied source exists and the caller holds rights other than Read. M13 is now killed (it survived the old test) |
| K-1 | No test of a mapped secure-root filing lookup | K2 → fixed | Test added (M14 killed) |
| K-2 | A mapped filing lookup that fails FIELD SECURITY refuses (403), and the text says "from chat" | K4 | Refusing is the fail-closed reading (a dropped filing lookup would leave the row unfiled). The OWNER confirms. The text is `CallerWriteAuthorization`'s, shared with every caller value since 013 (OWNER/017) |
| K-3 | A mapped lookup could complete an untyped caller pair that 013 refuses | K4 → fixed | Callers-first (decision 8); test added (M15 killed) |
| K-4 | The source is read app-only after a record-level Read probe, so field-secured source columns are copied | K3 | Task 003/030 residual (documented in `MappingSourceReadProbe`). 014 widens it to every create path: OWNER item |
| K-5 | "Filing" is a fixed column list. A mapped lookup to an ownership-parent table in another column drops rather than refuses | K2 | None exists on the registered tables. For UAC-r2 |
| K-6 | M12 is equivalent | K4 | Recorded above |
| K-7 | The Office "named contact, else mapped, else maker's contact" order cannot be expressed | K3 | Task 015/018 (a post-mapping default hook) |
| K-8 | A faulted step 3 kept its earlier warnings beside "none were copied" | K4 → fixed | One warning (M17 killed) |
| K-9 | Date columns reached the metadata `$filter` unchecked | K4 → fixed | Logical names only (M18 killed) |

**Pass 2** (the fix diff `43365bcb47..36ec35d62e` plus its direct callers and callees): K-3, K-8, K-9, F-2 and K-1 are
correct and complete.

| # | Finding | Class | Disposition |
|---|---|---|---|
| A | The F-1 check ran AFTER the plan. A plan that refused first gave a different answer from step 5's uniform 404 for a missing record — an existence oracle. Live: a matter with a masked or unreadable secure flag (409). By reading: a server-set intermediate under a secure matter | F | Fixed in `7a09762cdc`. The check runs BEFORE the plan and refuses in the plan's own AppendTo shape. Task 013's secure-parent test is unchanged and passes. Ordering test added (fails on `36ec35d62e`; M16 killed). The reviewer applied the same fix in an export (67/67 seam tests) |

The round-2 fix is the reviewer's own verified fix. I re-checked it against its callers (`ServerSetRootFilingLookups`,
`PlanRefused`, `CallerMayFileUnder`, task 013's secure-root seam tests), and no third pass was run.

## For UAC-r2 review

1. **New refusal code:** `record_creation.server_set_filing_lookup` (500, `Undecided`, nothing written). It is raised
   when a lineage or `FilingParentOf` lookup appears in `ServerSetLookupColumns`. It runs before step 5's checks,
   including on a create with no mapping, and comes from a server writer's defect, not the caller's input.
2. **A server-set root filing lookup** (work assignment or project, `SecureRootInheritance.FilingColumnsOf`) is asked
   AppendTo as the caller at step 6, BEFORE the plan. A refusal takes the plan's `AccessDenied` shape: the uniform 404,
   with the same body as step 5's. No new code.
   - Task 013's `CreateAsync_WhenTheCallerMayNotFileUnderTheSecureMatter_TheBodyIsTheMissingParents_404` now refuses at
     this check, not in the plan's callback. The refusal, `OwnerDecision.RefusalCode` and body are the same, and the test
     is unchanged.
   - The plan's callback still covers the secure parents reached through an intermediate (an invoice or event).
   - **After merging with 013-r2:** master's new
     `CreateAsync_AssignedToAContactWithASecureMattersId_AndFiledUnderThatMatterByTheServer_IsTheUniformNotFound_AndCreatesNothing`
     (UAC-r2 F-2: the callback's "already checked" set is keyed by (table, id)) is refused by this pre-plan check, with
     the same kind, `OwnerDecision.RefusalCode`, status and body.
     - It still passes, but it no longer drives the callback's keying: its server-set matter is now asked before the
       plan.
     - To keep pinning the keying, it needs a parent reached through an intermediate whose id equals a lookup the caller
       was asked about. UAC-r2 decides.
3. **D7:** `sprk_document` sets `MappingProtectedColumns = { sprk_accessinheritance }`. A create-time rule targeting it
   is skipped with a per-rule warning. `sprk_accesspermission` stays fill-only. Both are tested must-fire and
   must-not-fire. The push side is task 040, and `FieldMappingEndpoints.cs` is untouched.
4. **Never mapped:**
   - an inheriting root's access record (`sprk_accessinheritance`) and secure flag;
   - every server-owned, autonumber and creator column;
   - every server-set lookup;
   - the regarding pair and resolver columns.
5. **Mapped filing lookups refuse on AppendTo** (FR-08 amendment). Filing means a lineage lookup, an ownership parent
   (`FilingParentOf`), or a root's own filing column.
   - Open (review K-2): a mapped filing lookup that fails field security also refuses (403), where FR-08 would drop a
     mapped value. The owner confirms.
   - Open (review K-5): a mapped lookup to an ownership-parent table in a column outside those lists drops rather than
     refuses. None exists on the registered tables.
6. **Condition (b) holds:**
   - the caller's own values are checked alone first, so a caller refusal is byte-identical with or without mapping;
   - with no mapping, step 5 is task 013's single check;
   - the owner decision and post-create order are untouched, and the 013 seam suites pass unchanged.

## For ebilling-r1

1. **Item 1 — the SAME merged local.** The pre-write rules (5½), the plan and the create read the same `item`. It is
   reassigned after step 3 and again after step 5's drops, so a dropped mapped lookup is in none of them. Test:
   `ThePreWriteRuleSeesExactlyTheMergedItemTheCreateWrites_WithEveryMappedLookupsEntitySet` (M8 killed).
2. **Item 2 — every mapped lookup has a `RelatedEntitySet`.** The merged item is mapped as the caller by
   `DataverseWriteItemMapper`. Same test (`OnlyContain(l => RelatedEntitySet not empty)`).
3. **Item 3 — the invoice family's core lookups are mapping-protected** on `sprk_invoice` and `sprk_billingevent`:
   `sprk_matter`, `sprk_project` and `sprk_regardingworkassignment`. Tests: the registry theory for both tables, and
   `AnInvoice_NeverMapsASecondCoreLookup_ButStillFillsANonCoreColumn` (must-fire and must-not-fire).
4. **Other points:**
   - Pre-write rules are NOT widened to updates.
   - `RecordCreationServicePreWriteRuleSeamTests.cs` was not created.
   - When the RegardingTrio tables (invoicesubmission, invoiceapproval, externalidentifier, invoicecorrection) register
     in `RecordTableRules`, they need the same `MappingProtectedColumns`. They are not on this branch.

## OWNER items

1. **A system principal with a mapping is an argument error** (decision 4). Task 033 adds the app-only metadata reader
   with the first system creator.
2. **Response shape (017/018).** Both responses carry `warnings` as strings today: the child-record route's
   `{ id, warnings }` and `QuickCreateResponse.Warnings`. `RecordCreationOutcome.Warnings` fits both.
   `MappingWarnings` (code and column) has no field in either response, so 017/018 decide whether to add one. No
   contract was changed here.
3. **018:** the Office Project-under-Matter create must take the pair from the caller (or derive it from the filed-under
   parent) BEFORE mapping. `ApplyFillOnly` never writes the pair (task 002).
4. **Source field security (review K-4).** The source is read app-only after a record-level Read probe, so a
   field-secured source column is copied into a row the caller can see. This is task 003/030's documented residual;
   014 widens it to every create path and to a caller-chosen `mappingSource`. Recommend filing an issue or getting an
   owner decision before 017 re-points callers. Not filed from this task.
5. **A mapped filing lookup that fails field security** refuses (403, fail closed). The owner confirms refuse or drop.
   The 403 text ("from chat") is `CallerWriteAuthorization`'s, shared since 013 (017).
6. **Office default contact order (review K-7)** belongs to task 015 or 018.
7. **On merge:** close #1617 (mapped lookup AppendTo) and #1614 (Project AppendTo on the merged payload). For #2124
   (DI-001), the Money wiring is in place but unobservable on the Web API path; verify it on the SDK path in 018 or
   close it with this note.
8. **(K4)** The engine's warnings carry no column and use `mapping_rule_skipped`, including its "left blank" note for
   Concat/Template. Leave as is unless 017 surfaces codes per rule.

## POML acceptance criteria

| # | Criterion | Result |
|---|---|---|
| 1 | A mapped lookup without AppendTo is not written: a warning with its code and column, and the create succeeds (#1617) | Met |
| 2 | A caller's typed lookup without AppendTo still refuses with the uniform 404/403, unchanged | Met: the body is byte-equal with and without mapping |
| 3 | A mapped FILING lookup without AppendTo refuses with no create call; must-fire on a Secure parent; must-not-fire drop control | Met: event lineage on a secure matter, the work assignment root filing lookup, and the #1617 drop control |
| 4 | Field security: a mapped value is dropped with a warning; a caller value refuses | Met |
| 5 | A Project under a non-secure parent is AppendTo-checked on the merged payload (#1614) | Met. It also passes on 013, which already asks the pair's record; the test pins it with mapping requested |
| 6 | The caller's value wins (fill-only), and an unsupplied mapped column is filled in the same create | Met: one create call, no follow-up update |
| 7 | Denied and missing source: no mapped values and ONE identical warning, indistinguishable | Met: F-2 fixed, so the test now discriminates |
| 8 | A mapped check exception drops the value; a caller check exception refuses with no create | Met |
| 9 | The seeding proof is recorded | Met (M1) |
| 10 | BFF build has 0 errors; the named suites pass unchanged; publish, CVE and conflict-check are recorded | Met |
| 11 | `moneyAttributes` is passed from metadata; tested; then close #2124 | Partly: it is passed and the body is tested, but the Money form cannot be observed on the Web API path (M12 equivalent). #2124 goes to the coordinator (OWNER 7) |
| 12 | `ApplyFillOnly` never writes the pair; `OfficeCreate_*` and `OfficeQuickCreate_*` are unchanged and pass | Met |
| 13 | D7: accessinheritance is never mapped at create (must-fire), accesspermission stays fill-only (must-not-fire); push is 040's | Met at create. UAC-r2 review is pending |

POML step 7 (TASK-INDEX) was not done: the coordinator owns `TASK-INDEX.md`, `current-task.md` and the POML status.

## Fix round 3 (UAC-r2 review)

Branch `task/fm-014-r3`, cut from `58d1a3548d`. Commits: `faf7a373f4` (F-A), `584f06cb14` (F-B and K-1).

### Scope change (coordinator, mid-round)

The DI-004 / #2188 mapping half (read the source as the caller) was **held**. `IDataverseUserClient` returns Web API
JSON, `CreateTimeFieldMapping` needs a typed SDK `Entity`, and there is no OBO SDK client. The agreed design is a shared
caller mask: keep the app-only typed read, read the mapped columns once as the caller, drop any column that is non-null
for the app but null for the caller, and make a caller-read fault `Unavailable`. It lands on master first, as a helper
under `Services/Dataverse/FieldMapping/` (the Office hotfix), and step 3 reuses it afterwards. Step 3's source read and
`CreateTimeFieldMapping` are **unchanged** in this round. DI-004's mapping half is pending the shared caller-mask helper
(Office hotfix first), and it still blocks 017. No read-as-caller code was written, so there was nothing to revert.

### F-A — the plan callback's (table, id) key

- 013-r2's `CreateAsync_AssignedToAContactWithASecureMattersId_AndFiledUnderThatMatterByTheServer_...` is refused by
  014's pre-plan check, so it never reaches the callback. Its summary and because-text now say so.
- New: `CreateAsync_AssignedToAContactWithASecureMattersId_AndFiledUnderAnInvoiceOfThatMatter_IsTheUniformNotFound_AndCreatesNothing`
  (secure-root seam). The work assignment is filed under an invoice of a secure matter and assigned to a contact with
  that matter's id. Nothing is server-set. Only the callback asks the matter's AppendTo.
- It passes at HEAD and fails under mutant MA (an id-only `checkedRecords` key and `Contains`).

### F-B — one definition of "filing"

- `IsFilingLookup(table, MappedLookup)` is the old column lists OR `FilesUnderOwnershipParent`. That method CALLS
  `OwnedChildWrite.ParentsOf` on a one-lookup item; it does not copy its predicate.
- It is a union: a lineage-only lookup (a message's thread) stays filing, as the existing unit theory pins.
- Server-set, on any table except a work assignment or project: refused with `record_creation.server_set_filing_lookup`.
- Server-set, on a work assignment or project: `ServerSetRootFilingLookups` asks AppendTo before the plan.
- Mapped: `CheckMergedPayloadAsync` treats it as filing. A failed AppendTo refuses (uniform 404) rather than dropping
  the value. This closes K-5.
- The plan's filing columns are unchanged: #2190 is UAC's.

### K-1 — the guard moved before step 3

It is now evaluated after step 2, on the caller's item. Server-set columns are protected from mapping, so the caller's
item has every server-set lookup. A refused create reads no profile and no source. The step record is
`CallerChecks / Refused`. The step-5 call site is removed.

### K-4 — not changed (owner / 017)

The "from chat" text and the technical 500 detail stay with the owner and task 017.

### Tests

| File | Test | Kind |
|---|---|---|
| `RecordCreationServiceSecureRootSeamTests.cs` | `..._FiledUnderAnInvoiceOfThatMatter_...` | F-A must-fire (pins the key) |
| `RecordCreationServiceMappingSeamTests.cs` | `AWorkAssignmentsRegardingCommunicationMarkedServerSet_IsAskedAppendToBeforeThePlan_AndRefused` | F-B must-fire (root, server-set) |
| same | `AMappedWorkAssignmentRegardingCommunicationTheCallerCannotAppendTo_IsRefused_NotDropped` | F-B must-fire (mapped) |
| same | `AChildsOwnershipParentLookupNoListNames_MarkedServerSet_IsRefusedBeforeMappingReadsAnything` | F-B must-fire (child, `sprk_invoice.sprk_regardingworkassignment`) and K-1 |
| same | `ACitationLookupMarkedServerSet_IsNotAFilingLookup_AndPassesTheGuard` | must-not-fire (citation) |
| same | `AWorkAssignmentsAssignedToMarkedServerSet_IsNotAFilingLookup_AndCostsNoAppendTo` | must-not-fire (`sprk_assignedto`) |
| `RecordCreationMappedValueTests.cs` | `IsFilingLookup_OfALookup_IsTheListsOrTheOwnerDecisionsOwnTest` (6 cases) | both |

The shared `ScriptedUserClient` gained one metadata row: `sprk_workassignment.sprk_regardingcommunication`.

**Fail-before.** The pre-fix production files (`58d1a3548d`) were run with a one-line column-only shim, so the new unit
overload compiled. Five tests fail: the three seam must-fire tests and the two unit must-fire cases. All must-not-fire
tests pass. The F-A test passes pre-fix, as it should: it pins existing behaviour, and MA kills it.

**Mutants** (each applied alone, built, run, then restored; the tree was clean afterwards):

| Mutant | Change | Killed by |
|---|---|---|
| MA | callback key by id only | the F-A invoice test |
| MB2 | `FilesUnderOwnershipParent` ignores citations | citation seam test, unit `sprk_signal` case |
| MB3 | every lookup files | both `sprk_assignedto` controls, citation, the #1617 drop and mapped-fault tests |
| MB4 | root server-set uses the lists only | the WA server-set communication test |
| MB5 | mapped filing uses the lists only | the WA mapped communication test |
| MB6 | child server-set uses the lists only | the invoice test |
| MK1 | the guard back at step 5 | the invoice test (profile and probe were read) |

### Adversarial review (one opus pass, fix diff plus direct callers and callees)

No F-class finding.

| # | Finding | Class | Disposition |
|---|---|---|---|
| R3-1 | The server-set sites compare columns differently. `CallerWriteAuthorization` and the plan callback use the set's own comparer on raw strings; the guards and `ProtectedFromMapping` trim and ignore case. This can only over-refuse (500), never let something through | K1 | No production caller sets `ServerSetLookupColumns` yet. For 017/018: normalise once (trimmed, lower-case, case-insensitive) where the request is built |
| R3-2 | On a work assignment or project, a server-set filing lookup can be the implicit mapping source, so step 3 probes and reads it before step 6 asks AppendTo. The probe is as the caller, missing and denied are one outcome, step 6 still refuses with a 404, and nothing is written | K2 | K-1 holds for every other table. Moving step 6's pre-plan AppendTo before step 3 would reorder it ahead of step 5's refusals, which is not trivial. OWNER/UAC |
| R3-3 | #2190: the plan's filing columns omit `sprk_regardingcommunication`, so a WA under a secure communication gets step 7's 409 | K3 | UAC-owned, already filed; not changed here |

### Checks

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- ArchTests: 1214 passed.
- The required suites, run as one filter (RecordCreation*, SecureRoot*, MappingSource*, FieldMapping*, Office*,
  SecureChildOwnershipAiToolTests*, RefileWriterAfterRefileGuardTests, RouteAuthorizationGuardTests) on `584f06cb14`:
  2019 passed, 7 skipped, 0 failed.
- The three directly affected classes: 84 passed. Before the fix, 5 of them fail.
- No publish-size or package change: two production files, no new package, endpoint or registration.

**§11 (new surface).** `IsFilingLookup(string, MappedLookup)` and the private `FilesUnderOwnershipParent` are new
methods in an existing file.
1. Existing: `IsFilingLookup(string, string)` and `OwnedChildWrite.ParentsOf`.
2. Extension: they extend the first by calling the second; no new definition.
3. Cost of doing nothing: a server-set or mapped `sprk_regardingcommunication` files a work assignment under a
   communication with no AppendTo asked.

rework: fix-round-3 task 014
