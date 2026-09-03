# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-09-02 (by `context-handoff`) — end of a long session. **Branch PUSHED; tree clean.**
> **Recovery**: read Quick Recovery, then §UX (the live backlog), then §U8, then §R0.
> Everything below "Full State" is preserved history from earlier checkpoints.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Where we are** | **R8's own gates are CLOSED.** Track A passed (owner UAT: saved, reopened, edits held). `section-break-flattened` **ACCEPTED** — the signed residual-loss set is now **six rows**. The project has since absorbed an owner-approved **UX backlog**, most of which is now done and deployed. |
| **Branch** | `work/spaarkeai-compose-r8` @ **`0f5525bea`** — **pushed, 0 unpushed, tree clean.** PR **#924** retitled to cover the whole release; left as a **DRAFT deliberately** (in-flight UX work — do not promote to ready until the backlog below is finished or the owner asks). |
| **Next Action** | **1) Numbering engine** — client renumber via ProseMirror `appendTransaction`, gated by a shared parity corpus BUILT FIRST (`tests/fixtures/compose-numbering-parity/cases.json`, mirroring the #699 pattern). Design: `notes/uat/numbering-editing-design-options.md` (⚠️ read its RETRACTION block — the write path already exists). **2) Item 8** — see §U8; needs a `changesText` producer + trigger, NOT a wiring job. **3) Editable spacing** (UAT item 6) — grouped with numbering by owner decision. |
| **Suite** | Client **1,406/1,406** · Compose server **1,948/1,948** · ArchTests **187/187** · SprkChat **376/376** · build 0 errors — **all re-run AFTER the 24-commit master merge**, not carried over from before it |
| **Deployed** | `spaarkedev1`, BFF + `sprk_spaarkeai` **together** (NFR-05). BFF **45.43 MB** vs same-day fresh master **45.42** = **+0.01**. `/healthz` 200. |
| **CI** | ⚠️ **NOT verified.** `gh pr checks 924` reports *"no checks reported"* and `gh run list --branch` shows **no runs for today's commits** — the newest is `98558943b` (2026-09-01). Actions is **not** broken: other branches got runs today. So this is specific to this branch/PR, and the likeliest cause is that **#924 is a DRAFT**. **Re-check, and if runs still never appear, mark the PR ready (or push a trivial commit) to see whether draft status is the gate.** Do not read silence as green. |
| **Master merge** | ✅ **DONE this session.** Master had moved **24 commits** (UAC-r2 #931 merged) and GitHub reported #924 `CONFLICTING`/`DIRTY`. Merged `origin/master` into the branch (**never rebase**) — exactly **ONE** conflict, `.claude/CHANGELOG.md`, two independent appended entries, resolved by keeping both. Post-merge, ALL RE-RUN: solution build **0 errors** · ArchTests **187/187** (up from 181 — master added six) · client **1,406/1,406** · Compose server **1,948/1,948**. |

### ⚠️ Coordination — PR #932 (`unified-access-control-r2`) overlaps on TWO TEST FILES

`ComposeService.cs` itself does **not** overlap. These two do, and whoever merges second reconciles:
- `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs` — we moved 3 census entries
  `ClientSupplied` → `ServerDerivedRecord`; they add an external-upload write sink.
  **A census renumber presents as one stale entry + one undeclared site** — only both halves together
  distinguish it from a deletion.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceCreateOnSaveTests.cs`

---

## UX. The owner-approved backlog — order was **5 → 6 → 2 → 7 → 8**

| # | Item | State |
|---|---|---|
| **5** | Toolbar restructure, sub-items a–i | ✅ **DONE + deployed** (`c0751e0d9`) |
| — | Editor typography (headings collided when wrapping) | ✅ **DONE + deployed** (`4887bc98a`) |
| — | Numbering visibility (`data-projected-list`) | ✅ **DONE + deployed** (`b323c2718`) |
| **2** | Formatting notices → one collapsed row + popover | ✅ **DONE + deployed** (`e8ea2bbce`) |
| **7** | Injected Assistant turns pin to top | ✅ **DONE + deployed** (`8d9b31eb8`) |
| — | Document line spacing carried into the editor (READ path) | ✅ **DONE + deployed** (`0f5525bea`) |
| **6** | **Editable** spacing (the Spacing menu) | 🔲 grouped with numbering — **write path** |
| — | **Numbering menu**: toggle · `<` `>` level arrows · type (1./a./i.) | 🔲 owner's design; `<`/`>` small, type = new `ComposeNumberingAuthor` schemes |
| **8** | Document change summary | 🔲 see §U8 — bigger than first stated |

### The spacing ladder (owner asked "is there a less complex approach?" — yes, and rungs 1+2 shipped)

1. ✅ **Editor typography** — the readability fix. Headings inherited FluentProvider's **fixed 20px**
   line-height; a ~28px glyph in a 20px line box is why multi-line headings collided. Unitless ratios.
2. ✅ **Carry the document's own `w:spacing`** — read path only. `w:lineRule` is load-bearing: `w:line` is
   a MULTIPLE in 240ths under `auto` (360 = 1.5×) but TWIPS under `exact` (360 = 18pt), and **Word OMITS
   the rule when it means auto** — so absent must map to the multiple reading.
3. 🔲 **Editable spacing** — model + renderer. **The risk that keeps it out of a UX task**: the moment the
   model owns spacing, `InheritProperties` must change, and getting it wrong flattens spacing on every
   edited paragraph — the `paragraph-style-flattened` defect replayed.

---

## U8. Item 8 (document change summary) — CORRECTION, twice over

The round-1 note called this **"a wiring job, not a new capability"** and said its trigger was the
return-from-Word reanchor flow. **Both were wrong**; a repo-wide search settled it:

| Piece | Status |
|---|---|
| Consumer type, Action, input + output schemas | ✅ exists |
| Result renderer (`composeResultFormat.ts`) | ✅ exists |
| Server operand binding (`ContextBinder` `changesText`) | ✅ exists |
| **A client producer of `changesText`** | ❌ **does not exist** |
| **Any live trigger** | ❌ **does not exist** |

`AnnotationReanchorService` states the opposite of the old claim outright: *"the human-friendly change
summary is a SEPARATE gated capability … that DOES call the model; this engine does not."*

**The binding constraint that survives both corrections**: the action was pulled from the selection
toolbar because **without real change data the LLM fabricates a phantom "[Insertion]"**. Any trigger MUST
refuse when there are no tracked changes rather than dispatch an empty operand — which makes the
**producer** the load-bearing piece, not the button. Render target: `AgreementReviewSummaryPanel`.

---

## Method rules that cost real time when forgotten (carried forward)

1. **Run the negative control.** Every fix this session shipped with one; two of them found real problems
   (an in-test extractor that was line-anchored; a Direction-A scan that prose satisfied).
2. **Assert a seeded mutation is IN THE FILE before spending a suite run**, and that the build is green —
   a stale binary reports a PASS.
3. **`dotnet test` is ~7–9 min for the Compose filter.** Batch.
4. **Verify the deployed bundle by a STRING LITERAL, never a symbol name** — minification renames symbols,
   so an absent function name is not evidence of a stale build.
5. **Measure publish size against a SAME-DAY fresh master build with the SAME zip tool.** The recorded
   baseline ages; that error once overstated this project's delta 46×.
6. **A screenshot is evidence about a BUILD.** UAT round 2's heading report came from a stale bundle —
   the tell was toolbar text that had already been removed. Check the build before diagnosing the code.

---

## R0. UAT ROUND 1 (2026-09-02) — read `notes/uat/uat-round-1-findings-and-plan.md` for the full analysis

**Confirmed working**: PDF intake (open → editable → save → honest `pdf-intake-*` warnings) · the AI redline
stream (tracked insert/delete, "2 suggested edits pending", Accept-all, per-suggestion rationale).

**8 items returned. Two findings changed their scoping — do not re-derive these:**

1. **Items 3 + 4 are ONE finding and a KNOWN DEFERRAL, not an R8 regression.**
   `composeNumberAtomExtension.ts`'s header states it outright: legal numbers are computed SERVER-SIDE AT
   LOAD and painted as a ProseMirror **view decoration** (never a doc node — it must not shift the text
   offsets the redline/reanchor table indexes), and the native `<ol>` marker is suppressed
   **unconditionally**. So "remove numbering doesn't renumber" (decoration is a load-time snapshot) and
   "add numbering does nothing" (a new list has no server number to paint) are the same gap. The header
   names it **"R5 G3, explicitly OUT of R4.5 scope; escalate rather than converting this to a doc node."**
   Fixing = client renumbering engine (the two-engine drift this project exists to prevent) **or** a server
   round-trip per structural edit. **DESIGN ANSWERED 2026-09-02** →
   `notes/uat/numbering-editing-design-options.md`. Headline: the blocking constraint was misread — it rules
   out INLINE CONTENT, not a NODE ATTRIBUTE, and the number already IS one (`data-computed-number`). So the
   rendering mechanism needs no change; the only missing piece is **recomputation**, via ProseMirror's
   maintainer-prescribed `appendTransaction`. Recommended: **client engine for immediacy + server
   authoritative + parity enforced by the #699 shared-corpus pattern**. The HARDER half is not renumbering —
   it is authoring/removing `w:numPr` on the write path (a new list has no numbering definition to inherit).

   **Owner scope call (2026-09-02): "whether we continue r8 or start a new project is semantic — I'll follow
   your better judgement." DECISION: sequence, don't merge.** R8 closes on its own thesis (save reliability +
   fidelity) — it is deployed, green, every item closed; holding it open for a toolbar redesign delays value
   already earned. The UAT follow-on (items 2/5/6/7/8 + numbering) runs as the next project with its own
   design gate, because numbering touches the WRITE path and must not ride in on a UX task.

2. **Item 8 is a WIRING job, not a new capability.** `compose-summarize-word-changes` is a live consumer
   type with a binding row + client action, deliberately pulled from the selection toolbar because without
   real change data **the LLM fabricates a phantom "[Insertion]"**. The NDA analogue is
   `AgreementReviewSummaryPanel`, reusable. Binding rule to carry: **never fire it without real change data.**

**✅ U-0 — REPRODUCED, ROOT-CAUSED, FIXED (2026-09-02).** Full record: `notes/uat/u0-heading-style-loss.md`.

The projection never puts a numbered heading in a list (`headingLevel is null ? ListInfo(p, ctx) : null`),
so `isActive('orderedList')` is FALSE on it and **the "remove numbering" click was the toggle ADDING a
list**. One fact explains the whole screenshot. Measured losses on the real extension set: heading level
flattened · `computedNumber` → null · **`paraId` RE-MINTED** (orphaning anchored comments/redlines).
Irreversible; `keepAttributes: true` recovers none of it. It is a *fidelity* defect, not just UI: the save
re-renders the changed block from the model and `IsModelDeterminedStyle` treats Heading1-6 as model-owned,
so a toolbar click silently flattens a real Word heading in the `.docx`.

**Fix**: `listToggleWouldDestroyBlockIdentity` refuses both list toggles on a heading or a server-numbered
block, with an actionable hover reason. Ordinary unnumbered paragraphs unaffected (R5 task 011 stands).
Partial fixes rejected: carrying `paraId` alone trades a loud loss for a quiet one; authoring numbering is
impossible without a `w:numPr` definition, so the control would stay a broken promise. 11 tests, **both
negative controls run** (revert wiring → 3 red; force predicate always-true → 3 red the other way).
Body/Heading menu probed and **clean** — it preserves paraId AND computedNumber, so scope is measured.

**Hand-off to the numbering project**: the native `<ol>` marker is suppressed **unconditionally**, so an
editor-created list shows no number even born-in-editor — UAT item 4 is universal, not loaded-only. The
obvious quick fix (scope the suppression to projected lists) collides with invariant F-3 (never fabricate a
number for an unresolvable `numId`); distinguishing them needs a projection-emitted marker on the `<ol>`.

**⚠️ UAT sections A + B were NOT exercised**: (A) edit one paragraph of a real `.docx`, save, reopen,
confirm untouched content byte-identical — the entire subject of Track A; (B) the **`section-break-flattened`
accept/decline**, which changes the owner-signed residual-loss set from five rows to six. **R8 cannot close
without both.**

---

## R2. DRIVE PROVENANCE — the brief (next task)

**What it is.** `ApplyTemplateAsync` and `SaveAsync`'s replace branch both take `driveId` from the
CLIENT (route body / `request.DriveId`) and write bytes there. The authorized `sprk_document` row
already knows the answer (`sprk_graphdriveid`), and the server does not consult it.

**What it is NOT.** Not the app-only live-hole class. Every write is OBO, so SPE authorizes it as the
user — a caller cannot reach a drive they could not already reach. The defect is **provenance**: the
`sprk_document` record says the document lives at drive X while the save writes to drive Y, so the
record and the bytes diverge and the audit trail is wrong. Same reasoning as #858's `ContainerId`
deletion ("a field that still EXISTS is a capability that still exists"), one level down.

**The resolution already exists — reuse it.** `ComposeRecordResolution.TryFindDocumentByGraphItemIdAsync`
resolves speId → row, and after #781 it survives a duplicated/broken alternate key. **Extend its column
set to include `ComposeService.GraphDriveIdAttribute`** (it currently fetches DocumentId + the two
FR-C3 dedup columns); the sibling `TryFindDocumentByTransientKeyAsync` already fetches drive+item and
returns them as `TransientKeyMatch`, which is the shape to mirror.

**The one real design decision — fail-closed vs. fall-back.** Legacy rows may carry an empty
`sprk_graphdriveid`: `PromoteIfEphemeralAsync`'s create branch documents that a row without the full
SPE pointer makes downstream readers 409 with "No file is attached", which implies rows predating that
fix exist. So a hard fail-closed can break saves on real documents. **Recommended**: use the row's
drive when it has one and IGNORE the client's; log at Warning when they differ (that divergence IS the
signal); fall back to the client value ONLY when the row has no drive id, logged. An attacker cannot
make a row's drive id disappear, so the fallback covers legacy data, not an attack path. State this in
the PR rather than letting it read as a half-measure.

**Then the census moves honestly**: the 3 `ComposeSaveStorageCoordinator` entries + the apply-template
caller note in `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`. Remember the
2026-09-01 lesson — a renumber presents as one stale entry + one undeclared site, and only both halves
together distinguish it from a deletion.

**Sites**: `ComposeService.cs` — `ApplyTemplateAsync` (~T1 metadata read + the replace) and the save
replace branch (`request.DriveId` at ~1871–1897). `ComposeSaveEndpoints.cs:80` currently 400s when
`driveId` is absent from the body; that validation should survive the change (the client still sends
it) but its *meaning* becomes "the client's claim", not "the write target".

---

## R3. CARRIED ITEMS — what each one actually needs

| # | Item | What it needs | Owner action? |
|---|---|---|---|
| 1 | **`Repair-ComposeIdentityKey.ps1` never executed** | It has been written + syntax-checked, never run against real Dataverse. Report mode is READ-ONLY (`.\scripts\Repair-ComposeIdentityKey.ps1` with no switches) and safe anywhere; it needs `az login` + Dataverse admin. Run it against `spaarkedev1` first — that both validates the script and tells us whether dev is still clean after the 2026-08-17 hand-cleanup. `-Apply` only on an owner say-so. **Prod MUST have `sprk_graphitemid_uk` = Active before Compose ships there**, or every save 500s (the original incident). | Offered 2026-09-01; owner said "i don't know what that is but ok" — the explanation was given, no decision taken. Agent may run REPORT mode unprompted; never `-Apply`. |
| 2 | **Letter/roman corpus fixture** (open half of #698) | A Word doc numbered `1.` → `a.` → `i.` — i.e. Word's **Home → Multilevel List → the `1. / a. / i.` variant**. **The doc the owner supplied twice is NOT it**: `notes/Word doucment paragraph number test.docx` (21,113 B) and the earlier OneDrive copy (21,192 B) are the SAME document re-saved — same `numId=7`, decimal at every level. Already in the corpus as `style-inherited-numbering.docx`; do not add it a third time. Its `numbering.xml` DEFINES lowerLetter/lowerRoman in unused abstractNums, so no paragraph references them. | Yes — needs a document only the owner can produce. Low priority: the resolver's letter/roman PARSE is proven; what is unproven is the numbering engine deriving `(b)`/`(iii)` from real `numbering.xml`. |
| 3 | **`section-break-flattened` doc row** | `COMPOSE-WRITE-RESIDUAL-LOSS.md` has no row for it. It is now a per-EDITED-block code like the constructs already tabled there, so it arguably belongs. Settle at 090 wrap-up. | No |
| 4 | **Client citation-resolver parity has no forcing function** | `composeCitationResolver.ts` mirrors the server `CitationResolver`; parity rests on ported test cases + `@see` comments. Nothing fails if the server parser gains a shape the client lacks. Its sole consumer is `ComposeEditor.placeAdvisoryComments` — which IS #699 — so fix it there, not separately. | No |

---
| **Verify with** | **`dotnet build`** at the SOLUTION root — not one project (see §A2 for why that distinction cost real time) |

---

### 🔓 THE FREEZE IS LIFTED (2026-09-01) — `ComposeService.cs` is editable again

**UAC-r2's PR #887 MERGED as `13a1f5a4a`.** Their `ComposeService.cs` work (`c820b3f8f` — filename
sanitization at every SPE upload site) is on master. Our public promise on
[#858](https://github.com/spaarke-dev/spaarke/issues/858) — *"You will not have to rebase"* — is
**discharged**. Nothing in this project is waiting on them any more.

**Now unblocked**: 070 clusters **5a** and **2a/2b** · **#776** (apply-template `If-Match`) ·
**#781** (save-identity self-heal). #858 itself is still OPEN — theirs to close, not ours.

> **The trap that was here, recorded so it is not repeated.** The unfreeze trigger used to read
> *"their comment on #858"* — while our own last comment there opened *"✅ DEFINITIVE — you are
> unblocked. Nothing here needs a reply."* We asked for a signal and told them not to send it; waiting
> on it would have blocked us forever. **An unfreeze trigger must be something you can OBSERVE
> yourself** (a PR merging, a file changing on master), never a message another party has to volunteer.

**The coordination record is `notes/response-to-unified-access-control-r2-2026-08-27.md`** — its
`# ✅ DEFINITIVE STATUS` block at the top is the current agreement; everything below it is history. The
sibling `coordination-from-*.md` is THEIR document, received — do not edit it.

---

## R1. THE REMAINING WORK — one list, owner-directed "finish everything" (2026-09-01)

**Owner directive**: *"we need to get 070 and #777 and any other work completed — the order of operation is
up to you… the critical focus is on getting everything, all tasks, all issues fixed and completed."*
Also binding, same message: **do not display warnings the user cannot act on.** Routine docx→TipTap format
reconciliation is expected and must not be surfaced.

### ⛔ THE ONE OPEN QUESTION FOR THE OWNER — #698 corpus fixture

**Ask**: a Word document whose clauses number down to **letter + roman sub-items** — the
`4.2(b)(iii)` shape. Section → `4.1`/`4.2` → `(a)`/`(b)` → `(i)`/`(ii)`/`(iii)`, as real firm
documents number schedules and sub-clauses.

**Why it cannot be synthesised**: `CitationResolver` resolves `"4.2(b)(iii)"` today against in-memory
numbering chains plus a decoy, which proves the RESOLVER. It does not prove the **numbering engine**
derives that label from real Word `numbering.xml` — the two-engine drift this project exists to
prevent. A hand-built fixture would encode our own assumption about how Word numbers sub-items and
then pass against itself. The corpus is real documents for exactly this reason.

**Nearest existing fixture and why it does not cover it**: `multilevel-1-1-1.docx` is decimal
(`1.1.1`); no corpus document uses letter/roman sub-numbering.

**Where it goes**: `tests/fixtures/compose-corpus/` (Git-LFS — `*.docx filter=lfs`), suggested name
`letter-roman-subitems.docx`. Any real document works; it can be redacted, and content does not
matter — only the numbering scheme does.

**The OTHER half of #698 needs nothing from the owner.** "Confirm the `CitationResolver` consumer
contract" is answerable from the code: consumers exist and are live (`ComposeAnchorResolver.cs`
server-side, `composeCitationResolver.ts` + `Spaarke.Communication.Components/logic/citations`
client-side). Whether they want a `PublicContracts` wrapper is decided by reading them, not by asking.

### Done 2026-09-01 (later session — committed, NOT pushed)

| Commit | What |
|---|---|
| `a39c7abbe` | **#698 fixture** — owner supplied a real Word doc; added as `style-inherited-numbering.docx`. Its value is **style-inherited numbering**: three paragraphs carry NO `w:numPr` and are numbered by `ListParagraph`'s own `numPr` in `styles.xml`. Our engine already handles it (FR-12), so this is a guard, not a fix. **The negative control corrected the test's own claim**: dropping the style lookup does NOT shift deeper labels (Word gives an un-incremented level its `start`), so `1.1.1.` is unaffected — the damage is to the style-numbered paragraphs and their siblings (`1.2.`, `2.1.`) only. One of the four tests is decorative against that mutation and is labelled so. |
| `1789e9d08` | **#781 CLOSED** — all three remaining pieces. **(2) self-heal**: the alt-key lookup swallowed BOTH identity faults as not-found, sending an EXISTING document into the create branch whose upsert failed on the same key. A column query answers in both fault states → lands on the idempotent branch → no third row, and existing-document saves survive a Failed key. **Deviations from the issue text, deliberate**: oldest `createdon` (not newest `modifiedon` — `modifiedon` moves, so concurrent saves could pick different canonicals) and **nothing is deleted** on a user's save. **(3)** `scripts/Repair-ComposeIdentityKey.ps1`, same canonical rule as the runtime heal. **(4b)** `ComposeIdentityKeyHealthCheck` — needed *because of* (2): the heal turns a loud 500 into a quiet log, so the signal had to be restored. Degraded-never-Unhealthy + `catalog` tag so it cannot touch `/healthz` liveness. No interface widened (`TryUnwrapServiceClient` + the `protected virtual` fetch seam already existed). 18 tests; both negative controls fired. |
| *(uncommitted)* | **#777 `section-break-flattened`** — the last of its three codes. It fired at PROJECTION (open), whole-document; but `Capture` clones an untouched block **including** its `pPr/sectPr`, so only an EDITED paragraph loses it. Moved to the save path, per edited block. **KEPT, not retired** — unlike the other two its premise is still true. Also retired the 023-F1 promotion predicate (a hand-maintained mirror of the renderer's condition) in favour of a value comparison. Client copy for the two retired codes removed per the file's own Direction-B rule. Two negative controls, each firing on its own test. |

### Done earlier 2026-09-01 (committed, NOT pushed)

| Commit | What |
|---|---|
| `4f26c43fb` | **#777 `paragraph-style-flattened` — fixed.** `ComposeBlockMerge.InheritProperties` excluded `w:pStyle` wholesale; now scoped to model-owned styles via `IsModelDeterminedStyle` (Normal / Heading1-6 / ListParagraph). An UNMODELED style (firm body style, Quote, localized `Überschrift1`, numbered clause style) is carried onto an edited block. |
| `5593b9d24` | **Pinned it.** 11 seam tests, `ComposeParagraphStyleInheritanceSeamTests`. **Negative control RUN**: reverting the fix fails 5 of 11 and passes 6 — the detector fires on the regression and stays quiet on what it protects. |
| `81295b210` | **#777 `indentation-dropped` + `paragraph-style-flattened` warnings RETIRED** per the owner directive. Both premises were falsified (task 041 inheritance, then `4f26c43fb`). They fired per-paragraph whole-document at OPEN → "×84 / ×85" on an untouched 40-page contract. |
| `7324ad82f` | **070 cluster 2b** → `ComposeRecordResolution.cs`. Mutation pass found a REAL hole: `TryFindDocumentByTransientKeyAsync` could match NOTHING and all 1,813 tests stayed green — the transient-key dedup guarding "the 8-duplicate defect". Closed with a KEY-SENSITIVE contract test. |
| `d14a9cb78` | **070 cluster 2a** → `ComposeCreateOnSavePromoter.cs`. The four result-shaping helpers moved too (measured: `SaveAsync` calls them all, and its transient branch IS create-on-save). Also fixed an orphaned doc comment that had made `PromoteIfEphemeralAsync` read as "memory capture". |
| `1f1a4662a` | **070 cluster 5a** → `ComposeProfileRetriggerGuard.cs`. Mutation seeded BEFORE the move **survived all 1,814**: the G10 storm guard had NO test in either direction. Closed with 6 seam tests; re-ran the same mutation → 3 red. |
| `675a7fb3d` | 070 marked ✅ COMPLETE. `ComposeService.cs` **4,427 → 2,114**. No waiver to delete (ratchet retired 2026-08-20). |
| `27c5b2f16` | **#776 CLOSED** — apply-template asserts the version it merged. Added `rebaseOnConflict` (default true; apply-template false) because the save path's retry would have clobbered anyway, making the If-Match decorative. Added the missing 409 endpoint mapping. |
| `3810ce303` | **Merged #858** from UAC-r2 + reconciled: 3 conflicts in `ComposeService.cs`; ported cluster 2a onto THEIR post-#858 bodies; census entry recorded as MOVED-not-fixed and the dedup sink renumbered #2 → #1. |

**Final state: build 0/0 · ArchTests 176/176 · full BFF suite 11,779 passed / 0 failed / 57 skipped ·
23 commits ready · 0 behind master · 0 uncommitted.** `ComposeService.cs` is 2,429 lines post-merge.

### The remaining work — ordered (2026-09-01)

**Batch by TOOLCHAIN, not by issue number.** The server items share files already understood and one
build/test cycle (~9 min for the full BFF suite); the client items share one `npm` cycle. Interleaving
them doubles the cycles for no benefit. This is the same reasoning that made 070's clusters one pass.

| # | Work | Where | Notes |
|---|---|---|---|
| **1** | ✅ **#781** save-identity self-heal — DONE `1789e9d08` | `ComposeService.cs` / `ComposeCreateOnSavePromoter.cs` / `ComposeEndpoints.cs` | 3 of 5 pieces remain: **#2** self-heal when the promote upsert resolves a graphitemid to MULTIPLE rows (pick canonical by rule), **#3** retroactive dedup tool, **#4b** runtime key-health probe. Shipped already: graceful 409/503 mapping + `scripts/Verify-ComposeIdentityKey.ps1`. Partly served by the dedup test added in `7324ad82f`. |
| **2** | ✅ **#777 `section-break-flattened`** — DONE `1f88817c7` | `ComposeContentModelProjector.cs` + `ComposeBlockMerge.cs` | The LAST of its three codes and the only one still REAL loss: an interior `w:sectPr` on an EDITED paragraph. `SectionProperties` is excluded from inheritance because the renderer detaches/re-attaches the TRAILING sectPr — an interior one has no carrier. **KEEP this warning** (unlike the two retired in `81295b210`): it is real AND actionable ("open in Word"). |
| **3** | ✅ **Drive provenance** — DONE 2026-09-01 | `ComposeService.cs` + `ComposeRecordResolution.cs` | Both write paths into an EXISTING item now resolve the drive from `sprk_graphdriveid` on the authorized row (`TryResolveRecordedDriveIdAsync`, routed through `TryFindDocumentByGraphItemIdAsync` so it inherits the #781 self-heal). `SaveAsync` folds the result onto the request so reads AND the write move together; `ApplyTemplateAsync` renames its parameter to `requestedDriveId` (a read-merge-write converted at the write ONLY would read one drive and overwrite another). Fallback to the caller's value when the row records no drive — declared, logged, and pinned by a test; a hard fail-closed would break legacy rows to close a hole OBO already closes. 3 census entries `ClientSupplied` → `ServerDerivedRecord`. 11 tests, 3 negative controls. Reasoning: `notes/drive-provenance-decisions.md`. |
| **4** | ✅ **#696** unbounded request body — DONE `4fe224ce9` | `ComposeEndpoints.cs` | `/api/compose/project` + `/upload` run synchronous OOXML projection under only Kestrel's implicit ~28.6 MB cap. Align to the 25 MB chat-attachment policy. |
| **5** | ✅ **#698** contract half — DONE (answered on the issue; no wrapper) | `CitationResolver.cs` + its consumers | Read the live consumers and decide wrapper-or-not. **Fixture half is the owner ask above** — do the contract half regardless; do not block on the document. |
| **6** | ✅ **#699** — DONE `6cca7b649`, issue CLOSED | client `ComposeEditor.tsx :placeAdvisoryComments` | **Highest user-harm item left** — a review note can attach to the wrong clause. `placed=2` where 1 expected. Issue recommends anchoring by WS-4 computed clause number via `CitationResolver` where the model supplies a section ref. |
| **7** | ✅ **#858 client cutover** — DONE `3ac433683` | `ComposeWorkspace.tsx` | Remove `containerId` from the create-on-save body (`:2086`) + the dead pre-save `resolveContainer` leg. ⚠️ **UAC-r2 §1.4: do NOT bulk-delete `containerIdRef`** — six senders address OTHER endpoints (load/documentRef/shuttle) and some legitimately still take a container. Check each against its endpoint contract. Their plan: `C:\code_files\spaarke-wt-unified-access-control-r2\projects\unified-access-control-r2\notes\plan-858-closeout.md` §1. |
| **8** | ✅ **#853** — verified on branch, issue CLOSED | — | Already FIXED by `220ddd18e` on master. Verify on master + **close the issue**. |
| **9** | **090 wrap-up** | — | `/test-diet` → refresh `COMPOSE-WRITE-RESIDUAL-LOSS.md` (two codes retired; `paragraph-style-flattened` moves from §2-lost to §3-carried) → lessons-learned → `projects/INDEX.md` + root §17 + `.claude/CHANGELOG.md`. |
| **10** | **Deploy** | — | BFF + `sprk_spaarkeai` **together, one window, same net10 tree** (NFR-05). Report publish size vs the 44.96 MB baseline. |

### Standing rules that cost real time when forgotten

1. **Assert a seeded mutation is IN THE FILE before spending a suite run.** Three separate false
   results this session (`Copy-Item` timestamp defeating incremental build · a regex miss · an LF/CRLF
   miss). Each read exactly like a clean survival, and a false survival makes you write a test for a
   hole that does not exist.
2. **A test that passes is not a test that works.** The first #776 test passed in BOTH worlds
   (fix and naive-fix) because its mock returned the same eTag from every read. Always run the
   negative control.
3. **`dotnet test` is ~9 min for the full BFF suite, ~7 for the Compose filter.** Batch accordingly.
4. **`.docx` fixtures are Git-LFS.** A checkout without `lfs: true` yields a 128-byte pointer that
   `readFileSync` happily returns — this is what kept the client gate red for 40+ builds (#921).

### Small loose ends

- **Client copy map** `ComposeBannerStack.tsx:322-341` still maps the two retired codes. Dead, harmless, but
  remove them so nobody re-derives that the server still emits them.
- **`ComposeBlockMerge.cs` contains 2 raw NUL bytes** inside a string literal (offset ~19314), which makes the
  file read as BINARY to `grep`/ripgrep — `Grep` refuses it and silently returns nothing useful. Almost
  certainly a deliberate sentinel separator in the block-canonicalisation, but confirm intent; if deliberate,
  use `\0` escape instead so tooling can read the file.
- **Compose Client Gate is at 1 of 3** greens needed before `continue-on-error: true` comes off (#921 fixed
  the LFS checkout). Two more green runs, then delete that line and it becomes a real gate.
- **#858 is UAC-r2's to close**, not ours.

---

## S3. Session close (2026-08-30) — everything merged, and the verification habit that was missing

**All three PRs merged**: #806 (`19bf65ec4`) · #905 (`369c3ea89`) · #908 (`330b9fc55`). Branch has
**0 unmerged commits**. Main repo synced.

### The mistake worth carrying forward

After #905 merged I reported "done". It wasn't. **GitHub auto-merge merges the head that PASSED CHECKS,
not the current head** — and I had pushed the client-gate timeout fix while checks were already running.
#905 merged head `47859684c`, silently leaving behind:

1. the compose-client-gate flake fix — **still live on master**, and
2. the definitive UAC-r2 coordination record — so the file path I had just given them in #858 pointed at
   the stale 2026-08-27 version. They work from master.

Both were caught only because the owner asked "where is the coordination document?", which made me look
at **master** rather than my worktree. PR #908 fixed both.

> **The check that would have caught it, and is now the rule:**
> ```
> git log --oneline origin/master..HEAD   # MUST be 0 before saying "merged"
> ```
> A merge notification is not proof that YOUR commits merged. Also note the compose-client gate is **not
> a required check** (only `Router` is), so a PR can and did merge with it red.

### Cluster 4 closed out completely

P7 (`ClearPdfSourceMarkerAsync`) is **closed** — the last of **eleven** coverage holes this session's
mutation passes found across clusters 1, 3 and 4. All nine cluster-4 mutations now die.

### The client-gate flake — fixed at the cause, and a correction

I had called the #806 client-gate failure "stale" after the suite passed locally. **It was not stale.**
It is a recurring 5-second jest timeout in suites that mount a real TipTap editor —
`redline-from-ledger` takes ~95s for 24 tests (~4s each, no headroom under `--maxWorkers=2` contention).
Fixed with an explicit `testTimeout: 30000` in `jest.config.js`, with the reasoning and the
"if a test needs >30s that is a defect in the test" rule written inline.

---

## S2-appendix. The one thing that WAS not finished (now closed)

**P7 — `ClearPdfSourceMarkerAsync` has no test.** Replacing its body with `await Task.CompletedTask`
leaves all 1,801 tests green. Its own log calls this "the marker's one unsafe direction": a session that
served a PDF then serves a `.docx` must have the marker cleared, or a later save stamps a non-PDF
document **Authored** and silently drops redlines (the SEV-1 shape UAT #1A caught once). The test that
*looks* like the guard —
`ComposePdfRefreshBaselineSeamTests.SessionThatServedAPdfThenServesADocx_DoesNotStampTheDocxAuthored` —
is not: its own in-test comment says session BINDING, not the clear, is what makes it pass. Full detail
and two concrete test designs live in the seam map under
"⚠️ OPEN: `ClearPdfSourceMarkerAsync` has no test (P7)".

---

## S2. This session, part 3 (2026-08-30) — #806 merged, cluster 4 done

### PR #806 is merged (`19bf65ec4`)

The root blocker was **177 unpushed commits**, not the four red checks — all four were stale and were
diagnosed individually before pushing (§S1). Merged via **Path A auto-merge**, which `merge-to-master`
mandates regardless of protection state.

**Two traps the skill documents and I walked into anyway — do not repeat:**

1. **Classic branch protection returns `404 "Branch protection has been disabled"` on this repo.** That
   is NOT "protection is off" — I briefly concluded it was. The real rules are **rulesets**:
   `gh api repos/{owner}/{repo}/rules/branches/master`. Master requires a PR plus the check named
   literally **`Router`** (not `CI / Router`).
2. **Direction matters in Step 2.5**: merge master INTO the branch and resolve there, never resolve on
   master. Done — clean merge, re-verified locally before marking ready.

`gh pr ready 806` was required first — auto-merge cannot be enabled on a draft. **#858 was answered**:
UAC-r2 told that #806 is merged, that their create-on-save target is deliberately untouched, and that
the region they are about to edit has NOT had the mutation treatment (76.8% branch).

### Cluster 4 — extracted, 3 of 4 holes closed

`ComposePdfIntakeCoordinator` + `ComposeCacheJson` (the shared cache-payload serializer cluster 3 left
open, renamed). Nine mutations, five died, **four survived the full suite**. The entire bytes-first
sniff (task-040 Step-9.5 MEDIUM-5) had **no test at all**, and the derived-document key's `driveId`
turned out to be a **cross-container exposure** guard. Three tests close three; **P7 is open** (above).

### CVE work — the correct fix, not the easy one

`fast-uri` HIGHs: **`ajv` was mis-declared as a *production* dependency** in TrackingFieldTrio and
EmailProcessingMonitor when only the build toolchain needs it. Moving it to `devDependencies` takes
`fast-uri` out of the shipped graph — **no version bumped, no override, no framework drift**. Both
`npm update` and an `overrides` entry were rejected first: each dragged `@fluentui/react-components`
9.66→9.68 along with it, because ANY npm write to these stale lockfiles re-resolves everything in-range.
Proved rather than assumed — removing `ajv` outright builds fine in one control and **fails the other**,
which is why it landed as a move. `linkify-it`'s override was also reverted; `npm update` resolves that
one cleanly.

**21 other client lockfiles carry stale `fast-uri` via dev-only paths** (no runtime exposure). They need
a deliberate dependency-refresh pass with PCF regression testing — not a drive-by edit, for the
re-resolution reason above.

---

## S1. PR #806 was blocked by one thing: 177 unpushed commits (resolved 2026-08-30)

The four failing checks were a symptom, not the cause. `origin/work/spaarkeai-compose-r8` was pinned at
`80f6f63bb` (2026-08-28) while local HEAD was 177 commits ahead — so **every CI result on #806 described a
tree that no longer existed**, and UAC-r2 was waiting behind a PR that had never received the work.

Each failure was diagnosed before pushing rather than assumed stale:

| Check | Verdict |
|---|---|
| Tier 1 ArchTests (MUST-NOT subset) | **Stale.** "Task 074 census: the set of BFF endpoint files is pinned" — fixed on 2026-08-29 (110→117 + the 8 Compose endpoint files classified). Locally 150/150. |
| Compose Client Gate | **Stale.** `ComposeWorkspace.redline-from-ledger.test.tsx` — run locally: **24/24 pass**. |
| Router | Aggregate of Tier 1; nothing of its own. |
| Trivy | **Not a scan failure.** The check's title is *"1 configuration not found"* — a path-filtered sidecar workflow (`build-provisioning-sidecar.yml`) that exists on master but does not run for this PR. |

**Branch pushed.** #806 is now `MERGEABLE`, CI running against real state.

**On the Trivy alert counts** (1 high / 6 medium / 4 low): verified against master rather than trusting the
PR attribution — Trivy's own summary warns alerts may be misattributed when the diff is large, and it was.
All three genuine HIGHs are byte-identical on master in lockfiles this PR does not modify. `linkify-it`
(ours) is fixed; the `fast-uri` pair in `TrackingFieldTrio` is left for its own PR against master — an
unrelated PCF surface, and bumping it inside a 491-file Compose PR adds risk to both.

### What UAC-r2 needs to know (#858)

Their stated ask was: *"tell us when PR #806 merges, or when `IComposeService.cs`, `ComposeEndpoints.cs`
and `ComposeService.cs` are stable enough for us to edit."* The honest answer has changed and they should
be told: **#806 now has the work in it and is mergeable**, and `ComposeService.cs` has gone 4,427 → 3,236
under task 070, with the **create-on-save cluster they target (2b/2a) deliberately left untouched** so
their patch still applies to recognisable code. Their sequencing ("behind #806") is now achievable rather
than blocked.

---

## S0. This session (2026-08-30) — clusters 1 and 3, two production fixes, and the seven coverage holes

### The two production loose ends — FIXED, not filed

| Defect | Fix |
|---|---|
| `DataverseServiceClientImpl` built its own `DefaultAzureCredential` (ADR-028 A4 violation) | Credential is now a ctor param; `GraphModule` resolves it **from DI** (the shared singleton, one token cache) rather than calling the factory again. `Spaarke.Dataverse` is the base layer and cannot reference the factory (FR-14), so it is passed in. |
| `Graph:UseManagedIdentity` — a key **no production code reads** | Corrected in 43 fixtures + the wrong row in `docs/procedures/test-fixture-contracts.md` they had all copied it from. |

**Three silent drifts** the credential defect carried, none of which fails loudly: inverted UAMI-key
precedence (Dataverse could authenticate as a *different identity* than Graph), blank-not-normalised (an
App Service setting cleared to blank shadows the canonical key → unpinned credential → "Unable to load the
proper Managed Identity"), and no tenant pinning (invariant I5 / FR-32 — a forcing function that must be in
place *before* a multi-tenant switch).

**A side effect worth knowing**: resolving from DI puts this path behind the test host's
`UseStubTokenCredential`, which previously could not reach it. The note in
`Spe.Integration.Tests/IntegrationTestFixture.cs` that described both defects as unfixed now records that
they are — and explains why its `IFieldMappingDataverseService` mock is kept anyway.



**The headline is not the two extractions. It is that a mutation pass over moved code found seven
places where a documented safety guarantee had no test at all** — including two that could destroy a
user's document. All seven are now closed. Full reasoning lives in
[`notes/070-composeservice-seam-map.md`](notes/070-composeservice-seam-map.md); this is the index.

| Cluster | New file | Mutations | Survived → holes closed |
|---|---|---|---|
| **1** re-anchor / stale-base | `ComposeReanchorCoordinator.cs` | 6, one per member | 4 survived the WHOLE suite → 3 holes |
| **3** save baseline + concurrency | `ComposeSaveStorageCoordinator.cs` | 7, one per member | 3 survived the WHOLE suite → 3 holes |

**The seven tests added** (all extending existing seam files — no new fixture, §11):
`ConcurrencySaveSeamTests` gained fuzzy-AUTO-not-applied · unreadable-bytes-all-orphan ·
PDF-guard × 2 entry paths · precondition-retry-rebases · missing-version-404.
`ComposePartialApplyRecoverySeamTests` gained refusing-structural-op-is-its-own-unit.

**The two most serious holes**, worth knowing even without reading the seam map:

- `GuardBaselineIsNotPdf` (task-040 Step-9.5 HIGH-2) was **completely untested**. Disabled, all 1,791
  tests stayed green — while a `%PDF-` baseline would write DOCX bytes **over the .pdf drive item**.
- The **fuzzy-AUTO rejection gate** — invariant I-7 in its sharpest form. The suite covered exact-paraId
  AUTO (1.0) and total ORPHAN (0.0) but never a score between them, so the branch separating "scored
  well on content" from "is the same paragraph" could be deleted with everything still green.

### Three method lessons from this session — do not re-derive

1. **A surviving mutation is a statement about the suite you chose, not a licence to proceed.** Four of
   cluster 1's six survived a narrow filter; re-running against all 1,791 confirmed they were real holes
   rather than filter artifacts. Cluster 3's N8 went the other way — it survived the narrow filter and
   DIED on the full suite, because `ComposeCarrierRenderSeamTests` was not in the filter. **Always
   confirm a survivor against the full Compose filter before calling it a hole.**
2. **A mutation that survives a NEW test usually means the assertion is weaker than the claim.** The
   missing-version test first asserted only `!= 200`; the mutant passed it, because a save proceeding on
   empty bytes also fails — later, and for a different reason. It had to name the specific outcome
   (404 at resolution, not a 422 after the fact) before it counted.
3. **When a guard test breaks because code moved, repairing the marker is only half the fix.**
   `ComposeWritePathTextSearchAuditTests` fired when `ResolveRevisionAuthor` widened. Fixing only the
   marker would have let the guard silently shrink to whatever remained between its two markers — the
   ~470 moved lines had been inside its slice by position alone. The new file was added as an audited
   file in its own right.

### Decisions taken this session

| Decision | Rationale |
|---|---|
| `IsBatchLevelPatchRefusal` + `HasBaselineVersionCoordinates` move as `internal static`, outside callers follow | Third and fourth time this shape has come up (after cluster 5b's signal factories). Same resolution each time: the helper lives with the code that explains it. **Expect it again on clusters 4 and 2.** |
| `ResolveRevisionAuthor` widened to `internal static`, stays on `ComposeService` | Cluster 9; two of three callers are there. A pure function of its argument — a shared helper, not a cycle. |
| `SaveStampJsonOptions` stays on `ComposeService`, widened to `internal` | **Shared with cluster 4.** Duplicating it would let two cache-payload formats drift apart silently. **Cluster 4 should take it and rename it** — "save stamp" already under-describes it. |
| `ConcurrentExternalChangeCode` stays | Its reason-to-change is the client banner contract it mirrors, not concurrency. |

---

## S. This session (2026-08-29) — 10 commits, tree clean

### What changed, in order

1. **Merged `origin/master`** (152 behind, zero conflicts). Verified PR #847 was already on master
   rather than orphaned; did NOT hand-edit the ADR-010 ceiling.
2. **Classified 8 Compose endpoint files** into the `RouteAuthorizationGuardTests` census after the
   070 split — one entry each, deliberately not a `StartsWith("Api/Compose")` prefix rule.
3. **Fixed the credential hang** (`UseStubTokenCredential` across all 52 factories) + guard.
4. **Repaired `Spe.Integration.Tests` 23 → 0** (compile break, random caller identity, per-test
   session registry).
5. **Extracted 070 clusters 7 · 6 · 5b · 8**, each verified by mutation before being called done.

### Files created this session

| File | Why |
|---|---|
| `tests/integration/Shared/TestTokenCredential.cs` | stub credential + `UseStubTokenCredential()`; carries the full diagnosis |
| `tests/Spaarke.ArchTests/TestHostCredentialGuardTests.cs` | fails the build if a factory omits the stub |
| `src/…/Services/Compose/ComposeMemoryCapturer.cs` | cluster 7 |
| `src/…/Services/Compose/ComposeAnnotationStore.cs` | cluster 6 |
| `src/…/Services/Compose/ComposeProfileDispatcher.cs` | cluster 5b (owns the 4 step signals) |
| `src/…/Services/Compose/ComposeReferenceMapping.cs` | cluster 8 (static — pure functions) |
| `projects/…/notes/test-host-credential-hang.md` | the credential defect, end to end |

Modified: `ComposeService.cs` · `RouteAuthorizationGuardTests.cs` · `CustomWebAppFactory.cs` ·
`IntegrationTestFixture.cs` · `UploadIntegrationTests.cs` · 3 csproj files (link
`tests/integration/Shared/**`) · 7 `Spe.Integration.Tests` suites (stable caller identity) ·
`070-composeservice-seam-map.md`.

### Decisions taken this session (owner-approved where noted)

| Decision | Rationale |
|---|---|
| Fix the credential at the FIXTURE, not production | §F.2 Fixture-Config-FIRST — a real credential in a test host is a non-contract value. No assertion relaxed, no production change. |
| **Signal factories move with cluster 5b; 3 outside callers follow** *(owner)* | Calling back into `ComposeService` from the collaborator would be circular; the signals describe the steps they moved with. |
| **`IndexingSignal` stays in cluster 5** *(owner)* | Cluster 5's reason-to-change ("when a document gets (re)indexed") covers it; splitting a set of 4 step signals reads worse. |
| Cluster 8 as a `static` class | Pure functions, no state or deps — a ctor + field would be ceremony. |
| Public `IComposeService` members STAY on the service | Only the policy moves; relocating an interface impl would change what `ComposeService` *is*. Applies again at clusters 2 and 3. |
| Per-test session id **and** owner in the upload fixture | Three constraints at once; see §A2. Any two are easy, which is why the first attempt failed. |

### Two things I got wrong, corrected — do not re-derive

- The recorded **`SessionOwnershipFilter` timeout hypothesis was wrong.** It could never have
  explained `ScopePersonas`, a route with no session. The cause was the credential.
- **Seeding the owner while the caller stayed random** was a non-fix. Fixing one side of an equality
  and re-running is not a diagnosis; it cost a full 7-minute cycle.

### The verification recipe that actually works here

```
dotnet build                                                   # SOLUTION root, not one project
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "…~Compose" # 1,790 — after EACH extraction
dotnet test tests/Spaarke.ArchTests/…                           # 150
git diff --stat src/…/Program.cs src/…/Infrastructure/DI/       # MUST be empty (ADR-010)
```
Plus a **seeded mutation** per extraction: if the fault survives, the suite does not traverse the
moved code — that is a coverage statement, not a licence to proceed (learned on cluster 7).

---

## A1. RESOLVED — the ten failures were two causes, not three

**The 5 ArchTests**: branch staleness, as predicted. `git merge origin/master` — zero conflicts.
Master's census guard then fired correctly on task 070's split of `ComposeEndpoints.cs` into eight
files; all eight are now classified in `GovernedFiles` (one entry each, deliberately not a
`StartsWith("Api/Compose")` prefix rule, which would absorb the ninth file silently too).

**The other 5 were ONE defect** — and not the one recorded here yesterday. Every failure lasted
**~100s**, which is `HttpClient`'s default timeout, i.e. a hang. Test hosts held the REAL
`DefaultAzureCredential` from `Program.cs`, so the first request to an outbound-authenticating path
probed IMDS and blocked. The credential caches its answer, so only the FIRST caller paid — hence
rotating victims, uniform ~100s, and a test that passes in the suite but fails alone.

The recorded `SessionOwnershipFilter` hypothesis was **wrong** and could never have explained
`ScopePersonas`, a route with no session. Fixed at the fixture (§F.2), all 52 factories, guarded by
`TestHostCredentialGuardTests`. Full write-up: [`notes/test-host-credential-hang.md`](notes/test-host-credential-hang.md).

## A2. CLOSED — `Spe.Integration.Tests`: 23 → 0

That project **had not compiled since the #863 sweep**, so it had not run either; the 23 surfaced
when the compile break was fixed (shared helpers are now LINKED into the three projects that needed
them, not copied). Three causes, all fixture defects, none requiring an assertion to be relaxed:

- **18** — the caller had a RANDOM identity. `CreateAuthenticatedClient(…, userId = null)` defaulted
  to `Guid.NewGuid()`, so every request arrived as a different user. These suites had always been
  running "created by one user, read by another"; nothing noticed until #863 checked.
- **4** — `ai-upload` is a fixed **5 req/min partitioned BY USER** and the upload suite issues ~10.
  Those tests had only ever passed *because* the identity was broken — each got its own partition.
  Fixed by a per-test session registry: `CreateTestSession()` mints BOTH a session id and an owner
  per test. Both halves are required — `ChatSessionManager` caches by `tenant + sessionId` and NOT
  by user, so per-test oids alone made it worse (4 → 7, all 404).
- **1** — the credential hang again, this time with the stack trace naming it outright
  (`managed_identity_unreachable_network`, `169.254.169.254:80`).

Full record incl. the rejected option: [`notes/test-host-credential-hang.md`](notes/test-host-credential-hang.md).

**Practice change that outlives the bug**: verify with a SOLUTION-level `dotnet build`. A green
`dotnet test tests/unit/Sprk.Bff.Api.Tests/` said nothing about the other projects, and that is how
a non-compiling project stayed invisible.

---

> Sections A/B/C (the 2026-08-28 fix plan for the ten failures) were DELETED on 2026-08-29:
> A1/A2 above supersede them, and two of their conclusions are now known wrong — the
> `SessionOwnershipFilter` timeout hypothesis (§B) and "pre-existing, unrelated" (§C) were
> both the credential defect. Leaving them would let a future reader act on a refuted
> diagnosis. The reasoning is preserved in `notes/test-host-credential-hang.md`.

## D. Where #863 stands

**Complete**: `OwnerOid` on `ChatSession` + `StoredSession` (mapped both ways) · required positional
`ownerOid` on `CreateSessionAsync` · `AddSessionOwnershipFilter` on all 28 `{sessionId}` routes · 4
body-scoped routes checked in-handler and enumerated in the guard · History list owner-filtered · one
stable `session.not-found-or-not-owned` code + `auth.tid-missing` at 401 · unowned sessions fail
closed (cost accepted + documented).

**Two production defects the suite found (review did not)**: the Compose *document* session was
minted unowned — so the next dispatch 404'd for the user who had just registered the document — and
`POST /api/compose/active-document` had no ownership check although it mutates the named session and
its child inherits that owner.

**Tests**: `SessionOwnershipGuardTests` 5/5 · `SessionOwnershipTests` 8/8, both **proven
non-vacuous** (removing the ownership comparison turns 2 denial tests red; removing one
`.AddSessionOwnershipFilter()` line turns guard Rule 1 red).

Full record: `notes/863-session-ownership.md`. **Nothing on #863 awaits a decision.**

## E. Then: task 070 cluster 7

Extract **cluster 7 (memory capture)** first, NOT cluster 1 — coverage measured 2026-08-28 inverts
the structural order: cluster 1 is cleanest but only **76.6% branch**, while 7/6/5b/8 sit at 87–96%.
Evidence order: **7 → 6 → 5b → 8 → 2b → 2a → 1 → 3 → 4 → 5a**. Build + run the Compose seam/op-log
suites after EACH extraction (POML step 3), not once at the end.

> ⚠️ **Do NOT extract cluster 2 until `unified-access-control-r2` replies on #858.** They own a
> security fix inside `ComposeService.cs`; I proposed holding cluster 2 so their patch lands against
> today's line numbers.

The three standing 070 warnings (POML criteria unreachable · SaveAsync stays whole · 074 closed
do-not-delete) are UNCHANGED and still bind — preserved in full below.

## F. Task status

**44 ✅ · 4 🔲 (070, 071, 072, 090) · 1 ⊘ (043) · 1 ⛔ (074).** 059 closed this session (owner
sign-off; the directed cross-user fix became #863).

## G. Files modified this session

`Api/Filters/SessionOwnershipFilter.cs` **NEW** · `Models/Ai/Chat/ChatSession.cs` ·
`Services/Ai/Sessions/StoredSession.cs` · `Services/Ai/Chat/ChatSessionManager.cs` ·
`Services/Ai/Sessions/{I,}SessionPersistenceService.cs` · `Api/ComposeActiveDocumentEndpoints.cs` ·
`Api/Ai/{ChatEndpoints,AnalysisEndpoints}.cs` · `Api/Agent/AgentEndpoints.cs` ·
`Services/Compose/ComposeService.cs` · `Api/Filters/AiAuthorizationFilter.cs` (corrected the false
"handlers check ownership" comment) · `tests/Spaarke.ArchTests/SessionOwnershipGuardTests.cs` **NEW**
· `tests/integration/auth/Ai/SessionOwnershipTests.cs` **NEW** ·
`tests/integration/Shared/{TestSessionOwner,TestHttpContexts}.cs` **NEW** (wired into the csproj) ·
~60 test files (fixture repairs) · `notes/863-session-ownership.md` **NEW** · `tasks/TASK-INDEX.md`.

---

# Full State (preserved history — earlier checkpoints)

### 📋 Owner decisions taken 2026-08-28 — all recorded, none pending

| Item | Decision |
|---|---|
| **059** (security — tenant self-naming) | ✅ **SIGNED OFF, may merge.** Recorded in `notes/059-tenant-header-decisions.md` §9. |
| **059 cross-user DELETE gap** | ❌ owner **overrode** the "accept residual" recommendation → **fix it**. Filed as **#863** (schema change: `ChatSession` gains `OwnerOid`, persisted across Redis+Cosmos+Dataverse; the hard part is the migration policy for pre-existing unowned sessions, not the field). |
| **059 `RagEndpoints`** | 📄 **document + defer.** Evidence: the API-key principal carries NO tenant claim at all (`ApiKeyAuthenticationHandler.cs:92-96`), so nothing was bypassed. It is a machine credential that legitimately spans tenants — a different, lower class than 059. Correct fix is the key model. |
| **#853** (live-anchorless prompt vs retry) | ✅ **keep the prompt.** Closed on the issue. Tripwire noted: if `'live-anchorless'` fires often, the *anchor supply* has regressed — do not re-tune the copy. |
| **ADR-038 enforcement** | Filed **#864**. The 17 bans are documented and **nothing fails a build**; 24 touched files use the banned `Mock<HttpMessageHandler>` (4 added here, 20 pre-existing). Start with B4 + B13 — both at **zero** today, so a guard arms green. |

### 🍽️ `/test-diet` run early (report: `notes/test-diet-report.md`)

Run at owner request to answer *"do we have too many tests?"* with data. **Re-run at 090** — the skill is
a project-close gate and this project is still active.

**Answer: volume is not the problem, distribution is.** 187 test methods across 26 added files; the
project added **one** file outside a KEEP path and **deleted three**. The real finding is the unenforced
ban (#864) plus the pattern coverage exposed the same day: the `usePendingRedline` anchorless suite had
**29 tests, 23 of them on the same population** — the live path a user actually hits had **zero**. Test
count hides that; branch coverage finds it.

### 🔴 BEFORE EXTRACTING ANYTHING — coordination constraint from `unified-access-control-r2` (#858)

UAC-r2 owns a security fix in **`ComposeService.cs`**: create-on-save writes bytes into a CLIENT-NAMED
SPE container. They explicitly told compose-r8 **not** to implement it, and asked only to be told when
the file is stable enough to edit.

**They do not know 070 is about to restructure it.** I told them (#858 comment 2026-08-28) and proposed:

> **070 extracts every cluster EXCEPT cluster 2 (create-on-save / promotion).** `PromoteIfEphemeralAsync`
> (3169-3670) + record-resolution helpers (4036-4285) stay at their current line numbers until their fix
> lands. Costs us one deferred extraction; unblocks them completely.

⚠️ **Do NOT extract cluster 2 until UAC-r2 replies on #858.** Everything else is clear to proceed.

### ✅ #853 FIXED (`220ddd18e`) — live-anchorless is no longer called a replay

The discriminator was never missing: `MaterializeOrigin` was destructured at `usePendingRedline.ts:907`
and never read — invariant 7 breached in its purest form. New `AnchorlessSource` selected from
`origin` and **carried** (both proposal sites had hardcoded `'legacy-replay'`). Copy extracted to
`redlineFailureCopy.ts`; 19 new tests, non-vacuity proven. **Mechanics unchanged** — the confirmation
guard still applies to live-anchorless. 🔔 Owner question left open on #853: should a live-anchorless
edit *retry* instead of prompting? Not decided unilaterally.

### ✅ Issue #839 CLOSED OUT — PR #847 open, 131/131 ArchTests pass

All 6 ArchTest failures adjudicated. Do not re-open. Highlights worth keeping:

- **FR-27**: 5 of 8 findings were the regex matching a secret's NAME, not its value. Fixed with a
  name-vs-reference discriminator applied *after* the value regex — **never narrow the regex**, this is a
  CATASTROPHIC-severity detector. The real find: `PendingKvSecretWrite(VaultName, SecretName, Value)` — the
  guard reported the harmless `SecretName` and was blind to `Value`, which its own doc calls CLEARTEXT. New
  **secret-carrier rule** catches that pairing.
- **ServiceBusClientGuard**: demanded an architecturally forbidden fix (L2 has zero ProjectReferences and a
  MUST rule against referencing the BFF). Now one canonical construction site **per deployable**.
- **ADR-010**: ceiling 153 → 156. Net looked like +2; the diff was **7 added / 5 removed** — removals hid
  five additions from the ratchet. Evidence posted to #809.

### 📋 Everything else the repaired Tier 2 aggregator exposed is FILED, not carried

| Issue | Finding | Owner |
|---|---|---|
| **#848** | 5 unit-test failures; 4 are real-clock timing tests (`Spaarke.Scheduling.Tests`: 9s local vs 5m14s CI) | unclaimed; pairs with #795 |
| **#849** | 1212 broken markdown links, but **86% of the scanned corpus is historical `projects/**` docs** | unclaimed |
| **#850** | Prettier: **CI says 1907 files, local says 46** — not developer-reproducible. `npx prettier` is the pattern PR #393 already fixed for ESLint | `ci-cd-unit-test-remediation-r1` |
| **#853** | The Compose classifier bug above | **this project** |

Two genuine Prettier fixes landed here (`442fa904d`). 17 of 19 flagged files were **CRLF-only** —
`.gitattributes` doesn't cover `.ts`/`.tsx` and `core.autocrlf=true`, so they're already LF in CI and
`--write` produces a diff git normalizes away. Don't chase them.

### ✅ The authorization emergency is OVER — merged and deployed, owner-confirmed

| PR | Merge | Deployed | What |
|---|---|---|---|
| **#832** | `3e6fbd4d7` | dev, 45.07 MB | 38 broken caller-identity sites + 2 disclosures + `WorkspaceLayoutService` (3 breaks) |
| **#840** | `30e6fd9cf` | dev, 45.08 MB | remaining 41 `NameIdentifier` fallbacks · `CallerIdentityGuardTests` · Tier-2 aggregator repair |

Owner confirmed **"files are now showing"**. `/healthz` 200. **Do not re-investigate the oid/sub defect.**

### Worktrees

| Worktree | Branch | State |
|---|---|---|
| `c:\code_files\spaarke-wt-spaarkeai-compose-r8` | `work/spaarkeai-compose-r8` | PR **#806**. Synced with master, **11,462/0/95**. Clean, 0 unpushed, 0 behind. |
| `c:\tmp\spaarke-auth-oid` | `fix/caller-identity-sweep-clean` → now `fix/archtest-guard-adjudication` | Active work. Clean, 0 unpushed. |

---

## Active work — issue #839 detail

**Fixed and pushed (3 commits):**

1. `ed7fd7629` — **the Cosmos guard now actually RUNS.** It was still dead: the loader was repaired by
   `spe-admin-app-r2`, but nothing built the L2 DLLs it inspects, so it threw `FileNotFoundException`
   every CI run. The csproj claimed "CI's full-sln build satisfies this" — false; Tier 2 builds only the
   ArchTests project. Fixed with a `BuildL2ForCosmosGuard` MSBuild target (no `ProjectReference`, so the
   two original design reasons still hold). Proof it works: its **positive control now passes**.
2. `acd2b873a` — **FR-F1/FR-F2 closed.** `DataverseRegistryConcurrencyStore` was the one real ADR-028 A4
   violation (BFF's own app-reg + client secret). Its own FUTURE MIGRATION note gated the fix on the L2
   UAMI being a Dataverse Application User — **that was already true and the code never followed**
   (verified live: `sprk-controlplane-dev-uami`, app id `965a4a01-…`, enabled, `Spaarke Provisioning
   Registry` role). Migrated to `DefaultAzureCredential`, identical to the sibling
   `DataverseEnvironmentRegistryClient`. The other 3 sites are genuine E-1 (customer registrations,
   per-request) → allowlist + census entries. Bicep + KV reference removed end-to-end.
3. `46fe89d7d` — self-registered in `projects/INDEX.md`. **⚠️ Overlaps PR #845** (provisional row for the
   same project); whichever merges second takes mine, it is a superset.

**Remaining 3 — with the trap in each:**

- **FR-27** — 8 secret-shaped properties. Only 3 look like real secret VALUES
  (`SharedSecretResolution.Secret` ×2, `SolutionVerificationRequest.ClientSecret`); 5 look like the regex
  matching a property NAME (`PerEnvSettingEntry.Key`, `TrapVerificationRequest.KeyVaultName`,
  `PendingKvSecretWrite.SecretName`, …). **Do NOT narrow the regex** — it is a CATASTROPHIC-severity
  detector. Adjudicate per-property with evidence. NOTE: the rule is about **Cosmos-persisted** POCOs, and
  `SolutionVerificationRequest` is a transient request record never written to Cosmos — check persistence
  before classifying.
- **ADR-010** — ceiling 153 → 155. Identify the 2 added 1:1 interfaces; either justify + raise with docs,
  or register concrete.
- **ServiceBusClientGuard** — `ServiceBusModule.cs:144` `return new ServiceBusClient(fqn, credential);`
  Route through `ServiceBusClientFactory.CreateForNamespace`.

---

## ⚠️ Cross-project constraints — READ BEFORE TOUCHING CI OR AUTH

### CI shadow window (`ci-cd-unit-test-remediation-r1` owns `.github/workflows/**`)

**FROZEN**: `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`. My #840 edited two of them
and merged 5 minutes into the window; **disclosed and adjudicated ACCEPTED — no violation** (the freeze was
an unmerged PR at the time). The freeze now carries a **GATE REPAIR carve-out**: if a gate is silently not
enforcing, fix it and disclose. **I committed to disclosing before using it rather than self-authorising.**
Window is being re-baselined after #825; my branch adds no workflow changes.

### `unified-access-control-r2` owns parent→child access (their Amendment 1)

**R8 must NOT implement the parent-fallback, even as an interim.** Their §5 closed our Q6 (term 5 grants
the SAME right — no read/write fork). Vocabulary: **"Parental cascade"** = the Dataverse feature (rejected);
**"parent-fallback"** = their computed term 5. Docs: `notes/coordination-from-unified-access-control-r2-*.md`
+ `notes/response-to-unified-access-control-r2-*.md`.

### Sync #806 with `git merge origin/master`, NEVER rebase

It is a shared branch under review carrying merge commits. Also: a clean `mergeable` status is **not** a
clean build — the last sync returned MERGEABLE and then failed to compile (both sides had added the same
`using`, CS0105 ×4). Deduplicate after every sync.

---

## Session gotchas worth keeping

1. **The shell cwd resets between Bash calls.** Several greps/builds silently ran in the WRONG worktree and
   reported stale results. **`cd` explicitly at the start of every command.**
2. **`io.open(p,'w')` truncates before the write.** A Python edit that hit an encoding error left a
   committed doc at **0 bytes**. Recovered via `git checkout --`. Prefer the Edit tool for structured edits.
3. **Classify by SINK, not by expression shape.** Three sites written `oid`-first with early returns READ
   as correct and were cleared twice; two of them fed authorization and were broken.
4. **A guard not in the Tier 1 filter cannot fail the build.** `CredentialGuardTests` shipped red and CI
   reported green for 6 days. Arm a guard in the same PR that adds it.
5. **Verify a comment before repeating it as fact.** Three comment blocks said the L2 UAMI was not a
   Dataverse Application User. It had been for some time. One query settled it.
6. **Prove non-vacuity.** Every guard/test added this session was verified to FAIL against the pre-fix code
   (re-broken sites, probe files) before being accepted.

---

## Full State — PRIOR checkpoint (2026-08-26, Compose R8 UAT + deploy)

> Superseded as the ACTIVE task by the P1 work above, but still the record for the Compose R8
> project itself, which is not finished. Retained verbatim.

> **Last Updated**: 2026-08-26 (by `context-handoff`) · **Committed through**: `670d31db2` · **pushed, 0 unpushed**
> **Branch**: `work/spaarkeai-compose-r8` · **Recovery**: read "Quick Recovery" first.
> Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **DEPLOYED TO DEV — owner is mid-UAT.** No task in progress. |
| **Status** | 47 of 51 resolved. Tree clean, everything pushed, merged with `origin/master` (12 behind again — other projects merging). BFF **11,931 / 0 / 96** · integration **103 / 6** · ArchTests **9 failed / 101 passed — ALL 9 PRE-EXISTING ON MASTER** (verified against a clean `origin/master` checkout: 9 failed / 95 passed). |
| **Next Action** | **1. DEPLOY (090 step 2)** — `scripts/Deploy-BffApi.ps1` + `scripts/Deploy-CustomPage.ps1` for `sprk_spaarkeai`, **together, one window, same tree** (NFR-05). TFM verified **net10.0**. Owner authorised the deploy after being told the save-contract gate is red. **2. Then 090 docs**: `/test-diet` → write-side fidelity doc → lessons-learned (Half-A/Half-B) → `projects/INDEX.md` + root §17 + `.claude/CHANGELOG.md`. **3. Separate session: fix the client save-contract suite** (see below). |

---

## 🚀 DEPLOYED TO DEV 2026-08-26 — first deploy of Tracks A/B/C

| Component | Result |
|---|---|
| **BFF** `spaarke-bff-dev` | 45.14 MB · SHA-256 hash-verified · `/healthz` 200 · 2/2 CORS origins |
| **SpaarkeAi** `sprk_spaarkeai` | rebuilt + published, 5,725 KB. ⚠️ the previous `dist` was **Aug 21** — deploying it would have shipped a 5-day-old client against the new BFF. **ALWAYS rebuild before deploying.** |
| **Action mirrors** (4 Track C rows) | `target_para_id` **False → True** in `outputSchema` + `systemPrompt`. Verified 3 ways: PATCH result, independent re-read, second dry run = 0 changes (idempotent). |
| **Route-surface proof** | **All 17 authenticated Compose routes → 401, zero 404s** against the DEPLOYED app. Stronger than 073's two local oracles. |

**Track B is still DISARMED** — `SessionFileStore:BlobEndpoint` empty; dev has no storage account, UAMI has no storage role.

---

## 🔴 THE BIG FINDING: Track C was UN-DEPLOYABLE, not un-deployed

UAT hit two symptoms — a refusal banner on *make more concise*, and a *"Where should this suggestion go?"*
confirm dialog on *draft alternative*. **One root cause**: the deployed Action rows were the **2026-07-28**
versions asking the model for `target_text` and never for `target_para_id`. No anchor ⇒ every LIVE edit fell
into task 053's **replay** population ⇒ banner (prose didn't match) or confirm dialog (prose matched).

**The recorded prerequisite was never executable.** `Deploy-AnalysisAction.ps1` cannot deploy
`infra/dataverse/actions/*.action.json` for three independent, individually-fatal reasons:
1. it reads a `{actions:[...]}` wrapper — the mirrors are **bare objects**;
2. it hard-requires `actionTypeName` and skips without it — **0 of 17** mirrors carry it;
3. it writes `sprk_ActionTypeId@odata.bind` — **that column does not exist on the entity**.

**The column is missing BY DESIGN — verified, not assumed.** R7 task 028 / FR-07 removed the ActionTypeId
expand (*"Action is no longer the dispatch axis — orchestrator reads `node.sprk_executortype` directly"*,
`AnalysisActionService.cs:235`/`:343`); live metadata shows **65 attributes with no action-type lookup**;
the only surviving `sprk_ActionTypeId` reference in the whole BFF is a **stale comment**
(`InsightsActionRouter.cs:290` — the 6th stale-comment defect this project has hit). **Re-adding that column
would restore a retired dispatch axis — a regression against FR-07, not a fix.** `seed-data/manifest.yaml`
already recorded the gap: step `actions-r7`, **`deployer: null`**.

**Closed by NEW `scripts/Deploy-ActionMirrors.ps1`** — deploys all 17 mirrors, binds no action type, `-DryRun`
shows a per-field before/after, idempotent, and refuses to invent a row when no `sprk_actioncode` matches.

> **Lesson worth keeping**: the model contract lives in **Dataverse DATA** (`sprk_outputschemajson` +
> `sprk_systemprompt`), read at runtime. Shipping BFF + client code cannot move it. Any task that changes an
> Action's schema/prompt is **not deployed** until the mirror is pushed.

---

## 🔴 OPEN BUG — misleading copy on a live anchorless edit (NEXT THING TO FIX)

Both UAT symptoms rendered *"This suggestion came from an earlier session, before suggestions carried a
paragraph reference"* — to a user who had just selected the text a second earlier. That copy is **literally
true of the payload** (it genuinely had no anchor) and **completely wrong about the user's action**.

Root: everything anchorless is classified `legacy-replay`. The classifier must distinguish
**"no anchor because it predates anchors"** (replay — ask, don't place) from **"no anchor on a LIVE edit"**
(a model-contract failure — different words, and arguably a retry rather than a prompt). Sites:
`ComposeBannerStack.tsx:937-942` (banner) and `ComposeWorkspace.tsx:5340-5341` (dialog); classification in
`usePendingRedline.ts`. Note the fallback bound is structural and CORRECT — an anchored edit cannot reach
that dialog; only the wording and the live-vs-replay split are wrong.

---

### 058 — nested/conditional merge fields now carry (merged 2026-08-26)

Task 049 flattened these for a real structural reason, and that reasoning **survives intact**: a nested
field's recoverable instruction is a *concatenation* of both code phases, so re-emitting it authors a
different field. What 049 established is that a nested field cannot be **reconstructed** — not that it
cannot be **carried**. The third mechanism was never on the table: **carry the span's OOXML and never
parse it.** The tree survives because nothing reads it. Headline test asserts the saved span
**character-for-character** against the source — the one assertion a reconstruction cannot pass.

**It surfaced a second defect, which is the more valuable half**: `ComposeBlockMerge.InheritRunProperties`
donates the base paragraph's *dominant* run properties to every rendered run. In a conditional the
dominant run is the outer `IF` result — **bold** — so all 17 carried runs came back bold, silently bolding
both inner `MERGEFIELD` values. A fidelity loss introduced by the fix for a fidelity loss, and one that
would have shipped looking correct. Rule now stated where it lives: *inheritance repairs a re-authored
run; a carried run has nothing to repair.* Scoped to nested spans only.

Residual list: the nested half leaves §2; only the **unterminated** field (`TOC`/`INDEX`, which spans
paragraph marks) remains. [`notes/058-nested-field-carry.md`](notes/058-nested-field-carry.md).

✅ **Owner-signed 2026-08-26**: *"follow the established pattern."* A user who deletes a conditional chip
is indistinguishable from a client that never sent it, so the construct is **restored** — the same trade
already taken for bookmarks, SDT shells and objects. This is now the **fourth** construct behaving that
way and the pattern is explicitly sanctioned, so a future carry should adopt it without re-asking.

Still true and NOT covered by that sign-off: no browser/UAT run, and the document was never opened in
Word. Fidelity is asserted through the SDK, the schema validator and the relationship gate.

### 🔒 059 — what it actually turned out to be (read before signing off)

Filed as *"remove the spoofable `X-Tenant-Id` fallback from four handlers plus the auth path."*
The mandated enumeration found **21 sites across three mechanisms**, and **the filed one was the least
severe**:

| Mechanism | Sites | Status before 059 |
|---|---|---|
| `X-Tenant-Id` header, last tier of a `??` chain | 16 | **LATENT** — only reachable by a principal with **no `tid` claim at all**, since tier 1 short-circuits. One such principal exists (`RagApiKey`) but never touched this tier. |
| `X-Spaarke-Tenant-Id`, no claim consulted | 1 | Live, admin-gated, **zero senders** anywhere in the repo |
| **`?tenantId=` query string** | **4** | **LIVE for any authenticated user.** Three consult **no claim at all**; the fourth let the query string OUTRANK the claim. |

**Two of those four are Compose's own**: `GET /api/compose/documents/{documentSpeId}` (the document
**open/resume** path) and `GET /api/compose/sessions/{sessionId}/annotations`. Both took the tenant
from the URL, so a caller could open another tenant's Compose session and resume its anchored
annotations, defined terms and action history. Two of them rejected a missing value with *"tenantId
query parameter is required for multi-tenant isolation"* — isolation the caller chose.

All 21 are closed. The guarantee is **structural, not a rule**: `TenantResolution.ResolveTenantId`
takes a `ClaimsPrincipal`, **not** an `HttpContext`, so it cannot reach a header, query string or
body — the same idiom as `ComposeEditAnchorPass` (no document text) and post-064 offsets. A
two-armed tripwire (`Headers[…Tenant…]` | `[FromQuery … tenantId`) matches by **shape, not name**;
its regex is verified in both directions, and its query arm is what found the two Compose sites
*after* the header sweep was believed complete.

**Four test fixtures minted principals with no `tid`** — a shape Entra never issues — and the tests
compensated with the header. That fixture gap was holding the hole open: it made the spoofable
fallback the only tenant path those tests ever exercised. Repaired the fixture, not the symptom
(`bff-extensions.md` §F.2). Two further tests were passing **vacuously** and now assert something
real. Full record: [`notes/059-tenant-header-decisions.md`](notes/059-tenant-header-decisions.md).

### Landed across the last two sessions — all committed AND pushed (PR #806)
`052` demote text-search · `053` bounded confirmable fallback · `053b` null-identifier edits reach the
document · `061` lazy re-index · `062` retention + availability · `063` durable erasure · `064` retire the
orphaned edit-batch surface · `047b` never-silent hole · `052b` stale-detection durability · **`059`
tenant-selection security (awaiting sign-off)** · **`058` nested-field carry** · **`073` endpoint
decomposition** · **`074` CLOSED do-not-delete** · **deploy + `Deploy-ActionMirrors.ps1`**.

### Critical context in one paragraph
**Track C (AI edit placement) and Track B (durable session files) are both COMPLETE.** Text search is no
longer a placement mechanism — and that is now enforced by the TYPE SYSTEM in two places rather than by
rule: `ComposeEditAnchorPass.Validate` takes no document text, and after 064 no type in `Services/Compose/`
can express a character offset at all. The client fallback survives only as a bounded, confirmable proposal
for replayed entries, in a module that **has no `applied` outcome**. Three defects found along the way were
worse than filed: an anchored edit replaced the ENTIRE paragraph; a stale target was not detected at all
(silent overwrite of the user's newer text); and 047b was not merely under-reporting — it was cloning an
UNTOUCHED block from the wrong base, an outright breach of ADR-049 invariant 2, in a real signed NDA.

---

## ⚠️ Publish-size: the ~1.3 MB divergence is the SHELL — settled 2026-08-25

**Current: 45.03 MB compressed incl. PDBs under `pwsh` 7** (215 files, 4 `.pdb`, **raw dir sum 137.41 MB**)
— **+0.07 MB** vs the 44.96 MB net10 baseline; ceiling 60 MB.

This project has carried two conflicting clusters (43.68–43.74 vs 45.00–45.04) for months. Zipping the
*same directory twice in the same minute* settled it:

| Shell | `Compress-Archive -CompressionLevel Optimal` |
|---|---|
| Windows PowerShell **5.1** (what `powershell` resolves to from Git Bash) | **43.73 MB** |
| **pwsh 7.6.3** (what the `PowerShell` tool and CI use) | **45.03 MB** |

Neither is an artifact — different `System.IO.Compression` implementations. **Canonical: `pwsh` 7**, because
CI uses it and it reconciles with the 44.96 MB baseline at +0.07 MB; PS 5.1 would imply a −1.23 MB drop no
code-only change could produce, which is itself the evidence the baseline was taken under pwsh 7.

**Method — pin the shell:**
```
rm -rf <out>
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>
pwsh -Command "Compress-Archive -Path '<out>\*' -DestinationPath '<out>.zip' -CompressionLevel Optimal -Force"
```
**Always report the raw dir sum (~137 MB) + file count (215 / 4 `.pdb`) next to the zip.** Those are
shell-independent, so a mismatch there is a real content change while a zip-only mismatch is tooling. That
invariant is exactly what made this diagnosable.

## Owner decisions still in force (do not re-ask)

| Q | Decision |
|---|---|
| **Q1** Which bicep stack? | The question was wrong — dev is not stack-deployed. See "Track B is blocked". |
| **Q2** Sign off the residual list? | **YES — signed 2026-08-25.** Task 045 CLOSED. (Note: the field and object rows were *declined and fixed*, not accepted.) |
| **Q3** Conditional merge fields? | **Fix it** → task **058**. |
| **Q4** `X-Tenant-Id` fallback? | **Separate task, fix in R8** → task **059**. |
| **Q5** Silent-loss hole? | **Fix in R8** → task **047b**. |
| **052** `match_mode: 'all'` | **Retired in full.** Asymmetric failure modes; document-wide sweeps route to user-invoked find/replace. Reasoning: `notes/052-…-decisions.md` §2. |

### 🚨 ARMING WARNING — the code gate is closed, but do NOT set `BlobEndpoint` yet

Tasks 060–063 are done: durable store, lazy re-index, retention, erasure. ADR-015's precondition
(retention AND erasure before a persisted store is armed) is **satisfied in code**.

**Two pre-existing AUTHORIZATION defects sat on the same DELETE route.** One is now CLOSED; one remains.

1. ~~**The spoofable `X-Tenant-Id` fallback**~~ — **CLOSED by task 059** (2026-08-26), along with 20
   sibling sites it turned out to have. ⚠️ **Correction to what this warning previously said**: the
   header was described here as live on that route. It was **not**, for any caller holding a normal
   token — it sat at the END of a `??` chain, so it was only ever reached by a principal carrying **no
   `tid` claim at all**. The defect was **latent** (one route-registration away from live), not live.
   I wrote the earlier claim; it was wrong, and a test I wrote to prove it passed **vacuously** before
   the fix, which is how it was caught. See `notes/059-tenant-header-decisions.md` §3.
2. **No owner check — STILL OPEN.** `ChatSessionManager.DeleteSessionAsync(tenantId, sessionId, …)` is
   keyed on tenant + session only, and `ChatSession` has **no owner field at all** — so a check is not
   implementable without a persisted-schema change (Redis + Cosmos + Dataverse) and a policy for
   pre-existing sessions. 059 narrows it from **cross-tenant** to **within-tenant**; session ids are
   `Guid.NewGuid().ToString("N")`, so exploitation needs a leaked id, not a guess. **Owner decision
   pending** — `notes/059-tenant-header-decisions.md` §6a and §8.

What arming changes is **blast radius**: today these delete a 24-hour AI-Search index entry; armed, they
delete **90-day durable bytes**, and 063 confirms Azure soft-delete and versioning are OFF, so a
completed delete is final. A store that is armed and later disarmed also cannot be erased from.

**Arming is now gated on: (a) human sign-off of 059, and (b) the cross-user decision.** Not on further
code.

### The four operator steps (all still required, and still not done)
Provision/pick a storage account → create the container → grant **`mi-bff-api-dev`**
(the UAMI — **not** the system-assigned identity `model2-full.bicep` currently targets, which does not
exist on `spaarke-bff-dev`) *Storage Blob Data Contributor* → set `SessionFileStore:BlobEndpoint`.
063 also notes the role assignment is missing from `customer.bicep` and `model1-shared.bicep`.
Dev has **no storage account**, and the UAMI holds **no storage role of any kind**.

---

## Remaining queue (6 open, 1 blocked)

| # | Task | Gate |
|---|---|---|
| **058** | Nested / conditional merge fields | 049 ✅ 057 ✅ |
| **059** | SECURITY — `X-Tenant-Id` spoofable fallback + the cross-user DELETE gap; human sign-off required | 060 ✅ — **dispatch next; gates arming** |
| **070–073** | Track D decomposition | ready; same files as Track A/C — sequence carefully |
| **074** ⛔ | Retire `ComposeShadowPatchEngine` | gate-confirm before deleting 3,000 lines |
| **090** | Wrap-up (incl. `/test-diet`) | all |

---

## 🔔 The ONE decision waiting

**`ComposeEditAnchorPass` + `ComposeAnchorResolver` now have ZERO production callers.** Verified
independently after 064: only comment references remain in `src/`; all 15 `Validate` call sites are in
tests. `POST /api/compose/edit-batch/validate` was their only caller and 064 deleted it.

They are the same orphan category 064 just retired — but task **052 kept the anchor pass deliberately**, and
the ADR-043/041 assessment (§7, C-7) names it the designated home for closed-set validation. So retiring it
is an owner decision, not a cleanup. Three options, in `notes/064-orphan-retirement-decisions.md` §4:

- **(a) Keep** as the designated home — accept it is currently dark.
- **(b) Wire it** — the obvious candidate is server-side validation of whole-document `target_para_id`s
  (today the closed-set check is client-side only).
- **(c) Retire it too** and amend the assessment.

> Owner decisions A and B (2026-08-25) are DONE — A → task 053b, B → task 064. Do not re-ask them.
> One sub-decision inside 064 has a revert point: three always-default fossils (`MatchCount`,
> `EditErrorKind.Overlap`, `BatchValidationResult.BatchErrors`) were removed beyond the task's list.
> Rationale + blast radius: `notes/064-orphan-retirement-decisions.md` §3.4.

### Superseded — decision #1 is CLOSED
### 🔔 Decision waiting #1 — a false `applied` that contradicts what we tell the model (surfaced by 053 §5)

A **post-052** payload can carry `target_para_id: null` — Structured Outputs requires the key to be present,
so "no identifier" arrives as an explicit null, not an absent field. Such an edit has no anchor **and no
prose**, so 053's fallback cannot serve it; it falls through to the insertion-at-cursor branch and reports
**`applied`**. Meanwhile the catalog prompt tells the model, verbatim:

> *"Set target_para_id to null ONLY when you genuinely cannot identify the paragraph. An EDIT with a null
> identifier is **REFUSED rather than placed** — there is no prose fallback — so a missing identifier costs
> you the edit."*

So the system currently lies to the model and gives the user a stray insertion reported as success. It is
**not** a UAT-21 mis-placement (nothing is struck; it is a pending insertion at the user's own caret), which
is why 053 surfaced it instead of changing it — the same branch also serves `compose-draft-document` and
`compose_context_insert`, which are *legitimately* anchorless.

**The discriminator that separates them cleanly**: `hasOwnProperty(payload, 'target_para_id')` — key present
and null ⇒ an edit that failed to identify its target ⇒ **refuse**; key absent ⇒ a genuine insertion ⇒ insert
as today. **Fix it, or change the catalog promise to match the code?** Recommend fixing the code: the promise
is the correct behavior and R8's charter is no false `applied`.

### Superseded — decision #2 is CLOSED (task 064 executed it)
`ComposeEditBatch` + `ComposeEditTransaction` are now orphaned — the text-offset APPLY half of the
mechanism 052 retired, with no producer and no production consumer, so they can never apply anything. They
do **not** violate I-7 (they apply spans, they do not search), so 052 left them rather than delete ~500
lines outside its list. **Retire them (with `/edit-batch/validate` and the models serving only them)
alongside task 074?** Evidence: `notes/052-…-decisions.md` §1.4.

---

## How to run the next wave (this keeps working — reuse it)

**Parallelism.** The blanket `parallel-safe: false` on the Compose spine is too coarse. Judge **file AND
toolchain disjointness per pair**. Task 052 split cleanly into `src/server/**`+`tests/**/*.cs` (dotnet) ∥
`src/client/**`+`infra/dataverse/**` (jest) — but give each agent an explicit "you MUST NOT touch X"
boundary naming the *other* agent's paths, or they collide. **052 ∥ 047b/058 would collide** (all
`Services/Compose`).

⚠️ **Do NOT trust the POML `parallel-safe` flag — read the file sets.** 061/062/063 are all marked
`parallel-safe: ✅`, but **all three declare `Services/Ai/Sessions/` as `primary-edit`**, and 062
additionally touches the Compose client. They are safe *relative to other tracks*, not *to each other*.
Running them concurrently would collide on `SessionFileBlobStore` / `SessionFilesCleanupJob` /
`SessionRestoreService`. **Sequence them: 061 → 062 → 063.** The genuinely disjoint pair is
**053 (Compose client / jest) ∥ 061 (Ai Sessions server / dotnet)**, which is what was dispatched.

**Main session reserves** `TASK-INDEX.md`, `current-task.md`, `.claude/**` and ALL git operations. Tell
agents explicitly they cannot write `.claude/` (root §3) and should report proposed CHANGELOG text instead.

**Never build/test while an agent is mid-edit in the same tree** — you will read half-written work as a
regression. Note the cross-toolchain case: a C# test that reads `infra/dataverse/**` JSON at runtime is
affected by the *client* agent's edits.

**Run `dotnet format` before committing.** Task 052's files had whitespace/EOL violations; CI auto-formats
and pushes, which rejects your next push. Use `dotnet format whitespace --include <your paths>` — a
project-wide `dotnet format` also "fixes" ~22 pre-existing IDE1006 naming violations in unrelated files and
produces a huge diff.

**Beware `grep -i compose` in this worktree** — the path is `spaarke-wt-spaarkeai-compose-r8`, so it
matches EVERY line. Scope to `Services\\Compose\\` or a filename.

**Verify every agent report.** What that caught this time:
- a **wrong publish number already committed to a project note** (see the box above);
- a **stale test fixture neither agent owned** — `golden-utterances.json` still documented `match_mode` as
  a live payload field and carried a whole case for the retired `all` sweep. One agent fixed only the `.cs`
  half; the other flagged the file as out-of-boundary, and its flag was itself stale. **When two agents
  share a contract, check the seam neither one owns.**

---

## Standing constraints (unchanged)

### ✅ DEPLOYED TO DEV — 2026-08-26 (BFF + `sprk_spaarkeai` together, NFR-05 satisfied)

First deploy of Tracks A / B / C. Commit `cfc118fe4` (merged with `origin/master`, 0 behind).

| | |
|---|---|
| **BFF** | `spaarke-bff-dev` · package **45.14 MB** · SHA-256 hash-verified on 4 critical files · `/healthz` 200 · 2/2 CORS origins present |
| **SpaarkeAi** | web resource `sprk_spaarkeai` (`5206a442-…`) updated + customizations published · bundle **5,725 KB**, rebuilt today (the previous `dist` was **Aug 21** — five days stale) |
| **Route-surface proof** | **All 17 authenticated Compose routes return 401, zero 404s** — task 073's decomposition verified against the DEPLOYED app, which is stronger than the two local oracles. |

⚠️ **Still NOT observable: Track C.** `Deploy-AnalysisAction.ps1` has **not** been run. Task 052 changed
the four compose Action output schemas, so until those `sprk_analysisaction` rows are upserted, dev still
asks the model for `target_text` and the anchored-placement work cannot be exercised. **This is the next
deploy step, and it was not part of the requested deploy.**

⚠️ **Track B remains DISARMED** — `SessionFileStore:BlobEndpoint` empty; dev has no storage account and the
UAMI holds no storage role. Unchanged by this deploy.

- **Deploy prerequisite (CORRECTED 2026-08-26 — the old instruction was NOT EXECUTABLE)**: Track C needs
  the Action mirrors in `infra/dataverse/actions/` deployed to `sprk_analysisaction`, via the NEW
  `scripts/Deploy-ActionMirrors.ps1`. The previously recorded instruction — *run `Deploy-AnalysisAction.ps1`* —
  **could never have worked**: that script reads a `{actions:[...]}` wrapper (mirrors are bare objects),
  hard-requires `actionTypeName` (all 17 mirrors omit it), and writes `sprk_ActionTypeId@odata.bind` — a
  lookup that **does not exist** on the entity. The ActionType axis was retired ON PURPOSE by R7 task 028 /
  FR-07; `seed-data/manifest.yaml` already recorded `deployer: null` for this source. **DONE 2026-08-26** —
  the four Track C actions now carry `target_para_id` in both schema and prompt.
  **052 raises the stakes** — it changed the four compose Action output schemas, so until that script runs,
  dev still asks the model for `target_text`. Deploy BFF + `sprk_spaarkeai` together (NFR-05).
  **Nothing from Phase 3 onward is deployed.**
- Publish ceiling 60 MB **compressed**; current **43.73 MB**. No new NuGet on Track A.
- **NEVER delete `docxBridge.ts`.** Confirmed unmodified through 052.
- Pre-existing CI red, NOT ours: **Compose Client Gate** (timeout flake since `7069717bd`) and **Trivy**
  (HIGH CVE on master). PR **#806** open.
- **C-4 still unmeasured against a real model response.** Anchors add 3.50% at realistic payload size.
- **Nothing in Track B has run against real Azure** — no storage account, no MI, no RBAC.
- **No bicep file has been changed by this project at any point.**

---

## Hard-won gotchas (this session) — do not rediscover these

- **Publish size: PIN THE SHELL.** `Compress-Archive` gives **43.73 MB under Windows PowerShell 5.1** and
  **45.03 MB under pwsh 7** for the SAME directory. Canonical is **pwsh 7** (CI uses it; reconciles with the
  44.96 MB baseline at +0.07 MB). Always report the **raw dir sum (~137.41 MB) + file count (215 / 4 `.pdb`)**
  alongside the zip — those are shell-independent and are the only reason this was diagnosable.
- **Line endings**: `.gitattributes` sets `*.cs text eol=crlf`, and edits can silently produce pure LF.
  **`grep -c $'\r$'` reports those files as CRLF and is WRONG.** The reliable check needs the `tr`:
  `od -An -tx1 <file> | tr ' ' '\n' | grep -c '^0d$'` — non-zero means CRLF.
  ⚠️ **Without `| tr ' ' '\n'` it returns 0 for CORRECT files too** (od prints 16 bytes per line, so no line
  ever equals `0d`) — i.e. it silently reports every file as broken. Task 047b caught exactly that error in a
  brief written from this note; the note is now correct.
- **`dotnet format` before committing**, scoped: `dotnet format whitespace <csproj> --no-restore --include
  <your paths>`. CI auto-formats and pushes, which rejects the next push. A project-wide run also "fixes"
  ~22 pre-existing IDE1006 violations in unrelated files.
- **`grep -i compose` matches EVERY line** — the worktree path is `spaarke-wt-spaarkeai-compose-r8`. Scope to
  `Services\Compose\` or a filename.
- **Don't trust the POML `parallel-safe` flag — read the file sets.** 061/062/063 are all marked ✅ but all
  three declare `Services/Ai/Sessions/` as `primary-edit`.
- **Give each agent an explicit "you MUST NOT touch X" naming the OTHER agent's paths.** Both parallel waves
  this session stayed clean because of that; the one cross-agent seam that broke was a file *neither* owned.
- **When two agents share a contract, check the seam neither one owns.** Task 052: one agent fixed the `.cs`
  eval test, the other flagged the file as out-of-boundary (and its flag was itself stale) — the JSON fixture
  went stale and only main-session verification caught it.
- **Don't `dotnet build` while a `dotnet test` run is live** — the test host holds the output assembly and
  the build reports a phantom error. Same family as the mid-edit hazard. Re-run after it finishes.
- **The mid-run hazard includes FIXTURES, not just code.** Re-running a corpus generator
  (`tests/fixtures/compose-corpus/generators/*.py`) rewrites its `.docx` **in place**. Doing that during a
  live suite produced **2 corpus-theory failures at `< 1 ms`** that looked like real 058 regressions and were
  purely self-inflicted; a clean re-run gave **11,391 / 0**, exactly the predicted count. The `< 1 ms`
  duration is the tell — that is a file-read failure, not a logic failure.
- **A regenerated corpus `.docx` is NOT a no-op diff.** `zipfile.ZipFile(path, 'w')` stamps the current
  mtime into every entry, so the bytes differ on every run while the content is identical. `git status`
  cannot tell that apart from a real content change — unzip and `diff -r` before committing one.
- **Run the two client suites SEQUENTIALLY, not concurrently.** 052b saw 2 and 12 spurious failures
  running `Spaarke.Compose.Components` and `SpaarkeAi` at the same time; both green run one after the other.
- **Verify every agent report.** Caught so far: a wrong publish number already committed to a note, a stale
  test fixture, a misleading `parallel-safe` flag, two of an agent's own tests passing vacuously, and a
  "regenerates byte-identically" claim that was false (ZIP mtimes).
- **An agent whose worktree you remove will report DATA LOSS.** After collecting + committing 073 the
  worktree was removed; the agent then re-notified with an urgent "the deliverable is no longer on disk".
  Nothing was lost — verify with `git ls-tree -r --name-only HEAD | grep <artifact>` and move on. **Collect,
  commit, THEN remove** — and expect the alarm.
- **`gh`/OData/metadata queries that ERROR can print a false negative.** A `contains()` filter is unsupported
  on Metadata Entities; the failed call left `$m` null and the script printed "NONE — no attribute exists",
  which would have become evidence for re-adding a column. **A failed query is not a negative result.**
- **Model-contract changes live in Dataverse DATA, not code.** `sprk_outputschemajson` + `sprk_systemprompt`
  on `sprk_analysisaction` are read at RUNTIME. Deploying BFF + client cannot move them. Use
  `scripts/Deploy-ActionMirrors.ps1`.

---

## 🚨 047b found more than a reporting bug — read this before touching the merge

Task 047b was filed as "an edited block with no base counterpart reports no loss". It was **not only** that.
On `interior-text-boxes.docx`, blocks 1 and 2 project to **byte-identical** models (the text box's prose is
accept-flattened; the shape is not carried), so `ComposeBlockMerge.Plan`'s LCS was **ambiguous** — and the
traceback's tie-break skipped the *posted* block, producing:

```
posted 1 -> Render base=-1   <- the EDITED block, no counterpart -> nothing reported
posted 2 -> Clone  base=1    <- the UNTOUCHED twin, cloned from the WRONG base
              base 2 stranded, never written
```

The saved package held block 1's `v:shape` at position 2 and block 2's not at all. **ADR-049 invariant 2
("untouched blocks are preserved") was being breached by a clone.** The remark on `Plan` asserted this could
not happen — equality there is over the *projected model*, not the OOXML — and that comment is why nobody
looked. Fourth stale-comment defect this project has hit.

Corpus sweep, 24 docs × every block position = **294 single-block edits: unpaired blocks 5 → 0.** Four of the
five were in a **real signed NDA** (`AppligentNDA_Signed.docx`), on consecutive empty paragraphs.

**Why the fidelity gate never caught it**: the gate edits block 0 of that document. Every other parity row
sits in a document whose blocks all read differently. 047b added a `pictTextBoxTwin` parity row so the
published list is now measured **at a duplicate-key block position** — that is the gap that let this survive
four runs of a check built to catch it.

`COMPOSE-WRITE-RESIDUAL-LOSS.md` changed but **no row changed** — the signed five losses are identical. What
changed is that §2's promise ("reported by name … none is silent") is now *true* where it wasn't.

### Recorded by 047b, not fixed (deliberate)
- `BaselineUnavailable` / `BaselineUnaligned` fall back to R6's whole-document rebuild with no base side — a
  different failure CLASS (document-level, not per-edited-block), whose honest signal needs a new degradation
  code + client copy + banner state, which this project's CLAUDE.md forbids adding here. Reachability
  measured: **0 of 24** corpus documents. Both already on `ComposeMergeStats`; only a consumer is missing.
- LCS cannot see a MOVED block (matches never cross) — 0 of 294 after the fix.

## Doc drift to fix (not urgent, main-session only — hot path)
Root `CLAUDE.md`'s ADR-049 pointer says the save "pairs blocks by **document order**". It has paired by
**LCS** since task 040 — loosely true (matches are monotone) but imprecise, and 047b showed the imprecision
is where a real defect hid. Touching root CLAUDE.md needs `/conflict-check` + a `.claude/CHANGELOG.md` entry.

---

## 090 — WHY THE PROJECT IS NOT CLOSED (escalation fired, owner informed)

The POML's trigger: *"If any Track's gate did not pass, do NOT close the project."* Two are outstanding:

| Blocker | State |
|---|---|
| **Task 070** | 🔲 — clusters 5a + 2a/2b **frozen** in `ComposeService.cs` until `unified-access-control-r2` lands #858 and comments. A declared dep of 090. |
| **Compose Client Gate (Save-Contract Suite)** | ❌ red on master — 19 failing tests |

**The Compose FIDELITY Gate passes** — document fidelity is verified. The red one is the client
save-contract suite. Do not conflate them.

So: do the deploy + the 090 documentation, but **do NOT mark the project complete** until 070 unfreezes
and the save-contract gate is addressed.

## The save-contract gate — for the fresh session that will fix it

Full write-up: `notes/compose-client-gate-red-open.md`. **Read it before touching anything** — it records
three disproved hypotheses so they are not re-run.

**The single most important fact**: 19 failures × 15s ≈ 285s of a 288s run, so the **5 passing tests mount
the editor instantly while 19 never mount at all**. Slowness would make all 24 slow. The split is binary
⇒ **conditional state, not resources**. Three timing-shaped fixes (#908 `testTimeout`, #916
`asyncUtilTimeout`, #917 `maxWorkers`) all failed; #916 and #917 are reverted.

**Do NOT re-run**: RTL-vs-jest timeout distinction · `--coverage` overhead (the 10s-vs-102s figure was
warm-vs-cold jest cache; cold it is 20s vs 26s) · CPU contention (serial run is identical).

**Start here instead**: make CI print WHICH 5 tests pass. First N in file order ⇒ state pollution after
test N; scattered ⇒ per-test input.

**And the meta-rule**: this box has 32 cores and the suite passes on it unconditionally. **A local pass is
not evidence for this gate.** Validate on the real runner or not at all — that blind spot is what made all
three previous fixes look right.

**Do not "solve" it by deleting the tests.** 21 of the 24 assertions read editor-rendered DOM
(`data-compose-mark`, `span[data-comment-id]`); the editor mount IS the system under test, and the names
carry defect ids (DEF-09/11/12, FR-16 tasks 030/032, r8 task 055) — ADR-038 KEEP category.
