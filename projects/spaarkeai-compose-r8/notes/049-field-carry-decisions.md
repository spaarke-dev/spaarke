# Which Word FIELDS are carried through an edited paragraph — and which are not

> **Task 049** (`spaarkeai-compose-r8`, FR-A10 residual) · decided + measured 2026-08-25
> Owner decision 2026-08-25: the residual-loss list is not signed off with fields on it. Fields are to be
> **carried**, not accepted as losses. This note records the per-class decision and the evidence for it.
>
> Code: `ComposeContentModel.ComposeField` · `ComposeDocxProjectionBuilder.TryCarryField` ·
> `ComposeDocumentRenderer.AppendField`
> Tests: `tests/integration/seam/Compose/ComposeFieldCarrySeamTests.cs` (+ the two field rows in
> `ComposeResidualLossParityTests`)

---

## 1. The decision, in one line

**Every field whose OOXML can be reproduced exactly is carried, live. The gate is structural — "can this
construct be re-emitted as itself?" — never a list of instruction keywords.**

Carried: `REF`, `PAGEREF`, `PAGE`, `NUMPAGES`, `DATE`, `TIME`, `SEQ`, `STYLEREF`, `HYPERLINK`, and any
vendor or unknown instruction, in either authoring form (`w:fldSimple` or the `w:fldChar`
begin/instrText/separate/result/end run sequence).

Not carried, and named as such on the residual list: a **nested** field (`{ IF { PAGE } = 1 … }`) and a field
whose `begin`/`end` **straddle paragraphs** (a `TOC`, an `INDEX`). Both keep today's flatten and today's
`field-flattened-to-text` warning.

---

## 2. Why not a keyword allow-list

The task POML anticipated a per-instruction split: carry `PAGE`/`DATE` (they re-evaluate harmlessly), freeze
`REF`/`PAGEREF`/`TOC`/`HYPERLINK \l` (they target a bookmark, and if the target did not survive, Word shows
its broken-reference text where resolved prose stood). That is the right question to ask. Three findings
answered it the other way.

### 2.1 The bookmark target survives — verified, not assumed

`ComposeDocumentRenderer`'s own remarks (review 011-P4/P9) said a `REF` loses its target on save "because the
model does not carry bookmarks". **That comment was stale.** It predates task 041. Bookmarks survive a save
in both block positions:

| Where the target bookmark sits | What happens | Mechanism |
|---|---|---|
| A block the user did NOT edit | Cloned byte-verbatim | `ComposeBlockMerge` clone path |
| The block the user DID edit | Restored onto the rendered paragraph (span widened to the paragraph) | `ComposeBlockMerge.CarryBookmarks`, task 041 |
| A block the user DELETED | Gone | Word's own semantics for the same edit |
| The R6 fail-open path (baseline unprojectable) | Gone — along with everything else that path does not clone | `ComposeBlockMerge.Capture` returns null |

Evidence, in order of strength:

1. **Measured, this task**: `ComposeFieldCarrySeamTests.EditedBookmarkParagraph_StillCarriesTheTarget_SoACarriedRefResolves`
   edits the *bookmark's own paragraph* in `ref-cross-references.docx` — the worst case, where the target is
   re-authored rather than cloned — and asserts `_Ref_Confidentiality` is still there afterwards, with both
   fields still naming it.
2. **Measured, task 041**: `notes/edited-block-loss.md` records `ref-cross-references.docx` moving from ❌
   (`bookmarkStart`, `bookmarkEnd` among its differing paths) to ✅ intact once `CarryBookmarks` landed.
3. **Enforced continuously**: the `bookmark` row in `ComposeResidualLossParityTests` carries a `null` code,
   so the preserve direction fails the build if a future change starts dropping them.

The comment has been corrected in place. The two residual cases (user-deleted target; R6 fail-open) are
recorded there rather than left implied.

### 2.2 A keyword allow-list makes one document behave two ways

Loss in Compose is **per-edited-block**. A keyword split would therefore freeze the `DATE` field in the one
paragraph the user happened to edit while the other 39 pages keep live `DATE` fields — with nothing on
screen distinguishing them. A document that behaves two ways for the same construct is harder to trust, and
harder to diagnose, than one that behaves either way consistently.

### 2.3 Freezing is not the null action

The instinct is that flattening is the safe, do-nothing option. It is not: it is a mutation with a delayed
cost. A `REF` flattened to `4` keeps printing "Section 4" after the agreement renumbers to 5 — a wrong
cross-reference presented as ordinary prose, in a document class where cross-references are load-bearing.
Word's broken-reference text is an uglier failure and a **better** one, because somebody sees it.

Between a loud failure and a silent wrong answer, the loud one wins. That is the same judgement task 048
made for `w:sym`: writing the resolved look-alike (or the U+FFFD placeholder) into the file was worse than
carrying the identity, precisely because nothing looked wrong afterwards.

### 2.4 …and the display does not change anyway

The carry re-emits the instruction **and** the cached result. Word shows the cached result until something
asks the field to update, so the save is visually a no-op. The re-evaluation hazard the allow-list existed to
dodge can only materialise when the user presses F9, prints, or opens a `dirty` field — the same moment it
would have materialised in the document had Compose never touched it.

---

## 3. Per class

| Class | Decision | Reasoning |
|---|---|---|
| `PAGE`, `NUMPAGES`, `DATE`, `TIME` | **Carry** | Re-evaluate harmlessly; freezing them in one paragraph only is the inconsistency in §2.2 |
| `REF`, `PAGEREF`, `SEQ`, `STYLEREF` | **Carry** | Target survives (§2.1). A frozen cross-reference goes silently wrong (§2.3) |
| `HYPERLINK` (incl. `\l` to a bookmark) | **Carry** | Same target argument. (Distinct from the `w:hyperlink` *element*, which has its own `internal-link-flattened` handling on the projection side and is untouched here) |
| `TOC`, `INDEX` | **Flatten** — structural | The field spans paragraph marks, so its `begin`/`end` are in different containers and the scan never closes. There is no complete field to carry. Note: the `PAGEREF` fields *inside* a TOC's result paragraphs are intra-paragraph and **are** carried |
| Nested — `{ IF { PAGE } = 1 … }` | **Flatten** — structural | The scan folds an inner field into the outer span, so the recoverable instruction is a concatenation (` IF ` + ` PAGE ` + ` = 1 "…" "…" `). Re-emitting that would author a **different** field. Detected by `FieldScanState.MaxDepth > 1`, and for `w:fldSimple` by a descendant `SimpleField`/`FieldChar` |
| No recoverable instruction (empty `w:instr`, no `w:instrText`) | **Flatten** — structural | Nothing to carry |
| Unknown / vendor instruction | **Carry** | Carried **verbatim**, never interpreted. Compose does not need to understand a field to reproduce it, and refusing what we do not recognise is how a preservation list becomes a feature list (ADR-049) |
| `w:fldLock="true"` (any class) | **Carry, and carry the lock** | The one way this change could be worse than freezing: dropping `fldLock` converts a field the author deliberately froze into a live one. Asserted by `LockedField_StaysLocked_SoAFrozenFieldIsNotSilentlyMadeLive` |
| `w:dirty="true"` | **Carry the flag** | The document's own instruction about when the field may change; dropping it silently suppresses an update the author asked for |

**Neither escalation trigger fired.** The first (a class carryable only by re-evaluating to something the
user did not see) is closed by §2.1 + §2.4. The second (the projection not preserving the INSTRUCTION) was
true of the code as found — `FieldScanState` swallowed `w:instrText` runs — but the POML's own step 3 scopes
the projection change to this task, and it is a five-line accumulation in a state object that already walked
those runs. Stopping to report a gap the task was chartered to close would have been ceremony.

---

## 4. What the carry does NOT preserve

Stated here and on the published list, because "carried" must not be read as "byte-identical":

- The result run's properties **beyond bold / italic / underline** — `w:noProof`, a character style, a
  colour. The result is re-authored from the marker run's marks, which is the same property tier every other
  run in an edited block gets.
- **Correction (task 057, 2026-08-25):** the line above is true of the SERVER path only. On a KEYSTROKE
  edit, bold / italic / underline are lost too. The projection sets them on the field run from the result
  run's `rPr`, but an opaque atom declares `marks: ''`, so the client cannot carry them and `AppendField`
  receives `false` for all three; `ComposeBlockMerge.InheritRunProperties` then applies the edited
  paragraph's DOMINANT run properties. A bold cross-reference in an otherwise-plain paragraph therefore
  comes back plain. Same posture task 048 shipped for tab/symbol. Fixing it needs server-side atom
  attributes and is not in 049's or 057's scope — recorded so the published list is not read as
  promising more than the code delivers.
- A result that was **several differently-formatted runs** comes back as one.
- Field markup **inside** a text box (`w:txbxContent`, `mc:AlternateContent`) is not entered — those regions
  are carried whole or not at all, which is unchanged and deliberate.

---

## 5. The form is reproduced, not normalised

`w:fldSimple` comes back as `w:fldSimple`; the `w:fldChar` run sequence comes back as a run sequence. Word
treats the two as equivalent and would happily accept either, so normalising is tempting and would have been
simpler (one emit path instead of two).

It is still wrong. A save is not licensed to rewrite what the file contains because two encodings render
alike — the same rule that makes task 048 carry a symbol's code point rather than its resolved glyph. It also
has a mechanical consequence worth stating: `ComposeResidualLossParityTests` counts element local-names, so a
silent normalisation would read there as one form being lost and the other being invented.

Schema note: `w:fldSimple` is an `EG_PContent` element and **may not** sit inside `w:ins`/`w:del`, so the
simple form puts the revision wrapper *inside* the field around its result run — Word's own nesting, and the
same shape `ComposeDocumentRenderer` already uses for a revised hyperlink. The complex form is plain runs and
needs no such care.

---

## 6. Measured

`ComposeResidualLossParityTests`, 2026-08-25:

| Family | Untouched block | Edited block | Code emitted |
|---|---|---|---|
| `fldSimple` | 1/1 kept | **1/1 kept** | *(none — carried)* |
| `fldChar` | 2/2 kept | **2/2 kept** | *(none — carried)* |
| `fldNested` | 6/6 kept | 0/6 | `field-flattened-to-text` |

The `fldNested` row was added in the same change on purpose. Retiring both carried rows would otherwise have
left `field-flattened-to-text` as a code the published document names and the renderer can no longer emit —
direction B's failure mode (a list that drifts by accretion into fiction) arriving through the front door
rather than by neglect.

Corpus arm, `ref-cross-references.docx` through the real renderer: the `REF` field's own paragraph, the
`PAGEREF` field's own paragraph, and the bookmark's own paragraph each edited in turn; instruction, cached
result and bookmark all intact in every case.

---

## 7. The client half is NOT in this task, and the carry is incomplete without it

**Stated plainly because a green suite is not the same as a working feature.**

The save's edited block is rebuilt from the **client's** nodes (`docxBridge.ts` → `mergeLeafBlock` → the
rebuild tier). A `field` atom is opaque to the client mapper: `renderableAtomText` returns null for it, so
the atom contributes nothing and the field never reaches the posted model. This task's server change makes a
field survive **projection → model → renderer**, which is what the parity test and the seam tests measure and
what any server-side model round trip (including `ComposeBlockMerge`'s baseline re-projection) exercises. It
does **not** by itself make a keystroke edit in the browser preserve a field.

What this task ships toward that: the read-side atom now carries its payload —
`data-field-instr` (+ `data-field-complex` / `-locked` / `-dirty`) on the `data-atom-kind="field"` span, the
same mechanism task 048 added for `w:sym`'s font + code point. The **presence** of `data-field-instr` is the
contract: a nested or instruction-less field gets no payload, so the client cannot return something the
server would refuse. The producer exists; the consumer does not.

**Remaining work (client, `Spaarke.Compose.Components`, not this task's file boundary):** teach
`collectSegments` / `buildRunsFromNode` to map a `field` atom back to `{ field: { instruction, cachedResult,
complex, locked, dirty } }`, exactly as task 048 did for `tab` / `symbol`. Until that lands, the field carry
is reachable only from server-side model paths.

**ADR-049 I-2 note.** A field instruction is closer to markup than a font name is, and task 048's own remarks
argued a field is *not* self-describing. The distinction that makes this acceptable: the client neither
parses nor authors the instruction — it hands back the same opaque string it was given, and the server alone
decides whether it can be re-emitted. Flagging it rather than burying it, because it is the kind of judgement
a reviewer should get to disagree with.
