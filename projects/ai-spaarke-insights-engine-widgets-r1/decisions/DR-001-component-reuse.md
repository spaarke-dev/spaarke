# DR-001 — Component reuse for `InsightSummaryCard`

> **Decision-record series**: r1 project decisions
> **Status**: ✅ **Ratified** (empirical investigation confirms plan-time finding)
> **Date**: 2026-06-10
> **Decider**: task-execute (Task 001, Wave 0)
> **Inputs**: [`../notes/insight-component-reuse-investigation.md`](../notes/insight-component-reuse-investigation.md), `spec.md` FR-03 / Resolution Decisions
> **Supersedes**: — (first decision record in this project)

---

## Context

`spec.md` FR-03 mandates that `InsightSummaryCard` reuse existing Spaarke UI assets where possible and falls back to net-new construction only for genuinely novel composition. The plan-time `/project-pipeline` pre-flight (2026-06-10) applied a preliminary finding to `spec.md` Resolution Decisions (row "FR-03"); this decision record ratifies that finding with empirical evidence.

---

## Decision

`InsightSummaryCard` will ship in the **`@spaarke/ai-widgets`** package, composing Fluent v9 **`<Card>`** (inline surface) and **`<Dialog>`** (manual modal expand) primitives. It reuses the **lazy-load callback shape** and **state machine baseline** from `AiSummaryPopover` (extending the latter from 4 states to 6) but does NOT reuse `AiSummaryPopover`'s **`<Popover>`** surface — `Card` is the primary surface for the per-record use case, not Popover.

`FeedbackButtons` is explicitly **NOT** reused in r1 (Q-U3 — feedback affordance deferred until AIPU2 Cosmos `feedback` container lands on master per ADR-015).

---

## Rationale

One reason. Three empirical anchors.

**Reason**: the Card + Dialog composition is the **only** structurally honest fit for the per-record inline-card-with-optional-modal-expand UX pattern that FR-01 + FR-02 require, and `@spaarke/ai-widgets` is the only package whose conventions, dependencies, and sibling components support a lightweight per-record AI control without forcing a workspace-widget or PCF wrapper.

**Anchors**:

1. **`@spaarke/ai-widgets` v0.1.0** is the correct package home. Verified at [`src/client/shared/Spaarke.AI.Widgets/package.json`](../../../src/client/shared/Spaarke.AI.Widgets/package.json) (v0.1.0; Fluent v9 `^9.0.0` peer; React 19 `^19.0.0` peer; workspace dep on `@spaarke/ui-components`). Sibling lightweight per-record controls already live here — see [`src/client/shared/Spaarke.AI.Widgets/src/components/ConfidenceIndicator.tsx`](../../../src/client/shared/Spaarke.AI.Widgets/src/components/ConfidenceIndicator.tsx) (referenced from `src/index.ts` line 524). `@spaarke/ui-components` is the older sibling lib for context-agnostic primitives consumed BY this package (workspace dep direction).

2. **`AiSummaryPopover` lazy-load pattern IS reused**. The callback shape (`() => Promise<TData>`), the on-open-fetch-once gate (`open && !data && !loading`), and the copy-to-clipboard + 2s confirmation are reused verbatim. See [`src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover/AiSummaryPopover.tsx`](../../../src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover/AiSummaryPopover.tsx) lines 99-133 (state machine) and 106-113 (copy handler). The **4-state machine extends to 6 states** for r1 (`idle / loading / loaded / decline / stale / error`); the additional `decline` and `stale` states are net-new contracts driven by spec FR + cache-freshness semantics.

3. **No inline-expand-to-modal composition exists** anywhere in `src/client/shared/`. Grep over both `Spaarke.UI.Components` and `Spaarke.AI.Widgets` for `Dialog.*Popover`, `Popover.*Dialog`, `expand-to-modal`, `inline.*modal` returned zero composition hits (only export listings + unrelated header comments — see [`../notes/insight-component-reuse-investigation.md`](../notes/insight-component-reuse-investigation.md) §4 for full grep table). The Card + Dialog composition is therefore net-new for the r1 framework; it will be the canonical pattern future per-record AI cards inherit from.

---

## Consequences

### Positive

- **No new package introduced.** `@spaarke/ai-widgets` accumulates one more lightweight per-record component alongside `ConfidenceIndicator`, `CitationBadge`, `GroundednessHighlight`. Future per-record AI cards (r2+ topics, e.g., matter-collection, cohort) follow the same composition.
- **Lazy-load pattern reuse** preserves the consumer contract surface area: hosts inject a callback that returns a typed payload; the component owns state + presentation. This matches ADR-010 (DI minimalism — no new interface seams in r1).
- **Fluent v9 semantic tokens** carry forward (ADR-021). Dark-mode parity is by-construction.
- **Card + Dialog composition** becomes the **reference template** documented in `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` (Phase 6 task 060), seeding the framework story.

### Negative / risks to manage in Phase 3 task 030

- **Storybook gap in `@spaarke/ai-widgets`** — package has NO `.storybook/` directory, no `*.stories.tsx`, no Storybook deps. Phase 3 task 030 must satisfy SC-01 ("Storybook story or equivalent") via a dev sandbox playground (recommended: a route in the SpaarkeAi code page rendering all 6 states), NOT by introducing Storybook into the package. Rationale documented in [`../notes/insight-component-reuse-investigation.md`](../notes/insight-component-reuse-investigation.md) §3.3.
- **Fluent v9 portal re-wrap rule applies** — both `<Dialog>` and any incidental `<Popover>` (e.g., KPI tooltips) render through portals. The Matter form host must satisfy `.claude/patterns/ui/fluent-v9-portal-gotcha.md` (referenced in project CLAUDE.md "Applicable patterns" section). Phase 3 task 030 must explicitly test the modal expand within the Matter form host context (Phase 4 task 040+ integration verifies).
- **6-state machine is more complex than `AiSummaryPopover`'s 4-state**. Recommend `useReducer` in Phase 3 task 030 for readability once `decline` + `stale` enter the mix; do NOT chain `useState` hooks linearly.

### Neutral

- No new ADR introduced (spec NFR-09 honored).
- No identifier-suffix versioning vernacular used (Q-U1 owner ban honored).
- No `FeedbackButtons` reference in deliverables beyond the explicit exclusion (Q-U3 deferral honored).

---

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Ship `InsightSummaryCard` in `@spaarke/ui-components` (older sibling lib) | `@spaarke/ui-components` is the **context-agnostic primitives** lib. `InsightSummaryCard` consumes AI-specific concepts (insight envelope, citations, cache freshness, decline reason) that belong with the AI sibling components. Mixing AI-domain widgets into the primitives lib would violate ADR-012 boundaries. |
| Reuse `AiSummaryPopover` directly and add a modal-expand prop to it | `AiSummaryPopover`'s SURFACE is a Popover; adding modal escalation would invert the component's intent and break its existing R5 Summarize consumers. The reuse is at the **pattern level** (lazy-load callback, state machine, copy-paste UX), not at the component level. |
| Build `InsightSummaryCard` as a PCF control | PCF is for cases where MDA host-context lifecycle is required (per ADR-006 UI Surface Architecture). The Matter form integration in r1 is a small JS web-resource handler invoking a React component in a portal — not a full PCF lifecycle. PCF would over-engineer the surface and slow Phase 3. |
| Defer the modal expand to r2 and ship inline-Card-only in r1 | FR-02 explicitly requires the modal expand affordance ("manual 'expand to modal' affordance always available"). Owner accepted FR-02 at spec sign-off — cannot defer. |
| Include `FeedbackButtons` opportunistically (it exists in the package) | Q-U3 explicitly defers feedback to r2+. Pulling `FeedbackButtons` in now would require also pulling in AIPU2's Cosmos `feedback` container infrastructure (per ADR-015), which is on AIPU2's roadmap and not on master. Owner decision. |

---

## Acceptance criteria self-check (per task POML)

| Criterion | Met? | Evidence |
|---|---|---|
| DR-001 explicitly cites at least one file path from `src/client/shared/Spaarke.AI.Widgets/` | ✅ | Anchor 1 cites `Spaarke.AI.Widgets/package.json` and `Spaarke.AI.Widgets/src/components/ConfidenceIndicator.tsx` |
| DR-001 records whether `AiSummaryPopover` lazy-load pattern is reused or net-new | ✅ | Anchor 2 explicitly: "**`AiSummaryPopover` lazy-load pattern IS reused**" |
| Investigation notes name at least one Storybook-readiness gap | ✅ | [`../notes/insight-component-reuse-investigation.md`](../notes/insight-component-reuse-investigation.md) §3.3 names the `.storybook/` absence + recommends "equivalent" dev playground path |
| No identifier-suffix versioning syntax appears in either deliverable | ✅ | Verified by grep on both deliverables; zero identifier-suffix versioning tokens present in body content (Q-U1 owner ban honored) |
| No `FeedbackButtons` reference appears as a reuse candidate | ✅ | `FeedbackButtons` mentioned ONLY in the explicit-exclusion paragraph (§5 of investigation notes, "Decision" + "Consequences" sections of this DR) — never as a reuse candidate |

---

## Follow-up

- Phase 3 **task 030** consumes the [§7 punch list](../notes/insight-component-reuse-investigation.md#7-phase-3-task-030--punch-list-seeded-by-this-investigation) in the investigation notes.
- Phase 6 **task 060** (`BUILD-A-NEW-INSIGHT-CARD.md` tutorial) uses the Card + Dialog composition as its reference template.
- If during Phase 3 task 030 the implementer discovers a previously-overlooked composition pattern, file a **DR-001-amendment** (do NOT silently revise this record — DRs are append-only per repo convention).

---

*Decision ratified 2026-06-10. No divergence from plan-time pre-flight finding. No owner escalation required.*
