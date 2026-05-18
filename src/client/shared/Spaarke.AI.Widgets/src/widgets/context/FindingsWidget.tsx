/**
 * @spaarke/ai-widgets — FindingsWidget
 *
 * Context pane widget that displays structured analysis findings during the
 * sources-citations stage. Renders each finding as a card with a risk level
 * Badge, description text (expandable), and clickable citation links.
 *
 * Clicking a citation link dispatches a `context_highlight` event to the
 * 'context' PaneEventBus channel carrying the citationId so that the active
 * DocumentViewer widget can scroll to / highlight the referenced passage.
 *
 * Design constraints (ADR-021):
 * - All colours via Fluent v9 tokens — zero hard-coded hex values.
 * - Risk level Badge colours use Fluent v9 semantic colour system:
 *     high   → 'danger'
 *     medium → 'warning'
 *     low    → 'success'
 *     info   → 'informative'
 * - Dark-mode compatible by construction (tokens adapt automatically).
 * - makeStyles (Griffel) for all custom styles.
 * - Empty findings list renders an informational message — never a blank pane.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-089
 *
 * @see ContextWidgetRegistry — registered under 'findings'
 * @see useDispatchPaneEvent — dispatches context_highlight events
 * @see ADR-021 — Fluent v9 design system (makeStyles + tokens only)
 */

import React, { useState } from 'react';
import {
  makeStyles,
  tokens,
  Badge,
  Text,
  Button,
  mergeClasses,
  Divider,
} from '@fluentui/react-components';
import {
  ChevronDownRegular,
  ChevronUpRegular,
  LinkRegular,
  DocumentSearchRegular,
} from '@fluentui/react-icons';
import { useDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import type { ContextWidgetProps } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Data shape
// ---------------------------------------------------------------------------

/** Risk level for a finding — drives Badge colour. */
export type RiskLevel = 'high' | 'medium' | 'low' | 'info';

/**
 * A single citation reference attached to a finding.
 *
 * The `citationId` is the opaque reference used by DocumentViewer to locate
 * the passage. `displayLabel` is the human-readable link text shown in the UI.
 */
export interface Citation {
  /** Opaque citation reference passed to context_highlight events. */
  citationId: string;
  /** Human-readable label rendered as the link text, e.g. "§ 12.3, p. 4". */
  displayLabel: string;
}

/**
 * A single analysis finding.
 *
 * `id` must be unique within the findings array — it is used as the React key.
 * `citations` is optional; if absent the finding renders without citation links.
 * `detail` is optional; if present a chevron toggle reveals the full text.
 * `sectionRef` is optional metadata (not rendered directly).
 */
export interface Finding {
  /** Unique stable identifier for this finding (used as React key). */
  id: string;
  /** Short title or summary shown as the card heading. */
  title: string;
  /** Brief description rendered below the title (always visible). */
  description: string;
  /** Risk level — controls Badge colour. */
  riskLevel: RiskLevel;
  /** Optional list of citations supporting this finding. */
  citations?: Citation[];
  /** Optional extended detail text revealed by the expand toggle. */
  detail?: string;
  /** Optional section reference metadata (e.g. clause number) — not rendered. */
  sectionRef?: string;
}

/**
 * Data payload for FindingsWidget.
 * Delivered via the AI streaming response context_update event payload.
 */
export interface FindingsData {
  /**
   * Optional widget-level title override. When absent the widget renders
   * the default heading "Analysis Findings".
   */
  title?: string;
  /** Ordered list of findings to display. May be empty. */
  findings: Finding[];
}

// ---------------------------------------------------------------------------
// Risk level → Fluent v9 Badge colour mapping
// ---------------------------------------------------------------------------

/**
 * Maps a RiskLevel to the Fluent v9 Badge `color` prop value.
 *
 * Fluent v9 Badge colour options:
 *   'danger'      → red  (colorPaletteRedBackground2 family)
 *   'warning'     → yellow (colorPaletteYellowBackground2 family)
 *   'success'     → green (colorPaletteGreenBackground2 family)
 *   'informative' → blue (colorPaletteBlueBackground2 family)
 *
 * The token mapping is handled internally by Fluent — we never reference
 * colour tokens directly on the badge (ADR-021: no hard-coded colours).
 */
function getRiskBadgeColor(
  riskLevel: RiskLevel
): 'danger' | 'warning' | 'success' | 'informative' {
  switch (riskLevel) {
    case 'high':
      return 'danger';
    case 'medium':
      return 'warning';
    case 'low':
      return 'success';
    case 'info':
    default:
      return 'informative';
  }
}

/**
 * Human-readable label for each risk level shown inside the Badge.
 */
function getRiskLabel(riskLevel: RiskLevel): string {
  switch (riskLevel) {
    case 'high':
      return 'High Risk';
    case 'medium':
      return 'Medium Risk';
    case 'low':
      return 'Low Risk';
    case 'info':
    default:
      return 'Info';
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

  // ── Header ─────────────────────────────────────────────────────────────────
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  title: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
  },

  // ── Finding card ───────────────────────────────────────────────────────────
  findingCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },

  // ── Finding card header row (badge + title) ────────────────────────────────
  cardHeaderRow: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  cardTitle: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
    flex: 1,
    minWidth: 0,
    wordBreak: 'break-word',
  },

  // ── Description ────────────────────────────────────────────────────────────
  description: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    wordBreak: 'break-word',
  },

  // ── Detail expand/collapse ─────────────────────────────────────────────────
  detailToggleRow: {
    display: 'flex',
    alignItems: 'center',
  },
  detailToggleButton: {
    // Override Button defaults — minimal appearance, text-sized
    minWidth: 'unset',
    padding: '0',
    height: 'auto',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorBrandForeground1,
    gap: tokens.spacingHorizontalXS,
  },
  detailText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    lineHeight: tokens.lineHeightBase200,
    wordBreak: 'break-word',
    paddingTop: tokens.spacingVerticalXS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },

  // ── Citation links ─────────────────────────────────────────────────────────
  citationList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalXS,
  },
  citationRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  citationIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase200,
    flexShrink: 0,
  },
  citationButton: {
    // Inline link style — no box, text colour only
    minWidth: 'unset',
    padding: '0',
    height: 'auto',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorBrandForeground1,
    textDecoration: 'underline',
    textDecorationColor: tokens.colorBrandForeground1,
    ':hover': {
      color: tokens.colorBrandForeground2,
      textDecorationColor: tokens.colorBrandForeground2,
    },
  },

  // ── Empty state ────────────────────────────────────────────────────────────
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalS,
    padding: `${tokens.spacingVerticalXXL} ${tokens.spacingHorizontalM}`,
    flex: 1,
    textAlign: 'center',
  },
  emptyIcon: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeHero700,
  },
  emptyText: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
    lineHeight: tokens.lineHeightBase300,
  },

  // ── Error state ────────────────────────────────────────────────────────────
  errorText: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase300,
  },
});

// ---------------------------------------------------------------------------
// FindingCard sub-component
// ---------------------------------------------------------------------------

interface FindingCardProps {
  finding: Finding;
  onCitationClick: (citationId: string) => void;
  styles: ReturnType<typeof useStyles>;
}

function FindingCard({ finding, onCitationClick, styles }: FindingCardProps): React.JSX.Element {
  const [detailExpanded, setDetailExpanded] = useState(false);

  const badgeColor = getRiskBadgeColor(finding.riskLevel);
  const badgeLabel = getRiskLabel(finding.riskLevel);
  const hasCitations = finding.citations != null && finding.citations.length > 0;
  const hasDetail = finding.detail != null && finding.detail.trim().length > 0;

  return (
    <div className={styles.findingCard} data-testid={`finding-card-${finding.id}`}>
      {/* ── Risk badge + title ── */}
      <div className={styles.cardHeaderRow}>
        <Badge
          appearance="filled"
          color={badgeColor}
          size="small"
          data-testid={`risk-badge-${finding.id}`}
        >
          {badgeLabel}
        </Badge>
        <Text className={styles.cardTitle}>{finding.title}</Text>
      </div>

      {/* ── Description (always visible) ── */}
      <Text className={styles.description}>{finding.description}</Text>

      {/* ── Detail expand/collapse ── */}
      {hasDetail && (
        <>
          <div className={styles.detailToggleRow}>
            <Button
              appearance="transparent"
              size="small"
              className={styles.detailToggleButton}
              icon={detailExpanded ? <ChevronUpRegular /> : <ChevronDownRegular />}
              iconPosition="after"
              onClick={() => setDetailExpanded((prev) => !prev)}
              aria-expanded={detailExpanded}
              aria-label={detailExpanded ? 'Collapse detail' : 'Expand detail'}
            >
              {detailExpanded ? 'Less' : 'More'}
            </Button>
          </div>
          {detailExpanded && (
            <Text className={styles.detailText}>{finding.detail}</Text>
          )}
        </>
      )}

      {/* ── Citation links ── */}
      {hasCitations && (
        <div className={styles.citationList} aria-label="Citations">
          {finding.citations!.map((citation) => (
            <div key={citation.citationId} className={styles.citationRow}>
              <LinkRegular className={styles.citationIcon} />
              <Button
                appearance="transparent"
                size="small"
                className={styles.citationButton}
                onClick={() => onCitationClick(citation.citationId)}
                data-testid={`citation-link-${citation.citationId}`}
                aria-label={`View citation: ${citation.displayLabel}`}
              >
                {citation.displayLabel}
              </Button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// FindingsWidget component
// ---------------------------------------------------------------------------

/**
 * FindingsWidget — context pane widget for structured analysis findings.
 *
 * Renders findings from the `data` prop. Each finding card shows:
 *   - A risk level Badge (Fluent v9 semantic colour — no hard-coded colours)
 *   - Title and description text
 *   - Optional expandable detail text
 *   - Optional citation links that dispatch `context_highlight` events
 *
 * Citation link clicks dispatch to the 'context' PaneEventBus channel so that
 * the active DocumentViewer widget (also listening on 'context') can scroll
 * to and highlight the cited passage.
 *
 * Props follow ContextWidgetProps<FindingsData>. The shell passes:
 *   - `data`       — FindingsData payload from the SSE context_update event
 *   - `isLoading`  — reserved for future skeleton state (not yet implemented)
 *   - `widgetType` — "findings" (informational; not used for rendering)
 *   - `error`      — optional error message (rendered as an error state)
 *   - `className`  — optional root class name override
 */
const FindingsWidget: React.FC<ContextWidgetProps<FindingsData>> = ({
  data,
  isLoading = false,
  error,
  className,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // ------------------------------------------------------------------
  // Citation click handler
  // Dispatches context_highlight to 'context' channel so DocumentViewer
  // can scroll to / highlight the referenced passage.
  // ------------------------------------------------------------------
  function handleCitationClick(citationId: string): void {
    dispatch('context', {
      type: 'context_highlight',
      citationId,
    });
  }

  // ------------------------------------------------------------------
  // Error state
  // ------------------------------------------------------------------
  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  // ------------------------------------------------------------------
  // Loading state placeholder (skeleton not yet designed for findings)
  // ------------------------------------------------------------------
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText} style={{ color: tokens.colorNeutralForeground3 }}>
          Loading findings…
        </Text>
      </div>
    );
  }

  const { title = 'Analysis Findings', findings } = data;
  const hasFindings = findings != null && findings.length > 0;

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* ── Widget title ── */}
      <div className={styles.header}>
        <Text className={styles.title}>{title}</Text>
      </div>

      <Divider />

      {/* ── Findings list or empty state ── */}
      {hasFindings ? (
        findings.map((finding) => (
          <FindingCard
            key={finding.id}
            finding={finding}
            onCitationClick={handleCitationClick}
            styles={styles}
          />
        ))
      ) : (
        <div className={styles.emptyState}>
          <DocumentSearchRegular className={styles.emptyIcon} />
          <Text className={styles.emptyText}>No findings identified</Text>
        </div>
      )}
    </div>
  );
};

export default FindingsWidget;
