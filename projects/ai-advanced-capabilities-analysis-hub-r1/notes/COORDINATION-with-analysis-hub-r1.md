# Coordination — `ai-advanced-capabilities-agreements-r1` ↔ `ai-advanced-capabilities-analysis-hub-r1`

> **From**: `ai-advanced-capabilities-agreements-r1` (the Agreement Analysis review *machine*)
> **To**: `ai-advanced-capabilities-analysis-hub-r1` (the Analysis *platform*: hub widget + wizard + `sprk_analysis` spine + sessions)
> **Date**: 2026-07-29 · **Owner**: ralph.schroeder
> **Status**: 🟢 Shareable coordination brief — please review the 🔴 time-sensitive items before the hub deploys.
> **Why now**: the hub is reported near-deploy. Two of the asks below (wizard type-field + a sub-domain column) are
> **far cheaper to land inside the hub's in-flight schema/wizard work than as a separate agreements-r1 migration later.**

---

## 1. The project split (agreed, option A — 2026-07-29)

Both projects generalize NDA → Agreement Analysis, at **different layers**. Neither is a from-scratch surface.

| Layer | **analysis-hub-r1** (you) | **agreements-r1** (us) |
|---|---|---|
| Entry / platform | "Create New Analysis" hub widget · **Create Agreement Analysis wizard** · `sprk_analysis` spine (`sprk_worktype`, regarding field-set, subgrids) · session persistence + session↔Analysis binding · entry matrix · retiring the old `AnalysisWorkspace` code page | — |
| Review machine | — | Document-driven **classifier + orientation** · type→knowledge **routing + confirmation** · review-depth UX (multi-select batch, bidirectional highlight, cleaner confirmations) · **Review Summary Memo** + Word-comment export fidelity · nda→agreements generalization |

**One-line contract**: your wizard/launcher sets the work type + (new) agreement sub-domain and opens the three-pane;
**our machine** consumes that to bind the right knowledge and run the review. On the ad-hoc **Assistant chat-upload
path** (no wizard) our classifier infers the same signal itself.

---

## 2. Asks of analysis-hub-r1

### 🔴 Time-sensitive (touch hub deliverables; ideally land before/with the hub deploy)

**A1 — Add an "agreement type" (sub-domain) picker to the Create Agreement Analysis wizard (step 2).**
Your wizard today (hub design §6: *upload · access/associate/name/description · next-steps*) has **no explicit
agreement-type selection**. Our deterministic "explicit" entry path depends on it. The picker's value list comes from
**our sub-domain registry** (see §3, contract C1) — you render the control, we own the list.
- If the hub ships without it, the explicit path degrades to our classifier path (still works) until a fast-follow —
  but you lose the "user already told us the type" determinism your own concept use case relies on.

**A2 — Persist the selected/detected agreement sub-domain on `sprk_analysis`.**
You are already adding `sprk_worktype` (level-1 work type) as net-new schema (hub §11.1). There is **no level-2
sub-domain column** in either project's plan. Please add one (proposed `sprk_subdomain`, string/optionset) **with your
schema work**, so the chosen agreement type has a durable home. Landing it now = one migration; landing it later = a
second one owned by us.

**A3 — Carry `activeWorkType` + `subDomain` in the launch envelope.**
Per your open-Q #2 (`activeWorkType` host wiring) and §13.3 (`openSpaarkeAi` deep-link). Please confirm the launcher
sets **`activeWorkType='agreement-analysis'`** and includes the selected **`subDomain`** param (mirror the existing
`composeMode`/`openSpaarkeAiCompose` precedent), on **both** wizard-launch (2b) and open-existing (2d), so our machine
opens already-oriented (tool palette scoped + knowledge bound) instead of re-inferring.

### 🟡 Align during execution (we consume your surfaces — confirm, don't necessarily build early)

**A4 — Session ↔ Analysis binding for the classifier (chat-upload) path.**
You own fork-on-analysis + the `sprk_aichatsummary → sprk_analysis` binding (hub §3 rule #4, §11.7). When our
**"review this document"** classifier path starts a review, we want it to create/attach a **durable `sprk_analysis`
record via your binding** (so an ad-hoc review is reopenable, same as a wizard-launched one). Please confirm the
fork-on-analysis entry point is callable by a non-wizard trigger; if not available at our build time, we run the review
against the session transcript (Cosmos) and attach the Analysis when your binding lands — no rework on our side, but
we'd rather depend on it.

**A5 — `sprk_analysisoutput` shape for the Review Summary Memo.**
Our memo (#5) persists to `sprk_analysisoutput` (child of `sprk_analysis`; exists today, on your KEEP list). We write
structured fields + a JSON body for the changed-section array. Please confirm: (i) the 1:N `sprk_analysis →
sprk_analysisoutput` shape is stable, (ii) the analysis id is available to us at memo-generation time, (iii) no
conflicting write contract on that entity.

**A6 — We build only on your KEEP surfaces, not the retiring ones.**
Acknowledged from your §13: we will **not** build on `AnalysisWorkspace`, `sprk_analysischatmessage`, legacy
`AnalysisEndpoints` (`/resume`,`/continue`), or `sprk_chathistory`. We build on the KEEP set (`sprk_analysisoutput`,
`/export`, ChatEndpoints, shared widgets `NdaReviewSummaryPanel`/`FindingsWidget`/`AnalysisEditorWidget`). Please flag
if any KEEP item's retirement status changed.

---

## 3. Shared contracts (concrete shapes)

**C1 — Agreement sub-domain registry (agreements-r1 owns; hub consumes).**
We own the canonical list of agreement sub-domains + each one's knowledge-pack routing. Your wizard picker (A1) and any
type filter read from it. **Single source of truth — do not maintain a parallel list.** Registry is data-driven
(Action/Binding data, not code) so new agreement types (lease, employment, asset-purchase) register with **zero code**.
Initial entries: `nda` (shipped exemplar) + a `general` fallback. Proposed entry shape:
```
{ subDomain, displayName, knowledgePackRef (grounding source + clause taxonomy/rubric), classificationCue }
```

**C2 — Launch / hand-off envelope (hub → machine).**
```
openSpaarkeAi({
  ...existing,
  activeWorkType: 'agreement-analysis',   // A3 — scopes tool palette + orients Assistant
  subDomain: 'nda' | 'lease' | ... ,       // A1/A3 — the explicit user selection; authoritative
  analysisId, regarding, speDriveItemId/documentId
})
```
On the wizard path `subDomain` is **user-selected and authoritative** (no classifier guess). On the chat-upload path
it's **absent** and our classifier infers it.

**C3 — Persistence.** `sprk_analysis.sprk_worktype` (yours) = `agreement-analysis`; `sprk_analysis.sprk_subdomain`
(A2, new) = the chosen agreement type; `sprk_analysisoutput` (yours, KEEP) = the memo (A5).

---

## 4. What agreements-r1 will NOT build (boundary — so you don't assume we did)

- ❌ The hub widget, the Create Agreement Analysis **wizard**, or its steps (yours).
- ❌ The `sprk_analysis` spine schema, regarding field-set, subgrids, or the `sprk_worktype`/`sprk_subdomain` columns
  (yours — we only *specify* the sub-domain column need, A2).
- ❌ Session persistence / fork-on-analysis / session↔Analysis binding (yours — we *consume* it, A4).
- ❌ Retiring `AnalysisWorkspace` and the legacy session stack (yours).
- ❌ The **autonomous / email-intake** (no-human) review path — **explicitly out of scope for both projects here**;
  it's a future email-sibling concern. Our classifier is architected not to *preclude* headless invocation, but we
  build only the human-present interactive + wizard paths.

## 5. What agreements-r1 owns (so we don't collide)

- ✅ The document **classifier + orientation** (is-this-an-agreement + which-type → set `activeWorkType`, bind
  knowledge, focus tools/discussion) — the intelligence behind the chat-upload path.
- ✅ Type→knowledge **routing + the confirmation gate** — fired on **uncertainty** *or* **multiplicity** (a composite
  doc, e.g. employment + NDA addendum → "review as employment · just the NDA · both?", where **both = multiple packs**).
- ✅ The **sub-domain registry** (C1) and the per-type-knowledge-vs-general-fallback model (type-specific knowledge is
  the value; general grounding is the graceful fallback).
- ✅ Review-depth UX (multi-select batch actions, bidirectional summary↔note↔doc highlight, separated confirmations).
- ✅ The **Review Summary Memo** + toolbar (generate-docx / email) + **Word-comment export fidelity**.
- ✅ DEF-01 advisory-comment placement fix + the nda→agreements generalization/rename + WS-4 consumption.

---

## 6. Dependency & sequencing summary

| Capability | Needs the hub? |
|---|---|
| Classifier + orientation, review-depth UX, memo/export, DEF-01, rename | **No** — exercisable on the Assistant chat-upload path + today's entry. This is why agreements-r1 delivers value regardless of hub timing. |
| Wizard-driven **explicit** entry (concept steps 1–3) | **Yes** — A1/A3. |
| Durable, reopenable Analysis for a **classifier-started** review | **Yes** — A4 (degrades to transient without it). |
| Persisting the chosen sub-domain | **Yes** — A2. |

---

## 7. Decisions we need from the hub owner

1. **A1** — will the wizard's agreement-type picker land **in the hub before deploy**, or as a fast-follow (explicit
   path degrades to classifier path meanwhile)?
2. **A2** — approve adding **`sprk_subdomain`** to `sprk_analysis` with your in-flight schema work? (name/shape open —
   string vs optionset.)
3. **A4** — is fork-on-analysis / session↔Analysis binding **callable from a non-wizard (classifier) trigger** in your
   deliverable, or should we plan for transient-until-bound?

*Everything else (C1 registry ownership, C2 envelope, A5/A6) we can align on without blocking your deploy.*

---

*Companion to `projects/ai-advanced-capabilities-agreements-r1/design.md` (§ Relationship to analysis-hub-r1 + Lens 3d).
Source discussion: owner ↔ agreements-r1, 2026-07-29.*
