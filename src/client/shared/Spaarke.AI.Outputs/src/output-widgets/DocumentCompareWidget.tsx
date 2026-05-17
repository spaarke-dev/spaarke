/**
 * DocumentCompareWidget
 *
 * Renders a side-by-side or unified diff view of two document versions.
 * Changed lines are highlighted using Fluent v9 status color tokens:
 *   - added   → colorStatusSuccessBackground3
 *   - removed → colorStatusDangerBackground3
 *   - changed → colorStatusWarningBackground3
 *   - unchanged → no highlight
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data shape injected via the AI streaming response (already parsed by the
 * calling code page). No direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Text, Badge, Spinner, ToggleButton } from '@fluentui/react-components';
import type { OutputWidgetProps } from '../types';

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export type DocumentChangeType = 'added' | 'removed' | 'changed' | 'unchanged';

export interface DocumentCompareLine {
  /** Unique identifier for this line pair. */
  id: string;
  /** Text content from the left (original) document. */
  leftText: string;
  /** Text content from the right (revised) document. */
  rightText: string;
  /** Classification of what changed between left and right. */
  changeType: DocumentChangeType;
}

export interface DocumentCompareData {
  /** Display label for the left (original) document. */
  leftLabel: string;
  /** Display label for the right (revised) document. */
  rightLabel: string;
  /** Ordered list of line comparison entries. */
  lines: DocumentCompareLine[];
  /** Default view mode; can be toggled by the user in the widget. */
  viewMode?: 'side-by-side' | 'unified';
}

export type DocumentCompareWidgetProps = OutputWidgetProps<DocumentCompareData>;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    height: '100%',
    overflow: 'hidden',
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
    padding: tokens.spacingHorizontalL,
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalL}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  toolbarSpacer: {
    flexGrow: 1,
  },
  scrollArea: {
    overflowY: 'auto',
    flexGrow: 1,
  },
  // Side-by-side: two-column CSS grid
  sideBySideGrid: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: 0,
  },
  sideBySideHeader: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: 0,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  headerCell: {
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground3,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
  },
  headerCellLeft: {
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // Row cells
  lineCell: {
    padding: `2px ${tokens.spacingHorizontalM}`,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    minHeight: '22px',
    lineHeight: tokens.lineHeightBase300,
  },
  lineCellLeft: {
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // Unified view: single column
  unifiedRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: 0,
  },
  unifiedLineLabel: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    padding: `0 ${tokens.spacingHorizontalM}`,
  },
  // Change-type background colors (ADR-021: Fluent status tokens)
  bgAdded: {
    backgroundColor: tokens.colorStatusSuccessBackground3,
  },
  bgRemoved: {
    backgroundColor: tokens.colorStatusDangerBackground3,
  },
  bgChanged: {
    backgroundColor: tokens.colorStatusWarningBackground3,
  },
  bgUnchanged: {
    // No highlight — inherits default background
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function getChangeStyleKey(changeType: DocumentChangeType): 'bgAdded' | 'bgRemoved' | 'bgChanged' | 'bgUnchanged' {
  switch (changeType) {
    case 'added':
      return 'bgAdded';
    case 'removed':
      return 'bgRemoved';
    case 'changed':
      return 'bgChanged';
    default:
      return 'bgUnchanged';
  }
}

function changeTypeBadgeColor(changeType: DocumentChangeType): 'success' | 'danger' | 'warning' | undefined {
  switch (changeType) {
    case 'added':
      return 'success';
    case 'removed':
      return 'danger';
    case 'changed':
      return 'warning';
    default:
      return undefined;
  }
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

interface SideBySideViewProps {
  lines: DocumentCompareLine[];
  leftLabel: string;
  rightLabel: string;
  styles: ReturnType<typeof useStyles>;
}

function SideBySideView({ lines, leftLabel, rightLabel, styles }: SideBySideViewProps): React.ReactElement {
  return (
    <>
      <div className={styles.sideBySideHeader}>
        <div className={mergeClasses(styles.headerCell, styles.headerCellLeft)}>{leftLabel}</div>
        <div className={styles.headerCell}>{rightLabel}</div>
      </div>
      <div className={styles.scrollArea}>
        <div className={styles.sideBySideGrid}>
          {lines.map(line => {
            const bgKey = getChangeStyleKey(line.changeType);
            return (
              <React.Fragment key={line.id}>
                <div className={mergeClasses(styles.lineCell, styles.lineCellLeft, styles[bgKey])}>{line.leftText}</div>
                <div className={mergeClasses(styles.lineCell, styles[bgKey])}>{line.rightText}</div>
              </React.Fragment>
            );
          })}
        </div>
      </div>
    </>
  );
}

interface UnifiedViewProps {
  lines: DocumentCompareLine[];
  leftLabel: string;
  rightLabel: string;
  styles: ReturnType<typeof useStyles>;
}

function UnifiedView({ lines, leftLabel, rightLabel, styles }: UnifiedViewProps): React.ReactElement {
  return (
    <div className={styles.scrollArea}>
      {lines.map(line => {
        const bgKey = getChangeStyleKey(line.changeType);
        const isChanged = line.changeType !== 'unchanged';

        return (
          <div key={line.id} className={styles.unifiedRow}>
            {isChanged && line.leftText !== line.rightText ? (
              <>
                {line.leftText && (
                  <div className={mergeClasses(styles.lineCell, styles.bgRemoved)}>
                    <span className={styles.unifiedLineLabel}>− {leftLabel}: </span>
                    {line.leftText}
                  </div>
                )}
                {line.rightText && (
                  <div className={mergeClasses(styles.lineCell, styles.bgAdded)}>
                    <span className={styles.unifiedLineLabel}>+ {rightLabel}: </span>
                    {line.rightText}
                  </div>
                )}
              </>
            ) : (
              <div className={mergeClasses(styles.lineCell, styles[bgKey])}>{line.leftText || line.rightText}</div>
            )}
          </div>
        );
      })}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * DocumentCompareWidget renders a diff view between two document versions.
 * The user can toggle between side-by-side and unified view modes.
 * Changed lines are color-coded using Fluent v9 status color tokens.
 */
export default function DocumentCompareWidget({
  data,
  isLoading,
  error,
  className,
}: DocumentCompareWidgetProps): React.ReactElement {
  const styles = useStyles();
  const [viewMode, setViewMode] = React.useState<'side-by-side' | 'unified'>(data?.viewMode ?? 'side-by-side');

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading document comparison..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  const addedCount = data.lines.filter(l => l.changeType === 'added').length;
  const removedCount = data.lines.filter(l => l.changeType === 'removed').length;
  const changedCount = data.lines.filter(l => l.changeType === 'changed').length;

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Toolbar */}
      <div className={styles.toolbar}>
        {addedCount > 0 && (
          <Badge color="success" appearance="filled">
            +{addedCount}
          </Badge>
        )}
        {removedCount > 0 && (
          <Badge color="danger" appearance="filled">
            -{removedCount}
          </Badge>
        )}
        {changedCount > 0 && (
          <Badge color="warning" appearance="filled">
            ~{changedCount}
          </Badge>
        )}
        <div className={styles.toolbarSpacer} />
        <ToggleButton size="small" checked={viewMode === 'side-by-side'} onClick={() => setViewMode('side-by-side')}>
          Side by side
        </ToggleButton>
        <ToggleButton size="small" checked={viewMode === 'unified'} onClick={() => setViewMode('unified')}>
          Unified
        </ToggleButton>
      </div>

      {/* Content */}
      {viewMode === 'side-by-side' ? (
        <SideBySideView lines={data.lines} leftLabel={data.leftLabel} rightLabel={data.rightLabel} styles={styles} />
      ) : (
        <UnifiedView lines={data.lines} leftLabel={data.leftLabel} rightLabel={data.rightLabel} styles={styles} />
      )}
    </div>
  );
}
