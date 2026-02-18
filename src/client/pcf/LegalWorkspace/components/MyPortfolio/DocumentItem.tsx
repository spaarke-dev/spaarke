/**
 * DocumentItem — a single row in the My Portfolio Documents tab.
 *
 * Displays:
 *   - File type icon: DocumentPdfRegular (PDF), DocumentTextRegular (DOCX),
 *     TableRegular (XLSX), DocumentRegular (other/unknown)
 *   - Document name (bold, truncated) — clicking navigates to the document record
 *   - Description (truncated to 1 line)
 *   - Document type and matter name as secondary metadata
 *   - Last modified timestamp (relative or formatted)
 *
 * Layout mirrors MatterItem.tsx:
 *   - Left column: icon + document info (name + secondary text)
 *   - Right column: document type badge + timestamp
 *
 * All colours are Fluent UI v9 semantic palette tokens — no hardcoded hex.
 * Clicking anywhere on the row navigates to the document record in MDA.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Badge,
} from '@fluentui/react-components';
import {
  DocumentPdfRegular,
  DocumentTextRegular,
  TableRegular,
  DocumentRegular,
} from '@fluentui/react-icons';
import { IDocument } from '../../types/entities';
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
  fileIcon: {
    flex: '0 0 auto',
    color: tokens.colorNeutralForeground3,
    fontSize: '20px',
    display: 'flex',
    alignItems: 'center',
  },
  documentInfo: {
    flex: '1 1 auto',
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  documentName: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  description: {
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  secondaryText: {
    color: tokens.colorNeutralForeground4,
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
// File type icon helpers
// ---------------------------------------------------------------------------

/**
 * Derive the file extension from a document name.
 * Returns lowercase extension without the dot, or empty string.
 */
function getFileExtension(name: string): string {
  const lastDot = name.lastIndexOf('.');
  if (lastDot === -1 || lastDot === name.length - 1) return '';
  return name.substring(lastDot + 1).toLowerCase();
}

/**
 * Choose the appropriate @fluentui/react-icons icon based on the file extension.
 *
 * - PDF  → DocumentPdfRegular
 * - DOCX → DocumentTextRegular
 * - XLSX → TableRegular
 * - Other → DocumentRegular
 */
function getFileIcon(name: string): React.ReactElement {
  const ext = getFileExtension(name);
  switch (ext) {
    case 'pdf':
      return <DocumentPdfRegular />;
    case 'doc':
    case 'docx':
      return <DocumentTextRegular />;
    case 'xls':
    case 'xlsx':
      return <TableRegular />;
    default:
      return <DocumentRegular />;
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

export interface IDocumentItemProps {
  /** The document entity from Dataverse */
  document: IDocument;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * DocumentItem renders a single document as a clickable row inside the
 * My Portfolio Documents tab. Clicking navigates to the document record in MDA.
 */
export const DocumentItem: React.FC<IDocumentItemProps> = ({ document }) => {
  const styles = useStyles();

  const fileIcon = getFileIcon(document.sprk_name);
  const timestamp = formatTimestamp(document.modifiedon);

  // ---------------------------------------------------------------------------
  // Navigation
  // ---------------------------------------------------------------------------

  const handleNavigate = React.useCallback(() => {
    navigateToEntity({
      action: 'openRecord',
      entityName: 'sprk_document',
      entityId: document.sprk_documentid,
    });
  }, [document.sprk_documentid]);

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
      aria-label={`Document: ${document.sprk_name}.${document.sprk_type ? ` Type: ${document.sprk_type}.` : ''}`}
    >
      {/* File type icon */}
      <span className={styles.fileIcon} aria-hidden="true">
        {fileIcon}
      </span>

      {/* Center: document name, description, secondary metadata */}
      <div className={styles.documentInfo}>
        <Text size={200} className={styles.documentName}>
          {document.sprk_name}
        </Text>

        {document.sprk_description && (
          <Text size={100} className={styles.description}>
            {document.sprk_description}
          </Text>
        )}
      </div>

      {/* Right: document type badge + timestamp */}
      <div className={styles.rightColumn}>
        {document.sprk_type && (
          <Badge
            size="small"
            color="informative"
            appearance="tint"
            aria-label={`Document type: ${document.sprk_type}`}
          >
            {document.sprk_type}
          </Badge>
        )}

        {timestamp && (
          <Text size={100} className={styles.timestamp} aria-label={`Last modified: ${timestamp}`}>
            {timestamp}
          </Text>
        )}
      </div>
    </div>
  );
};
