# Op-Schema Extension Design — `table` / `acceptRevision` / `rejectRevision`

> **Task**: `004-op-schema-extension-design` (Phase 0 gate) · **Project**: spaarkeai-compose-r5
> **Rigor**: FULL · **Model tier**: opus · **Status**: design-only (NO catalog code committed here)
> **Author**: task-execute 004 · **Date**: 2026-07-29
> **Spec**: FR-04 (G4 table) · FR-11 (G12 accept/reject) · R5-D3 (extend the closed catalog, never fork) · plan P0-4
> **Gates**: this design is the contract that tasks **012** (G12 single), **013** (G12 batch), **014** (G4 table), **033** (G5 — adjacent, uses the same mirror discipline) implement on both ends.

---

## 0. Summary + escalation result

This note extends the **closed `ComposeOperation` catalog** (ADR-039 / R4 FR-11) with **three new op types** — `table`, `acceptRevision`, `rejectRevision` — as a **byte-exact paired addition** to:

- **server** `src/server/api/Sprk.Bff.Api/Services/Compose/Operations/ComposeOperation.cs` — the `[JsonPolymorphic]` base (`ComposeOperation`) + its `[JsonDerivedType]` list (currently `:148–157`, ten ops).
- **client** `src/client/shared/Spaarke.Compose.Components/src/types/compose-operations.ts` — the `COMPOSE_OPERATION_TYPES` closed tuple (`:54–65`) + the `ComposeOperation` discriminated union (`:230–240`).

**Escalation check (POML `<escalation><trigger>` / ADR-039):** ✅ **NO escalation.** All three ops are expressible entirely inside the existing envelope-only model:
- they extend the **one** `ComposeOperation` polymorphic union (no second union, no fork);
- they are carried in the existing `ComposeOperationLog` envelope and applied by the existing `ComposeShadowPatchEngine.Apply` over the existing `POST /api/compose/save` op-log path;
- **no new AI-dispatch endpoint**, **no side channel**, **no AI-catalog row change** is required (ADR-039 §"MUST NOT introduce a new AI dispatch endpoint"). The AI redline path stays envelope-only and engine-frozen.
- **NFR-02 / I-7 (no text-search):** every new op resolves by `(paraId, runIndex, offset)` anchor or by native OOXML **revision id** — never by content match.

Because none of the three ops needs a mechanism outside the closed catalog, the CLAUDE.md §6.5 conflict paths (A exception / B amendment / C pivot) do **not** fire. This is a clean **catalog extension under version control** — exactly the mechanism ADR-039 / R5-D3 provide for.

---

## 1. The mirror convention (what "paired addition" means)

The two files are **one schema both ends compile against** (server `ComposeOperation.cs` header lines 13–15; client `compose-operations.ts` header lines 8–14). The rules a new op MUST obey to keep the mirror byte-exact:

| Aspect | Server (`.cs`) | Client (`.ts`) | Rule |
|---|---|---|---|
| Discriminator string | `[JsonDerivedType(typeof(XOperation), "x")]` on `ComposeOperation` | literal `'x'` in `COMPOSE_OPERATION_TYPES` **and** `type: 'x'` on the interface | **identical camelCase string** both ends |
| Op record/interface | `public sealed record XOperation : ComposeOperation` | `export interface XOperation extends ComposeOperationBase { type: 'x'; … }` | same field **names** + same JSON shape |
| Field JSON name | `[JsonPropertyName("foo")]` | property `foo` | camelCase, identical spelling |
| Base field | inherits `ParaId` (`[JsonPropertyName("paraId")]`, `required`) | `paraId: string` (from `ComposeOperationBase`) | **every op carries `paraId`** (durable coarse anchor) |
| Enums | `enum ComposeX { A, B }` + `[JsonConverter(typeof(JsonStringEnumConverter))]` → **PascalCase** member-name serialization | `type ComposeX = 'A' \| 'B'` (PascalCase literals) | member names match the enum serialization exactly |
| Union membership | listed in the `[JsonDerivedType]` block | added to the `ComposeOperation` union (`:230`) + the `COMPOSE_OPERATION_TYPES` tuple | both places updated together |

**Two conventions worth calling out** (both already in the catalog, both reused unchanged by these ops):

1. **Enums serialize as PascalCase member names** (`ComposeMarkType.Bold` → `"Bold"`, `ComposeBlockAttr.Alignment` → `"Alignment"`). New enums (`ComposeTableOpKind`, `ComposeTableProp`, `ComposeRevisionScope`) follow this; the client mirrors them as PascalCase string-literal unions.
2. **The "second-paragraph / minted id in the properties slot" precedent** (`splitParagraph.newParaId`, `insertParagraph.newParaId`, `mergeParagraph.targetParaId`). These are durable `w14:paraId`s carried as op **properties**, NOT anchors — task-003 op-shape decision, D2-compliant. `table` insert ops reuse this precedent for the minted paraIds of newly created cells.

---

## 2. `table` op (G4 / FR-04)

**Discriminator:** `"table"`

**Intent:** express **full tracked table structure** edits — insert/delete row, insert/delete column, set a cell's content, set table-level properties — so a loaded doc's table edits round-trip as Word-valid tracked changes (`w:trPr/w:ins`, `w:trPr/w:del`, `w:tcPr/w:cellIns`, `w:tcPr/w:cellDel`, `w:tblPrChange`, `w:tblGridChange`) instead of being silently dropped (SDL-3; the table control stays disabled today).

### 2.1 Anchoring — how a table op stays paraId-anchored (no new anchor mechanism)

OOXML `w:tbl` nodes do **not** carry a `w14:paraId`; only `w:p` do. But every table **cell** (`w:tc`) contains at least one `w:p`, and those cell paragraphs are stamped with `w14:paraId` exactly like body paragraphs — `importedRevisions.collectBlocks` already "descends into table cells (server parity)" (`importedRevisions.ts:123–135`), and the engine's block walk gives cell paragraphs the same ids.

**Therefore the base `ParaId` on a `table` op = the `w14:paraId` of a paragraph *inside the target table*** (canonical: the table's **first cell's first paragraph**). The applier resolves the paragraph O(1) by paraId (existing machinery), then walks the OpenXml ancestry `w:p → w:tc → w:tr → w:tbl` to reach the target `w:tbl`. **No table-id, no new anchor type, no text-search** — the existing durable coarse anchor identifies the table. This is the design decision that keeps `table` inside the closed catalog and inside I-3/I-7.

Row/column **coordinates** (`row`, `column`) address the cell **within** that resolved table (0-based, `w:tblGrid`-column-aligned for horizontally-merged spans — see §2.4 open item).

### 2.2 Fields

| Field | JSON name | Type (server / client) | Required for | Notes |
|---|---|---|---|---|
| (base) paraId | `paraId` | `string` (required) | **all** | A `w14:paraId` of a paragraph inside the target table (canonical: first cell's first paragraph). Locates the `w:tbl`. |
| kind | `kind` | `ComposeTableOpKind` / PascalCase union | **all** | Which structural table edit (closed enum §2.3). |
| row | `row` | `int?` / `number \| null` | InsertRow, DeleteRow, SetCellContent | 0-based row index within the table. `null` for column-only / table-prop ops. |
| column | `column` | `int?` / `number \| null` | InsertColumn, DeleteColumn, SetCellContent | 0-based grid-column index. `null` for row-only / table-prop ops. |
| position | `position` | `ComposeParagraphPosition?` / `'Before' \| 'After' \| null` | InsertRow, InsertColumn | **Reuses** the existing `ComposeParagraphPosition` enum. Insert relative to `row` (InsertRow) or `column` (InsertColumn). |
| newParaIds | `newParaIds` | `IReadOnlyList<string>` / `string[]` | InsertRow, InsertColumn | The minted `w14:paraId`s stamped on the **new cells' paragraphs**, ordered by grid position: InsertRow → one per column (left→right); InsertColumn → one per row (top→bottom). Follows the `splitParagraph.newParaId` minted-durable-id precedent (§1.2). Lets subsequent content ops / SetCellContent target the new cells. Empty `[]` for non-insert kinds. |
| text | `text` | `string?` / `string \| null` | SetCellContent | The cell's new text content (replaces the cell paragraph's content). Tier 3 (document content). `null`/absent otherwise. |
| marks | `marks` | `IReadOnlyList<ComposeMarkType>` / `ComposeMarkType[]` | SetCellContent (optional) | Inline marks on the set content. Empty = inherit. Reuses the existing `ComposeMarkType`. |
| tableProp | `tableProp` | `ComposeTableProp?` / PascalCase union `\| null` | SetTableProps | Which table-level property (closed enum §2.3). |
| value | `value` | `string?` / `string \| null` | SetTableProps | The property value, interpreted per `tableProp`. **Mirrors the `setBlockAttr` `attr`/`value` pattern exactly.** `null` clears to style default. |

> **Cell content editing vs `SetCellContent`.** Ordinary inline edits inside an *existing* cell (typing, delete, mark) already work through the existing `insertText` / `deleteRange` / `replaceRange` / `setMark` ops targeting the **cell paragraph's `paraId`** — no `table` op needed. `SetCellContent` exists for the case a coordinate-addressed whole-cell content set is needed where the caller has a `(row,column)` but not a stable paraId — most importantly **cells freshly created by InsertRow/InsertColumn**, whose paragraphs carry newly-minted paraIds from `newParaIds`. (Component-justification note: this is the concrete non-overlap — without it, a just-inserted row/column's cells cannot be filled in the same op-log without an extra round-trip.)

### 2.3 New closed enums

```
// server — Services/Compose/Operations/ComposeOperation.cs
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeTableOpKind { InsertRow, DeleteRow, InsertColumn, DeleteColumn, SetCellContent, SetTableProps }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeTableProp { Alignment, Width, Borders }   // closed; extend via catalog only
```
```ts
// client — types/compose-operations.ts
export type ComposeTableOpKind =
  | 'InsertRow' | 'DeleteRow' | 'InsertColumn' | 'DeleteColumn' | 'SetCellContent' | 'SetTableProps';
export type ComposeTableProp = 'Alignment' | 'Width' | 'Borders';
```
- `ComposeTableProp` value interpretation (mirrors `setBlockAttr`): `Alignment` → `Left`/`Center`/`Right`; `Width` → `"auto"` \| `"pct:NN"` \| `"dxa:NNNN"`; `Borders` → `None`/`Single`/`Double`. Kept intentionally small + closed; new table props are added by the same catalog-extension process, never by a free-form bag.

### 2.4 Validation rules (`table`)

1. `paraId` present, 8-hex, resolves to a paragraph that has a `w:tbl` ancestor → else `ParagraphNotFound` (or a new `TableNotFound`, see §5) → 422. **No text-search fallback.**
2. `kind` ∈ the closed `ComposeTableOpKind` set → else deserialization fails (unknown enum) → 400.
3. Per-kind required fields present and in range for the resolved table's dimensions:
   - InsertRow: `row` ∈ [0, rowCount], `position`, `newParaIds.length == columnCount`.
   - DeleteRow: `row` ∈ [0, rowCount).
   - InsertColumn: `column` ∈ [0, columnCount], `position`, `newParaIds.length == rowCount`.
   - DeleteColumn: `column` ∈ [0, columnCount).
   - SetCellContent: `row`,`column` in range; `text` non-null.
   - SetTableProps: `tableProp` non-null; `value` valid for that prop.
   - Out-of-range index → `RunIndexOutOfRange`-analogue / a new `TableIndexOutOfRange` (§5) → 422.
4. `newParaIds` entries are fresh, non-colliding `w14:paraId`s (no existing paragraph already owns them) → else `StructuralOperationRefused` → 422.
5. Applier resolves by structure only (ancestry walk + grid index), never by content → NFR-02/I-7 preserved.

### 2.5 OOXML applier mapping (informative — applier is task 014)

Shape-only design here; the exact tracked-OOXML **ordering** is spec Open-Question "G4 tracked-table OOXML ordering" and is a **task-014 design-time discovery** — spec explicitly notes it "does not block the op-schema addition." Indicative mapping the applier will emit (imported/tracked mode):
- InsertRow → new `w:tr` with `w:trPr/w:ins`; cells' content as `w:ins`.
- DeleteRow → `w:trPr/w:del` on the target `w:tr`.
- InsertColumn → a `w:tc` per row with `w:tcPr/w:cellIns`; `w:tblGrid` updated + `w:tblGridChange`.
- DeleteColumn → `w:tcPr/w:cellDel` on each row's target `w:tc`.
- SetCellContent → in the target cell's `w:p`: existing runs → `w:del`/`w:delText`, new text → `w:ins` (reuses `ApplyInsertText`/`WrapRunAsDeleted`).
- SetTableProps → new `w:tblPr` wrapped by `w:tblPrChange` recording prior props.

In **clean-apply (G2 authored)** mode the same op emits untracked structure (no `w:ins`/`w:del`) — clean-vs-tracked is an engine-mode concern (G2/FR-02), **not** a per-op field.

---

## 3. `acceptRevision` / `rejectRevision` ops (G12 / FR-11)

**Discriminators:** `"acceptRevision"`, `"rejectRevision"`

**Intent:** reconcile **imported** Word tracked changes (`w:ins`/`w:del` recovered on Load as `ImportedRevision`, `compose-contracts.ts:319`) so accepting/rejecting one and saving no longer 422s with `TrackedChangeReconciliationUnsupported` (ET-2). **Single-by-id AND accept-all/reject-all batch** (owner selected both, spec FR-11 / Open-Question Q5).

### 3.1 Addressing — by revision **id**, never offset/content (I-7 / NFR-02)

An `ImportedRevision` carries `id` = the native OOXML `w:ins`/`w:del` `w:id` (`compose-contracts.ts:322–323`) plus its containing paragraph `paraId` (primary anchor). The accept/reject op addresses the revision by that **id**, scoped to its paragraph for O(1) resolution:

- **Single** (`scope: Single`): `revisionId` = the native `w:id` (**required**); base `paraId` = the revision's containing paragraph (`ImportedRevision.paraId`). The applier resolves the paragraph O(1), then finds the `w:ins`/`w:del` whose `w:id` matches `revisionId` within it. **No document scan, no text-search.**
- **All** (`scope: All`): `revisionId` = `null`; the op applies to **every** tracked revision in the document.

### 3.2 The batch-anchor decision (base `ParaId` is `required` — how batch honors it)

Every op in the catalog carries a **required, real** `w14:paraId` (base contract, `ComposeOperation.cs:166–167`). A document-wide accept-all does not have a single target paragraph. Two shapes were considered:

- **(rejected) paraId sentinel** (e.g. `"*"`): keeps batch semantics clean but **breaks the field-shape invariant** "`paraId` is always a real 8-hex `w14:paraId`" and weakens the `isComposeOperation` guard's meaning.
- **(chosen) presence-anchor**: an `All`-scope op still carries a **real, resolvable** `paraId` — the paraId of the **first paragraph (document order) that contains a tracked revision**. The client already has this (`ImportedRevision[]` with `paraId` + `paragraphHint`). It serves as a cheap existence/precondition check (there is ≥1 revision) and preserves the hard invariant that `paraId` is always a valid 8-hex id the engine can resolve. For `All` scope the applier **ignores it for targeting** and instead accepts/rejects every revision in deterministic document order (§3.4).

**Chosen: presence-anchor.** Rationale: it preserves the base contract and the `isComposeOperation` guard (`paraId.length > 0`) with **zero** change to the base record or the guard; the alternative sentinel would fork the paraId shape. This is the one intentional, documented semantic wrinkle — see the ADR-tension note §6.

> **Alternative also considered — client fan-out to N single ops** (no explicit batch op): rejected because the owner directive + spec FR-11 explicitly want a **batch op in the catalog** with server-side deterministic reconciliation ordering, and one server authority for ordering is more robust (and atomic) than N client-ordered ops. The explicit batch op is the committed shape.

### 3.3 Fields (both ops — identical shape)

| Field | JSON name | Type (server / client) | Required | Notes |
|---|---|---|---|---|
| (base) paraId | `paraId` | `string` (required) | always | Single: the revision's containing paragraph. All: presence-anchor = first paragraph (doc order) holding a revision. |
| scope | `scope` | `ComposeRevisionScope` / `'Single' \| 'All'` | always | `Single` = one revision by id; `All` = every tracked revision, doc order. |
| revisionId | `revisionId` | `string?` / `string \| null` | required iff `scope==Single` | The native OOXML `w:ins`/`w:del` `w:id` (= `ImportedRevision.id`). `null` for `All`. |

```
// server
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeRevisionScope { Single, All }
```
```ts
// client
export type ComposeRevisionScope = 'Single' | 'All';
```

`acceptRevision` and `rejectRevision` are **structurally identical** (same three fields); they differ only in the applier's action (§3.5). Two discriminators (not one op with a direction flag) matches the catalog's existing "one discriminator per semantic action" convention (`setMark` vs `clearMark`, `splitParagraph` vs `mergeParagraph`).

### 3.4 Deterministic batch ordering (`scope: All`)

Reconciliation order is **document order (OpenXml preorder traversal of `document.xml`)**: revisions are accepted/rejected ascending by `(paragraph document-order index, revision document-order index within the paragraph)`. This is stable, Word-valid, and independent of client input order. (Spec Open-Question "G12 batch reconciliation ordering" — the *exact* Word-valid interleave when accept/reject of one revision shifts a sibling's indices — is a **task-013 design-time detail**; the schema commits to the ordering *rule* here, and single-by-id (task 012) can land before batch (task 013).)

### 3.5 Validation rules (accept/reject)

1. `scope` ∈ `{Single, All}` → else 400 (unknown enum).
2. `scope==Single`: `revisionId` non-null/non-empty; `paraId` resolves; a `w:ins`/`w:del` with `w:id == revisionId` exists in that paragraph → else `ParagraphNotFound` / a new `RevisionNotFound` (§5) → 422. **Never** falls back to text-search or content match.
3. `scope==All`: `revisionId` MUST be `null` (a non-null id with `All` scope → 400 contradiction); `paraId` resolves to a real paragraph (presence-anchor). If the document has zero tracked revisions the op is a **no-op success** (idempotent), not an error.
4. Applier action (native, per ADR-049 NFR-07 "Word-valid revision state"):
   - **acceptRevision** on `w:ins` → unwrap the inserted run into normal content (strip the `w:ins`); on `w:del` → remove the run entirely (`w:del` + `w:delText` gone).
   - **rejectRevision** = the inverse: `w:ins` → remove the inserted run; `w:del` → restore the deleted run as normal content.
   - Batch (`All`) applies the same, every revision, in §3.4 order.
5. Resolving onto a genuinely ambiguous overlapping revision region is refused deterministically (existing `TrackedChangeReconciliationUnsupported` boundary), never guessed — but the *normal* accept/reject of a cleanly-`w:id`'d revision is exactly what G12 makes succeed.

---

## 4. Paired catalog entries (byte-exact — copy targets for tasks 012/013/014)

### 4.1 Server — add to the `[JsonDerivedType]` block on `ComposeOperation` (after `:157`)

```csharp
[JsonDerivedType(typeof(TableOperation), "table")]
[JsonDerivedType(typeof(AcceptRevisionOperation), "acceptRevision")]
[JsonDerivedType(typeof(RejectRevisionOperation), "rejectRevision")]
```
Plus the three new `sealed record`s (`TableOperation`, `AcceptRevisionOperation`, `RejectRevisionOperation` : `ComposeOperation`) and the three new enums (`ComposeTableOpKind`, `ComposeTableProp`, `ComposeRevisionScope`) per §2–§3. The base-class doc comment "Exactly ten derived types" (`:140`) updates to **thirteen**.

### 4.2 Client — add to `COMPOSE_OPERATION_TYPES` (the `:54–65` tuple) and the union (`:230`)

```ts
export const COMPOSE_OPERATION_TYPES = [
  'insertText','deleteRange','replaceRange','setMark','clearMark',
  'splitParagraph','mergeParagraph','insertParagraph','deleteParagraph','setBlockAttr',
  'table','acceptRevision','rejectRevision',   // R5 additions (G4 / G12)
] as const;
```
Plus the three new `export interface`s + their union members on `ComposeOperation` (`:230–240`), and the three string-literal-union type aliases. The union doc comment "exactly ten operations" (`:227`) updates to **thirteen**. `isComposeOperation` needs **no change** — it validates against `COMPOSE_OPERATION_TYPES` + `paraId` generically, so the three new discriminators pass automatically once added to the tuple (batch ops satisfy `paraId.length > 0` via the presence-anchor).

### 4.3 Schema-version recommendation

`COMPOSE_OPERATION_SCHEMA_VERSION` / `ComposeOperationSchema.Version` is currently `"compose-ops-v1"`. Adding op types is **forward-incompatible** (an old `v1` engine receiving a log containing `table` fails polymorphic deserialization on the unknown discriminator). **Recommendation: bump to `"compose-ops-v2"` when the catalog code is committed** (i.e. in the first implementing task, or a small coordinating catalog task ahead of 012/014). Effect during the shared-`sprk_spaarkeai` / shared-`spaarke-bff-dev` deploy-skew window: a `v2` client sending to a `v1` server gets a deterministic `UnsupportedSchemaVersion` 400 (both ends "validate the version they compile against", `ComposeOperation.cs:53–55`) rather than a confusing 500 — the desired fail-safe. Deploy both ends together (task-042 concern). This bump is a **recommendation for the implementing tasks**, not committed here.

---

## 5. Applier / error-kind touchpoints (informative — for tasks 012/013/014)

The design implies these engine changes (out of scope for this task, listed so the implementers see the surface):
- `ComposeShadowPatchEngine.Apply` op switch (`ComposeShadowPatchEngine.cs:222`,`:266`) gains `TableOperation` / `AcceptRevisionOperation` / `RejectRevisionOperation` cases.
- `setBlockAttr`'s `StructuralOpNotYetImplemented` seam (`:1438`) is a sibling pattern — table/revision ops similarly move from "not yet implemented" to real appliers.
- Likely new `ComposePatchErrorKind` members: `TableNotFound`, `TableIndexOutOfRange`, `RevisionNotFound` (all → 422), following the existing per-refusal mapping (`:1401–1439`). Exact set is the implementers' call; the **schema** (this task) does not depend on them.

---

## 6. ADR-tension note (surfaced per CLAUDE.md §6.5 — for the record, no escalation required)

**Tension (not a violation):** the base `ComposeOperation.ParaId` is `required` and documented as "always a real 8-hex `w14:paraId`, the coarse anchor." The `acceptRevision`/`rejectRevision` **`scope: All`** batch form carries a `paraId` that is a **presence-anchor** (first paragraph holding a revision), not a targeting anchor — a mild semantic stretch of "the paragraph this op is anchored to."

- **Why this is not an ADR-039 issue:** it does not fork the catalog, add an endpoint, or a side channel; it stays a single closed-union member applied by the frozen engine over the existing envelope. It is a **field-semantics** choice within the extension, resolvable by design (path C — comply), which is what §6.5 prefers.
- **Why presence-anchor over a `"*"` sentinel:** presence-anchor preserves the hard invariant that `paraId` is always a resolvable 8-hex id (keeping the engine's O(1) resolve + `isComposeOperation` guard meaningful) at the cost of a documented semantic; the sentinel would keep the semantic clean but fork the field shape. The invariant-preserving choice was taken.
- **Alternative (client fan-out) considered and rejected:** owner + FR-11 want an explicit catalog batch op with **server-authoritative** deterministic ordering; that is strictly more robust than N client-ordered single ops.

No human sign-off is required to proceed; this note records the decision so the implementing tasks (012/013) and code-review see the reasoning rather than re-litigate it.

---

## 7. Placement Justification (CLAUDE.md §10 — BFF Hygiene)

- **Placement:** the op types are pure `record`s + `enum`s in `Services/Compose/Operations/ComposeOperation.cs` — the existing op-schema location. **No** new namespace, service, DI registration, endpoint, or package.
- **Purity (ADR-013 Tier-1 NetArchTest / ADR-007):** no `IOpenAiClient` / executor / routing type; no `Microsoft.Graph` type. `ComposeShadowPatchEngine` stays `byte[]`-in/`byte[]`-out.
- **ADR-039:** closed-catalog extension under version control; AI redline path unchanged (envelope-only, engine frozen). **No new AI-dispatch endpoint.**
- **Publish size (NFR-04, ≤60 MB; ~46.11 MB post-R4 baseline):** ≈0 delta — three records + three enums (+ client type text), **zero new runtime package** (NFR-03). No measurable compressed-size impact expected when the code lands.

## 8. Component Justification (CLAUDE.md §11 — three-question, per op)

| Op | Existing overlap | Extend instead? | Cost-of-doing-nothing (concrete) |
|---|---|---|---|
| `table` | Closed `ComposeOperation` catalog; no op expresses table structure | **Extend** the union with one op + 2 enums (never fork) | Table structural edits on loaded docs are silently dropped; the table control stays disabled (SDL-3) |
| `acceptRevision` | Closed catalog; `ImportedRevision` (id-bearing) exists but no op consumes it for accept | **Extend** with an id-addressed op | Accepting an imported tracked change + saving → `TrackedChangeReconciliationUnsupported` 422 (ET-2) |
| `rejectRevision` | ditto (`clearMark`/`deleteRange` operate on user marks, not native imported `w:ins`/`w:del` by id) | **Extend** — inverse of accept, same shape | Rejecting an imported tracked change + saving → same 422 |

Batch (`All`) is a `scope` field on the accept/reject ops, not a fourth op — one excellent op shape over two overlapping ones.

## 9. Mirror unit test (for the implementing tasks)

The paired addition MUST be guarded by a **mirror assertion** so the two ends can't drift (the FR-11 spine promise). Recommended (tasks 012–014, `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`):
- assert the server `[JsonDerivedType]` discriminator string set **equals** the client `COMPOSE_OPERATION_TYPES` tuple (extend the existing schema/mirror test to include `table`, `acceptRevision`, `rejectRevision`);
- per new op: a **round-trip** serialize→deserialize test proving `type` + all fields survive `client → server → client` without loss (the ADR-038 seam-DoD companion is a through-the-wire save slice on a corpus doc — a G12 doc with pre-existing revisions, a G4 doc with a table — per the project CLAUDE.md seam obligation).

---

## 10. Dependency map

| Task | Consumes from this design |
|---|---|
| **012** (G12 single) | `acceptRevision`/`rejectRevision` op shape, `scope: Single`, `revisionId` addressing, validation §3.5, mirror test §9 |
| **013** (G12 batch) | `scope: All` presence-anchor (§3.2), deterministic ordering rule (§3.4), batch validation, batch seam cases |
| **014** (G4 table) | `table` op shape (§2), `ComposeTableOpKind`/`ComposeTableProp`, ancestry-walk anchoring (§2.1), `newParaIds` convention, applier mapping (§2.5), schema-version bump (§4.3) |
| **033** (G5 hyperlink) | not a new op here, but the same mirror discipline (§1) + the `ComposeMarkType` extension precedent it will reuse |

---

## 11. Acceptance-criteria self-check (POML `<acceptance-criteria>`)

1. ✅ Each of the three ops has a defined discriminator, field set (with types), addressing semantics, and validation rules — §2, §3.
2. ✅ Accept/reject address by revision **id** (single) + accept-all/reject-all batch (`scope: All`) with a deterministic ordering rule (§3.4), and **never** by offset/content — §3.1, §3.5.
3. ✅ Paired server `[JsonDerivedType]` + client `COMPOSE_OPERATION_TYPES` entries specified as byte-exact mirrors (same discriminator strings + field names) — §4.
4. ✅ Negative: **no** op requires a new AI-dispatch endpoint or side channel; all extend the closed catalog (ADR-039) — §0 escalation check.
5. ✅ Negative: **no** catalog code committed in this task — design only, feeding tasks 012/013/014/033.
