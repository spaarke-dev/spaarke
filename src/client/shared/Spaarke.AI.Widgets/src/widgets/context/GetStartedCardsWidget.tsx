/**
 * @spaarke/ai-widgets — GetStartedCardsWidget
 *
 * Context-pane welcome-state widget rendering the 7 "Get Started" action cards
 * (FR-18 of project spaarke-ai-platform-unification-r3). Replaces the
 * PlaybookGalleryWidget on the welcome stage of the Context pane.
 *
 * Layout
 * ======
 * - Responsive CSS grid: `gridTemplateColumns: repeat(auto-fill, minmax(160px, 1fr))`,
 *   `gap: spacingHorizontalS` (task 103 — Round 7 operator feedback 2026-05-22).
 *   At narrow Context-pane widths the grid resolves to 2 columns (preserves the
 *   prior 2-col behavior at the typical ~400px pane width). At wider pane
 *   widths (when Assistant + Workspace are both collapsed per task 100) the
 *   grid auto-fills more columns — 3, 4, 5 cards per row depending on the
 *   available width. Each card has a minimum width of 160px so cards don't
 *   shrink below a usable touch target. The 160px minimum was chosen to match
 *   the visual size of the previous fixed 2-column layout at ~400px pane width.
 * - Vertical scroll on overflow: `overflowY: 'auto'` — height is constrained by
 *   the parent pane, not by this widget (NFR-05 + spec constraint).
 *
 * Cards (FR-19 mapping)
 * =====================
 *   Create Matter     → 'create-matter-wizard'    (dispatched as widget_load)
 *   Create Project    → 'create-project-wizard'   (dispatched as widget_load)
 *   Assign Work       → 'assign-work'             (launcher — handled by task 042)
 *   Summarize Files   → 'document-upload-wizard'  (dispatched as widget_load)
 *   Find Similar      → 'find-similar-wizard'     (dispatched as widget_load)
 *   Send Email        → 'email-compose'           (dispatched as widget_load)
 *   Schedule Meeting  → 'meeting-schedule'        (dispatched as widget_load)
 *
 * This widget does NOT dispatch PaneEventBus events itself. It accepts an
 * `onCardClick(widgetType)` callback prop and task 042 (Wave 3e) wires that
 * callback to the `widget_load` dispatcher on the `workspace` channel + special-
 * cases the `assign-work` card to call `launchAssignWorkWizard()` instead. This
 * separation keeps the component pure (renderable in isolation, in tests, in
 * Storybook) and lets task 042 own the cross-pane wiring concern.
 *
 * ActionCard import (per task 012 STAY decision — see
 * `projects/spaarke-ai-platform-unification-r3/notes/drafts/012-actioncard-decision.md`)
 * =====================================================================
 * `ActionCard` is consumed from the existing shared library at
 * `@spaarke/ui-components` (provenance: `WorkspaceShell/ActionCard.tsx`). Task
 * 012 verified that the LegalWorkspace local copy and the shared copy are
 * functionally equivalent and that a new lift would duplicate the existing
 * component. The LegalWorkspace local copy is preserved unchanged for FR-25
 * / NFR-10 (standalone LegalWorkspace must keep functioning identically).
 *
 * Constraints applied
 * ===================
 * - ADR-012: Component lives in `@spaarke/ai-widgets` (shared lib). Reuses the
 *   shared `ActionCard` primitive — no copy-paste, no LegalWorkspace imports.
 * - ADR-021: Fluent v9 tokens only. Grid `gap` is `tokens.spacingHorizontalS`;
 *   container padding is `tokens.spacingHorizontalM`; no hex / rgba literals.
 *   ActionCard itself handles all card colors via tokens (dark-mode safe).
 * - ADR-022: React 19. Uses `makeStyles` from `@fluentui/react-components`.
 * - ADR-025: All 7 card icons sourced from `@fluentui/react-icons` v9.
 * - ADR-028: No `accessToken` props or state. This widget does NOT make BFF
 *   calls — it only invokes the `onCardClick` callback. Any future BFF
 *   interaction would go through `authenticatedFetch`.
 * - NFR-05: Keyboard-navigable. The shared `ActionCard` already supplies
 *   `tabIndex=0`, `role="button"`, and Enter/Space activation. We add arrow-
 *   key navigation at the grid container level (per FR-18 + spec criterion).
 * - Spec: 2-column grid, vertical scroll on overflow, no hard-coded height.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: 041 (FR-18 — Get Started cards)
 *
 * @see ADR-012  — Shared component library (consume, not copy)
 * @see ADR-021  — Fluent UI v9 tokens only
 * @see ADR-025  — @fluentui/react-icons v9
 * @see ADR-028  — No token snapshots in props/state
 * @see projects/spaarke-ai-platform-unification-r3/notes/drafts/012-actioncard-decision.md
 * @see projects/spaarke-ai-platform-unification-r3/tasks/041-getstartedcards-widget.poml
 * @see projects/spaarke-ai-platform-unification-r3/tasks/042-register-widget-stage-swap.poml (wiring)
 */

import React, { useCallback, useMemo, useRef } from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import {
  DocumentAddRegular,
  FolderAddRegular,
  PeopleAddRegular,
  DocumentMultipleRegular,
  SearchRegular,
  MailRegular,
  CalendarLtrRegular,
} from '@fluentui/react-icons';
import type { FluentIcon } from '@fluentui/react-icons';

import { ActionCard } from '@spaarke/ui-components';
import type { ActionCardProps } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * String-literal type for the 7 Get Started card identifiers. Matches the
 * FR-19 widget-type mapping. Exported so task 042 (wiring) can type its
 * dispatcher and so consumers can switch on these values exhaustively.
 *
 * NOTE: Six of these values are dispatched as `widget_load` PaneEventBus
 * events on the `workspace` channel by task 042. The seventh — `'assign-work'`
 * — is special-cased by task 042 to call `launchAssignWorkWizard()` (it crosses
 * the host boundary into Dataverse via Xrm.Navigation.navigateTo instead of
 * opening an in-app workspace tab).
 */
export type GetStartedCardId =
  | 'create-matter-wizard'
  | 'create-project-wizard'
  | 'assign-work'
  | 'document-upload-wizard'
  | 'find-similar-wizard'
  | 'email-compose'
  | 'meeting-schedule';

/**
 * Props for {@link GetStartedCardsWidget}.
 */
export interface GetStartedCardsWidgetProps {
  /**
   * Called when a card is activated (click, Enter, or Space). Receives the
   * card's widget-type identifier. Task 042 wires this to the PaneEventBus
   * `widget_load` dispatcher (with `assign-work` special-cased to the
   * launcher). Defaults to a no-op so the widget renders cleanly in tests +
   * Storybook without a callback.
   */
  onCardClick?: (cardId: GetStartedCardId) => void;

  /** Optional class name applied to the grid container. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Internal: card definitions
// ---------------------------------------------------------------------------

/**
 * Internal shape used to render each card. Not exported — consumers shouldn't
 * need to construct one. The widget renders a fixed list of 7 cards per FR-18.
 */
interface CardDefinition {
  /** Card identifier — drives the `onCardClick` callback argument. */
  id: GetStartedCardId;
  /** Card label rendered below the icon. */
  label: string;
  /**
   * Verbose description of the action this card initiates. Forms the basis of
   * the card's `aria-label` so screen reader users hear "Create Matter — start
   * a new matter workspace" rather than just "Create Matter".
   */
  description: string;
  /** Fluent v9 icon component. ADR-025 — sourced from @fluentui/react-icons. */
  icon: FluentIcon;
}

/**
 * The 7 Get Started cards in display order (FR-18 + FR-19 mapping). Frozen at
 * module load so the array reference is stable across renders.
 */
const CARDS: readonly CardDefinition[] = Object.freeze([
  {
    id: 'create-matter-wizard',
    label: 'Create Matter',
    description: 'Start a new matter workspace.',
    icon: DocumentAddRegular,
  },
  {
    id: 'create-project-wizard',
    label: 'Create Project',
    description: 'Start a new project workspace.',
    icon: FolderAddRegular,
  },
  {
    id: 'assign-work',
    label: 'Assign Work',
    description: 'Open the Create Work Assignment wizard.',
    icon: PeopleAddRegular,
  },
  {
    id: 'document-upload-wizard',
    label: 'Summarize Files',
    description: 'Upload one or more documents and generate an AI summary.',
    icon: DocumentMultipleRegular,
  },
  {
    id: 'find-similar-wizard',
    label: 'Find Similar',
    description: 'Find documents similar to a chosen example.',
    icon: SearchRegular,
  },
  {
    id: 'email-compose',
    label: 'Send Email',
    description: 'Open the Analysis Builder to compose an email.',
    icon: MailRegular,
  },
  {
    id: 'meeting-schedule',
    label: 'Schedule Meeting',
    description: 'Open the Analysis Builder to schedule a meeting.',
    icon: CalendarLtrRegular,
  },
]);

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Grid container — responsive auto-fill layout (task 103, Round 7, 2026-05-22).
   *
   * - `gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))'`: at narrow
   *   pane widths (~400px) the grid resolves to 2 equal-width columns
   *   (preserving the pre-task-103 visual rhythm). At wider pane widths (when
   *   Assistant + Workspace are both collapsed via task 100, freeing the
   *   Context pane to the full main-area width), the grid auto-fills more
   *   columns — 3, 4, 5 cards per row depending on available width. The 160px
   *   minimum prevents cards from shrinking below a usable touch target and
   *   matches the visual card size of the previous fixed 2-column layout at
   *   the default Context-pane width.
   * - `gap: tokens.spacingHorizontalS`: per the spec constraint.
   * - `overflowY: 'auto'`: vertical scroll when content exceeds the pane.
   *   Height is bounded by the parent (`100%`) — we do NOT hard-cap a pixel
   *   height here, so a tall pane shows all 7 cards without a scrollbar and
   *   a short pane scrolls naturally.
   * - `padding: tokens.spacingHorizontalM`: small breathing room around the
   *   grid so cards don't touch the pane chrome.
   * - `boxSizing: 'border-box'`: padding counts against the 100% height so
   *   the scrollbar appears inside the pane's borders, not outside them.
   * - `alignContent: 'start'`: when there's extra vertical space (tall pane),
   *   cards stick to the top instead of stretching to fill the container.
   * - Focus styling is handled per-card by `ActionCard` (`:focus-visible`
   *   ring on the card itself). The grid container itself has no focus ring.
   */
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingHorizontalM,
    boxSizing: 'border-box',
    width: '100%',
    height: '100%',
    minHeight: 0,
    overflowY: 'auto',
    alignContent: 'start',
  },
});

// ---------------------------------------------------------------------------
// Keyboard navigation helpers
// ---------------------------------------------------------------------------

/**
 * Move keyboard focus across the 7-card grid in response to arrow keys.
 *
 * The grid is row-major in source order; with the task-103 responsive grid
 * (`repeat(auto-fill, minmax(160px, 1fr))`) the column count varies with pane
 * width. We compute the current column count at runtime by measuring the
 * card row positions from the DOM (`getBoundingClientRect().top` for each
 * `[data-card-index]` wrapper — cards in the same row share the same top).
 * That keeps arrow navigation correct at any column count without coupling
 * to a fixed layout assumption.
 *
 *   - ArrowLeft  → index - 1
 *   - ArrowRight → index + 1
 *   - ArrowUp    → index - <columnsPerRow> (move up one row)
 *   - ArrowDown  → index + <columnsPerRow> (move down one row)
 *
 * Out-of-range indices are clamped to [0, length - 1] (no wrap-around — that
 * surprises screen-reader users).
 *
 * The function reads focusable card elements from the container via
 * `[data-card-index]` so it doesn't need to know the React tree shape.
 */
function getColumnsPerRow(container: HTMLDivElement): number {
  // Walk the rendered cards and count how many share the smallest `top`
  // coordinate — that's the column count in the first row. A 2-col layout
  // produces 2 cards at the same top; a 4-col layout produces 4. We use the
  // FIRST row because some browsers can return slightly different `top`
  // values for the last (possibly partial) row.
  const cards = container.querySelectorAll<HTMLElement>('[data-card-index]');
  if (cards.length === 0) return 1;
  const firstTop = cards[0].getBoundingClientRect().top;
  let count = 0;
  for (const card of cards) {
    // Allow a 1px tolerance to absorb sub-pixel rounding differences.
    if (Math.abs(card.getBoundingClientRect().top - firstTop) <= 1) {
      count += 1;
    } else {
      break; // Different row → stop counting; we're done with row 1.
    }
  }
  return Math.max(count, 1);
}

function moveFocus(container: HTMLDivElement, fromIndex: number, direction: 'left' | 'right' | 'up' | 'down'): void {
  const total = CARDS.length;
  const columnsPerRow = getColumnsPerRow(container);
  let next: number;
  switch (direction) {
    case 'left':
      next = fromIndex - 1;
      break;
    case 'right':
      next = fromIndex + 1;
      break;
    case 'up':
      next = fromIndex - columnsPerRow;
      break;
    case 'down':
      next = fromIndex + columnsPerRow;
      break;
  }
  if (next < 0 || next >= total) {
    return; // Clamp — no wrap.
  }
  const wrapper = container.querySelector<HTMLElement>(`[data-card-index="${next}"]`);
  if (!wrapper) return;
  // Focus the inner ActionCard (role="button", tabIndex=0), NOT the wrapper.
  // ActionCard is the element that handles Enter/Space activation; focusing
  // the wrapper would let arrow nav work but break keyboard activation.
  const actionCard = wrapper.querySelector<HTMLElement>('[role="button"]');
  if (actionCard) {
    actionCard.focus();
  } else {
    wrapper.focus();
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * GetStartedCardsWidget — 7-card welcome-state widget for the Context pane.
 *
 * Renders a 2-column CSS grid of {@link ActionCard} instances. Each card,
 * when activated (click / Enter / Space), invokes `props.onCardClick` with
 * the card's {@link GetStartedCardId}. Arrow keys move focus between cards.
 *
 * @example
 * ```tsx
 * import { GetStartedCardsWidget } from '@spaarke/ai-widgets';
 *
 * <GetStartedCardsWidget
 *   onCardClick={(cardId) => {
 *     if (cardId === 'assign-work') {
 *       launchAssignWorkWizard({ bffBaseUrl: getBffBaseUrl() });
 *     } else {
 *       dispatch('workspace', { type: 'widget_load', widgetType: cardId });
 *     }
 *   }}
 * />
 * ```
 */
export const GetStartedCardsWidget: React.FC<GetStartedCardsWidgetProps> = ({ onCardClick, className }) => {
  const styles = useStyles();
  const gridRef = useRef<HTMLDivElement>(null);

  /**
   * Stable per-card click handler. We pre-build the array so each ActionCard
   * gets the same callback reference across renders (avoids the inline-arrow
   * re-render churn pattern). The closure captures `onCardClick` from props;
   * if `onCardClick` changes between renders, the handlers re-bind — that's
   * intentional and rare.
   */
  const cardHandlers = useMemo(
    () =>
      CARDS.map(card => () => {
        onCardClick?.(card.id);
      }),
    [onCardClick]
  );

  /**
   * Single keydown handler at the grid container. Each card has
   * `data-card-index={i}` on its root element; the handler reads that index
   * from `event.target` and dispatches an arrow-key focus move. Enter/Space
   * are NOT handled here — the shared ActionCard already activates onClick
   * on those keys, and intercepting them at the container level would risk
   * double-firing.
   */
  const handleGridKeyDown = useCallback((event: React.KeyboardEvent<HTMLDivElement>) => {
    const container = gridRef.current;
    if (!container) return;

    // Identify which card currently has focus by walking up to the nearest
    // [data-card-index] element. We can't rely on event.target being the
    // card root because ActionCard renders a div containing icon + label.
    const target = event.target as HTMLElement | null;
    const cardEl = target?.closest<HTMLElement>('[data-card-index]');
    if (!cardEl) return;
    const indexAttr = cardEl.getAttribute('data-card-index');
    if (indexAttr === null) return;
    const fromIndex = Number.parseInt(indexAttr, 10);
    if (Number.isNaN(fromIndex)) return;

    switch (event.key) {
      case 'ArrowLeft':
        event.preventDefault();
        moveFocus(container, fromIndex, 'left');
        break;
      case 'ArrowRight':
        event.preventDefault();
        moveFocus(container, fromIndex, 'right');
        break;
      case 'ArrowUp':
        event.preventDefault();
        moveFocus(container, fromIndex, 'up');
        break;
      case 'ArrowDown':
        event.preventDefault();
        moveFocus(container, fromIndex, 'down');
        break;
      default:
        // Other keys (Tab, Enter, Space, etc.) — defer to default behavior /
        // ActionCard's own keydown handler. Do NOT preventDefault.
        break;
    }
  }, []);

  return (
    <div
      ref={gridRef}
      className={mergeClasses(styles.grid, className)}
      role="grid"
      aria-label="Get Started actions"
      onKeyDown={handleGridKeyDown}
      data-testid="getstartedcards-widget"
    >
      {CARDS.map((card, i) => (
        // Wrapper div carries the `data-card-index` attribute used by the
        // arrow-key focus handler. We do NOT pass `tabIndex` or `role` to the
        // wrapper — those live on ActionCard itself (`role="button"`,
        // `tabIndex={0}`). The wrapper exists purely as a focus-tracking anchor
        // since ActionCard doesn't accept arbitrary `data-*` props as part of
        // its public ActionCardProps API.
        <div
          key={card.id}
          data-card-index={i}
          // Subtree role for screen readers: each cell announces as a grid cell.
          role="gridcell"
          // Make the wrapper itself focusable so `target.closest('[data-card-index]')`
          // resolves even when ActionCard hasn't yet propagated focus into the
          // wrapper. In practice ActionCard's tabIndex=0 wins for Tab focus, but
          // the wrapper participates in `closest()` lookups during keyboard nav.
          tabIndex={-1}
        >
          <ActionCard
            // Type assertion here is intentional and load-bearing.
            //
            // The shared @spaarke/ui-components `ActionCard` is type-emitted
            // against its own workspace's `@fluentui/react-icons` (v2.0.320).
            // This Spaarke.AI.Widgets package has its own pinned copy
            // (v2.0.326) per its devDependencies. The two `FluentIcon` types
            // are structurally near-identical but nominally distinct because
            // they sit at different physical paths in node_modules.
            //
            // The values themselves are interchangeable at runtime — Fluent
            // icons are plain React components with the same shape. TypeScript
            // doesn't know that, so we narrow via `as` here rather than
            // forcing both packages to share a hoisted icon dependency (which
            // would couple our build to a workspace-level package manager
            // contract we don't otherwise rely on).
            //
            // ADR-025 is still satisfied: the icon ORIGIN is
            // `@fluentui/react-icons` Fluent v9 — we're just bridging two
            // copies of the same external types package.
            icon={card.icon as ActionCardProps['icon']}
            label={card.label}
            ariaLabel={`${card.label} — ${card.description}`}
            onClick={cardHandlers[i]}
          />
        </div>
      ))}
    </div>
  );
};

GetStartedCardsWidget.displayName = 'GetStartedCardsWidget';

export default GetStartedCardsWidget;
