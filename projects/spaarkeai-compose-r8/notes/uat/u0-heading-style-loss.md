# U-0 — heading-style loss on "remove numbering": REPRODUCED, root-caused, fixed

> 2026-09-02 · the sub-observation from UAT round 1, which `uat-round-1-findings-and-plan.md` said to
> reproduce before acting. It reproduces, and it is worse than the screenshot suggested.

## What the owner saw

Removing numbering from *"1.2 Technical Field of the Invention"* also cost it its **heading style** — it
came back as indented body text.

## What actually happens

The projection never puts a numbered heading in a list. `ComposeDocxProjectionBuilder.RenderParagraph`:

```csharp
var listInfo = headingLevel is null ? ListInfo(p, ctx) : null;
```

A heading therefore emits as `<h2 data-computed-number="1.2">`, **outside any `<ol>`** — so
`editor.isActive('orderedList')` is `false` on it. **The "remove numbering" click was the toggle ADDING a
list**, not removing one. That single fact explains the entire screenshot: the number vanished, the text
indented, and the heading style went.

Measured against the real production extension set (`StarterKit` + paraId + number-atom + pStyle + indent):

```
before  <h2 data-paraid="AAAA1111" data-computed-number="1.2" data-numbering-level="1">…</h2>
after   <ol><li><p>…</p></li></ol>
```

| | before | after |
|---|---|---|
| node | `heading`, level 2 | `paragraph` inside a `listItem` |
| `paraId` | `AAAA1111` | `1EBA2C7D` — **freshly minted** |
| `computedNumber` | `1.2` | `null` |
| `numberingLevel` | `1` | `null` |

**Three independent losses**, and a second toggle does not restore the heading — the round trip is
irreversible. `orderedList: { keepAttributes: true }` was measured too and recovers **none** of it, so
there is no TipTap configuration that fixes this.

**One loss I nearly reported that is not real**: `pStyle` is `null` on both sides. Its extension sets
`parseHTML: () => null` deliberately, so it is never populated from projected HTML in the first place.
Checking beat asserting.

## Why it is a fidelity defect, not only a UI defect

The save re-renders a **changed** block from the content model, and
`ComposeBlockMerge.IsModelDeterminedStyle` deliberately treats `Heading1-6` as MODEL-owned — so the
baseline's `Heading2` is intentionally *not* inherited. The model now says "plain paragraph", so **one
toolbar click silently flattens a real Word heading in the saved `.docx`**. Silently, and named nowhere in
`COMPOSE-WRITE-RESIDUAL-LOSS.md`. That is R8's own thesis, so it is fixed here rather than deferred.

The re-minted `paraId` is the quieter half: any comment or redline anchored to `AAAA1111` is orphaned.

## The fix — refuse, don't half-fix

`ComposeFormatToolbar.listToggleWouldDestroyBlockIdentity` disables **both** list toggles when the caret is
in a heading or in a block carrying a server-computed number. Hover reason: *"Headings and numbered clauses
take their numbering from the document — change it in Word for now."*

Two partial fixes were considered and rejected:

- **Carry `paraId` through the toggle** — preserves the anchor while still flattening the heading. Trades a
  loud loss for a quiet one.
- **Make the toggle author numbering** — a new list has no `w:numPr`/numbering definition to reference, so
  the block would still come back unnumbered (UAT item 4). The control would remain a broken promise.

Scope is deliberately narrow: **ordinary unnumbered body paragraphs keep their list toggles**, so R5 task
011's "re-enabled on loaded docs" decision stands everywhere it was not destructive.

## Scoped by measurement, not by assumption

The Body/Heading menu was probed for the same failure and is **clean** — `setParagraph` and
`toggleHeading` both preserve `paraId` *and* `computedNumber`, and the heading round-trips back to `h2`.
The destruction is specific to the list **wrap**, not to block retyping in general.

## Tests

`ComposeFormatToolbar.numberedBlockGuard.test.tsx`, 11 tests in three parts:

1. **The mechanism** — pins what `toggleList` does to a projected numbered heading. This suite is the
   guard's *evidence*: if upstream TipTap ever makes the retype non-destructive, it goes red and tells us
   the refusal can be lifted. A guard whose premise is never re-checked outlives its reason.
2. **The predicate** — heading → true, numbered paragraph → true, plain paragraph → false, plus null and
   `getAttributes`-less editors.
3. **The refusal** — through the real toolbar driven by a **real** editor, not the sibling file's
   chainable mock, so the disabled state derives from genuine projected HTML. A mock configured to report
   `isActive('heading') === true` would prove only that the mock was configured.

**Both negative controls run.** Reverting the wiring → 3 red (the two "disables" tests + the tooltip),
while "leaves enabled" stays green. Forcing the predicate always-true → 3 red in the *other* direction
(plain-paragraph, bare-editor, leaves-enabled). The detector fires on the regression AND on over-blocking.

## What this hands to the numbering project (Option C)

A finding the design note did not have: **the native `<ol>` marker is suppressed *unconditionally*** in
`ComposeEditor`'s `useStyles().editorSurface`, and the number is painted only from `computedNumber`. So a
list created in the editor shows **no number at all** — on born-in-editor documents too, not just loaded
ones. UAT item 4 is therefore universal, not loaded-only.

The tempting quick fix — scope the suppression to projected lists so new lists get the browser's native
marker — **conflicts with invariant F-3**: a projected paragraph whose `numId` was unresolvable is
deliberately left unnumbered (never fabricate a number), and un-suppressing would hand it a browser-invented
one. Distinguishing the two needs a projection-emitted marker on the `<ol>` itself. That is server + client
+ an invariant, i.e. exactly the write-path work that must not ride in on a UX task — which is why it goes
to the numbering project rather than being taken here.
