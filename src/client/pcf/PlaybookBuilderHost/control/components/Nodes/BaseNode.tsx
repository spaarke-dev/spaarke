/**
 * BaseNode - Common structure for all playbook node types
 *
 * Migrated to react-flow-renderer v10 for React 16 compatibility (ADR-022).
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
import type { PlaybookNodeData, PlaybookNodeType } from '../../stores';

const useStyles = makeStyles({
  container: {
    minWidth: '140px',
    maxWidth: '180px',
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
    width: '24px',
    height: '24px',
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
  body: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
  },
});

// Node color schemes by type
export const nodeColorSchemes: Record<
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

export interface BaseNodeProps {
  data: PlaybookNodeData;
  selected?: boolean;
  icon: React.ReactNode;
  typeLabel: string;
  children?: React.ReactNode;
  sourceHandleCount?: number;
  targetHandleCount?: number;
}

/**
 * Base node component providing common structure for all node types.
 * Includes header with icon/label, body content area, and connection handles.
 */
export const BaseNode = React.memo(function BaseNode({
  data,
  selected,
  icon,
  typeLabel,
  children,
  sourceHandleCount = 1,
  targetHandleCount = 1,
}: BaseNodeProps) {
  const styles = useStyles();
  const colorScheme = nodeColorSchemes[data.type];

  return (
    <div className={mergeClasses(styles.container, selected && styles.selected)}>
      {/* Target handle (input) */}
      {targetHandleCount > 0 && <Handle type="target" position={Position.Top} />}

      {/* Header */}
      <div className={styles.header}>
        <div
          className={styles.iconWrapper}
          style={{
            backgroundColor: colorScheme.background,
            color: colorScheme.iconColor,
          }}
        >
          {icon}
        </div>
        <div className={styles.headerText}>
          <Text size={300} weight="semibold" className={styles.label}>
            {data.label}
          </Text>
          <Text size={100} className={styles.typeLabel}>
            {typeLabel}
          </Text>
        </div>
      </div>

      {/* Body content */}
      {children && <div className={styles.body}>{children}</div>}

      {/* Source handle (output) */}
      {sourceHandleCount > 0 && (
        <Handle type="source" position={Position.Bottom} />
      )}
    </div>
  );
});
