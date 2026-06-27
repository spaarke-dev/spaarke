# Insight component reuse — investigation notes

> **Task**: 001-fr03-component-reuse-ratification
> **Author**: task-execute (Wave 0, parallel)
> **Date**: 2026-06-10
> **Status**: Investigation complete; ratifies plan-time FR-03 finding.

---

## 1. Purpose

Empirically verify every claim that `/project-pipeline` recorded against **FR-03** in `spec.md` Resolution Decisions:

| Plan-time claim | Verification approach |
|---|---|
| `InsightSummaryCard` should ship in `@spaarke/ai-widgets` | Inspect `Spaarke.AI.Widgets/package.json` + index exports + sibling widget structure |
| Closest extant pattern is `AiSummaryPopover` (inline + popover; **not** modal) | Read `AiSummaryPopover.tsx` end-to-end; document state machine + reuse-able primitives |
| No extant inline-expand-to-modal composition pattern | Grep `src/client/shared/` for `Popover.*Dialog`, `Dialog.*Popover`, `expand-to-modal`, `inline.*modal` |
| `FeedbackButtons` is **NOT** a r1 reuse candidate (Q-U3 deferred) | Confirm by exclusion; spec FR-08 explicitly defers feedback |

---

## 2. AiSummaryPopover — anatomy of the closest extant pattern

**File**: `src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover/AiSummaryPopover.tsx`

### 2.1 Props (`IAiSummaryPopoverProps`)

| Prop | Type | Role for `InsightSummaryCard` reuse |
|---|---|---|
| `trigger` | `React.ReactElement` | Pattern: consumer-provided trigger (typically a `Button` with Sparkle icon). Lets `InsightSummaryCard` keep its own card-header chrome and pass through any clickable affordance. |
| `onFetchSummary` | `() => Promise<ISummaryData>` | **Lazy-load callback shape we will reuse.** Zero service dependency in the component — the host injects the BFF call. Maps cleanly to `InsightSummaryCard.onFetchInsight: () => Promise<InsightEnvelope>`. |
| `positioning` | `'above' \| 'below' \| 'before' \| 'after'` | Optional. Default `'after'`. Suitable for the inline-from-sparkle case. Not relevant in modal expansion. |
| `withArrow` | `boolean` | Optional. Default `true`. Cosmetic. |

### 2.2 Return shape (`ISummaryData`)

```ts
export interface ISummaryData {
  summary: string | null;
  tldr: string | null;
}
```

This is **R5 Summarize-shaped**, not r1-shaped. `InsightSummaryCard` needs a richer envelope (narrative + KPIs + citations + decline reason + cache freshness). Mark as **new contract** in the decision record; pattern is the *shape* of the lazy-load callback, not the *fields* it returns.

### 2.3 State machine (lines 99-133)

Four useState hooks: `data`, `loading`, `error`, `copied`.

The control flow in `handleOpenChange`:

```
open && !data && !loading  →  setLoading(true); setError(false);
                              onFetchSummary()
                                .then(setData; setLoading(false))
                                .catch(setError(true); setLoading(false))
```

**Maps directly to FR-01's 5 states**: idle (`!data && !loading && !error`), loading, loaded (data present), error (`error` true), and — net-new for r1 — `decline` and `stale`. r1 needs to extend this 4-state machine to a 6-state machine: idle / loading / loaded / **decline** / **stale** / error.

### 2.4 Render structure

- `<Popover>` with `<PopoverTrigger disableButtonEnhancement>` and `<PopoverSurface>`
- Surface inner: header row (Sparkle icon + label + copy button) → spinner OR error text OR (tldr + summary) blocks
- Surface dimensions: `width: '480px'; maxHeight: '400px'; overflowY: 'auto'`

For `InsightSummaryCard` the **inline card** wraps the same content with card chrome (header + KPI slot + body + footer). The popover affordance is replaced by `<Card>` chrome from Fluent v9. The Dialog/modal expand is **net-new** composition (not present anywhere in `Spaarke.UI.Components` per the grep below).

### 2.5 Reusable design primitives that carry over

| Primitive | Reuse | Net-new |
|---|---|---|
| Lazy-load callback (`onFetchSummary` shape) | ✅ | — |
| 4-state machine (idle/loading/loaded/error) | ✅ baseline | extend to 6 states |
| `Sparkle20Filled` icon for AI affordance | ✅ same icon family (we will likely use `Sparkle24Filled` for card-header per FR-04 `sprk_icon` evidence) | — |
| Copy-to-clipboard with 2-second confirmation | ✅ — apply to insight body | — |
| Fluent v9 semantic tokens for spacing/colors | ✅ ADR-021 mandate | — |
| Popover-as-surface composition | ❌ — `InsightSummaryCard` uses **Card** as primary surface; Popover may appear for KPI tooltips only | — |
| Inline-expand-to-modal (Dialog around card body) | — | ✅ **NET-NEW** (no extant pattern) |

### 2.6 Author-time gotchas to flag for Phase 3 task 030

1. **Fluent v9 portal re-wrap rule** — `Popover` and `Dialog` both render through portals. The `InsightSummaryCard` consumer (Matter form host) needs the portal target to inherit theme + auth context. See `.claude/patterns/ui/fluent-v9-portal-gotcha.md` — explicit rule per project CLAUDE.md "Applicable patterns" section.
2. **Card width inside form section** — `AiSummaryPopover` hard-codes `width: '480px'`. The card must be fluid (host form section width) with a `maxWidth` cap before modal expand kicks in.
3. **No service singleton** — like `AiSummaryPopover`, `InsightSummaryCard` MUST stay callback-based. The host (Matter form bootstrap) wires the BFF `IInsightsAi.AnswerQuestionAsync` call, the component is service-agnostic. ADR-010 (DI minimalism, no new interface seams in r1).

---

## 3. `@spaarke/ai-widgets` package inventory

**File**: `src/client/shared/Spaarke.AI.Widgets/package.json` (verified v0.1.0)

### 3.1 Dependencies — confirm r1-compatible

| Dependency | Version | Compatible with r1 needs |
|---|---|---|
| `@fluentui/react-components` (peer) | `^9.0.0` (dev `^9.73.2`) | ✅ Fluent v9 — required by ADR-021 |
| `@fluentui/react-icons` (peer) | `^2.0.0` (dev `^2.0.320`) | ✅ Sparkle icon family present |
| `react` (peer) | `^19.0.0` (dev `^19.2.6`) | ✅ React 19 per ADR-021 (code pages); Matter form host is MDA form JS so this is consumed only by the shared lib build, not the form host runtime |
| `@spaarke/ui-components` (workspace) | local | ✅ — gives `InsightSummaryCard` access to anything reusable from the older sibling lib |
| `@spaarke/ai-outputs` (workspace) | local | Not used by r1 (envelope is local to InsightSummaryCard) |

### 3.2 Existing exports (from `src/index.ts`, 669 lines)

Sample of relevant siblings — establishes the conventions an `InsightSummaryCard` will follow:

- `ConfidenceIndicator` (line 524) — small per-response control; sibling pattern for a non-workspace UI component in this package.
- `CitationBadge` (line 609) — Fluent v9 Badge for citation verification status; **possibly composable** as the citation chip rendered inside `InsightSummaryCard` when a citation is present.
- `GroundednessHighlight` (line 616) — text-segment annotation; r1 does NOT consume groundedness in the inline card surface, but the pattern shows how the package wraps Fluent v9 with semantic-token styling.
- `FeedbackButtons` (line 536) — **EXCLUDED from r1 reuse** per Q-U3 defer / spec FR-08 (see §5 below).
- Wizard launcher widgets (`CreateMatterWizardWidget`, `DocumentUploadWizardWidget`, etc.) — show the package's **workspace-widget** pattern. `InsightSummaryCard` is **NOT** a workspace widget — it is a per-record component embedded in a Matter form section. The closest sibling is `ConfidenceIndicator` (lightweight per-record control), not the workspace widgets.

### 3.3 Tooling readiness

| Tool | Status | Gap for Phase 3 task 030 |
|---|---|---|
| TypeScript build (`tsc`) | ✅ configured | — |
| Lint (`eslint --ext .ts,.tsx`) | ✅ configured | — |
| Jest tests (`jest`) | ✅ configured with `__tests__/` directory | — |
| **Storybook** | ❌ **NOT configured** — no `.storybook/` dir, no `*.stories.tsx` files, no Storybook deps in `package.json` | ⚠️ **Gap for FR-01 / SC-01 acceptance.** The sibling `Spaarke.UI.Components` package HAS Storybook (`src/client/shared/Spaarke.UI.Components/.storybook/`, 8 `.stories.tsx` files). Phase 3 task 030 must either: (a) add Storybook to `@spaarke/ai-widgets` (config + at least 5-state story for `InsightSummaryCard`); OR (b) per spec FR-01 wording "Storybook story **(or equivalent)**", deliver a dev sandbox playground (e.g., a route in the SpaarkeAi code page that renders all 6 states) that satisfies SC-01 without standing up Storybook in this package. |

**Recommendation for Phase 3 task 030**: pick option (b) for r1 — keep `@spaarke/ai-widgets` Storybook-less; ship an "equivalent" dev playground. Rationale: r1 is the first lightweight per-record component in this package; standing up Storybook for one component is yak-shaving and not required by SC-01 wording. r2+ can add Storybook when the package accumulates a second or third lightweight component.

---

## 4. Inline-expand-to-modal pattern — search results

### 4.1 Grep scope

```
src/client/shared/  (both Spaarke.UI.Components and Spaarke.AI.Widgets)
patterns: Dialog.*Popover, Popover.*Dialog, expand-to-modal, inline.*modal
```

### 4.2 Findings

| Pattern | Files matched | Notes |
|---|---|---|
| `Dialog.*Popover` / `Popover.*Dialog` | `Spaarke.AI.Widgets/src/index.ts` (export listings only, no composition); `Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` (header comments only — refers to a DIFFERENT pattern, `RichFilePreviewDialog`, which is an inline-vs-dialog **alternative**, not a Popover-to-Dialog escalation) | **No composition pattern.** |
| `expand-to-modal` / `inline.*modal` | `Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx` (welcome cards header, NOT card-to-modal escalation) | **No card-to-modal-on-expand pattern.** |

### 4.3 Conclusion

**Ratifies plan-time finding**: there is no extant inline-card-to-modal-Dialog escalation pattern in `Spaarke.UI.Components` or `Spaarke.AI.Widgets`. The closest neighbors:

- `AiSummaryPopover` — popover surface, no modal escalation
- `FilePreviewContextWidget` — has inline AND a separate `RichFilePreviewDialog` companion; the user picks one, they don't escalate from one to the other within the same control
- Wizard widgets — modal-only (Dialog surface from the start), not escalations

So `InsightSummaryCard`'s FR-02 modal-expand affordance is **net-new composition**: `<Card>` (inline) + `<Dialog>` (modal expand triggered by an explicit affordance, no auto-promote in r1 per assumption #12).

---

## 5. `FeedbackButtons` — confirmed NOT a r1 reuse candidate

Per spec FR-08 (deferred) + Q-U3 resolution + Q-U3-driven removal of `onFeedback` prop from FR-01.

`Spaarke.AI.Widgets/src/components/FeedbackButtons.tsx` exists (line 536 of index.ts), but r1's `InsightSummaryCard` does NOT import, render, or pass through to it. r2+ may revisit once AIPU2's Cosmos `feedback` container lands on master per ADR-015.

This note exists to make the exclusion explicit so a future reader does not "discover" `FeedbackButtons` and assume it was overlooked.

---

## 6. Proposed composition approach for `InsightSummaryCard`

**(Sketch only — actual implementation lives in Phase 3 task 030, NOT this task.)**

```
<Card>                                       Fluent v9 Card
  <CardHeader
    image={<Sparkle24Filled />}              FR-04 sprk_icon: 'Sparkle24Filled'
    header={<Text>{displayName}</Text>}      from sprk_aitopicregistry.sprk_displayname
    description={<StaleBadge />}             cache-freshness indicator (FR + stale state)
  />
  {kpiSlot}                                  consumer-provided KPI strip (optional)
  <CardBody>
    {state === 'idle' &&    <IdleAffordance onClick={fetch} />}
    {state === 'loading' && <Spinner />}
    {state === 'loaded' &&  <NarrativeWithCitations />}
    {state === 'decline' && <DeclineMessage />}
    {state === 'error' &&   <ErrorMessage />}
  </CardBody>
  <CardFooter>
    <Button onClick={openModalExpand}>Expand</Button>     FR-02 manual modal escalation
    <CopyButton />                                          AiSummaryPopover pattern reuse
  </CardFooter>
</Card>

<Dialog open={modalOpen} onOpenChange={...}>          Triggered only by explicit user click
  <DialogSurface>
    <DialogTitle>{displayName}</DialogTitle>
    <DialogBody>
      <NarrativeWithCitations />                       Same render, larger surface
    </DialogBody>
    <DialogActions>
      <Button onClick={closeModal}>Close</Button>
    </DialogActions>
  </DialogSurface>
</Dialog>
```

**Key composition decisions** (to be re-confirmed in Phase 3 task 030):

- `<Card>` + `<Dialog>` siblings under a single parent component. No `<Popover>` in the primary path — Popover was the right primitive for `AiSummaryPopover` (popover IS the surface), not for `InsightSummaryCard` (Card IS the surface).
- State machine extends `AiSummaryPopover`'s 4-state shape to 6 states; useReducer recommended for readability once `decline` + `stale` enter the mix.
- The `<Dialog>` re-renders the same body component (`<NarrativeWithCitations />`) — single source of truth for the rendered narrative, only the chrome differs.
- Modal trigger is **manual only** for r1 per assumption #12 (no character/line threshold auto-promote).

---

## 7. Phase 3 task 030 — punch list seeded by this investigation

(For the task that actually builds `InsightSummaryCard`.)

- [ ] Define `InsightEnvelope` type (richer than `ISummaryData`; includes narrative, tldr, citations[], decline reason, cache freshness, kpis[])
- [ ] Define `InsightSummaryCardProps` per FR-01 (topic, subject, mode, parameters, kpiSlot, onCitationClick) — explicitly **no** `onFeedback` (FR-08 deferred)
- [ ] Implement 6-state machine (idle/loading/loaded/decline/stale/error) — useReducer
- [ ] Compose `<Card>` (inline) + `<Dialog>` (modal expand) per §6 sketch
- [ ] Reuse Fluent v9 `Sparkle24Filled` icon family + `tokens.*` semantic tokens per ADR-021
- [ ] Reuse `AiSummaryPopover`'s lazy-load callback shape (`onFetchInsight: () => Promise<InsightEnvelope>`)
- [ ] Reuse `AiSummaryPopover`'s copy-to-clipboard + 2s confirmation pattern
- [ ] Apply `.claude/patterns/ui/fluent-v9-portal-gotcha.md` rule for `<Dialog>` mounting under the Matter form host
- [ ] **Storybook decision**: deliver dev sandbox playground in a SpaarkeAi code page route (no Storybook config added to `@spaarke/ai-widgets` for r1) — satisfies SC-01 "Storybook story (or equivalent)" clause
- [ ] Verify dark-mode token rendering (ADR-021 dark-mode parity)
- [ ] Export from `Spaarke.AI.Widgets/src/index.ts` alongside `ConfidenceIndicator` (sibling pattern for lightweight per-record components)

---

## 8. Conclusion

Empirical investigation **ratifies** every plan-time claim in spec.md Resolution Decisions row "FR-03":

1. ✅ `InsightSummaryCard` belongs in `@spaarke/ai-widgets` (package exists at v0.1.0, has the right peer deps, sibling pattern via `ConfidenceIndicator`).
2. ✅ `AiSummaryPopover` IS the closest extant pattern, but composes Popover (not Card + Dialog).
3. ✅ No extant inline-expand-to-modal pattern. Card + Dialog composition is net-new.
4. ✅ `FeedbackButtons` is correctly excluded per Q-U3.

**No divergence — no owner escalation required.** Proceed to DR-001 ratified.

---

*Investigation complete. Decision recorded in [`../decisions/DR-001-component-reuse.md`](../decisions/DR-001-component-reuse.md).*
