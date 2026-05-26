# Pattern Spec: Queue / Inbox-centered

> **Status**: Draft v0.1 — first end-to-end pattern spec; demonstration of the template in use
> **Date**: 2026-05-18
> **Pattern slug**: queue-inbox
> **Closest analogous product:** Outlook (with reference to Linear Triage and Superhuman where they diverge usefully)
> **Primary user intent:** *"What needs my attention next?"*
> **Companion documents:** [bibliography.md](bibliography.md), [spaarke-ux-strategy-overview.md](spaarke-ux-strategy-overview.md), [pattern-spec-template.md](pattern-spec-template.md)

---

## 1. Optimizes For

The Queue / Inbox pattern optimizes for **moving through prioritized work one item at a time, making a fast accept / route / defer decision per item, and getting back to a clean state**. The optimization target is *decision velocity* across many items, not deep engagement with any one item.

Three specific properties this pattern is good at:

- **Sustaining attention across a flat list** rather than a hierarchy. Users move through items in order [Cited: nng-email-research-base]. A queue with depth is a cognitive load problem; a flat list with sort and filter is a velocity tool.
- **Producing a defined next action per item.** Modern queue products converge on the principle that every item must exit the queue with a decision attached — replied, archived, snoozed, delegated, routed — rather than silently aging in place [Cited: superhuman-triage-philosophy-2025, linear-triage-docs-2026].
- **Letting users *flow* rather than *manage*.** When users can move through items at the speed of thought (Superhuman's 100ms rule), the queue becomes the productivity tool; when they have to think about how to operate the tool, the queue becomes the obstacle [Cited: superhuman-100ms-philosophy-2025].

The pattern is *not* optimized for deep inspection of any single item. Items that warrant deep work exit the queue into a different pattern.

---

## 2. When To Use / When Not To Use

### 2.1 Use this pattern when

- **The user faces a high volume of incoming items that require triage but not all require deep work.** [Cited: bloomberg-law-contract-management-2025 — in-house counsel manage high contract volumes; 43% of respondents say contract-related tasks are at least half of daily work, much of which is triage-and-route, not deep review.]
- **Items have a defined set of dispositions** (accept / reject / route to someone / defer / archive). [Cited: linear-triage-docs-2026 — triage as "review issues before accepting them and moving them to the backlog or cycles" with a finite outcome set.]
- **The user benefits from sorting / filtering / ranking** to handle volume that would be unmanageable in arrival order. [Cited: nng-newsletter-inbox-congestion-2019 — inbox volume has grown 300% over four years, making sort/filter mandatory, not optional.]
- **Disposition decisions are usually fast** (seconds to a minute), with occasional escalation. [Cited: superhuman-flow-model-2025 — "every message gets your attention for a second or two, and then it's gone."]
- **The user benefits from session-end closure** ("inbox zero" or "triage queue cleared"). [Cited: superhuman-triage-philosophy-2025 — though Inbox Zero is reframed as "every email has a defined next action," not literal zero.]

### 2.2 Do not use this pattern when

This is the most important subsection in the spec, per the Overview's mandate that "when not to use" must actually restrict.

- **The user is doing deep work on a single item.** [Judgment: that is the Canvas pattern's optimization target. A user reviewing a complex 40-page MSA is not in a queue; they are on a canvas. Putting Canvas mechanics inside a queue row degrades both patterns.]
- **The work is staged with explicit transitions and state machines** (draft → review → approved → executed). [Judgment: that is the Workflow pattern. A queue is *flat*; Workflow is *staged*. The R2 design's separation of these patterns is correct and must be preserved.]
- **The user needs to see how items relate to each other** (dependencies, hierarchies, parent-child structure). [Cited: linear-conceptual-model-2026 — Linear separates issues from projects-and-cycles precisely because hierarchical relationships are a different visualization problem.]
- **The decision per item requires substantial context** that doesn't fit in a row. [Judgment: if a user must open every item to make any decision, the queue is doing no work. Queue rows must carry enough triage signal to enable many no-open decisions.]
- **There are very few items** (typically <5–10 active at a time). [Judgment: small item counts don't benefit from queue mechanics; they fit naturally in a Summary widget or a simple list within an Entity view.]

### 2.3 Pattern overlaps

| Confused with | Distinguishing feature |
|---|---|
| **Workflow / Process-centered** | Workflow has *staged transitions* (e.g., "this contract is in the negotiation stage; next state is signature"). Queue has *flat triage* (e.g., "this contract needs disposition; the disposition is one of these options"). A queue *feeds* workflows, but they are different patterns. |
| **Summary / Intelligence-centered** | Summary surfaces *aggregate state* ("12 contracts pending, 3 overdue"). Queue surfaces *individual items requiring action*. A summary may link into a queue but they are different views. |
| **Canvas / Document-centered** | Canvas is the *destination* for an item that exits the queue into deep work. The same user task can begin in Queue ("which NDA is next") and end in Canvas ("now I'm reviewing this one"). |

---

## 3. Mechanics

### 3.1 Core mechanics

The behaviors without which the pattern is not the pattern.

1. **Flat, sortable, filterable list of items.** Items appear in a single visual stream, ordered by some default (typically priority or recency), with sort and filter controls that don't displace the list. [Cited: nng-email-research-base, linear-conceptual-model-2026]

2. **Per-item triage signal that supports no-open decisions.** Each row must carry enough information for the user to disposition many items without opening them: sender / source, subject / summary, age / due date, priority signal, status. [Cited: nng-newsletter-inbox-congestion-2019 — "clear from and subject lines" are mandatory at scale; users decide from row content.]

3. **A defined disposition set per item.** Every item exits the queue with one of: accept (route into Workflow), reject / archive (close), defer / snooze (return later), delegate (route to another user), escalate (route up). The set may be customized per queue but the principle of "every item gets a disposition" holds. [Cited: linear-triage-docs-2026, superhuman-triage-philosophy-2025]

4. **Single-item focus mode for deeper inspection.** When a row's content isn't enough, the user opens the item to see more — but the queue is still the operating context. The focus view is *inside* the queue, not a separation from it. [Cited: superhuman-flow-model-2025 — "move through emails one by one, making a decision for each."]

5. **Disposition shortcuts that match user pace.** Keyboard shortcuts, swipe gestures, or one-click actions for the common dispositions. Speed matters because the queue is fundamentally a velocity pattern. [Cited: superhuman-shortcuts-2025, superhuman-100ms-philosophy-2025 — every action under 100ms; mouse navigation interrupts flow.]

6. **A clear empty state.** When the queue is cleared, the user gets unambiguous feedback. This isn't decoration — it is what makes the pattern a closed-loop activity rather than an open-ended one. [Cited: superhuman-inbox-zero-2025 — closure is psychologically load-bearing.]

### 3.2 Supporting mechanics

Behaviors that strengthen the pattern but aren't strictly required.

1. **Split or grouped views.** Users can carve a high-volume queue into separate streams (Superhuman's Split Inbox; Linear's filters; Outlook Focused) to reduce context-switching during triage. [Cited: superhuman-split-inbox-2025, shortwave-vs-superhuman-2025]

2. **Bulk operations.** Selecting multiple items and applying one disposition to all (archive selected, route selected, defer selected). [Cited: whatifdesign-b2b-ux-2026 — batch operations are baseline expectation for enterprise data-dense interfaces.] Critical: bulk operations should never *replace* per-item disposition for the head of the queue, only augment it for the tail.

3. **Triage responsibility / shared queues.** Multiple users can work the same queue with assignment / claim mechanics so items aren't worked twice. [Cited: linear-triage-responsibility-2024]

4. **Snooze with intelligent return.** Deferred items return at a chosen time or on new activity, whichever comes first. [Cited: linear-triage-docs-2026, superhuman-triage-philosophy-2025]

### 3.3 AI augmentation mechanics

The AI overlay specific to this pattern. These follow the conventions in `_cross-cutting-ai-augmentation.md`; any divergences are flagged.

1. **AI priority labeling with visible reasoning.** Items can be auto-labeled high / normal / low priority. Critically, the user can see *why* a label was assigned — a brief, plain-language explanation in the reading pane or row hover. [Cited: copilot-outlook-prioritize-2026, ctts-copilot-inbox-2026 — Microsoft's design explicitly surfaces "why this is important" because trust requires explainability.]

2. **AI routing suggestions.** For items that need to be routed (assigned, delegated, sent to a specific queue), AI proposes a target with rationale. The user accepts or overrides. [Cited: linear-triage-intelligence-2026]

3. **AI batch suggestions** — "here are 12 items that look routine and have similar dispositions; review and confirm as a batch." Distinct from raw bulk operations because AI is proposing the grouping, not the user. [Cited: superhuman-auto-labels-2025]

4. **User behavior feedback loop.** The AI's priority and routing decisions learn from what the user actually does (which items they open, reply to, defer, override). Personalization is gradual, not abrupt. [Cited: copilot-outlook-prioritize-2026 — "the model balances general rules with individual usage patterns"; copilot-priority-learning-2025]

5. **Conservative defaults.** AI signals augment, never replace, the user's view. The queue continues to function correctly with AI labels off; AI labels appear as overlays on a queue that is already complete. [Judgment: this is the right trust posture for legal contexts where missed items have real consequences. Multiple sources flag the risk: "users must learn to trust Copilot's judgment. A poorly prioritized email, or missed reply, could result in business consequences." [Cited: windowsforum-priority-view-2025]]

---

## 4. Expectations to Honor (Closest Analogous Product: Outlook)

Per Overview §7: "Honor the closest analogous product the user already uses. Diverge only with stated reason."

Every primary persona for Spaarke uses Outlook daily. Their muscle memory for queue triage *is* Outlook's muscle memory. The Queue pattern in Spaarke must feel like Outlook unless we have a stated reason for it not to.

### 4.1 What Outlook does that Spaarke must match

- **Reading-pane-on-right with list-on-left as the default layout.** Users scan the list, click a row, read in the pane. [Cited: outlook-reading-pane-base — established Outlook layout convention.] Alternatives (single-pane scroll, conversation-first view) work in consumer products but are not Outlook's mainstream pattern.
- **Subject / sender / preview as the row content.** [Cited: nng-newsletter-inbox-congestion-2019 — these are the load-bearing columns at high volume.]
- **Keyboard shortcuts for archive, delete, flag, reply.** Standard Outlook bindings (Delete to delete, Reply with Ctrl+R, etc.) must work; alternative bindings can layer on top but cannot conflict. [Cited: outlook-shortcuts-base]
- **Unread / read distinction is visually unambiguous.** Bold-vs-regular weight is the standard treatment; users notice when it is absent or weak. [Cited: nng-email-research-base]
- **A search affordance that scopes to the queue, with the option to broaden.** [Cited: outlook-search-base]
- **Drag-and-drop for moving items between queues / folders.** Even users who prefer keyboards expect drag to work. [Cited: outlook-drag-drop-base]
- **A clear "this queue is empty" state, not a confusing blank.** [Cited: superhuman-inbox-zero-2025]

### 4.2 Where Spaarke legitimately diverges

| Spaarke divergence | Why | Tag |
|---|---|---|
| Queue items carry stronger contextual signals (matter, counterparty, deadline, risk score) than Outlook rows | Legal triage decisions require more context per item than personal email triage [Cited: xakia-prioritization-framework-2024 — risk and impact ratings are load-bearing for legal triage] | `[Cited]` |
| AI priority reasoning is *expected to appear by default*, not opt-in | Legal users are accountable for what they do or don't act on; "AI prioritized this" must be explainable in audit, not hidden behind a toggle | `[Judgment: legal accountability context differs from consumer email]` |
| Bulk operations are first-class, not buried in a context menu | In-house counsel routinely face batches of routine renewals or low-risk NDAs that can be dispositioned in groups [Cited: bloomberg-law-contract-management-2025; sirion-ai-contract-review-2026 — bulk auto-classification by risk is a core enterprise expectation] | `[Cited]` |
| The queue can drive the Context pane to show matter-level state (deadlines, related items, prior actions) when a row is focused | This is where the three-pane shell adds value over Outlook's two-pane layout; the Context pane is the divergence's reason for existing | `[Judgment: ties to Spaarke's shell architecture, design.md §2]` |

---

## 5. Common Failure Modes

| Failure mode | Symptom | Fix |
|---|---|---|
| **Queue drifts into Workflow.** Designers add staged status columns (Draft / Review / Approved) to a queue. | Users start managing stages instead of triaging items. Decision velocity drops; the pattern stops working. | Enforce flatness. Staged work belongs in Workflow. The queue can show *current state* per row but cannot become a state-management UI. |
| **Row content is insufficient for no-open decisions.** Users must open every item to disposition it. | Triage takes 10× as long as it should. The queue becomes a list of links, not a triage surface. | Mandate triage-signal density per row. Test: can the user disposition ≥70% of items without opening them? |
| **AI prioritization without explainability.** Items get high/normal/low labels with no visible reason. | Users override the AI on everything (defeats the feature) or trust it blindly (real consequences when wrong). [Cited: windowsforum-priority-view-2025] | Always surface the "why" — a one-sentence rationale on hover or in the focus pane. Never ship priority signals without reasoning surfaces. |
| **No closure state.** The queue never reaches zero, even when all items are dispositioned. | Users lose the sense of completion that makes the pattern psychologically tolerable. | Design the empty state. If "true zero" isn't achievable (always-on queues), define "current batch cleared" as the closure signal. |
| **Bulk operations encourage carelessness.** Users select-all-and-archive to clear backlog, missing important items. | Legal accountability problem: missed items where the user can't say what they discarded. | Bulk operations require visible confirmation showing what's about to happen ("Archive 47 items? Three are marked high priority — review first?"). |
| **Filter / sort changes look like data changes.** A user filters to "high priority" and thinks items disappeared. | Confusion, loss of trust in the system. | Filter / sort state is always visible; result count is shown; "showing 12 of 247" is mandatory. |

---

## 6. Spaarke Component Mapping

### 6.1 Current design positions

The Queue / Inbox pattern is **not currently represented as a primary widget in the R2 design**. This is a meaningful gap.

| Pattern element | Current Spaarke component | Reference |
|---|---|---|
| Queue list widget | **No existing widget.** Closest analog is the playbook gallery in the Context pane, which is not a queue. | design.md §2.1 (Context pane gallery is not this) |
| Pane assignment | The Workspace pane is the natural home for a queue (the user's active work surface). | design.md §2 (Workspace as primary work area) |
| AI augmentation surface | The Context pane is suited to display per-item reasoning when a queue row is focused. | design.md §2.1 (Context pane is adaptive; entity info / sources stage applies) |
| Cross-pane interaction | Selecting a queue row should drive the Context pane to matter / entity context. This protocol exists in the SSE event contract. | spec.md FR-207 (cross-pane interaction protocol); FR-801 (SSE events) |

### 6.2 Design-challenge findings

These flow to `/challenges/design-challenges.md` per Task X.5.

| Finding | Current design | Pattern requires | Evidence tier |
|---|---|---|---|
| Queue widget does not exist in the R2 widget set | R1's 7 widgets (StatusSummary, BudgetDashboard, ProgressTracker, FindingsWidget, etc.) plus R2 additions (DocumentViewer, RedlineViewerWidget) cover Summary, Workflow, and Canvas patterns but not Queue | A first-class **TriageQueueWidget** is needed. It would render in the Workspace pane, support keyboard-driven triage, bulk operations, AI priority labels with reasoning, and cross-pane Context drive. | `[Judgment: the use case literature strongly implies queue-style triage is core to in-house counsel work; bloomberg-law-contract-management-2025, ironclad-legal-intake-2025, xakia-prioritization-framework-2024 all describe queue-pattern workflows]` |
| AI priority reasoning surface convention is not yet specified for non-Generative patterns | design.md §9 covers AI safety for the conversational pattern; the convention for AI overlays on queue rows is not specified | A reasoning-surface convention is needed: where it appears (on hover? in the focused-row pane? in the Context pane?), how it is styled (consistent with other AI-suggested-vs-user-entered distinctions), and how user overrides are captured for the feedback loop | `[Open: prototype-testing — three placements should be tested before committing]` |
| Existing SSE event types may not cover "row focused" as a discrete event | spec.md FR-801 names workspace events (workspace_widget, workspace_action) but not row-level focus changes within a widget | Either: (a) a new SSE event type `workspace_row_focused` with row ID and widget ID, or (b) widget-internal event handling with a defined hook into the cross-pane protocol. Choice has implications for how AI reasoning syncs to the Context pane. | `[Open: engineering call — both designs work; the right one depends on whether other widgets need row-focus events]` |

### 6.3 New components / events / widgets proposed

| Proposed addition | Type | Why this pattern needs it |
|---|---|---|
| **TriageQueueWidget** | New Workspace widget | First-class implementation of the Queue pattern. Carries triage signal per row, disposition shortcuts, bulk operations, AI priority overlays with reasoning. Renders in the Workspace pane; drives the Context pane on row focus. |
| **AI reasoning surface convention** | Cross-cutting AI augmentation pattern | Define where "why this priority?" appears, consistent with other AI overlays. Belongs in `_cross-cutting-ai-augmentation.md` and is referenced by this spec. |
| **`workspace_row_focused` SSE event** (provisional) | New SSE event type, pending engineering call in §6.2 finding 3 | Enables widgets to participate in cross-pane Context drive at row granularity. May also benefit other list-based widgets if added. |

---

## 7. Open Questions

These feed Task 6.1 (claim categorization sweep).

| Question | Why it matters | Proposed validation |
|---|---|---|
| What is the right disposition vocabulary for in-house counsel triage queues? Outlook uses Archive / Delete / Flag / Move; Linear uses Accept / Decline / Snooze. Legal triage may need its own (Accept-and-route / Decline / Defer / Escalate / Send-back-for-info)? | Wrong disposition set forces users into manual workarounds (creating fake states, using flags as dispositions) | Contextual inquiry with primary personas; observation of current workarounds in R1 or in Outlook / CLM tools |
| How much triage signal per row is right? Too little forces opens; too much makes the row unscannable. | The pattern's central tradeoff. Wrong setting kills decision velocity. | Prototype testing with 3 row-density variants |
| When AI prioritization is wrong, what's the right correction UX? Click-and-relabel? A dedicated "this was wrong" affordance? Implicit (the user moves the item and the model learns)? | Trust-building over time depends on correction mechanics. Wrong design produces either AI fatigue (users ignore signals) or AI dependency (users stop checking). | Prototype testing + pilot instrumentation on override rates |
| Should the Spaarke queue support multiple concurrent queues (split inbox / shared queues / personal vs team) in v1, or is a single-queue v1 sufficient? | Implementation scope. Single-queue ships sooner; multi-queue is what high-volume in-house counsel actually need. | Designer's call informed by primary persona use case volumes; revisit at pilot |
| How does the queue integrate with cross-matter context? When the user pivots from a queue item in matter A to a queue item in matter B, does the queue itself stay scoped to one matter, or is it cross-matter with filtering? | Critical for the Operational Containers cross-cutting. Wrong answer breaks privilege handling (design.md §9.2.4). | Engineering call + composition guide (Phase 5) work |

A pattern spec with zero open questions is suspicious. These five are real and unresolved.

---

## 8. Sources Cited

This is a representative set; the full bibliography will list all sources with full schema. The IDs here are provisional and may be refined when `bibliography.md` populates.

- `nng-email-research-base` — Nielsen Norman Group, email research collection
- `nng-newsletter-inbox-congestion-2019` — NN/g, inbox congestion and information density
- `superhuman-triage-philosophy-2025` — Superhuman, email triage method
- `superhuman-100ms-philosophy-2025` — Superhuman, 100ms rule and Game Design philosophy
- `superhuman-shortcuts-2025` — Superhuman, keyboard shortcut architecture
- `superhuman-flow-model-2025` — Superhuman, "decisions per email" workflow model
- `superhuman-split-inbox-2025` — Superhuman, Split Inbox feature
- `superhuman-inbox-zero-2025` — Superhuman, redefinition of Inbox Zero
- `superhuman-auto-labels-2025` — Superhuman, AI auto-labels (October 2025 release)
- `linear-triage-docs-2026` — Linear, Triage documentation
- `linear-triage-intelligence-2026` — Linear, Triage Intelligence (AI routing)
- `linear-triage-responsibility-2024` — Linear, Triage Responsibility (shared queues)
- `linear-conceptual-model-2026` — Linear, conceptual model (issues / projects / triage)
- `shortwave-vs-superhuman-2025` — Baytech Consulting, comparison of structured vs. velocity triage philosophies
- `copilot-outlook-prioritize-2026` — Microsoft Community Hub, Copilot for Outlook Prioritize My Inbox
- `copilot-priority-learning-2025` — Practical365, Copilot's learning model
- `ctts-copilot-inbox-2026` — CTTS, Copilot inbox prioritization and explainability
- `windowsforum-priority-view-2025` — Windows Forum, Priority View risk analysis
- `bloomberg-law-contract-management-2025` — Bloomberg Law, guide to legal contract management
- `ironclad-legal-intake-2025` — Ironclad, legal intake for in-house counsel
- `xakia-prioritization-framework-2024` — Xakia, prioritization framework for legal departments
- `sirion-ai-contract-review-2026` — Sirion, AI contract review with bulk classification
- `whatifdesign-b2b-ux-2026` — What If Design, B2B enterprise UX principles
- `outlook-reading-pane-base` — Outlook design baseline (Microsoft documentation; foundational tier)
- `outlook-shortcuts-base` — Outlook keyboard shortcuts baseline (Microsoft documentation; foundational tier)
- `outlook-drag-drop-base` — Outlook drag-and-drop conventions baseline (Microsoft documentation; foundational tier)
- `outlook-search-base` — Outlook search-in-folder convention baseline (Microsoft documentation; foundational tier)

---

## 9. Evidence Discipline Self-Check

Per the template's §0.2, this is an established pattern (well-cited research base), so the target was ≥60% Cited and ≤30% Judgment. Rough self-count across §1–§5 substantive claims:

- **Cited**: ~24 claims
- **Judgment**: ~7 claims
- **Open**: 5 open questions in §7 plus 2 Open findings in §6.2

That's about 67% Cited, 19% Judgment, 14% Open — within the target band for an established pattern.

The Open share is real, not performative. Three of the five open questions cannot be answered from literature; they require either contextual inquiry, prototype testing, or pilot instrumentation. Naming this honestly is the discipline.

---

## 10. Review Log

| Date | Reviewer | Status | Notes |
|---|---|---|---|
| 2026-05-18 | (initial author) | Draft v0.1 — end-to-end demonstration | First completed pattern spec; serves as the worked example for the other six |

---

*Draft v0.1 — 2026-05-18. This is the worked example end-to-end; the other six pattern specs and two cross-cutting specs will follow this structure.*
