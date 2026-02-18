/**
 * SuccessConfirmation.tsx
 * Success dialog/state shown after the Create New Matter wizard completes.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │                         ✓                                             │
 *   │                  Matter created!                                      │
 *   │           "Smith v. Jones" has been created                          │
 *   │               and is ready to use.                                   │
 *   │                                                                       │
 *   │         [View Matter]     [Close]                                    │
 *   │                                                                       │
 *   │  ── Warnings (partial success) ──────────────────────────────────── │
 *   │  ⚠ File upload failed. Files can be added from the matter record.   │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * On "View Matter": calls navigateToEntity with the new matter ID.
 * On "Close": calls onClose.
 *
 * Warnings are shown for partial success (matter created but some
 * follow-on actions failed) using MessageBar intent="warning".
 *
 * Constraints:
 *   - Fluent v9: Text, Button, MessageBar — ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 */

import * as React from 'react';
import {
  Text,
  Button,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  CheckmarkCircleFilled,
} from '@fluentui/react-icons';
import { navigateToEntity } from '../../utils/navigation';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISuccessConfirmationProps {
  /** Display name of the created matter. */
  matterName: string;
  /** Dataverse GUID of the created sprk_matter record. */
  matterId: string;
  /** Non-fatal warnings from partial success (file upload failures, etc.). */
  warnings: string[];
  /** Called when the user clicks Close. */
  onClose: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXL,
    textAlign: 'center',
  },

  iconWrapper: {
    color: tokens.colorPaletteGreenForeground1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: tokens.spacingVerticalS,
  },

  titleText: {
    color: tokens.colorNeutralForeground1,
  },

  bodyText: {
    color: tokens.colorNeutralForeground2,
    maxWidth: '400px',
  },

  matterName: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  // ── Action buttons row ────────────────────────────────────────────────
  actionsRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    justifyContent: 'center',
    marginTop: tokens.spacingVerticalS,
  },

  // ── Warnings section ──────────────────────────────────────────────────
  warningsSection: {
    width: '100%',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalM,
    alignItems: 'stretch',
    textAlign: 'left',
  },

  warningsLabel: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// SuccessConfirmation (exported)
// ---------------------------------------------------------------------------

export const SuccessConfirmation: React.FC<ISuccessConfirmationProps> = ({
  matterName,
  matterId,
  warnings,
  onClose,
}) => {
  const styles = useStyles();

  const handleViewMatter = React.useCallback(() => {
    navigateToEntity({
      action: 'openRecord',
      entityName: 'sprk_matter',
      entityId: matterId,
    });
    onClose();
  }, [matterId, onClose]);

  const hasWarnings = warnings.length > 0;

  return (
    <div className={styles.root}>
      {/* Success icon */}
      <div className={styles.iconWrapper} aria-hidden="true">
        <CheckmarkCircleFilled fontSize={64} />
      </div>

      {/* Title */}
      <Text as="h2" size={600} weight="semibold" className={styles.titleText}>
        {hasWarnings ? 'Matter created with warnings' : 'Matter created!'}
      </Text>

      {/* Body */}
      <Text size={300} className={styles.bodyText}>
        <span className={styles.matterName}>&ldquo;{matterName}&rdquo;</span> has been
        created{hasWarnings ? ', though some follow-on actions could not complete. See details below.' : ' and is ready to use.'}
      </Text>

      {/* Action buttons */}
      <div className={styles.actionsRow}>
        <Button
          appearance="primary"
          onClick={handleViewMatter}
          aria-label={`View matter: ${matterName}`}
        >
          View Matter
        </Button>
        <Button
          appearance="secondary"
          onClick={onClose}
        >
          Close
        </Button>
      </div>

      {/* Warnings */}
      {hasWarnings && (
        <div className={styles.warningsSection} aria-live="polite">
          <Text size={200} weight="semibold" className={styles.warningsLabel}>
            Some actions could not complete:
          </Text>
          {warnings.map((warning, i) => (
            <MessageBar key={i} intent="warning">
              <MessageBarBody>{warning}</MessageBarBody>
            </MessageBar>
          ))}
        </div>
      )}
    </div>
  );
};
