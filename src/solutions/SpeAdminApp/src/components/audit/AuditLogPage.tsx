import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
  Select,
  Field,
  Input,
  Badge,
  Tooltip,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  MessageBar,
  MessageBarBody,
  Divider,
} from "@fluentui/react-components";
import {
  ArrowLeft20Regular,
  ArrowRight20Regular,
  ArrowClockwise20Regular,
  Filter20Regular,
} from "@fluentui/react-icons";
import type { AuditCategory, AuditLogEntry } from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";
import { useBuContext } from "../../contexts/BuContext";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Number of rows per page */
const PAGE_SIZE = 25;

/** All audit category options for the filter dropdown */
const AUDIT_CATEGORIES: Array<{ value: AuditCategory | ""; label: string }> = [
  { value: "", label: "All Categories" },
  { value: "ContainerType", label: "Container Type" },
  { value: "Container", label: "Container" },
  { value: "Permission", label: "Permission" },
  { value: "File", label: "File" },
  { value: "Search", label: "Search" },
  { value: "Security", label: "Security" },
];

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

  /** Page header row: title + refresh button */
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

  /** Filter bar row: date range, category, BU, config selectors */
  filterBar: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "flex-end",
    gap: tokens.spacingHorizontalM,
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    flexShrink: 0,
  },

  filterField: {
    minWidth: "160px",
    maxWidth: "220px",
  },

  filterApplyButton: {
    alignSelf: "flex-end",
    marginBottom: "2px",
  },

  /** Grid and pagination area — fills remaining height */
  gridArea: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    paddingLeft: tokens.spacingVerticalL,
    paddingRight: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    gap: tokens.spacingVerticalS,
  },

  /** DataGrid wrapper — scrollable */
  gridWrapper: {
    flex: "1 1 auto",
    overflow: "auto",
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftWidth: "1px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Pagination controls row */
  pagination: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    flexShrink: 0,
  },

  paginationText: {
    flex: "1 1 auto",
    color: tokens.colorNeutralForeground2,
  },

  /** Loading / error / empty state overlay */
  stateContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
  },

  /** Status badge cell layout */
  statusCell: {
    display: "flex",
    alignItems: "center",
  },

  /** Truncate long text in cells */
  cellText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "200px",
  },

  /** Monospace font for operation names — font/color applied via inline style to avoid token undefined types */
  operationText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "200px",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format an ISO timestamp to a compact local date/time string.
 * e.g. "2026-03-14 09:31 AM"
 */
function formatTimestamp(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

/**
 * Map HTTP response status code to a Fluent Badge color.
 * 2xx → success, 4xx → warning, 5xx → danger, others → neutral
 */
function statusBadgeColor(
  code: number
): "success" | "warning" | "danger" | "informative" {
  if (code >= 200 && code < 300) return "success";
  if (code >= 400 && code < 500) return "warning";
  if (code >= 500) return "danger";
  return "informative";
}

/**
 * Get a default "from" date: 30 days ago, formatted as YYYY-MM-DD for the
 * HTML date input.
 */
function defaultFromDate(): string {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return d.toISOString().slice(0, 10);
}

/**
 * Get today's date as YYYY-MM-DD for the HTML date input.
 */
function todayDate(): string {
  return new Date().toISOString().slice(0, 10);
}

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

type AuditLogColumn = TableColumnDefinition<AuditLogEntry>;

/**
 * Build the DataGrid column definitions for the audit log table.
 * Columns: operation, target, status, user, timestamp.
 */
function buildColumns(styles: ReturnType<typeof useStyles>): AuditLogColumn[] {
  return [
    createTableColumn<AuditLogEntry>({
      columnId: "operation",
      compare: (a, b) => a.operation.localeCompare(b.operation),
      renderHeaderCell: () => "Operation",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip content={item.operation} relationship="description">
            <Text
              className={styles.operationText}
              style={{
                fontFamily: tokens.fontFamilyMonospace,
                fontSize: tokens.fontSizeBase200,
                color: tokens.colorNeutralForeground1,
              }}
            >
              {item.operation}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
    createTableColumn<AuditLogEntry>({
      columnId: "target",
      compare: (a, b) =>
        a.targetResourceName.localeCompare(b.targetResourceName),
      renderHeaderCell: () => "Target",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip
            content={`${item.targetResourceName} (${item.targetResourceId})`}
            relationship="description"
          >
            <Text className={styles.cellText}>{item.targetResourceName || item.targetResourceId}</Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
    createTableColumn<AuditLogEntry>({
      columnId: "category",
      compare: (a, b) => a.category.localeCompare(b.category),
      renderHeaderCell: () => "Category",
      renderCell: (item) => (
        <TableCellLayout>
          <Text size={200}>{item.category}</Text>
        </TableCellLayout>
      ),
    }),
    createTableColumn<AuditLogEntry>({
      columnId: "status",
      compare: (a, b) => a.responseStatus - b.responseStatus,
      renderHeaderCell: () => "Status",
      renderCell: (item) => (
        <TableCellLayout>
          <div className={styles.statusCell}>
            <Tooltip
              content={item.responseSummary || String(item.responseStatus)}
              relationship="description"
            >
              <Badge
                color={statusBadgeColor(item.responseStatus)}
                appearance="filled"
                size="small"
              >
                {item.responseStatus}
              </Badge>
            </Tooltip>
          </div>
        </TableCellLayout>
      ),
    }),
    createTableColumn<AuditLogEntry>({
      columnId: "user",
      compare: (a, b) => a.performedBy.localeCompare(b.performedBy),
      renderHeaderCell: () => "Performed By",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip content={item.performedBy} relationship="description">
            <Text className={styles.cellText}>{item.performedBy}</Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
    createTableColumn<AuditLogEntry>({
      columnId: "timestamp",
      compare: (a, b) =>
        new Date(a.performedOn).getTime() - new Date(b.performedOn).getTime(),
      renderHeaderCell: () => "Timestamp",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip content={item.performedOn} relationship="description">
            <Text size={200} style={{ whiteSpace: "nowrap" }}>
              {formatTimestamp(item.performedOn)}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// Filter State
// ─────────────────────────────────────────────────────────────────────────────

interface FilterState {
  /** ISO date string (YYYY-MM-DD) for the from date filter */
  fromDate: string;
  /** ISO date string (YYYY-MM-DD) for the to date filter */
  toDate: string;
  /** Audit category filter — empty string means all categories */
  category: AuditCategory | "";
}

// ─────────────────────────────────────────────────────────────────────────────
// AuditLogPage Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * AuditLogPage — displays a filterable, paginated grid of SPE audit log entries.
 *
 * Filters: date range (from/to), operation category, BU-scoped config.
 * Grid columns: operation, target, category, status (HTTP code badge), user, timestamp.
 * Pagination: client-side page navigation over server-returned batch.
 *
 * The page requires a selected config (via BuContext) to scope audit queries.
 * Without a selected config, a prompt guides the user to the Settings page.
 *
 * ADR compliance:
 *   - ADR-021: All UI via Fluent v9 makeStyles + design tokens; dark mode via tokens
 *   - ADR-012: speApiClient.audit.query() for API calls; no direct fetch()
 */
export const AuditLogPage: React.FC = () => {
  const styles = useStyles();
  const { selectedConfig, selectedBu } = useBuContext();

  // ── Filter state ────────────────────────────────────────────────────────

  const [filters, setFilters] = React.useState<FilterState>({
    fromDate: defaultFromDate(),
    toDate: todayDate(),
    category: "",
  });

  // Draft filter state (edited in the filter bar but not applied until user clicks Apply)
  const [draftFilters, setDraftFilters] = React.useState<FilterState>(filters);

  // ── Data state ──────────────────────────────────────────────────────────

  const [entries, setEntries] = React.useState<AuditLogEntry[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Pagination state ────────────────────────────────────────────────────

  /** Current page index (0-based) */
  const [pageIndex, setPageIndex] = React.useState(0);

  /** Total count — we fetch up to 500 entries and paginate client-side */
  const totalEntries = entries.length;
  const totalPages = Math.max(1, Math.ceil(totalEntries / PAGE_SIZE));

  /** Entries on the current page */
  const pageEntries = React.useMemo(() => {
    const start = pageIndex * PAGE_SIZE;
    return entries.slice(start, start + PAGE_SIZE);
  }, [entries, pageIndex]);

  // ── Column definitions ──────────────────────────────────────────────────

  const columns = React.useMemo(() => buildColumns(styles), [styles]);

  // ── Load data ───────────────────────────────────────────────────────────

  const loadData = React.useCallback(
    async (appliedFilters: FilterState) => {
      if (!selectedConfig) return;

      setIsLoading(true);
      setError(null);
      setPageIndex(0);

      try {
        const results = await speApiClient.audit.query({
          configId: selectedConfig.id,
          from: appliedFilters.fromDate
            ? new Date(appliedFilters.fromDate).toISOString()
            : undefined,
          to: appliedFilters.toDate
            ? new Date(appliedFilters.toDate + "T23:59:59").toISOString()
            : undefined,
          category: appliedFilters.category || undefined,
          top: 500, // Fetch up to 500; paginate client-side
          skip: 0,
        });
        setEntries(results);
      } catch (err) {
        const msg =
          err instanceof Error ? err.message : "Failed to load audit log.";
        setError(msg);
        setEntries([]);
      } finally {
        setIsLoading(false);
      }
    },
    [selectedConfig]
  );

  // Load on mount and when filters are applied
  React.useEffect(() => {
    if (selectedConfig) {
      loadData(filters);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedConfig]);

  // ── Event handlers ──────────────────────────────────────────────────────

  const handleApplyFilters = React.useCallback(() => {
    setFilters(draftFilters);
    loadData(draftFilters);
  }, [draftFilters, loadData]);

  const handleRefresh = React.useCallback(() => {
    loadData(filters);
  }, [filters, loadData]);

  const handlePrevPage = React.useCallback(() => {
    setPageIndex((p) => Math.max(0, p - 1));
  }, []);

  const handleNextPage = React.useCallback(() => {
    setPageIndex((p) => Math.min(totalPages - 1, p + 1));
  }, [totalPages]);

  // ── Render helpers ──────────────────────────────────────────────────────

  const renderNoConfig = () => (
    <div className={styles.stateContainer}>
      <Filter20Regular style={{ fontSize: "32px", color: tokens.colorNeutralForeground3 }} />
      <Text size={400} weight="semibold">
        No Config Selected
      </Text>
      <Text size={300} style={{ color: tokens.colorNeutralForeground2, textAlign: "center", maxWidth: "360px" }}>
        Select a Business Unit and Container Type Config using the BU picker to view the audit log.
        {selectedBu && !selectedConfig
          ? ` Business Unit "${selectedBu.name}" is selected — please also select a Config.`
          : ""}
      </Text>
    </div>
  );

  const renderLoading = () => (
    <div className={styles.stateContainer}>
      <Spinner size="medium" label="Loading audit log..." />
    </div>
  );

  const renderError = () => (
    <MessageBar intent="error">
      <MessageBarBody>
        {error}
        <Button
          appearance="transparent"
          size="small"
          onClick={handleRefresh}
          style={{ marginLeft: tokens.spacingHorizontalS }}
        >
          Retry
        </Button>
      </MessageBarBody>
    </MessageBar>
  );

  const renderEmpty = () => (
    <div className={styles.stateContainer}>
      <Text size={400} weight="semibold">
        No Entries Found
      </Text>
      <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
        No audit log entries match the current filters.
        Try expanding the date range or removing the category filter.
      </Text>
    </div>
  );

  // ── Pagination label ────────────────────────────────────────────────────

  const paginationLabel = React.useMemo(() => {
    if (totalEntries === 0) return "0 entries";
    const start = pageIndex * PAGE_SIZE + 1;
    const end = Math.min((pageIndex + 1) * PAGE_SIZE, totalEntries);
    return `${start}–${end} of ${totalEntries} entries`;
  }, [pageIndex, totalEntries]);

  // ── Main render ─────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={500} weight="semibold">
          Audit Log
        </Text>
        {selectedConfig && (
          <Tooltip content="Refresh audit log" relationship="label">
            <Button
              appearance="subtle"
              icon={<ArrowClockwise20Regular />}
              onClick={handleRefresh}
              disabled={isLoading}
              aria-label="Refresh audit log"
            />
          </Tooltip>
        )}
      </div>

      {/* ── Config context breadcrumb ── */}
      {selectedConfig && (
        <div style={{ paddingLeft: tokens.spacingVerticalL, paddingBottom: tokens.spacingVerticalS }}>
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            {selectedBu?.name && `${selectedBu.name} / `}
            {selectedConfig.name}
          </Text>
        </div>
      )}

      <Divider style={{ flexShrink: 0 }} />

      {/* ── Filter Bar (only shown when a config is selected) ── */}
      {selectedConfig && (
        <div className={styles.filterBar}>
          {/* From date */}
          <Field label="From" className={styles.filterField}>
            <Input
              type="date"
              value={draftFilters.fromDate}
              onChange={(_e, data) =>
                setDraftFilters((prev) => ({ ...prev, fromDate: data.value }))
              }
              max={draftFilters.toDate || todayDate()}
            />
          </Field>

          {/* To date */}
          <Field label="To" className={styles.filterField}>
            <Input
              type="date"
              value={draftFilters.toDate}
              onChange={(_e, data) =>
                setDraftFilters((prev) => ({ ...prev, toDate: data.value }))
              }
              min={draftFilters.fromDate}
              max={todayDate()}
            />
          </Field>

          {/* Category */}
          <Field label="Category" className={styles.filterField}>
            <Select
              value={draftFilters.category}
              onChange={(_e, data) =>
                setDraftFilters((prev) => ({
                  ...prev,
                  category: data.value as AuditCategory | "",
                }))
              }
            >
              {AUDIT_CATEGORIES.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </Select>
          </Field>

          {/* Apply button */}
          <Button
            className={styles.filterApplyButton}
            appearance="primary"
            onClick={handleApplyFilters}
            disabled={isLoading}
            icon={<Filter20Regular />}
          >
            Apply Filters
          </Button>
        </div>
      )}

      {/* ── Main content area ── */}
      <div className={styles.gridArea}>
        {/* No config selected */}
        {!selectedConfig && renderNoConfig()}

        {/* Error state */}
        {selectedConfig && error && renderError()}

        {/* Loading state */}
        {selectedConfig && isLoading && renderLoading()}

        {/* Empty state */}
        {selectedConfig && !isLoading && !error && totalEntries === 0 && renderEmpty()}

        {/* Data grid */}
        {selectedConfig && !isLoading && !error && totalEntries > 0 && (
          <>
            <div className={styles.gridWrapper}>
              <DataGrid
                items={pageEntries}
                columns={columns}
                sortable
                getRowId={(item: AuditLogEntry) => item.id}
                style={{ width: "100%" }}
                aria-label="Audit log entries"
              >
                <DataGridHeader>
                  <DataGridRow>
                    {({ renderHeaderCell }) => (
                      <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                    )}
                  </DataGridRow>
                </DataGridHeader>
                <DataGridBody<AuditLogEntry>>
                  {({ item, rowId }) => (
                    <DataGridRow<AuditLogEntry> key={rowId}>
                      {({ renderCell }) => (
                        <DataGridCell>{renderCell(item)}</DataGridCell>
                      )}
                    </DataGridRow>
                  )}
                </DataGridBody>
              </DataGrid>
            </div>

            {/* ── Pagination Controls ── */}
            <div className={styles.pagination}>
              <Text size={200} className={styles.paginationText}>
                {paginationLabel}
              </Text>
              <Tooltip content="Previous page" relationship="label">
                <Button
                  appearance="subtle"
                  icon={<ArrowLeft20Regular />}
                  onClick={handlePrevPage}
                  disabled={pageIndex === 0}
                  aria-label="Previous page"
                />
              </Tooltip>
              <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                Page {pageIndex + 1} of {totalPages}
              </Text>
              <Tooltip content="Next page" relationship="label">
                <Button
                  appearance="subtle"
                  icon={<ArrowRight20Regular />}
                  onClick={handleNextPage}
                  disabled={pageIndex >= totalPages - 1}
                  aria-label="Next page"
                />
              </Tooltip>
            </div>
          </>
        )}
      </div>
    </div>
  );
};
