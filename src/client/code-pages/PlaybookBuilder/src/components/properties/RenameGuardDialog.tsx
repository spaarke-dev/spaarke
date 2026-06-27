/**
 * RenameGuardDialog — Fluent v9 confirmation dialog for OutputVariable renames.
 *
 * R3 P9 H2 — FR-3H2.1 / AC-H2.1. Surfaced by NodePropertiesForm and
 * NodePropertiesDialog when a user changes a node's OutputVariable AND at
 * least one other node has a `{{<oldName>.output.*}}` template reference.
 *
 * Three actions (per FR-3H2.1):
 *   - "Auto-rename references" (PRIMARY / default) — caller invokes
 *     canvasStore.renameOutputVariableReferences(oldName, newName) and
 *     commits the new name on the renamed node.
 *   - "Keep old name" — caller reverts the OutputVariable field on the
 *     renamed node (downstream references unchanged).
 *   - "Cancel rename" — same effect as "Keep old name"; presented as an
 *     escape hatch so users don't have to mentally translate "Keep old name"
 *     as cancellation.
 *
 * Component reuses the existing Fluent UI v9 Dialog primitives already used
 * elsewhere in PlaybookBuilder (NodePropertiesDialog, TestOptionsDialog) and
 * never introduces hardcoded color values (ADR-021 — semantic tokens only;
 * dark mode parity).
 */

import { memo, useCallback } from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Text,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import type { OutputVariableReference } from '../../services/canvasValidation';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Action returned by the dialog to its parent. */
export type RenameGuardAction = 'autoRename' | 'keepOldName' | 'cancel';

export interface RenameGuardDialogProps {
  /** Whether the dialog is currently open. */
  open: boolean;
  /** The original OutputVariable name being changed. */
  oldName: string;
  /** The new OutputVariable name the user typed. */
  newName: string;
  /** References discovered by findOutputVariableReferences (one per node). */
  references: OutputVariableReference[];
  /** Resolution callback — caller decides how to apply the chosen action. */
  onResolve: (action: RenameGuardAction) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    maxWidth: '560px',
    width: '90vw',
  },
  intro: {
    marginBottom: tokens.spacingVerticalM,
  },
  nameRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    marginBottom: tokens.spacingVerticalM,
  },
  nameToken: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalSNudge),
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
  },
  arrow: {
    color: tokens.colorNeutralForeground3,
  },
  referencesHeading: {
    marginBottom: tokens.spacingVerticalXS,
  },
  referenceList: {
    listStyleType: 'none',
    ...shorthands.padding('0'),
    ...shorthands.margin('0'),
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  referenceItem: {
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    color: tokens.colorNeutralForeground1,
  },
  referenceLabel: {
    fontWeight: tokens.fontWeightSemibold,
  },
  referenceMeta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RenameGuardDialog = memo(function RenameGuardDialog({
  open,
  oldName,
  newName,
  references,
  onResolve,
}: RenameGuardDialogProps) {
  const styles = useStyles();

  const handleAutoRename = useCallback(() => onResolve('autoRename'), [onResolve]);
  const handleKeepOldName = useCallback(() => onResolve('keepOldName'), [onResolve]);
  const handleCancel = useCallback(() => onResolve('cancel'), [onResolve]);

  const totalRefs = references.reduce((acc, r) => acc + r.rawRefs.length, 0);
  const nodeCount = references.length;

  return (
    <Dialog
      open={open}
      onOpenChange={(_e, data) => {
        // Treat dismissal (esc / overlay click) the same as Cancel — preserves
        // the principle that closing the dialog never silently breaks refs.
        if (!data.open) handleCancel();
      }}
    >
      <DialogSurface className={styles.surface}>
        <DialogBody>
          <DialogTitle>
            {nodeCount === 1
              ? 'Variable referenced by 1 downstream node'
              : `Variable referenced by ${nodeCount} downstream nodes`}
          </DialogTitle>
          <DialogContent>
            <Text as="p" className={styles.intro}>
              You are renaming an Output Variable that {totalRefs === 1 ? 'is' : 'is'} referenced by {totalRefs}{' '}
              downstream template expression{totalRefs === 1 ? '' : 's'}. Choose how to handle the existing references.
            </Text>

            <div className={styles.nameRow}>
              <span className={styles.nameToken}>{oldName}</span>
              <span className={styles.arrow} aria-hidden="true">
                &rarr;
              </span>
              <span className={styles.nameToken}>{newName}</span>
            </div>

            <Text as="div" weight="semibold" className={styles.referencesHeading}>
              Referencing nodes
            </Text>
            <ul className={styles.referenceList} aria-label="Nodes referencing the renamed variable">
              {references.map(ref => (
                <li key={ref.nodeId} className={styles.referenceItem}>
                  <span className={styles.referenceLabel}>{ref.nodeLabel}</span>
                  <span className={styles.referenceMeta}>
                    {' '}
                    ({ref.nodeType}) &mdash; {ref.rawRefs.length} reference
                    {ref.rawRefs.length === 1 ? '' : 's'}
                  </span>
                </li>
              ))}
            </ul>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={handleCancel}>
              Cancel rename
            </Button>
            <Button appearance="secondary" onClick={handleKeepOldName}>
              Keep old name
            </Button>
            <Button appearance="primary" onClick={handleAutoRename}>
              Auto-rename references
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
});
