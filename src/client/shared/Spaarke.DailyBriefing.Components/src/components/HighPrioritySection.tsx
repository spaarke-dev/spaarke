/**
 * HighPrioritySection — cross-entity flagged-item roll-up shown above the TL;DR.
 *
 * R7 W12 feedback item 9 (2026-07-01):
 *   "create a top section above TL;DR with a light subtle red background title
 *    'High Priority'; this lists items that are due today or that are overdue
 *    and the associated record has value sprk_monitor = yes or sprk_priority
 *    (renamed sprk_highpriority in the shipped schema) = yes."
 *
 * MVP behavior (aligned with operator intent):
 *   - Renders the section only when items[].length > 0. Empty state → null
 *     (no wasted vertical space; the classic TL;DR fills the top).
 *   - Each item is a clickable Link that opens the record in a Dataverse modal
 *     (target:2, 80%×80%) via the parent's onOpenRecord callback.
 *   - Compact single-line rows: [kindLabel] · Name · optional overdue/today badge.
 *   - Subtle-red banner background using Fluent v9 semantic tokens
 *     (colorPaletteRedBackground1) — passes dark-mode adaptation per ADR-021.
 *   - Server-side sort is preserved (due-ascending, undated last).
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark-mode via semantic tokens.
 *   - Xrm-free: navigation happens in the parent via onOpenRecord.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Link,
  Badge,
} from '@fluentui/react-components';
import { AlertUrgentRegular } from '@fluentui/react-icons';

import type { HighPriorityItemResult } from '../services/briefingService';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    // Subtle red banner. colorPaletteRedBackground1 is Fluent's lightest red;
    // adapts in dark mode. Border adds visual anchor without heavy weight.
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
    marginBottom: tokens.spacingVerticalS,
  },
  headerIcon: {
    fontSize: '20px',
    color: tokens.colorPaletteRedForeground1,
  },
  headerText: {
    color: tokens.colorPaletteRedForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  itemRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    flexWrap: 'wrap',
  },
  kindLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    minWidth: '90px',
  },
  itemLink: {
    color: tokens.colorBrandForeground1,
    textDecorationLine: 'none',
    cursor: 'pointer',
    ':hover': {
      textDecorationLine: 'underline',
    },
    // Allow the name to grow but not push badges off-screen.
    flexShrink: 1,
    minWidth: 0,
  },
  badgeOverdue: {
    // Use Fluent's `severe` intent for overdue; distinct from the container's
    // red background so it still reads as urgent.
  },
  badgeDueToday: {
    // `warning` intent for due-today — softer than overdue.
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

/**
 * Classify a due date relative to "today UTC start" for badge selection.
 * Undated → null (no badge). Past → "overdue". Today → "today". Future → null
 * (no badge — the section is already showing them because they're flagged).
 */
type DueClassification = 'overdue' | 'today' | 'future' | null;

function classifyDueDate(iso: string | undefined): DueClassification {
  if (!iso) return null;
  try {
    const due = new Date(iso);
    if (Number.isNaN(due.getTime())) return null;
    const now = new Date();
    const todayStart = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
    const tomorrowStart = new Date(todayStart.getTime() + 24 * 60 * 60 * 1000);
    if (due < todayStart) return 'overdue';
    if (due < tomorrowStart) return 'today';
    return 'future';
  } catch {
    return null;
  }
}

function formatDueLabel(iso: string | undefined, classification: DueClassification): string | null {
  if (!iso || classification === null) return null;
  try {
    const due = new Date(iso);
    if (Number.isNaN(due.getTime())) return null;
    const shortDate = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(due);
    if (classification === 'overdue') return `Overdue · ${shortDate}`;
    if (classification === 'today') return `Due today`;
    return `Due ${shortDate}`;
  } catch {
    return null;
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
        const classification = classifyDueDate(item.dueDate);
        const dueLabel = formatDueLabel(item.dueDate, classification);
        return (
          <div key={`${item.entityType}-${item.entityId}`} className={styles.itemRow}>
            {item.kindLabel && (
              <Text size={200} className={styles.kindLabel}>
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
            {classification === 'overdue' && dueLabel && (
              <Badge appearance="filled" color="danger" size="small" className={styles.badgeOverdue}>
                {dueLabel}
              </Badge>
            )}
            {classification === 'today' && dueLabel && (
              <Badge appearance="filled" color="warning" size="small" className={styles.badgeDueToday}>
                {dueLabel}
              </Badge>
            )}
            {classification === 'future' && dueLabel && (
              <Badge appearance="outline" color="informative" size="small">
                {dueLabel}
              </Badge>
            )}
          </div>
        );
      })}
    </div>
  );
};
