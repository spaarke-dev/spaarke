# UAT round 2 — findings (2026-09-02)

> Against the U-0 re-deploy. Six items. **Two of them close the project's two biggest open questions.**

## The two that matter most

### ✅ Item 4 — Track A PASSES

*"i saved/opened and the edits held."* That is R8's core claim, exercised against a real document by the
owner. Combined with item 3 below, the save path is doing what the release set out to make it do.

### ✅ Item 3 — the numbering prediction is CONFIRMED

*"in Word i do see the numbering."*

This was the experiment proposed hours earlier, and it settles the sizing question: **the document is
already correct; only the editor cannot display the number.** The write path — `ComposeBlock.ListItem` →
`BuildListItem`'s direct `w:numPr` → `ComposeNumberingAuthor` minting/merging `numbering.xml` — works end
to end. UAT item 4 of round 1 ("numbering adds nothing") is therefore a **display defect**, not a
round-trip hole, and the numbering work is correspondingly smaller than the round-1 note claimed.

---

## Fixed and re-deployed in this session

| # | Report | Root cause | Fix |
|---|---|---|---|
| **6** | *"says 'auto save on' but doesn't seem to save"* | **The label was false.** The autosave tick writes a **localStorage recovery draft and deliberately never calls the BFF** (NFR-03). So the toolbar read *"Unsaved · Auto Save On"* simultaneously — two halves contradicting each other. The mechanism was fine; the words were wrong. | Indicator → *"Recovery draft On/Off"*; Save-menu item → *"Keep recovery draft"* (under Save/Save As, "Auto Save" read as a third save mode). Truthfulness fix, no behaviour change. |
| **5** | Remove the down arrow, use the modern vertical scroll | The editor surface **hid its native scrollbar** (`scrollbarWidth: 'none'`) and substituted a floating down-arrow FAB — the exact control [`src/client/shared/CLAUDE.md`](../../../../src/client/shared/CLAUDE.md) bans, and Compose used the canonical thin scrollbar **nowhere**. | FAB removed (with its state, callback, measuring effect, style, icon); `editorSurface` spreads `thinScrollbarStyle` (ADR-051). A real scrollbar also restores position/length feedback a FAB cannot convey. |
| **2** (half) | *"no way to move up or down the number"* | **Not list indentation.** In a heading-style-numbered document, outline depth **is** numbering depth, and Tab does nothing because Tab only sinks *list items*. The Body menu offered **Heading 1–3 only**, capping depth at three — while both ends already supported six (`heading: { levels: [1..6] }`; server `MaxHeadingLevel = 6`). | Body menu now offers Heading 1–6. `currentBlockLabel` iterates the same list, so the button can't lag the menu (it would have said "Body" for a Heading 4). Exposing existing capability — no write-path change. |

Each carries a negative control: reverting `thinScrollbarStyle` → the scroll assertion reds while the
no-FAB assertion correctly stays green; narrowing `HEADING_LEVELS` back to `[1,2,3]` → 2 red. The two
suites that asserted the OLD behaviour were **rewritten, not deleted**, and the toolbar suite asserts
`not(/auto ?save/i)` so a relapse to the misleading label fails rather than passing quietly.

Client suite **1,394 / 1,394**.

---

## To the numbering project

- **Item 1** — deleting a number changes the line's level. Same family as U-0: the number is not editable
  text, so keystrokes against it do something other than what they appear to.
- **Item 2 (other half)** — **Tab / Shift-Tab as promote/demote.** Deliberately not added here: its
  interaction with lists and tables should be designed, not guessed, and Word's own behaviour is
  context-dependent (Tab demotes in a numbered heading scheme, indents in a list, moves cells in a table).
- **Round-1 item 4** — now known to be display-only (item 3 above). The fix is scoping the unconditional
  `<ol>` marker suppression, which still collides with invariant F-3 (never fabricate a number for an
  unresolvable `numId`) and needs a projection-emitted discriminator on the `<ol>`.

## 🔔 One open question for the owner

**Should Compose also autosave to the DOCUMENT, not just to a local recovery draft?**

Renaming the label makes today's behaviour honest, which is correct either way. But it does not answer
whether the underlying behaviour is the one wanted. A true server-side autosave is a real behaviour change
with real consequences — every tick is an SPE version and a concurrency event — so it is put rather than
assumed. Not an R8 blocker; R8's save path is manual-save-correct and now honestly labelled.

## R8 status after this round

Track A ✅ passes. The **only** remaining gate is **B — the `section-break-flattened` accept/decline**.
