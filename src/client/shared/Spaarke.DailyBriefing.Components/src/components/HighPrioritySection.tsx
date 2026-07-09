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
import { makeStyles, tokens, Text, Link, Badge, Button, Tooltip } from '@fluentui/react-components';
import { AlertUrgentRegular, MailRegular } from '@fluentui/react-icons';

import type { HighPriorityItemResult } from '../services/briefingService';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    marginBottom: tokens.spacingVerticalXL,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalM,
  },
  headerIcon: {
    fontSize: '20px',
    color: tokens.colorPaletteRedForeground1,
  },
  headerText: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightBold,
  },
  headerCount: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightRegular,
  },
  cardList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  // Each critical item is its own card with a severity-colored left accent
  // (danger/warning/informative/subtle → set inline per item).
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '4px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    boxShadow: tokens.shadow2,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
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
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
  },
  itemLink: {
    color: tokens.colorNeutralForeground1,
    textDecorationLine: 'none',
    cursor: 'pointer',
    fontWeight: tokens.fontWeightSemibold,
    ':hover': {
      textDecorationLine: 'underline',
      color: tokens.colorBrandForeground1,
    },
    flexShrink: 1,
    minWidth: 0,
    flex: 1,
  },
  description: {
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
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  reasonChip: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

/**
 * Severity accent for the card's left border, keyed by the action badge color.
 * Fluent v9 status/neutral tokens only (ADR-021, dark-mode safe).
 */
const ACCENT_BY_COLOR: Record<string, string> = {
  danger: tokens.colorStatusDangerBorder1,
  warning: tokens.colorStatusWarningBorder1,
  informative: tokens.colorBrandStroke1,
  subtle: tokens.colorNeutralStroke1,
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface HighPrioritySectionProps {
  /** High-priority items from /render — pre-sorted by due date ascending. */
  items: HighPriorityItemResult[];
  /** Called on item click. Wire to the parent's Xrm.Navigation modal open. */
  onOpenRecord?: (entityType: string, entityId: string) => void;
  /**
   * r5 email-share #3 (2026-07-09) — called when the user clicks the per-item
   * "Email" affordance ("share this item with a colleague"). The parent opens the
   * shared email dialog, composes a body from the item's structured fields + a deep
   * link, and creates a draft email activity. Xrm-free per ADR-021 — this is a pure
   * callback. When omitted, the Email affordance is not rendered (back-compat).
   */
  onEmailItem?: (item: HighPriorityItemResult) => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface ActionBadgeStyle {
  label: string;
  color: 'danger' | 'warning' | 'informative' | 'subtle';
  appearance: 'filled' | 'outline' | 'tint';
}

/**
 * Map the server-computed `action` string to a Fluent v9 Badge style + label.
 * Word-only labels (no dates) with the softer `tint` appearance — the due date
 * is conveyed by the row content, not the pill (operator design call, 2026-07-09).
 */
export function actionToBadge(action: string): ActionBadgeStyle | null {
  switch (action) {
    case 'Overdue':
      return { label: 'Overdue', color: 'danger', appearance: 'tint' };
    case 'DueToday':
      return { label: 'Due today', color: 'warning', appearance: 'tint' };
    case 'DueSoon':
      return { label: 'Due soon', color: 'informative', appearance: 'tint' };
    case 'Recent':
      return { label: 'Recently updated', color: 'subtle', appearance: 'tint' };
    default:
      return null;
  }
}

/**
 * Translate the reason enum into a short chip label. Empty string when reason
 * is missing (widget just omits the chip).
 */
export function reasonToLabel(reason?: string): string {
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

export const HighPrioritySection: React.FC<HighPrioritySectionProps> = ({ items, onOpenRecord, onEmailItem }) => {
  const styles = useStyles();

  if (!Array.isArray(items) || items.length === 0) {
    return null;
  }

  const handleOpen = (entityType: string, entityId: string): void => {
    if (!entityType || !entityId) return;
    onOpenRecord?.(entityType, entityId);
  };

  return (
    <div className={styles.container} role="region" aria-label="Critical today">
      <div className={styles.header}>
        <AlertUrgentRegular className={styles.headerIcon} aria-hidden="true" />
        <Text as="h2" size={500} className={styles.headerText}>
          Critical Today
        </Text>
        <Text size={400} className={styles.headerCount}>
          ({items.length})
        </Text>
      </div>
      <div className={styles.cardList}>
        {items.map(item => {
          // Every critical item carries a status pill; items with no due-date
          // action fall back to a subtle "Flagged" pill (the reason tags —
          // HighPriority/Monitor — are intentionally not surfaced to the reader).
          const badge: ActionBadgeStyle = actionToBadge(item.action ?? 'None') ?? {
            label: 'Flagged',
            color: 'subtle',
            appearance: 'tint',
          };
          const accent = ACCENT_BY_COLOR[badge.color] ?? tokens.colorNeutralStroke1;
          return (
            <div
              key={`${item.entityType}-${item.entityId}`}
              className={styles.card}
              style={{ borderLeftColor: accent }}
            >
              <div className={styles.itemRowTop}>
                {item.kindLabel && (
                  <Text size={100} className={styles.kindChip}>
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
                  <Badge appearance={badge.appearance} color={badge.color} size="small">
                    {badge.label}
                  </Badge>
                  {onEmailItem && (
                    <Tooltip content="Email this item to a colleague" relationship="label">
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={<MailRegular />}
                        aria-label={`Email ${item.name || 'item'} to a colleague`}
                        onClick={() => onEmailItem(item)}
                      />
                    </Tooltip>
                  )}
                </div>
              </div>
              {item.description && (
                <Text size={200} className={styles.description}>
                  {item.description}
                </Text>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
};
