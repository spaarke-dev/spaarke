/**
 * @spaarke/ai-widgets — RedlineViewerWidget
 *
 * Workspace widget that renders a side-by-side document comparison (redline)
 * produced by the server-side CompareDocumentsTool (task AIPU2-042).
 *
 * Layout: left column shows the original document text; right column shows the
 * modified version. Changed sections are highlighted with Fluent v9 semantic
 * color tokens — additions (green), deletions (red), modifications (yellow).
 * Unchanged sections collapse by default and can be expanded individually.
 * A navigation sidebar lists changed sections for quick jumping.
 *
 * Serialize/restore (D-08 — data-refreshed restore): only documentAId,
 * documentBId, and comparisonId are serialized. On restore the BFF comparison
 * endpoint is re-called to produce fresh diff data; stale snapshots are never
 * rehydrated directly.
 *
 * ADR-021 compliance:
 * - All colors via Fluent v9 tokens — no hard-coded values.
 * - makeStyles (Griffel) for all custom styling.
 * - Dark mode: semantic palette tokens adapt automatically.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-085
 */

import React, { useCallback, useRef, useState } from 'react';
import {
  Badge,
  Button,
  Card,
  CardHeader,
  Divider,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  ChevronDown20Regular,
  ChevronRight20Regular,
  ColumnDoubleCompareRegular,
  ErrorCircle20Regular,
  Navigation20Regular,
} from '@fluentui/react-icons';
import type { WorkspaceWidgetProps } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Data types (mirrors server-side CompareDocumentsTool output)
// ---------------------------------------------------------------------------

/** Change classification for a diff entry, matching server DiffChangeType enum. */
export type DiffChangeType = 'Addition' | 'Deletion' | 'Modification' | 'Unchanged';

/** A single word-level change within a section (mirrors server DiffChange record). */
export interface DiffChange {
  changeType: DiffChangeType;
  originalText?: string | null;
  modifiedText?: string | null;
  changeDescription?: string | null;
}

/**
 * Diff result for a single named section (mirrors server SectionDiff record).
 */
export interface DiffSection {
  sectionTitle: string;
  changeType: DiffChangeType;
  changes: DiffChange[];
}

/**
 * Full document diff payload delivered by the server (mirrors DocumentDiff record).
 * JSON keys use camelCase as serialised by System.Text.Json.
 */
export interface RedlineViewerData {
  /** Dataverse sprk_document GUID for the first (original/baseline) document. */
  documentId1: string;
  /** Dataverse sprk_document GUID for the second (revised/modified) document. */
  documentId2: string;
  /** ISO 8601 timestamp of the comparison. */
  comparedAt: string;
  totalSections: number;
  totalChanges: number;
  additions: number;
  deletions: number;
  modifications: number;
  sections: DiffSection[];
  isError?: boolean;
  errorMessage?: string | null;
}

/** Actions exposed by this widget (read-only; no toolbar actions for now). */
export type RedlineViewerActions = never;

/**
 * Serialized state for Cosmos DB persistence (D-08: identifiers only).
 * Matches the queryParams shape used by WidgetState<RedlineViewerData>.
 */
export interface RedlineViewerQueryParams {
  documentAId: string;
  documentBId: string;
  comparisonId: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Root layout
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // Header bar
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexShrink: 0,
    flexWrap: 'wrap',
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  headerIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  headerBadges: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
    alignItems: 'center',
  },
  headerSpacer: {
    flex: 1,
  },

  // Nav toggle button
  navToggle: {
    flexShrink: 0,
  },

  // Main content area (nav + columns)
  contentArea: {
    display: 'flex',
    flex: 1,
    minHeight: 0,
    gap: tokens.spacingHorizontalS,
  },

  // Section navigation sidebar
  nav: {
    display: 'flex',
    flexDirection: 'column',
    width: '220px',
    flexShrink: 0,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingRight: tokens.spacingHorizontalS,
    overflowY: 'auto',
    gap: tokens.spacingVerticalXXS,
  },
  navHidden: {
    display: 'none',
  },
  navHeading: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    paddingBottom: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
  navItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusMedium,
    cursor: 'pointer',
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground2,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
      color: tokens.colorNeutralForeground1,
    },
  },
  navItemActive: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
  },
  navDot: {
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    flexShrink: 0,
  },
  navDotAddition: {
    backgroundColor: tokens.colorPaletteGreenForeground1,
  },
  navDotDeletion: {
    backgroundColor: tokens.colorPaletteRedForeground1,
  },
  navDotModification: {
    backgroundColor: tokens.colorPaletteYellowForeground1,
  },
  navDotUnchanged: {
    backgroundColor: tokens.colorNeutralForeground4,
  },
  navLabel: {
    flex: 1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },

  // Two-column diff area
  diffArea: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    overflowY: 'auto',
    gap: tokens.spacingVerticalS,
  },

  // Column headers row
  columnHeaders: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
    position: 'sticky',
    top: 0,
    zIndex: 1,
    backgroundColor: tokens.colorNeutralBackground1,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  columnHeader: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    paddingLeft: tokens.spacingHorizontalS,
  },

  // Section block
  section: {
    display: 'flex',
    flexDirection: 'column',
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    overflow: 'hidden',
  },

  // Section header (title row, always visible)
  sectionHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    cursor: 'pointer',
    backgroundColor: tokens.colorNeutralBackground2,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  sectionHeaderAddition: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
    ':hover': {
      backgroundColor: tokens.colorPaletteGreenBackground1,
    },
  },
  sectionHeaderDeletion: {
    backgroundColor: tokens.colorPaletteRedBackground2,
    ':hover': {
      backgroundColor: tokens.colorPaletteRedBackground1,
    },
  },
  sectionHeaderModification: {
    backgroundColor: tokens.colorPaletteYellowBackground2,
    ':hover': {
      backgroundColor: tokens.colorPaletteYellowBackground1,
    },
  },
  sectionTitle: {
    flex: 1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  chevronIcon: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
  },

  // Section body — two-column grid
  sectionBody: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: '1px',
    backgroundColor: tokens.colorNeutralStroke2,
  },
  sectionBodyHidden: {
    display: 'none',
  },

  // Individual column cell within a section
  cell: {
    padding: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground1,
    fontFamily: tokens.fontFamilyBase,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    minHeight: '48px',
  },
  cellAddition: {
    backgroundColor: tokens.colorPaletteGreenBackground2,
  },
  cellDeletion: {
    backgroundColor: tokens.colorPaletteRedBackground2,
  },
  cellModification: {
    backgroundColor: tokens.colorPaletteYellowBackground2,
  },
  cellEmpty: {
    color: tokens.colorNeutralForeground4,
    fontStyle: 'italic',
  },

  // Change entry within a cell
  changeEntry: {
    marginBottom: tokens.spacingVerticalXXS,
    padding: tokens.spacingVerticalXXS,
    borderRadius: tokens.borderRadiusSmall,
  },
  changeEntryAddition: {
    backgroundColor: tokens.colorPaletteGreenBackground1,
    borderLeft: `3px solid ${tokens.colorPaletteGreenForeground1}`,
  },
  changeEntryDeletion: {
    backgroundColor: tokens.colorPaletteRedBackground1,
    borderLeft: `3px solid ${tokens.colorPaletteRedForeground1}`,
    textDecoration: 'line-through',
    color: tokens.colorPaletteRedForeground2,
  },
  changeEntryModification: {
    backgroundColor: tokens.colorPaletteYellowBackground1,
    borderLeft: `3px solid ${tokens.colorPaletteYellowForeground2}`,
  },

  // States
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  errorContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  errorCard: {
    width: '100%',
    maxWidth: '560px',
  },
  errorIcon: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase500,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground2,
    fontSize: tokens.fontSizeBase300,
  },
  emptyContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    color: tokens.colorNeutralForeground3,
    gap: tokens.spacingVerticalM,
  },
  summaryRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
    alignItems: 'center',
    flexShrink: 0,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
});

// ---------------------------------------------------------------------------
// Helper utilities
// ---------------------------------------------------------------------------

/** Return the section-level header style modifier based on change type. */
function sectionHeaderClass(styles: ReturnType<typeof useStyles>, changeType: DiffChangeType): string {
  switch (changeType) {
    case 'Addition':
      return styles.sectionHeaderAddition;
    case 'Deletion':
      return styles.sectionHeaderDeletion;
    case 'Modification':
      return styles.sectionHeaderModification;
    default:
      return '';
  }
}

/** Return the cell modifier class based on change type. */
function cellClass(styles: ReturnType<typeof useStyles>, changeType: DiffChangeType): string {
  switch (changeType) {
    case 'Addition':
      return styles.cellAddition;
    case 'Deletion':
      return styles.cellDeletion;
    case 'Modification':
      return styles.cellModification;
    default:
      return '';
  }
}

/** Return the change entry modifier class. */
function changeEntryClass(styles: ReturnType<typeof useStyles>, changeType: DiffChangeType): string {
  switch (changeType) {
    case 'Addition':
      return styles.changeEntryAddition;
    case 'Deletion':
      return styles.changeEntryDeletion;
    case 'Modification':
      return styles.changeEntryModification;
    default:
      return '';
  }
}

/** Human-readable label for a change type badge. */
function changeTypeLabel(changeType: DiffChangeType): string {
  switch (changeType) {
    case 'Addition':
      return 'Added';
    case 'Deletion':
      return 'Deleted';
    case 'Modification':
      return 'Modified';
    case 'Unchanged':
      return 'Unchanged';
  }
}

/** Fluent Badge appearance for the change type. */
function changeTypeBadgeColor(changeType: DiffChangeType): 'success' | 'danger' | 'warning' | 'subtle' {
  switch (changeType) {
    case 'Addition':
      return 'success';
    case 'Deletion':
      return 'danger';
    case 'Modification':
      return 'warning';
    default:
      return 'subtle';
  }
}

/** Dot CSS class for nav sidebar indicator. */
function navDotClass(styles: ReturnType<typeof useStyles>, changeType: DiffChangeType): string {
  switch (changeType) {
    case 'Addition':
      return styles.navDotAddition;
    case 'Deletion':
      return styles.navDotDeletion;
    case 'Modification':
      return styles.navDotModification;
    default:
      return styles.navDotUnchanged;
  }
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

interface SectionRowProps {
  section: DiffSection;
  index: number;
  isExpanded: boolean;
  onToggle: (index: number) => void;
  sectionRef: (el: HTMLDivElement | null) => void;
  styles: ReturnType<typeof useStyles>;
  isActive: boolean;
}

/**
 * Renders one section of the diff as a collapsible two-column block.
 * Unchanged sections start collapsed; changed sections start expanded.
 */
const SectionRow: React.FC<SectionRowProps> = ({
  section,
  index,
  isExpanded,
  onToggle,
  sectionRef,
  styles,
  isActive,
}) => {
  const { sectionTitle, changeType, changes } = section;

  // Build original and modified text from the change list.
  // For a section-level Deletion: left shows the original body, right is empty.
  // For a section-level Addition: left is empty, right shows the modified body.
  // For a Modification: left shows original words, right shows modified words.
  // For Unchanged: both sides show the same collapsed indicator.
  const originalParts: DiffChange[] = [];
  const modifiedParts: DiffChange[] = [];

  if (changeType === 'Deletion') {
    originalParts.push(...changes);
  } else if (changeType === 'Addition') {
    modifiedParts.push(...changes);
  } else if (changeType === 'Modification') {
    // Word-level changes: show on the appropriate side
    for (const change of changes) {
      if (change.changeType === 'Deletion' || change.changeType === 'Modification') {
        originalParts.push(change);
      }
      if (change.changeType === 'Addition' || change.changeType === 'Modification') {
        modifiedParts.push(change);
      }
    }
  }
  // Unchanged: no parts — cells render the placeholder

  const headerModifier = sectionHeaderClass(styles, changeType);

  return (
    <div className={styles.section} ref={sectionRef} aria-label={`Section: ${sectionTitle}`}>
      {/* Section header — click to toggle expand/collapse */}
      <div
        className={mergeClasses(styles.sectionHeader, headerModifier)}
        onClick={() => onToggle(index)}
        role="button"
        aria-expanded={isExpanded}
        tabIndex={0}
        onKeyDown={e => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            onToggle(index);
          }
        }}
      >
        {isExpanded ? (
          <ChevronDown20Regular className={styles.chevronIcon} />
        ) : (
          <ChevronRight20Regular className={styles.chevronIcon} />
        )}
        <Text className={styles.sectionTitle} title={sectionTitle}>
          {sectionTitle}
        </Text>
        <Badge appearance="tint" color={changeTypeBadgeColor(changeType)} size="small">
          {changeTypeLabel(changeType)}
        </Badge>
      </div>

      {/* Section body — two-column grid */}
      <div className={mergeClasses(styles.sectionBody, !isExpanded && styles.sectionBodyHidden)}>
        {/* Original (left) column */}
        <div
          className={mergeClasses(
            styles.cell,
            changeType !== 'Unchanged' && cellClass(styles, changeType),
            originalParts.length === 0 && changeType !== 'Unchanged' && styles.cellEmpty
          )}
          aria-label="Original text"
        >
          {changeType === 'Unchanged' ? (
            <Text style={{ color: tokens.colorNeutralForeground4, fontStyle: 'italic' }}>— Unchanged —</Text>
          ) : originalParts.length === 0 ? (
            <Text style={{ color: tokens.colorNeutralForeground4, fontStyle: 'italic' }}>
              (not present in original)
            </Text>
          ) : (
            originalParts.map((change, i) => (
              <div key={i} className={mergeClasses(styles.changeEntry, changeEntryClass(styles, change.changeType))}>
                {change.originalText ?? ''}
              </div>
            ))
          )}
        </div>

        {/* Modified (right) column */}
        <div
          className={mergeClasses(
            styles.cell,
            changeType !== 'Unchanged' && cellClass(styles, changeType),
            modifiedParts.length === 0 && changeType !== 'Unchanged' && styles.cellEmpty
          )}
          aria-label="Modified text"
        >
          {changeType === 'Unchanged' ? (
            <Text style={{ color: tokens.colorNeutralForeground4, fontStyle: 'italic' }}>— Unchanged —</Text>
          ) : modifiedParts.length === 0 ? (
            <Text style={{ color: tokens.colorNeutralForeground4, fontStyle: 'italic' }}>(not present in revised)</Text>
          ) : (
            modifiedParts.map((change, i) => (
              <div key={i} className={mergeClasses(styles.changeEntry, changeEntryClass(styles, change.changeType))}>
                {change.modifiedText ?? change.originalText ?? ''}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

/**
 * RedlineViewerWidget
 *
 * Renders the DocumentDiff payload from CompareDocumentsTool as a side-by-side
 * redline view with section navigation.
 *
 * Satisfies WorkspaceWidget<RedlineViewerData, RedlineViewerActions>.
 * serializeState / restoreState are implemented as static methods and exposed
 * as named exports for the registry entry wiring.
 */
const RedlineViewerWidget: React.FC<WorkspaceWidgetProps<RedlineViewerData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();

  // Track expanded/collapsed state per section index.
  // Changed sections default to expanded; unchanged sections default to collapsed.
  const [expandedSections, setExpandedSections] = useState<Set<number>>(() => {
    if (!data?.sections) return new Set<number>();
    const initial = new Set<number>();
    data.sections.forEach((s, i) => {
      if (s.changeType !== 'Unchanged') initial.add(i);
    });
    return initial;
  });

  const [navVisible, setNavVisible] = useState(true);
  const [activeSection, setActiveSection] = useState<number | null>(null);

  // Refs to each section DOM element for scrolling.
  const sectionRefs = useRef<(HTMLDivElement | null)[]>([]);

  const handleToggleSection = useCallback((index: number) => {
    setExpandedSections(prev => {
      const next = new Set(prev);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
  }, []);

  const handleNavClick = useCallback((index: number) => {
    setActiveSection(index);
    // Ensure the section is expanded
    setExpandedSections(prev => {
      if (prev.has(index)) return prev;
      const next = new Set(prev);
      next.add(index);
      return next;
    });
    // Scroll to the section
    const el = sectionRefs.current[index];
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, []);

  // ---------------------------------------------------------------------------
  // Loading state
  // ---------------------------------------------------------------------------
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.loadingContainer}>
          <Spinner size="medium" label="Loading document comparison…" />
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Error state (prop-level or data-level)
  // ---------------------------------------------------------------------------
  const displayError = error ?? (data?.isError ? data.errorMessage : null);
  if (displayError) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.errorContainer}>
          <Card className={styles.errorCard}>
            <CardHeader
              image={<ErrorCircle20Regular className={styles.errorIcon} />}
              header={
                <Text weight="semibold" className={styles.errorText}>
                  Document comparison failed
                </Text>
              }
              description={<Text style={{ color: tokens.colorNeutralForeground3 }}>{displayError}</Text>}
            />
          </Card>
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Empty / no data state
  // ---------------------------------------------------------------------------
  if (!data?.sections || data.sections.length === 0) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.emptyContainer}>
          <ColumnDoubleCompareRegular style={{ fontSize: '48px', color: tokens.colorNeutralForeground4 }} />
          <Text style={{ color: tokens.colorNeutralForeground3 }}>
            No sections to display. Both documents may be identical.
          </Text>
        </div>
      </div>
    );
  }

  const { sections, additions, deletions, modifications, totalSections, totalChanges, comparedAt } = data;

  // Filter to only changed sections for the nav sidebar
  const changedSections = sections
    .map((s, i) => ({ section: s, index: i }))
    .filter(({ section }) => section.changeType !== 'Unchanged');

  // Format comparison timestamp
  let comparedAtLabel = '';
  try {
    comparedAtLabel = new Date(comparedAt).toLocaleString(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
    });
  } catch {
    comparedAtLabel = comparedAt ?? '';
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Header */}
      <div className={styles.header}>
        <div className={styles.headerTitle}>
          <ColumnDoubleCompareRegular className={styles.headerIcon} />
          <Text weight="semibold" size={400}>
            Document Comparison
          </Text>
        </div>

        <div className={styles.headerBadges}>
          {additions > 0 && (
            <Tooltip content={`${additions} added section(s)`} relationship="label">
              <Badge appearance="tint" color="success" size="small">
                +{additions} added
              </Badge>
            </Tooltip>
          )}
          {deletions > 0 && (
            <Tooltip content={`${deletions} deleted section(s)`} relationship="label">
              <Badge appearance="tint" color="danger" size="small">
                -{deletions} deleted
              </Badge>
            </Tooltip>
          )}
          {modifications > 0 && (
            <Tooltip content={`${modifications} modified section(s)`} relationship="label">
              <Badge appearance="tint" color="warning" size="small">
                ~{modifications} modified
              </Badge>
            </Tooltip>
          )}
          {totalChanges === 0 && (
            <Badge appearance="tint" color="subtle" size="small">
              No changes
            </Badge>
          )}
        </div>

        <div className={styles.headerSpacer} />

        {comparedAtLabel && (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3, flexShrink: 0 }}>
            Compared {comparedAtLabel}
          </Text>
        )}

        <Tooltip content={navVisible ? 'Hide section navigation' : 'Show section navigation'} relationship="label">
          <Button
            className={styles.navToggle}
            appearance="subtle"
            size="small"
            icon={<Navigation20Regular />}
            onClick={() => setNavVisible(v => !v)}
            aria-label={navVisible ? 'Hide navigation' : 'Show navigation'}
          />
        </Tooltip>
      </div>

      <Divider />

      {/* Main content: nav sidebar + diff columns */}
      <div className={styles.contentArea}>
        {/* Section navigation sidebar */}
        <nav className={mergeClasses(styles.nav, !navVisible && styles.navHidden)} aria-label="Section navigation">
          <Text className={styles.navHeading}>Changed Sections ({changedSections.length})</Text>
          {changedSections.length === 0 && (
            <Text size={200} style={{ color: tokens.colorNeutralForeground4, fontStyle: 'italic' }}>
              All sections unchanged
            </Text>
          )}
          {changedSections.map(({ section, index }) => (
            <div
              key={index}
              className={mergeClasses(styles.navItem, activeSection === index && styles.navItemActive)}
              onClick={() => handleNavClick(index)}
              role="button"
              tabIndex={0}
              aria-label={`Navigate to ${section.sectionTitle}`}
              onKeyDown={e => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  handleNavClick(index);
                }
              }}
            >
              <div className={mergeClasses(styles.navDot, navDotClass(styles, section.changeType))} aria-hidden />
              <span className={styles.navLabel} title={section.sectionTitle}>
                {section.sectionTitle}
              </span>
            </div>
          ))}
        </nav>

        {/* Diff view */}
        <div className={styles.diffArea} role="region" aria-label="Document diff">
          {/* Column header labels (sticky) */}
          <div className={styles.columnHeaders} aria-hidden>
            <Text className={styles.columnHeader}>Original</Text>
            <Text className={styles.columnHeader}>Revised</Text>
          </div>

          {/* Section rows */}
          {sections.map((section, index) => (
            <SectionRow
              key={`${section.sectionTitle}-${index}`}
              section={section}
              index={index}
              isExpanded={expandedSections.has(index)}
              onToggle={handleToggleSection}
              isActive={activeSection === index}
              sectionRef={el => {
                sectionRefs.current[index] = el;
              }}
              styles={styles}
            />
          ))}
        </div>
      </div>

      {/* Summary footer */}
      <div className={styles.summaryRow} aria-label="Comparison summary">
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          {totalSections} section{totalSections !== 1 ? 's' : ''} total
        </Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground4 }}>
          ·
        </Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          {totalChanges} change{totalChanges !== 1 ? 's' : ''} detected
        </Text>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// serializeState / restoreState — static helpers
//
// These are attached as named exports so the registry bootstrap can call them.
// The component itself is a functional component; the serialization contract
// (WorkspaceWidget<TData>.serializeState / restoreState) is satisfied by the
// registry entry wrapper in register-workspace-widgets.ts.
// ---------------------------------------------------------------------------

/**
 * Serialize only the document identifiers needed to re-fetch the diff on restore.
 * D-08 compliance: stores query params, NOT the full diff payload.
 *
 * @param documentAId - Dataverse GUID for the original document.
 * @param documentBId - Dataverse GUID for the revised document.
 * @param comparisonId - Optional stable comparison identifier (e.g. session-scoped key).
 */
export function serializeRedlineState(
  documentAId: string,
  documentBId: string,
  comparisonId: string
): {
  widgetType: string;
  version: number;
  queryParams: RedlineViewerQueryParams;
  timestamp: string;
} {
  return {
    widgetType: 'redline-viewer',
    version: 1,
    queryParams: {
      documentAId,
      documentBId,
      comparisonId,
    },
    timestamp: new Date().toISOString(),
  };
}

export default RedlineViewerWidget;
