/**
 * @spaarke/ai-widgets — SearchCriteriaResultWidget
 *
 * Minimal workspace widget that renders a summary of search criteria captured
 * by the Context-pane Semantic Search tool (Task 043 / W-5). Created as the
 * receiving widget for the first end-to-end Context → Workspace `widget_load`
 * demo per FR-03 / OC-R4-08.
 *
 * Demo scope (Risk R-7, R4 plan.original.md §8):
 *   This is intentionally a SHIM viewer that shows the search criteria
 *   (domain, query, filters, date range) as a workspace tab. It does NOT
 *   actually run the search or invoke the BFF AI Search endpoint — the modal
 *   Semantic Search page (sprk_semanticsearch) remains the production search
 *   surface. This widget proves the Context → Workspace mount-source pattern
 *   so future Context wizards can promote results into workspace tabs.
 *
 * Pattern reference: DocumentViewerWidget (task 042 / W-4 sibling). Both
 * widgets follow the same Pattern D shape (shared-lib widget + thin
 * registration shim) with a typed `widgetData` payload dispatched on the
 * `workspace` PaneEventBus channel.
 *
 * ADR compliance:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; context-agnostic
 *   - ADR-021: Fluent v9 semantic tokens only — no hex / rgba / Fluent v8
 *   - ADR-022: React 19, functional component + hooks only
 *   - ADR-028: no token snapshots; no BFF call in v1 (criteria are pure user input)
 *   - ADR-030: typed `widgetData` shape, no `any` casts
 *
 * React 19, NOT PCF-safe.
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Card,
  CardHeader,
  Text,
  Badge,
} from '@fluentui/react-components';
import { SearchRegular } from '@fluentui/react-icons';
import type { WorkspaceWidgetProps } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Typed `widgetData` payload consumed by SearchCriteriaResultWidget.
 *
 * Dispatched by the Context pane (R4 task 043 / W-5) when the user clicks
 * Search with the "Also add to Workspace" option checked in
 * SemanticSearchCriteriaTool. The Context pane constructs this payload from
 * the tool's local criteria state and embeds it inside a
 * `WorkspaceWidgetLoadEvent` payload on the `workspace` channel.
 *
 * Mirror of `PersistedCriteria` in
 * src/solutions/SpaarkeAi/src/components/context/SemanticSearchCriteriaTool.tsx
 * — replicated here as a typed payload contract so this widget remains
 * context-agnostic (ADR-012) and the dispatcher constructs a typed payload
 * (ADR-030, no `any`).
 *
 * Future extensions (out of scope for task 043 — defer to follow-up):
 *   - Embed actual search RESULTS (hits, scores, snippets) by calling the
 *     BFF AI Search endpoint via `authenticatedFetch` (ADR-028).
 *   - Per-result citation drilldown into the Context pane.
 */
export interface SearchCriteriaResultWidgetData {
  /** Free-text AI search query (may be empty). */
  query: string;
  /** Active search domain — one of documents / matters / projects / invoices. */
  domain: string;
  /** Document Type filter when domain === 'documents' (e.g. 'contract'). */
  documentType?: string;
  /** File Type filter when domain === 'documents' (e.g. 'pdf'). */
  fileType?: string;
  /** Matter Type filter when domain === 'matters' or 'documents'. */
  matterType?: string;
  /** Date range preset (off | last7 | last30 | last90 | custom). */
  dateRange?: string;
  /**
   * ISO-8601 timestamp at which the dispatcher constructed this payload.
   * Renders as "Captured at <time>" subtitle. Optional; widget tolerates
   * absence.
   */
  capturedAt?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  card: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
  },
  headerIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase600,
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  headerSubtitle: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  body: {
    flex: 1,
    overflow: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    minHeight: 0,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  sectionLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
  },
  sectionValue: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  badgeRow: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  emptyValue: {
    fontStyle: 'italic',
    color: tokens.colorNeutralForeground3,
  },
  emptyState: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalXL,
    textAlign: 'center',
  },
  noticeBanner: {
    flexShrink: 0,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Map dateRange preset id → human label. Mirrors SemanticSearchCriteriaTool. */
function formatDateRange(preset: string | undefined): string {
  switch (preset) {
    case 'last7':
      return 'Last 7 days';
    case 'last30':
      return 'Last 30 days';
    case 'last90':
      return 'Last 90 days';
    case 'custom':
      return 'Custom range';
    case 'off':
    case undefined:
    case '':
      return 'Off';
    default:
      return preset;
  }
}

/** Title-case helper for domain labels (documents → Documents). */
function titleCase(value: string): string {
  if (!value) return '';
  return value.charAt(0).toUpperCase() + value.slice(1);
}

/** Format the optional capturedAt ISO timestamp into a short locale string. */
function formatCapturedAt(iso: string | undefined): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return null;
  try {
    return date.toLocaleString();
  } catch {
    return iso;
  }
}

/** Type guard for the widget payload — defensive narrowing at the boundary. */
function isSearchCriteriaResultData(
  value: unknown
): value is SearchCriteriaResultWidgetData {
  if (value === null || typeof value !== 'object') return false;
  const obj = value as Record<string, unknown>;
  return typeof obj.query === 'string' && typeof obj.domain === 'string';
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SearchCriteriaResultWidget — workspace tab for a Semantic Search criteria
 * snapshot promoted from the Context pane.
 *
 * Renders the captured search criteria as a read-only summary card. Future
 * work (out of scope for task 043) will replace this shim with an actual
 * results-grid that calls the AI Search BFF endpoint via `authenticatedFetch`.
 *
 * No BFF calls in v1 — the criteria are pure user input.
 */
const SearchCriteriaResultWidget: React.FC<
  WorkspaceWidgetProps<SearchCriteriaResultWidgetData>
> = ({ data, widgetType, isLoading, error, className }) => {
  const styles = useStyles();

  // Defensive: subscribers may pass `unknown` payloads through; narrow here.
  const isValid = isSearchCriteriaResultData(data);

  const query = isValid ? data.query : '';
  const domain = isValid ? data.domain : '';
  const documentType = isValid ? data.documentType : undefined;
  const fileType = isValid ? data.fileType : undefined;
  const matterType = isValid ? data.matterType : undefined;
  const dateRange = isValid ? data.dateRange : undefined;
  const capturedAtLabel = formatCapturedAt(isValid ? data.capturedAt : undefined);

  // Per-domain filter visibility — mirrors SemanticSearchCriteriaTool.
  const showDocumentTypeFilter = domain === 'documents';
  const showFileTypeFilter = domain === 'documents';
  const showMatterTypeFilter = domain === 'documents' || domain === 'matters';

  const renderFilterValue = (value: string | undefined): React.ReactNode => {
    if (!value || value === 'all' || value === '') {
      return <Text className={mergeClasses(styles.sectionValue, styles.emptyValue)}>All</Text>;
    }
    return <Text className={styles.sectionValue}>{titleCase(value)}</Text>;
  };

  return (
    <div
      className={mergeClasses(styles.root, className)}
      data-widget-type={widgetType}
      data-testid="search-criteria-result-widget"
    >
      <Card className={styles.card}>
        <CardHeader
          image={<SearchRegular className={styles.headerIcon} />}
          header={
            <Text className={styles.headerTitle}>
              Search criteria — {titleCase(domain || 'Unknown')}
            </Text>
          }
          description={
            capturedAtLabel ? (
              <Text className={styles.headerSubtitle}>
                Captured at {capturedAtLabel}
              </Text>
            ) : undefined
          }
        />

        {/* Loading state */}
        {isLoading && (
          <div className={styles.emptyState}>
            <Text>Loading criteria…</Text>
          </div>
        )}

        {/* Error state */}
        {error && (
          <div className={styles.emptyState}>
            <Text style={{ color: tokens.colorPaletteRedForeground1 }}>{error}</Text>
          </div>
        )}

        {/* Invalid / empty payload */}
        {!isLoading && !error && !isValid && (
          <div className={styles.emptyState}>
            <Text>No criteria captured.</Text>
          </div>
        )}

        {/* Body */}
        {!isLoading && !error && isValid && (
          <div className={styles.body} data-testid="search-criteria-result-body">
            <div className={styles.section}>
              <Text className={styles.sectionLabel}>AI Query</Text>
              {query.length > 0 ? (
                <Text className={styles.sectionValue}>{query}</Text>
              ) : (
                <Text className={mergeClasses(styles.sectionValue, styles.emptyValue)}>
                  No query text
                </Text>
              )}
            </div>

            <div className={styles.section}>
              <Text className={styles.sectionLabel}>Domain</Text>
              <div className={styles.badgeRow}>
                <Badge appearance="tint" size="medium">
                  {titleCase(domain) || 'Unknown'}
                </Badge>
              </div>
            </div>

            {showDocumentTypeFilter && (
              <div className={styles.section}>
                <Text className={styles.sectionLabel}>Document Type</Text>
                {renderFilterValue(documentType)}
              </div>
            )}

            {showFileTypeFilter && (
              <div className={styles.section}>
                <Text className={styles.sectionLabel}>File Type</Text>
                {renderFilterValue(fileType)}
              </div>
            )}

            {showMatterTypeFilter && (
              <div className={styles.section}>
                <Text className={styles.sectionLabel}>Matter Type</Text>
                {renderFilterValue(matterType)}
              </div>
            )}

            <div className={styles.section}>
              <Text className={styles.sectionLabel}>Date Range</Text>
              <Text className={styles.sectionValue}>{formatDateRange(dateRange)}</Text>
            </div>

            <div className={styles.noticeBanner} data-testid="search-criteria-result-notice">
              Demo widget. To run the search, use the Semantic Search button — the
              modal results page is the production search surface. This tab is a
              snapshot of the criteria promoted from the Context pane.
            </div>
          </div>
        )}
      </Card>
    </div>
  );
};

export default SearchCriteriaResultWidget;
