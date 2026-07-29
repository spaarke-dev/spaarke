# Task 032 — WS-3 render the computed label as an explicit non-editable number-atom (FR-13/FR-14)

> Written by the task 032 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are
> owned by the main session and NOT touched here.

## Summary

Renders task 031's server-computed numbering label as an explicit, non-editable number-atom prefix in the
Compose editor — the owner-locked decision (spec FR-13, design §4 WS-3 "Rendering"). The editor no longer
relies (nor ever did in this project's plan) on the browser `<ol>` CSS auto-count for a legal number; the
atom is the SOLE source of a displayed legal number.

## How the number reaches the client — a DATA ATTRIBUTE, never text content

**Server** (`ComposeDocxProjectionBuilder.cs`): a new `computedNumberByParagraph` dictionary is populated
in the EXISTING Pass-1 loop (same walk 031 already computes labels in — no second pass), then threaded into
`BuildContext`. A new `BuildContext.AppendNumberingAttrs(Paragraph p)` method emits
`data-computed-number="…"` (the 031 label) and `data-numbering-level="…"` (the paragraph's own `Ilvl`) on
the paragraph's `<p>`/`<h#>` tag — called from `RenderParagraph` in BOTH the list-item (`<li><p>`) and
plain/heading branches, right after `AppendParaIdAttr`. No-op when the paragraph has no computed label
(unnumbered, or 031's fail-closed "unresolvable numId" case — never fabricate a number).

**Client** (`composeNumberAtomExtension.ts`, new file): two pieces, mirroring two established patterns:

1. `addGlobalAttributes()` on `paragraph`+`heading` (mirrors `composeIndentExtension.ts`'s pattern) —
   parses `data-computed-number`/`data-numbering-level` off the projected HTML (otherwise TipTap's base
   Paragraph/Heading schema silently strips an unregistered `data-*`) and re-emits them on `getHTML()`.
2. A ProseMirror **VIEW DECORATION** (`addProseMirrorPlugins()`, mirrors `TrackChangesExtension.ts` /
   `QaHighlightExtension.ts`'s pattern) — `buildNumberAtomDecorations(doc)` walks every numbered
   paragraph/heading and emits a `Decoration.widget(contentStart, …, { side: -1, ignoreSelection: true })`
   rendering a `<span class="compose-number-atom" contenteditable="false">{label}</span>` prefix.

## Why a decoration, not a doc node (the FR-14 design decision)

The POML flagged the escalation trigger: "if the number-atom participates in the tracked-edit stream OR
becomes user-editable — STOP." A real ProseMirror **atom node** (the `composeBlockAtom`/`composeInlineAtom`
pattern `opaqueAtomNode.ts` uses for SDT/field/object placeholders) would have been the more obvious mirror
of that file, but it was rejected for THIS feature: an atom node inserted as literal paragraph content would
become part of the paragraph's `content` model — shifting the paragraph's own text offsets that the
step-interceptor / annotation-reanchor system (`stepOperationInterceptor.ts`, task 011's offset-addressing
table) indexes, and would appear in `node.textContent`, which `TrackChangesExtension`'s live-redline word-diff
walks — i.e. it WOULD have crossed into "participates in the tracked-edit stream."

A **widget decoration** is not part of the document model at all: it never appears in `editor.getJSON()`,
cannot be selected into or typed into (structurally, not just via `contenteditable="false"`), never shifts
a sibling text offset, and is invisible to any diff/walk over `node.textContent`. This is the load-bearing
reason FR-14's boundary holds without any extra guard code — the escalation did NOT fire, because the
decoration approach makes the disallowed outcome structurally impossible rather than merely policed.

## Double-numbering suppression

`ComposeEditor.tsx`'s `useStyles().editorSurface` gained `'& .ProseMirror ol': { listStyleType: 'none' }` —
UNCONDITIONAL, not per-item. Rationale (recorded per the POML's explicit ask): every `<ol>` paragraph the
projection emits either (a) carries a `data-computed-number` (the common case — 031 computes a label for
every resolved `ParagraphNumberingRef`, and `ListInfo`'s ordered-detection and `ResolveParagraphNumbering`'s
direct-`w:numPr` path share the exact same `numPr?.NumberingId?.Val` condition, so they never disagree in
practice) or (b) is the rare, not-in-corpus "unresolvable numId" case where 031's `Compute()` returns `null`
and NO atom renders. Case (b) intentionally shows NO number rather than falling back to the browser's
1-based arabic counter, which is the CORRECT fail-closed behavior per this codebase's repeated "never
fabricate a number" posture (`Compute()`'s own doc comment) — a silently-wrong CSS-counted "1." next to a
correctly-computed sibling "4.2." would be a worse defect than a blank prefix. `<ul>` (bullet) lists are
untouched — confirmed by a dedicated CSSOM negative-check test.

## R5 G3 coupling (FR-14, recorded per constraint)

This task is READ-TIME ONLY. The atom renders the label 031 computed AT LOAD TIME; it does not recompute or
auto-renumber when the user edits the paragraph's text (verified by a dedicated test: inserting text into a
numbered paragraph leaves its `computedNumber` attribute unchanged). Live renumber-on-insert/delete
(reflected in redline) is **R5 G3**, and per 031's notes, G3 is expected to re-run
`NumberingComputationEngine` over the post-edit paragraph sequence rather than fork it — this task's
client-side decoration is compatible with that plan unchanged: G3 would simply need to re-source the
`computedNumber` node attribute after a server round-trip (or an equivalent client recompute), and the SAME
decoration-rendering mechanism here would pick up the new value with no client-side redesign.

## Escalation

**Did not fire.** The atom is not user-editable (structurally, via the decoration mechanism — see above)
and does not participate in the tracked-edit stream (verified: `editor.getText()` and the paragraph's own
`node.textContent` never contain the computed label).

## Verification

- **Server build** (`dotnet build src/server/api/Sprk.Bff.Api/ -c Release`): **0 errors** (23 pre-existing
  warnings, unchanged set).
- **Server tests** (`dotnet test --filter "FullyQualifiedName~Compose"`): **688 passed / 0 skipped / 0
  failed** (031's baseline was 682 passed / 0 skipped; +6 new `[Fact]`s for `AppendNumberingAttrs`).
- **Golden Theories** (`--filter "FullyQualifiedName~TextExactness|FullyQualifiedName~NumberingExactness"`):
  **32 passed** — 8 text-exactness (unaffected: the computed label is an attribute, never run text) + 24
  numbering-exactness (unaffected: server-side `ParaIdMapEntry.ComputedNumber`, unchanged by this task's
  purely additive attribute emission).
- **Publish size** (root CLAUDE.md §10): compressed **47.52 MB** (same `Compress-Archive` method as
  030/031) vs 031's post-task **47.52 MB** → **delta +0.00 MB**. No `.csproj` change (`git diff --stat --
  '*.csproj'` empty).
- **Client typecheck** (`npx tsc --noEmit`): the same **8 pre-existing errors** (5×
  `@spaarke/ai-widgets` unbuilt-workspace-dependency + 3× `ComposeWorkspace.tsx` implicit-`any`) on both
  baseline and post-change trees. **Zero new errors** from this task's files.
- **Client tests** (`npx jest --testPathPatterns="Compose"`): **661 passed / 1 failed / 662 total** (58
  baseline suites + 2 new files = 60 suites; 643 baseline tests + 12 headless
  `composeNumberAtomExtension.test.ts` + 7 React-mounted `ComposeEditor.numberAtom.test.tsx` = 662). The ONE
  failure is the **pre-existing** `ComposeEditor.advisoryComments.test.tsx` (`placed` expected `1` got `2`,
  DEF-01) — **unchanged by this task** (confirmed: same failure, same assertion, present before and after
  032's changes; 032 does not touch the advisory-comments materialize path). DEF-01 stays filed against its
  031-domain origin; 032 neither fixes nor worsens it.

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — `computedNumberByParagraph`
  dictionary (Pass-1), `BuildContext.AppendNumberingAttrs` (emits `data-computed-number`/
  `data-numbering-level`), two call sites in `RenderParagraph`.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/composeNumberAtomExtension.ts` (NEW) — the
  attribute-preservation + widget-decoration extension (`COMPOSE_NUMBER_ATOM`).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` — registers
  `COMPOSE_NUMBER_ATOM` additively (never mutates `LOCKED_EXTENSIONS`); adds `.ProseMirror ol { list-style-
  type: none }` (double-numbering suppression) and `.compose-number-atom` (ADR-021 token-based styling) to
  `useStyles().editorSurface`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs` — 6 new `[Fact]`s
  for `AppendNumberingAttrs` (list-item, unnumbered-negative, no-text-leak/text-exactness guard, ilvl-2
  level attribute, style-linked heading, bullet-glyph-not-fabricated-arabic).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/composeNumberAtomExtension.test.ts` (NEW) — 12
  headless `@tiptap/core` `Editor` tests (schema attrs, non-editable render, letters/roman/multi-level/
  "Article I" verbatim rendering, interrupted-run continuity, FR-14 read-time-only boundary, pure
  `buildNumberAtomDecorations` unit tests).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.numberAtom.test.tsx` (NEW) — 7
  React-mounted tests (`<ol>` marker suppression, style-linked heading atom, no-regression on plain/bullet
  content, ADR-021 light+dark token check, non-interactive CSS).

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: no numbering RENDER mechanism existed on the client — `ComposeEditor.tsx` relied entirely on
  the browser `<ol>` auto-count for any numbered content; grep of the pre-032 file found zero
  `data-computed-number`/number-atom handling.
- **Extension**: Yes — a new render-only TipTap extension inside the EXISTING editor mount, following the
  EXACT precedent of `composeIndentExtension.ts` (attribute preservation) and `TrackChangesExtension.ts`/
  `QaHighlightExtension.ts` (view-decoration-only, never a doc node). Not a new service/endpoint/package/DI
  registration — additive to the existing `useEditor` extensions array, never mutating `LOCKED_EXTENSIONS`.
  Server-side: `AppendNumberingAttrs` extends the EXISTING `AppendParaIdAttr`/`AppendParagraphStyle`
  attribute-emission pattern in `RenderParagraph`, inside the existing single Pass-2 render walk.
- **Cost-of-doing-nothing**: 031 computes the exact Word-identical label server-side, but with no client
  render it stays invisible — the editor would keep showing the browser's `<ol>` auto-count, which restarts
  at 1 on any interruption and cannot represent letters/roman/"Article I"/style-linked schemes at all. The
  core legal-fidelity defect (NFR-02) would remain visible to the reader even though the server-side
  computation (031) is correct — "Section 4.2" would still render as "1." in the actual product.
- `Services/Compose/` stays pure `byte[]`-in/projection-out (ADR-007/013): `AppendNumberingAttrs` reads only
  the already-computed `computedNumberByParagraph`/`numberingByParagraph` dictionaries and
  `DocumentFormat.OpenXml.Wordprocessing` types — no `Microsoft.Graph`, no AI-internal type. Client-side: no
  `@tiptap-pro/*`, no AGPL (NFR-03) — pure `@tiptap/core` Extension + `@tiptap/pm` Plugin/Decoration, the
  same MIT primitives `TrackChangesExtension.ts` already uses.
- **`/conflict-check`** must be run by the MAIN SESSION before the PR (subagent does not commit/PR):
  `Services/Compose/` overlaps `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`; the
  client `ComposeEditor.tsx` is the SAME shared mount file 013/020/021/032 (this task) have all touched —
  WS-3 sequential stretch per plan.md group W3 (`parallel-safe: false`), consistent with the POML's own
  `<parallel-reason>`.
