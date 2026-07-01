/**
 * UnknownNode — Warning-state node renderer for unrecognized executor types.
 *
 * R7 Wave 8 task 089 (FR-27). Rendered when a canvas node carries an
 * `executorType` Choice value that is NOT present in `EXECUTOR_METADATA`
 * (e.g., a future executor not yet shipped to this PlaybookBuilder version,
 * or a typo / corrupted record). Previously the canvas silently fell back
 * to the React Flow default box with no diagnostic — the maker had no way
 * to tell what was wrong.
 *
 * Renders a node shell with warning-state styling (semantic tokens only —
 * ADR-021) and an "Unknown Executor Type {N}" label. Clicking the node
 * opens the NodePropertiesDialog forced to the Action tab so the maker can
 * pick a known executor type via the ExecutorTypeSelector (the dialog
 * itself disables all other tabs while `data.type === 'unknown'`).
 *
 * Once the maker picks a known type, the canvas store rewrites
 * `node.type` (and `node.data.type`) to the matching `canvasType` and this
 * component is replaced by the regular renderer for that type on next
 * render — see `coerceUnknownNodeTypes` in `canvasStore.ts`.
 *
 * @see ADR-006 — Fluent UI v9 only
 * @see ADR-021 — Dark mode semantic tokens (no hardcoded warning hex)
 * @see spec.md FR-27
 */

import React from 'react';
import type { Node, NodeProps } from '@xyflow/react';
import { Handle, Position } from '@xyflow/react';
import { makeStyles, tokens, Text, mergeClasses, shorthands } from '@fluentui/react-components';
import { Warning20Filled } from '@fluentui/react-icons';
import type { PlaybookNodeData } from '../../types/canvas';

const useStyles = makeStyles({
  container: {
    minWidth: '160px',
    maxWidth: '200px',
    ...shorthands.borderRadius('0'),
    backgroundColor: tokens.colorStatusWarningBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorStatusWarningBorder1),
    ...shorthands.overflow('hidden'),
    transitionProperty: 'border-color',
    transitionDuration: '0.2s',
    transitionTimingFunction: 'ease',
  },
  selected: {
    ...shorthands.borderColor(tokens.colorBrandStroke1),
    ...shorthands.borderWidth('2px'),
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorStatusWarningBorder1}`,
  },
  iconWrapper: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '24px',
    height: '24px',
    borderRadius: tokens.borderRadiusSmall,
    color: tokens.colorStatusWarningForeground1,
    flexShrink: 0,
  },
  headerText: {
    flex: 1,
    overflow: 'hidden',
  },
  label: {
    display: 'block',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    color: tokens.colorStatusWarningForeground1,
  },
  typeLabel: {
    display: 'block',
    color: tokens.colorStatusWarningForeground2,
  },
  body: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    color: tokens.colorStatusWarningForeground1,
  },
  cta: {
    display: 'block',
    marginTop: tokens.spacingVerticalXS,
    color: tokens.colorStatusWarningForeground2,
    fontStyle: 'italic',
  },
});

/**
 * UnknownNode — warning-state shell for nodes whose `executorType` is not
 * present in the local `EXECUTOR_METADATA` catalog.
 *
 * Reads `data.executorType` for the label suffix. Does NOT render the regular
 * BaseNode shell because (a) BaseNode's `nodeColorSchemes` lookup would fail
 * on `data.type === 'unknown'` (no scheme entry), and (b) we deliberately
 * want a visually distinct warning treatment driven entirely by
 * `tokens.colorStatusWarning*` semantic tokens.
 */
export const UnknownNode = React.memo(function UnknownNode({ data, selected }: NodeProps<Node<PlaybookNodeData>>) {
  const styles = useStyles();
  const executorTypeLabel = typeof data.executorType === 'number' ? String(data.executorType) : 'unset';

  return (
    <div
      className={mergeClasses(styles.container, selected && styles.selected)}
      role="group"
      aria-label={`Unknown executor type ${executorTypeLabel}`}
    >
      {/* Target handle (input) — keep wiring intact so users can re-edit
          surrounding edges; they just can't EXECUTE this node until the
          executorType is reassigned. */}
      <Handle type="target" position={Position.Top} />

      <div className={styles.header}>
        <div className={styles.iconWrapper} aria-hidden="true">
          <Warning20Filled />
        </div>
        <div className={styles.headerText}>
          <Text size={300} weight="semibold" className={styles.label}>
            {data.label || 'Unknown Node'}
          </Text>
          <Text size={100} className={styles.typeLabel}>
            Unknown Executor Type {executorTypeLabel}
          </Text>
        </div>
      </div>

      <div className={styles.body}>
        <Text size={100}>This node's executor type is not recognized by the current PlaybookBuilder version.</Text>
        <Text size={100} className={styles.cta}>
          Open the node and pick an Executor Type to enable execution.
        </Text>
      </div>

      {/* Source handle (output) */}
      <Handle type="source" position={Position.Bottom} />
    </div>
  );
});
