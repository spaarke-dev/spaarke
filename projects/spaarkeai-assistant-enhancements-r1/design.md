# Spaarke AI Assistant Enhancements R1 — Design (Working Document)

> **Status**: DRAFT — initial refinement document, **reality-aligned to as-built 2026-07-15** (see revision log). Not yet a committed spec. Captures the 2026-07-13 owner design conversation for `/design-to-spec` intake.
> **Codename**: **Follow-Through** (the Assistant finishes the thought the way the operator would have)
> **Positioning**: The Assistant stops being an "ask-me-anything" text box and becomes the system's intelligent **dispatcher** — it anticipates the operator's likely next step, grounds it in what Spaarke can actually do, and routes the work onto the existing surfaces.
> **Project ID**: `spaarkeai-assistant-enhancements-r1`
> **R1 Theme**: **Prove the (mostly-shipped) spine with one tangible surface.** Wire the Next-Best-Action machinery (≈80% already shipped under ADR-039) + the User Model that feeds it, surfaced through a new Assistant tool drop-down (Quick Start modal + "My Assistant" questionnaire). Proactive/ambient behavior is designed here but deferred to a later R.
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-07-15 (as-built alignment pass — see revision log)
> **Binding foundations (to verify during spec intake)**: [ADR-039 Grounded Execution & Closed Catalogs](../../docs/adr/ADR-039-grounded-execution-closed-catalogs.md) · [ADR-040 Session Ledger](../../docs/adr/ADR-040-session-ledger.md) — the NBA pipeline's grounding invariant rides ADR-039's closed-catalog (Action + Binding) model.
> **Platform reference (canonical as-built)**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md)
>
> ### Revision log
>
> **2026-07-15 (as-built alignment pass)** — After a code investigation of the shipped platform (three parallel `Explore` passes over `Services/Ai`, ADR-039/040/043, and the Dataverse data-model), this draft was **reality-aligned**: the NBA "four-stage pipeline" (§3) turned out to be ~80% already implemented under ADR-039's *one dispatch protocol · three entry paths · two closed catalogs* model. Corrections folded in: the Action entity is **`sprk_analysisaction`** (there is no `sprk_actiondefinition`); the intent surface the LLM matches against is the **Binding's** `sprk_tooldescription`, not an Action field; successor cards already exist as **`sprk_chiptransitions`** (each entry carries `target_binding_id`, `chip_label`, `requires_attachments`, `prefill_slots` — the last being a foothold for the deferred §10 entry-payload contract); risk lives on **`sprk_risk`**; grounding is the deterministic **`AgentToolProjection.PreFilter`** (a pure predicate — the ONLY dispatch aid ADR-039 permits). §§3–6, 9, 11, 12 rewritten to describe *extending the shipped catalog* rather than building a new pipeline. New binding constraint surfaced: the User Model / ranker **MUST NOT become a second probabilistic decider** (ADR-039 forbids a second intent mechanism) — added as a §12 decision. The genuinely-new R1 surface narrows to: the Assistant tool drop-down UI (§8), the **User Model / AI-readable user profile** (§6), a few additive grounding-predicate columns on the Binding, and authoring content (`sprk_tooldescription` + `sprk_chiptransitions` rows).
>
> **2026-07-13 (initial draft)** — Created from the owner design conversation that established: the Follow-Through concept, the Next-Best-Action (NBA) pipeline (candidate → ground → rank → tier), the Assistant-as-Dispatcher repositioning ("initiative, not real estate"; three-pane layout unchanged), the User Model (one artifact / three writers / one reader), the risk-tier + confirmation-gate reuse, intent resolution as the same pipeline shape upstream, the entry-point model (sensor / standing-state / actuator / destinations), and the R1 surface: an Assistant tool drop-down with a Quick Start modal + a "My Assistant" questionnaire. Learning loop authority ratified as **both** AI-adaptive and user-confirmed. compose-r2 noted as an **adjacent widget-model precedent, not a dependency** (it migrates Compose from a workspace *layout* to a workspace *widget* — unrelated to the wizard entry-contract concern).

This document leads with **what the operator experiences** and then maps each behavior to the architecture that powers it. Design follows from value.

---

## 1. Product Statement

Today Spaarke's Assistant is **reactive**: the operator asks, the Assistant answers or acts. That surface is powerful, but it positions the AI as a passive respondent and forces the operator to already know what to ask — and it forces the system to interpret arbitrary natural-language intent, which is the hardest and most error-prone thing it does.

**The motivating example (owner, 2026-07-13):** asked for domain-name ideas, a capable assistant didn't just brainstorm — it inferred the *goal behind the request* ("I want a domain I can actually register"), anticipated the operator's *next step* (check availability), and — critically — **had a tool to verify** (the domain registry) so it only surfaced names that were actually registrable. The intelligence wasn't creativity; it was **goal inference + next-step anticipation + a grounded action that made the suggestion true instead of merely plausible.**

**Follow-Through** brings that pattern to Spaarke legal operations. After the Assistant answers or an operator completes an action, the Assistant offers (or, when safe, performs) the **grounded, actually-possible next step** the operator would most likely want — "Send this summary to the recipient," "Update the matter record," "Save this to the DMS" — never an ungrounded guess.

R1 builds the **spine** that makes this reliable, plus one tangible surface that proves it and seeds it with data. Later releases invert the trigger from *reactive* (operator did something) to *proactive* (the system leads: "here's what's ready for you"), but the machine is the same.

### 1.5 Delivered product (R1 user synopsis)

What a Spaarke operator can DO when R1 ships:

1. **See Suggested Next Steps after an Assistant answer or a completed action** — a small, ranked set of *grounded* next actions (each one is something Spaarke can actually do in this context, right now), rendered as actionable cards, not buried text.
2. **Trust that suggestions never overreach** — informational suggestions may run automatically; anything consequential (send, update-of-record, file to DMS) is offered as a one-tap affordance, never silently executed.
3. **Open Quick Start from the Assistant** — a new **tool drop-down** in the Assistant pane opens the wizard library as a modal (Quick Start stops being a separate top-level surface and becomes a dispatcher tool).
4. **Tailor the Assistant via "My Assistant"** — a short questionnaire wizard collects role, focus areas, and preferences that seed both Assistant behavior and user memory, so the very first Suggested Next Steps are already relevant.
5. **Shape suggestions conversationally over time** — "stop leading with task reminders," "always surface my filing deadlines" — spoken to the Assistant, these update the operator's preferences; the Assistant can also *propose* a preference change ("I notice you keep dismissing these — want me to stop leading with them?") for the operator to confirm.

**Out of R1 (designed, deferred):** the *proactive* flip (Assistant speaks first with "here's what's ready"), the wizard entry-payload retrofit (Assistant launches + pre-seeds a wizard with an uploaded file), and Follow-Through surfaced outside the Assistant (on records / workspace widgets).

---

## 2. Core Concepts & Vocabulary

| Term | Meaning |
|---|---|
| **Follow-Through** | The behavior: the Assistant finishes the operator's likely intent — anticipates the next step and either performs it (if safe) or offers it (if consequential), always grounded in a real Spaarke capability. |
| **Suggested Next Steps** | The user-facing surface: the ranked, actionable cards the Assistant presents. |
| **Next-Best-Action (NBA) pipeline** | The mechanism behind both — **~80% already shipped** (ADR-039 three-path dispatch): intent via Text-path `capability_*` projection → deterministic `PreFilter` grounding → `sprk_chiptransitions` successor cards → `sprk_risk` gate. Same whether triggered reactively or, later, proactively. |
| **Assistant-as-Dispatcher** | The repositioning: the Assistant claims **initiative and orchestration**, not screen real estate. It leads the conversation about *what to do* and pushes work onto the existing panes. It is the **conductor, not the stage.** |
| **User Model** | The AI-readable user profile injected into the one agent turn to personalize suggestions — **never a separate ranker** (ADR-039). A read-time projection over `MemoryItem` (learned), membership/BU (role), and a new stated profile (interview). |

**Framing constraint (owner, 2026-07-13):** the three-pane design (Workspace · Assistant · Context) does **not** change, and the Workspace remains the primary surface because it is where work/content actually happens. The Assistant's repositioning is **behavioral, not spatial** — it does not become the landing surface; it becomes the surface that *takes initiative* and *routes*.

---

## 3. The Next-Best-Action Pipeline — mostly already shipped

> **As-built alignment (code investigation, 2026-07-15):** the four-stage pipeline this section originally proposed is **~80% already implemented** under **ADR-039** — *one dispatch protocol · three entry paths · two closed catalogs · every output grounded.* This section now describes the **shipped machinery** and the **thin R1 delta** on top of it, with the real field/class names. The owner's 2026-07-13 collapse (intent-resolution at the head, declared successor edges at the tail, deterministic grounding between) turned out to be essentially the as-built model — R1 **extends** it, it does not build it.

Two Dataverse tables carry the whole model:

- **Action** (`sprk_analysisaction`) — a **pure reusable prompt template**: `sprk_systemprompt` (the NL instruction the LLM executes) + `sprk_outputschemajson` (structured-output schema) + `sprk_temperature` + `sprk_inputschema` (the typed-argument contract). The "what runs." Post-R7 it carries **no** intent/risk/successor metadata by design.
- **Binding** (`sprk_playbookconsumer`) — the invocation unit and **the single routing surface** (ADR-039). Everything the owner wanted to "add to the Action" already lives here as columns: intent (`sprk_tooldescription`), risk (`sprk_risk`), successor cards (`sprk_chiptransitions`), surface pre-filter (`sprk_surfaces`), deterministic match predicates (`sprk_matchconditions`), event membership (`sprk_oneventbindings`).

> **Field-name correction:** an earlier draft referred to `sprk_actiondefinition` and to "declaring successors/risk on the Action." Neither exists. The entity is `sprk_analysisaction`, and intent/risk/successors are all **Binding** columns. The Action's own `sprk_description` is explicitly *NOT* the loop-facing intent surface — `sprk_tooldescription` (on the Binding) is.

### 3.1 Two layers (the owner's collapse), mapped to as-built

**Layer 1 — utterance/event → entry capability. SHIPPED (this *is* the "intent router").** On the **Text path**, every enabled Binding with a non-empty `sprk_tooldescription` is projected to the chat LLM as a `capability_{consumerType}` function-call tool: description = the maker-authored `sprk_tooldescription`, parameters = the target Action's `sprk_inputschema`. **The model's tool choice IS the dispatch decision.** This is the ONLY probabilistic decider on the platform — no separate classifier, keyword map, reranker, or vector router (ADR-039 forbids adding one). So "how does the Assistant recognize what the user wants?" = it reads the `sprk_tooldescription` of every in-context capability and function-calls one. The owner's "we need to author scripts that interpret what the operator enters" = **authoring good `sprk_tooldescription` text** on the right Bindings.

**Layer 2 — capability → successor cards. SHIPPED (`sprk_chiptransitions`).** After a capability runs, the platform emits curated next-step chips from the Binding's `sprk_chiptransitions` column. Each entry carries `target_binding_id`, `chip_label`, `bulk_chip_label`, `requires_attachments`, and `prefill_slots`. Clicking a chip is the **Click path** — it dispatches the target Binding by id through the same seam. This is the owner's "after Summarize, present cards for Create-matter / Add-to-DMS / Draft-reporting-letter": the successor edges are **authored, Binding→Binding, deterministic** — never inferred. R1's card UI renders the top N chips + a "more" affordance (the NBA-library modal).

### 3.2 Grounding is a deterministic pre-filter, not a scorer. SHIPPED.

The grounding worry — *"if the check can pick the 'best' candidates, aren't we back at auto-detecting from the full set?"* — is resolved in code by keeping grounding **binary, not a ranking**. `AgentToolProjection.PreFilter` is documented as *"a pure predicate — no scoring, no classification, no utterance inspection"* (ADR-039 permits exactly one scale aid: deterministic pre-filtering, **never** a decision-maker). It keys on structural session facts assembled at dispatch time — `Surface`, `HasSessionFiles`, `HasActiveDocument`, `HasAnalysisBinding`, and the host record (`ChatHostContext.EntityType`/`EntityId`) — plus the Binding's own `sprk_surfaces` and `sprk_matchconditions`. Grounding **removes the impossible; it never picks the best.** To hide "Create matter" when already inside a matter, you **add one catalog column** (e.g. `requires-no-attached-record`) tested against `ChatHostContext.EntityId == null` — no model call. The owner's instinct (a pre-narrowed, human-curated successor list makes this cheap and high-quality) is exactly the sanctioned pattern: a small curated set + a boolean truth-filter, not an AI relevance-scorer over the whole catalog.

### 3.3 The three entry paths already ARE the event-trigger model (§3.5's ask, satisfied)

The original §3.5 asked to "key the trigger on an event, not a chat message." The platform already does — the same dispatch seam is reached three ways:

| Path | Who picks the capability | Mechanism |
|---|---|---|
| **Click** | The caller (chip / ribbon / wizard) names it | `binding_id` **is** the routing decision — deterministic |
| **Text** | The chat LLM | Function-calls one `capability_*` tool from the closed projected set (Layer 1) |
| **Event** | Catalog data | `sprk_oneventbindings` membership (e.g. `document_uploaded` → auto-classify, then offer a Summarize chip) — deterministic |

The file-upload→Summarize flow the owner cited is the **Event path**, already wired: upload auto-runs *classify* (cheap Fast tier), then emits the "Summarize this document" chip from `sprk_chiptransitions`. Summarize happens only on the click. So the forward-compat trigger model is not something to design — it exists; R1 just wires the reactive (Text/Click) surfaces.

### 3.4 What R1 actually adds (the thin delta)

Strip out everything above (all shipped) and R1's genuinely-new work is:

1. The Assistant **tool drop-down** UI — Quick Start modal + My Assistant questionnaire (§8). *New client work.*
2. The **User Model / AI-readable user profile** (§6) — new persisted artifact, architected as a signal that *tunes the one existing decider and reorders the already-grounded chip set*, **never a second selection stage** (ADR-039 tension — §12).
3. A few **new grounding-predicate columns** on the Binding (the `requires-no-attached-record` style guards) — additive schema.
4. **Authoring content** — richer `sprk_tooldescription` + `sprk_chiptransitions` rows so the shipped machinery has good edges to traverse. *Content, not engineering.*

### 3.5 Where "ranking" goes — to the head, within ADR-039 limits

The owner's collapse correctly deletes a **tail** ranking stage. The soft/fuzzy work belongs at the **head** — the one function-calling agent turn that resolves intent — biased by the User Model. **Binding constraint (ADR-039):** the User Model MUST NOT become a second probabilistic decider (no reranker / vector classifier that scores capabilities to decide what to surface). Only two uses are permitted: **(a)** inject the user's profile/preferences into the one agent turn's context, and **(b)** reorder the already-grounded, already-authored `sprk_chiptransitions` for *display* (chips are Click-path; reordering their presentation is not a dispatch decision). See §6 and the §12 tension. Vectors as an intent mechanism are **out** — ADR-039 names "vector classifier" among the forbidden second mechanisms.

---

## 4. Risk Tiers & the Confirmation Gate — reuse the shipped gate

> **As-built (2026-07-15):** presentation/gating is already driven by catalog data — the tool's `side_effect_class` + the Binding's `sprk_risk` + the ONE confirmation gate (ADR-039). There is **no new "tier attribute" to invent**; the Inform/Reversible/Consequential model below is a *conceptual lens* over these shipped fields, not a new column.

`sprk_risk` (on the Binding) is a three-value posture: `None` · `Confirm When Uncertain` · `Always Confirm`. It complements the **tool-level** `side_effect_class` (which is what actually gates writes — ADR-039 MUST-NOT: never gate by hardcoded tool-name lists). Together they map the conceptual tiers:

| Conceptual tier | Examples | Shipped mechanism |
|---|---|---|
| **Inform** | Retrieve, list, summarize | read-class `side_effect_class`; `sprk_risk = None` → runs without a gate |
| **Reversible** | Draft, save to scratch, create a to-do | `sprk_risk = Confirm When Uncertain`; easy undo / one-tap |
| **Consequential / outward-facing** | Send email, update record-of-record, file to DMS, client-facing | write-class `side_effect_class` + `sprk_risk = Always Confirm` → the gate renders it as a **suggestion-that-launches**, never a silent act |

The "intelligent links" the owner described (Send summary to email / Update the record / Save to DMS) are the Consequential row: surfaced as a `sprk_chiptransitions` chip, gated by `side_effect_class` + `sprk_risk`. Same machinery — catalog data decides the surface.

**Gate reuse (ADR-039 — the ONE gate):** the confirmation gate fires on **risk** (`side_effect_class` / `sprk_risk`) and, upstream, **ambiguity** (§5). Do not build a second gate. A proactively-surfaced consequential action must stay a suggestion-that-launches, never a silent act.

---

## 5. Intent Resolution — the Same Agent Turn, One Step Upstream

> **As-built (2026-07-15):** intent resolution is **not a new subsystem** — it is the *same* single function-calling agent turn (Layer 1, §3.1) doing its job. There is no separate resolver, and ADR-039 forbids adding one: a term→concept "lexicon resolver" that runs before or beside the agent turn is exactly the banned second intent mechanism.

"Open a file" is ambiguous: a document (technical meaning) or a matter record (many legal users think *file = record*). This resolves inside the one agent turn:

- **Follow-Through**: completed situation → the agent function-calls the next **capability** → gate if *consequential* (`sprk_risk`).
- **Intent resolution**: utterance → the agent picks the right **capability** (or asks) → **elicitation / confirmation gate** if *ambiguous*.

Two consequences, both on shipped machinery:

1. **It reuses the gate.** Comparable-confidence meanings → the agent surfaces the elicitation gate ("Open the document, or start a new matter?"). No new disambiguation subsystem — the same gate, firing on ambiguity. (Missing required args already suspend into an elicitation gate today rather than letting the model guess.)
2. **The User Model collapses it per-operator.** If this operator always means "matter record," the profile injected into the agent context biases resolution so it *doesn't ask* — a Layer-1 bias, **not** a second decider.

**Where ambiguities are enumerated (ADR-039-safe):** *not* a standalone lexicon table that resolves terms before the agent turn (forbidden second mechanism). Instead, known ambiguities are authored **into the `sprk_tooldescription` text** of the competing capabilities ("use this when the user means the matter RECORD; if they may mean a document, prefer the document-open capability") so the one decider sees them — plus the elicitation gate to ask when it's genuinely uncertain. Free-form terms that miss everything fall through to the model's own judgment, the one permitted probabilistic decision.

> **R1 scope note:** the *machinery* to gate on ambiguity is shipped (same gate + elicitation). R1's dial is **how many ambiguities we author into tool descriptions** — a narrow high-frequency set (file, open, close, matter) vs. defer. See §14.

---

## 6. The User Model — an AI-readable profile, composed from mostly-existing stores

> **As-built alignment (code investigation, 2026-07-15):** the owner's reframing is right — the "User Model" **is** an AI-readable user profile (role, practice areas, office/location, preferences). But it is **not one new artifact.** Three stores already exist, and the profile is best modelled as a **read-time projection** that composes them and injects them into the one agent turn through a seam that already exists but is currently empty (`IOrganizationalContextProvider` → today a `NullOrganizationalContextProvider`). "One reader" is real and shipped; "one artifact" is not — a monolithic new store would duplicate governed stores and trip code review (ADR-042 flags any new memory store).

**The owner's question — put it on `systemuser`, or a new entity?** Neither extreme:
- **Do NOT add `sprk_` columns to `systemuser`.** Nothing in Spaarke extends the OOB user table; the universal pattern is a *related row keyed by `systemuserid`*.
- **Do NOT build one big new "User Model" store.** It would duplicate two governed stores. Compose instead:

| Owner's "three writers" | Store | Status |
|---|---|---|
| **Learned behavior** | `MemoryItem` **User scope** (Cosmos `memory-items`, ADR-042) — already carries provenance (`Source` = `user` / `ai-derived` / `insights-engine`) and is **already injected at chat time** by `ContextBinder.ResolveUserMemoryFragmentAsync` → `IMemoryItemStore.ToUserPromptFragmentAsync` | **EXISTS — reuse.** The `insights-engine` writer is reserved but unwired (a named follow-on). |
| **Role / persona defaults** | `sprk_userentityassociation` (discovered `sprk_role` per record) + OOB `businessunitid` / team / security roles — the identity graph to **reference, not copy** | **EXISTS — reference** via membership services + `CallerSystemUserResolver`. |
| **Stated / interview** (My Assistant §8) — role, focus areas, **practice areas, office/location**, preferences | **The genuinely-new slice** (practice-area / office-location have no home today). A **typed profile keyed by `systemuserid`**: recommended a small new `sprk_userprofile` entity (one typed row/user) over stuffing the untyped `sprk_userpreference` EAV blob | **NEW — the one real build.** Entity-vs-preference-type decided in §12. |

**One reader (shipped seam, currently empty):** implement `IOrganizationalContextProvider` (the null-object at `Services/Ai/Context/`, FR-B-11 seam) to project *stated profile + membership/role* into the agent context, alongside the User-scope memory fragment `ContextBinder` already injects. **Wiring this seam is most of the User Model's runtime value** — the store work is small; the injection is the point.

> **Note on `sprk_userpreference`:** a stated-preferences EAV table already exists (keyed to `systemuser`), but the investigation found a singular/plural schema discrepancy (`sprk_userpreference` vs `sprk_UserPreferences`, disagreeing on the lookup name and option-set meaning). **Confirm the live entity's exact logical name + option set before extending it** (§14 / spec intake).

**Binding invariants:**
- **Feeds the one decider, never the candidate set.** A preference or learned habit can bias the agent turn or reorder the already-grounded `sprk_chiptransitions`; it can **never grant a capability** (grounding still gates). Preference ≠ permission — this keeps proactivity from becoming privilege creep by observation, and keeps the User Model on the ADR-039-safe side of §3.5 (not a second decider).
- **Provenance per signal.** Native to `MemoryItem` (`MemoryOrigin`); the stated profile is provenance = `user` by construction. The reader weights stated vs learned and knows when to ask (`MemoryItem.ConfirmedByUser` is a shipped hook).

**Learning-loop authority (owner-ratified 2026-07-13 — BOTH):** the loop is **AI-adaptive and user-confirmed**. The Assistant adapts within tolerance *and* proposes explicit preference changes the operator confirms; when learned behavior contradicts a stated preference, the conflict is surfaced ("you said deadlines first, but you keep opening tasks — switch?") rather than silently overridden. Same user-in-control philosophy as the confirmation gate.

---

## 7. Entry-Point Model — Why Each Surface Exists

The overlap between Daily Briefing, workspace tabs, Quick Start, and the Assistant dissolves once each has a distinct job. The organizing principle: **STATE vs FLOW.**

| Surface | Role | STATE / FLOW |
|---|---|---|
| **Daily Briefing** | The **sensor**. Read-only situational awareness — "here's what's *true* about your world." Detects; does not act. | State (computed) |
| **Workspace tabs** | **Standing state you curated.** Pull, not push — "the things I always want on-hand." | State (curated) |
| **Assistant** | The **actuator / dispatcher.** "Here's what to *do* about that state — and I'll take you there." Absorbs Quick Start. | Flow |
| **Wizards / lists / workspace views** | **Destinations** — the substantial working surfaces the Assistant hands off to. | — |

**Reference interaction (the chain):** Daily Briefing (sensor) says *"You have 7 overdue tasks"* → Assistant (actuator) says *"Let's review them"* → opens the list in a workspace tab (destination). Sensor detects → actuator proposes flow → destination holds the work.

**Quick Start's fate:** a static menu of launchers with zero awareness of state or history is the *un-smart* version of exactly what the dispatcher does dynamically. Quick Start is **absorbed into the Assistant** as a tool (§8), and the dispatcher becomes the *contextual, ranked* Quick Start — "here are the actions worth doing right now," not "here is everything you could create."

---

## 8. R1 Surface — the Assistant Tool Drop-Down

R1's tangible, self-contained surface. The Assistant pane gains a **tool drop-down** (the pane's first step beyond a chat log toward the richer component vocabulary a dispatcher needs). It contains at least two new components:

1. **Quick Start** — opens a **modal presenting the wizard library** (the existing Create* wizards). Reuses existing wizards as-is; this is Quick Start relocated from a top-level surface into the dispatcher.
2. **My Assistant** — opens a **questionnaire wizard** that collects role, focus areas, and preferences, and **seeds both the stated profile and user memory** (i.e., writes the initial User Model — §6). This is the cold-start seed for the User Model the agent turn reads.

Why this surface is the right R1 proof: it is self-contained, it does not disturb the three-pane layout, it reuses existing wizards, and — critically — **My Assistant produces the exact data the User Model needs**, de-risking every downstream (proactive, workspace-wide) surface.

---

## 9. R1 Scope Boundary

**In R1 (the thin delta on the shipped catalog — see §3.4):**
- **Reuse** the shipped NBA machinery, no new pipeline: Text-path `capability_*` projection (candidate generation) → `AgentToolProjection.PreFilter` (grounding) → `sprk_chiptransitions` (successor cards) → `sprk_risk` + confirmation gate (tier gate).
- **Suggested Next Steps UI** — render the emitted `sprk_chiptransitions` chips as ranked actionable cards in the Assistant pane (reactive trigger: after an answer / completed action), + a "more" affordance opening the NBA-library modal.
- **The Assistant tool drop-down** — Quick Start modal + My Assistant questionnaire (§8).
- **The User Model / AI-readable user profile** (§6) — the new typed stated-profile slice + wiring `IOrganizationalContextProvider`; provenance flags; the feeds-the-decider-never-grants-capability invariant; and the ADR-039 not-a-second-decider constraint (§3.5 / §12).
- **A few additive grounding-predicate columns** on the Binding (e.g. `requires-no-attached-record`) + authoring content (`sprk_tooldescription` / `sprk_chiptransitions` rows).
- Event-keyed trigger is **already shipped** (§3.3, the three paths) — R1 wires the reactive Text/Click surfaces onto it.

**Designed here, deferred to later R:**
- **Proactive / ambient triggers** — the Assistant *speaks first* ("here's what's ready for you"), fired by system/time events (deadlines, Daily-Briefing outputs, new arrivals). *(Candidate for a single R1 proof — see §14.)*
- **Wizard entry-payload contract** — the Assistant *launches and pre-seeds* a wizard with an uploaded file (§10). *Note: `sprk_chiptransitions.prefill_slots` is a shipped foothold — the gap is narrower than a new contract.*
- **Follow-Through outside the Assistant** — Suggested Next Steps on records / workspace widgets / create-wizard completion.
- **Broad ambiguity coverage** — R1 authors a *narrow* set of ambiguities into `sprk_tooldescription` text (§5); full breadth is later. (There is no separate "lexicon" to build — ADR-039 forbids a lexicon-resolver.)

---

## 10. Wizard Interaction & the Entry-Payload Contract (deferred; design intent)

> **As-built foothold (2026-07-15):** `sprk_chiptransitions` entries already carry a **`prefill_slots`** field, and the Event path already threads uploaded `fileIds` into dispatch `args`. So the "envelope" is *partially shipped*: a successor chip can already declare which slots to pre-seed on the next capability. The remaining gap is narrower than "a new contract" — it is connecting `prefill_slots` (+ a handed-in file) to the wizards' Field-Mapping pre-fill seam. This downgrades §10 from "spike a new contract" toward "wire two existing seams." Still deferred from R1, but re-scope the spike accordingly.

**The future scenario:** operator uploads a file → Assistant offers "Create a new matter" → the file + inferred field values flow into the wizard and pre-fill it.

**Assessment (retrofit, not re-architect — to confirm in a spike):** the interaction needs one new thing — a **wizard entry-payload contract**: a standard envelope (files, seed field values, source metadata) any caller can open a wizard with, plus letting the AI pre-fill consume a *handed-in* file rather than only one uploaded *inside* the wizard. The optimistic part: the wizards **already have a creation-time pre-fill seam** (the Field Mapping Framework, wired into all seven `Create*Wizard` services). The Assistant's pre-fill feeds *Assistant-supplied seed values* into that existing seam. Bracketed by two existing seams — **PaneEventBus carries the launch in, the Field Mapping pre-fill applies it** — the only genuinely new piece is the entry-payload contract in between.

**Investigation question (spike):** do the seven `Create*Wizard` flows share a common host/entry mechanism (→ additive contract, retrofit) or is each bespoke (→ the "re-architecture" is really *extracting a common entry contract*, worth doing regardless)?

**Not related to compose-r2:** compose-r2 migrates Compose from a workspace *layout* to a workspace *widget*; it is an adjacent widget-model precedent, **not** a dependency of this contract.

---

## 11. Architecture Placement & Governance (stubs — complete during spec)

### 11.1 Hot-Path Declaration (per root CLAUDE.md §10 / bff-extensions §G)

```xml
<hot-path-declaration>
  <bff>Y — confirmed: `IOrganizationalContextProvider` implementation + stated-profile store access in Services/Ai/Context; reuses existing dispatch + memory seams (no new pipeline/ranker)</bff>
  <spaarke-ai>Y — Assistant pane tool drop-down + My Assistant questionnaire + Suggested Next Steps cards</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

### 11.2 Placement Justification (per root CLAUDE.md §10)

To be completed for each major new component. Anticipated components + their placement question:
- **Suggested Next Steps consumer** — there is **no new NBA orchestrator**; the runtime reuses the shipped **session-dispatch seam** (`SessionDispatchOrchestrator` / `IConsumerRoutingService`) and the `sprk_chiptransitions` emission. The only placement question is *where the SNS card-projection + User-Model read compose* (BFF `Services/Ai` vs client). No new dispatch path (compose-r2 invariant). (Per §10-bullet-3, use the `Services/Ai/PublicContracts/` facade if CRUD code needs it.)
- **User Model store** — resolved by the 2026-07-15 investigation (§6): learned signals → existing `MemoryItem` **User scope** (ADR-042 — do NOT duplicate); role/defaults → `sprk_userentityassociation` + BU/team; only the **stated typed profile** (practice-area / office-location) is new (`sprk_userprofile` entity vs `sprk_userpreference` type — decide in §12). Reader = the shipped-but-empty `IOrganizationalContextProvider` seam.
- **User Model reader** — the `IOrganizationalContextProvider` implementation (BFF `Services/Ai/Context`); what it projects (stated profile + membership + the `MemoryItem` fragment `ContextBinder` already injects). **Not a ranker** (ADR-039 — §3.5).
- **Publish-size impact** — measure per §10-bullet-4 (≤60 MB ceiling; current baseline ~49.63 MB incl. PDBs).

### 11.3 Component Justification (per root CLAUDE.md §11 — default to reuse)

Reuse-first candidates (verify with grep before claiming "new"):
- **Bindings / Actions catalog (ADR-039)** — grounding stage reuses it; do not build a parallel capability registry.
- **Session-dispatch seam** — dispatch reuses `POST /api/ai/chat/sessions/{id}/dispatch`; **no new BFF dispatch endpoint** (the compose-r2 AI-dispatch invariant applies here too).
- **PaneEventBus (`@spaarke/events-components`)** — cross-pane dispatch (Assistant → Workspace/destination). Investigation: is it rich enough to carry a "launch with payload" event?
- **Field Mapping Framework** — wizard pre-fill seam (§10).
- **User memory (`MemoryItem` User scope, ADR-042)** — the User Model's learned-behavior store; already read at chat time by `ContextBinder`. Do NOT build a second memory store. Insights Engine's charter *extends* to usage-insights (§13); the `insights-engine` write-origin is reserved-but-unwired.
- **`IOrganizationalContextProvider`** — the shipped null-object injection seam (FR-B-11); implement it, don't invent a new context path.
- **`CallerSystemUserResolver`** — canonical AAD `oid` → `systemuserid`; reuse for any profile lookup.
- **Confirmation gate (`sprk_risk` + `side_effect_class`)** — tier + ambiguity gating; do not build a second gate.

Genuinely-new surface (needs the three-question justification in spec) — **much smaller than the original draft claimed**: successor edges (`sprk_chiptransitions`), risk (`sprk_risk`), and the intent surface (`sprk_tooldescription`) **already exist** — reuse, not new. The only genuinely-new surface is: the **stated typed user-profile slice** (`sprk_userprofile`), the **`IOrganizationalContextProvider` implementation**, the **tool drop-down UI** (§8), and **a few additive grounding-predicate columns** on the Binding. There is **no** "mapping layer," "risk-tier attribute on Actions," or "curated lexicon" to build.

---

## 12. ADR-Level Decisions to Make (decide before task decomposition)

1. **ADR-039 tension — the User Model MUST NOT become a second decider (BINDING).** ADR-039 forbids any second intent mechanism (regex, keyword map, vector classifier, reranker, routing middleware); the ONE probabilistic decider is the single function-calling agent turn. The User Model may therefore only **(a)** bias that one agent turn (profile injected via `IOrganizationalContextProvider`) and **(b)** reorder already-grounded `sprk_chiptransitions` for *display*. **Decision:** ratify as a project-scoped design constraint (path A, root §6.5); confirm no ranking/scoring stage is introduced. *This supersedes the original §3.3 "ranking stage" and the §3/§5 "vectors" language.*
2. **Stated-profile store shape + the feeds-the-decider-never-grants-capability invariant.** Learned/role slices reuse `MemoryItem` + `sprk_userentityassociation` (§6). The open call: the **new stated profile** as a typed `sprk_userprofile` entity (recommended) vs a new `sprk_userpreference` type — *and first resolve the `sprk_userpreference`/`sprk_UserPreferences` singular-plural schema discrepancy against the live env.* Plus the binding rule: preference ≠ permission; grounding still gates.
3. **Insights Engine charter extension** — from "insights on legal record data" to "insights on system usage / workflow" (the learned-behavior writer into `MemoryItem`, `source=insights-engine` — reserved but unwired). Amendment vs project-scoped note.
4. **Authoring *ownership*** — the authoring *home* is already decided (it is `sprk_tooldescription` + `sprk_chiptransitions` on the Binding, BA-editable in Dataverse, zero-deploy — **not** an ADR question anymore). The remaining call is *who owns* authoring the operator's workflow model: maker-per-Binding vs a central curation role. A genuine org/ownership decision, not a technical one.

---

## 13. Relationship to Adjacent Projects

| Project | Relationship |
|---|---|
| **Insights Engine** (`ai-spaarke-insights-engine-*`) | The **learning half** of Follow-Through belongs here: mining usage telemetry to write `MemoryItem` User-scope signals (`source=insights-engine`, reserved-but-unwired) and to reveal *missing* `sprk_chiptransitions` edges. **Boundary:** the *runtime* path (project profile → agent turn → grounded chips) is a hot interactive concern and must NOT take a dependency on the analytics subsystem. Insights Engine observes and feeds the profile; the runtime consumes a cheap projected fragment. |
| **compose-r2** (`spaarkeai-compose-r2`) | Adjacent **widget-model precedent** (layout → widget migration) — **not a dependency.** Noted only so the wizard entry-contract work (§10) does not diverge from Compose's widget direction later. |
| **ai-architecture-redesign-r1 / r2** | Charter/platform baseline for the Action + Binding + session-dispatch surface this project builds on. |

---

## 14. Open Questions / Decisions Pending

1. **One proactive proof in R1?** The scope boundary (§9) defers all proactive/ambient behavior. Should R1 include **one** proactive trigger — e.g., the Daily-Briefing → "let's review them" handoff (§7 reference interaction) — to prove the reactive→proactive "flip," or is the reactive spine + tool drop-down the right, tighter R1? *(Owner input requested.)*
2. **Authoring ownership** (§12.4) — the authoring *home* is decided (Binding columns, BA-editable, zero-deploy); the open call is *who owns* it: maker-per-Binding vs a central curation role. Org/ownership, not technical.
3. **Ambiguity coverage for R1** (§5) — how many high-frequency ambiguities (file, open, close, matter) to author into `sprk_tooldescription` text vs defer and let the elicitation gate fire only when the model is unsure. *(No separate lexicon to build — ADR-039 forbids a lexicon-resolver.)*
4. **Stated-profile shape** (§12.2) — largely resolved by the 2026-07-15 investigation (learned → `MemoryItem`; role → membership/BU). Remaining call: new `sprk_userprofile` entity vs new `sprk_userpreference` type — *after* confirming the `sprk_userpreference` / `sprk_UserPreferences` singular-plural schema discrepancy against the live env.
5. **BFF hot-path + publish size** (§11.1) — confirm the `IOrganizationalContextProvider` impl + stated-profile access land in BFF (they do) and run the ≤60 MB publish-size check (baseline ~49.63 MB incl. PDBs).
6. **Suggested Next Steps after a *non-dispatching* answer** (§1.5-item-1 vs §3.1) — the shipped `sprk_chiptransitions` chips are emitted when a **capability runs** (Click / Event / Text-dispatch). A purely conversational answer that dispatches **no** capability emits no chips today. So the "See Suggested Next Steps after an Assistant *answer*" promise (§1.5) exceeds the shipped mechanism for bare Q&A. **Decision:** either (a) accept that SNS appears only after a capability runs — and lean on Layer-1 tool descriptions so questions like "what are my tasks" *dispatch* a `list-tasks` capability (whose `sprk_chiptransitions` then fire), or (b) build new machinery to derive successors from a non-dispatching answer (heavier; risks re-introducing an ungrounded suggestion path). Recommend (a) — it keeps everything grounded and needs only authoring, not new code. *This is the single place the R1 UX promise and the as-built mechanism diverge — resolve at spec intake.*

---

> **Next step:** owner review of this draft → resolve the §14 open questions → run `/design-to-spec` to produce `spec.md` → `/project-pipeline` → `/task-create`. This file needs git tracking activated in the main worktree (`git add projects/spaarkeai-assistant-enhancements-r1/design.md`).
