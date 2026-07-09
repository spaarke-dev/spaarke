/**
 * FollowOnGrid.tsx
 * Config-driven "Next Steps" card grid, shared across all Spaarke create
 * wizards.
 *
 * Renders a checkbox-card grid from an arbitrary `FollowOnCardConfig[]`. When
 * the user toggles a card, the grid drives the wizard shell directly:
 *   - select   → `addDynamicStep(stepConfig, canonicalOrder)`
 *   - deselect → `removeDynamicStep(stepId)`
 * and reports the new selection via `onSelectionChange` (in canonical /
 * card-array order). Zero selection renders the early-finish hint — the
 * consuming wizard wires the actual `isEarlyFinish: () => selected.length === 0`
 * on its own "Next Steps" step config (this grid is that step's content).
 *
 * Why the grid drives the steps in the click handler (not a mount-scoped
 * effect): the grid unmounts whenever the user navigates onto one of the
 * follow-on steps it injected. Driving `addDynamicStep`/`removeDynamicStep`
 * imperatively at click time — while the grid is guaranteed mounted — avoids
 * the stale-effect / cleanup hazard that a `useEffect` diff on `selected`
 * would introduce, and keeps the grid a pure, testable function of its props.
 *
 * Constraints:
 *   - Fluent v9 only — ZERO hard-coded colors; semantic tokens throughout.
 *   - ADR-021: dark-mode via colorNeutral/colorBrand tokens.
 *   - ADR-012: context-agnostic, prop-driven; no PCF/solution imports.
 *
 * @see followOnTypes.ts — FollowOnCardConfig contract
 * @see Wizard/wizardShellTypes.ts — addDynamicStep/removeDynamicStep handle
 */
import * as React from 'react';
import { Card, Text, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import { CheckboxCheckedRegular, CheckboxUncheckedRegular } from '@fluentui/react-icons';
import type { IWizardStepConfig } from '../Wizard/wizardShellTypes';
import type { FollowOnCardConfig } from './followOnTypes';
import { followOnStepId } from './followOnTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFollowOnGridProps {
  /** The card set this wizard offers. Any length / any card ids. */
  cards: FollowOnCardConfig[];
  /** Currently-selected card ids (controlled). */
  selected: string[];
  /** Called with the next selection whenever a card is toggled. */
  onSelectionChange: (selected: string[]) => void;
  /**
   * Shell handle: add a dynamic follow-on step. Sourced from the wizard shell
   * (`IWizardShellHandle.addDynamicStep`). Passed as a bare function so the grid
   * stays decoupled from the shell and is trivially testable with a mock.
   */
  addDynamicStep: (config: IWizardStepConfig, canonicalOrder?: string[]) => void;
  /** Shell handle: remove a dynamic follow-on step by id. */
  removeDynamicStep: (stepId: string) => void;
  /**
   * Optional canonical ordering of STEP ids for sidebar insertion. Defaults to
   * the `cards` array order (mapped to step ids), so a wizard controls ordering
   * simply by ordering its card array.
   */
  canonicalOrder?: string[];
  /** Step heading. Defaults to "Next Steps". */
  title?: string;
  /** Step subheading. A sensible generic default is used when omitted. */
  subtitle?: string;
  /** Hint shown when nothing is selected. A sensible generic default is used. */
  emptyHint?: string;
}

// ---------------------------------------------------------------------------
// Styles (ported from the canonical FollowOnSteps card visual)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  headerText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },
  cardRow: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
    gap: tokens.spacingHorizontalM,
  },
  card: {
    cursor: 'pointer',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalL,
    borderTopWidth: '2px',
    borderRightWidth: '2px',
    borderBottomWidth: '2px',
    borderLeftWidth: '2px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    userSelect: 'none',
    transition: 'border-color 0.1s ease, background-color 0.1s ease',
    boxShadow: 'none',
    ':hover': {
      borderTopColor: tokens.colorBrandStroke1,
      borderRightColor: tokens.colorBrandStroke1,
      borderBottomColor: tokens.colorBrandStroke1,
      borderLeftColor: tokens.colorBrandStroke1,
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  cardSelected: {
    borderTopColor: tokens.colorBrandStroke1,
    borderRightColor: tokens.colorBrandStroke1,
    borderBottomColor: tokens.colorBrandStroke1,
    borderLeftColor: tokens.colorBrandStroke1,
    backgroundColor: tokens.colorBrandBackground2,
    ':hover': {
      borderTopColor: tokens.colorBrandStroke1,
      borderRightColor: tokens.colorBrandStroke1,
      borderBottomColor: tokens.colorBrandStroke1,
      borderLeftColor: tokens.colorBrandStroke1,
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  cardTopRow: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  cardIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
  },
  cardIconNeutral: {
    color: tokens.colorNeutralForeground3,
  },
  checkboxIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: '20px',
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
  },
  checkboxIconNeutral: {
    color: tokens.colorNeutralForeground3,
  },
  cardLabel: {
    color: tokens.colorNeutralForeground1,
    marginTop: tokens.spacingVerticalXS,
  },
  cardDescription: {
    color: tokens.colorNeutralForeground2,
  },
  selectedExtra: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
  },
  skipMessage: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    paddingTop: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// CheckboxCard sub-component
// ---------------------------------------------------------------------------

interface ICheckboxCardProps {
  card: FollowOnCardConfig;
  selected: boolean;
  onToggle: (card: FollowOnCardConfig) => void;
}

const CheckboxCard: React.FC<ICheckboxCardProps> = ({ card, selected, onToggle }) => {
  const styles = useStyles();

  const handleClick = React.useCallback(() => onToggle(card), [card, onToggle]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === ' ' || e.key === 'Enter') {
        e.preventDefault();
        onToggle(card);
      }
    },
    [card, onToggle]
  );

  return (
    <Card
      className={mergeClasses(styles.card, selected && styles.cardSelected)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      role="checkbox"
      aria-checked={selected}
      tabIndex={0}
      aria-label={`${card.label}: ${card.description}${selected ? ' — selected' : ''}`}
    >
      <div className={styles.cardTopRow}>
        <span className={mergeClasses(styles.cardIcon, !selected && styles.cardIconNeutral)} aria-hidden="true">
          {card.icon}
        </span>
        <span className={mergeClasses(styles.checkboxIcon, !selected && styles.checkboxIconNeutral)} aria-hidden="true">
          {selected ? <CheckboxCheckedRegular fontSize={22} /> : <CheckboxUncheckedRegular fontSize={22} />}
        </span>
      </div>
      <Text size={300} weight="semibold" className={styles.cardLabel}>
        {card.label}
      </Text>
      <Text size={200} className={styles.cardDescription}>
        {card.description}
      </Text>
    </Card>
  );
};

// ---------------------------------------------------------------------------
// FollowOnGrid (exported)
// ---------------------------------------------------------------------------

const DEFAULT_SUBTITLE =
  'Optionally select follow-on actions to complete after the record is created. You can skip all of these.';
const DEFAULT_EMPTY_HINT = 'No actions selected — click Finish to create the record without follow-on steps.';

export const FollowOnGrid: React.FC<IFollowOnGridProps> = ({
  cards,
  selected,
  onSelectionChange,
  addDynamicStep,
  removeDynamicStep,
  canonicalOrder,
  title = 'Next Steps',
  subtitle = DEFAULT_SUBTITLE,
  emptyHint = DEFAULT_EMPTY_HINT,
}) => {
  const styles = useStyles();

  // Canonical order defaults to the card-array order (mapped to step ids).
  const resolvedOrder = React.useMemo(
    () => canonicalOrder ?? cards.map(c => followOnStepId(c.id)),
    [canonicalOrder, cards]
  );

  // Build the dynamic step config the shell inserts for a selected card.
  const buildStepConfig = React.useCallback(
    (card: FollowOnCardConfig): IWizardStepConfig => ({
      id: followOnStepId(card.id),
      label: card.stepLabel ?? card.label,
      canAdvance: card.canAdvance ?? (() => true),
      isSkippable: card.isSkippable,
      renderContent: card.renderStep,
    }),
    []
  );

  const handleToggle = React.useCallback(
    (card: FollowOnCardConfig) => {
      const isSelected = selected.includes(card.id);
      if (isSelected) {
        // Deselect: remove the injected step, then report the trimmed selection.
        removeDynamicStep(followOnStepId(card.id));
        onSelectionChange(selected.filter(id => id !== card.id));
      } else {
        // Select: inject the step, then report the selection in card-array order.
        addDynamicStep(buildStepConfig(card), resolvedOrder);
        const next = cards.map(c => c.id).filter(id => selected.includes(id) || id === card.id);
        onSelectionChange(next);
      }
    },
    [cards, selected, onSelectionChange, addDynamicStep, removeDynamicStep, buildStepConfig, resolvedOrder]
  );

  return (
    <div className={styles.root}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          {title}
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          {subtitle}
        </Text>
      </div>

      <div className={styles.cardRow} role="group" aria-label="Follow-on actions">
        {cards.map(card => (
          <CheckboxCard key={card.id} card={card} selected={selected.includes(card.id)} onToggle={handleToggle} />
        ))}
      </div>

      {/* Per-card inline extras (e.g. SummarizeFiles "short summary" toggle) */}
      {cards.map(card =>
        card.renderSelectedExtra && selected.includes(card.id) ? (
          <div key={`extra-${card.id}`} className={styles.selectedExtra}>
            {card.renderSelectedExtra()}
          </div>
        ) : null
      )}

      {selected.length === 0 && (
        <Text size={200} className={styles.skipMessage}>
          {emptyHint}
        </Text>
      )}
    </div>
  );
};

FollowOnGrid.displayName = 'FollowOnGrid';
