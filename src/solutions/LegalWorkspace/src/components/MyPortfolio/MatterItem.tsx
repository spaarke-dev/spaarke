/**
 * MatterItem — a single row in the My Portfolio Matters tab.
 *
 * Displays:
 *   - Matter name (primary text, truncated)
 *   - Practice area / type (secondary text)
 *   - Status badge (Critical / Warning / OnTrack) with semantic colour
 *   - Three grade pills (Budget Controls, Guidelines, Outcomes)
 *   - Overdue indicator icon when sprk_overdueeventcount > 0
 *
 * All colours are Fluent UI v9 semantic palette tokens — no hardcoded hex.
 * Clicking anywhere on the row navigates to the matter record in MDA.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  mergeClasses,
  Tooltip,
} from '@fluentui/react-components';
import { AlertRegular } from '@fluentui/react-icons';
import { IMatter } from '../../types/entities';
import { MatterStatus, GradeLevel } from '../../types/enums';
import { deriveMatterStatus, extractMatterGrades, isMatterOverdue } from '../../utils/statusDerivation';
import { navigateToEntity } from '../../utils/navigation';
import { GradePill } from './GradePill';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  row: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    transition: 'background-color 0.15s ease',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorBrandForeground1,
      outlineOffset: '-2px',
    },
    ':last-child': {
      borderBottomWidth: '0px',
    },
  },
  matterInfo: {
    flex: '1 1 auto',
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  nameRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
  },
  matterName: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flex: '1 1 auto',
    minWidth: 0,
  },
  secondaryText: {
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  rightColumn: {
    flex: '0 0 auto',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-end',
    gap: tokens.spacingVerticalXXS,
  },
  gradeRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  overdueIcon: {
    color: tokens.colorPaletteCranberryForeground2,
    fontSize: '14px',
    lineHeight: '1',
    display: 'flex',
    alignItems: 'center',
    flex: '0 0 auto',
  },
});

// ---------------------------------------------------------------------------
// Status badge helpers
// ---------------------------------------------------------------------------

/** Map MatterStatus to Fluent Badge color prop */
type BadgeColor = 'danger' | 'warning' | 'success' | 'brand' | 'informative' | 'important' | 'severe' | 'subtle';

function getStatusBadgeColor(status: MatterStatus): BadgeColor {
  switch (status) {
    case 'Critical': return 'danger';
    case 'Warning':  return 'warning';
    case 'OnTrack':  return 'success';
  }
}

function getStatusLabel(status: MatterStatus): string {
  switch (status) {
    case 'Critical': return 'Critical';
    case 'Warning':  return 'Warning';
    case 'OnTrack':  return 'On Track';
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IMatterItemProps {
  /** The matter entity from Dataverse */
  matter: IMatter;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * MatterItem renders a single matter as a clickable row inside the
 * My Portfolio Matters tab. Clicking navigates to the matter record in MDA.
 *
 * React.memo: prevents re-renders when the parent re-renders but the matter
 * object reference has not changed. With 500+ matters in the portfolio list,
 * this eliminates O(N) re-renders on every parent state update (NFR-07).
 */
export const MatterItem: React.FC<IMatterItemProps> = React.memo(({ matter }) => {
  const styles = useStyles();

  // Derived values — computed from raw entity fields
  const status = deriveMatterStatus(matter);
  const overdue = isMatterOverdue(matter);
  const { budgetControlsGrade, guidelinesComplianceGrade, outcomesSuccessGrade } =
    extractMatterGrades(matter);

  const statusBadgeColor = getStatusBadgeColor(status);
  const statusLabel = getStatusLabel(status);

  // Secondary line: prefer practiceArea, fall back to type
  const secondaryText = matter.practiceAreaName ?? matter.matterTypeName ?? '';

  // ---------------------------------------------------------------------------
  // Navigation
  // ---------------------------------------------------------------------------

  const handleNavigate = React.useCallback(() => {
    navigateToEntity({
      action: 'openRecord',
      entityName: 'sprk_matter',
      entityId: matter.sprk_matterid,
    });
  }, [matter.sprk_matterid]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        handleNavigate();
      }
    },
    [handleNavigate]
  );

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div
      className={styles.row}
      role="listitem"
      tabIndex={0}
      onClick={handleNavigate}
      onKeyDown={handleKeyDown}
      aria-label={`Matter: ${matter.sprk_name}. Status: ${statusLabel}.${overdue ? ' Has overdue events.' : ''}`}
    >
      {/* Left: matter name and secondary text */}
      <div className={styles.matterInfo}>
        <div className={styles.nameRow}>
          <Text size={200} className={styles.matterName}>
            {matter.sprk_name}
          </Text>

          {/* Overdue indicator */}
          {overdue && (
            <Tooltip
              content={`${matter.sprk_overdueeventcount} overdue event${matter.sprk_overdueeventcount !== 1 ? 's' : ''}`}
              relationship="label"
            >
              <span className={styles.overdueIcon} aria-hidden="true">
                <AlertRegular />
              </span>
            </Tooltip>
          )}
        </div>

        {secondaryText && (
          <Text size={100} className={styles.secondaryText}>
            {secondaryText}
          </Text>
        )}
      </div>

      {/* Right: status badge and grade pills */}
      <div className={styles.rightColumn}>
        <Badge
          size="small"
          color={statusBadgeColor}
          appearance="filled"
          aria-label={`Status: ${statusLabel}`}
        >
          {statusLabel}
        </Badge>

        {/* Grade pills — only rendered when at least one grade is present */}
        {(budgetControlsGrade || guidelinesComplianceGrade || outcomesSuccessGrade) && (
          <div className={styles.gradeRow} aria-label="Grades">
            {budgetControlsGrade && (
              <GradePill
                grade={budgetControlsGrade as GradeLevel}
                dimensionLabel="Budget Controls"
              />
            )}
            {guidelinesComplianceGrade && (
              <GradePill
                grade={guidelinesComplianceGrade as GradeLevel}
                dimensionLabel="Guidelines Compliance"
              />
            )}
            {outcomesSuccessGrade && (
              <GradePill
                grade={outcomesSuccessGrade as GradeLevel}
                dimensionLabel="Outcomes Success"
              />
            )}
          </div>
        )}
      </div>
    </div>
  );
});

MatterItem.displayName = 'MatterItem';
