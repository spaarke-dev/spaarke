/**
 * @spaarke/ai-widgets — EntityInfoWidget
 *
 * Context pane widget that displays entity detail during the entity-info stage.
 * Renders matter name, client, status, key dates, budget, and any custom fields
 * passed in the context_update event payload.
 *
 * Design principles:
 * - No hard-coded colors — all styling via Fluent v9 tokens (ADR-021).
 * - No hard-coded entity schemas — renders whatever fields arrive in the payload.
 * - Absent optional fields are simply not rendered (no empty rows).
 * - Skeleton loading state while entity data is incomplete (isLoading === true).
 * - Reacts to subsequent context_update events via usePaneEvent on the 'context'
 *   channel, updating state when a new entityId arrives.
 *
 * Task: AIPU2-087
 * FR:   FR-201, FR-204
 *
 * @see ContextWidgetRegistry — registered under 'entity-info'
 * @see usePaneEvent — subscription to context_update events
 * @see ADR-021 — Fluent v9 design system (makeStyles + tokens only)
 */

import React, { useState } from 'react';
import {
  makeStyles,
  tokens,
  Badge,
  Text,
  Skeleton,
  SkeletonItem,
  mergeClasses,
  Divider,
} from '@fluentui/react-components';
import {
  CalendarRegular,
  MoneyRegular,
  PersonRegular,
  BuildingRegular,
} from '@fluentui/react-icons';
import { usePaneEvent } from '../../events/usePaneEvent';
import type { ContextWidgetProps } from '../../types/widget-types';
import type { ContextPaneEvent } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Data shape
// ---------------------------------------------------------------------------

/** A single key date entry (label + ISO date string). */
export interface KeyDate {
  /** Human-readable label, e.g. "Filing Deadline" */
  label: string;
  /** ISO 8601 date string, e.g. "2026-09-30" */
  date: string;
}

/** Budget summary — spent vs. total, with optional currency code. */
export interface BudgetSummary {
  /** Total budget amount (numeric). */
  total: number;
  /** Amount spent to date (numeric). */
  spent: number;
  /** ISO 4217 currency code, e.g. "USD". Defaults to "USD" if absent. */
  currency?: string;
}

/**
 * Data shape delivered to EntityInfoWidget via the context_update event payload.
 *
 * All fields except entityType, entityId, and displayName are optional.
 * Missing optional fields are omitted from the rendered output — no empty rows.
 */
export interface EntityInfoData {
  /** Entity type discriminant, e.g. "matter", "contract", "project". */
  entityType: string;
  /** Dataverse record GUID for the entity. */
  entityId: string;
  /** Human-readable entity name shown as the widget heading. */
  displayName: string;
  /** Status value, e.g. "Active", "Closed", "Draft". */
  status?: string;
  /** Client name associated with the entity. */
  clientName?: string;
  /** Owner / responsible person name. */
  ownerName?: string;
  /** Ordered list of key dates. */
  keyDates?: KeyDate[];
  /** Budget summary (spent vs. total). Rendered as a progress bar if present. */
  budget?: BudgetSummary;
  /** Arbitrary additional label/value pairs rendered as a definition list. */
  customFields?: Record<string, string>;
}

// ---------------------------------------------------------------------------
// Status badge color mapping
// ---------------------------------------------------------------------------

/**
 * Maps well-known status strings to Fluent v9 Badge appearance/color combinations.
 * Falls back to "informative" for any unknown status value.
 */
function getStatusBadgeColor(
  status: string
): 'success' | 'warning' | 'danger' | 'important' | 'informative' | 'severe' {
  const normalized = status.toLowerCase();
  if (['active', 'open', 'in progress', 'approved'].includes(normalized)) return 'success';
  if (['closed', 'completed', 'resolved'].includes(normalized)) return 'informative';
  if (['draft', 'pending', 'review'].includes(normalized)) return 'warning';
  if (['cancelled', 'rejected', 'terminated'].includes(normalized)) return 'danger';
  if (['overdue', 'urgent'].includes(normalized)) return 'severe';
  return 'informative';
}

// ---------------------------------------------------------------------------
// Currency formatter
// ---------------------------------------------------------------------------

function formatCurrency(amount: number, currency = 'USD'): string {
  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency,
      maximumFractionDigits: 0,
    }).format(amount);
  } catch {
    return `${currency} ${amount.toLocaleString()}`;
  }
}

// ---------------------------------------------------------------------------
// Date formatter
// ---------------------------------------------------------------------------

function formatDate(isoDate: string): string {
  try {
    return new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    }).format(new Date(isoDate));
  } catch {
    return isoDate;
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalM,
    height: '100%',
    overflowY: 'auto',
    boxSizing: 'border-box',
  },

  // ── Header ──────────────────────────────────────────────────────────────
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  entityTypeBadgeRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  displayName: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase500,
    wordBreak: 'break-word',
  },

  // ── Section ──────────────────────────────────────────────────────────────
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  },

  // ── Key field row (label + value) ────────────────────────────────────────
  fieldRow: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    minHeight: '20px',
  },
  fieldIcon: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase400,
    flexShrink: 0,
    marginTop: '2px',
  },
  fieldContent: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minWidth: 0,
  },
  fieldKey: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  fieldValue: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },

  // ── Key dates ────────────────────────────────────────────────────────────
  dateList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  dateRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusSmall,
  },
  dateIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase300,
    flexShrink: 0,
  },
  dateLabel: {
    flex: 1,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    minWidth: 0,
  },
  dateValue: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    flexShrink: 0,
  },

  // ── Budget ───────────────────────────────────────────────────────────────
  budgetSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  budgetLabels: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  budgetSpent: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  budgetTotal: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  progressTrack: {
    height: '6px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralBackground4,
    overflow: 'hidden',
  },
  progressFill: {
    height: '100%',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground,
    transition: 'width 0.3s ease',
  },
  progressFillOver: {
    backgroundColor: tokens.colorPaletteRedBackground3,
  },
  budgetSubtext: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },

  // ── Custom fields (definition list) ──────────────────────────────────────
  customFieldList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  customFieldRow: {
    display: 'flex',
    flexDirection: 'column',
    padding: `${tokens.spacingVerticalXS} 0`,
  },
  customFieldKey: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    marginBottom: '2px',
  },
  customFieldValue: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },

  // ── Skeleton ──────────────────────────────────────────────────────────────
  skeletonRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalM,
  },
  skeletonHeader: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  skeletonSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

interface KeyFieldRowProps {
  label: string;
  value: string;
  icon: React.ReactElement;
  styles: ReturnType<typeof useStyles>;
}

function KeyFieldRow({ label, value, icon, styles }: KeyFieldRowProps): React.JSX.Element {
  return (
    <div className={styles.fieldRow}>
      <span className={styles.fieldIcon}>{icon}</span>
      <div className={styles.fieldContent}>
        <Text className={styles.fieldKey}>{label}</Text>
        <Text className={styles.fieldValue}>{value}</Text>
      </div>
    </div>
  );
}

interface BudgetBarProps {
  budget: BudgetSummary;
  styles: ReturnType<typeof useStyles>;
}

function BudgetBar({ budget, styles }: BudgetBarProps): React.JSX.Element {
  const { total, spent, currency = 'USD' } = budget;
  const pct = total > 0 ? Math.min((spent / total) * 100, 100) : 0;
  const isOver = total > 0 && spent > total;

  return (
    <div className={styles.budgetSection}>
      <div className={styles.budgetLabels}>
        <Text className={styles.budgetSpent}>{formatCurrency(spent, currency)} spent</Text>
        <Text className={styles.budgetTotal}>of {formatCurrency(total, currency)}</Text>
      </div>
      <div className={styles.progressTrack}>
        <div
          className={mergeClasses(styles.progressFill, isOver ? styles.progressFillOver : undefined)}
          style={{ width: `${pct}%` }}
          role="progressbar"
          aria-valuenow={Math.round(pct)}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label={`Budget: ${Math.round(pct)}% used`}
        />
      </div>
      {isOver && (
        <Text className={styles.budgetSubtext}>Over budget by {formatCurrency(spent - total, currency)}</Text>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Loading skeleton
// ---------------------------------------------------------------------------

function EntityInfoSkeleton({ className }: { className?: string }): React.JSX.Element {
  const styles = useStyles();
  return (
    <div className={mergeClasses(styles.skeletonRoot, className)}>
      <Skeleton>
        <div className={styles.skeletonHeader}>
          <SkeletonItem size={16} style={{ width: '60px' }} />
          <SkeletonItem size={20} style={{ width: '80%' }} />
        </div>
      </Skeleton>
      <Divider />
      <Skeleton>
        <div className={styles.skeletonSection}>
          <SkeletonItem size={12} style={{ width: '40px' }} />
          <SkeletonItem size={16} style={{ width: '70%' }} />
          <SkeletonItem size={16} style={{ width: '55%' }} />
          <SkeletonItem size={16} style={{ width: '65%' }} />
        </div>
      </Skeleton>
      <Divider />
      <Skeleton>
        <div className={styles.skeletonSection}>
          <SkeletonItem size={12} style={{ width: '50px' }} />
          <SkeletonItem size={32} style={{ width: '100%', borderRadius: '4px' }} />
          <SkeletonItem size={32} style={{ width: '100%', borderRadius: '4px' }} />
        </div>
      </Skeleton>
    </div>
  );
}

// ---------------------------------------------------------------------------
// EntityInfoWidget component
// ---------------------------------------------------------------------------

/**
 * EntityInfoWidget — context pane widget for entity detail during entity-info stage.
 *
 * Renders entity information from the `data` prop and subscribes to subsequent
 * context_update events via usePaneEvent so it updates reactively when the user
 * navigates to a different entity record.
 *
 * Props follow ContextWidgetProps<EntityInfoData>. The shell passes:
 *  - `data`       — EntityInfoData payload from the SSE event
 *  - `isLoading`  — true while initial data is being fetched (renders skeleton)
 *  - `widgetType` — "entity-info" (informational; not used for rendering)
 *  - `error`      — optional error message (rendered as an error state)
 *  - `className`  — optional root class name override
 */
const EntityInfoWidget: React.FC<ContextWidgetProps<EntityInfoData>> = ({
  data: initialData,
  isLoading = false,
  error,
  className,
}) => {
  const styles = useStyles();

  // Local state that tracks the most-recently received entity payload.
  // Initialised from props; updated when a context_update event arrives with
  // a new entityId so the widget renders fresh data without a prop re-render.
  const [data, setData] = useState<EntityInfoData>(initialData);

  // Subscribe to context_update events on the 'context' channel.
  // When a new entity-info event arrives with a different entityId, update state.
  usePaneEvent('context', (event: ContextPaneEvent) => {
    if (event.type !== 'context_update') return;
    if (!event.contextData) return;

    const incoming = event.contextData as EntityInfoData;
    // Only update if this is a different entity (avoids redundant re-renders).
    if (incoming.entityId && incoming.entityId !== data.entityId) {
      setData(incoming);
    }
  });

  // Skeleton loading state — rendered while the initial payload is being fetched.
  if (isLoading) {
    return <EntityInfoSkeleton className={className} />;
  }

  // Error state
  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text style={{ color: tokens.colorPaletteRedForeground1 }}>{error}</Text>
      </div>
    );
  }

  const {
    entityType,
    displayName,
    status,
    clientName,
    ownerName,
    keyDates,
    budget,
    customFields,
  } = data;

  // Build non-empty custom field entries once to avoid multiple Object.entries calls.
  const customFieldEntries = customFields
    ? Object.entries(customFields).filter(([, v]) => v != null && v !== '')
    : [];

  // Determine which key-field section rows to render (only non-empty values).
  const hasKeyFields = status || clientName || ownerName;
  const hasKeyDates = keyDates && keyDates.length > 0;
  const hasBudget = budget != null;
  const hasCustomFields = customFieldEntries.length > 0;

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* ── Header: entity type badge + display name ── */}
      <div className={styles.header}>
        <div className={styles.entityTypeBadgeRow}>
          <Badge appearance="outline" color="brand" size="small">
            {entityType}
          </Badge>
          {status && (
            <Badge appearance="filled" color={getStatusBadgeColor(status)} size="small">
              {status}
            </Badge>
          )}
        </div>
        <Text className={styles.displayName}>{displayName}</Text>
      </div>

      {/* ── Key fields: client, owner ── (status already in badge above) */}
      {hasKeyFields && (
        <>
          <Divider />
          <div className={styles.section}>
            <Text className={styles.sectionLabel}>Details</Text>
            {clientName && (
              <KeyFieldRow
                label="Client"
                value={clientName}
                icon={<BuildingRegular />}
                styles={styles}
              />
            )}
            {ownerName && (
              <KeyFieldRow
                label="Owner"
                value={ownerName}
                icon={<PersonRegular />}
                styles={styles}
              />
            )}
          </div>
        </>
      )}

      {/* ── Key dates: timeline-style list ── */}
      {hasKeyDates && (
        <>
          <Divider />
          <div className={styles.section}>
            <Text className={styles.sectionLabel}>Key Dates</Text>
            <div className={styles.dateList}>
              {keyDates!.map((kd) => (
                <div key={`${kd.label}-${kd.date}`} className={styles.dateRow}>
                  <CalendarRegular className={styles.dateIcon} />
                  <Text className={styles.dateLabel}>{kd.label}</Text>
                  <Text className={styles.dateValue}>{formatDate(kd.date)}</Text>
                </div>
              ))}
            </div>
          </div>
        </>
      )}

      {/* ── Budget: progress bar (spent vs. total) ── */}
      {hasBudget && (
        <>
          <Divider />
          <div className={styles.section}>
            <div className={styles.fieldRow}>
              <span className={styles.fieldIcon}>
                <MoneyRegular />
              </span>
              <Text className={styles.sectionLabel} style={{ marginBottom: 0 }}>
                Budget
              </Text>
            </div>
            <BudgetBar budget={budget!} styles={styles} />
          </div>
        </>
      )}

      {/* ── Custom fields: definition list ── */}
      {hasCustomFields && (
        <>
          <Divider />
          <div className={styles.section}>
            <Text className={styles.sectionLabel}>Additional Info</Text>
            <div className={styles.customFieldList}>
              {customFieldEntries.map(([key, value]) => (
                <div key={key} className={styles.customFieldRow}>
                  <Text className={styles.customFieldKey}>{key}</Text>
                  <Text className={styles.customFieldValue}>{value}</Text>
                </div>
              ))}
            </div>
          </div>
        </>
      )}
    </div>
  );
};

export default EntityInfoWidget;
