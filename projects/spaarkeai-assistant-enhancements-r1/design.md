# Spaarke AI Assistant Enhancements R1 — Design (Working Document)

> **Status**: DRAFT — initial refinement document, **reality-aligned to as-built 2026-07-15** (see revision log). Not yet a committed spec. Captures the 2026-07-13 owner design conversation for `/design-to-spec` intake.
> **Codename**: **Follow-Through** (the Assistant finishes the thought the way the operator would have)
> **Positioning**: The Assistant stops being an "ask-me-anything" text box and becomes the system's intelligent **dispatcher** — it anticipates the operator's likely next step, grounds it in what Spaarke can actually do, and routes the work onto the existing surfaces.
> **Project ID**: `spaarkeai-assistant-enhancements-r1`
> **R1 Theme**: **Prove the (mostly-shipped) spine with one tangible surface.** Wire the Next-Best-Action machinery (≈80% already shipped under ADR-039) + the User Model that feeds it, surfaced through a new Assistant tool drop-down (Quick Start modal + "My Assistant" questionnaire). **R1 is reactive-first (owner decision 2026-07-15, driven by R2 UAT):** R1 core fixes the highest-value, most-broken flows — **draft-in-chat → pre-seeded-wizard structured creation** (create-matter/to-do/event), the deterministic constrained-field resolver, and the action-truthfulness invariant (§10, §4) — alongside the User Model and tool drop-down. The **full proactive-push capability** (server-initiated push via Azure SignalR + durable outbox, §14.1a) is fully designed and **sequenced as R1.5**, immediately after — because a proactive suggestion must not launch into a create flow that doesn't yet work.
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-07-15 (as-built alignment pass — see revision log)
> **Binding foundations (to verify during spec intake)**: [ADR-039 Grounded Execution & Closed Catalogs](../../docs/adr/ADR-039-grounded-execution-closed-catalogs.md) · [ADR-040 Session Ledger](../../docs/adr/ADR-040-session-ledger.md) — the NBA pipeline's grounding invariant rides ADR-039's closed-catalog (Action + Binding) model.
> **Platform reference (canonical as-built)**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md)
>
> ### Revision log
>
> **2026-07-15 (R2 UAT integration → reactive-first R1)** — Distilled the redesign-r2 live-UAT analysis ([`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md)) into the design and **re-sequenced R1 to reactive-first** (owner decision). The UAT is empirical proof of the design's thesis — free-form chat fails exactly at closed-set resolution + constrained multi-field commits — and shows the **most-broken, highest-value flows (create matter / to-do / event) require the wizard hand-off** the design had *deferred*. Changes: **§10 moved deferred → R1 core** (retitled "Structured Creation — Draft-in-Chat, Commit-in-Wizard"), with the six UX invariants **P1–P6** distilled as binding requirements + the UAT doc cited as evidence base (not pasted). **Two new primitives added:** the **deterministic constrained-field resolver** (P1, §10/§15.5 — the field-value analogue of grounding; fixes the UC-3 dead-end) and the **action-outcome truthfulness invariant** (P5, §4 — ack-gated claims or honest failure; UC-5 fabrication). **Capability modeling** (P4: distinct `create-todo`/`create-event`) + "To Do vs Event" as a §5 ambiguity. **Full proactive-push (§14.1a) re-sequenced R1→R1.5** — because a proactive suggestion must not launch into a create flow that doesn't yet work. Header, §1.5, §9, §15.4/§15.5 updated. Also folded in the earlier owner-ratified **general notification-spine** framing (§14.1b: SignalR + outbox as a `kind`-typed spine; suggestions are consumer #1) and the **producer-topology** clarification (SignalR delivers to live browsers only; jobs write Dataverse + outbox, then optionally ping).
>
> **2026-07-15 (push-channel researcher spike → Azure SignalR)** — Resolved §12.5 with a source-cited comparison (Azure SignalR vs Web PubSub vs persistent SSE vs polling). **Decision: Azure SignalR Service, Default mode (hub-in-BFF), Standard tier** — Microsoft's documented escape hatch for real-time on App Service (clients connect to the managed service, so ARR affinity / 230s idle timeout / scale-out fan-out all vanish); per-user delivery via `Clients.User(oid)`; ~$49/mo/unit; ~40 KB client SDK. **Durability is mandatory regardless of channel** (all options at-most-once, no offline replay) → the transport is only a "suggestions changed → fetch" signal; payload authority lives in the outbox. Refined §14.1a layer 3: the authoritative outbox is a **thin new `sprk_` pending-suggestion table** (not `appnotification`, which renders in the MDA notification center — kept as an optional mirror). §12.5, §14.1a, §11.1, §15.4 updated. Rejected Web PubSub (not a SignalR replacement for .NET) and persistent-SSE (poor App Service end-state). Full report in researcher memory. Open: verify target-env CSP (`connect-src`) before design freeze.
>
> **2026-07-15 (owner decisions on the three §15.1 scope calls)** — Owner resolved all three: **(1) Proactive → build the FULL real capability**, not the same-session quick-win ("we need the full, real capability"). Added **§14.1a** (five-layer target architecture) + **§12.5** (real-time channel decision, under a researcher spike). Key enabling finding: the **durable pending-suggestion outbox already exists** — the native Dataverse `appnotification` entity, written server-side by `NotificationService` and already read by Daily Briefing — so "full and real" reuses four of five layers; the only new infra is the real-time delivery channel (a latency upgrade over the free next-load fallback, not a correctness dependency). Header, §1.5, §9 updated; this is a **material scope expansion** (WBS decides one-R1-vs-split). **(2) `sprk_risk` → wire live in R1** (§4/§15.1). **(3) User-slice token budget → amend the 300 ceiling** (§6/§15.1). §15.1 rewritten from "decisions needed" to "resolved."
>
> **2026-07-15 (Fable four-agent as-built review)** — Ran a full design-vs-code verification with four parallel Fable agents (dispatch spine · User-Model seam · client/proactive · cross-cutting governance). **All three prior 2026-07-15 corrections verified** against code + live Dataverse (User-fragment reader seam; empty-skeleton `sprk_userprofile`; and the `systemuser.sprk_userprofile` lookup **empirically confirmed to exist**). Reactive spine confirmed real and often *more*-shipped-than-claimed (`ConsumerChips` already renders `aria-label="Suggested next steps"`; `wizard_step` `set-field` pre-fill already works; tool-drop-down + wizard-card patterns exist as siblings). **Corrections folded in:** §3.2 (host record is NOT on the filter context — the grounding guard is a 4-part plumbing task, and `sprk_matchconditions` is not part of Text-path grounding); **§4 (`sprk_risk` is data-plumbed but NOT gate-wired — a real, previously-unscoped R1 work item)**; §6 (preferences bug spans 6+ client surfaces; token-budget/byte-stability/caching realities); §9 + §12.1 (stale `IOrganizationalContextProvider` lines removed); §11.1 (hot-path format → binding bff-extensions **§H** `<bff-api>YES` shape; registry obligation); §13 (redesign-**r2 Phase E** is a merge-ordering dependency on the same `ContextBinder`/dispatch-spine files, not just a baseline); **§14.1 (NO server-push/polling channel exists anywhere in the client — the proactive proof must be scoped to same-session client-originated dispatch).** New **§15** consolidates all spec-intake obligations + gaps (eval-case merge gate, testing/seam DoD, security/authZ of profile data, three unspecified write paths, NFR/latency/caching, ADR-040/041/043 engagement, governance registration). Net verdict: reactive-spine claims hold well; the design engages ADR-039/042 deeply but leaves a **heavier-than-"thin-delta" spec-intake load** now captured in §15; three items (§15.1) need an owner call before `/design-to-spec`.
>
> **2026-07-15 (owner §14 answers + deeper as-built verification)** — Owner answered the six §14 open questions (2026-07-15 chat). Folded in, with two new code-verified corrections that go beyond the answers: **(1)** the `sprk_userprofile` entity the owner confirmed exists is, per a live Dataverse `describe`, a **bare skeleton** — only OOB system columns + `sprk_name`; it has **none** of the profile fields (role / focus areas / practice areas / office-location / preferences). So the store decision is settled (use `sprk_userprofile`, drop the `sprk_userpreferences`-type option) but the real R1 build is **adding the columns + the read/write wiring**, not "one new entity." The owner also states the relation is a **lookup on `systemuser` → `sprk_userprofile`** (verify direction at spec intake — the OOB user table normally is not extended; §6). **(2) Reader-seam correction (supersedes the §6/§11/§12 `IOrganizationalContextProvider` framing):** reading `IOrganizationalContextProvider.cs` shows it is an **inbound *Organizational*-scope seam (Work IQ — org chart / team / reporting line), with a counts-only result shape and its runtime provider deferred by owner ruling 2026-07-08.** The Assistant's **stated user profile** (practice areas, office, preferences) is **User-scope**, not organizational — its natural injection point is the **User fragment** path in `ContextBinder` (sibling to the `MemoryItem` User fragment already composed by `ResolveUserMemoryFragmentAsync` → `ToUserPromptFragmentAsync`), **not** the org provider. **R1 therefore does NOT implement `IOrganizationalContextProvider`** (it stays deferred as the Work IQ seam); R1 adds a User-scope stated-profile producer into the existing `userFragment` composition. §§6, 11.2, 12.2 and §14 rewritten accordingly. §14 open questions resolved below.
>
> **2026-07-15 (post-pull reconciliation)** — Re-verified every as-built claim against `master` after pulling 144 commits from origin (heavy on `spaarkeai-compose-r2` + `email-r4`) — the earlier alignment pass had run on code 144 commits stale. **Verdict: the dispatch/catalog spine, the User Model substrate, and the shared wizard/PaneEventBus seams are all intact.** compose-r2 changed the shared seams only *additively* (added a `Compose` disposition + PaneEventBus discriminants) and *strengthened* the no-new-dispatch-endpoint invariant by retiring the parallel `/api/compose/action` route; `IOrganizationalContextProvider` is still an unimplemented null-object (the §6 premise holds); email-r4 is orthogonal. Four corrections folded in: PaneEventBus lives in **`@spaarke/ai-widgets`** (`Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`), not `@spaarke/events-components`; `IOrganizationalContextProvider` is under `Services/Ai/PublicContracts/`, not `Services/Ai/Context/`; the live preferences entity is the **plural `sprk_userpreferences`** (the DailyBriefing client's singular `sprk_userpreference` is a real mismatch bug); and `AgentToolProjection.PreFilter` currently branches only on `Surface` + `sprk_surfaces` — the three session facts are declared-but-unused, so a grounding guard is a new column **+ a new PreFilter branch**.
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

6. **Create records reliably from chat** — "create a matter / a to-do / an event" **draft in chat, then commit in a pre-seeded wizard** with real dropdowns and no dead-ends (fixes the R2 UAT create-flow failures; §10). The Assistant never guesses a closed value set, and never claims an action it didn't actually complete (§4).

**R1.5 (designed, sequenced immediately after R1):** the full **proactive-push capability** — the Assistant surfaces a grounded, gated suggestion while the user is idle (Azure SignalR + durable outbox + Daily-Briefing producer, §14.1a). Sequenced after R1 so proactive suggestions launch into create flows that actually work.

**Out of R1/R1.5 (designed, deferred):** broader proactive producers (filing deadlines, new arrivals), other notification kinds on the shared spine (§14.1b), and Follow-Through surfaced outside the Assistant (on records / workspace widgets).

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

The grounding worry — *"if the check can pick the 'best' candidates, aren't we back at auto-detecting from the full set?"* — is resolved in code by keeping grounding **binary, not a ranking**. `AgentToolProjection.PreFilter` is documented as *"a pure predicate — no scoring, no classification, no utterance inspection"* (ADR-039 permits exactly one scale aid: deterministic pre-filtering, **never** a decision-maker). Today it branches **only on `Surface` + the Binding's `sprk_surfaces`**. (Corrected 2026-07-15: `sprk_matchconditions` is **not** part of Text-path grounding — it is evaluated only in `ConsumerRoutingService.ResolveBinding` on the Click/named-intent path; a text-projected Binding's match conditions are ignored.) The `AgentToolFilterContext` carries `HasSessionFiles`, `HasActiveDocument`, `HasAnalysisBinding` — **declared-but-not-yet-consumed** (reserved for future catalog columns). **Correction (Fable review 2026-07-15):** the host record (`ChatHostContext.EntityType`/`EntityId`) is **NOT** on the filter context — it lives on `ChatHostContext` upstream at `SprkChatAgentFactory`, and is never threaded into `AgentToolFilterContext`. Grounding **removes the impossible; it never picks the best.** So the `requires-no-attached-record` guard is slightly more than "one column + one branch": it needs **(1)** a new catalog column, **(2)** a new field on `AgentToolFilterContext`, **(3)** threading `ChatHostContext.EntityId` from `SprkChatAgentFactory` into the filter context, and **(4)** a new `PreFilter` branch — still all additive/deterministic (no model call), but a real, if small, plumbing task, not a one-liner. The owner's instinct (a pre-narrowed, human-curated successor list makes this cheap and high-quality) is exactly the sanctioned pattern: a small curated set + a boolean truth-filter, not an AI relevance-scorer over the whole catalog.

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

**Action-outcome truthfulness (P5 — BINDING, from R2 UAT).** Separate from the risk gate but non-negotiable for a dispatcher: **every action claim ("opened X", "created Y", "saved Z") is gated on a client acknowledgment referencing the emitted action, or fails honestly** ("I couldn't open Compose"). UAT UC-5 showed the assistant *fabricating* a completed UI action ("I have opened a draft in Compose") that never happened — **a dispatcher that lies about what it did is worse than no dispatcher.** R1 adopts the platform's D-F3 ack contract (redesign-r2 core) as a Follow-Through invariant across all three tiers (auto-run *Inform* and one-tap *Consequential* alike must report true outcomes). Related no-regress guard: an orchestrated action must **not** cause **collateral teardown** of unrelated panes/tabs (UC-4: a delete closed an unrelated Compose tab) — the dispatcher is "conductor, not stage" (§2); side-effects stay scoped to their own surface. See [`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md).

> **⚠️ As-built correction (Fable review 2026-07-15) — `sprk_risk` is data-plumbed but NOT gate-wired.** `PendingPlanManager.RequiresConfirmation(sideEffectClass, risk, dispatchUncertain)` implements the full policy, but the **only production call site passes `side_effect_class` alone** — `risk` defaults to `None` and `dispatchUncertain` is always null (no live routing-confidence producer). Today the gate fires **only** on `side_effect_class` Write/Communicate; `Binding.Risk` is read in production solely as ledger vocabulary. **Consequence for R1:** the Inform/Reversible/Consequential lens is sound, but if R1's UX leans on `sprk_risk = Always Confirm` / `Confirm When Uncertain` to render "suggestion-that-launches" trust, **someone must wire the resolved Binding's risk into `RequiresConfirmation` at the dispatch/gate seam** (and, for `Confirm When Uncertain`, build the `dispatchUncertain` producer). This is a **real R1 work item currently absent from §3.4's "thin delta" and §9** — added to §15. Shipping UI that *displays* Always-Confirm semantics without wiring the gate would promise a guarantee the gate doesn't enforce.

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
| **Stated / interview** (My Assistant §8) — role, focus areas, **practice areas, office/location**, preferences | **The genuinely-new slice.** The **`sprk_userprofile` entity already exists** (owner-confirmed + Dataverse `describe` 2026-07-15) — but as a **bare skeleton**: only OOB system columns + `sprk_name`. **None** of the profile fields exist yet. | **NEW — but the build is the FIELDS, not the entity.** Store decision settled (use `sprk_userprofile`); R1 adds the typed columns + read/write. |

> **Store decision (settled 2026-07-15 — supersedes the §12.2 entity-vs-preference-type open call):** the entity question is answered — **use the existing `sprk_userprofile` entity**, not a new type on `sprk_userpreferences`. Two facts drive this: (a) `sprk_userprofile` already exists and the owner states `systemuser` carries a **lookup field `sprk_userprofile`** to it (so the identity wiring is partly in place); (b) it is empty, so R1's real work is **adding the typed columns** — role, focus areas, practice areas, office/location, and a small preferences set — plus the questionnaire that writes them and the reader that projects them.

**Fields to add to `sprk_userprofile` (owner will create manually, 2026-07-15):**

| Logical name | Display | Type | Notes |
|---|---|---|---|
| `sprk_primaryrole` | Primary Role | Choice (local option set) | Persona → default behavior/tone. Starter values: Attorney, Partner, Associate, Paralegal, Legal Operations, Practice Support, Administrator, Other |
| `sprk_practiceareas` | Practice Areas | Choices (multi-select) | Legal domains. Starter: Corporate/M&A, Litigation, IP, Employment/Labor, Real Estate, Tax, Regulatory/Compliance, Bankruptcy, Other |
| `sprk_focusareas` | Focus Areas | Multiline text (500) | Free-text personal focus ("M&A due diligence, NDA review"). May later merge with Practice Areas; kept distinct for the interview |
| `sprk_officelocation` | Office / Location | Single line text (100) | Text unless an office/BU entity exists (then a lookup is cleaner) |
| `sprk_assistantpreferences` | Assistant Preferences | Multiline text (JSON, 2000) | Small JSON for conversationally-editable prefs, e.g. `{"leadWith":"deadlines","suppress":["task-reminders"]}`. **Stated** prefs only — learned prefs live in `MemoryItem` |
| `sprk_profilecompletedon` | Profile Completed On | DateTime | Cold-start gate: if null → surface My Assistant |
| `sprk_profileversion` | Profile Version | Whole number | Questionnaire schema version, for migration |

> *Not a column:* a pre-rendered "AI summary" blob — the reader renders the NL fragment at read time from these structured fields (one source of truth, byte-stable per turn).
>
> **Relationship direction (technical call delegated to Claude → resolved 2026-07-15: Option B).** Add a **`sprk_systemuser` lookup on `sprk_userprofile` → `systemuser`, plus an alternate key** on it — the canonical 1:1 profile-extension pattern. Rationale: (a) **keyed idempotent upsert** for the questionnaire write (`PATCH sprk_userprofiles(sprk_systemuser=<guid>)` — no find-then-create race, which was a flagged risk); (b) **platform-enforced one-profile-per-user** (the alternate key; the systemuser-side lookup cannot guarantee this); (c) **no OOB-table dependency** (all custom schema stays on `sprk_userprofile`); (d) **direct keyed read** from `systemuserid` (which `CallerSystemUserResolver` already resolves). The existing `systemuser.sprk_userprofile` lookup (already created) may remain for MDA form convenience, but the BFF treats the **profile→user** side as authoritative. (Option A — the systemuser-side lookup alone — also works for R1 reads with a governed upsert, but B is the cleaner write path and the recommended long-term shape.)

**One reader — the User-fragment path (CORRECTED 2026-07-15; supersedes the earlier `IOrganizationalContextProvider` framing):** the stated profile is **User-scope** context, so it injects through the **same seam that already injects User-scope memory** — `ContextBinder`'s `userFragment` composition (today: `ResolveUserMemoryFragmentAsync` → `IMemoryItemStore.ToUserPromptFragmentAsync`). R1 adds a **User-scope stated-profile producer** whose rendered fragment is composed into `userFragment` alongside the memory recall, so the one agent turn reads *stated profile + learned memory* together. **R1 does NOT implement `IOrganizationalContextProvider`.** That interface, read as-built, is a *different scope* — an **inbound *Organizational*** seam (Work IQ: org chart / team / reporting line), with a deliberately **counts-only** result shape (no place to carry a profile) and a runtime provider **deferred by owner ruling 2026-07-08**. Injecting a user's practice-areas/preferences through a counts-only "organizational" result would be the wrong seam. Role / BU / team (org identity) may later ride that org seam; for R1 role is read from `sprk_userentityassociation` + BU/team via the membership services and folded into the same User fragment. **The injection is the point, and it lands on an existing, live seam — no deferred provider on the R1 critical path.**

> **Note on preferences (resolved 2026-07-15; blast radius corrected by Fable review):** a separate stated-preferences table also exists — the **live/deployed entity is the plural `sprk_userpreferences`** (PK `sprk_userpreferencesid`; `describe` of the *singular* `sprk_userpreference` returns "Could not find an entity"). The singular (nonexistent) logical name is called by **at least six client surfaces** — not just DailyBriefing's `preferencesService.ts`: `LegalWorkspace/src/services/DataverseService.ts`, `LegalWorkspace/src/hooks/useUserPreferences.ts`, `LegalWorkspace/src/hooks/useDailyDigestAutoPopup.ts`, `Spaarke.UI.Components/src/utils/themeStorage.ts`, `client/webresources/js/sprk_ThemeMenu.js`, plus DailyBriefing — while **server** code (`PlaybookRunContext.cs`) uses the plural correctly. So it is a **platform-wide client bug**, not one file. R1 does **not** depend on it (stated prefs go on the `sprk_userprofile` columns above). **Resolution (owner 2026-07-15):** **keep the entity plural** and **fix the ~6 client references** to `sprk_userpreferences`/`sprk_userpreferencesid` — do **NOT** recreate a singular entity (that would split the same data across two tables). Zero schema change; a pure client-code fix. **File via `project-defer-issue-tracking`** (out of R1 scope).

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
- **The User Model / AI-readable user profile** (§6) — the new typed stated-profile columns on `sprk_userprofile` + a **User-scope stated-profile producer composed into `ContextBinder.userFragment`** (NOT `IOrganizationalContextProvider` — that org-scope seam stays deferred, §6/§14.4); provenance flags; the feeds-the-decider-never-grants-capability invariant; and the ADR-039 not-a-second-decider constraint (§3.5 / §12).
- **A few additive grounding-predicate columns** on the Binding (e.g. `requires-no-attached-record`) + authoring content (`sprk_tooldescription` / `sprk_chiptransitions` rows).
- Event-keyed trigger is **already shipped** (§3.3, the three paths) — R1 wires the reactive Text/Click surfaces onto it.
- **Structured-creation hand-off (§10) — R1 CORE (moved in from "deferred" per R2 UAT, 2026-07-15).** The highest-value, most-broken flows. R1 delivers: draft-in-chat → **pre-seeded wizard commit** for **create-matter / create-to-do / create-event**; the **deterministic constrained-field resolver** (P1); the **wizard entry-payload envelope**; **capability modeling** to real entities (P4: distinct `create-todo`/`create-event`); the **action-truthfulness invariant** (P5, §4); grounding-optional (P6). These fix the UC-1…UC-3 create-flow failures.

**R1.5 — designed here, sequenced immediately after R1 (reactive-first ordering, owner decision 2026-07-15):**
- **Full proactive push capability (§14.1a / §12.5).** Server-initiated push (Azure SignalR + outbox + server-fireable Event path + Daily-Briefing producer + Assistant render). **Deliberately after R1**, for two reasons: (1) the reactive create-flow hand-off must *work* before it is worth surfacing proactively — a proactive suggestion that launches into a broken create flow amplifies the failure; (2) it keeps R1 bounded. Fully designed; ready to execute as R1.5.

**Designed here, deferred to later R:**
- **Broader proactive event producers** — additional triggers (filing deadlines, new arrivals) beyond the R1.5 Daily-Briefing producer. Each is a new producer + authoring on the R1.5 surface — not new pipeline (§14.1a).
- **General notification spine consumers** — job-completion / share / system-alert notification kinds on the same SignalR channel (§14.1b); R1.5 ships the `suggestion` kind, others adopt incrementally.
- **Follow-Through outside the Assistant** — Suggested Next Steps on records / workspace widgets / create-wizard completion.
- **Broad ambiguity coverage** — R1 authors a *narrow* set of ambiguities into `sprk_tooldescription` text (§5); full breadth is later. (There is no separate "lexicon" to build — ADR-039 forbids a lexicon-resolver.)

---

## 10. Structured Creation — Draft-in-Chat, Commit-in-Wizard (R1 CORE)

> **Evidence base:** [`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md) — live UAT of the redesign-r2 free-form-chat create flows (create-task, create-to-do, create-matter). These are **the highest-value, most-broken flows and the cleanest proof of the dispatcher thesis**, so this hand-off is **R1 core, not deferred** (moved from "later R" on 2026-07-15 per the UAT). The distilled UX invariants below are binding requirements; the full failure-by-failure analysis lives in the referenced doc.

**The lesson (one sentence):** free-form chat is excellent at *drafting free text and proposing values* and structurally bad at *resolving closed, system-owned value sets* and *committing constrained multi-field records* — so structured creation is **draft-in-chat, commit-in-a-pre-seeded-wizard, with the LLM never resolving a set the system already owns.**

**UX invariants (binding — distilled from the UAT):**

| # | Invariant | Enforced in |
|---|---|---|
| **P1** | **The LLM never resolves a closed, system-owned set** (option sets, lookups, entity type, valid assignees). Resolve **deterministically against Dataverse metadata**; on uncertainty show a picker defaulted to the best guess. Constrained fields are **excluded from LLM arg-filling.** | §3.2 grounding + the new resolver (below) |
| **P2** | **Draft-in-chat, commit-in-wizard.** Chat drafts/proposes; constrained multi-field creation commits in a pre-seeded wizard — never one-field-per-turn elicitation. | §7 destinations, §8, this §10 |
| **P3** | **The wizard entry-payload hand-off is load-bearing** (files + resolved/proposed values + source metadata → wizard). | this §10 |
| **P4** | **Capabilities modeled to the real entities.** One generic `create-task`→`sprk_event` cannot serve "To Do vs Event" — model **distinct `create-todo` (`sprk_todo`) and `create-event`**; "To Do vs Event" is an authored §5 ambiguity. | §3.1 / §5 authoring |
| **P5** | **No optimistic UI claims.** Every action assertion ("opened X", "created Y") is **ack-gated on a client acknowledgment referencing the emitted action, or fails honestly.** A dispatcher that lies about what it did is worse than none. | §4 (truthfulness invariant) |
| **P6** | **Grounding is optional, not a prerequisite input.** A simple task must not demand a document to be "grounded." | §3.2 (grounding removes-the-impossible; never require-input) |

**New primitive R1 must build — the deterministic constrained-field resolver (P1). Fully specified (resolved 2026-07-15 so it can't stall execution):**
- **What:** a deterministic (no-LLM) "match a proposal against a closed catalog" service — the field-value analogue of the design's capability-level grounding (§3.2). Fixes the UC-3 dead-end (the LLM guessed "Commercial Transactions"/"Litigation" against a closed set and looped).
- **Contract:** Input `(entityLogicalName, attributeLogicalName, proposedValue, optional context)` → Output `{ resolved?: optionValue|recordId, confidence: high|low|none, candidates[] }`.
- **Valid-set source:** Dataverse metadata — choice label/value for option-sets; a filtered candidate query for lookups; cached per field.
- **Match ladder:** exact case-insensitive → normalized (trim/punctuation/synonyms) → fuzzy above threshold → none. No model call.
- **Consumed by:** the **smart pre-seed** (assistant reads the letter → pre-resolves proposals → hands the wizard defaulted dropdowns) and the wizard renders `high → pre-select` / `low|none → picker defaulted to top candidate` (the `candidates[]` come from the same metadata read, so the picker fallback is free).
- **Placement + reuse:** a BFF service in `Services/Ai`, callable from the chat/dispatch path; **the genuinely-new piece is the matcher only** — the wizards already render option-set dropdowns from metadata (Field-Mapping / option-set config); verify those helpers before building new.

**The hand-off mechanism (as-built — retrofit, not re-architect):** operator drafts in chat → Assistant launches the correct **pre-seeded** `Create*Wizard`; the wizard is the structured surface (real dropdowns defaulted by the resolver, attach, assign, review) and **owns the gated write** → a real record, no dead-end. As-built footholds confirmed by the Fable client review: `sprk_chiptransitions.prefill_slots` ships; **`wizard_step` `set-field` pre-fill already works** and `CreateMatterWizardWidget` already applies it (5 of 7 wizards have embedded wrappers); the Field-Mapping Framework is wired into all seven `Create*Wizard` services; `WorkspaceWidgetLoadEvent.widgetData` (`unknown`) can carry the launch. **Two corrections from the review:** (1) Field-Mapping is **record-sourced** (`sourceEntity`+`sourceId`), not value-sourced — Assistant *seed values* ride `wizard_step` `set-field` or a new mount-payload field, not `applyFieldMappings`; (2) the `navigateTo` webresource-modal launch path carries only a query-string `data` param, so the **typed entry-payload envelope** is the genuinely-new piece (unify the 5-of-7 embedded path + close the 2 gaps). *Not related to compose-r2* (adjacent widget-model precedent, not a dependency).

---

## 11. Architecture Placement & Governance (stubs — complete during spec)

### 11.1 Hot-Path Declaration (per root CLAUDE.md §10 / bff-extensions **§H** — format corrected 2026-07-15)

```xml
<hot-path-declaration>
  <bff-api>YES — a User-scope stated-profile producer folded into `ContextBinder.userFragment` + `sprk_userprofile` read access in Services/Ai/Context; the `sprk_risk` gate-wiring work item (§4); the `requires-no-attached-record` grounding predicate (new column + `AgentToolFilterContext` field + `SprkChatAgentFactory` threading + `PreFilter` branch, §3.2); **a new Azure SignalR hub + `/negotiate` endpoint (§12.5), a server-fireable Event-path producer, and a pending-suggestion outbox `sprk_` table (§14.1a)** for proactive push. Reuses existing dispatch + User-fragment/memory seams (no new pipeline/ranker; does NOT implement the deferred org-scope `IOrganizationalContextProvider`). NEW Azure resource (Azure SignalR) ⇒ placement justification + publish-size check.</bff-api>
  <spaarke-ai>YES — Assistant pane tool drop-down (Quick Start modal + My Assistant questionnaire), Suggested Next Steps cards, and the same-session proactive-chip subscriber/render slot (§14.1)</spaarke-ai>
  <ci-workflows>NO</ci-workflows>
  <skill-directives>NO</skill-directives>
  <root-CLAUDE-md>NO</root-CLAUDE-md>
</hot-path-declaration>
```

> **Registry obligation (Fable review 2026-07-15):** this project is **not yet in [`projects/INDEX.md`](../INDEX.md)** — the §H/root-§17 active-project registry consumed by `/conflict-check`. It MUST be registered (13 of 17 active worktrees touch BFF; concurrency with `ai-architecture-redesign-r2` Phase E on the *same* `ContextBinder`/dispatch-spine files is a real merge-ordering dependency — see §13/§15).

### 11.2 Placement Justification (per root CLAUDE.md §10)

To be completed for each major new component. Anticipated components + their placement question:
- **Suggested Next Steps consumer** — there is **no new NBA orchestrator**; the runtime reuses the shipped **session-dispatch seam** (`SessionDispatchOrchestrator` / `IConsumerRoutingService`) and the `sprk_chiptransitions` emission. The only placement question is *where the SNS card-projection + User-Model read compose* (BFF `Services/Ai` vs client). No new dispatch path (compose-r2 invariant). (Per §10-bullet-3, use the `Services/Ai/PublicContracts/` facade if CRUD code needs it.)
- **User Model store** — resolved by the 2026-07-15 investigation (§6): learned signals → existing `MemoryItem` **User scope** (ADR-042 — do NOT duplicate); role/defaults → `sprk_userentityassociation` + BU/team; the **stated typed profile** goes on the **existing `sprk_userprofile` entity** (settled — no new entity), whose profile columns are **new and must be added** (the entity is an empty skeleton today). Reader = the User-fragment path (below), not the org seam.
- **User Model reader** — a **User-scope stated-profile producer** composed into `ContextBinder.userFragment` (BFF `Services/Ai/Context`), sibling to the `MemoryItem` User fragment already injected by `ResolveUserMemoryFragmentAsync`. Projects stated profile + membership/role into the one agent turn. **Not `IOrganizationalContextProvider`** (that is the deferred inbound org-scope / Work IQ seam, wrong scope + counts-only — §6). **Not a ranker** (ADR-039 — §3.5).
- **Publish-size impact** — measure per §10-bullet-4 (≤60 MB ceiling; current baseline ~49.63 MB incl. PDBs).

### 11.3 Component Justification (per root CLAUDE.md §11 — default to reuse)

Reuse-first candidates (verify with grep before claiming "new"):
- **Bindings / Actions catalog (ADR-039)** — grounding stage reuses it; do not build a parallel capability registry.
- **Session-dispatch seam** — dispatch reuses `POST /api/ai/chat/sessions/{id}/dispatch`; **no new BFF dispatch endpoint** (the compose-r2 AI-dispatch invariant applies here too).
- **PaneEventBus (`@spaarke/ai-widgets` — `Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`; NOT `@spaarke/events-components`, which is the Calendar/Events package)** — cross-pane dispatch (Assistant → Workspace/destination). Carrier = `WorkspaceWidgetLoadEvent` (`widgetData: unknown`) + `wizard_step` set-field; rich enough to *carry* a payload, but a **typed** entry-payload envelope is unbuilt (§10). compose-r2 extended this contract additively (ADR-030 discriminants) — no breaking change.
- **Field Mapping Framework** — wizard pre-fill seam (§10).
- **User memory (`MemoryItem` User scope, ADR-042)** — the User Model's learned-behavior store; already read at chat time by `ContextBinder`. Do NOT build a second memory store. Insights Engine's charter *extends* to usage-insights (§13); the `insights-engine` write-origin is reserved-but-unwired.
- **`ContextBinder.userFragment` composition** (`Services/Ai/Context`) — the live User-scope injection seam (`ResolveUserMemoryFragmentAsync` → `ToUserPromptFragmentAsync`). R1 composes the stated-profile fragment here alongside memory recall — this is the reader. Do NOT invent a new context path.
- **`IOrganizationalContextProvider`** (`Services/Ai/PublicContracts/`, ADR-032 null-object) — **NOT used by R1.** As-built it is an inbound *organizational*-scope seam (Work IQ; counts-only result; runtime provider deferred by owner ruling 2026-07-08) — wrong scope for the User-scope stated profile (§6). Listed here so spec/task authors do not mistakenly wire the stated profile through it.
- **`CallerSystemUserResolver`** — canonical AAD `oid` → `systemuserid`; reuse for any profile lookup.
- **Confirmation gate (`sprk_risk` + `side_effect_class`)** — tier + ambiguity gating; do not build a second gate.

Genuinely-new surface (needs the three-question justification in spec) — **much smaller than the original draft claimed**: successor edges (`sprk_chiptransitions`), risk (`sprk_risk`), and the intent surface (`sprk_tooldescription`) **already exist** — reuse, not new. The only genuinely-new surface is: the **profile columns on the existing `sprk_userprofile` entity** (the entity exists but is empty), the **User-scope stated-profile producer** feeding `ContextBinder.userFragment`, the **tool drop-down UI** (§8), and **a few additive grounding-predicate columns** on the Binding. There is **no** new entity, no `IOrganizationalContextProvider` implementation (deferred, wrong scope), no "mapping layer," "risk-tier attribute on Actions," or "curated lexicon" to build.

---

## 12. ADR-Level Decisions to Make (decide before task decomposition)

1. **ADR-039 tension — the User Model MUST NOT become a second decider (BINDING).** ADR-039 forbids any second intent mechanism (regex, keyword map, vector classifier, reranker, routing middleware); the ONE probabilistic decider is the single function-calling agent turn. The User Model may therefore only **(a)** bias that one agent turn (profile injected via the **User-fragment path in `ContextBinder`** — §6, NOT `IOrganizationalContextProvider`) and **(b)** reorder already-grounded `sprk_chiptransitions` for *display*. **Decision:** ratify as a project-scoped design constraint (path A, root §6.5); confirm no ranking/scoring stage is introduced. *This supersedes the original §3.3 "ranking stage" and the §3/§5 "vectors" language.*
2. **Stated-profile store shape — SETTLED 2026-07-15.** Use the **existing `sprk_userprofile` entity** (owner-confirmed it exists; Dataverse `describe` confirms it is an empty skeleton). No new entity; no `sprk_userpreferences` type. R1 **adds the typed columns** (role, focus areas, practice areas, office/location, small preferences set) and reads them via the User-fragment path (§6). Remaining sub-items for spec intake: (a) confirm the `systemuser`↔`sprk_userprofile` relationship direction; (b) enumerate the exact columns. **Binding rule (unchanged):** preference ≠ permission — a stated preference feeds the one decider / reorders grounded chips but **never grants a capability**; grounding still gates.
3. **Insights Engine charter extension** — from "insights on legal record data" to "insights on system usage / workflow" (the learned-behavior writer into `MemoryItem`, `source=insights-engine` — reserved but unwired). Amendment vs project-scoped note.
4. **Authoring *ownership* — clarified 2026-07-15 (owner asked what this means).** This is **not a technical decision** — the authoring *home* is already settled: the content is the `sprk_tooldescription` text (what each capability tells the LLM about itself) and the `sprk_chiptransitions` rows (the "after X, offer Y" successor edges), both **Dataverse rows a business admin edits with zero deploy.** The only open question is **which human role is accountable for writing and maintaining that content**, because it decides whether the dispatcher feels coherent or chaotic:
   - **Maker-per-Binding** (decentralized) — whoever builds a capability writes its own tool-description + chips. Fast, but nobody owns the whole picture: two capabilities can describe themselves in overlapping ways (the LLM then can't tell them apart), and the successor-chip graph grows ad hoc.
   - **Central curation role** (one designated person/team) — owns the entire dispatcher content set as a coherent whole: consistent tool-description voice, non-overlapping intent boundaries, and a deliberate successor-chip "workflow map." Like an editor for the Assistant's behavior.

   **"Owner" here = a *person/role*, not a system component** — the human accountable so this content doesn't become nobody's job (the classic failure where the catalog rots). **"Spec names the owner"** = the spec designates that person by role.
   **Recommendation:** the R1 catalog is small (a handful of Bindings + chips), so **maker-per-Binding + one named reviewer** (a single person who approves new tool-descriptions/chips for consistency) is sufficient and cheapest; a standing central-curation role only pays off as the catalog scales. **The R1 spec names one reviewer** (owner to designate — likely Ralph or a delegate) and flags "revisit central curation when the Binding catalog exceeds ~15–20 capabilities."
5. **Proactive push real-time channel (NEW — owner directive 2026-07-15, §14.1a layer 4). RESOLVED by researcher spike 2026-07-15 → Azure SignalR Service.** The one genuinely-new infra piece for full proactive push. **Recommendation: Azure SignalR Service, Default mode (an ASP.NET Core SignalR hub *in the BFF*, clients connect to the managed service — not to App Service), Standard tier.** Rationale (Microsoft-documented as the exact escape hatch for real-time on App Service):
   - **App Service fit:** in Default mode clients never hold a persistent connection to App Service, so ARR affinity, the non-configurable **230s idle timeout**, WebSocket-enable, per-instance connection ownership, and scale-out fan-out **all become non-issues** — the burdens that sink the raw-SSE option (§14.1a layer-4 alternatives).
   - **Auth / per-user targeting:** the SPA calls the hub `/negotiate` on the BFF with the **same bearer token it already sends**; `Context.User` in the hub is the same `ClaimsPrincipal` as REST; per-user delivery is first-class via `Clients.User(oid)` with `IUserIdProvider` keyed on the Entra `oid` (matches `CallerSystemUserResolver`'s identity key). BFF→service auth via managed identity (no connection strings). No OBO hop for the channel itself — grounding/data still ride normal OBO request paths.
   - **Durability is still mandatory (channel-independent):** Azure SignalR is **at-most-once with no offline replay** — so the transport is only a *"suggestions changed → fetch"* signal; **payload authority lives in the outbox** (§14.1a layer 3), never in the transport. This is what makes SignalR / SSE / polling interchangeable and multi-tab/offline/"generated-at-2am" correct by default. .NET stateful reconnect covers blips only.
   - **Cost/footprint:** one Azure resource; Standard S1 ≈ $49/mo/unit (1,000 concurrent conns / 1M msgs — ample for a hundreds-of-users legal-ops base; **note: 1 browser tab = 1 connection**). Client `@microsoft/signalr` ≈ 40 KB gzipped (trivial). `Microsoft.Azure.SignalR` server package is small → negligible publish-size delta.
   - **Rejected alternatives:** **Web PubSub** — Microsoft's own FAQ says it is *not* a SignalR replacement for a .NET/SignalR stack; strictly more hand-rolling (negotiate, fallback, reconnect, user-targeting, inbound CloudEvents webhooks) for zero benefit. **Persistent SSE stream** — acceptable *stepping stone* (same outbox contract, native `Last-Event-ID` resume) but a poor end-state on App Service (230s timeout keep-alives, Redis backplane needed at scale-out, request-slot-per-connection, `EventSource` can't send auth headers) — "at scale you rebuild a worse Azure SignalR." **Polling** — keep as the **degraded fallback** hitting the same pending-suggestions endpoint (notably, Dataverse's own in-app notifications are polling-delivered).
   - **Governance (BFF-hygiene §10/§11):** new Azure resource + BFF hub surface ⇒ **placement justification + publish-size check**; **CSP `connect-src` allow-list** of `wss://*.service.signalr.net` (+ negotiate `https://`) in target environments (SDK degrades to SSE/long-poll if WebSocket is blocked — a resilience win inside the iframed Power Apps host). **Open verification (before design freeze):** whether the dev/prod Dataverse environments have Power Platform (Strict) CSP enabled → §15.
   - **Empirical unknown to spike at implementation:** `withAutomaticReconnect` + on-reconnect refetch **inside the Power Apps iframe host** (transport fallback under the host's proxying).
   *This ADR-level item is genuinely new engineering, unlike §12.1–12.4.* Full source-cited comparison saved to researcher memory (`.claude/agent-memory/researcher/assistant-push-channel-2026-07-15.md`).

---

## 13. Relationship to Adjacent Projects

| Project | Relationship |
|---|---|
| **Insights Engine** (`ai-spaarke-insights-engine-*`) | The **learning half** of Follow-Through belongs here: mining usage telemetry to write `MemoryItem` User-scope signals (`source=insights-engine`, reserved-but-unwired) and to reveal *missing* `sprk_chiptransitions` edges. **Boundary:** the *runtime* path (project profile → agent turn → grounded chips) is a hot interactive concern and must NOT take a dependency on the analytics subsystem. Insights Engine observes and feeds the profile; the runtime consumes a cheap projected fragment. |
| **compose-r2** (`spaarkeai-compose-r2`) | Adjacent **widget-model precedent** (layout → widget migration) — **not a dependency** (re-confirmed post-pull 2026-07-15). It *reinforces* this design: it extended the shared PaneEventBus + `SessionDispatchOrchestrator` seams **additively** and **strengthened** the no-new-dispatch-endpoint invariant by retiring the parallel `/api/compose/action` route. Its `prefill_slots` + PaneEventBus additions are supporting footholds for §10, not blockers. |
| **ai-architecture-redesign-r1 / r2** | Charter/platform baseline for the Action + Binding + session-dispatch surface this project builds on. **⚠️ Merge-ordering dependency (Fable review 2026-07-15):** redesign-**r2 Phase E** is in flight and converges engines **in `ContextBinder` / the dispatch spine** — the *exact files R1 modifies* (the new User-fragment producer + the `PreFilter`/gate wiring). ADR-043 is **Proposed, not Accepted** (promotes at the R2 Phase-E gate). This is not merely a "baseline" — it is a real ordering/coordination dependency; register in `projects/INDEX.md` and sequence against Phase E. |

---

## 14. Decisions — Resolved (owner answers, 2026-07-15)

All six §14 questions were answered by the owner on 2026-07-15. Each is recorded below with the resolution and, where the owner asked a question back, the answer that now governs spec intake.

### 14.1 One proactive proof in R1 — YES (one), with a stated build-out path

**Owner:** "one proactive is fine — BUT how are we then going to build all the others?"

**Resolved:** R1 includes **exactly one** proactive proof — the **Daily-Briefing → "let's review them" handoff** (§7 reference chain: sensor detects → Assistant proposes flow → destination holds the work). It proves the reactive→proactive flip on real machinery.

**How the others get built (the answer to "all the others"):** proactive triggers are **not** bespoke pipelines — every one is the **Event path** (§3.3) firing the *same* dispatch seam. After R1, each additional proactive trigger is **authoring + at most a new event emitter**: add an `sprk_oneventbindings` row binding a new event (a filing deadline, a new arrival) to a capability, and the already-built surface renders it. So the cost curve is **authoring-dominant**, identical to the reactive story — "the machine is the same, only the trigger flips" (§1/§3). Later R's add event *sources*; none add pipeline.

> **⚠️ Critical as-built correction (Fable review 2026-07-15) — there is NO server-push or polling channel in the client.** A grep across `src/solutions/SpaarkeAi` for `SignalR|EventSource|WebSocket` returns **zero** matches; every SSE stream is the response to a *client-initiated* POST, and the Event path itself only fires when the **client** POSTs an event (e.g. document-uploaded). There is **no** mechanism to deliver a chip to the Assistant pane while the user is idle. The dormant `conversation`-channel `'suggestion'` discriminant is **text-only** (no `bindingId`/chips payload) and has no dispatcher or subscriber. The current chip lifecycle (`ConsumerChips` / `useConsumerChips`) re-arms chips **only** from a dispatch SSE stream and clears on click/session-change — it does not accommodate injected chips. Also, `Spaarke.DailyBriefing.Components` has **no** PaneEventBus import and **no** "assistant" reference — the sensor and actuator have never spoken; both emitter and receiver/renderer are new.
>
> **Owner decision (2026-07-15):** build the **full, real capability** — genuine server-initiated push (Assistant surfaces grounded work while the user is idle), **not** the same-session quick-win. Target architecture below (§14.1a).

### 14.1a Proactive Push — the full, real target architecture (owner directive 2026-07-15)

The good news from the code investigation: most of the "real capability" already has shipped substrate, so "full and real" is **not** the same as "all new infrastructure." Five layers, four of which reuse:

| Layer | What it does | As-built status |
|---|---|---|
| **1. Event producers (server-side)** | Detect the proactive trigger: filing-deadline monitors, document-arrival (SPE/Graph change) webhooks, Daily-Briefing computed state | **Pattern shipped** — background workers/hosted services already exist (`SpeWebhookRenewalHostedService`, `StaleCheckoutSweeperHostedService`, Service Bus job processing, `PlaybookSchedulerJob`). R1 adds **one** real producer (the Daily-Briefing signal); others are later-R additions on this pattern. |
| **2. Grounding + dispatch (server-fireable)** | Turn the event into a **grounded** capability/chip via `sprk_oneventbindings` → the Event-path dispatch — so the suggestion is real, not a guess | **Mostly shipped** — the Event path + `CreateNotificationNodeExecutor` (a playbook can already emit a notification as a destination) exist; the delta is making the Event path **server-fireable from a background producer**, not only client-POST-triggered. |
| **3. Durable pending-suggestion outbox** | Persist the grounded suggestion per-user so one generated while the Assistant is closed is delivered on next open (the "Assistant closed" failure mode). **Mandatory regardless of channel** — every push option here is at-most-once with no offline replay (researcher-confirmed), so payload authority MUST live here, not in the transport. | **Substrate shipped; a thin new table recommended.** The pattern + a usable substrate exist — `NotificationService.CreateNotificationAsync` already writes native **`appnotification`** (per-user `ownerid`, `ttlinseconds`, `data` JSON with `actionUrl`+`aiMetadata`) and **a code page already reads it via Web API** (`useBriefingNotifications`). BUT `appnotification` renders in the **MDA notification center** and isn't a purpose-built grounded-suggestion store. **Recommended authoritative outbox = a small dedicated `sprk_` table** (per-user pending suggestion: grounded `bindingId` + chip/`prefill_slots` payload, `delivered`/`dismissed` state, expiry), with **`appnotification` as an optional complementary mirror** for out-of-workspace visibility. So layer 3 is *de-risked, not free* — a thin additive table, not new architecture. |
| **4. Real-time delivery channel (server→client push)** | Deliver the suggestion to the live client **now**, not on next reload — the one genuinely-new piece | **NEW — the only real infra gap. RESOLVED → Azure SignalR Service** (Default mode, hub-in-BFF, Standard tier; §12.5). No persistent channel exists today (grep: zero `SignalR`/`EventSource`/`WebSocket` in the SPA; all SSE request-scoped). SignalR is Microsoft's documented escape hatch for real-time on App Service; per-user delivery via `Clients.User(oid)`; the transport carries only a *"suggestions changed → fetch layer 3"* signal. Because layer 3 gives next-load delivery for free, this channel is a **latency upgrade (now vs next-load), not a correctness dependency** — it degrades to polling gracefully. |
| **5. Client render (Assistant pane)** | Subscribe + render the grounded chip/card outside the dispatch-driven chip lifecycle; carry `bindingId`, not just text | **NEW (small)** — needs the unsolicited-chip **payload contract** (the dormant conversation-channel `'suggestion'` discriminant is text-only today), an Assistant-pane subscriber, and a render slot decoupled from the dispatch-SSE chip lifecycle. |

**Why this is the *real* capability and not a hack:** the durable outbox (`appnotification`) is the source of truth; the real-time channel is a best-effort accelerator over it. A suggestion is **always** persisted + grounded + gated (ADR-039/041) and **always** deliverable (next-load fallback), whether or not the push channel delivered it live. Every subsequent proactive trigger is then a new **producer** (layer 1) + authoring (`sprk_oneventbindings` row) — layers 2–5 are built once. That restores the "authoring-dominant build-out" story (§14.1) on genuinely-real infrastructure.

**ADR-040 (session ledger):** a pushed suggestion is **store-before-render** — persisted (as the `appnotification` and/or a ledger entry) before it renders; its impressions/dismissals/clicks are `WidgetEvent` ledger entries. **ADR-041 (`ConfirmationPolicyEngine`):** an unsolicited-origin consequential chip is **origin = proactive ≠ user-initiated** — the gate must treat it accordingly (never silent-act; §4).

**Scope (RESOLVED 2026-07-15 → R1.5):** this capability is **R1.5**, sequenced immediately after the reactive create-flow core (R1). It comprises: the real-time channel (layer 4, Azure SignalR §12.5) + client render (layer 5) + grounded-chip contract + server-fireable Event path (layer 2 delta) + the outbox table (layer 3) + **one** producer (layer 1: Daily-Briefing). Reactive-first because a proactive suggestion must not launch into a create flow that doesn't yet work (R2 UAT), and to keep R1 bounded. The channel decision (§12.5) is resolved.

**Producer topology (who can push — clarifies a common mis-model).** SignalR is a **delivery leg to live browsers only** — it never writes Dataverse and never runs jobs. A producer does its work + writes Dataverse + writes the outbox through normal server code, then *optionally* pings the browser via SignalR. Reach of the ping depends on where the producer runs: **(a) in-BFF** background/hosted/Service-Bus jobs push directly via `IHubContext` (Default mode); **(b) external** producers (a separate Function, a **Dataverse plugin**, Power Automate) either call a BFF endpoint, use the Azure SignalR **REST API**, or **just write the outbox and skip the live push** — the user then gets it on next load / via polling. Because the outbox is the durable source of truth, a producer that cannot reach SignalR is still correct — the live push is acceleration, never a dependency.

### 14.1b The push channel is a general notification spine (owner-ratified 2026-07-15)

The SignalR channel + per-user outbox + client subscriber is **not suggestion-specific** — it is a general **server→client notification spine**. Proactive NBA suggestions are simply its **first (R1.5) consumer.** Design it general from day one to avoid a future second push mechanism (root §11 reuse):

- **Typed envelope with a `kind` discriminator** (`suggestion` | `job-complete` | `share` | `system-alert` | …) on both the outbox row and the SignalR message — not a suggestion-only shape.
- **One client subscriber routes by `kind`:** `suggestion` → Assistant-pane grounded chip; other kinds → a toast / notification tray renderer.
- **R1.5 wires only `suggestion`.** Job-completion (async analysis/upload/worker done), share, and system-alert kinds adopt the spine **incrementally** in later R's — a config/renderer change, not new infrastructure.
- **Complements, does not replace, Dataverse OOB `appnotification`** (which renders in the MDA shell notification center, poll-delivered): keep `appnotification` as an optional **mirror** for out-of-workspace visibility; the spine is the primary **in-workspace, real-time, interactive** channel.
- **Governed by the User Model.** Notification fatigue is the real risk of any push system; opt-in / category / quiet-hours preferences live in the same `sprk_assistantpreferences` / `sprk_userprofile` (§6) — the two workstreams reinforce each other. Actionable kinds (suggestions) run the risk gate (§4); pure-FYI kinds skip it but still get ADR-040 store-before-render.

*Cost note:* generalizing the envelope is a **framing/typing choice, not added R1.5 scope** — the same channel is built either way; the discriminator just prevents rebuilding it later.

### 14.2 Authoring ownership — clarified (it's a *who*, not a *what*)

**Owner:** "I don't understand this question — what is 'makers' versus 'central curation' role; 'spec should name owner' — who/what is an owner in this context?"

**Answer (now folded into §12.4):** The *content* in question is the `sprk_tooldescription` text + `sprk_chiptransitions` rows — Dataverse rows a business admin edits, zero deploy. The **home** for it is settled; the only open point is **which human role is accountable for writing and maintaining it.**
- **Maker-per-Binding** = decentralized: whoever builds a capability writes its own description + chips. Fast, but no one owns the whole picture (overlapping descriptions confuse the LLM; the chip graph grows ad hoc).
- **Central curation role** = one person/team owns the whole dispatcher content set for coherence (consistent voice, non-overlapping intents, a deliberate successor "workflow map") — an editor for the Assistant's behavior.

**"Owner" = a person/role, not a system component.** "Spec names the owner" = the spec says whose job this is, so the catalog doesn't rot as nobody's responsibility. **Recommendation (see §12.4):** for R1's small catalog, **maker-per-Binding + one named reviewer** is enough; name that one reviewer in the spec (owner to designate); revisit a standing central-curation role when the catalog exceeds ~15–20 capabilities.

### 14.3 Ambiguity coverage for R1 — narrow set now, authoring-driven backlog

**Owner:** "we can do a narrow high frequency list but like 14.1 what is the plan for building the others?"

**Resolved:** R1 authors a **narrow high-frequency set** (file, open, close, matter) as disambiguation guidance inside the competing capabilities' `sprk_tooldescription` text; the elicitation gate handles the rest when the model is genuinely uncertain.

**How the others get built (parallel to 14.1):** it is **authoring, never engineering** — ADR-039 forbids a lexicon/resolver, so there is nothing to *build*. Each new ambiguity is one more sentence of guidance in a tool-description. The **backlog is prioritized by telemetry, not guesswork**: **Insights Engine** (§13) observes where the elicitation gate actually fires and where users correct a misfire, and ranks the highest-frequency real ambiguities to author next. So the plan is: R1 seeds the obvious few by hand → production usage surfaces the true top-N → each is a small authoring edit. It stays content forever; the pipeline never changes.

### 14.4 Stated-profile store — SETTLED: use existing `sprk_userprofile`, add the fields

**Owner:** "yes `sprk_userprofile` entity already created; the field on the entity 'User' for lookup to user profile is `sprk_userprofile`."

**Resolved + verified:** confirmed the entity exists via Dataverse `describe` — **but it is an empty skeleton** (only OOB system columns + `sprk_name`; none of role / focus / practice-area / office / preferences). So:
- **Store: settled** — use `sprk_userprofile`; drop the `sprk_userpreferences`-type alternative.
- **Real R1 build: the columns + wiring**, not "a new entity." Add the typed profile columns; the My Assistant questionnaire (§8) writes them; a **User-scope stated-profile producer** reads them into `ContextBinder.userFragment` (§6).
- **Relationship direction to verify at spec intake:** the owner reports a **lookup on `systemuser` → `sprk_userprofile`** — the inverse of the usual "profile keyed by `systemuserid`." Spaarke normally does not extend the OOB user table, so confirm whether this is a lookup on `systemuser` or a `systemuserid` key on the profile row, and reconcile with `CallerSystemUserResolver` (AAD `oid` → `systemuserid`) for the read path.
- **Reader-seam correction (see §6):** inject via the **User fragment** path (sibling to the `MemoryItem` User fragment), **not** `IOrganizationalContextProvider` (deferred, org-scope, counts-only — wrong seam).

### 14.5 BFF hot-path + publish size — OK

**Owner:** "ok." Confirmed: the User-scope stated-profile producer + `sprk_userprofile` read access land in BFF `Services/Ai/Context` (hot-path `<bff>Y`, §11.1). Run the **≤60 MB** publish-size check per root §10-bullet-4 during execution (baseline ~49.63 MB incl. PDBs). *Correction vs the pre-answer §11.1: R1 does **not** implement `IOrganizationalContextProvider` — the BFF work is the User-fragment producer, which is lighter.*

### 14.6 Suggested Next Steps after a non-dispatching answer — accept (a)

**Owner:** "yes" (= recommendation (a)).

**Resolved:** SNS chips appear **only after a capability runs** (Click / Event / Text-dispatch). For bare conversational Q&A, we **do not** build new machinery to synthesize successors (option (b), rejected — it would re-introduce an ungrounded suggestion path). Instead we lean on **Layer-1 authoring**: write `sprk_tooldescription` so common questions ("what are my tasks?") **dispatch** a `list-*` capability, whose `sprk_chiptransitions` then fire. Everything stays grounded; the fix is authoring, not code. *Consequence for the §1.5 promise:* "Suggested Next Steps after an Assistant answer" holds for answers that dispatch a capability; a purely chatty reply legitimately shows none. Spec should phrase the promise that way.

---

## 15. Spec-Intake Obligations & Newly-Surfaced Gaps (Fable four-agent review, 2026-07-15)

A full design-vs-code verification (four parallel Fable agents: dispatch spine, User-Model seam, client/proactive, cross-cutting governance) **confirmed all three 2026-07-15 corrections against code + live Dataverse** and confirmed the reactive spine is real and often *more* shipped than claimed (the SNS chip strip `ConsumerChips` already renders with `aria-label="Suggested next steps"`; `wizard_step` `set-field` pre-fill already works; the tool-drop-down + wizard-library-card patterns already exist as siblings). It also surfaced items the "thin delta" framing under-weighted. These do **not** need resolving in this draft, but `/design-to-spec` MUST carry them into `spec.md`.

### 15.1 Scope-affecting decisions — RESOLVED (owner, 2026-07-15)

1. **Proactive proof → build the FULL real capability, not the quick win (owner directive).** Owner: *"what is the right long-term solution — not the 'quick win' solution — we need the full, real capability."* So R1 does **not** take the same-session-only shortcut. The proactive surface is architected as a genuine **server-initiated push** capability (the Assistant can surface grounded work while the user is idle / not currently interacting). Target architecture + the real R1 slice are designed in **§14.1a** (new). This is a **material scope expansion** vs the original "defer all proactive" boundary — effort + the push-channel choice are flagged there.
2. **`sprk_risk` gate-wiring → WIRE LIVE in R1 (owner decision).** R1 adds the task to pass the resolved Binding's `sprk_risk` into `PendingPlanManager.RequiresConfirmation` and to build the `dispatchUncertain` routing-confidence producer for the `Confirm When Uncertain` tier, so the Inform/Reversible/Consequential trust promise is actually enforced (§4). This is dispatch-spine work (ADR-038 `tests/integration/seam/**` DoD; hot-path `<bff-api>YES`).
3. **User-slice token budget → AMEND the 300 ceiling (owner decision).** R1 formally raises `EnvelopeBudget.User` to accommodate the profile fragment (§6) and re-baselines the golden-utterance gate. Spec sizes the new ceiling from the actual rendered profile-fragment length and records the re-ratification (a change to a ratified constant → note in spec's ADR-tensions + code-review sign-off).

### 15.2 As-built engineering realities the spec must design (not just "authoring")

4. **No chip-reorder / "top-N" seam exists.** `BuildTransitionChips` emits **all** transitions in authored JSON order; there is no priority field, no cap, no ordering hook, and chip emission happens inside the dispatch stream with no User-Model input. §3.1's "top N chips" and §3.5's "reorder for display" must be pinned to a **client-side, deterministic, preference-keyed** reorder over the already-emitted closed chip set — **no model call, no score** (else the ADR-039 "not a second decider" line is crossed in practice).
5. **SNS "cards" ≠ the shipped chip strip.** `ConsumerChips` is a transcript-footer pill strip tightly coupled to the dispatch stream (re-armed only from dispatch SSE, cleared on click/session change). "Ranked actionable **cards** + a 'more'/NBA-library modal" is new UI touching those lifecycle invariants. Reuse the existing `OutcomeCard` v1 contract (ADR-041/043 store-before-render) or justify a parallel card shape (root §11).
6. **Field-Mapping is the wrong seam for Assistant seed values (§10).** `applyFieldMappings` reads from an existing *source record* (`sourceEntity`+`sourceId`); it has no seed-values parameter. Assistant-supplied seeds ride `wizard_step` `set-field` (shipped) or a new mount-payload field. Also the widget wrappers cover only **5 of 7** wizards, and the `navigateTo` webresource-modal launch path (used by the shared launcher) can carry only a query-string `data` param — not a rich envelope. §10's "wire two existing seams" hides a three-door unification.
7. **Grounding-guard plumbing (§3.2)** — `requires-no-attached-record` = new column + new `AgentToolFilterContext` field + thread `ChatHostContext` from `SprkChatAgentFactory` + new `PreFilter` branch.

### 15.3 Whole-topic gaps a spec needs (currently absent from the design)

8. **Eval-case obligation (merge gate).** R1 is authoring-heavy (`sprk_tooldescription`, ambiguity text, chip rows, new columns) — exactly what regresses dispatch. NFR-06 requires eval cases per catalog change, **plus negative cases proving profile injection does NOT flip dispatch decisions** (the operational proof of §12.1). This is the one mechanical safety net for the whole R1 value prop and is currently unmentioned.
9. **Testing strategy (ADR-038 KEEP categories + `tests/integration/seam/**` DoD for dispatch-spine changes).** Unit + seam coverage for the profile producer; direct `PreFilter`-branch tests; questionnaire write-path tests; a negative test asserting `AgentToolFilterContext` carries no profile/memory-derived members (mechanically guards the preference≠permission invariant). TEST-MODIFYING rigor override applies.
10. **Byte-stability / eval re-baseline.** The User fragment is in the byte-pinned stable-prefix / prompt-cache prefix (NFR-04). The profile fragment must render deterministically (ordinal-ordered multi-selects, canonicalized prefs JSON — no map-order nondeterminism); adding it requires a renderer-test + golden-utterance re-baseline.
11. **NFRs / latency / caching.** The profile adds a **second per-turn Dataverse read** (`systemuser` expand `sprk_userprofile`) on the hot chat-bind path. Spec needs a latency budget + caching decision (the `IdentityNormalizationService` Redis 10-min TTL is the in-repo precedent to cite or reject) + **soft-fail-to-null** posture matching every sibling resolver (NFR-07) + ADR-032 null-object classification for the producer.
12. **Security / authorization / privacy of profile data.** Dataverse security roles for `sprk_userprofile` read/write; OBO vs app-only for the BFF producer (app-only bypasses row security); GDPR/erasure tier of the stated profile; and **prompt-injection stance** on user-authored `sprk_focusareas` / `sprk_assistantpreferences` text injected verbatim into the stable prefix (bounded by the closed tool projection + catalog-data gate, but a new write surface).
13. **Write paths (three, all unspecified).** (a) My Assistant questionnaire → `Xrm.WebApi` vs a new BFF endpoint (apply `DATA-ACCESS-DECISION-CRITERIA`); (b) §1.5-5 **conversational preference editing** is a chat-initiated Dataverse write → per ADR-039's closed catalog it needs a **cataloged capability with declared `side_effect_class` + gate** (cf. `memory.write`), not ad-hoc; (c) §8 "seeds user memory" → the wizard→`MemoryItem` (`source=user`) write path (`IMemoryItemStore.UpsertAsync` needs `tenantId`).
14. **Learned-vs-stated conflict surfacing (§6) has no shipped mechanism.** Detecting that a User-scope `MemoryItem` contradicts a `sprk_assistantpreferences` value is new surface needing §11 justification; spec must name the precedence rule (recommend **stated > learned unless the user confirms otherwise**).
15. **Learning-loop telemetry.** §6's "you keep dismissing these" and §14.3's Insights-Engine backlog both presuppose captured signals (chip impressions/dismissals, gate-fire events, misfire corrections). No capture mechanism is named — without it the later-R learning story has no data source.
16. **ADR engagement.** ADR-040 (which *session* receives an unsolicited chip; ledger-write-before-render + entry type; SNS impressions/clicks as `WidgetEvent`), ADR-041 (`ConfirmationPolicyEngine` — tier×risk×**origin**; an unsolicited-origin consequential chip ≠ user-initiated origin), ADR-043 (named engine owner + intake path for dispatch-spine changes) are all cited-or-relevant but un-engaged.
17. **Governance mechanics.** Register in `projects/INDEX.md` (§11.1); complete §11.2 Placement Justification to the binding bff-extensions **Project-Level Imperative** bar (size-impact estimate + per-component decision-criteria answers + boundary statement + constraint-file citation — the rule binds *design.md*, not spec.md); §12.3 Insights-Engine boundary should be stated as an enforceable rule (`Services/Ai` MUST NOT reference `Services/Insights` — same process, code-dependency boundary, not deployable).
18. **Failure modes + schema deploy.** Enumerate: profile-store outage, partial profile, malformed `sprk_assistantpreferences` JSON, **dangling `target_binding_id`** in authored chips (targets a disabled/deleted Binding), Daily-Briefing event with the Assistant closed. Plus a solution-management / dev→prod promotion path for the manually-created columns (owner creates in dev; spec needs a verification step + recorded column contract so POMLs don't assume columns that didn't promote).
19. **File the `sprk_userpreferences` singular/plural client bug** (6+ surfaces, §6) via `project-defer-issue-tracking`.

### 15.4 Proactive-push build items — **R1.5** (owner directive 2026-07-15, §14.1a + §12.5)

20. **Azure SignalR provisioning + hub (layer 4).** New Azure resource (Standard tier, managed-identity service auth) + an ASP.NET Core SignalR hub in the BFF (`/negotiate` bearer-authenticated; `IUserIdProvider` keyed on Entra `oid`; `Clients.User(oid)`). ⇒ **BFF-hygiene §10 placement justification + publish-size check** (`Microsoft.Azure.SignalR` is small — expect negligible delta) + azure-deployment/Key-Vault wiring.
21. **Pending-suggestion outbox table (layer 3).** New thin `sprk_` table (per-user; grounded `bindingId` + chip/`prefill_slots` payload; `delivered`/`dismissed`; expiry). Additive schema; **component justification** (extends the notification pattern, does not duplicate `MemoryItem`/catalog). Decide whether to mirror high-value rows into `appnotification` for out-of-workspace visibility.
22. **Server-fireable Event path (layer 2 delta).** Make the `sprk_oneventbindings` Event-path dispatch invokable from a background producer (not only client-POST), so a proactive trigger produces a *grounded, gated* suggestion into the outbox. Dispatch-spine change ⇒ ADR-038 `tests/integration/seam/**` DoD.
23. **One event producer (layer 1) + client render (layer 5).** The Daily-Briefing producer (writes grounded suggestions to the outbox); the unsolicited-chip **payload contract** (upgrade the dormant conversation-channel `'suggestion'` discriminant to carry `bindingId`/chip, not just text); an Assistant-pane subscriber + render slot **decoupled from the dispatch-SSE chip lifecycle**; and the **polling fallback** on the same pending-suggestions endpoint. ADR-040 store-before-render + `WidgetEvent` ledgering; ADR-041 **proactive-origin** gate treatment (§4/§14.1a).
24. **Open environment verification (before design freeze).** Whether the dev/prod Dataverse environments have Power Platform (Strict) **CSP** enabled — determines whether `connect-src` allow-listing of `wss://*.service.signalr.net` (+ negotiate `https://`) is needed. Plus an implementation-time spike: `withAutomaticReconnect` + on-reconnect refetch **inside the Power Apps iframe host** (transport fallback under the host proxy is the one empirical unknown).

### 15.5 R1-core structured-creation build items — from R2 UAT ([`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md), §10/§4)

25. **Deterministic constrained-field resolver (P1) — new primitive.** *"Match an LLM proposal against a closed set (option set / lookup) → `{pre-select | picker defaulted to best guess}`."* Resolve against existing Dataverse metadata / Field-Mapping option-set config (reuse-first — verify before building). Constrained fields **excluded from LLM arg-filling**. Fixes UC-3. Include the negative-case eval: a nonsensical proposal (practice-area × matter-type mismatch) cannot commit.
26. **Wizard entry-payload envelope + hand-off.** Typed launch envelope (files + resolved/proposed values + source metadata) → pre-seeded `Create*Wizard`; ride `wizard_step` `set-field` (not `applyFieldMappings`, which is record-sourced); unify the 5-of-7 embedded wrappers + close the 2 gaps; the wizard owns the gated write.
27. **Capability modeling (P4).** Distinct `create-todo` (`sprk_todo`) and `create-event` (`sprk_event`) capabilities (today one generic `create-task`→`sprk_event`); author "To Do vs Event" as a §5 ambiguity; assign-to-me + association picker honored in-wizard (FR-B-06); grounding optional (P6).
28. **Action-truthfulness invariant (P5).** Adopt the D-F3 ack contract as a Follow-Through invariant: every action claim is ack-gated on a client acknowledgment or fails honestly (§4). No-regress guard: an action must not collaterally tear down unrelated panes/tabs (UC-4).

---

> **Next step:** **owner review of this updated draft.** All scope calls are resolved: **R1 is reactive-first** — the create-flow structured-creation core (§10, §15.5) + User Model + tool drop-down + `sprk_risk` wiring + grounding-guard — and the **full proactive-push capability is R1.5** (§14.1a/§15.4, channel resolved to Azure SignalR, architected as a general notification spine §14.1b). Spec-intake carry-forward is consolidated in §15.2–15.5. The R2 UAT ([`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md)) is the R1-core evidence base. Then: `/design-to-spec` → `/project-pipeline` → `/task-create`. This file still needs git tracking activated in the main worktree (`git add projects/spaarkeai-assistant-enhancements-r1/design.md`).
