# UAT Failure Analysis → Design Input for Follow-Through (assistant-enhancements-r1)

> **Source**: Live browser UAT of `spaarke-ai-architecture-redesign-r2` (core), spaarkedev1, 2026-07-15 (operator run).
> **Purpose**: Capture *why* the current free-form-chat record-creation flows failed, and *how* the Follow-Through architecture resolves each. These use cases are the empirical motivation for this project — they are the highest-value, most-broken flows and the cleanest proof of the dispatcher thesis.
> **Cross-reference**: [`../design.md`](../design.md) §3 (NBA pipeline), §4 (risk tiers), §8 (tool drop-down), §10 (wizard entry-payload contract).
> **Companion (core-side defect record)**: `spaarke-ai-architecture-redesign-r2/notes/` UAT-defects (A5/A6 route to SpaarkeAi/compose surfaces).

---

## The one-sentence lesson

**Free-form chat is excellent at *drafting free text* and *proposing* values, and structurally bad at *resolving closed, system-owned value sets* and *committing constrained multi-field records* — so structured creation must be draft-in-chat, commit-in-a-pre-seeded-wizard, with the LLM never resolving a set the system already owns.**

---

## Use-case failures

### UC-1 — "create a follow-up task, assign it to me" (checklist A1)

**Observed**: Assistant demanded a file to "ground" the task; created a **`sprk_event`** record; showed **no association picker** (matter/project/invoice/none); did **not** assign to the user; took many turns.

**Failure reasons**
1. **Capability-modeling gap (root).** Catalog-verified: only two create capabilities are live — `create-matter` and `create-task`. There is **no `create-todo` and no `create-event`** — the single generic `create-task` produces a `sprk_event`. The assistant has no path that makes the record the user means.
2. **Spurious grounding requirement.** The capability treats task creation as needing document/session content to "ground" it, so it blocks on a file even for a trivial task.
3. **No association affordance.** The binding's disposition is `Informational`; no gate/association picker is wired. The A1 acceptance criteria expected a matter/project/invoice/none picker.
4. **Assign-to-me not honored** end-to-end (FR-B-06).

**How it should/could be resolved**
- **Model capabilities to the real entities.** To Do (`sprk_todo`) and Event (`sprk_event`) are distinct; the dispatcher must route to the right one. Entity-type ambiguity resolves by **high-confidence inference or a one-tap pick — never a text negotiation** (design §5 intent resolution; §4 gate-on-ambiguity).
- **Draft-in-chat → launch the To Do wizard pre-seeded** (title, due date, `assignee = current user`, association) — the wizard handles association/assignment/attachment natively (design §10 wizard entry-payload contract; §8 dispatcher hands off to destinations).
- **Grounding is optional.** A simple task must not require a document; grounding applies only when the user references source material.

---

### UC-2 — "create a to do task", then "a To Do not an Event" (checklist A2)

**Observed**: Assistant could not distinguish To Do from Event; over-elicited across ~8 turns (asked for title, then due date, then assignee, then a file, then a confirm); when finally created, the record was an **Event, not a To Do**, the file was **not attached**, and it was **not assigned**.

**Failure reasons**
1. **Un-routable request.** Same single `create-task`→`sprk_event`. When the user explicitly says "a To Do not an Event," there is literally no capability to honor it — so it silently makes an Event.
2. **Structured fields elicited one-per-turn** in chat = poor UX and high abandonment; each constrained field is a separate conversational round-trip.
3. **No deterministic form**, so assignment + attachment fall through the cracks.

**How it should/could be resolved**
- Same as UC-1: **the dispatcher launches the correct pre-seeded wizard.** The wizard *is* the structured surface (fields, dropdowns, attach, assign, review) — it replaces the multi-turn elicitation entirely, giving the user visibility + control on a substantial working surface (design §7 destinations; §1 draft-in-chat/commit-in-wizard).
- Operator's own read during UAT: *"this should be resolved by launching a wizard."*

---

### UC-3 — "create a new matter" → closed-set resolution dead-end

**Observed**: Assistant drafted the matter proposal fine (name, description). On confirm, it **failed to resolve** practice area "Commercial Transactions" and matter type "Litigation" to their exact values ("schema differences"), then **looped asking the user to supply the exact label names**, could **not open the record** (creation failed → no GUID), and dead-ended. It had also proposed an **incoherent pair** (a "Commercial Transactions" practice area with a "Litigation" matter type).

**Failure reasons**
1. **The LLM is resolving a closed, system-owned value set** (practice area, matter type — option set / config lookup) via free text, and failing to match. **This is the central anti-pattern.** The valid values are knowable; the assistant guessed instead of reading them.
2. **All-or-nothing commit.** It refuses to create without every field resolved, and cannot open a partial draft form — so the user is trapped.
3. **No picker fallback.** On match failure it asks the user to *recite* labels the system already holds — inverting the direction of knowledge.
4. **Unconstrained proposal** produced a nonsensical practice-area/matter-type combination.

**How it should/could be resolved**
- **The LLM never resolves a closed set.** Constrained fields resolve **deterministically against Dataverse metadata**: high-confidence match → pre-select; ambiguity/no match → **show the picker defaulted to the best guess** (design §3.1–§3.2 — candidate generation is deterministic/grounded, not LLM; the "RDAP" grounding step).
- **Draft-in-chat, commit-in-wizard.** Launch `CreateMatterWizard` pre-seeded: name/description filled, option-set fields rendered as **real dropdowns** defaulted to the best guess, source document attached. **The wizard owns the gated write** → a real `sprk_matter`, no dead-end.
- This makes the failure **structurally impossible** (the form only offers valid values) and **prevents the incoherent combo** (a grounded picker can't emit a nonsensical pair; free-text LLM resolution does so routinely).

---

### UC-4 — "delete this task" also closed the Compose tab (checklist A5)

**Observed**: The delete removed the Event correctly, but **also closed the unrelated Compose workspace tab**; the assistant retained the uploaded file.

**Failure reason**: **Cross-surface side-effect.** A chat action's completion triggered a workspace re-render / tab teardown that closed an unrelated Compose tab (tab-lifecycle bug; SpaarkeAi/compose keep-alive territory — compose-r2 did tab keep-alive work in round-8).

**How it should/could be resolved**
- **Action side-effects must be scoped to their own surface.** A record delete must never tear down unrelated workspace tabs; tab lifecycle must be independent of chat-action outcomes.
- **Relevance to this project**: the dispatcher's whole job is *triggering cross-surface actions*, so this is a first-class dispatcher requirement — an orchestrated action must not cause **collateral teardown** of other panes/tabs (design §2 "conductor, not stage" — the dispatcher moves work onto surfaces without disrupting them). Owner of the fix: SpaarkeAi/compose surface; but this project must not regress it.

---

### UC-5 — "draft a reporting letter in compose editor" → claimed opened, didn't (checklist A6)

**Observed**: Assistant said *"I have opened a draft reporting letter for the client in the Compose editor…"* — but **no Compose tab opened**.

**Failure reason**: **UI-action truthfulness failure (fabrication).** The assistant asserted a UI action (open in Compose) that did not occur — either the chat→Compose bridge failed silently, or the success text was optimistic. The D-F3 ack contract (claim only on a client acknowledgment referencing the emitted action; otherwise fail honestly) either did not cover this path or the deployed build predates it. *(Per the A7 caveat, the Compose-editor open is partly compose-r2's DEF-08 surface — triage core-ack vs compose ownership.)*

**How it should/could be resolved**
- **No optimistic UI claims.** Every action assertion — "opened X," "created Y," "saved Z" — is **gated on a client ack referencing the emitted action, or fails honestly** ("I couldn't open Compose").
- **Relevance to this project**: this is existential for a dispatcher. The entire value proposition is *taking actions on the user's behalf* — **a dispatcher that lies about what it did is worse than no dispatcher.** Ack-or-honest-failure is a non-negotiable invariant for every Follow-Through action (design §4 tiers — auto-run *inform* and one-tap *consequential* actions alike must report true outcomes).

---

## Cross-cutting principles (the reusable lessons)

| # | Principle | Design mechanism |
|---|---|---|
| **P1** | **The LLM never resolves a closed, system-owned set** (option sets, lookups, valid assignees, entity types). Resolve deterministically against metadata; picker on uncertainty. | §3.1–§3.2 grounded candidate generation |
| **P2** | **Draft-in-chat, commit-in-wizard.** Chat drafts free text + proposes; structured/constrained multi-field creation belongs in a pre-seeded wizard. | §1, §7, §8 |
| **P3** | **The wizard entry-payload contract is load-bearing** — files + resolved/proposed field values + source metadata → wizard. Currently §10-deferred; these findings argue to pull it forward. | §10 |
| **P4** | **Capabilities must be modeled to the real entities.** One generic `create-task`→`sprk_event` cannot serve "To Do vs Event." | §3.1 successor/capability modeling |
| **P5** | **No optimistic UI claims** — every action is ack-gated or fails honestly. | §4 tiers + truthfulness |
| **P6** | **Grounding is optional, not mandatory** — a simple task must not demand a document to be "grounded." | §3 pipeline (grounding ≠ prerequisite input) |

---

## Implications for R1 scope

1. **Pull the wizard entry-payload contract (§10) forward into R1.** "Create a matter / create a task" are the highest-value, most-broken flows *and* the cleanest proof of the dispatcher thesis. Deferring the hand-off leaves the single most-requested capability broken.
2. **Build the deterministic constrained-field resolver as a first-class primitive** — "match an LLM proposal against a valid set, return {pre-select | picker}." The ranker/candidate layer needs this shape anyway (P1); the matter/task flows are its first consumer.
3. **Decide the pre-seed intelligence level** (design open question): (i) thin hand-off (wizard dropdowns do all resolution) vs (ii) smart pre-seed (assistant pre-resolves + defaults the dropdowns). UC-3 shows (ii) is where the "it read the letter and filled the form" magic lives — but it requires the P1 resolver on the assistant side of the hand-off.
4. **Feeds the core UAT disposition decision** (A: patch chat creation vs B: route to wizard). This analysis supports **B** — do not bolt pickers and option-set resolution onto free-form chat.

---

## Traceability

| Use case | Checklist item | Surface / owner | Fix home |
|---|---|---|---|
| UC-1 task creation | A1 | chat capability + wizard | assistant-enhancements-r1 (route to wizard) + capability modeling |
| UC-2 To Do vs Event | A2 | chat capability + wizard | assistant-enhancements-r1 |
| UC-3 create matter | C1 (adjacent) | chat capability + wizard | assistant-enhancements-r1 (P1 resolver + §10) |
| UC-4 delete closes Compose tab | A5 | SpaarkeAi/compose tab lifecycle | core/compose surface; no-regress requirement here |
| UC-5 Compose-open fabrication | A6 / A7 | chat→Compose bridge ack | core-ack vs compose (DEF-08) triage; ack-invariant here |
