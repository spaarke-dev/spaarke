/**
 * NextStepsSelectionStep.tsx
 * Step 4: "Next Steps" — card selection grid for follow-on actions.
 *
 * Cards: Assign Work, Send Email, Create an Event
 * Follows the same checkbox-card pattern as CreateRecordWizard/FollowOnSteps.
 */
import * as React from 'react';
import {
  Text,
  Checkbox,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  PersonRegular,
  MailRegular,
  CalendarRegular,
} from '@fluentui/react-icons';
import type { WorkAssignmentFollowOnId } from './formTypes';

// ---------------------------------------------------------------------------
// Card metadata
// ---------------------------------------------------------------------------

interface IFollowOnCard {
  id: WorkAssignmentFollowOnId;
  label: string;
  description: string;
  icon: React.ReactElement;
}

const FOLLOW_ON_CARDS: IFollowOnCard[] = [
  {
    id: 'assign-work',
    label: 'Assign Work',
    description: 'Assign attorneys, paralegals, and law firms to this work assignment.',
    icon: <PersonRegular />,
  },
  {
    id: 'send-email',
    label: 'Send Email',
    description: 'Send an email notification about this work assignment.',
    icon: <MailRegular />,
  },
  {
    id: 'create-event',
    label: 'Create an Event',
    description: 'Create a follow-up event linked to this work assignment.',
    icon: <CalendarRegular />,
  },
];

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface INextStepsSelectionStepProps {
  selectedActions: WorkAssignmentFollowOnId[];
  onSelectedActionsChange: (actions: WorkAssignmentFollowOnId[]) => void;
}

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
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalS,
  },
  cardGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, 1fr)',
    gap: tokens.spacingHorizontalM,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusMedium,
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
    cursor: 'pointer',
    transitionProperty: 'border-color, background-color, box-shadow',
    transitionDuration: '150ms',
    transitionTimingFunction: 'ease',
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
    boxShadow: `0 0 0 1px ${tokens.colorBrandStroke1}`,
  },
  cardIcon: {
    fontSize: '28px',
    color: tokens.colorBrandForeground1,
  },
  cardLabel: {
    textAlign: 'center',
  },
  cardDescription: {
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  skipMessage: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NextStepsSelectionStep: React.FC<INextStepsSelectionStepProps> = ({
  selectedActions,
  onSelectedActionsChange,
}) => {
  const styles = useStyles();

  const toggleAction = React.useCallback(
    (actionId: WorkAssignmentFollowOnId) => {
      onSelectedActionsChange(
        selectedActions.includes(actionId)
          ? selectedActions.filter((a) => a !== actionId)
          : [...selectedActions, actionId]
      );
    },
    [selectedActions, onSelectedActionsChange]
  );

  return (
    <div className={styles.container}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Next Steps
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Select additional actions to perform after creating the work assignment.
        </Text>
      </div>

      <div className={styles.cardGrid}>
        {FOLLOW_ON_CARDS.map((card) => {
          const isSelected = selectedActions.includes(card.id);
          return (
            <div
              key={card.id}
              className={`${styles.card} ${isSelected ? styles.cardSelected : ''}`}
              onClick={() => toggleAction(card.id)}
              role="checkbox"
              aria-checked={isSelected}
              aria-label={card.label}
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  toggleAction(card.id);
                }
              }}
            >
              <span className={styles.cardIcon}>{card.icon}</span>
              <Text size={300} weight="semibold" className={styles.cardLabel}>
                {card.label}
              </Text>
              <Text size={200} className={styles.cardDescription}>
                {card.description}
              </Text>
              <Checkbox checked={isSelected} tabIndex={-1} />
            </div>
          );
        })}
      </div>

      {selectedActions.length === 0 && (
        <Text size={200} className={styles.skipMessage}>
          You can skip this step to create the work assignment without additional actions.
        </Text>
      )}
    </div>
  );
};
