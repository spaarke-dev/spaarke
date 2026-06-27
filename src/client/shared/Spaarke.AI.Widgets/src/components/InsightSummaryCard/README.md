# InsightSummaryCard

> Per-record AI insight surface for Spaarke MDA forms. Composes Fluent v9
> `<Card>` (inline) + `<Popover>` (inline expand) + `<Dialog>` (modal expand).
> Implements the FR-06 6-state machine and the FR-05 topic-registry mount-check.

**Package**: `@spaarke/ai-widgets` (v0.1.0)
**Status**: Phase 3 complete (Tasks 030–035). Phase 4 host integration in progress (Task 042+).
**Spec**: `projects/ai-spaarke-insights-engine-widgets-r1/spec.md` FR-01 / FR-02 / FR-05 / FR-06 / FR-07 / FR-20 / FR-21

---

## Storybook status — "or equivalent" per SC-01

`@spaarke/ai-widgets` does **NOT** have Storybook configured (no `.storybook/`,
no Storybook deps, no `*.stories.tsx`). This is intentional per
[DR-001 §Negative](../../../../../../../projects/ai-spaarke-insights-engine-widgets-r1/decisions/DR-001-component-reuse.md):

> Storybook gap in `@spaarke/ai-widgets` — package has NO `.storybook/`
> directory, no `*.stories.tsx`, no Storybook deps. Phase 3 Task 030 must
> satisfy SC-01 ("Storybook story or equivalent") via a dev sandbox playground,
> NOT by introducing Storybook into the package.

The **dev sandbox is the SC-01 equivalent** — `InsightSummaryCardSandbox.tsx`
in this folder is a self-rendering React component that demonstrates all six
FR-06 states (idle / loading / loaded / error / decline / stale) in a
responsive grid, with a built-in light/dark theme toggle and an inline props
table. It is exported from the package barrel and can be embedded into any
host shell (dev playground, internal admin page, manual QA surface) with:

```tsx
import { InsightSummaryCardSandbox } from '@spaarke/ai-widgets';

export function DevPlayground() {
  return <InsightSummaryCardSandbox />;
}
```

Storybook may be added to `@spaarke/ai-widgets` in r2+ when the package
accumulates a second or third lightweight per-record component. Until then,
shipping Storybook config + tooling for a single component is yak-shaving per
DR-001 §Consequences.

---

## Files in this folder

| File | Purpose |
|---|---|
| `InsightSummaryCard.tsx` | Component implementation. Composes Card + Popover + Dialog with FluentProvider portal re-wrap (binding per `fluent-v9-portal-gotcha.md`). |
| `InsightSummaryCard.types.ts` | Public prop contract — `InsightSummaryCardProps` + `InsightDeclineError` helper. JSDoc on every prop. |
| `InsightSummaryCardSandbox.tsx` | **SC-01 dev sandbox** — all six states + light/dark toggle + props table. Exported from the package barrel. |
| `Citation.types.ts` | Discriminated `Citation` union (`assessment` / `document` / unknown fallback) per FR-07. Type guards exported. |
| `state.ts` | 6-state reducer + action union + `InsightEnvelope` shape. `insightCardReducer` is pure and exported (testable in isolation). |
| `useInsightSummaryCardStyles.ts` | Griffel styles. Semantic tokens ONLY (ADR-021); no hex, no rgba, no `var(--...)`. |
| `index.ts` | Barrel — re-exports everything the package surface needs. |
| `README.md` | This file. |

---

## Props (high level)

Full JSDoc lives on the `InsightSummaryCardProps` interface in
`InsightSummaryCard.types.ts`. Summary:

| Prop | Type | Required | Notes |
|---|---|---|---|
| `topic` | `string` | yes | Registry key from `sprk_aitopicregistry`. Bare identifier (no `@v1`/`@vN` per Q-U1). |
| `subject` | `string` | yes | `matter:GUID` for r1; multi-entity schemes shape-compatible for r2+. |
| `mode` | `string` | no (default `'single'`) | Reserved for r2+ multi-mode topics. |
| `parameters` | `Record<string, unknown>` | no | Forwarded to playbook invocation; r1 doesn't read any specific keys. |
| `kpiSlot` | `ReactNode` | no | Host-supplied KPI block (Matter Health KPIs per FR-13). |
| `onCitationClick` | `(c: Citation) => void` | no | Host wires navigation (`Xrm.Navigation.openForm` for assessments; SPE viewer for documents). |
| `onFetchInsight` | `(opts?: { force?: boolean }) => Promise<InsightEnvelope>` | no | Lazy-load callback; `force=true` bypasses cache per FR-20. |
| `onFetchRegistry` | `(topic, mode) => Promise<InsightRegistryEntry \| null>` | no | FR-05 mount-check. `null` → renders nothing. |
| `onRegistryResolved` | `(entry: InsightRegistryEntry) => void` | no | Fires when a registry row resolves to `enabled`. Host wires TTL timers. |
| `theme` | `Theme` | no (default `webLightTheme`) | **Production hosts MUST pass active MDA theme** for portal re-wrap (ADR-021 dark mode). |
| `triggerLabel` | `string` | no (default `'View Insight'`) | Localisation is host-responsibility. |
| `className` | `string` | no | Merged LAST via `mergeClasses` per Fluent v9 convention. |

**Intentionally absent**: `onFeedback`. Feedback affordance deferred to r2+
pending the AIPU2 Cosmos `feedback` container (Q-U3 + ADR-015). Do NOT add
without re-opening Q-U3.

---

## State machine (FR-06)

```
                ┌─ MARK_STALE ─┐
                ▼               │
  idle ──► loading ──► loaded ──┘
            │  │
            │  ├──► error      (graceful 503 message; FeatureDisabled per ADR-032)
            │  │
            │  └──► decline    (insufficient evidence; owner-confirmed exact text)
            │
   (RESET any → idle)
```

All transitions are pure (`insightCardReducer` in `state.ts`); the reducer is
exported and unit-testable. The component owns state; the host owns service
injection via `onFetchInsight`.

---

## Theme + portal re-wrap

Both the `<Popover>` and the `<Dialog>` render through React Portal and escape
the outer FluentProvider DOM subtree. The component re-wraps **each** portal
surface in its own `<FluentProvider theme={portalTheme}>` so:

- Dark mode propagates through both surfaces (most common regression).
- Custom tenant themes propagate.
- Token-driven spacing/colors resolve correctly.

The `theme` prop is the surface that lets the host pass the active MDA theme
through. Defaulting to `webLightTheme` is acceptable for dev sandboxes
(Task 035) but **NOT** for Matter form hosts — passing the active theme is a
binding ADR-021 requirement.

See `.claude/patterns/ui/fluent-v9-portal-gotcha.md` for the canonical rule.

---

## Related decisions + tasks

- [DR-001 — Component reuse](../../../../../../../projects/ai-spaarke-insights-engine-widgets-r1/decisions/DR-001-component-reuse.md)
- [Tasks 030–034 — Component implementation series](../../../../../../../projects/ai-spaarke-insights-engine-widgets-r1/tasks/)
- [Task 035 — This sandbox](../../../../../../../projects/ai-spaarke-insights-engine-widgets-r1/tasks/035-storybook-stories.poml)
- [Task 042 — Matter form host integration](../../../../../../../projects/ai-spaarke-insights-engine-widgets-r1/tasks/042-host-card-on-matter-form.poml)
- [ADR-021 — Fluent v9 + semantic tokens](../../../../../../../.claude/adr/ADR-021-fluent-v9.md)
- [ADR-012 — Shared component library](../../../../../../../.claude/adr/ADR-012-shared-component-library.md)
