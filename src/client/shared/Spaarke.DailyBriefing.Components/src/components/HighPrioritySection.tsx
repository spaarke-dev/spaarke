/**
 * HighPrioritySection — cross-entity flagged-item roll-up shown above the TL;DR.
 *
 * R7 W12 feedback item 9 + follow-up (2026-07-01):
 *   Renders every record from the 7 flagged entities (matter, project, invoice,
 *   document, workassignment, event, todo) where sprk_highpriority = true OR
 *   sprk_monitor = true. Compact "mini-report" layout: each row is a card with
 *   Kind + Name link + Description + Action badge + reason chip so the operator
 *   sees at a glance WHY the record is here + WHAT triggered attention.
 *
 * Layout (per operator feedback):
 *   ┌─────────────────────────────────────────────────────────────────────────┐
 *   │ [Kind chip]  Name ↗                              [Action badge] [Reason] │
 *   │              Description text (truncated when long)                     │
 *   └─────────────────────────────────────────────────────────────────────────┘
 *
 * Interactions:
 *   - Item name click → opens Dataverse record modal via onOpenRecord
 *     (parent handles Xrm.Navigation.navigateTo call).
 *   - Renders null when items.length === 0 (no wasted vertical space).
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark-mode via semantic tokens.
 *   - Xrm-free: navigation happens in the parent via onOpenRecord.
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Link, Badge } from '@fluentui/react-components';
import { AlertUrgentRegular } from '@fluentui/react-icons';

import type { HighPriorityItemResult } from '../services/briefingService';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    backgroundColor: tokens.colorPaletteRedBackground1,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorPaletteRedBorderActive,
    borderRightColor: tokens.colorPaletteRedBorderActive,
    borderBottomColor: tokens.colorPaletteRedBorderActive,
    borderLeftColor: tokens.colorPaletteRedBorderActive,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalM,
    marginBottom: tokens.spacingVerticalL,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    marginBottom: tokens.spacingVerticalM,
  },
  headerIcon: {
    fontSize: '20px',
    color: tokens.colorPaletteRedForeground1,
  },
  headerText: {
    color: tokens.colorPaletteRedForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  itemCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorPaletteRedBorderActive,
    // Last row has no bottom border for a cleaner list edge.
    ':last-child': {
      borderBottomWidth: '0',
    },
  },
  itemRowTop: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  kindChip: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    minWidth: '110px',
  },
  itemLink: {
    color: tokens.colorBrandForeground1,
    textDecorationLine: 'none',
    cursor: 'pointer',
    fontWeight: tokens.fontWeightSemibold,
    ':hover': {
      textDecorationLine: 'underline',
    },
    flexShrink: 1,
    minWidth: 0,
    flex: 1,
  },
  descriptionRow: {
    paddingLeft: 'calc(110px + ' + tokens.spacingHorizontalS + ')',
    color: tokens.colorNeutralForeground2,
    // Truncate long descriptions to 2 lines.
    display: '-webkit-box',
    WebkitLineClamp: 2,
    WebkitBoxOrient: 'vertical' as const,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  badgeGroup: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  reasonChip: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: 'italic',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface HighPrioritySectionProps {
  /** High-priority items from /render — pre-sorted by due date ascending. */
  items: HighPriorityItemResult[];
  /** Called on item click. Wire to the parent's Xrm.Navigation modal open. */
  onOpenRecord?: (entityType: string, entityId: string) => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface ActionBadgeStyle {
  label: string;
  color: 'danger' | 'warning' | 'informative' | 'subtle';
  appearance: 'filled' | 'outline';
}

/**
 * Map the server-computed `action` string to a Fluent v9 Badge style + label.
 * Adds the due date when known so the badge is self-explanatory. Falls back
 * to modifiedon relative time for the "Recent" action.
 */
function actionToBadge(action: string, dueDate?: string, modifiedOn?: string): ActionBadgeStyle | null {
  switch (action) {
    case 'Overdue':
      return {
        label: dueDate ? `Overdue · ${formatShortDate(dueDate)}` : 'Overdue',
        color: 'danger',
        appearance: 'filled',
      };
    case 'DueToday':
      return { label: 'Due today', color: 'warning', appearance: 'filled' };
    case 'DueSoon':
      return {
        label: dueDate ? `Due ${formatShortDate(dueDate)}` : 'Due soon',
        color: 'informative',
        appearance: 'outline',
      };
    case 'Recent':
      return {
        label: modifiedOn ? `Updated ${formatRelative(modifiedOn)}` : 'Recently updated',
        color: 'subtle',
        appearance: 'outline',
      };
    default:
      return null;
  }
}

function formatShortDate(iso: string): string {
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '';
    return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(d);
  } catch {
    return '';
  }
}

function formatRelative(iso: string): string {
  try {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '';
    const days = Math.floor((Date.now() - d.getTime()) / (1000 * 60 * 60 * 24));
    if (days === 0) return 'today';
    if (days === 1) return 'yesterday';
    if (days < 7) return `${days}d ago`;
    return formatShortDate(iso);
  } catch {
    return '';
  }
}

/**
 * Translate the reason enum into a short chip label. Empty string when reason
 * is missing (widget just omits the chip).
 */
function reasonToLabel(reason?: string): string {
  switch (reason) {
    case 'Both':
      return 'HighPriority + Monitor';
    case 'HighPriority':
      return 'HighPriority';
    case 'Monitor':
      return 'Monitor';
    default:
      return '';
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const HighPrioritySection: React.FC<HighPrioritySectionProps> = ({ items, onOpenRecord }) => {
  const styles = useStyles();

  if (!Array.isArray(items) || items.length === 0) {
    return null;
  }

  const handleOpen = (entityType: string, entityId: string): void => {
    if (!entityType || !entityId) return;
    onOpenRecord?.(entityType, entityId);
  };

  return (
    <div className={styles.container} role="region" aria-label="High priority items">
      <div className={styles.header}>
        <AlertUrgentRegular className={styles.headerIcon} aria-hidden="true" />
        <Text as="h2" size={400} className={styles.headerText}>
          High Priority ({items.length})
        </Text>
      </div>
      {items.map(item => {
        const badge = actionToBadge(item.action ?? 'None', item.dueDate, item.modifiedOn);
        const reasonLabel = reasonToLabel(item.reason);
        return (
          <div key={`${item.entityType}-${item.entityId}`} className={styles.itemCard}>
            <div className={styles.itemRowTop}>
              {item.kindLabel && (
                <Text size={200} className={styles.kindChip}>
                  {item.kindLabel}
                </Text>
              )}
              <Link
                appearance="default"
                className={styles.itemLink}
                onClick={() => handleOpen(item.entityType, item.entityId)}
                role="link"
                tabIndex={0}
                onKeyDown={(e: React.KeyboardEvent) => {
                  if (e.key === 'Enter' || e.key === ' ') handleOpen(item.entityType, item.entityId);
                }}
              >
                {item.name || '(untitled)'}&nbsp;&#8599;
              </Link>
              <div className={styles.badgeGroup}>
                {badge && (
                  <Badge appearance={badge.appearance} color={badge.color} size="small">
                    {badge.label}
                  </Badge>
                )}
                {reasonLabel && (
                  <Text size={200} className={styles.reasonChip} title={`Flagged because ${reasonLabel} = Yes`}>
                    · {reasonLabel}
                  </Text>
                )}
              </div>
            </div>
            {item.description && (
              <Text size={200} className={styles.descriptionRow}>
                {item.description}
              </Text>
            )}
          </div>
        );
      })}
    </div>
  );
};
