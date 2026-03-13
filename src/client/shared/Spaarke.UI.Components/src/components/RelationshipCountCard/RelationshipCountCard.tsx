/**
 * RelationshipCountCard - Displays a count of semantically related documents
 * with drill-through capability.
 *
 * Callback-based component with zero service dependencies.
 * Supports loading, error, zero-count, and normal states.
 *
 * @see ADR-012 - Shared component library (callback-based props)
 * @see ADR-021 - Fluent UI v9 design tokens
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Card,
  Text,
  Spinner,
  Button,
  Badge,
  mergeClasses,
} from "@fluentui/react-components";
import {
  Open16Regular,
  ArrowSync20Regular,
  Warning20Regular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IRelationshipCountCardProps {
  /** Card title — no longer displayed but kept for API compatibility. */
  title?: string;
  /** Number of semantically related documents. */
  count: number;
  /** Whether the count is currently being loaded. */
  isLoading?: boolean;
  /** Error message to display. Pass null or undefined for no error. */
  error?: string | null;
  /** Called when the user clicks to open/drill-through to related documents. */
  onOpen: () => void;
  /** Called when the user clicks the refresh button. */
  onRefresh?: () => void;
  /** Timestamp of the last relationship analysis. */
  lastUpdated?: Date;
  /** Optional graph preview element rendered above the count when count > 0. */
  graphPreview?: React.ReactElement | null;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM + " " + tokens.spacingHorizontalM,
    minWidth: "200px",
    cursor: "default",
  },
  topRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalXS,
  },
  /** When graph preview is present: count left, graph right */
  graphRow: {
    display: "flex",
    alignItems: "stretch",
    gap: "0px",
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
  },
  countColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    flexBasis: "50%",
    width: "50%",
    padding: tokens.spacingVerticalS + " " + tokens.spacingHorizontalM,
  },
  graphPreviewContainer: {
    display: "flex",
    justifyContent: "center",
    alignItems: "center",
    flexBasis: "50%",
    width: "50%",
    minHeight: "120px",
    overflow: "hidden",
  },
  /** Fallback body when no graph preview */
  body: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
  },
  countContainer: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  count: {
    fontSize: "42px",
    fontWeight: tokens.fontWeightBold,
    lineHeight: "48px",
    color: tokens.colorNeutralForeground1,
  },
  countLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  zeroCount: {
    color: tokens.colorNeutralForeground3,
  },
  zeroLabel: {
    color: tokens.colorNeutralForeground3,
  },
  spinnerContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "48px",
  },
  errorContainer: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorPaletteRedForeground1,
  },
  errorIcon: {
    color: tokens.colorPaletteRedForeground1,
    flexShrink: 0,
  },
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-end",
  },
  lastUpdated: {
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Format a Date for display as a relative or short timestamp.
 */
function formatLastUpdated(date: Date): string {
  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(date);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RelationshipCountCard: React.FC<IRelationshipCountCardProps> = ({
  count,
  isLoading = false,
  error,
  onOpen,
  onRefresh,
  lastUpdated,
  graphPreview,
}) => {
  const styles = useStyles();

  // Action buttons row (top-right: refresh + open)
  const isZero = count === 0;
  const actionRow = (
    <div className={styles.topRow}>
      {onRefresh && (
        <Button
          appearance="subtle"
          icon={<ArrowSync20Regular />}
          size="small"
          onClick={onRefresh}
          title="Refresh"
        />
      )}
      {!isZero && !isLoading && !error && (
        <Button
          appearance="subtle"
          icon={<Open16Regular />}
          size="small"
          onClick={onOpen}
          title="Open full viewer"
        />
      )}
    </div>
  );

  // ── Loading state ────────────────────────────────────────────────────
  if (isLoading) {
    return (
      <Card className={styles.card}>
        {actionRow}
        <div className={styles.spinnerContainer}>
          <Spinner size="small" label="Loading..." />
        </div>
      </Card>
    );
  }

  // ── Error state ──────────────────────────────────────────────────────
  if (error) {
    return (
      <Card className={styles.card}>
        {actionRow}
        <div className={styles.errorContainer}>
          <Warning20Regular className={styles.errorIcon} />
          <Text size={200}>{error}</Text>
        </div>
      </Card>
    );
  }

  // ── Normal / Zero-count state ────────────────────────────────────────
  const hasGraph = !isZero && graphPreview;

  return (
    <Card className={styles.card}>
      {actionRow}
      {hasGraph ? (
        /* Graph layout: count left | graph right */
        <div className={styles.graphRow}>
          <div className={styles.countColumn}>
            <Text className={styles.count}>{count > 99 ? "99+" : count}</Text>
            <Text className={styles.countLabel}>Similar</Text>
          </div>
          <div className={styles.graphPreviewContainer}>{graphPreview}</div>
        </div>
      ) : (
        /* No-graph layout: count + badge inline */
        <div className={styles.body}>
          <div className={styles.countContainer}>
            <Text
              className={mergeClasses(styles.count, isZero && styles.zeroCount)}
            >
              {count}
            </Text>
            {isZero && (
              <Text size={200} className={styles.zeroLabel}>
                No related documents found
              </Text>
            )}
            {!isZero && (
              <Badge appearance="filled" color="brand" size="small">
                found
              </Badge>
            )}
          </div>
        </div>
      )}
      {lastUpdated && (
        <div className={styles.footer}>
          <Text className={styles.lastUpdated} size={100}>
            Updated {formatLastUpdated(lastUpdated)}
          </Text>
        </div>
      )}
    </Card>
  );
};

export default RelationshipCountCard;
