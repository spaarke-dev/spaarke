import * as React from 'react';
import { Button, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import {
  CheckmarkCircle20Filled,
  Circle20Regular,
  Circle20Filled,
} from '@fluentui/react-icons';
import { SprkModal } from '../SprkModal';

/**
 * WizardModal — thin `SprkModal` config matching the production "Create New
 * Matter" chrome: a fixed 200px stepper sidebar (done/active/pending) + a
 * scrollable content column, with a Cancel-left / Skip·Back·Next(Finish)-right
 * footer (spec FR-09; design §6.1/§6.8). This is a PRESET only — it composes
 * `SprkModal`'s `wizard` size + `footerStart`/`footer` slots and owns NO
 * Dialog/header/footer of its own. It is NOT a fork of `WizardShell` (which
 * retains its `embedded` mode + reducer + success screen separately).
 */
const useStyles = makeStyles({
  wizardGrid: { display: 'grid', gridTemplateColumns: '200px 1fr', height: '100%', minHeight: 0 },
  stepper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalL,
    borderRight: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  stepsLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
  },
  step: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
  },
  stepActive: { color: tokens.colorNeutralForeground1, fontWeight: tokens.fontWeightSemibold },
  stepIconDone: { color: tokens.colorBrandForeground1 },
  stepIconActive: { color: tokens.colorBrandForeground1 },
  stepIconPending: { color: tokens.colorNeutralForeground4 },
  wizardContent: { padding: tokens.spacingHorizontalXL, overflow: 'auto', minWidth: 0 },
});

export interface WizardModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — wired to the × and the Cancel button. */
  onClose: () => void;
  /** Header title (ellipsized; announced). */
  title: string;
  /** Ordered step labels rendered in the stepper sidebar. */
  steps: string[];
  /** 0-based index of the current step. */
  active: number;
  /** Back callback — wired to the secondary Back button (disabled at step 0). */
  onBack: () => void;
  /** Next callback — wired to the primary button (labeled "Finish" on the last step). */
  onNext: () => void;
  /** Optional Skip callback — shown only when supplied and not on the last step. */
  onSkip?: () => void;
  /** The `--sprk-ui-scale` factor for sizing, forwarded to the shell. */
  uiScale?: number;
  /** Wizard step content. */
  children: React.ReactNode;
}

export const WizardModal: React.FC<WizardModalProps> = ({
  open,
  onClose,
  title,
  steps,
  active,
  onBack,
  onNext,
  onSkip,
  uiScale,
  children,
}) => {
  const styles = useStyles();
  const isLast = active >= steps.length - 1;
  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      size="wizard"
      dismiss="explicit"
      uiScale={uiScale}
      padded={false}
      footerStart={
        <Button appearance="secondary" onClick={onClose}>
          Cancel
        </Button>
      }
      footer={
        <>
          {onSkip && !isLast && (
            <Button appearance="transparent" onClick={onSkip}>
              Skip
            </Button>
          )}
          <Button appearance="secondary" disabled={active <= 0} onClick={onBack}>
            Back
          </Button>
          <Button appearance="primary" onClick={onNext}>
            {isLast ? 'Finish' : 'Next'}
          </Button>
        </>
      }
    >
      <div className={styles.wizardGrid}>
        <div className={styles.stepper}>
          <span className={styles.stepsLabel}>Steps</span>
          {steps.map((label, i) => {
            const done = i < active;
            const isActive = i === active;
            const Icon = done ? CheckmarkCircle20Filled : isActive ? Circle20Filled : Circle20Regular;
            const iconClass = done
              ? styles.stepIconDone
              : isActive
                ? styles.stepIconActive
                : styles.stepIconPending;
            return (
              <div
                key={label}
                className={mergeClasses(styles.step, isActive && styles.stepActive)}
              >
                <Icon className={iconClass} />
                <Text>{label}</Text>
              </div>
            );
          })}
        </div>
        <div className={styles.wizardContent}>{children}</div>
      </div>
    </SprkModal>
  );
};

export default WizardModal;
