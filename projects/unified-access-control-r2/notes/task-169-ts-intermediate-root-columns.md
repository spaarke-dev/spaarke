# Task 169: the TypeScript stamp derivation mirrors task 156's intermediate table

> GitHub #1108 · owner round 8 (156 item 13a) · branch `task/uac-r2-169` · base `5903ca957`
> (= `task/uac-r2-156-c1-r2` + merge of `work/unified-access-control-r2` @ `8166dd9ea`; the merge was clean).
> Rigor FULL (TEST-MODIFYING). Model tier opus @ high. Steps directional.

## What changed

`PolymorphicResolverService.deriveCoreAncestorStamps` (shared client library `@spaarke/ui-components`, used by the
RegardingResolver PCF) now derives a child's FR-26 stamp from **the same table the server uses**. Before this task it
read only the four `sprk_regarding{core}` columns, and only for the six CHILD entities.

- **New literal table `INTERMEDIATE_ROOT_COLUMNS`** (30 rows, plus the row type `IIntermediateRootColumn`), exported from
  `PolymorphicResolverService.ts` only. The `services/index.ts` barrel is unchanged.
- **Derivation.** It reads its rows ONLY from that table, through the private `intermediateRootColumnsFor` filter.
  - A CORE target is unchanged.
  - A target that is not an intermediate → `unclassified`, with no read and no metadata call.
  - An intermediate target → one metadata presence check, then ONE `$select` of the root columns present on it.
  - Stamps are keyed by ROOT ENTITY, and each is written on the child's `CORE_ANCESTOR_LOOKUPS` column for that root.
  - Two columns of one root type naming DIFFERENT records → `error`, no stamps. The error text names the target, the
    root type and both columns, but never the two root ids.
  - The same record named twice (compared as `cleanGuid`, so braces and case are ignored) → one stamp.
  - A root with no `CORE_ANCESTOR_LOOKUPS` entry → `error` (unreachable while the closure test holds).
- **Unchanged, byte for byte:** `CORE_RECORD_ENTITIES`, `CHILD_RECORD_ENTITIES`, `CORE_ANCESTOR_LOOKUPS`,
  `isCoreRecordEntity`, `isChildRecordEntity`. `Taxonomy_MatchesTheTypeScriptSide` is green and unedited.
- **Consumers are unchanged.** Manual pick still writes and stages nothing on `error`. Auto-detect still stages no stamp
  and never blocks the host save (FR-24). `buildTodoRegardingUpdate` still throws.
- **Two C# lock-step tests** in `CoreAncestorResolverTests`:
  - `IntermediateRootColumns_MatchTheTypeScriptSide` parses the TS table. It fails if the body contains a spread or a
    comment, or if the `{` count differs from the parsed-row count. It also fails if anything other than whitespace and
    commas remains once the parsed rows are removed (a referenced constant or a helper call). It then asserts set
    equality with the flattened C# table, compared in lower case, with equal counts and no duplicates.
  - `CoreAncestorLookups_MatchTheTypeScriptSide` pins the root → stamp-column pairs.
- **`CoreAncestorResolver.cs`: doc comments only.** The "client mirror still derives only the four columns" remark is
  replaced, and the `IntermediateRootColumns` summary now says the TS table must equal it. No compiled member changed
  (verified: the diff has no non-`///` changed line).
- **RegardingResolver PCF 1.5.0 → 1.5.1.** The version changed in the manifest, the footer `CONTROL_VERSION`,
  `solution.xml`, the Solution `ControlManifest.xml` and `pack.ps1`; `pack.ps1` was stale at 1.4.9. `BUILD_DATE` is
  `2026-10-04`. `npm run build:prod` succeeded. `bundle.js` and `ControlManifest.xml` were copied from
  `out/controls/RegardingResolver/`; `styles.css` was byte-identical. The bundle is 76,256 B, against 72,799 B committed
  (+3.4 KB: the table plus the new branch).

## Step 0 records

### The merged C# table: the 30 rows match the POML background exactly

Read from `CoreAncestorResolver.IntermediateRootColumns` on the merged branch (`:154-173`; `sprk_document` is the
projection of `Spaarke.Dataverse.DocumentLinkFields.All` filtered to matter / project / work assignment):

| intermediate | column → root |
|---|---|
| `sprk_communication`, `sprk_event`, `sprk_todo`, `sprk_analysis` (each) | `sprk_regardingproject` → project; `sprk_regardingmatter` → matter; `sprk_regardingworkassignment` → work assignment; `sprk_regardingservicerequest` → service request |
| `sprk_invoice` | `sprk_project` → project; `sprk_matter` → matter |
| `sprk_document` | `sprk_matter`, `sprk_relatedmatter` → matter; `sprk_project`, `sprk_relatedproject` → project; `sprk_workassignment`, `sprk_relatedworkassignment` → work assignment |
| `sprk_agreement`, `sprk_reportcard` | `sprk_regardingmatter` → matter; `sprk_regardingproject` → project |
| `sprk_budget` | `sprk_matter` → matter; `sprk_project` → project |

The `ResolveStampsAsync` rule (`:439-570`) is as described in the POML, so no behaviour difference was found and
escalation trigger 4 did not fire. The C# table is still public and fully expressible as literal rows, so trigger 2
did not fire.

### The consumer grep: only the RegardingResolver PCF calls the derivation (trigger 3 did not fire)

Searched `deriveCoreAncestorStamps|buildRegardingSelectionPayload|buildTodoRegardingUpdate` over `src`, excluding
node_modules, dist, out and `__tests__`. Runtime callers:

- `src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts:378` (manual pick)
- `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx:1186` (subgrid auto-detect)

`buildTodoRegardingUpdate` is exported through the barrel but has no runtime caller. The other hits are doc comments:
`TodoDetail/types.ts` and `CoreAncestorResolver.cs`. This is the same list as at authoring.

### The bundling grep

Searched `PolymorphicResolverService|TodoRegardingUpdateBuilder` over `src`, excluding node_modules, dist, out,
Solution, bin, obj and `__tests__`. Client packages that bundle or reference the module, none of which calls the
derivation:

- **PCFs:** CommunicationConnections, VisualHost.
- **Code pages and solutions:** EventDetailSidePane, SmartTodo, Notepad, NavigatorPane, SpaarkeAi, CommunicationPage.
- **Shared packages:** Spaarke.Communication.Components (`ConnectionsWriteHandler.ts`), Spaarke.AI.Widgets
  (`CreateAnalysisWizardWidget.tsx`), and in Spaarke.UI.Components the Create*Wizard services, AssociateToStep,
  TodoDetail, LookupField, XrmDataverseClient, the FieldMapping services, EntityCreationService and the adapters.
- **Not bundlers:** `sprk_todo_regarding_presave.js` and `sprk_regardingrecordnumber_hyperlink.js` only mention the
  name in comments. The office-addins `cleanGuid.ts` and the three BFF `.cs` files are comments or name mentions.

Their behaviour does not change, so none needs a redeploy for this task; they pick the table up on their next routine
rebuild.

### The live count for trigger 1 (read-only): 0, so trigger 1 did not fire

Dataverse MCP SQL on the connected dev org, 2026-10-04:

```sql
SELECT sprk_documentid, … FROM sprk_document
WHERE sprk_relatedmatter IS NOT NULL OR sprk_relatedproject IS NOT NULL OR sprk_relatedworkassignment IS NOT NULL
```

This returned **0 rows** out of **542** `sprk_document` rows in total. No document sets any `sprk_related*` root link, so
the ambiguity rule refuses no existing document. This matches the 156 note (2026-10-02).

### Task 147: not landed

TASK-INDEX shows 147 🔲, and the merged branch carries RegardingResolver 1.5.0. This task therefore took **1.5.1**.
Whichever of 147 and 169 lands second rebases onto the other and takes the next patch version.

### Conflict check: soft warn, no line overlap

`/conflict-check` against 25 open PRs found one overlap: **PR #1121** (`chore/shared-building-blocks`). It edits:

- `PolymorphicResolverService.ts`: it moves `cleanGuid` to `utils/guid.ts` and keeps an `import` plus re-export under
  the same name.
- `RegardingResolverApp.tsx`: hunks at lines 665-830 only.
- `ResolverWriteHandler.ts`.

None of its hunks touches the lines this task edits (the table, the derivation at about line 1100, `BUILD_DATE` at line
311). The new code calls `cleanGuid` by name, so it keeps working whichever lands first. No other open PR touches these
files.

## Known difference that this task KEEPS

When metadata answers with an EMPTY column list, the client says `error` and the server says `NoAncestor`. The reason
is that `discoverNavProps` returns `[]` both for "succeeded with nothing" and for "failed". The client is the stricter,
fail-closed side, so it was not "aligned" by weakening it. This is recorded in the function JSDoc and in
`phase3-derivation-rules.md` §3.

## Pre-existing test expectations: none edited (trigger 5 did not fire)

Every pre-existing test in the four affected test files passes unedited:

- `PolymorphicResolverService.coreAncestor.test.ts`: 25 pre-existing tests, plus 25 new.
- `TodoRegardingUpdateBuilder.test.ts`: not edited. Its report-card target now takes a read and lands on `no-ancestor`
  instead of `unclassified`; the payload is the same and the builder only throws on `error`.
- `ResolverWriteHandler.test.ts`: one FIXTURE line was widened, and no expectation changed. The `makeWebApi` fake
  recognised an ancestor read only by `$select=_sprk_regarding`. It now matches any `$select=_sprk_*_value`, because a
  budget or document target's root columns are typed (`_sprk_matter_value`, `_sprk_relatedmatter_value`).
- `RegardingResolverApp.test.tsx`, `regardingPresave.test.ts`: not edited.

## Tests added (closed set from the acceptance criteria)

**Library jest: `PolymorphicResolverService.coreAncestor.test.ts`, 25 new tests.**

- **Table closure (4):**
  - every CHILD entity is an intermediate;
  - no CORE entity is an intermediate;
  - every root is CORE and has a `CORE_ANCESTOR_LOOKUPS` entry;
  - no (intermediate, column) pair appears twice.
- **Typed roots:**
  - invoice `_sprk_matter_value` → one matter stamp on `sprk_regardingmatter` / `sprk_matters`;
  - budget `_sprk_project_value` → one project stamp;
  - document with only `_sprk_relatedmatter_value` → one matter stamp on `sprk_regardingmatter`, never on
    `sprk_relatedmatter` or `sprk_matter`;
  - an invoice with both values → two stamps, one per root.
- **Agreement and report card:** `_sprk_regardingproject_value` → one project stamp each, and `isChildRecordEntity`
  stays false.
- **The read:** for a document, exactly the six root columns are selected. The metadata stub also lists `sprk_invoice`,
  `sprk_relatedservicerequest`, `sprk_regardingmatter` and `ownerid`, and none of those is selected. There is exactly one
  ancestor read.
- **Ambiguity:**
  - three cases: matter, project and work assignment pairs that differ → `error`, `stamps: []`;
  - the error text names the target, the root type and both columns;
  - `buildRegardingSelectionPayload` → `success: false`, no payload.
- **Dedupe:** the same lettered matter id, once braced and upper-case and once bare → one stamp.
- **Budget fails closed (5):**
  - discovery fails → `error` (no read);
  - the read throws → `error`;
  - no row → `error`;
  - metadata lists only `ownerid` → `no-ancestor` with no ancestor read;
  - both root values null → `no-ancestor`.
- **Unclassified:** `sprk_organization`, `contact` and `account` → `unclassified`, with no read and no metadata call
  (the call is asserted through a `jest.fn` wrapper around the existing `metadataFetchStub`; there is no second stub).

**PCF jest: `ResolverWriteHandler.test.ts`, real derivation through the source mapping, 3 new tests.**

- A budget pick with `_sprk_matter_value` on an existing host to-do → ONE `updateRecord`, carrying
  `sprk_RegardingBudget` and `sprk_RegardingMatter@odata.bind = /sprk_matters({id})`.
- A document naming two different matters, existing host → `success: false`, `ancestorStatus: 'error'`, no
  `updateRecord`, no payload.
- The same with a new host (no GUID) → nothing is returned for staging (`payload`, `ancestorStamps` and `clearLookups`
  are all undefined).

**C# (2):** `IntermediateRootColumns_MatchTheTypeScriptSide` and `CoreAncestorLookups_MatchTheTypeScriptSide`.

## Seeds: every guard bites

Each seed was applied to `PolymorphicResolverService.ts`, the named tests were run, and the file was then restored
byte-for-byte and touched. The SHA-256 after every seed was `16d29699…87bd`, identical to the pre-seed file. The runner
is the scratch script `seed169.py`; the C# seeds ran `--no-build`, because the tests read the TS file at run time.

| Seed | Change | Failed |
|---|---|---|
| S1 | different-id check → `if (false)` | 5 jest: `failsClosedWhenADocumentNamesTwoDifferent {matter, project, work assignment} records`, `namesTheTargetTheRootTypeAndBothColumnsInTheAmbiguityError`, `refusesToBuildAPayloadForADocumentNamingTwoDifferentMatters` |
| S2 | dedupe dropped (`find` also requires a different id, so the same record twice yields two stamps) | 1 jest: `stampsOnceWhenADocumentNamesTheSameMatterTwiceInDifferentSpellings` |
| S3 | `isChildRecordEntity` gate restored before the table lookup | 8 jest: `derivesABudgetsProjectFromItsTypedLookup`, `derives{sprk_agreement,sprk_reportcard}ProjectFromItsRegardingProjectWithoutBecomingAChild`, all 5 budget fail-closed tests |
| S4 | stamp `lookupAttribute: r.column` (the intermediate's own column) | 5 jest: `stampsADocumentsRelatedMatterOnTheChildsRegardingMatterColumn`, `derivesAnInvoicesMatterFromItsTypedLookup`, `derivesABudgetsProjectFromItsTypedLookup`, `derivesOneStampPerRootTypeWhenAnInvoiceNamesBothAMatterAndAProject`, `stampsOnceWhenADocumentNamesTheSameMatterTwiceInDifferentSpellings` |
| S5 | one TS row deleted (budget → project) | `IntermediateRootColumns_MatchTheTypeScriptSide` ("Expected tsRows to contain 30 item(s), but found 29") |
| S6 | one TS row added (budget → work assignment) | `IntermediateRootColumns_MatchTheTypeScriptSide` ("… but found 31") |
| S7 | one row's rootEntity changed (budget `sprk_project` → `sprk_matter`) | `IntermediateRootColumns_MatchTheTypeScriptSide` (row `Item3` "sprk_project" vs "sprk_matter") |
| S8 | literal rows kept, plus a referenced value `EXTRA_INTERMEDIATE_ROW,` | `IntermediateRootColumns_MatchTheTypeScriptSide` (the shape guard: the remainder after the parsed rows is not empty) |
| S8b | literal rows kept, plus a spread `...EXTRA_INTERMEDIATE_ROWS,` | `IntermediateRootColumns_MatchTheTypeScriptSide` (the spread guard: "Did not expect body … to contain '...'") |
| S9 | constant renamed to `INTERMEDIATE_ROOT_COLUMN_TABLE` | `IntermediateRootColumns_MatchTheTypeScriptSide` ("INTERMEDIATE_ROOT_COLUMNS must exist in the TypeScript resolver") |
| S10 | `CORE_ANCESTOR_LOOKUPS` project `lookupAttribute` → `sprk_regardingprojectx` | `CoreAncestorLookups_MatchTheTypeScriptSide` |
| S11 | a commented-out row inside the array literal | `IntermediateRootColumns_MatchTheTypeScriptSide` (the comment guard: "Did not expect body … to contain '//'") |

In every C# seed the two other `*TypeScriptSide` tests stayed green (3 run, 1 failed), so each guard is specific.

## Suite results

| Suite | Passed | Skipped | Failed | Notes |
|---|---|---|---|---|
| Library jest, the two affected files (`PolymorphicResolverService.coreAncestor.test.ts` + `TodoRegardingUpdateBuilder.test.ts`) | 84 | 0 | 0 | 50 in the coreAncestor file (25 pre-existing + 25 new) |
| Library `npm run build` (`tsc`) | ✅ | | | Needed the sibling packages `Spaarke.Auth` and `Spaarke.SdapClient` built first in this fresh worktree (their `dist/` is not committed) |
| Library `npm run lint` | — | — | 5 errors | All 5 pre-existing, in files this task does not touch (see Quality gates); `eslint` on the 2 changed library files is clean |
| Library jest, FULL (`npm test`) | 3384 | 0 | 15 | See below: 12 pre-existing, plus 3 from contention |
| RegardingResolver `npm run build:prod` | ✅ | | | bundle 76,256 B |
| RegardingResolver jest, FULL (3 suites: `ResolverWriteHandler`, `RegardingResolverApp`, `regardingPresave`) | 109 | 0 | 0 | |
| BFF `CoreAncestorResolverTests` (affected class) | 43 | 0 | 0 | |
| BFF unit, FULL (`tests/unit/Sprk.Bff.Api.Tests`) | 14401 | 54 | 0 | 18 m 10 s |
| NetArchTest (`tests/Spaarke.ArchTests`) | 346 | 0 | 0 | |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | 104 | 0 | 0 | |
| `tests/integration/Spe.Integration.Tests` | 403 | 25 | 0 | |

**The library's full-suite failures: none is new, and none is in a file this task touches.**

- **The comparison.** I re-ran the 10 failing suites in isolation, twice: once with the BASE commit's
  `PolymorphicResolverService.ts` swapped in (then restored, with the hash checked), and once with this task's version.
  Both runs gave the **same 12 failures in the same 7 suites**; the `diff` of the failure lists is empty.
- **The 12 pre-existing failures (7 suites):**
  - `surfaceLaunchRegistry` ×2
  - `todoScoreMappings` (a locked-file sha256 check)
  - `buildDynamicWorkspaceConfig` ×1
  - `RecordHeader/configResolution` ×2 (a module-purity source scan)
  - `ConversationView.forward` ×1
  - `TimelineComposeBox` ×3
  - `RichFilePreview` ×2
- **The other 3 failures were contention.** `AccessGrantModal.grantOutcome`, `ConversationView.emailInFlow` and
  `RecordNavigationModalShell` failed only in the full parallel run and PASS in isolation, with both the base and the
  task version.

## Quality gates (Step 9.5)

**Code review: no Critical findings.**

**Metrics:**

| File | Lines (before → after) | Signal |
|---|---|---|
| `PolymorphicResolverService.ts` | 1263 → 1402 | grew 11% |

- The growth is one 30-row literal table plus its JSDoc, and the derivation's stamp loop (about +40 lines).
- `deriveCoreAncestorStamps` estimated complexity rose by about 5, from three new fail-closed exits.
- The module was already large. It stays cohesive (the resolver service) and has no new responsibility.

| Severity | Finding | Confidence |
|---|---|---|
| Warning | The task POML is tagged `pcf` but has no `<ui-tests>` block (code-review Step 6.7). It predates this task; the acceptance-criteria MANUAL GATE covers the UI check. Not edited here, because this task may only edit `<status>` and `<execution>`. | high |
| Suggestion | `deriveCoreAncestorStamps` is now about 190 lines with 4 fail-closed exits. It is still one responsibility; the stamp loop could become a private helper. Left inline so the C# loop and the TS loop read side by side. | medium |
| Suggestion | The shared library's `npm run lint` exits 1 with 5 errors. All are pre-existing, in files this task does not touch: `invoiceService.ts:401`, `matterService.ts:416`, `workAssignmentService.ts:607` (`no-dupe-else-if`), and `HeaderCellContent.tsx:698,789`. `eslint` over the two changed library files is clean (exit 0). | high |
| Positive | The ambiguity error names the target and both columns but not the two root ids, so it cannot leak an id the user may not be able to read. | n/a |
| Positive | The C# shape guard closes the parser's blind spot (a row the runtime has and the parser cannot see), proven by S8, S8b and S11. | n/a |

There were no AI-smell findings. `IIntermediateRootColumn` is a data shape, not a DI seam. No try/catch was added, and
no null check was made on a non-nullable value.

**ADR check: 0 violations.**

- **Compliant:**
  - **ADR-002:** no plugin; the client is the WP-2 preview of the server invariant, and the C# table stays the source
    of truth.
  - **ADR-003:** every new branch fails closed: ambiguity, no row, a throw, discovery failure, and a root with no
    lookup entry.
  - **ADR-024:** the derivation stays in the shared service. `ResolverWriteHandler.test.ts`'s structural test (no
    derivation identifiers in the handler) is green.
  - **ADR-034:** one read, pinned by `readsExactlyTheDocumentsRootColumnsInOneRead` and the pre-existing one-hop test.
  - **ADR-038:** no `Mock<HttpMessageHandler>`, no DI or constructor-null tests, no second fetch stub, no live Dataverse.
- **Not applicable:** ADR-001/007/008/009/010/013/028/052 (no BFF code), and ADR-006/021/022 (no UI or webresource
  change).
- **Warning, documented path A:** ADR-012 (literal entity names). The project-scoped exception in
  `phase3-derivation-rules.md` §7 "ADR-012 tension 1" is **widened explicitly** to name `INTERMEDIATE_ROOT_COLUMNS`,
  with the same rationale. **Cite it in the PR for code-review approval.**
- **Warning, pre-existing, not introduced:** ADR-002 WP-3. The RegardingResolver writes the to-do through
  `Xrm.WebApi`; the WP-5 reconciliation job covers it.

## Placement and justification (CLAUDE.md §10 / §11)

- **No BFF code is added.** The C# change is two tests plus doc comments
  (`.claude/constraints/bff-extensions.md` §A: there is nothing to place). No endpoint, DI registration, option, job,
  column or package is added, and no NuGet or npm package is added. `package-lock.json` was touched by `npm install`
  and reverted.
- **Publish size:** not measured, per the task brief; the compiled BFF does not change.
- **No route is touched.** `RouteAuthorizationGuardTests` is unaffected: no route, waiver or ledger row.
- **New surface:** `INTERMEDIATE_ROOT_COLUMNS` plus its row type, and a private filter. The POML `<justification>`
  holds:
  - **Existing:** `CORE_ANCESTOR_LOOKUPS` is the child's stamp columns and the pre-clear set, keyed by root. A search of
    src/client for `sprk_relatedmatter` outside node_modules finds no TS copy of the document link vocabulary.
  - **Extension:** extending `CORE_ANCESTOR_LOOKUPS` instead would null a host's own `sprk_matter` / `sprk_project`
    on every pick.
  - **Cost of doing nothing:** a pick under an invoice, document, budget or report card saves unstamped, and the client
    previews a different access answer from the server's, with no failing test.

## `.claude/` edits needed

None.

## Manual gates (main session)

The deploy follows the acceptance-criteria MANUAL GATE, on dev, after this branch merges and after task 156's BFF is
deployed.

1. **Deploy the RegardingResolver PCF 1.5.1** with `/pcf-deploy`: `npm run build:prod` in
   `src/client/pcf/RegardingResolver`, then `pack.ps1` (zip `RegardingResolverSolution_v1.5.1.zip`), then a
   solution-ZIP import. Hard-refresh and confirm the footer reads `v1.5.1 • Built 2026-10-04`.
2. **Probe as the existing non-admin test user** `uac.child.user@demo.spaarke.com` (owner round 11), or another the
   owner names. On a new to-do form, pick through the RegardingResolver:
   - (1) an invoice filed under a matter M the user can read;
   - (2) a budget under M;
   - (3) a document whose `sprk_matter` is M.

   After each save, confirm read-only that the to-do's `_sprk_regardingmatter_value` = M:
   `GET /api/data/v9.2/sprk_todos({id})?$select=_sprk_regardingmatter_value,modifiedon`.
3. **Auto-detect path.** If an invoice form has a to-do subgrid, create a to-do from its "+ New" and confirm the same
   stamp. Otherwise record that there is none.
4. **After the next 5-minute reconciliation run,** confirm that `modifiedon` is unchanged for each probe to-do (the
   client and the server agree).
5. If task 168's form lock is deployed by then, run (1)-(3) on the locked form. If a stamp does not land there, STOP and
   report.
6. Delete the probe to-dos and record the deletion. Record results in this note, with ids redacted to their first 8
   characters.

The redeploy list is the RegardingResolver PCF only. No code page, other PCF or BFF redeploy is required.
