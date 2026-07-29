# Task 021 — WS-2 Emit `w:ind` Indentation + `white-space:pre-wrap` for Preserved Whitespace

> Written by the task 021 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are NOT
> touched here — owned by the main session.

## Summary

Closes the two WS-2 read-fidelity gaps FR-07/FR-08 targeted:

- **FR-07** — `w:ind` (`@w:left`/`@w:firstLine`/`@w:hanging`) was dropped entirely by the projection; indented
  legal clauses rendered flush-left. Now emitted as `margin-left`/`text-indent` CSS on the projected paragraph
  (server), **and** preserved through the ProseMirror parse/render round-trip on the client (see Deviation #1).
- **FR-08** — `xml:space="preserve"` runs and consecutive spaces were stored faithfully by the reader but
  collapsed to a single visible space under the editor surface's default `white-space: normal`. Fixed via an
  explicit `white-space: pre-wrap` rule on `editorSurface['& .ProseMirror']` (see Deviation #2 for a related
  finding about `@tiptap/core`'s own auto-injected baseline).

## FR-07 — `w:ind` emit (server)

`ComposeDocxProjectionBuilder.cs`: `AppendAlignment` (the FR-09 alignment-emit pattern cited by the POML,
previously at `:883-897`) was renamed `AppendParagraphStyle` and extended with a new `AppendIndentDeclarations`
helper, because **an HTML element cannot carry two `style` attributes** — alignment and indentation MUST share
one call site / one combined `style="…"` output, semicolon-joined. Both call sites (`RenderParagraph`'s
list-item path and its plain/heading path) were updated from `AppendAlignment(p, ctx)` to
`AppendParagraphStyle(p, ctx)`.

**Unit conversion — `pt`, not `px`.** OOXML twips convert EXACTLY to CSS points: `1pt == 20 twips` by
definition (both are point-based units), so `twips / 20.0` is lossless with no assumed reference DPI — `px`
would have required picking a DPI (96 is the common web default but is not part of the OOXML unit system, so
it would have been an added assumption, not a fact). `FormatPt` uses `"0.##"` so whole-point values format as
`"36pt"`, not `"36.00pt"`.

**OOXML semantics mirrored** (ECMA-376 §17.3.1.12): `w:left` → `margin-left`. `w:firstLine` is an ADDITIONAL
positive offset applied only to the first line → positive `text-indent` (on top of `margin-left`, matching
CSS `text-indent`'s own semantics). `w:hanging` outdents the first line relative to the rest of the
paragraph → NEGATIVE `text-indent` equal to `-hanging`. `w:hanging` and `w:firstLine` are mutually exclusive
per spec; if a malformed source carries both, `w:hanging` wins (Word's own resolution) — verified by a unit
test (`Build_ParagraphWithHangingAndFirstLineBothPresent_HangingTakesPrecedence`).

## FR-08 — `white-space: pre-wrap` emit (client)

`ComposeEditor.tsx`'s `editorSurface['& .ProseMirror']` Griffel rule gained `whiteSpace: 'pre-wrap'`.

## Deviation #1 (necessary, not scope creep) — `composeIndentExtension.ts` (NEW file, not in the POML's output list)

The POML's `<relevant-files modify>`/`<outputs>` list only two modify targets
(`ComposeDocxProjectionBuilder.cs`, `ComposeEditor.tsx`) plus one test file. Re-grepping the client mount path
(step 1) surfaced a real gap that would have made FR-07 silently non-functional end-to-end:

TipTap's base `paragraph`/`heading` nodes (`@tiptap/extension-paragraph`) have `parseHTML: () => [{ tag: 'p'
}]` with **no attribute extraction** — an arbitrary inline `style` attribute on the server's projected `<p
style="margin-left:36pt">` is **silently stripped** when `editor.commands.setContent(projection.html)` parses
it, unless some registered extension's `addGlobalAttributes` declares an attribute whose `parseHTML` reads it
back out. This is exactly the mechanism the LOCKED `@tiptap/extension-text-align` already uses for
`text-align` (`ComposeEditor.tsx` `LOCKED_EXTENSIONS`, `TextAlign.configure({ types: ['heading', 'paragraph']
})`) — confirmed by inspecting `@tiptap/extension-text-align`'s source directly (`element.style.textAlign` /
`renderHTML: () => ({ style: 'text-align: …' })`). Without an equivalent for indentation, the BFF's FR-07 emit
would be present in `projection.html` as a *string* but invisible in the actual editor — indented legal
clauses would keep rendering flush-left in the real product, which is the exact defect FR-07 exists to fix
(the POML's own goal statement says paragraphs must "render" at authored indentation, not merely carry the
style in an HTML string).

Added `src/client/shared/Spaarke.Compose.Components/src/widgets/composeIndentExtension.ts` — a small,
additive `Extension.create` (mirrors the `ComposePStyleExtension` / `paraIdExtension.ts` file-organization
precedent already established in this codebase) registering `indentMarginLeft`/`indentTextIndent` global
attributes on `paragraph`+`heading`, sourced from and re-emitted as the `margin-left`/`text-indent` inline
style. Registered additively in `useEditor`'s extensions array as `COMPOSE_INDENT` (never mutates
`LOCKED_EXTENSIONS`). `@tiptap/core`'s `mergeAttributes` combines this extension's `style` output with
`TextAlign`'s own `text-align` declaration into ONE `style` attribute (verified: `mergeAttributes` splits on
`;`, maps `property: value`, re-joins — no collision since the property names never overlap) — proven by the
client test `an indented AND center-aligned paragraph preserves BOTH text-align and margin-left`.

This extension does not compute an indent value itself — it only PRESERVES what the server computed
(`Services/Compose/` stays the sole owner of the OOXML read, ADR-007/013). No new package, no BFF change, no
publish-size impact (client-only file). Justification per root CLAUDE.md §11: **existing** — no prior
mechanism preserved arbitrary paragraph `style` attrs; **extension** — mirrors the already-registered
`TextAlign` pattern exactly, same node types, same idiom; **cost of doing nothing** — the BFF's FR-07 emit
would be silently discarded on mount, so indented clauses would keep rendering flush-left in the actual
editor even after this task, defeating the task's stated goal.

## Deviation #2 (finding, not a defect) — `@tiptap/core` already auto-injects a preserving `white-space` rule

While building the FR-08 client test, inspecting `document.styleSheets`/`cssRules` (not just `<style>` tag
`textContent`, since Griffel inserts via CSSOM `insertRule`, which does not update `textContent`) revealed
that `@tiptap/core` ships its own base stylesheet (`src/style.ts`, auto-injected via the `injectCSS` option,
**default `true`**, not currently overridden anywhere in this codebase) containing:

```css
.ProseMirror {
  white-space: pre-wrap;
  white-space: break-spaces;   /* same rule, second declaration wins within the rule: break-spaces */
}
```

`break-spaces` is a strict superset of `pre-wrap` (MDN: identical behavior, but ADDITIONALLY never collapses
trailing/wrapped whitespace either) — so this task's premise ("consecutive spaces collapse under the editor's
default CSS") was correct for a hypothetical bare-ProseMirror mount, but this codebase's actual TipTap
integration already had a non-collapsing baseline via TipTap's own default, undocumented anywhere in this
repo. This task's explicit Griffel rule (`.editorSurface .ProseMirror { white-space: pre-wrap }`, specificity
0-2-0 — two classes — vs. TipTap's bare `.ProseMirror` at 0-1-0) is kept regardless, because:

1. It is the task's explicit deliverable (root POML `<outputs>`), documenting the fidelity guarantee
   explicitly at the app-styling layer.
2. In a real browser, CSS specificity is resolved correctly across stylesheets regardless of source order —
   the app's own rule (0-2-0) wins over TipTap's library default (0-1-0), so this task's fix is the one
   actually governing the rendered value in production, not TipTap's default.
3. It is defense-in-depth: if a future TipTap upgrade changes or removes its `injectCSS` default, this app's
   own explicit rule keeps the fidelity guarantee independent of the library's internals.

**jsdom caveat (test-authoring note, not a production concern):** jsdom's CSS engine does not always resolve
same-property declarations across competing stylesheets by full CSS specificity the way a real browser does
(verified empirically: with both rules present, `getComputedStyle(...).whiteSpace` returned `break-spaces` —
TipTap's declaration — in this test env, despite the app's rule having higher specificity). The client test
(`ComposeEditor.indentAndWhitespace.test.tsx`) accepts either `pre-wrap` or `break-spaces` for exactly this
reason — **both satisfy FR-08's actual behavior contract** (neither collapses consecutive whitespace) — plus
a direct CSSOM assertion that the app's own `pre-wrap` rule is present in the stylesheet, independent of
jsdom's cascade-resolution quirk.

## Escalation

**Did not fire.** The task 021 escalation trigger ("if `pre-wrap` on the editor surface regresses existing R4
rendering of tabs/atoms/alignment — STOP and surface before shipping") does not apply:

- **`compose-tab`** is exactly ONE preserved space per tab (`<span class="compose-tab"> </span>`) — `pre-wrap`
  (and `break-spaces`) render a lone space identically to `normal`; collapsing only matters for RUNS of 2+
  whitespace characters. Verified by a client test.
- **`.compose-atom`/`.compose-atom-block`** — block layout (`display`, `padding`, `margin`) is orthogonal to
  `white-space`; `pre-wrap` still soft-wraps at the container width (unlike `pre`, which never wraps and would
  have risked overflow) — no atom's layout depends on whitespace collapsing.
- **alignment** (`text-align`) and the new **indentation** (`margin-left`/`text-indent`) are orthogonal CSS
  properties to `white-space` — no interaction, and their coexistence in one combined `style` attribute was
  separately verified (see FR-07 section).

## Tests

### Server (`ComposeDocxProjectionBuilderTests.cs`) — 6 new

`Build_ParagraphWithLeftIndent_EmitsMarginLeftStyle`,
`Build_ParagraphWithFirstLineIndent_EmitsPositiveTextIndentAlongsideMarginLeft`,
`Build_ParagraphWithHangingIndent_EmitsNegativeTextIndentAlongsideMarginLeft`,
`Build_ParagraphWithHangingAndFirstLineBothPresent_HangingTakesPrecedence`,
`Build_ParagraphWithAlignmentAndIndent_CombinesIntoOneStyleAttribute` (also asserts exactly ONE `style="`
substring in the output — the single-attribute invariant), `Build_ParagraphWithNoIndentation_...` (negative
case), plus the pre-existing alignment tests (`Build_ParagraphWithJustification_...`,
`Build_ParagraphWithLeftJustification_...`) re-verified unchanged (still pass against the renamed method).

### Client (`ComposeEditor.indentAndWhitespace.test.tsx`) — NEW file, 7 tests (the `<ui-tests>` equivalent)

Three FR-07 tests (left indent survives mount; hanging indent survives mount with correct negative
text-indent; alignment+indent coexist in one `style`), three FR-08 tests (light/dark mode `it.each` —
ADR-021 — non-collapsing whitespace; direct CSSOM presence of the app's `pre-wrap` rule), and the escalation
non-regression guard (`compose-tab` unaffected).

## Build / test / publish-size results

- `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` → **0 errors** (23 pre-existing warnings, identical
  set to task 020's baseline).
- `dotnet test --filter "FullyQualifiedName~Compose"` → **Passed: 629, Skipped: 1, Failed: 0, Total: 630**
  (task 020's baseline was 623 passed + 1 skipped; net +6 new server `[Fact]` tests, matching the 6 listed
  above). Skip is the pre-existing WS-3-gated `NumberingExactness_...` Theory, unrelated to this task.
- `dotnet test --filter "FullyQualifiedName~ComposeReadFidelityHarnessSeamTests"` → **Passed: 12, Skipped: 1,
  Failed: 0** — harness stays GREEN; all 8 corpus docs remain 100% text-exact (unaffected by this task, which
  touches paragraph-property style emit, not run text).
- Client: `npx jest --testPathPatterns="Compose"` → **642 passed, 1 failed, 643 total** (57/58 suites). The
  ONE failure (`ComposeEditor.advisoryComments.test.tsx`, `placed` expected `1` got `2`) is **pre-existing** —
  confirmed via `git stash` (reverting ALL task 021 changes) reproducing the identical failure on the
  unmodified baseline. Not caused by, or related to, this task.
- Client typecheck: `npm run build` (`tsc`) → identical 8 pre-existing errors on both baseline and
  post-change trees (all `@spaarke/ai-widgets` unbuilt-workspace-dependency errors + 3 pre-existing
  `ComposeWorkspace.tsx` implicit-`any` errors) — confirmed via the same `git stash` A/B comparison. Zero NEW
  errors from this task's files.
- Publish-size delta: `Sprk.Bff.Api.dll` **11,266,048 → 11,266,560 bytes (+512 bytes)** — measured directly
  (same method as task 020: before = task 020's recorded post-change size; after = this task's `dotnet publish
  -c Release`). Well inside the root CLAUDE.md §10 ≥5 MB escalation threshold and the ≤60 MB hard ceiling; a
  full-tree compressed archive (incl. PDBs) measured **~46.1 MB**, consistent with the ~49.63 MB project
  baseline range. No `.csproj` change (`git diff --stat -- '*.csproj'` empty).

## Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

- **Existing**: `AppendAlignment` already existed as the paragraph-level style-emit pattern in
  `ComposeDocxProjectionBuilder.cs`; `BuildContext`/the paragraph-render call sites already existed.
- **Extension**: Yes — `AppendAlignment` renamed to `AppendParagraphStyle` and extended with
  `AppendIndentDeclarations`, combining alignment + indentation into the SAME single `style` attribute (an
  HTML necessity, not a design choice). No new service, no new DI registration, no new package, no new BFF
  endpoint. Client-side, `TextAlign`'s exact pattern is mirrored for indentation (see Deviation #1) — no new
  abstraction invented, an existing one is replicated for a sibling concern.
- **Cost of doing nothing**: Indented legal clauses (a common construct in NDAs/contracts — nested
  definitions, sub-clauses, block quotes of prior agreement text) continue rendering flush-left, and preserved
  legal spacing (e.g., signature-block alignment, tabular-looking text built from spaces) continues collapsing
  to single spaces — both are F-1 (text/layout exactness) failures a reader would notice immediately when
  comparing the Compose editor to the source Word document, undermining the whole R4.5 "read + reference
  fidelity" project promise.
- `Services/Compose/` stays pure — no `Microsoft.Graph`/AI-internal reference added (ADR-007/013); no new
  `byte[]`-in/projection-out contract change. Publish-size delta ~512 bytes (~0 MB), well under the escalation
  threshold. `Services/Compose/` remains the sole owner of the OOXML read; the client extension only
  round-trips what the server computed, never computes an indent value itself.
