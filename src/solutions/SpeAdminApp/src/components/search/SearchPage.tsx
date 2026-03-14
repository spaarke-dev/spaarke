import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Input,
  Button,
  Spinner,
  Tab,
  TabList,
  Badge,
  Tooltip,
  MessageBar,
  MessageBarBody,
  Divider,
} from "@fluentui/react-components";
import {
  Search20Regular,
  Dismiss20Regular,
  ArrowClockwise20Regular,
} from "@fluentui/react-icons";
import type {
  ContainerSearchResult,
  DriveItemSearchResult,
} from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";
import { useBuContext } from "../../contexts/BuContext";
import { ContainerResultsGrid } from "./ContainerResultsGrid";
import { ItemResultsGrid } from "./ItemResultsGrid";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Active tab identifier */
type SearchTab = "containers" | "items";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Full-height page container (flex column).
   * Fits inside AppShell's contentInner scroll area.
   */
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Page header: title */
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    flexShrink: 0,
  },

  headerTitle: {
    flex: "1 1 auto",
    color: tokens.colorNeutralForeground1,
  },

  /** Search bar row: input + submit button */
  searchBar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    flexShrink: 0,
  },

  searchInput: {
    flex: "1 1 auto",
    maxWidth: "600px",
  },

  /** Tab list area — sits below the search bar */
  tabArea: {
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    flexShrink: 0,
  },

  /** Results area — fills remaining vertical space, scrollable */
  resultsArea: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalM,
    gap: tokens.spacingVerticalS,
  },

  /** Loading / empty / initial state container */
  stateContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
    flex: "1 1 auto",
  },

  /** Tab label with badge — flex row */
  tabLabel: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },

});



// ─────────────────────────────────────────────────────────────────────────────
// SearchPage Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SearchPage — unified full-text search interface for the SPE Admin App.
 *
 * Provides a search input and submit button. On search, calls both
 * POST /api/spe/search/containers and POST /api/spe/search/items in parallel.
 * Results are shown in a tabbed layout:
 *   - Container Results tab — matching containers with storage stats
 *   - Item Results tab — matching drive items with hit highlights
 *
 * Both tab labels show result counts once a search completes.
 *
 * States:
 *   - Initial: prompt to enter a query
 *   - Loading: spinner during search
 *   - Empty: no results message per tab
 *   - Results: DataGrid with sortable columns
 *   - Error: MessageBar with error text and retry button
 *
 * ADR compliance:
 *   - ADR-006: React Code Page (React 18, bundled) — not PCF
 *   - ADR-012: speApiClient for all API calls; no direct fetch()
 *   - ADR-021: Fluent v9 makeStyles + design tokens; dark mode via tokens; no hard-coded colors
 */
export const SearchPage: React.FC = () => {
  const styles = useStyles();
  const { selectedConfig, selectedBu } = useBuContext();

  // ── Search input state ──────────────────────────────────────────────────

  /** Current value of the search input field */
  const [query, setQuery] = React.useState("");

  /** The query that was last submitted (used to distinguish initial vs. results state) */
  const [submittedQuery, setSubmittedQuery] = React.useState("");

  // ── Active tab ──────────────────────────────────────────────────────────

  const [activeTab, setActiveTab] = React.useState<SearchTab>("containers");

  // ── Loading state ───────────────────────────────────────────────────────

  const [isLoading, setIsLoading] = React.useState(false);

  // ── Results state ───────────────────────────────────────────────────────

  const [containerResults, setContainerResults] = React.useState<
    ContainerSearchResult[]
  >([]);
  const [itemResults, setItemResults] = React.useState<DriveItemSearchResult[]>(
    []
  );

  // ── Error state ─────────────────────────────────────────────────────────

  const [error, setError] = React.useState<string | null>(null);

  // ── Search handler ───────────────────────────────────────────────────────

  /**
   * Execute search — calls both container and item search endpoints in parallel.
   * Results are stored in separate state slices; tab counts update automatically.
   */
  const handleSearch = React.useCallback(async () => {
    const trimmed = query.trim();
    if (!trimmed || !selectedConfig) return;

    setIsLoading(true);
    setError(null);
    setSubmittedQuery(trimmed);

    try {
      const [containers, items] = await Promise.all([
        speApiClient.search.containers(selectedConfig.id, {
          query: trimmed,
          top: 50,
        }),
        speApiClient.search.items(selectedConfig.id, {
          query: trimmed,
          top: 50,
        }),
      ]);
      setContainerResults(containers);
      setItemResults(items);
    } catch (err) {
      const msg =
        err instanceof Error ? err.message : "Search failed. Please try again.";
      setError(msg);
      setContainerResults([]);
      setItemResults([]);
    } finally {
      setIsLoading(false);
    }
  }, [query, selectedConfig]);

  /**
   * Handle Enter key in the search input to submit.
   */
  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") {
        void handleSearch();
      }
    },
    [handleSearch]
  );

  /**
   * Clear the search input and reset results.
   */
  const handleClear = React.useCallback(() => {
    setQuery("");
    setSubmittedQuery("");
    setContainerResults([]);
    setItemResults([]);
    setError(null);
  }, []);

  // ── Tab label with badge ─────────────────────────────────────────────────

  /**
   * Render a tab label with an optional result-count badge.
   * Badge is only shown after a search has been submitted.
   */
  const renderTabLabel = (
    label: string,
    count: number,
    hasSearched: boolean
  ) => (
    <span className={styles.tabLabel}>
      {label}
      {hasSearched && (
        <Badge
          appearance="filled"
          color={count > 0 ? "brand" : "subtle"}
          size="small"
          shape="rounded"
        >
          {count}
        </Badge>
      )}
    </span>
  );

  // ── Render helpers ───────────────────────────────────────────────────────

  const hasSearched = submittedQuery.length > 0;

  const renderNoConfig = () => (
    <div className={styles.stateContainer}>
      <Search20Regular style={{ fontSize: "32px", color: tokens.colorNeutralForeground3 }} />
      <Text size={400} weight="semibold">
        No Config Selected
      </Text>
      <Text
        size={300}
        style={{
          color: tokens.colorNeutralForeground2,
          textAlign: "center",
          maxWidth: "360px",
        }}
      >
        Select a Business Unit and Container Type Config using the BU picker to
        search across your SharePoint Embedded environment.
        {selectedBu && !selectedConfig
          ? ` Business Unit "${selectedBu.name}" is selected — please also select a Config.`
          : ""}
      </Text>
    </div>
  );

  const renderInitialState = () => (
    <div className={styles.stateContainer}>
      <Search20Regular
        style={{ fontSize: "40px", color: tokens.colorNeutralForeground3 }}
      />
      <Text size={400} weight="semibold">
        Search SharePoint Embedded
      </Text>
      <Text
        size={300}
        style={{
          color: tokens.colorNeutralForeground2,
          textAlign: "center",
          maxWidth: "400px",
        }}
      >
        Enter a search query above to find containers and files across your
        SharePoint Embedded environment. Results appear in the Containers and
        Items tabs.
      </Text>
    </div>
  );

  const renderLoading = () => (
    <div className={styles.stateContainer}>
      <Spinner size="medium" label={`Searching for "${submittedQuery}"…`} />
    </div>
  );

  const renderError = () => (
    <MessageBar intent="error">
      <MessageBarBody>
        {error}
        <Button
          appearance="transparent"
          size="small"
          onClick={() => void handleSearch()}
          style={{ marginLeft: tokens.spacingHorizontalS }}
          icon={<ArrowClockwise20Regular />}
        >
          Retry
        </Button>
      </MessageBarBody>
    </MessageBar>
  );

  const renderEmpty = (tabLabel: string) => (
    <div className={styles.stateContainer}>
      <Text size={400} weight="semibold">
        No {tabLabel} Found
      </Text>
      <Text
        size={300}
        style={{ color: tokens.colorNeutralForeground2, textAlign: "center" }}
      >
        No {tabLabel.toLowerCase()} matched &quot;{submittedQuery}&quot;. Try a
        different search term.
      </Text>
    </div>
  );

  // ── Container results grid ───────────────────────────────────────────────

  const renderContainerGrid = () => {
    if (containerResults.length === 0) return renderEmpty("Containers");
    return (
      <ContainerResultsGrid
        results={containerResults}
        configId={selectedConfig!.id}
        onDeleted={() => void handleSearch()}
        onLocked={() => void handleSearch()}
      />
    );
  };

  // ── Item results grid ────────────────────────────────────────────────────
  // Delegated to ItemResultsGrid (task 068) which provides multi-select,
  // toolbar actions (Delete, Download, Manage Permissions, Export CSV),
  // right-click context menu, delete confirmation, and skip-based pagination.

  const renderItemGrid = () => (
    <ItemResultsGrid
      query={submittedQuery}
      initialResults={itemResults}
      isActive={activeTab === "items" && hasSearched && !isLoading && !error}
    />
  );

  // ── Main render ──────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={500} weight="semibold">
          Search
        </Text>
      </div>

      {/* ── Config context breadcrumb ── */}
      {selectedConfig && (
        <div
          style={{
            paddingLeft: tokens.spacingVerticalL,
            paddingBottom: tokens.spacingVerticalS,
          }}
        >
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            {selectedBu?.name && `${selectedBu.name} / `}
            {selectedConfig.name}
          </Text>
        </div>
      )}

      <Divider style={{ flexShrink: 0 }} />

      {/* ── Search Input Bar ── */}
      <div className={styles.searchBar}>
        <Input
          className={styles.searchInput}
          placeholder="Search containers and files…"
          value={query}
          onChange={(_e, data) => setQuery(data.value)}
          onKeyDown={handleKeyDown}
          disabled={!selectedConfig || isLoading}
          contentBefore={
            <Search20Regular
              style={{ color: tokens.colorNeutralForeground3 }}
            />
          }
          contentAfter={
            query ? (
              <Tooltip content="Clear search" relationship="label">
                <Button
                  appearance="transparent"
                  size="small"
                  icon={<Dismiss20Regular />}
                  onClick={handleClear}
                  aria-label="Clear search"
                />
              </Tooltip>
            ) : undefined
          }
          aria-label="Search query"
        />
        <Button
          appearance="primary"
          icon={<Search20Regular />}
          onClick={() => void handleSearch()}
          disabled={!selectedConfig || !query.trim() || isLoading}
          aria-label="Search"
        >
          Search
        </Button>
      </div>

      {/* ── No config state ── */}
      {!selectedConfig && renderNoConfig()}

      {/* ── Content when config is selected ── */}
      {selectedConfig && (
        <>
          {/* Tab list — always visible when config is selected */}
          <div className={styles.tabArea}>
            <TabList
              selectedValue={activeTab}
              onTabSelect={(_e, data) =>
                setActiveTab(data.value as SearchTab)
              }
            >
              <Tab value="containers" aria-label="Container results">
                {renderTabLabel(
                  "Container Results",
                  containerResults.length,
                  hasSearched
                )}
              </Tab>
              <Tab value="items" aria-label="Item results">
                {renderTabLabel(
                  "Item Results",
                  itemResults.length,
                  hasSearched
                )}
              </Tab>
            </TabList>
          </div>

          {/* Results area */}
          <div className={styles.resultsArea}>
            {/* Error state */}
            {error && renderError()}

            {/* Loading state */}
            {isLoading && renderLoading()}

            {/* Initial state (no search submitted yet) */}
            {!isLoading && !error && !hasSearched && renderInitialState()}

            {/* Results (containers tab) */}
            {!isLoading && !error && hasSearched && activeTab === "containers" &&
              renderContainerGrid()}

            {/* Results (items tab) */}
            {!isLoading && !error && hasSearched && activeTab === "items" &&
              renderItemGrid()}
          </div>
        </>
      )}
    </div>
  );
};
