# Task 014 (G4 tables — full tracked structure) — Deviations & Findings

> Written per task 014 POML step 10. Records the design-time OOXML discovery (spec Open-Question) + the one
> scope decision surfaced to the operator (§6.5 path-A style).

## 1. Word-valid tracked-table OOXML ordering (the spec Open-Question, resolved here)

The `table` op emits FULL tracked table structure. Verified Word-VALID by running every patched package through
`OpenXmlValidator(FileFormatVersions.Office2019)` in the seam slice — **zero validation errors** on all six kinds:

- **InsertRow** → new `w:tr` carrying `w:trPr/w:ins`; each new cell paragraph mark marked inserted. Accept keeps the
  row, reject removes it.
- **DeleteRow** → target `w:tr` gets `w:trPr/w:del`; cell content struck (`w:del`/`w:delText`), cell paragraph marks
  deleted. Physical row stays until accept.
- **InsertColumn** → a `w:tc` per row with `w:tcPr/w:cellIns`; the `w:tblGrid` gains a `w:gridCol` AND a
  `w:tblGridChange` records the PRIOR grid (via `PreviousTableGrid`). Grid snapshot is captured BEFORE the new col
  is added.
- **DeleteColumn** → each row's target `w:tc` gets `w:tcPr/w:cellDel` + struck content. The grid is NOT changed on
  delete (physical cells remain until accept) — asymmetric-but-correct vs InsertColumn.
- **SetCellContent** → in-cell `w:del`(old) + `w:ins`(new), reusing `WrapRunAsDeleted`/`BuildRun`.
- **SetTableProps** → live prop set via the SDK typed setters (schema-correct CT_TblPr ordering) + a `w:tblPrChange`
  (never stacks) recording the prior props via `PreviousTableProperties`.

**SDK schema-context types confirmed by compile + validator** (the task-010 `*Extended` gotcha zone): the nested
previous-props under the `w:*Change` elements are `PreviousTableGrid` (w:tblGridChange child) and
`PreviousTableProperties` (w:tblPrChange child); `Inserted`/`Deleted` are valid children of `TableRowProperties`;
`CellInsertion`/`CellDeletion` are children of `TableCellProperties`. No `*Extended` variant was needed for the
table `*Change` elements (unlike `w:pPrChange` → `ParagraphPropertiesExtended`).

## 2. Anchoring (I-7 / NFR-02) — paraId ancestry walk, never text-search

The op's base `paraId` = a `w14:paraId` of a paragraph INSIDE the target table (canonical: first cell's first
paragraph). The applier `Resolve(paraId)` O(1) then walks `para.Ancestors<TableCell>()` → `.Ancestors<Table>()`.
No table-id, no text-search. `ParseTableWidth` was rewritten to parse the `unit:number` value via `Split` +
equality (NOT `StartsWith`) so the `ComposeWritePathTextSearchAuditTests` lexical ban (`.StartsWith(`/`.IndexOf(`/
`.Contains(`/`.EndsWith(`) stays green.

## 3. SCOPE DECISION (surfaced to operator — §6.5 path A, documented not silent)

Task-004's closed table-op catalog is **6 kinds = structural EDITS of an EXISTING table** (InsertRow/DeleteRow/
InsertColumn/DeleteColumn/SetCellContent/SetTableProps). Whole-table **CREATE** is deliberately NOT a kind (design
§2: a brand-new table on a tracked baseline is a whole-block author, not a structural edit).

Consequence for the toolbar SDL-3 removal:
- The row/column/delete-table + cell-content EDIT commands were already enabled on loaded docs (gated only by
  `controlDisabled` + `editor.can()`), but were **silently dropped** at the interceptor (`defer-structural`). Task
  014 captures them as the `table` op → they now **round-trip** (the real SDL-3 silent-loss fix). Delete-table is
  captured as one DeleteRow per row.
- **Insert-table (a brand-new table) stays gated on loaded docs** (`tableInsertDisabled` unchanged): enabling it
  would emit a step the closed catalog cannot carry → the exact silent-loss NFR-08 forbids. It stays disabled with
  an honest "future release" tooltip (NOT silent loss). Born-in-editor tables remain enabled (renderer authors
  them cleanly).

**Net NFR-08 result:** every currently-ENABLED loaded-doc table command either round-trips (row/col add/delete,
delete-table, cell content) or is cleanly refused; the one out-of-catalog command (insert new table) is disabled,
not silently dropped. This is a documented scope boundary, surfaced for operator awareness — not a hidden skip.

## 4. Publish size + byte-diff + baseline (NFR-01 / NFR-03 / NFR-04)

Release publish (same Python zip @ compresslevel=9 A/B method as tasks 010–013): **47.38 MB excl PDB / 48.23 MB
incl PDB** — well under the 60 MB HARD ceiling and the 55 MB architecture-review threshold. Zero new runtime
package (NFR-03). Absolute delta vs task-012's 45.18 MB excl-PDB is +2.2 MB; the code delta is KB-scale (3 records
+ 3 enums + ~300 lines engine C# + TS) so this is publish tooling / runtime-asset measurement variance (noted in
task-010 deviations), not real code growth, and is under the +5 MB single-task escalation threshold.

Corpus byte-diff **24/24** green (no regression). Full test project: **9275 passed / 5 failed / 101 skipped** — the
5 failures are all pre-existing `Services.Communication.*` (the documented unrelated baseline); the 3 ArchTest
baseline failures live in the separate `Spaarke.ArchTests` project. **Zero NEW failures.** Compose suite fully green
(798 unit/seam incl. 9 new table seam tests + OpenXmlValidator Word-validity + text-search audit + 5-test catalog
mirror). Client: 61 tests green (schema round-trip updated to 13 ops; interceptor unchanged behavior).

## 5. Catalog mirror (ADR-039)

`table` added byte-exact to both ends: server `ComposeOperation.cs` `[JsonDerivedType(..., "table")]` + `TableOperation`
record + `ComposeTableOpKind`/`ComposeTableProp` enums; client `compose-operations.ts` tuple + `TableOperation`
interface + type aliases. `compose-ops-v2` already in place (no version bump needed — 012 already bumped). No new
AI-dispatch endpoint, no side channel (§0 escalation check clean).
