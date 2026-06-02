/**
 * `<DataGrid configId={...} />` — the core component of the Spaarke DataGrid framework.
 *
 * Configuration-driven Dataverse grid built on Fluent v9 native `DataGrid` primitives:
 * - Three-tier config resolution (explicit `overrides` → `sprk_configjson` → metadata + layoutXml → framework defaults)
 * - Native selection (`selectionMode="multiselect"`) — NO hand-rolled checkbox column
 * - Native sort (`sortable` + `createTableColumn({ compare })`) — NO manual sort state
 * - Native column resize (`resizableColumns` + `columnSizingOptions`) — NO drag handles
 * - Native keyboard nav (`focusMode="composite"`) — NO `onKeyDown` switch
 * - Lazy infinite-scroll paging (FetchXML paging-cookie chain via {@link useLazyLoad})
 * - `DataGridContextProvider` mounted internally so extensions can call {@link useDataGridContext}
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6, §11.5.1
 * **FR**: FR-DG-01 (component contract), FR-DG-04 (three-tier resolution),
 *         FR-DG-11 (Fluent native exploitation), FR-DG-12 (lazy paging),
 *         FR-DG-15 (useDataGridContext hook)
 * **ADR**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe)
 *
 * **NO @fluentui/react v8 imports. NO raw hex. NO useId / useSyncExternalStore / createRoot.**
 *
 * @see SearchResultsGrid.tsx — closest existing Fluent v9 native DataGrid usage
 */

import * as React from 'react';
import {
  DataGrid as FluentDataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  createTableColumn,
  Spinner,
  Text,
  Link,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  type TableColumnDefinition,
  type DataGridProps as FluentDataGridProps,
  type TableRowId,
  type TableColumnSizingOptions,
} from '@fluentui/react-components';

import type {
  IDataverseClient,
  EntityMetadata,
  SavedQueryResult,
} from '../../services/IDataverseClient';
import { XrmDataverseClient } from '../../services/XrmDataverseClient';
import type { DataGridConfiguration } from '../../types/DataGridConfiguration';
import { isValidDataGridConfiguration } from '../../types/DataGridConfiguration';
import {
  DataGridContextProvider,
  type DataGridContextValue,
  type DataGridParentContext,
} from '../../hooks/useDataGridContext';
import { dataGridTokens } from './tokens';
import {
  resolveConfig,
  type DataGridOverrides,
  type ResolvedConfig,
  type ResolvedColumn,
} from './configResolution';
import { useLazyLoad } from './useLazyLoad';

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

/** Host-side context object passed to event handlers. */
export interface DataGridHostContext {
  configId: string;
  entityName: string;
  parentContext?: DataGridParentContext;
  selectedIds: string[];
}

/**
 * Props for {@link DataGrid}.
 *
 * `dataverseClient` is OPTIONAL — defaults to `new XrmDataverseClient()` per spec FR-DG-01.
 * Non-MDA hosts (Code Pages outside Dataverse, workspace widgets, Storybook stories) MUST
 * pass an explicit client (`BffDataverseClient` from task 015 once it lands, or a mock).
 * The `XrmDataverseClient` default itself throws a clear error if `Xrm` is unavailable at
 * runtime — see `XrmDataverseClient.ts`.
 */
export interface DataGridProps {
  /** REQUIRED — GUID of the `sprk_gridconfiguration` record to render. */
  configId: string;

  /**
   * OPTIONAL — Dataverse access implementation.
   * Defaults to `new XrmDataverseClient()` (MDA hosts). Non-MDA hosts MUST pass an
   * explicit client (Storybook: mock; Code Pages outside MDA: BffDataverseClient).
   */
  dataverseClient?: IDataverseClient;

  /** OPTIONAL — drill-through context (e.g., current Matter on form embed). */
  parentContext?: DataGridParentContext;

  /** OPTIONAL — fires when the user clicks the primary-name link on a row. */
  onRecordOpen?: (
    recordId: string,
    record: Record<string, unknown>,
    ctx: DataGridHostContext,
  ) => void;

  /** OPTIONAL — fires when the user invokes a per-row secondary action. */
  onRecordAction?: (
    actionId: string,
    recordId: string,
    record: Record<string, unknown>,
    ctx: DataGridHostContext,
  ) => void;

  /** OPTIONAL — fires when the user invokes a command bar action. */
  onCommandInvoke?: (
    commandId: string,
    selectedIds: string[],
    ctx: DataGridHostContext,
  ) => void;

  /** OPTIONAL — escape-hatch overrides for column renderers, badge map, filter chip allowlist. */
  overrides?: DataGridOverrides;

  /** OPTIONAL — additional class merged AFTER component classes (per Spaarke convention). */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles — `makeStyles` at MODULE SCOPE (NEVER inside component) per ADR-021
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    position: 'relative',
    backgroundColor: dataGridTokens.container.background,
    ...shorthands.border('1px', 'solid', dataGridTokens.container.border),
    borderRadius: dataGridTokens.container.borderRadius,
    boxShadow: dataGridTokens.container.shadow,
    overflow: 'hidden',
  },
  gridScroll: {
    flex: 1,
    overflow: 'auto',
    position: 'relative',
  },
  loadingOverlay: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: dataGridTokens.container.background,
    opacity: 0.85,
    zIndex: 10,
  },
  loadMoreContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalM),
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    columnGap: tokens.spacingVerticalS,
    rowGap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
    ...shorthands.padding(tokens.spacingVerticalXXL),
  },
  sentinel: {
    height: '1px',
    width: '100%',
  },
  cell: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontFamily: dataGridTokens.cell.fontFamily,
    fontSize: dataGridTokens.cell.fontSize,
    fontWeight: dataGridTokens.cell.fontWeight,
  },
  headerCell: {
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontWeight: dataGridTokens.header.fontWeight,
    backgroundColor: dataGridTokens.header.background,
    color: dataGridTokens.header.foreground,
  },
  primaryNameLink: {
    color: dataGridTokens.primaryNameLink.color,
    fontWeight: dataGridTokens.primaryNameLink.fontWeight,
    textDecorationLine: 'none',
    cursor: 'pointer',
    ':hover': {
      textDecorationLine: 'underline',
    },
  },
  errorBanner: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalL),
    color: tokens.colorPaletteRedForeground1,
    backgroundColor: tokens.colorPaletteRedBackground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helper — pretty-print arbitrary cell value into a string
// ─────────────────────────────────────────────────────────────────────────────

function renderCellValue(value: unknown, renderer: string): string {
  if (value === null || value === undefined || value === '') return '';
  switch (renderer) {
    case 'currency': {
      const num = typeof value === 'number' ? value : Number(value);
      if (Number.isNaN(num)) return String(value);
      return new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency: 'USD',
        minimumFractionDigits: 2,
      }).format(num);
    }
    case 'percentage': {
      const num = typeof value === 'number' ? value : Number(value);
      if (Number.isNaN(num)) return String(value);
      return `${Math.round(num * 100)}%`;
    }
    case 'date': {
      try {
        return new Date(value as string | number).toLocaleDateString();
      } catch {
        return String(value);
      }
    }
    case 'datetime': {
      try {
        return new Date(value as string | number).toLocaleString();
      } catch {
        return String(value);
      }
    }
    default: {
      if (typeof value === 'object') {
        const v = value as Record<string, unknown>;
        if (typeof v.name === 'string') return v.name;
        return JSON.stringify(value);
      }
      return String(value);
    }
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase A: load + parse the sprk_gridconfiguration record (best-effort)
// ─────────────────────────────────────────────────────────────────────────────

interface ConfigLoadState {
  configRecord: DataGridConfiguration | null;
  savedQuery: SavedQueryResult | null;
  entityMetadata: EntityMetadata | null;
  isLoading: boolean;
  error: Error | null;
}

const INITIAL_LOAD_STATE: ConfigLoadState = {
  configRecord: null,
  savedQuery: null,
  entityMetadata: null,
  isLoading: true,
  error: null,
};

/**
 * Best-effort fetch of the `sprk_gridconfiguration` record (FR-DG-04 — non-existent
 * configIds MUST fall through gracefully). Returns `null` configRecord on miss.
 */
async function fetchConfigRecord(
  dataverseClient: IDataverseClient,
  configId: string,
): Promise<DataGridConfiguration | null> {
  try {
    const rec = await dataverseClient.retrieveRecord<Record<string, unknown>>(
      'sprk_gridconfiguration',
      configId,
      ['sprk_configjson'],
    );
    const raw = rec['sprk_configjson'];
    if (typeof raw !== 'string' || raw.trim() === '') return null;
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      // Per FR-DG-03: invalid JSON does NOT throw — fall back to defaults.
      // eslint-disable-next-line no-console
      console.warn(`[DataGrid] Invalid JSON in sprk_configjson for configId=${configId}`);
      return null;
    }
    if (!isValidDataGridConfiguration(parsed)) {
      // eslint-disable-next-line no-console
      console.warn(`[DataGrid] sprk_configjson did not match v1.0 schema for configId=${configId}`);
      return null;
    }
    return parsed;
  } catch {
    // configId not found, OR Xrm threw — graceful fallthrough.
    return null;
  }
}

/**
 * Resolve `source` into a `{ entityName, fetchXml, layoutXml }` triple via savedquery
 * lookup, inline literal, or savedquery-set discovery + first match.
 */
async function resolveSource(
  dataverseClient: IDataverseClient,
  configRecord: DataGridConfiguration | null,
  fallbackEntityName: string | undefined,
): Promise<SavedQueryResult | null> {
  if (!configRecord) {
    // No config record — caller may pass `fallbackEntityName` for synthesized fallback.
    if (!fallbackEntityName) return null;
    return null; // No fetchXml available; columns will synthesize from metadata.
  }
  const source = configRecord.source;
  if (source.type === 'savedquery') {
    try {
      return await dataverseClient.retrieveSavedQuery(source.savedQueryId);
    } catch {
      return null;
    }
  }
  if (source.type === 'inline') {
    // Inline source carries fetchXml + layoutXml directly; entityName extracted from fetchXml.
    const entityName = extractEntityFromFetchXml(source.fetchXml) ?? '';
    return {
      entityName,
      fetchXml: source.fetchXml,
      layoutXml: source.layoutXml,
      name: configRecord.display?.title ?? 'Inline',
    };
  }
  if (source.type === 'savedquery-set') {
    try {
      const queries = await dataverseClient.retrieveSavedQueriesForEntity(source.entityLogicalName);
      const def = queries.find((q) => q.isDefault) ?? queries[0];
      if (!def) return null;
      return await dataverseClient.retrieveSavedQuery(def.id);
    } catch {
      return null;
    }
  }
  return null;
}

function extractEntityFromFetchXml(fetchXml: string): string | undefined {
  if (!fetchXml) return undefined;
  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(fetchXml, 'text/xml');
    if (doc.querySelector('parsererror')) return undefined;
    return doc.querySelector('entity')?.getAttribute('name') ?? undefined;
  } catch {
    return undefined;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// The component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Configuration-driven Fluent v9 DataGrid.
 *
 * See module-level JSDoc for the full feature list and ADR references.
 */
export const DataGrid: React.FC<DataGridProps> = (props) => {
  const {
    configId,
    dataverseClient: dataverseClientProp,
    parentContext,
    onRecordOpen,
    onRecordAction: _onRecordAction,
    onCommandInvoke: _onCommandInvoke,
    overrides,
    className,
  } = props;

  // Stable default: instantiate XrmDataverseClient once if no client passed.
  // XrmDataverseClient throws at first method call if Xrm context is unavailable
  // (Storybook / non-MDA hosts) — callers in those contexts MUST pass an explicit client.
  const defaultClientRef = React.useRef<IDataverseClient | undefined>(undefined);
  if (!dataverseClientProp && !defaultClientRef.current) {
    defaultClientRef.current = new XrmDataverseClient();
  }
  const dataverseClient: IDataverseClient = dataverseClientProp ?? (defaultClientRef.current as IDataverseClient);

  const styles = useStyles();

  // Phase 1: load configRecord + savedQuery + metadata
  const [loadState, setLoadState] = React.useState<ConfigLoadState>(INITIAL_LOAD_STATE);
  const isMountedRef = React.useRef<boolean>(true);
  const [refreshCounter, setRefreshCounter] = React.useState<number>(0);

  React.useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  React.useEffect(() => {
    let cancelled = false;
    setLoadState((prev) => ({ ...prev, isLoading: true, error: null }));

    (async () => {
      try {
        const configRecord = await fetchConfigRecord(dataverseClient, configId);
        const savedQuery = await resolveSource(dataverseClient, configRecord, undefined);
        const entityName =
          savedQuery?.entityName ??
          (configRecord?.source?.type === 'savedquery-set'
            ? configRecord.source.entityLogicalName
            : undefined);

        if (!entityName) {
          // Cannot fetch metadata without an entity name. Surface a graceful empty state.
          if (cancelled || !isMountedRef.current) return;
          setLoadState({
            configRecord,
            savedQuery,
            entityMetadata: null,
            isLoading: false,
            error: new Error(
              `[DataGrid] Cannot resolve entityName from configId=${configId}. ` +
                'No savedquery, inline fetchXml, or savedquery-set entityLogicalName was available.',
            ),
          });
          return;
        }
        const entityMetadata = await dataverseClient.retrieveEntityMetadata(entityName);
        if (cancelled || !isMountedRef.current) return;
        setLoadState({
          configRecord,
          savedQuery,
          entityMetadata,
          isLoading: false,
          error: null,
        });
      } catch (err) {
        if (cancelled || !isMountedRef.current) return;
        setLoadState({
          configRecord: null,
          savedQuery: null,
          entityMetadata: null,
          isLoading: false,
          error: err instanceof Error ? err : new Error(String(err)),
        });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [dataverseClient, configId, refreshCounter]);

  // Once we have metadata, resolve the full configuration.
  const resolved: ResolvedConfig | null = React.useMemo(() => {
    if (!loadState.entityMetadata) return null;
    return resolveConfig(
      overrides,
      loadState.configRecord,
      loadState.entityMetadata,
      loadState.savedQuery?.layoutXml,
      loadState.savedQuery?.entityName,
    );
  }, [loadState.entityMetadata, loadState.configRecord, loadState.savedQuery, overrides]);

  // Lazy load — only kicks off once we have entityName + fetchXml
  const fetchXml = loadState.savedQuery?.fetchXml ?? '';
  const entityNameForLoad = resolved?.entityName ?? '';
  const pageSize = resolved?.behavior.pageSize ?? 100;

  const {
    records,
    isLoading: isLoadingRows,
    hasMore,
    fetchNextPage,
    reset: resetLazyLoad,
  } = useLazyLoad<Record<string, unknown>>({
    dataverseClient,
    entityName: entityNameForLoad,
    fetchXml,
    pageSize,
  });

  // Selection — preserved across pages per FR-DG-12.
  const [selectedRowIds, setSelectedRowIds] = React.useState<Set<TableRowId>>(new Set());

  // Sentinel + IntersectionObserver for lazy paging.
  const sentinelRef = React.useRef<HTMLDivElement>(null);
  const scrollContainerRef = React.useRef<HTMLDivElement>(null);

  React.useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel || !hasMore || isLoadingRows) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting) {
          fetchNextPage();
        }
      },
      { root: scrollContainerRef.current, rootMargin: '200px', threshold: 0 },
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [hasMore, isLoadingRows, fetchNextPage]);

  // ───────────────────────────────────────────────────────────────────────────
  // Build Fluent v9 table columns + sizing options from ResolvedConfig.columns
  // ───────────────────────────────────────────────────────────────────────────
  type GridItem = Record<string, unknown> & { _rowId: string };

  const visibleColumns: ReadonlyArray<ResolvedColumn> = React.useMemo(
    () => (resolved?.columns ?? []).filter((c) => !c.hidden),
    [resolved],
  );

  const tableColumns: TableColumnDefinition<GridItem>[] = React.useMemo(() => {
    if (!resolved) return [];
    return visibleColumns.map((col) =>
      createTableColumn<GridItem>({
        columnId: col.name,
        compare: (a, b) => {
          const aVal = a[col.name];
          const bVal = b[col.name];
          if (typeof aVal === 'number' && typeof bVal === 'number') {
            return aVal - bVal;
          }
          return String(aVal ?? '').localeCompare(String(bVal ?? ''));
        },
        renderHeaderCell: () => col.label,
        renderCell: (item) => {
          const value = item[col.name];

          if (col.isPrimaryName && resolved && onRecordOpen) {
            const idValue = String(item[resolved.primaryIdAttribute] ?? item._rowId ?? '');
            const label = renderCellValue(value, 'default') || '(no name)';
            return React.createElement(
              Link,
              {
                as: 'button',
                className: styles.primaryNameLink,
                onClick: () => {
                  onRecordOpen(idValue, item, {
                    configId,
                    entityName: resolved.entityName,
                    parentContext,
                    selectedIds: Array.from(selectedRowIds).map(String),
                  });
                },
              },
              label,
            );
          }

          // Host-supplied custom renderer wins over derived renderer.
          const customRenderer = resolved?.overrides.columnRenderers?.[col.name];
          if (customRenderer) {
            return customRenderer(value, item) as React.ReactNode;
          }
          return renderCellValue(value, col.renderer);
        },
      }),
    );
  }, [resolved, visibleColumns, onRecordOpen, configId, parentContext, selectedRowIds, styles.primaryNameLink]);

  const columnSizingOptions: TableColumnSizingOptions = React.useMemo(() => {
    const options: TableColumnSizingOptions = {};
    for (const col of visibleColumns) {
      options[col.name] = {
        defaultWidth: col.width,
        minWidth: Math.max(80, Math.round(col.width * 0.5)),
        idealWidth: col.width,
      };
    }
    return options;
  }, [visibleColumns]);

  // Items with stable row ids (primaryId-derived, so selection survives reorder).
  const items: GridItem[] = React.useMemo(() => {
    if (!resolved) return [];
    return records.map((r, i) => ({
      ...r,
      _rowId: String(r[resolved.primaryIdAttribute] ?? `__row_${i}`),
    }));
  }, [records, resolved]);

  const handleSelectionChange: FluentDataGridProps['onSelectionChange'] = React.useCallback(
    (_ev, data: { selectedItems: Set<TableRowId> }) => {
      setSelectedRowIds(data.selectedItems);
    },
    [],
  );

  const refresh = React.useCallback(() => {
    // Re-fetch from config (in case the underlying record changed) AND reset lazy load.
    setRefreshCounter((n) => n + 1);
    resetLazyLoad();
  }, [resetLazyLoad]);

  // Selection ids as strings — passed into context for extensions.
  const selectedIds = React.useMemo(
    () => new Set<string>(Array.from(selectedRowIds).map(String)),
    [selectedRowIds],
  );

  // Density derives from configjson; consumer may toggle via a later command-bar primitive.
  const density: 'medium' | 'small' =
    resolved?.display.densityDefault === 'compact' ? 'small' : 'medium';

  // ───────────────────────────────────────────────────────────────────────────
  // Loading / error / empty states
  // ───────────────────────────────────────────────────────────────────────────
  if (loadState.isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)} aria-busy="true">
        <div className={styles.loadingOverlay}>
          <Spinner size="medium" label="Loading grid configuration..." />
        </div>
      </div>
    );
  }

  if (loadState.error || !loadState.entityMetadata || !resolved) {
    return (
      <div className={mergeClasses(styles.root, className)} role="alert">
        <div className={styles.errorBanner}>
          <Text size={300}>
            {loadState.error?.message ?? 'DataGrid configuration could not be resolved.'}
          </Text>
        </div>
      </div>
    );
  }

  const contextValue: DataGridContextValue = {
    selectedIds,
    refresh,
    currentView: resolved.display.title ?? loadState.savedQuery?.name ?? 'View',
    parentContext,
    dataverseClient,
    entityMetadata: loadState.entityMetadata,
  };

  const isEmpty = !isLoadingRows && records.length === 0 && !loadState.isLoading;

  return (
    <DataGridContextProvider value={contextValue}>
      <div className={mergeClasses(styles.root, className)} aria-label={contextValue.currentView}>
        {isEmpty ? (
          <div className={styles.emptyState}>
            <Text size={400} weight="semibold">
              {resolved.display.emptyStateMessage ?? 'No records to display'}
            </Text>
          </div>
        ) : (
          <div className={styles.gridScroll} ref={scrollContainerRef}>
            <FluentDataGrid
              items={items}
              columns={tableColumns}
              selectionMode={resolved.behavior.selectionMode === 'none'
                ? undefined
                : resolved.behavior.selectionMode === 'single'
                  ? 'single'
                  : 'multiselect'}
              selectedItems={selectedRowIds}
              onSelectionChange={handleSelectionChange}
              sortable={resolved.behavior.enableSorting}
              resizableColumns={resolved.behavior.enableColumnResize}
              columnSizingOptions={columnSizingOptions}
              focusMode="composite"
              size={density}
              subtleSelection
              getRowId={(item: GridItem) => item._rowId}
              style={{ minWidth: '100%' }}
              aria-label={contextValue.currentView}
            >
              <DataGridHeader>
                <DataGridRow
                  selectionCell={{ checkboxIndicator: { 'aria-label': 'Select all rows' } }}
                >
                  {({ renderHeaderCell }) => (
                    <DataGridHeaderCell className={styles.headerCell}>
                      {renderHeaderCell()}
                    </DataGridHeaderCell>
                  )}
                </DataGridRow>
              </DataGridHeader>
              <DataGridBody<GridItem>>
                {({ item, rowId }) => (
                  <DataGridRow<GridItem>
                    key={rowId}
                    selectionCell={{ checkboxIndicator: { 'aria-label': 'Select row' } }}
                  >
                    {({ renderCell }) => (
                      <DataGridCell className={styles.cell}>{renderCell(item)}</DataGridCell>
                    )}
                  </DataGridRow>
                )}
              </DataGridBody>
            </FluentDataGrid>

            {isLoadingRows && (
              <div className={styles.loadMoreContainer}>
                <Spinner size="small" label="Loading more..." />
              </div>
            )}

            {/* Infinite-scroll sentinel — IntersectionObserver wired above in useEffect. */}
            <div ref={sentinelRef} className={styles.sentinel} aria-hidden="true" />
          </div>
        )}
      </div>
    </DataGridContextProvider>
  );
};

export default DataGrid;
