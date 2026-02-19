/**
 * ProjectItem — a single row in the My Portfolio Projects tab.
 *
 * Displays:
 *   - Project name (bold, truncated) — clicking navigates to the project record
 *   - Status badge: Active (success/green) or Planning (brand/blue)
 *   - Project type and practice area as secondary text
 *   - Last activity timestamp (modifiedon, relative or formatted)
 *
 * Layout follows the same visual rhythm as MatterItem.tsx:
 *   - Left column: project info (name + secondary text)
 *   - Right column: status badge + timestamp
 *
 * All colours are Fluent UI v9 semantic palette tokens — no hardcoded hex.
 * Clicking anywhere on the row navigates to the project record in MDA.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Badge,
} from '@fluentui/react-components';
import { IProject } from '../../types/entities';
import { navigateToEntity } from '../../utils/navigation';

// ---------------------------------------------------------------------------
// Styles — mirrors the rhythm of MatterItem.tsx
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
  projectInfo: {
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
  projectName: {
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
  timestamp: {
    color: tokens.colorNeutralForeground4,
  },
});

// ---------------------------------------------------------------------------
// Status helpers
// ---------------------------------------------------------------------------

/** Dataverse sprk_project.sprk_status option set integer values */
const PROJECT_STATUS_ACTIVE = 1;
const PROJECT_STATUS_PLANNING = 0;

type ProjectBadgeColor =
  | 'danger'
  | 'warning'
  | 'success'
  | 'brand'
  | 'informative'
  | 'important'
  | 'severe'
  | 'subtle';

/**
 * Map a raw Dataverse sprk_status integer to a display label and badge colour.
 * Unmapped values fall back to "Planning" (brand blue).
 */
function getStatusDisplay(status: number | undefined): {
  label: string;
  color: ProjectBadgeColor;
} {
  switch (status) {
    case PROJECT_STATUS_ACTIVE:
      return { label: 'Active', color: 'success' };
    case PROJECT_STATUS_PLANNING:
    default:
      return { label: 'Planning', color: 'brand' };
  }
}

// ---------------------------------------------------------------------------
// Timestamp formatting
// ---------------------------------------------------------------------------

/**
 * Format an ISO date string into a compact relative or absolute string.
 * Returns strings like "Today", "Yesterday", "3d ago", "Jan 5".
 */
function formatTimestamp(isoDate: string): string {
  const date = new Date(isoDate);
  if (isNaN(date.getTime())) return '';

  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

  if (diffDays === 0) return 'Today';
  if (diffDays === 1) return 'Yesterday';
  if (diffDays < 30) return `${diffDays}d ago`;

  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IProjectItemProps {
  /** The project entity from Dataverse */
  project: IProject;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ProjectItem renders a single project as a clickable row inside the
 * My Portfolio Projects tab. Clicking navigates to the project record in MDA.
 */
export const ProjectItem: React.FC<IProjectItemProps> = ({ project }) => {
  const styles = useStyles();

  const { label: statusLabel, color: statusColor } = getStatusDisplay(project.sprk_status);
  const timestamp = formatTimestamp(project.modifiedon);

  // Secondary line: prefer practiceArea, fall back to type, then owner lookup
  const secondaryParts: string[] = [];
  if (project.sprk_practicearea) secondaryParts.push(project.sprk_practicearea);
  if (project.projectTypeName) secondaryParts.push(project.projectTypeName);
  const secondaryText = secondaryParts.join(' · ');

  // ---------------------------------------------------------------------------
  // Navigation
  // ---------------------------------------------------------------------------

  const handleNavigate = React.useCallback(() => {
    navigateToEntity({
      action: 'openRecord',
      entityName: 'sprk_project',
      entityId: project.sprk_projectid,
    });
  }, [project.sprk_projectid]);

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
      aria-label={`Project: ${project.sprk_name}. Status: ${statusLabel}.`}
    >
      {/* Left: project name and secondary text */}
      <div className={styles.projectInfo}>
        <div className={styles.nameRow}>
          <Text size={200} className={styles.projectName}>
            {project.sprk_name}
          </Text>
        </div>

        {secondaryText && (
          <Text size={100} className={styles.secondaryText}>
            {secondaryText}
          </Text>
        )}
      </div>

      {/* Right: status badge and timestamp */}
      <div className={styles.rightColumn}>
        <Badge
          size="small"
          color={statusColor}
          appearance="filled"
          aria-label={`Status: ${statusLabel}`}
        >
          {statusLabel}
        </Badge>

        {timestamp && (
          <Text size={100} className={styles.timestamp} aria-label={`Last modified: ${timestamp}`}>
            {timestamp}
          </Text>
        )}
      </div>
    </div>
  );
};
