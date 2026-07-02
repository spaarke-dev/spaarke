/**
 * ComposeEmptyState.tsx — default-open empty state for the Compose surface
 *
 * Project:   spaarkeai-compose-r1
 * Task:      044 — Frontend: create `ComposeEmptyState.tsx` (Browse + Search options)
 * Phase:     Phase 4: Frontend — SpaarkeAi Compose Surface
 *
 * Renders the empty pane shown when Compose is mounted WITHOUT a document
 * context (no Path A modal launch, no Path B upload yet). The pane surfaces
 * two affordances per the locked R1 decision in `design.md` §14 row 5 +
 * `projects/spaarkeai-compose-r1/CLAUDE.md` "Decisions Made":
 *
 *   1. **Browse / open file** — opens the SPE picker (Path B: ephemeral upload).
 *      Receiver / picker UI is OUT OF SCOPE for R1 of this task; the callback
 *      fires so a parent (ComposeWorkspace, task 042) can route the action.
 *
 *   2. **Search for Document** — opens the existing Spaarke Document search
 *      flow (Path A: pick an existing `sprk_document` record). Receiver is the
 *      existing SemanticSearch surface; the callback fires so the parent can
 *      route the action.
 *
 * **Architectural intent**: this component is STATELESS PRESENTATION. It owns
 * NO event-bus wiring, NO modal lifecycle, NO auth, NO data fetching. The two
 * actions are exposed as callback props (`onBrowseRequested`, `onSearchRequested`)
 * so the parent — `ComposeWorkspace.tsx` (task 042) — chooses how to wire them
 * (PaneEventBus dispatch in the standard production wiring; direct picker
 * invocation in tests; both in the smoke test).
 *
 * **Why callbacks and not direct PaneEventBus dispatch here**: per dispatch
 * instructions ("ComposeEmptyState is a stateless presentation component —
 * receives callbacks via props for the two actions") + CLAUDE.md §11 component
 * justification (smallest reasonable surface). The POML §steps[2,3] reference
 * "dispatch a `compose:browse-requested` PaneEventBus event" — that dispatch
 * lives in the parent so this component does not couple to a specific event
 * shape and can be reused (R2: a modal-launch variant; tests: a no-op variant).
 *
 * **Constraints honoured**:
 *   - ADR-021: Fluent v9 only; `makeStyles` + `tokens.*` (semantic) — dark mode
 *     works automatically without additional CSS. No hardcoded colors.
 *   - ADR-022: React 19 (component returns `React.JSX.Element`).
 *   - ADR-028: No auth surface — defer to `@spaarke/auth` only if a downstream
 *     receiver requires elevated scope.
 *   - CLAUDE.md §10 #6: tests obligation — see component-tests in
 *     `src/solutions/SpaarkeAi/src/components/compose/__tests__/` (added by
 *     the parallel work in tasks 042–046 if/where needed; this component is
 *     pure presentation + callbacks so testing is a minimal render + click
 *     assertion).
 *   - CLAUDE.md §11 component justification:
 *       Existing — three empty-state components inspected (DailyBriefing,
 *         SemanticSearch, LegalWorkspace ActivityFeed/NotificationPanel); each
 *         is surface-specific (success message, no-results message, list-empty
 *         message) with NO two-CTA layout. No reusable shared
 *         `<EmptyStateWithCTAs />` exists.
 *       Extension — cannot extend the above; they encode their own message
 *         semantics + iconography. A generic two-CTA primitive would need to
 *         be HOISTED to `@spaarke/ui-components` first; that hoist is a
 *         separate deferral, not a Phase 4 task.
 *       Cost-of-doing-nothing — FR-18 fails: users opening Compose without a
 *         document context see a blank pane; Path A search picker is
 *         unreachable; Path B upload is unreachable. Both are locked R1
 *         decisions.
 *
 * @see projects/spaarkeai-compose-r1/design.md §14 row 5 (locked decision)
 * @see projects/spaarkeai-compose-r1/spec.md FR-18 (two-option empty state)
 * @see projects/spaarkeai-compose-r1/CLAUDE.md "Decisions Made" (R1 default-open)
 * @see WelcomePanel.tsx — sibling Fluent v9 / token / dark-mode pattern
 * @see src/solutions/SpaarkeAi/src/types/compose-contracts.ts — flow contracts (task 041)
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  Text,
} from '@fluentui/react-components';
import {
  DocumentArrowUpRegular,
  SearchRegular,
} from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Outer container fills the Compose pane vertically + horizontally and centres
  // the card. Uses semantic tokens so dark mode works without additional CSS.
  root: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
    height: '100%',
    minHeight: '320px',
    paddingTop: tokens.spacingVerticalXXXL,
    paddingBottom: tokens.spacingVerticalXXXL,
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    width: '100%',
    maxWidth: '480px',
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  heading: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    textAlign: 'center',
    margin: 0,
  },
  description: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    maxWidth: '380px',
  },
  // Side-by-side CTA row; collapses to stacked on narrow viewports via
  // flex-wrap so the two buttons remain reachable and tappable.
  actions: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'center',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    width: '100%',
    marginTop: tokens.spacingVerticalM,
  },
  cta: {
    // Each button gets a minimum hit target so the layout is keyboard- and
    // touch-friendly. Fluent's Button already handles focus rings via tokens.
    minWidth: '180px',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * Props for `<ComposeEmptyState>`.
 *
 * Both callbacks are REQUIRED — the empty state's purpose is to surface these
 * two affordances; omitting either is a misuse and rendering with both
 * disabled is the intended fallback when a parent has not yet finished
 * bootstrapping its receivers.
 */
export interface ComposeEmptyStateProps {
  /**
   * Invoked when the user clicks the "Browse / open file" CTA. The parent
   * routes this to:
   *   - R1 standard: PaneEventBus dispatch on the `workspace` channel with
   *     an additive `compose_browse_requested`-style event (consumer wires the
   *     SPE picker open + ephemeral upload via Path B).
   *   - R2+: the same dispatch with full SPE picker integration.
   *   - Test harness: a `jest.fn()` (or equivalent) to assert click intent.
   */
  onBrowseRequested: () => void;

  /**
   * Invoked when the user clicks the "Search for Document" CTA. The parent
   * routes this to:
   *   - R1 standard: PaneEventBus dispatch on the `workspace` channel with
   *     an additive `compose_search_requested`-style event (consumer wires the
   *     existing Spaarke Document semantic search picker — Path A).
   *   - R2+: the same dispatch with full picker + result-binding flow.
   *   - Test harness: a `jest.fn()` (or equivalent) to assert click intent.
   */
  onSearchRequested: () => void;

  /**
   * When `true`, both CTAs render as disabled (focusable but not actionable).
   * Use when the parent is mid-bootstrap (e.g. auth handshake) and cannot yet
   * route the actions safely. Defaults to `false`.
   */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * `<ComposeEmptyState>` — centred Fluent v9 card with Browse + Search CTAs.
 *
 * Rendered by `ComposeWorkspace` (task 042) when no document context is
 * present. The component itself is stateless presentation; the parent is the
 * authority on what each click does.
 *
 * Accessibility:
 *  - The outer container carries `role="region"` + `aria-label` so screen
 *    readers announce the empty state.
 *  - Both CTAs are native Fluent `<Button>` elements — keyboard navigation
 *    (Tab + Enter/Space) works out of the box, focus rings come from Fluent
 *    tokens, and dark mode adapts automatically.
 *  - Each button carries an `aria-label` echoing its visible text + intent so
 *    the affordance reads unambiguously in assistive contexts.
 */
export function ComposeEmptyState({
  onBrowseRequested,
  onSearchRequested,
  disabled = false,
}: ComposeEmptyStateProps): React.JSX.Element {
  const styles = useStyles();

  return (
    <div
      className={styles.root}
      role="region"
      aria-label="Compose — no document open"
      data-testid="compose-empty-state"
    >
      <Card className={styles.card} appearance="subtle">
        <Text as="h2" size={500} className={styles.heading}>
          Open a document to start composing
        </Text>
        <Text size={300} className={styles.description}>
          Browse and upload a file to draft from, or search for an existing
          Spaarke document.
        </Text>

        <div className={styles.actions} role="group" aria-label="Open document options">
          <Button
            appearance="primary"
            icon={<DocumentArrowUpRegular />}
            disabled={disabled}
            onClick={onBrowseRequested}
            className={styles.cta}
            aria-label="Browse or open a file to compose"
            data-testid="compose-empty-state-browse"
          >
            Browse / open file
          </Button>
          <Button
            appearance="outline"
            icon={<SearchRegular />}
            disabled={disabled}
            onClick={onSearchRequested}
            className={styles.cta}
            aria-label="Search for an existing Spaarke document"
            data-testid="compose-empty-state-search"
          >
            Search for Document
          </Button>
        </div>
      </Card>
    </div>
  );
}

export default ComposeEmptyState;
