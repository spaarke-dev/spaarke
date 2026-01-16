/**
 * Condition Node - branches execution flow based on a condition.
 *
 * Has two outputs: true (left) and false (right).
 */

import * as React from 'react';
import { Handle, Position } from 'react-flow-renderer';
import {
  makeStyles,
  tokens,
  Text,
  mergeClasses,
  shorthands,
} from '@fluentui/react-components';
import { Branch20Regular } from '@fluentui/react-icons';
import type { PlaybookNodeData, PlaybookNodeType } from '../../stores';

const nodeColorSchemes: Record<
  PlaybookNodeType,
  { background: string; iconColor: string }
> = {
  aiAnalysis: { background: '#0078D4', iconColor: '#ffffff' },
  aiCompletion: { background: '#0078D4', iconColor: '#ffffff' },
  condition: { background: '#FFB900', iconColor: '#000000' },
  deliverOutput: { background: '#107C10', iconColor: '#ffffff' },
  createTask: { background: '#8764B8', iconColor: '#ffffff' },
  sendEmail: { background: '#8764B8', iconColor: '#ffffff' },
  wait: { background: '#E3008C', iconColor: '#ffffff' },
};

const useStyles = makeStyles({
  container: {
    minWidth: '180px',
    maxWidth: '220px',
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke1),
    boxShadow: tokens.shadow4,
    ...shorthands.overflow('hidden'),
    transitionProperty: 'box-shadow, border-color',
    transitionDuration: '0.2s',
    transitionTimingFunction: 'ease',
  },
  selected: {
    ...shorthands.borderColor(tokens.colorBrandStroke1),
    boxShadow: tokens.shadow16,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  iconWrapper: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '28px',
    height: '28px',
    borderRadius: tokens.borderRadiusSmall,
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
  },
  typeLabel: {
    display: 'block',
    color: tokens.colorNeutralForeground3,
  },
  branches: {
    display: 'flex',
    justifyContent: 'space-between',
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    position: 'relative',
  },
  branchLabel: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalXS,
    flex: 1,
  },
  trueBranch: {
    color: tokens.colorPaletteGreenForeground1,
  },
  falseBranch: {
    color: tokens.colorPaletteRedForeground1,
  },
});

interface ConditionNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
}

/**
 * Condition node - branches execution flow based on a condition.
 * Has two outputs: true (left) and false (right).
 */
export const ConditionNode = React.memo(function ConditionNode({
  data,
  selected,
}: ConditionNodeProps) {
  const styles = useStyles();
  const colorScheme = nodeColorSchemes[data.type];

  return (
    <div className={mergeClasses(styles.container, selected && styles.selected)}>
      {/* Target handle (input) */}
      <Handle type="target" position={Position.Top} />

      {/* Header */}
      <div className={styles.header}>
        <div
          className={styles.iconWrapper}
          style={{
            backgroundColor: colorScheme.background,
            color: colorScheme.iconColor,
          }}
        >
          <Branch20Regular />
        </div>
        <div className={styles.headerText}>
          <Text size={300} weight="semibold" className={styles.label}>
            {data.label}
          </Text>
          <Text size={100} className={styles.typeLabel}>
            Condition
          </Text>
        </div>
      </div>

      {/* Branch labels */}
      <div className={styles.branches}>
        <div className={styles.branchLabel}>
          <Text size={100} weight="semibold" className={styles.trueBranch}>
            True
          </Text>
        </div>
        <div className={styles.branchLabel}>
          <Text size={100} weight="semibold" className={styles.falseBranch}>
            False
          </Text>
        </div>
      </div>

      {/* Source handles (two outputs) */}
      <Handle
        type="source"
        position={Position.Bottom}
        id="true"
        style={{ left: '25%' }}
      />
      <Handle
        type="source"
        position={Position.Bottom}
        id="false"
        style={{ left: '75%' }}
      />
    </div>
  );
});
