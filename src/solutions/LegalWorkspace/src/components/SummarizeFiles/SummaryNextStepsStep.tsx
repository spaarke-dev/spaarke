/**
 * SummaryNextStepsStep.tsx
 * Step 3 of the Summarize New File(s) wizard — action buttons for follow-up work.
 *
 * Actions:
 *   1. Work on Analysis (primary) — creates an Analysis record pre-filled with summary
 *   2. Send Email (secondary) — opens email compose with summary toggle
 *   3. Create Project (secondary) — launches Create Project wizard with files
 */
import * as React from 'react';
import {
  Button,
  Card,
  Checkbox,
  makeStyles,
  Text,
  tokens,
} from '@fluentui/react-components';
import {
  ClipboardTaskRegular,
  MailRegular,
  FolderAddRegular,
} from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** IDs for the available next-step actions. */
export type SummaryActionId = 'work-on-analysis' | 'send-email' | 'create-project';

export interface ISummaryNextStepsStepProps {
  /** Currently selected action IDs. */
  selectedActions: SummaryActionId[];
  /** Called when user toggles an action on/off. */
  onSelectionChange: (actions: SummaryActionId[]) => void;
  /** Whether "Include Full Summary" is toggled (vs short summary). */
  includeFullSummary: boolean;
  /** Toggle between full and short summary for email. */
  onIncludeFullSummaryChange: (checked: boolean) => void;
}

// ---------------------------------------------------------------------------
// Card definitions
// ---------------------------------------------------------------------------

interface IActionCardDef {
  id: SummaryActionId;
  label: string;
  description: string;
  icon: React.ReactNode;
}

const ACTION_CARDS: IActionCardDef[] = [
  {
    id: 'work-on-analysis',
    label: 'Work on Analysis',
    description: 'Create an Analysis record pre-filled with the summary output.',
    icon: <ClipboardTaskRegular fontSize={28} />,
  },
  {
    id: 'send-email',
    label: 'Send Email',
    description: 'Compose and send an email with the file summary.',
    icon: <MailRegular fontSize={28} />,
  },
  {
    id: 'create-project',
    label: 'Create Project',
    description: 'Launch the Create Project wizard with the uploaded files.',
    icon: <FolderAddRegular fontSize={28} />,
  },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  stepTitle: {
    display: 'block',
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    display: 'block',
    color: tokens.colorNeutralForeground3,
  },
  cardGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))',
    gap: tokens.spacingVerticalM,
  },
  card: {
    cursor: 'pointer',
    padding: tokens.spacingVerticalM,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalM,
    transition: 'border-color 0.15s ease',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    ':hover': {
      borderColor: tokens.colorBrandStroke1,
    },
  },
  cardSelected: {
    borderColor: tokens.colorBrandStroke1,
    backgroundColor: tokens.colorNeutralBackground1Selected,
  },
  cardContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  iconContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '40px',
    height: '40px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  emailToggle: {
    marginTop: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SummaryNextStepsStep: React.FC<ISummaryNextStepsStepProps> = ({
  selectedActions,
  onSelectionChange,
  includeFullSummary,
  onIncludeFullSummaryChange,
}) => {
  const styles = useStyles();

  const handleToggle = React.useCallback(
    (id: SummaryActionId) => {
      const isSelected = selectedActions.includes(id);
      if (isSelected) {
        onSelectionChange(selectedActions.filter((a) => a !== id));
      } else {
        onSelectionChange([...selectedActions, id]);
      }
    },
    [selectedActions, onSelectionChange],
  );

  return (
    <div className={styles.container}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Next Steps
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Choose what you&apos;d like to do with the summary results. Select one or more actions.
        </Text>
      </div>

      <div className={styles.cardGrid}>
        {ACTION_CARDS.map((card) => {
          const isSelected = selectedActions.includes(card.id);
          return (
            <Card
              key={card.id}
              className={`${styles.card} ${isSelected ? styles.cardSelected : ''}`}
              onClick={() => handleToggle(card.id)}
              role="checkbox"
              aria-checked={isSelected}
              aria-label={card.label}
            >
              <Checkbox
                checked={isSelected}
                onChange={() => handleToggle(card.id)}
                style={{ marginTop: '2px' }}
              />
              <div className={styles.iconContainer}>{card.icon}</div>
              <div className={styles.cardContent}>
                <Text size={300} weight="semibold">{card.label}</Text>
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  {card.description}
                </Text>
              </div>
            </Card>
          );
        })}
      </div>

      {/* Email summary toggle — only visible when Send Email is selected */}
      {selectedActions.includes('send-email') && (
        <div className={styles.emailToggle}>
          <Checkbox
            checked={includeFullSummary}
            onChange={(_e, data) => onIncludeFullSummaryChange(!!data.checked)}
            label="Include full summary in email (otherwise uses condensed version)"
          />
        </div>
      )}
    </div>
  );
};
