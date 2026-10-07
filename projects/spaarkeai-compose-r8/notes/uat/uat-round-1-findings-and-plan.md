# UAT round 1 — findings + plan

> 2026-09-02 · owner UAT against the `spaarkedev1` deploy (BFF `dad37c1ee` lineage, `sprk_spaarkeai` 5,752 KB)

## What the round confirmed (worth stating — it is the release's core)

- **PDF intake works end to end.** A PDF opened, was editable, saved, and produced honest per-code
  degradation warnings (`pdf-intake-*`, ×2 and ×6). That is the fail-honest path behaving exactly as
  designed — the complaint is its *presentation*, not its truthfulness.
- **The AI redline stream works.** Screenshot 5 shows tracked insert/delete, "2 suggested edits pending",
  Accept-all, and per-suggestion "What I changed" rationale.

## ⚠️ What this round did NOT cover — and R8 cannot close without it

The plan's **sections A and B** were not exercised:

- **A** — open a real `.docx`, edit one paragraph, save, reopen, confirm *untouched content is byte-identical*.
  This is the entire subject of R8 Track A. No result was reported.
- **B** — the **`section-break-flattened` accept/decline decision**. The residual-loss list has a sixth §2
  row awaiting owner sign-off. Until it is answered, the published list is accurate but the *acceptability*
  of that loss is unresolved.

Everything below is additive to those two, not a substitute for them.

---

## The eight items, classified

| # | Item | Class | Root cause established? |
|---|---|---|---|
| 1 | PDF loaded; editable | ✅ PASS | — |
| 2 | Formatting warning too intrusive → notification popover | UX | Yes — presentation only |
| 3 | Remove numbering does not renumber the rest | **Architecture gap, previously deferred** | **Yes — see below** |
| 4 | Paragraph → numbering does not add numbering | **Same root cause as 3** | **Yes — see below** |
| 5 | Toolbar layout redesign (9 sub-items) | UX | Yes — restructure |
| 6 | New "Spacing" formatting group | Feature | Yes — new |
| 7 | Assistant should scroll latest turn to TOP | UX | Yes — scroll anchor |
| 8 | Do we have a document change summary? | **Question — answered below** | Yes |

---

## 🔑 Items 3 + 4 are ONE finding, and it is a known deferral — not an R8 regression

`composeNumberAtomExtension.ts`'s own header states the constraint that produces both symptoms:

> The editor **NEVER** relies on the browser `<ol>` CSS auto-count for a legal number … `list-style: none`
> **unconditionally**, and THIS module's decoration is the sole source of the displayed number.
>
> If a future change needs the number to participate in editing (**live renumber-on-insert/delete**,
> reflected in redline) — that is **R5 G3, explicitly OUT of R4.5 scope**; escalate rather than converting
> this to a doc node.

So:

- Legal numbers are **computed server-side at load** (`NumberingComputationEngine` → `data-computed-number`)
  and painted as a **ProseMirror view decoration** — deliberately *not* a document node, so it cannot be
  selected, typed into, or shift text offsets (the offset table the redline/reanchor system indexes).
- **Item 3** follows directly: the decoration is a snapshot of load-time state. Removing numbering from one
  paragraph cannot renumber the others, because nothing recomputes.
- **Item 4** follows too: the native `<ol>` marker is suppressed *unconditionally*, and a newly-created list
  has no server-computed number to paint — so the button appears to do nothing.

**This is the deferred "R5 G3" capability, surfacing in UAT for the first time.** It is a genuine gap the
owner has now hit, but it is not something R8 broke, and it is not a small fix: making numbering participate
in editing means either a **client-side renumbering engine** that mirrors Word's rules (the two-engine drift
this project exists to prevent) or a **server round-trip on structural change** (a new endpoint + latency on
every list operation). **Both options need a design decision before any code.**

### Sub-observation to reproduce before acting

Screenshot 2/3/5 suggest that removing numbering from **"1.2 Technical Field of the Invention"** also cost it
its **heading style** — it appears afterwards as indented body text, and the tracked-change pane shows it
absorbed next to the preceding paragraph's text. If reproducible, that is a **separate and more serious**
defect than the numbering gap (a style/structure loss on an edit), and it would belong to R8's own fidelity
scope rather than to R5 G3. **Reproduce first; do not assume.**

---

## Item 8 — answered: the capability exists, the surface does not

`compose-summarize-word-changes` is a **live consumer type with a Dataverse binding row** and a client
action. It was **deliberately removed from the selection toolbar** (FIX #5, an earlier UAT):

> It is a RETURN-FROM-WORD action requiring real tracked-change data (`changesText`); on the selection
> toolbar it has no change data, so the LLM **fabricates a phantom "[Insertion]"**.

> ❌ **BOTH claims below were WRONG. Corrected 2026-09-02 — see `current-task.md` §U8.**
>
> **"Its only wired trigger today is the return-from-Word reanchor flow"** — there is **no** trigger. A
> repo-wide search returns nine references and not one dispatches it: two comments (one of them the note
> explaining the action's REMOVAL), the result renderer, the consumer-type constant, and the dispatch
> orchestrator's discriminator. `AnnotationReanchorService` explicitly says the opposite of what I wrote:
> *"the human-friendly change summary is a SEPARATE gated capability … that DOES call the model; this
> engine does not."* The reanchor flow deliberately does **not** trigger it.
>
> **"A wiring job, not a new capability"** — the server half is complete (Action, both schemas,
> `ContextBinder`'s `changesText` operand, the renderer) but the client half is **entirely absent**: there
> is no producer of `changesText` and no trigger. Item 8 needs both built.

The NDA/agreement analogue the owner remembers is **`AgreementReviewSummaryPanel`** — a real, reusable
panel, and still the right render target.

The prior removal is the binding constraint and survives the correction intact: **never trigger it without
real change data**, or it invents changes. That makes the PRODUCER the load-bearing piece, not the button —
any trigger must refuse when there are no tracked changes rather than dispatch an empty operand.

---

## Recommended plan — and the scope question it forces

Items 2, 5, 6, 7 are a **Compose UI/UX release**. Item 3/4 is a **deferred architecture capability**. Neither
is what R8 set out to do ("make Compose save reliably and stop destroying Word formatting"). R8 currently
stands at: every code + issue item closed, deployed, awaiting the two untested UAT sections.

**Proposed split:**

### Stays in R8 (finish what it is)
- **U-0** Reproduce the heading-style-loss sub-observation. If real → R8 fidelity defect, fix here.
- **U-1** Sections A + B of the UAT plan, including the `section-break-flattened` decision.
- Then merge PR #924 and close.

### New project — `spaarkeai-compose-ux-r9` (or similar)
| Item | Work | Size |
|---|---|---|
| 5 | Toolbar restructure — 9 sub-items across `ComposeFormatToolbar.tsx` (1,227 ln), `ComposeToolbar.tsx` (188 ln), `ComposeWorkspace.tsx` wiring | M |
| 2 | Notification popover — depends on 5 (it is a toolbar affordance); replaces the full-width banner | S, after 5 |
| 6 | Spacing group — 1.0/1.15/1.5/2.0/2.5/3.0 + Line Spacing Options + Add Space Before/After | S–M |
| 7 | Assistant scroll-latest-to-top (Copilot-style) | S |
| 8 | Wire `compose-summarize-word-changes` to a document-level surface, reusing `AgreementReviewSummaryPanel` | M |

### Separate design decision — numbering participates in editing (items 3 + 4)
Needs a written design before code: client renumbering engine vs. server round-trip, and how either interacts
with the redline offset table and the "one reader / deterministic numbering" invariants. This is the R5 G3
escalation the code comment asks for. **Do not let it be absorbed silently into a UX task** — that is how the
two-engine drift this project exists to prevent gets reintroduced.

**Owner decision needed**: split as above, or expand R8 to cover all of it. My recommendation is the split —
R8's thesis is save reliability + fidelity, it is deployed and green, and holding it open for a toolbar
redesign delays the value it already has. But the "no deferrals" directive is the owner's to apply here.

---

## Item 5 — the restructure, itemised (for whoever picks it up)

| Sub-item | Change |
|---|---|
| a | Left-align formatting dropdown groups: Body · Paragraph · Font · Table |
| b | Right-align tool icons: Word · Save · Reload — separator — Comments · Track-changes toggle |
| c | Move undo/redo to the **far left**, left of Body |
| d | Save icon gains a **warning triangle** when unsaved; **delete** the "Unsaved · Auto Save On" text |
| e | Word menu gains a **dropdown arrow** on the right (match the Save group) |
| f | Add "Open document" into the Word group, labelled **"Open in preview"** |
| g | Relabel Word menu items → **"Open in web"**, **"Open in desktop"** (+ "Open in preview") |
| h | Move "Refresh document profile" into the **Save** group, relabel **"Refresh profile"** |
| i | Move "Apply template" into the **Word** group |
