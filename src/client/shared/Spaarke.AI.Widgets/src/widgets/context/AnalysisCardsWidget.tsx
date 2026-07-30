/**
 * @spaarke/ai-widgets — AnalysisCardsWidget
 *
 * The "Analysis" tab of the tabbed Quick Start modal
 * (ai-advanced-capabilities-analysis-hub-r1). Renders the three Analysis
 * work-type launch cards, IDENTICAL in layout to the sibling
 * `GetStartedCardsWidget` (the Quick Start "Create" tab) — same responsive
 * `ActionCard` grid, same keyboard model — so the two tabs read as one surface.
 *
 * Card set (spec Open-Question resolution, §345 — unchanged from the retired
 * `AnalysisHubWidget` card row this REPLACES):
 *   - Agreement Review    — LIVE (actionable). Fires `onCardClick('agreement-review')`.
 *   - Legal Research      — "coming soon" (disabled, badge overlay).
 *   - Patent Application  — "coming soon" (disabled, badge overlay).
 *
 * This is the SAME three-card definition that previously lived inline in
 * `AnalysisHubWidget` (`HUB_CARDS`). It is RELOCATED here — not duplicated — so
 * the Analysis widget can become a plain dataset grid and analysis creation
 * flows through the one shared Quick Start modal (owner UX, 2026-07-29/30).
 *
 * Purity (mirrors `GetStartedCardsWidget`): this component does NOT dispatch
 * PaneEventBus events itself. It invokes `onCardClick(cardId)`; the host
 * (`QuickStartModal`) maps the live card to its launch (close Quick Start +
 * dispatch `open_create_analysis_wizard`).
 *
 * Standards:
 *   - ADR-012: shared lib (`@spaarke/ai-widgets`) reusing the shared `ActionCard`
 *     primitive (`@spaarke/ui-components`) — no fork.
 *   - ADR-021: Fluent v9 semantic tokens only; light/dark both adapt.
 *   - ADR-022: React 19 functional component.
 *   - ADR-025: icons from `@fluentui/react-icons` v9.
 *
 * @see GetStartedCardsWidget — the sibling "Create" tab card grid (layout mirror)
 * @see ActionCard            — @spaarke/ui-components card primitive (reused as-is)
 * @see QuickStartModal.tsx   — the tabbed host that consumes both card widgets
 */

import * as React from 'react';
import { useMemo, useRef } from 'react';
import { Badge, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { DocumentAddRegular, DocumentMultipleRegular, DocumentSearchRegular } from '@fluentui/react-icons';
import type { FluentIcon } from '@fluentui/react-icons';

import { ActionCard } from '@spaarke/ui-components';
import type { ActionCardProps } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * The three Analysis work-type card identifiers. Only `'agreement-review'` is
 * LIVE this project; the other two render disabled ("coming soon"). Exported so
 * the host can switch on these values exhaustively.
 */
export type AnalysisCardId = 'agreement-review' | 'legal-research' | 'patent-application';

export interface AnalysisCardsWidgetProps {
  /**
   * Called when an actionable (non-coming-soon) card is activated. Receives the
   * card id. Disabled cards never fire this. Defaults to a no-op so the widget
   * renders cleanly in tests / Storybook.
   */
  onCardClick?: (cardId: AnalysisCardId) => void;
  /** Optional class name applied to the grid container. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Card definitions
// ---------------------------------------------------------------------------

interface AnalysisCardDefinition {
  id: AnalysisCardId;
  label: string;
  description: string;
  icon: FluentIcon;
  /** When true the card renders disabled with a "Coming soon" badge affordance. */
  comingSoon: boolean;
}

/**
 * Exactly three cards ship this project (spec Open-Question resolution, §345).
 * Expandable later (owner: "Analysis will only have 3 for now but we'll expand it").
 */
const ANALYSIS_CARDS: readonly AnalysisCardDefinition[] = Object.freeze([
  {
    id: 'agreement-review',
    label: 'Agreement Review',
    description: 'Analyze a contract or agreement document.',
    icon: DocumentAddRegular,
    comingSoon: false,
  },
  {
    id: 'legal-research',
    label: 'Legal Research',
    description: 'Legal research analysis — coming soon.',
    icon: DocumentSearchRegular,
    comingSoon: true,
  },
  {
    id: 'patent-application',
    label: 'Patent Application',
    description: 'Patent application analysis — coming soon.',
    icon: DocumentMultipleRegular,
    comingSoon: true,
  },
]);

// ---------------------------------------------------------------------------
// Styles — mirrors GetStartedCardsWidget's responsive grid (ADR-021 tokens only)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
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
  cardWrapper: {
    position: 'relative',
    display: 'flex',
    minWidth: 0,
  },
  comingSoonBadge: {
    position: 'absolute',
    top: tokens.spacingVerticalXS,
    right: tokens.spacingHorizontalXS,
    pointerEvents: 'none',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * AnalysisCardsWidget — the three Analysis work-type launch cards for the
 * Quick Start modal's "Analysis" tab.
 *
 * @example
 * ```tsx
 * <AnalysisCardsWidget onCardClick={(id) => { if (id === 'agreement-review') openWizard(); }} />
 * ```
 */
export const AnalysisCardsWidget: React.FC<AnalysisCardsWidgetProps> = ({ onCardClick, className }) => {
  const styles = useStyles();
  const gridRef = useRef<HTMLDivElement>(null);

  // Only the actionable card gets a real handler — disabled ActionCard instances
  // already ignore onClick, but omitting it keeps the intent explicit.
  const cardHandlers = useMemo<Partial<Record<AnalysisCardId, () => void>>>(
    () => ({
      'agreement-review': () => onCardClick?.('agreement-review'),
    }),
    [onCardClick]
  );

  return (
    <div
      ref={gridRef}
      className={mergeClasses(styles.grid, className)}
      role="group"
      aria-label="Create new analysis"
      data-testid="analysis-cards-widget"
    >
      {ANALYSIS_CARDS.map(card => (
        <div key={card.id} className={styles.cardWrapper} data-testid={`analysis-card-${card.id}`}>
          <ActionCard
            // Type assertion is intentional and load-bearing — see the identical
            // precedent + rationale in GetStartedCardsWidget.tsx (two structurally
            // identical but nominally distinct `@fluentui/react-icons` copies).
            icon={card.icon as ActionCardProps['icon']}
            label={card.label}
            ariaLabel={
              card.comingSoon
                ? `${card.label} — coming soon, not yet available`
                : `${card.label} — ${card.description}`
            }
            onClick={cardHandlers[card.id]}
            disabled={card.comingSoon}
          />
          {card.comingSoon && (
            <Badge
              className={styles.comingSoonBadge}
              appearance="tint"
              color="informative"
              size="small"
              data-testid={`analysis-card-coming-soon-badge-${card.id}`}
            >
              Coming soon
            </Badge>
          )}
        </div>
      ))}
    </div>
  );
};

AnalysisCardsWidget.displayName = 'AnalysisCardsWidget';

export default AnalysisCardsWidget;
