# Pattern Spec: Workflow / Process-centered

> **Status**: Draft v0.1
> **Pattern slug**: workflow-process
> **Closest analogous product:** Dynamics 365 business process flow (with reference to ServiceNow workflow and Asana / Linear stage progressions)
> **Primary user intent:** *"Help me move this through approval / execution / completion."*

---

## 1. Optimizes For

The Workflow pattern optimizes for **moving a specific thing through a defined sequence of stages with defined transitions**. The user is not deciding what to work on (Queue) or doing deep work on one artifact (Canvas) — they are advancing an item through its lifecycle, with each stage having specific work and exits.

This is the pattern that handles "new matter intake → conflicts check → engagement letter → active matter," or "draft contract → internal review → counterparty negotiation → executed." The shape of the work is *staged*, and the user benefits from seeing the shape.

---

## 2. When To Use / When Not To Use

### 2.1 Use this pattern when

- The work has *named stages* that an item passes through in a defined order (or in a small set of allowed orders).
- Each stage has *specific work* that must happen before the item can advance.
- Users need to *see where an item is* in the flow at a glance and *what's next*.
- The work involves *handoffs* between roles or stages, with the system tracking who has the ball.
- Audit trail of stage transitions matters (when did this advance to executed, by whom).

### 2.2 Do not use this pattern when

- The work is flat triage with disposition options that don't constitute a *progression* (Queue, not Workflow).
- The item doesn't have stages — it has a state that changes but not a defined sequence (Entity).
- The stages are so simple that a single status field would suffice ("not started / done"). Workflow's structure is overkill for two states.
- The user is doing the work, not advancing the work. Drafting a contract is Canvas; *moving* the contract through review-and-approval is Workflow.

### 2.3 Pattern overlaps

- **Queue / Inbox-centered**: Queue is flat triage. Workflow is staged progression. A queue often *feeds* a workflow (the item is queued for triage, then enters a workflow on accept). They are different patterns and the R2 design's separation is correct.
- **Form / Wizard-centered**: Forms are *embedded inside* workflow stages. The "new matter intake" workflow has a form at the first stage; the form is a Form pattern instance, the surrounding stage progression is Workflow.
- **Entity-centered**: An entity record participates in many workflows over its life. The workflow is a *view* of the entity at a moment; the entity persists across workflows.

---

## 3. Mechanics

### 3.1 Core mechanics

1. **Visual stage progression.** The user sees the named stages, where the current item is, what's done, and what's ahead. Dynamics' business process flow uses a horizontal stepped bar; ServiceNow uses a similar pattern. The convention is well-established.

2. **Per-stage work surface.** When the user is at a stage, they see what's required to complete it — the form to fill in, the document to review, the approval to grant. The work surface is *contextual to the stage* and changes as the user advances.

3. **Explicit stage transitions.** Advancing to the next stage is a user action, not an automatic side effect. "Mark this stage complete and advance" is a defined operation that may include validation ("required fields incomplete — cannot advance").

4. **Backward navigation with care.** A user can return to a previous stage to review or correct, but the system tracks that this happened and the audit trail records it. Workflow is not unidirectional; it is *predominantly* forward with explicit reversal.

5. **Role-aware stages.** Different stages may be owned by different users. The system shows "this stage is awaiting [user / role]" and routes correctly when the current user is not the right owner.

6. **Stage validation and required work.** Cannot advance to "executed" until "all signatures collected" passes. The validation is part of the stage definition, not an afterthought. Users see what's blocking advancement.

7. **A clear "completed" state.** When the workflow finishes, the user sees it. The item exits the workflow into its post-workflow state (an executed contract becomes a passive Entity in active matters; a closed matter is archived).

### 3.2 Supporting mechanics

1. **Skip / branch options where the flow allows them.** Not all workflows are linear. Some have optional stages or branches ("if value > $1M, additional approval required"). The visual representation must show possible paths, not just the linear one.

2. **Parallel stages.** Some workflow steps happen concurrently rather than sequentially ("internal review" and "counterparty notification" in parallel). The visualization must handle this without forcing them into a false sequence.

3. **Reminders / SLA tracking.** Stages have expected durations; the system flags items overdue at their current stage. This blurs into Summary and Queue territory (an overdue dashboard, or a queue of overdue items) — that's expected and good.

4. **Stage notes and handoff context.** When a stage is completed by user A and the next is owned by user B, A can leave a note that B sees on arrival. Without this, workflows decay into "who knows what I was thinking when I sent this over."

### 3.3 AI augmentation mechanics

1. **AI-suggested next action.** Within a stage, the AI may surface "the next thing to do here" based on what's missing or what's typical. "This matter intake is missing conflicts-check sign-off; the typical next step is to forward to [name]."

2. **AI completion of stage forms.** Many workflow stages are forms; AI pre-fill of those forms is the same mechanic as in the Form pattern, but contextualized by the workflow's prior stages.

3. **AI-summarized handoff context.** When user A completes a stage and B picks up the next, the AI can produce a short summary of what happened so far. "Stage 1 (intake) completed by A on May 10; conflicts check cleared with one waiver from [counterparty]. Open question: governing-law preference."

4. **AI-flagged stage anomalies.** "This matter has been in the negotiation stage 3× longer than typical for matters of this type. Possible blockers: [analysis]." A workflow-specific anomaly surface.

5. **Conservative defaults.** AI does not advance stages on its own. Advancement is always a user action. The reason is accountability: stages are decisions with consequence; the AI can prepare them but not commit them.

---

## 4. Expectations to Honor (Closest Analogous Product: Dynamics 365 Business Process Flow)

Spaarke users with Dynamics exposure (most) will have intuitions for this pattern. Those without will have ServiceNow, Salesforce flow, or Asana stage exposure. All converge on similar conventions.

### 4.1 What Dynamics BPF does that Spaarke must match

- **Horizontal stepped header showing all stages**, with the current stage prominent and prior stages marked complete.
- **Clicking a stage in the header navigates** to that stage's work surface.
- **Required fields per stage** are surfaced and block advancement until satisfied.
- **Stage advancement is one explicit action** (a button labeled "Next Stage" or equivalent), not multiple steps.
- **Stage history is preserved**; a user can see what was filled in at prior stages without losing the current stage's work.

### 4.2 Where Spaarke legitimately diverges

| Spaarke divergence | Why |
|---|---|
| The Context pane shows matter-level state alongside the workflow's per-stage surface | Spaarke's three-pane shell lets workflow stages share visual real estate with matter context; Dynamics fits both in a single canvas |
| AI handoff summary at stage transition | Dynamics has stage notes but no AI-generated summary; for legal workflows where stages may span days and team members, the summary materially reduces re-orientation cost |
| Parallel and branching stages have first-class visualization | Dynamics handles parallel weakly; many legal workflows (e.g., redline review + counterparty signature collection) are genuinely parallel and need cleaner representation |

---

## 5. Common Failure Modes

| Failure mode | Symptom | Fix |
|---|---|---|
| **Stages that don't reflect real work.** Designer-defined stages that don't match what users actually do. | Users either ignore the workflow (do work outside it and update the stage at the end) or fight it (force-advance to keep moving). | Workflows must be use-case-grounded, not abstract. Define stages from observation, not from process diagrams. |
| **Hidden required work.** The user clicks "Next Stage" and gets "Cannot advance — 3 required fields incomplete" without knowing which ones. | Frustration and false starts. | Required fields are visible on the stage surface before the user tries to advance; advancement-blocking conditions are surfaced inline. |
| **Backward navigation is a black hole.** User clicks back, can't get forward again without losing work. | Users avoid going back even when they should; mistakes compound. | Backward navigation is safe, reversible, and preserves work-in-progress on the current stage. |
| **Stages that should be parallel are forced sequential.** "Internal review must complete before counterparty notification" when in practice both happen at once. | Users batch-advance through artificial stages; the workflow stops reflecting reality. | Support parallel stages where the actual work is parallel. |
| **Handoffs that lose context.** User A completes a stage; user B picks up the next stage with no idea what just happened. | Repeated work, missed signals, decisions reversed. | Mandatory handoff context (notes or AI summary) at stage boundaries with multi-user workflows. |
| **No view of items in flight.** A workflow exists for new matter intake, but there's no way to see "all matters currently in workflow." | Users use the workflow for individual items but lose the portfolio view. | Workflow needs a portfolio companion (Summary or Queue showing items by current stage). This is the Workflow → Summary cross-pattern transition. |

---

## 6. Spaarke Component Mapping

### 6.1 Current design positions

| Pattern element | Current Spaarke component |
|---|---|
| Workflow visualization | **ProgressTrackerWidget** (R1) — represents stage progression. Existing component is fit for purpose at the pattern level. |
| Embedded forms per stage | **Embedded wizards** in Workspace (spec.md FR-206) — the WizardDialog adapted for in-workspace use, hosted by the workflow at each stage. |
| Stage transitions | SSE event `workspace_action` (spec.md FR-801) handles cross-pane coordination on stage advancement. |
| Audit / history | spec.md addresses audit at the Dataverse level; the user-facing surface (stage history view) needs UI specification. |
| AI augmentation | Not currently specified for workflow-specific behaviors (handoff summary, anomaly detection, suggested next action). |

### 6.2 Design-challenge findings

| Finding | Current design | Pattern requires |
|---|---|---|
| **Parallel and branching workflows aren't represented in ProgressTrackerWidget.** | ProgressTracker shows linear progression. | Either extend ProgressTracker to handle branches and parallel stages, or add a separate visualization for non-linear flows. Most legal workflows have at least one branch (value-based approval thresholds, parallel reviews). |
| **AI handoff summary at stage transition isn't a defined component.** | No current spec. | A standard component for AI-generated handoff context when stages change owner. Belongs in AI Augmentation cross-cutting; rendered by ProgressTracker or by a companion. |
| **Portfolio view of in-flight workflows isn't defined.** | Individual workflows are tracked; no defined "show me everything in workflow stage X" view. | A Workflow → Summary cross-pattern transition: a dashboard view of workflows in flight, grouped by current stage, with drill-down to specific items. Belongs in the Composition Guide. |
| **Stage-history surface for the user (not just the audit log) isn't specified.** | Audit at the data layer is solid; the UI representation (a timeline of stage advances and notes) isn't yet specified. | A stage-history surface within the workflow widget — likely a collapsible timeline view of the workflow's transitions. |

### 6.3 New components / events / widgets proposed

- **Extended ProgressTrackerWidget** supporting parallel and branching stages — engineering call on whether this extends the existing widget or introduces a separate one for complex workflows.
- **Stage-handoff summary surface** — AI-generated context delivered at stage transitions to the new owner. Belongs in AI Augmentation cross-cutting.
- **Workflow portfolio view** — a Summary-pattern companion specifically for items in flight across all workflows. Belongs in Composition Guide as a Workflow → Summary transition.
- **Stage-history timeline** within the workflow widget.

---

## 7. Open Questions

| Question | Why it matters | Validation |
|---|---|---|
| Are workflows fixed at design time or can they be customized per matter / per customer? | Customization is what enterprise customers expect; rigidity is what makes the product reliable. Power Apps gives us customization machinery; the question is how much we expose. | Engineering call + product positioning decision |
| When a workflow stage requires a form that's also exposed standalone (e.g., conflicts check), is it the same component embedded or a workflow-specific variant? | Architecture cleanliness. Same-component-embedded is cleaner but requires the form to be aware of workflow context. | Engineering call |
| When a user is at stage 3 and edits a field set at stage 1, does the workflow snap back? Is stage 1 marked incomplete again? Or is the edit just allowed and recorded? | Audit and consistency. The right answer probably varies by workflow type. | Designer's call per workflow type; pilot observation |
| For overdue stage SLAs, where does the notification go — into a Queue (as a new item), into a Summary (as an aggregate), or both? | This is one of the most consequential composition questions in the system. | Composition guide (Phase 5) and engineering call |

---

*Draft v0.1 — 2026-05-18.*
