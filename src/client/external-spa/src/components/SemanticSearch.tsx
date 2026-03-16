/**
 * SemanticSearch component — natural language document search for the Secure Project Workspace.
 *
 * Allows external users to search project documents using natural language queries,
 * powered by Azure AI Search through the BFF API (POST /api/ai/search).
 *
 * Security: The project_ids filter is applied server-side via the entity scope.
 * When a projectId prop is provided, search is scoped to that single project.
 * The BFF API enforces that the caller's Bearer token only allows access to
 * documents from projects the authenticated user is permitted to see.
 *
 * Access levels:
 *   - View Only (100000000): can search and view results — no download links
 *   - Collaborate (100000001): can search and view results with document link navigation
 *   - Full Access (100000002): same as Collaborate
 *
 * Design patterns from src/client/code-pages/SemanticSearch/ are referenced
 * for the API request shape (DocumentSearchRequest) and result display.
 *
 * ADR-021: All styles use Fluent v9 design tokens exclusively. No hard-coded colors.
 * ADR-022: React 18 functional component.
 * ADR-013: All AI calls go through the BFF API — no direct Azure AI Search calls.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  Input,
  Spinner,
  Text,
  Badge,
  MessageBar,
  MessageBarBody,
  Divider,
  Tooltip,
} from "@fluentui/react-components";
import {
  SearchRegular,
  DocumentRegular,
  DismissRegular,
  SparkleRegular,
  FolderOpenRegular,
  OpenRegular,
} from "@fluentui/react-icons";
import { bffApiCall } from "../auth/bff-client";
import { AccessLevel } from "../types";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    width: "100%",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  searchRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    width: "100%",
  },
  searchInput: {
    flex: "1",
  },
  metaRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
  },
  metaText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  resultsContainer: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  resultCard: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2,
      borderColor: tokens.colorNeutralStroke1,
    },
  },
  resultHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalS,
  },
  resultTitleRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flex: "1",
    minWidth: 0,
  },
  resultIcon: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
  },
  resultName: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  resultNameClickable: {
    color: tokens.colorBrandForeground1,
    cursor: "pointer",
    ":hover": {
      textDecoration: "underline",
    },
  },
  resultMeta: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
    flexShrink: 0,
  },
  resultScore: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    fontFamily: tokens.fontFamilyMonospace,
    whiteSpace: "nowrap",
  },
  highlightList: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
    paddingLeft: tokens.spacingHorizontalS,
    borderLeftWidth: "2px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorBrandStroke1,
  },
  highlightText: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase400,
    // Allow line wrapping for excerpt readability
    whiteSpace: "pre-wrap",
    wordBreak: "break-word",
  },
  projectName: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: "4px",
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  projectIcon: {
    color: tokens.colorNeutralForeground4,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "200px",
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalXL,
  },
  emptyStateIcon: {
    fontSize: "40px",
    color: tokens.colorNeutralForeground4,
  },
  emptyStateText: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  emptyStateHint: {
    color: tokens.colorNeutralForeground4,
    textAlign: "center",
    fontSize: tokens.fontSizeBase200,
  },
  loadingContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "160px",
    gap: tokens.spacingVerticalM,
  },
  idleState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "160px",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground4,
  },
  idleIcon: {
    fontSize: "36px",
  },
  actionButton: {
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// BFF API types (subset of DocumentSearchRequest / DocumentSearchResponse)
// ---------------------------------------------------------------------------

/** Minimal search request body sent to POST /api/ai/search */
interface SearchRequest {
  query: string;
  /** "entity" scope with entityType="project" and entityId scopes to a single project */
  scope: string;
  entityType?: string;
  entityId?: string;
  options: {
    limit: number;
    offset: number;
    includeHighlights: boolean;
    hybridMode: "rrf";
  };
}

/** Individual document result from POST /api/ai/search */
interface SearchResult {
  documentId?: string;
  name?: string;
  documentType?: string;
  fileType?: string;
  combinedScore: number;
  highlights?: string[];
  parentEntityType?: string;
  parentEntityId?: string;
  parentEntityName?: string;
  fileUrl?: string;
  recordUrl?: string;
  createdAt?: string;
}

/** Response envelope from POST /api/ai/search */
interface SearchResponse {
  results: SearchResult[];
  metadata: {
    totalResults: number;
    returnedResults: number;
    searchDurationMs: number;
    embeddingDurationMs: number;
  };
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Format a combined score (0–1) as a readable relevance percentage.
 */
function formatScore(score: number): string {
  return `${Math.round(score * 100)}%`;
}

/**
 * Trim and truncate a highlight excerpt for display.
 * Replaces multiple whitespace runs with a single space and limits to maxLen chars.
 */
function formatHighlight(text: string, maxLen = 280): string {
  const cleaned = text.replace(/\s+/g, " ").trim();
  if (cleaned.length <= maxLen) return cleaned;
  return `${cleaned.substring(0, maxLen)}…`;
}

// ---------------------------------------------------------------------------
// SearchResultCard sub-component
// ---------------------------------------------------------------------------

interface SearchResultCardProps {
  result: SearchResult;
  /** Whether the user's access level allows navigating to the project document page */
  canNavigate: boolean;
  /** The projectId the search is scoped to (for building navigation links) */
  projectId: string;
}

const SearchResultCard: React.FC<SearchResultCardProps> = ({
  result,
  canNavigate,
  projectId,
}) => {
  const styles = useStyles();

  const documentName = result.name ?? "Untitled Document";
  const highlights = (result.highlights ?? []).slice(0, 3);
  const projectName = result.parentEntityName;
  const resultProjectId = result.parentEntityId ?? projectId;

  const handleNavigateToDocument = () => {
    if (!canNavigate || !result.documentId) return;
    // Navigate to the project page where the document lives.
    // The hash router path is #/project/:id — the user can then find the document
    // in the Documents tab. We do not have a per-document deep-link in this SPA.
    const projectHash = `#/project/${resultProjectId}`;
    window.location.hash = projectHash.replace("#", "");
  };

  return (
    <div className={styles.resultCard}>
      {/* Header: document name + metadata */}
      <div className={styles.resultHeader}>
        <div className={styles.resultTitleRow}>
          <DocumentRegular className={styles.resultIcon} fontSize={18} />
          {canNavigate && result.documentId ? (
            <Tooltip
              content={`Navigate to project containing "${documentName}"`}
              relationship="label"
            >
              <Text
                size={300}
                className={`${styles.resultName} ${styles.resultNameClickable}`}
                onClick={handleNavigateToDocument}
                truncate
                wrap={false}
              >
                {documentName}
              </Text>
            </Tooltip>
          ) : (
            <Text
              size={300}
              className={styles.resultName}
              truncate
              wrap={false}
            >
              {documentName}
            </Text>
          )}
        </div>

        <div className={styles.resultMeta}>
          {result.documentType && (
            <Badge appearance="tint" size="small">
              {result.documentType}
            </Badge>
          )}
          {result.fileType && (
            <Badge appearance="outline" size="small" color="informative">
              {result.fileType.toUpperCase()}
            </Badge>
          )}
          <Text className={styles.resultScore}>
            {formatScore(result.combinedScore)}
          </Text>
          {canNavigate && result.documentId && (
            <Tooltip
              content={`Go to project containing this document`}
              relationship="label"
            >
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                onClick={handleNavigateToDocument}
                aria-label={`Navigate to project for ${documentName}`}
              />
            </Tooltip>
          )}
        </div>
      </div>

      {/* Project attribution (shown when visible in context) */}
      {projectName && (
        <div className={styles.projectName}>
          <FolderOpenRegular className={styles.projectIcon} fontSize={12} />
          <Text size={100}>{projectName}</Text>
        </div>
      )}

      {/* Highlights / excerpts */}
      {highlights.length > 0 && (
        <div className={styles.highlightList}>
          {highlights.map((highlight, index) => (
            <Text
              key={index}
              className={styles.highlightText}
              size={200}
              as="p"
            >
              …{formatHighlight(highlight)}…
            </Text>
          ))}
        </div>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SemanticSearchProps {
  /**
   * Dataverse GUID of the owning sprk_project record.
   * When provided, search is scoped to documents from this project only.
   * The BFF API uses this as the `entityId` for an entity-scoped search.
   */
  projectId: string;
  /** The authenticated user's access level for this project */
  accessLevel: AccessLevel;
}

// ---------------------------------------------------------------------------
// SemanticSearch — main component
// ---------------------------------------------------------------------------

/**
 * SemanticSearch — natural language document search for the Secure Project Workspace.
 *
 * Features:
 * - Free-text search input with keyboard support (Enter to search, Escape to clear)
 * - Calls POST /api/ai/search via BFF API with project entity scope
 * - Results display: document name, type badge, relevance score, excerpt highlights
 * - View Only users see results but cannot navigate to the project document page
 * - Collaborate / Full Access users get a clickable link to the project page
 * - Loading state shown during search execution
 * - Empty state when no results match the query
 * - Error state on API failure with retry option
 * - Idle state before the first search is executed
 *
 * Security: The BFF API enforces that only documents from projects accessible
 * to the authenticated user are returned. The `entityId` scope further
 * restricts results to the current project.
 *
 * ADR-021: Fluent UI v9 only. makeStyles + tokens. No hard-coded colors.
 * ADR-013: AI calls go through BFF API — POST /api/ai/search.
 */
export const SemanticSearch: React.FC<SemanticSearchProps> = ({
  projectId,
  accessLevel,
}) => {
  const styles = useStyles();

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  const [query, setQuery] = React.useState<string>("");
  const [submittedQuery, setSubmittedQuery] = React.useState<string>("");
  const [results, setResults] = React.useState<SearchResult[]>([]);
  const [totalResults, setTotalResults] = React.useState<number>(0);
  const [searchDurationMs, setSearchDurationMs] = React.useState<number | null>(null);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<string | null>(null);
  const [hasSearched, setHasSearched] = React.useState<boolean>(false);

  // Collaborate and Full Access users can navigate to the project page for a document
  const canNavigate =
    accessLevel === AccessLevel.Collaborate ||
    accessLevel === AccessLevel.FullAccess;

  // ---------------------------------------------------------------------------
  // Search handler
  // ---------------------------------------------------------------------------

  const executeSearch = React.useCallback(
    async (searchQuery: string) => {
      const trimmed = searchQuery.trim();
      if (!trimmed) return;

      setLoading(true);
      setError(null);
      setHasSearched(true);
      setSubmittedQuery(trimmed);

      const requestBody: SearchRequest = {
        query: trimmed,
        // Scope search to the current project via entity scope
        scope: "entity",
        entityType: "project",
        entityId: projectId,
        options: {
          limit: 20,
          offset: 0,
          includeHighlights: true,
          hybridMode: "rrf",
        },
      };

      try {
        const response = await bffApiCall<SearchResponse>("/api/ai/search", {
          method: "POST",
          body: JSON.stringify(requestBody),
        });

        setResults(response.results ?? []);
        setTotalResults(response.metadata?.totalResults ?? response.results?.length ?? 0);
        setSearchDurationMs(response.metadata?.searchDurationMs ?? null);
      } catch (err) {
        console.error("[SemanticSearch] Search failed:", err);
        setError(
          "Search failed. Please check your connection and try again."
        );
        setResults([]);
        setTotalResults(0);
      } finally {
        setLoading(false);
      }
    },
    [projectId]
  );

  const handleSearch = () => {
    void executeSearch(query);
  };

  const handleClear = () => {
    setQuery("");
    setSubmittedQuery("");
    setResults([]);
    setTotalResults(0);
    setSearchDurationMs(null);
    setError(null);
    setHasSearched(false);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      void executeSearch(query);
    } else if (e.key === "Escape") {
      handleClear();
    }
  };

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className={styles.root}>
      {/* Search input row */}
      <div className={styles.searchRow}>
        <Input
          className={styles.searchInput}
          placeholder="Search documents using natural language…"
          value={query}
          onChange={(_ev, data) => setQuery(data.value)}
          onKeyDown={handleKeyDown}
          contentBefore={<SearchRegular />}
          contentAfter={
            query.length > 0 ? (
              <Button
                appearance="subtle"
                size="small"
                icon={<DismissRegular />}
                onClick={handleClear}
                aria-label="Clear search"
              />
            ) : undefined
          }
          disabled={loading}
          aria-label="Document search query"
          size="large"
        />
        <Button
          className={styles.actionButton}
          appearance="primary"
          icon={loading ? <Spinner size="tiny" /> : <SearchRegular />}
          onClick={handleSearch}
          disabled={!query.trim() || loading}
          aria-label="Run search"
          size="large"
        >
          {loading ? "Searching…" : "Search"}
        </Button>
      </div>

      {/* Result metadata row (shown after a search) */}
      {hasSearched && !loading && !error && (
        <div className={styles.metaRow}>
          <SparkleRegular fontSize={14} style={{ color: tokens.colorBrandForeground2 }} />
          <Text className={styles.metaText}>
            {results.length > 0
              ? `${totalResults} result${totalResults !== 1 ? "s" : ""} for "${submittedQuery}"`
              : `No results for "${submittedQuery}"`}
            {searchDurationMs !== null && ` · ${searchDurationMs}ms`}
          </Text>
        </div>
      )}

      {/* Error state */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Loading state */}
      {loading && (
        <div className={styles.loadingContainer}>
          <Spinner size="medium" label="Searching documents…" />
        </div>
      )}

      {/* Idle state — before any search has been run */}
      {!hasSearched && !loading && (
        <div className={styles.idleState}>
          <SearchRegular className={styles.idleIcon} />
          <Text size={300} className={styles.emptyStateText}>
            Enter a natural language query to search project documents.
          </Text>
          <Text size={200} className={styles.emptyStateHint}>
            Try: "contract amendments from last quarter" or "liability clauses"
          </Text>
        </div>
      )}

      {/* Empty state — search returned no results */}
      {hasSearched && !loading && !error && results.length === 0 && (
        <div className={styles.emptyState}>
          <DocumentRegular className={styles.emptyStateIcon} />
          <Text size={400} weight="semibold">
            No Results Found
          </Text>
          <Text size={300} className={styles.emptyStateText}>
            No documents matched "{submittedQuery}". Try different search terms or a broader query.
          </Text>
        </div>
      )}

      {/* Results list */}
      {!loading && !error && results.length > 0 && (
        <div className={styles.resultsContainer}>
          {results.map((result, index) => (
            <React.Fragment key={result.documentId ?? `result-${index}`}>
              {index > 0 && <Divider appearance="subtle" />}
              <SearchResultCard
                result={result}
                canNavigate={canNavigate}
                projectId={projectId}
              />
            </React.Fragment>
          ))}
        </div>
      )}
    </div>
  );
};

export default SemanticSearch;
