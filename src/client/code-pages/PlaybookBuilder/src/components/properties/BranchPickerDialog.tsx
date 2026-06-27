/**
 * BranchPickerDialog — Prompts the author to pick a branch (true / false / both)
 * when connecting an edge FROM a Condition node to a downstream node.
 *
 * Triggered by canvasStore.onConnect when the source is a condition node and
 * the user dragged from the body (no sourceHandle). When the user picks:
 *   - True : single edge with sourceHandle='true', type='trueBranch'
 *   - False: single edge with sourceHandle='false', type='falseBranch'
 *   - Both : TWO edges (one True + one False) — we never invent a 'bothBranch' type
 *   - Cancel: no edge created
 *
 * Labels for "True"/"False" branches come from the source Condition node's
 * `conditionJson` (managed by ConditionEditor). If the author renamed them
 * (e.g. "Approved"/"Rejected"), those labels are surfaced as option hints.
 *
 * @see R3-092 (FR-3H2.2 / AC-H2.2)
 * @see notes/playbookbuilder-pattern-research.md §6 (task 092 mapping)
 * @see ADR-021 (Fluent UI v9, semantic tokens, dark-mode parity)
 */

import { memo, useCallback, useMemo, useState, useEffect } from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Label,
  Radio,
  RadioGroup,
  Text,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { useCanvasStore, type BranchChoice } from '../../stores/canvasStore';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Extract True/False branch labels from a Condition node's `conditionJson`.
 * Mirrors ConditionEditor.parseCondition's default fallbacks ("True"/"False")
 * but only reads — does NOT mutate. Defensive against malformed JSON.
 */
function readBranchLabels(conditionJson: string | undefined): { trueLabel: string; falseLabel: string } {
  const defaults = { trueLabel: 'True', falseLabel: 'False' };
  if (!conditionJson) return defaults;
  try {
    const parsed = JSON.parse(conditionJson);
    return {
      trueLabel: typeof parsed.trueBranch === 'string' && parsed.trueBranch.length > 0 ? parsed.trueBranch : 'True',
      falseLabel:
        typeof parsed.falseBranch === 'string' && parsed.falseBranch.length > 0 ? parsed.falseBranch : 'False',
    };
  } catch {
    return defaults;
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    maxWidth: '480px',
    width: '90vw',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  intro: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
  },
  optionRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  branchDot: {
    width: '10px',
    height: '10px',
    ...shorthands.borderRadius('50%'),
    flexShrink: 0,
  },
  trueDot: {
    backgroundColor: tokens.colorPaletteGreenForeground1,
  },
  falseDot: {
    backgroundColor: tokens.colorPaletteRedForeground1,
  },
  bothDot: {
    // Diagonal split: green half + red half (semantic tokens; no hardcoded hex)
    background: `linear-gradient(90deg, ${tokens.colorPaletteGreenForeground1} 50%, ${tokens.colorPaletteRedForeground1} 50%)`,
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Subscribes to `canvasStore.pendingBranchConnection`. When non-null, opens
 * a Fluent v9 Dialog asking the author which branch to wire. Mount once
 * near the canvas root (PlaybookCanvas does this).
 */
export const BranchPickerDialog = memo(function BranchPickerDialog() {
  const styles = useStyles();

  const pending = useCanvasStore(s => s.pendingBranchConnection);
  const nodes = useCanvasStore(s => s.nodes);
  const confirmBranchSelection = useCanvasStore(s => s.confirmBranchSelection);
  const cancelBranchSelection = useCanvasStore(s => s.cancelBranchSelection);

  const [choice, setChoice] = useState<BranchChoice>('true');

  // Reset the radio selection each time a new pending connection appears,
  // so the next dialog opens with a clean default.
  useEffect(() => {
    if (pending) setChoice('true');
  }, [pending]);

  // Read the source Condition node's branch labels (read-only).
  const labels = useMemo(() => {
    if (!pending) return { trueLabel: 'True', falseLabel: 'False' };
    const sourceNode = nodes.find(n => n.id === pending.source);
    if (!sourceNode || sourceNode.data.type !== 'condition') {
      return { trueLabel: 'True', falseLabel: 'False' };
    }
    return readBranchLabels(sourceNode.data.conditionJson as string | undefined);
  }, [pending, nodes]);

  const handleConfirm = useCallback(() => {
    confirmBranchSelection(choice);
  }, [choice, confirmBranchSelection]);

  const handleCancel = useCallback(() => {
    cancelBranchSelection();
  }, [cancelBranchSelection]);

  const isOpen = pending !== null;

  return (
    <Dialog open={isOpen} onOpenChange={(_, data) => !data.open && handleCancel()}>
      <DialogSurface className={styles.surface}>
        <DialogTitle>Choose branch for this connection</DialogTitle>
        <DialogBody>
          <DialogContent>
            <div className={styles.body}>
              <Text className={styles.intro}>
                This edge starts at a Condition node. Pick which branch the downstream node should follow.
              </Text>
              <Label>Wire to</Label>
              <RadioGroup
                value={choice}
                onChange={(_, data) => setChoice(data.value as BranchChoice)}
                aria-label="Branch selection"
              >
                <Radio
                  value="true"
                  label={
                    <span className={styles.optionRow}>
                      <span className={`${styles.branchDot} ${styles.trueDot}`} />
                      <Text>True branch ({labels.trueLabel})</Text>
                    </span>
                  }
                />
                <Radio
                  value="false"
                  label={
                    <span className={styles.optionRow}>
                      <span className={`${styles.branchDot} ${styles.falseDot}`} />
                      <Text>False branch ({labels.falseLabel})</Text>
                    </span>
                  }
                />
                <Radio
                  value="both"
                  label={
                    <span className={styles.optionRow}>
                      <span className={`${styles.branchDot} ${styles.bothDot}`} />
                      <Text>Both branches (creates two edges)</Text>
                    </span>
                  }
                />
              </RadioGroup>
              <Text className={styles.hint}>
                Choosing &quot;Both&quot; creates one edge for the True branch and one for the False branch.
              </Text>
            </div>
          </DialogContent>
        </DialogBody>
        <DialogActions>
          <Button appearance="secondary" onClick={handleCancel}>
            Cancel
          </Button>
          <Button appearance="primary" onClick={handleConfirm}>
            Connect
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
});

export default BranchPickerDialog;
