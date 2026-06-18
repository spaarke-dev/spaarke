# InsightSummaryCard — Accessibility Audit (WCAG 2.1 AA per NFR-04)

> **Task**: 036 (Wave 3c, parallel with 037)
> **Date**: 2026-06-11
> **Rigor**: STANDARD (per POML)
> **Component audited**: `src/client/shared/Spaarke.AI.Widgets/src/components/InsightSummaryCard/InsightSummaryCard.tsx`
> **Sandbox surface**: `src/client/shared/Spaarke.AI.Widgets/src/components/InsightSummaryCard/InsightSummaryCardSandbox.tsx`
> **States covered**: idle / loading / loaded / error / decline / stale (6 per FR-06)
> **Author**: Claude task-execute sub-agent

---

## Method

**Static code review** against the WCAG 2.1 AA checklist supplied in the task brief, plus keyboard-nav verification by code inspection of ARIA roles, focus management, Escape handlers, and Tab order in the Popover + Dialog wrappers.

**Why static, not live axe**: sub-agent boundary forbids running browser tooling; Storybook is not installed in `@spaarke/ai-widgets` (DR-001 §Negative — sandbox stands in for Storybook). Live axe DevTools + screen-reader smoke tests require operator action — listed under "Operator follow-up" below.

**Code citations** use `InsightSummaryCard.tsx:NNN` line refs from the file at HEAD of `work/ai-spaarke-insights-engine-widgets-r1` as of 2026-06-11.

---

## Checklist findings

| # | Check | Code evidence | Status |
|---|---|---|---|
| 1 | Trigger button has accessible name | `InsightSummaryCard.tsx:608-614` — `<Button icon={<Sparkle20Regular />}>{triggerText}</Button>` with visible text "View Insight" (default) | PASS |
| 2 | Loading skeleton has `aria-live` | `InsightSummaryCard.tsx:233` — `<div className={styles.skeleton} aria-live="polite" aria-busy="true">` + Spinner label | PASS |
| 3 | Error message announced | `InsightSummaryCard.tsx:243` — `<div className={styles.errorBlock} role="alert">` | PASS |
| 4 | Decline message announced | `InsightSummaryCard.tsx:254-263` — `<div className={styles.declineBlock}>` — **NO `role` or `aria-live`** | ISSUE-1 |
| 5 | Stale banner announced | `InsightSummaryCard.tsx:272` — `<div className={styles.staleBanner} role="status">` | PASS |
| 6 | Citations are keyboard-focusable | `InsightSummaryCard.tsx:152-160` (assessment) + `:179-188` (document) — Fluent `<Link>` natively focusable; Enter activates | PASS |
| 7 | Unknown-citation fallback NOT focusable | `InsightSummaryCard.tsx:196-204` — plain `<span>` with no `tabIndex`, correctly inert (no action available) | PASS |
| 8 | Popover Escape close | Fluent v9 `Popover` honours Escape by default; no `closeOnEscape={false}` override anywhere | PASS |
| 9 | Dialog Escape close | Fluent v9 `Dialog` honours Escape by default; no override | PASS |
| 10 | Color contrast (semantic tokens) | All colors via `tokens.*` (`useInsightSummaryCardStyles.ts`); Fluent v9 web theme set is AA-compliant by token contract | PASS (delegated) |
| 11 | Focus return to trigger on Popover close | Fluent v9 `Popover` manages focus return to `PopoverTrigger` by default | PASS |
| 12 | Sparkle icon hidden from AT | `InsightSummaryCard.tsx:596,627` — `<Sparkle20Regular aria-hidden="true" />` | PASS |
| 13 | Refresh button has accessible name | `InsightSummaryCard.tsx:638-647` + `:702-710` — `aria-label="Refresh insight"` + visible "Refresh" text on both Popover footer and Dialog actions | PASS |
| 14 | Dialog has accessible name | `InsightSummaryCard.tsx:693` — `<DialogTitle>{topicLabel}</DialogTitle>`; Fluent v9 auto-wires `aria-labelledby` | PASS |
| 15 | Tab order logical | DOM order: Trigger → (on open) Popover header → body content → citation links → Refresh → Expand → (on Expand click) Dialog title → body → citation links → Refresh → Close. Linear, matches visual order, no `tabIndex>0` anti-pattern. | PASS |
| 16 | Idle affordance copy | `InsightSummaryCard.tsx:226-228` — `<Text>Click to load insight</Text>` is informational, NOT a button; fetch fires on Popover open (`handlePopoverOpenChange` `:533-540`). Text is misleading but not a WCAG fail. | ISSUE-2 (minor copy) |

---

## Critical issues

**Count: 1** (ISSUE-1 below). Not a blocker for Phase 3 sign-off, but should be addressed before production deploy.

### ISSUE-1 — `decline` state lacks AT announcement (WCAG 4.1.3 Status Messages, AA)

**Where**: `InsightSummaryCard.tsx:254-263`

```tsx
case 'decline':
  return (
    <div className={styles.declineBlock}>
      <Text>{state.message}</Text>
      {state.recommendedAction && (
        <Text className={styles.declineRecommendation}>
          {state.recommendedAction}
        </Text>
      )}
    </div>
  );
```

**Problem**: When the card transitions `loading` → `decline` (e.g., the BFF returns "Insufficient evidence" per FR-06), AT users get no announcement. The `error` state correctly uses `role="alert"` (line 243), and the `stale` banner uses `role="status"` (line 272). Decline is semantically a status message (informational, not urgent) so it should mirror `stale`.

**Suggested fix** (NOT applied — this audit is read-only per sub-agent boundary):

```tsx
<div className={styles.declineBlock} role="status" aria-live="polite">
```

**Severity**: WCAG 2.1 AA — Success Criterion 4.1.3 Status Messages.
**Owner**: Phase 3 follow-up task (suggest naming `036.1-fix-decline-announcement`) OR fold into Task 037 dark-mode pass if scope allows.

### ISSUE-2 — `idle` affordance copy is misleading (minor UX, not WCAG)

**Where**: `InsightSummaryCard.tsx:226-228`

"Click to load insight" suggests user action, but `handlePopoverOpenChange` (`:533-540`) auto-fires the fetch when the Popover opens. By the time the user reads the text, the fetch is in flight. Recommend changing to "Insight not yet loaded" or removing the idle branch (let Popover render empty briefly until `loading` kicks in).

**Severity**: Copy / UX nit. NOT a WCAG failure.
**Owner**: Phase 3 polish — non-blocking.

---

## Acceptance criteria (POML `<acceptance-criteria>`) — assessment

| Criterion | Result | Notes |
|---|---|---|
| Zero axe violations across all stories | UNVERIFIED via static audit | Live axe DevTools run required (operator). Static review surfaced 1 likely violation (ISSUE-1). |
| Tab order is logical | PASS via code inspection | No `tabIndex>0` anti-patterns; DOM order matches visual order. Live verification still recommended. |
| Escape closes Popover and Dialog | PASS via code inspection | Fluent v9 defaults honoured; no `closeOnEscape={false}` overrides. Live verification still recommended. |

---

## Operator follow-up (cannot be performed by sub-agent)

1. **Live axe DevTools run** (per task POML step 1): Open `InsightSummaryCardSandbox` in a host (e.g., a dev shell page), run axe DevTools on each of the 6 state cards. Confirm ISSUE-1 (decline state) is flagged. Capture any additional violations not visible in static review (e.g., insufficient contrast on actual rendered tokens at the operator's zoom level).
2. **Keyboard nav smoke test** (per task POML step 3): Tab through the page from before the trigger → into the Popover → through the citation links → into the Refresh / Expand buttons → into the Dialog → out via Escape. Verify focus return lands on the trigger button (NOT the body) after Escape.
3. **Screen-reader smoke test** (per task POML step 4): NVDA or Windows Narrator on at least the `loaded` and `decline` state cards. Confirm:
   - `loaded`: tldr + narrative + citations are read in order; citation links announce as "link" with their `label`.
   - `decline`: message is announced when the card transitions from loading. **If silent → ISSUE-1 confirmed.**
4. **Fix ISSUE-1** if confirmed (one-line edit to add `role="status" aria-live="polite"` to the decline block).

---

## Blockers

**None for the audit itself**. ISSUE-1 is a non-blocking finding suitable for a Phase 3 polish follow-up. The component is structurally sound: 14 / 16 checklist items pass on static review; the 2 issues found are 1 minor WCAG 4.1.3 (decline announcement) + 1 UX copy nit (idle text).

Phase 3 (`InsightSummaryCard` component) can proceed to Phase 4 (Matter form integration) provided the operator confirms ISSUE-1 is filed as a follow-up and ISSUE-2 is acknowledged as non-blocking.

---

*Audit performed under sub-agent permission boundary (writes restricted to `projects/.../notes/handoffs/`). All code observations are from HEAD of `work/ai-spaarke-insights-engine-widgets-r1` as of 2026-06-11.*
