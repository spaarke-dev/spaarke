import * as React from 'react';
import { Button, Text, makeStyles, tokens } from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';

/**
 * ChoiceModal — a thin `SprkModal` config presenting 2-4 mutually exclusive, rich
 * (labelled + described) choices the user picks between. NOT in the prototype —
 * built fresh (project CLAUDE.md), re-basing `ChoiceDialog`'s behavior (the
 * choice-dialog-pattern, formerly ADR-023) onto the canonical shell rather than
 * forking it: the same selection model (one `onSelect(choiceId)` callback, no
 * pre-selected default — a conscious choice is always required), the same stacked,
 * keyboard-operable rich-choice buttons (label + description), and Cancel as the
 * only other way out.
 *
 * `size="xs"` — design.md §3.3 maps `ChoiceDialog` into the `xs` size class
 * alongside the other confirm/HITL surfaces. `dismiss="explicit"`: Cancel is always
 * present in `footerStart`, so requiring an explicit action (rather than an
 * accidental ESC/backdrop dismiss) strengthens — rather than degrades — the
 * "force a conscious choice" discipline; the selection model, keyboard nav, and
 * 2-4 rich-choice presentation are unchanged from `ChoiceDialog`.
 */
const useStyles = makeStyles({
  message: {
    marginBottom: tokens.spacingVerticalM,
  },
  choices: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  choiceButton: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-start',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalM,
    width: '100%',
    textAlign: 'left',
    minHeight: '64px',
  },
  choiceIcon: {
    fontSize: tokens.fontSizeBase500,
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  choiceText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    overflow: 'hidden',
  },
  choiceLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  choiceDescription: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
});

/** A single rich, described choice (choice-dialog-pattern, formerly ADR-023). */
export interface ChoiceModalChoice {
  /** Unique identifier passed to `onSelect` when this choice is picked. */
  id: string;
  /** Short, action-oriented label. */
  label: string;
  /** Explanation of what happens when this choice is picked. */
  description: string;
  /** Optional leading icon (24px Fluent icon recommended). */
  icon?: React.ReactNode;
  /** Disables this choice. */
  disabled?: boolean;
}

export interface ChoiceModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — invoked by Cancel and the × (no light-dismiss; `explicit`). */
  onClose: () => void;
  /** Header title. */
  title: string;
  /** Optional contextual message shown above the choices. */
  message?: React.ReactNode;
  /** 2-4 mutually exclusive, rich choices. */
  choices: ChoiceModalChoice[];
  /** Invoked with the picked choice's `id`. */
  onSelect: (choiceId: string) => void;
  /** The `--sprk-ui-scale` factor for sizing (default 1). */
  uiScale?: number;
}

export const ChoiceModal: React.FC<ChoiceModalProps> = ({
  open,
  onClose,
  title,
  message,
  choices,
  onSelect,
  uiScale,
}) => {
  const styles = useStyles();
  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      size="xs"
      dismiss="explicit"
      maximizable={false}
      uiScale={uiScale}
      footerStart={
        <Button appearance="secondary" onClick={onClose}>
          Cancel
        </Button>
      }
    >
      {message &&
        (typeof message === 'string' ? (
          <Text className={styles.message} block>
            {message}
          </Text>
        ) : (
          message
        ))}
      <div className={styles.choices}>
        {choices.map((choice) => (
          <Button
            key={choice.id}
            appearance="outline"
            className={styles.choiceButton}
            disabled={choice.disabled}
            onClick={() => onSelect(choice.id)}
            aria-describedby={`sprk-choice-desc-${choice.id}`}
          >
            {choice.icon && <span className={styles.choiceIcon}>{choice.icon}</span>}
            <div className={styles.choiceText}>
              <span className={styles.choiceLabel}>{choice.label}</span>
              <span id={`sprk-choice-desc-${choice.id}`} className={styles.choiceDescription}>
                {choice.description}
              </span>
            </div>
          </Button>
        ))}
      </div>
    </SprkModal>
  );
};

export default ChoiceModal;
