# Task 033 — born-in-editor table-op gap — DEFERRED to task 037 (owner Path B/C)

> **Date**: 2026-07-22
> **Status**: ✅ **RESOLVED (deferred)** by the orchestrator → new task **037** (born-in-editor + table-authoring
> op-schema extension). 033 now depends on 037; born-in-editor stays on `ComposeDocumentRenderer` (working, no
> regression) as a documented §6.5 Path-A interim exception. The **Path B (extend the FR-11 op schema for table
> authoring) vs Path C (drop born-in-editor table authoring — product regression)** choice is a deferred OWNER
> decision (see task 037 `<owner-decision-required>`), surfaced at the Phase-3 boundary alongside task 036
> (push-annotations) — same class of gap. Blocks Success Criterion 7 (one byte-author). Original analysis below.
> **Original status**: BLOCKED at the pre-implementation representability check; no code changed.
> **Trigger fired**: the task's own `<escalation>` clause — *"Any born-in-editor formatting or construct
> cannot be expressed as an operation set onto an empty shadow package (forcing a fallback full-render
> path). STOP and surface per root §6 / §6.5 rather than silently retaining a second render/byte-author
> path — that would reintroduce the two-path defect R4 removes."*

## Precondition check — satisfied

Task 030 (`ComposeShadowPatchEngine` core) and 031 (structural operations) are both ✅ complete per
`tasks/TASK-INDEX.md`. The engine + insert path this task was meant to point born-in-editor saves at
genuinely exist. The block below is NOT a sequencing gap (unlike task 023's original block) — it is a
**closed-schema representability gap**: the task-003 operation contract (FR-11, "the spine both ends
implement identically") has no way to express one of the constructs born-in-editor documents can
legitimately contain today.

## The specific unrepresentable construct: native OOXML tables

**Born-in-editor documents CAN contain tables today, and this is load-bearing, tested, shipped behavior:**

1. **Editor surface** — `ComposeFormatToolbar.tsx:328-330` exposes a live "insert table" toolbar button
   (`editor.chain().focus().insertTable({ rows: 2, cols: 2, withHeaderRow: true }).run()`), gated only by
   `controlDisabled` (read-only state) — **not** by loaded-vs-born-in-editor mode. A user drafting a new
   contract from scratch can insert a fee schedule / signature table exactly as easily as one editing an
   uploaded doc.
2. **Content-model surface** — `ComposeContentModel` (`types/compose-contracts.ts`) has a first-class
   `ComposeBlockKind.Table` with `ComposeTableRow`/`ComposeTableCell` (header flag + nested block content).
   `buildContentModel` (`docxBridge.ts:404+`) maps a live editor table into this shape
   (`docxBridge.contentModel.test.ts` — *"maps a native table to a Table block with header cells + cell
   paragraphs"*, currently green). Server-side, `ComposeDocumentRenderer.SynthesizeDocument` authors a real
   `w:tbl` from this content model. **Tables are a genuine, currently-shipped born-in-editor capability.**
3. **The task-003 operation schema has NO table primitive.** `Services/Compose/Operations/ComposeOperation.cs`
   declares the discriminated union as CLOSED — exactly ten `[JsonDerivedType]` entries (`insertText`,
   `deleteRange`, `replaceRange`, `setMark`, `clearMark`, `splitParagraph`, `mergeParagraph`,
   `insertParagraph`, `deleteParagraph`, `setBlockAttr`) — every one paragraph/run-scoped. There is no
   `insertTable`/`insertRow`/`insertCell` op, and the file's own header comment underscores the schema is
   deliberately closed and shared: *"Any change here is a breaking change to [the client mirror] and
   vice-versa"* / task-003 decisions doc: *"resist adding beyond the set."*
4. **The Patch Engine treats tables as opaque obstacles, never as something it authors.**
   `ComposeShadowPatchEngine.cs` explicitly REFUSES structural operations that would cross a table boundary
   (`"an intervening block (e.g. a table or a section boundary) makes the merge non-representable"`,
   `"one is inside a table cell"`) — tables are handled as pre-existing structure the engine works AROUND on
   a retained original, never as a construct it can build from nothing. This is architecturally consistent
   with I-1 ("never wholesale-regenerated for a loaded doc") but has no analogue for authoring a table into
   an EMPTY shadow package, because born-in-editor has no retained original to "work around" — the table
   must be authored, and nothing in the op set can author one.

## Why this specifically blocks THIS task (not a pre-existing, already-accepted gap elsewhere)

Table-INSERT edits on a *loaded* document already fall outside the op-log's captured surface today (the
step interceptor's `classifyStep` has no table-node handling — a table-insert step is not one of the
paragraph/heading textblock cases it recognizes). That is an already-shipped (task 020/022/032) limitation
for loaded docs, and arguably defensible there under I-1 (existing tables in a retained original survive
byte-identical regardless; only NEW table edits on a loaded doc are unsupported).

**Born-in-editor is categorically different**: there is no retained original to fall back on. If the op
model cannot author a table, a born-in-editor document containing a table has **no way to be saved with
that table present** under the unified model — full stop, not a degraded edit. Unifying born-in-editor onto
the op-log model as FR-09 literally specifies (`buildContentModel` "reconciled/removed", "no separate
full-render export path remains") would make table-containing new documents **unsaveable-as-drafted**
(either silently drop the table — forbidden by both this task's escalation clause and invariant
"never-silently-dropped" that runs through the whole op-log design — or throw, which regresses a
currently-working, tested feature).

## What was checked

1. Read `Services/Compose/Operations/ComposeOperation.cs` in full — confirmed the closed 10-member
   discriminated union, no table-shaped operation, explicit "spine both ends implement identically" framing.
2. Read `ComposeShadowPatchEngine.cs` merge/split refusal logic — confirmed tables are refused-across, never
   authored.
3. Read `ComposeService.cs` `ResolveSaveBaselineAsync` (`(a0) BORN-IN-EDITOR` branch, lines ~814-825) —
   confirmed the current path: `request.ContentModel is not null` → `_documentRenderer.SynthesizeDocument(...)`,
   entirely bypassing the Patch Engine (skips the `hasOperations || hasComments` branch at line 571 because
   `request.ContentModel is null` is false).
4. Grepped `ComposeFormatToolbar.tsx` — confirmed `insertTable` is a live, unconditional (mode-agnostic)
   editor command.
5. Read `docxBridge.contentModel.test.ts` `buildContentModel (FR-01a)` describe block — confirmed native
   table mapping is tested, current, green production behavior.
6. Grepped `stepOperationInterceptor.ts` `classifyStep` — confirmed no table-node case exists (supporting
   evidence that this is a schema-level gap, not an oversight isolated to this task).
7. Cross-checked `design.md` §12 Q5 and `spec.md` FR-09/Owner Clarifications — both state the unification
   requirement ("insert-everything op set") without carving out tables; neither anticipates the schema gap.

## Decision needed (owner / next planning pass)

One of (per root CLAUDE.md §6.5 — reviewer chooses, not defaulted):

- **(A) Project-scoped exception (recommended for THIS task)** — implement the FULL insert-everything
  unification for every representable construct (paragraphs, headings, ordered/bullet lists, bold/italic/
  underline runs — the majority of born-in-editor documents), and KEEP `ComposeContentModel` /
  `ComposeDocumentRenderer` / the `buildContentModel` client export ALIVE but EXPLICITLY SCOPED to
  table-containing born-in-editor saves only (a documented, narrow, temporary two-path exception — not the
  silent full-parallel-path this task's escalation clause forbids). File a fast-follow task to close the gap
  per (B). This ships the FR-09 intent for ~the common case now, with an honest, cited exception for the one
  construct the schema can't yet carry.
- **(B) ADR/schema amendment** — extend the task-003 operation schema (FR-11, the shared spine) with a
  table-authoring operation family (e.g. `insertTable`/`insertTableRow`/`insertTableCell`, or a coarser
  `insertBlock` carrying an embedded content sub-model). This is itself a significant, separately-scoped
  design decision (the schema is explicitly described as "the spine both ends implement identically";
  changing it is "a breaking change... and vice-versa") — recommend a dedicated task, not a sub-step of 033.
- **(C) Pivot to comply** — remove table support from the born-in-editor authoring surface (disable
  `insertTable` when there is no `documentSpeId`/loaded baseline) so FR-09's "no separate full-render path"
  can be satisfied literally, with tables becoming import-only (present in uploaded docs, never author-able
  from scratch). This is a product-visible feature regression and needs explicit owner sign-off — not a
  purely technical call.

**No path was selected under this task** — implementing (A) without owner sign-off would itself be a
silent, unilateral scope/feature decision; implementing (B) is out of this task's estimated size (2 days)
and touches the FR-11 spine, which the schema's own header comment flags as needing deliberate, first-class
handling. Reporting blocked, with this evidence, is the responsible action per the task's own escalation
clause and root CLAUDE.md §6.5.

## Status

Task 033 POML: `<status>blocked</status>`. No code changes were made — `ComposeService.cs`, `docxBridge.ts`,
`ComposeEditor.tsx`, `ComposeWorkspace.tsx`, and `compose-contracts.ts` are all untouched by this task.
