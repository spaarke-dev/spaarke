/**
 * SummaryNextStepsStep.tsx
 * Step 3 of the Summarize New File(s) wizard — checkbox card selection for
 * optional follow-on steps.
 *
 * Layout matches CreateMatter/NextStepsStep:
 *   ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
 *   │ ☐  Send          │ │ ☐  Create        │ │ ☐  Work on      │
 *   │    Email         │ │    Project       │ │    Analysis     │
 *   └─────────────────┘ └─────────────────┘ └─────────────────┘
 *
 * Selecting a card dynamically injects a follow-on step into the wizard
 * sidebar (via ADD_DYNAMIC_STEP action on WizardShell).
 * Deselecting removes that step from the sidebar.
 */
import * as React from 'react';
import {
  Card,
  Checkbox,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import {
  MailRegular,
  FolderAddRegular,
  ClipboardTaskRegular,
  CheckboxCheckedRegular,
  CheckboxUncheckedRegular,
} from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type SummaryActionId = 'send-email' | 'create-project' | 'work-on-analysis';

export interface IFollowOnCardDef {
  id: SummaryActionId;
  label: string;
  description: string;
  stepLabel: string;
  icon: React.ReactNode;
}

export interface ISummaryNextStepsStepProps {
  /** Currently selected action IDs. */
  selectedActions: SummaryActionId[];
  /** Called when selection changes. */
  onSelectionChange: (actions: SummaryActionId[]) => void;
  /** Whether "Include only short summary" is toggled for email. */
  includeShortSummary: boolean;
  /** Toggle for short summary in email. */
  onIncludeShortSummaryChange: (checked: boolean) => void;
}

// ---------------------------------------------------------------------------
// Card definitions (canonical order)
// ---------------------------------------------------------------------------

const CARD_DEFS: IFollowOnCardDef[] = [
  {
    id: 'send-email',
    label: 'Send Email',
    description: 'Compose and send an email with the file summary.',
    stepLabel: 'Send Email',
    icon: <MailRegular fontSize={28} />,
  },
  {
    id: 'create-project',
    label: 'Create Project',
    description: 'Launch the Create Project wizard with the uploaded files.',
    stepLabel: 'Create Project',
    icon: <FolderAddRegular fontSize={28} />,
  },
  {
    id: 'work-on-analysis',
    label: 'Work on Analysis',
    description: 'Choose a playbook to run analysis on the uploaded files.',
    stepLabel: 'Work on Analysis',
    icon: <ClipboardTaskRegular fontSize={28} />,
  },
];

// ---------------------------------------------------------------------------
// Exported maps consumed by SummarizeFilesDialog
// ---------------------------------------------------------------------------

/** Map SummaryActionId to the IWizardStep id used in the sidebar. */
export const FOLLOW_ON_STEP_ID_MAP: Record<SummaryActionId, string> = {
  'send-email': 'followon-send-email',
  'create-project': 'followon-create-project',
  'work-on-analysis': 'followon-work-on-analysis',
};

/** Map SummaryActionId to the sidebar step label. */
export const FOLLOW_ON_STEP_LABEL_MAP: Record<SummaryActionId, string> = {
  'send-email': 'Send Email',
  'create-project': 'Create Project',
  'work-on-analysis': 'Work on Analysis',
};

/** Canonical order array for dynamic step insertion. */
export const FOLLOW_ON_CANONICAL_ORDER = [
  'followon-send-email',
  'followon-create-project',
  'followon-work-on-analysis',
];

// ---------------------------------------------------------------------------
// Styles (matching CreateMatter/NextStepsStep pattern)
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

  emailToggle: {
    marginTop: tokens.spacingVerticalS,
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
  def: IFollowOnCardDef;
  selected: boolean;
  onToggle: (id: SummaryActionId) => void;
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
          className={mergeClasses(
            styles.cardIcon,
            !selected && styles.cardIconNeutral
          )}
          aria-hidden="true"
        >
          {def.icon}
        </span>
        <span
          className={mergeClasses(
            styles.checkboxIcon,
            !selected && styles.checkboxIconNeutral
          )}
          aria-hidden="true"
        >
          {selected ? (
            <CheckboxCheckedRegular fontSize={22} />
          ) : (
            <CheckboxUncheckedRegular fontSize={22} />
          )}
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
// SummaryNextStepsStep (exported)
// ---------------------------------------------------------------------------

export const SummaryNextStepsStep: React.FC<ISummaryNextStepsStepProps> = ({
  selectedActions,
  onSelectionChange,
  includeShortSummary,
  onIncludeShortSummaryChange,
}) => {
  const styles = useStyles();

  const handleToggle = React.useCallback(
    (id: SummaryActionId) => {
      if (selectedActions.includes(id)) {
        onSelectionChange(selectedActions.filter((a) => a !== id));
      } else {
        // Maintain canonical order
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
          Next Steps
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Choose what you&apos;d like to do with the summary results. Select one or more actions,
          or click Finish to close.
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

      {/* Email summary toggle — only visible when Send Email is selected */}
      {selectedActions.includes('send-email') && (
        <div className={styles.emailToggle}>
          <Checkbox
            checked={includeShortSummary}
            onChange={(_e, data) => onIncludeShortSummaryChange(!!data.checked)}
            label="Include only short summary in email"
          />
        </div>
      )}

      {selectedActions.length === 0 && (
        <Text size={200} className={styles.skipMessage}>
          No actions selected — click Finish to complete without follow-on steps.
        </Text>
      )}
    </div>
  );
};
