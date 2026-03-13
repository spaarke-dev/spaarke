/**
 * FollowOnSteps.tsx
 * "Next Steps" card selection UI and follow-on step ID/label mappings.
 *
 * Extracted from LegalWorkspace's CreateMatter/NextStepsStep.tsx into the
 * shared library so that all entity wizards share the same follow-on UX.
 *
 * The three optional follow-on actions are:
 *   1. Assign Resources — attorney/paralegal/outside counsel lookups
 *   2. Draft Summary   — AI-generated summary + recipient distribution
 *   3. Send Email       — compose introductory email to client
 *
 * @see CreateRecordWizard — parent component that syncs card selections
 *      with dynamic wizard steps via WizardShell.addDynamicStep.
 */
import * as React from 'react';
import {
  Card,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import {
  PersonRegular,
  DocumentTextRegular,
  MailRegular,
  CheckboxCheckedRegular,
  CheckboxUncheckedRegular,
} from '@fluentui/react-icons';
import type { FollowOnActionId } from './types';

// ---------------------------------------------------------------------------
// Card definition
// ---------------------------------------------------------------------------

interface IFollowOnCardDef {
  id: FollowOnActionId;
  label: string;
  description: string;
  stepLabel: string;
  icon: React.ReactNode;
}

const CARD_DEFS: IFollowOnCardDef[] = [
  {
    id: 'assign-counsel',
    label: 'Assign Resources',
    description: 'Search and assign internal and external resources.',
    stepLabel: 'Assign Resources',
    icon: <PersonRegular fontSize={28} />,
  },
  {
    id: 'draft-summary',
    label: 'Draft Summary',
    description: 'Generate an AI-assisted summary and distribute to recipients.',
    stepLabel: 'Draft Summary',
    icon: <DocumentTextRegular fontSize={28} />,
  },
  {
    id: 'send-email',
    label: 'Send Email to Client',
    description: 'Compose and queue an introductory email to the client.',
    stepLabel: 'Send Email',
    icon: <MailRegular fontSize={28} />,
  },
];

// ---------------------------------------------------------------------------
// Exported maps (consumed by CreateRecordWizard for dynamic step sync)
// ---------------------------------------------------------------------------

/** Map FollowOnActionId → sidebar step ID. */
export const FOLLOW_ON_STEP_ID_MAP: Record<FollowOnActionId, string> = {
  'assign-counsel': 'followon-assign-counsel',
  'draft-summary': 'followon-draft-summary',
  'send-email': 'followon-send-email',
};

/** Map FollowOnActionId → sidebar step label. */
export const FOLLOW_ON_STEP_LABEL_MAP: Record<FollowOnActionId, string> = {
  'assign-counsel': 'Assign Resources',
  'draft-summary': 'Draft Summary',
  'send-email': 'Send Email',
};

/** Canonical order for dynamic follow-on steps in the sidebar. */
export const FOLLOW_ON_CANONICAL_ORDER = [
  'followon-assign-counsel',
  'followon-draft-summary',
  'followon-send-email',
];

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface INextStepsStepProps {
  /** Currently selected action IDs. */
  selectedActions: FollowOnActionId[];
  /** Called when selection changes. */
  onSelectionChange: (selected: FollowOnActionId[]) => void;
  /** Entity label for text (e.g. "matter", "event"). Defaults to "record". */
  entityLabel?: string;
}

// ---------------------------------------------------------------------------
// Styles
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
    gridTemplateColumns: 'repeat(3, 1fr)',
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
  def: IFollowOnCardDef;
  selected: boolean;
  onToggle: (id: FollowOnActionId) => void;
}

const CheckboxCard: React.FC<ICheckboxCardProps> = ({ def, selected, onToggle }) => {
  const styles = useStyles();

  const handleClick = React.useCallback(() => {
    onToggle(def.id);
  }, [def.id, onToggle]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === ' ' || e.key === 'Enter') {
        e.preventDefault();
        onToggle(def.id);
      }
    },
    [def.id, onToggle]
  );

  return (
    <Card
      className={mergeClasses(styles.card, selected && styles.cardSelected)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      role="checkbox"
      aria-checked={selected}
      tabIndex={0}
      aria-label={`${def.label}: ${def.description}${selected ? ' — selected' : ''}`}
    >
      <div className={styles.cardTopRow}>
        <span
          className={mergeClasses(styles.cardIcon, !selected && styles.cardIconNeutral)}
          aria-hidden="true"
        >
          {def.icon}
        </span>
        <span
          className={mergeClasses(styles.checkboxIcon, !selected && styles.checkboxIconNeutral)}
          aria-hidden="true"
        >
          {selected ? <CheckboxCheckedRegular fontSize={22} /> : <CheckboxUncheckedRegular fontSize={22} />}
        </span>
      </div>
      <Text size={300} weight="semibold" className={styles.cardLabel}>
        {def.label}
      </Text>
      <Text size={200} className={styles.cardDescription}>
        {def.description}
      </Text>
    </Card>
  );
};

// ---------------------------------------------------------------------------
// NextStepsStep (exported)
// ---------------------------------------------------------------------------

export const NextStepsStep: React.FC<INextStepsStepProps> = ({
  selectedActions,
  onSelectionChange,
  entityLabel = 'record',
}) => {
  const styles = useStyles();

  const handleToggle = React.useCallback(
    (id: FollowOnActionId) => {
      if (selectedActions.includes(id)) {
        onSelectionChange(selectedActions.filter((a) => a !== id));
      } else {
        const orderedIds = CARD_DEFS.map((d) => d.id);
        const next = orderedIds.filter(
          (orderedId) => selectedActions.includes(orderedId) || orderedId === id
        );
        onSelectionChange(next);
      }
    },
    [selectedActions, onSelectionChange]
  );

  return (
    <div className={styles.root}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Next steps
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Optionally select follow-on actions to complete after the {entityLabel} is
          created. You can skip all and handle these from the {entityLabel} record.
        </Text>
      </div>

      <div className={styles.cardRow} role="group" aria-label="Follow-on actions">
        {CARD_DEFS.map((def) => (
          <CheckboxCard
            key={def.id}
            def={def}
            selected={selectedActions.includes(def.id)}
            onToggle={handleToggle}
          />
        ))}
      </div>

      {selectedActions.length === 0 && (
        <Text size={200} className={styles.skipMessage}>
          No actions selected — click Finish to create the {entityLabel} without follow-on steps.
        </Text>
      )}
    </div>
  );
};
