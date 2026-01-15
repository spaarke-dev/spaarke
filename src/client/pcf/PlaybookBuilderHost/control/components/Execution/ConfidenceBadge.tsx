/**
 * ConfidenceBadge - Color-coded confidence score display
 *
 * Displays AI confidence scores with visual color coding:
 * - Green (>=0.9): High confidence
 * - Yellow (0.7-0.9): Medium confidence
 * - Red (<0.7): Low confidence
 *
 * @version 1.0.0
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Badge,
  Tooltip,
  mergeClasses,
} from '@fluentui/react-components';
import { Sparkle20Regular } from '@fluentui/react-icons';

const useStyles = makeStyles({
  badge: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    ...shorthands.padding('2px', '6px'),
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    display: 'inline-flex',
    alignItems: 'center',
    ...shorthands.gap('4px'),
  },
  highConfidence: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    color: tokens.colorPaletteGreenForeground2,
  },
  mediumConfidence: {
    backgroundColor: tokens.colorPaletteYellowBackground2,
    color: tokens.colorPaletteYellowForeground2,
  },
  lowConfidence: {
    backgroundColor: tokens.colorPaletteRedBackground2,
    color: tokens.colorPaletteRedForeground2,
  },
  icon: {
    width: '14px',
    height: '14px',
  },
  // Compact variant for node badges
  compact: {
    fontSize: tokens.fontSizeBase100,
    ...shorthands.padding('1px', '4px'),
  },
  // Large variant for metrics panel
  large: {
    fontSize: tokens.fontSizeBase300,
    ...shorthands.padding('4px', '8px'),
  },
});

export type ConfidenceLevel = 'high' | 'medium' | 'low';

export interface ConfidenceBadgeProps {
  /** Confidence score between 0 and 1 */
  confidence: number;
  /** Size variant */
  size?: 'compact' | 'default' | 'large';
  /** Show icon alongside value */
  showIcon?: boolean;
  /** Custom className */
  className?: string;
}

/**
 * Get confidence level based on score
 * - >= 0.9: high (green)
 * - 0.7-0.9: medium (yellow)
 * - < 0.7: low (red)
 */
export function getConfidenceLevel(confidence: number): ConfidenceLevel {
  if (confidence >= 0.9) return 'high';
  if (confidence >= 0.7) return 'medium';
  return 'low';
}

/**
 * Get human-readable description for confidence level
 */
export function getConfidenceDescription(confidence: number): string {
  const level = getConfidenceLevel(confidence);
  const percentage = Math.round(confidence * 100);

  switch (level) {
    case 'high':
      return `High confidence (${percentage}%) - AI is highly certain about this result`;
    case 'medium':
      return `Medium confidence (${percentage}%) - AI is reasonably certain but some uncertainty exists`;
    case 'low':
      return `Low confidence (${percentage}%) - AI is uncertain about this result, review recommended`;
  }
}

/**
 * Color-coded badge displaying AI confidence score.
 */
export const ConfidenceBadge: React.FC<ConfidenceBadgeProps> = ({
  confidence,
  size = 'default',
  showIcon = false,
  className,
}) => {
  const styles = useStyles();
  const level = getConfidenceLevel(confidence);
  const percentage = Math.round(confidence * 100);
  const description = getConfidenceDescription(confidence);

  const levelClassName =
    level === 'high'
      ? styles.highConfidence
      : level === 'medium'
        ? styles.mediumConfidence
        : styles.lowConfidence;

  const sizeClassName =
    size === 'compact'
      ? styles.compact
      : size === 'large'
        ? styles.large
        : undefined;

  return (
    <Tooltip content={description} relationship="description" withArrow>
      <span
        className={mergeClasses(
          styles.badge,
          levelClassName,
          sizeClassName,
          className
        )}
      >
        {showIcon && <Sparkle20Regular className={styles.icon} />}
        {percentage}%
      </span>
    </Tooltip>
  );
};

/**
 * Badge variant using Fluent UI Badge component
 * For use in node status displays
 */
export interface ConfidenceNodeBadgeProps {
  /** Confidence score between 0 and 1 */
  confidence: number;
  /** Custom className */
  className?: string;
}

export const ConfidenceNodeBadge: React.FC<ConfidenceNodeBadgeProps> = ({
  confidence,
  className,
}) => {
  const level = getConfidenceLevel(confidence);
  const percentage = Math.round(confidence * 100);
  const description = getConfidenceDescription(confidence);

  const color =
    level === 'high' ? 'success' : level === 'medium' ? 'warning' : 'danger';

  return (
    <Tooltip content={description} relationship="description" withArrow>
      <Badge appearance="filled" color={color} className={className}>
        {percentage}%
      </Badge>
    </Tooltip>
  );
};

export default ConfidenceBadge;
