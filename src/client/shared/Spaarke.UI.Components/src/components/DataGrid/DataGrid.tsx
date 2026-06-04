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
  webLightTheme,
  type Theme,
  type TableColumnDefinition,
  type DataGridProps as FluentDataGridProps,
  type TableRowId,
  type TableColumnSizingOptions,
} from '@fluentui/react-components';

import type { IDataverseClient, EntityMetadata, SavedQueryResult } from '../../services/IDataverseClient';
import { XrmDataverseClient } from '../../services/XrmDataverseClient';
import type { DataGridConfiguration } from '../../types/DataGridConfiguration';
import { isValidDataGridConfiguration } from '../../types/DataGridConfiguration';
import {
  DataGridContextProvider,
  type DataGridContextValue,
  type DataGridParentContext,
} from '../../hooks/useDataGridContext';
import { dataGridTokens } from './tokens';
import { resolveConfig, type DataGridOverrides, type ResolvedConfig, type ResolvedColumn } from './configResolution';
import { useLazyLoad } from './useLazyLoad';
import { overlayParentContextFilter, overlayHostFilters, type HostFilterCondition } from './fetchXmlOverlay';
import { CommandBar as DataGridCommandBar } from './commandBar/CommandBar';
import {
  discoverChips,
  augmentFetchXmlWithChips,
  FilterChipBar,
  type ChipDescriptor,
  type ChipState,
} from './filterChips';
import { HeaderCellContent } from './HeaderCellContent';
import { ViewSelector, type SavedView } from './ViewSelector';
import type { SavedQuerySummary } from '../../services/IDataverseClient';

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

  /**
   * OPTIONAL — host-injected FetchXML conditions overlaid onto the resolved query.
   *
   * Permanent third composition layer (after savedquery base + parent-context overlay,
   * before chip augmentation). Designed for hosts that own their own filter UI (e.g.
   * the SpaarkeAi Calendar workspace widget's filter row) and need to translate that
   * state into FetchXML at runtime without writing it to a savedquery.
   *
   * For stability across re-renders, pass a memoized array (e.g. `useMemo`); the
   * framework re-runs the composition pipeline when this prop's identity changes.
   *
   * See {@link HostFilterCondition} for the value shape + supported operators.
   */
  hostFilters?: ReadonlyArray<HostFilterCondition>;

  /**
   * OPTIONAL — fires every time `useLazyLoad` resolves a fresh records page.
   *
   * Receives the full accumulated records array (page 1 ∪ page 2 ∪ ...), allowing
   * the host to derive aggregate state (e.g. the Calendar widget's per-date event
   * counts for its calendar strip dot indicators). Fires after every page fetch,
   * including the initial load and any lazy "load more" page.
   *
   * Mirrors the legacy `GridSection.onRecordsLoaded` contract — host code that
   * previously consumed `GridSection` can pass the same handler in.
   */
  onRecordsLoaded?: (records: ReadonlyArray<Record<string, unknown>>) => void;

  /** OPTIONAL — fires when the user clicks the primary-name link on a row. */
  onRecordOpen?: (recordId: string, record: Record<string, unknown>, ctx: DataGridHostContext) => void;

  /** OPTIONAL — fires when the user invokes a per-row secondary action. */
  onRecordAction?: (
    actionId: string,
    recordId: string,
    record: Record<string, unknown>,
    ctx: DataGridHostContext
  ) => void;

  /** OPTIONAL — fires when the user invokes a command bar action. */
  onCommandInvoke?: (commandId: string, selectedIds: string[], ctx: DataGridHostContext) => void;

  /** OPTIONAL — escape-hatch overrides for column renderers, badge map, filter chip allowlist. */
  overrides?: DataGridOverrides;

  /**
   * OPTIONAL — active Fluent v9 theme. Passed to inner CommandBar so dialog portals
   * resolve dark/light correctly (NFR-03). Defaults to `webLightTheme`. Hosts that
   * render the grid inside a themed `<FluentProvider>` SHOULD pass the same theme.
   */
  theme?: Theme;

  /**
   * OPTIONAL — fires when the user clicks the back arrow next to the view picker.
   * When undefined, the back arrow is HIDDEN. Custom Pages should pass a handler
   * that closes the dialog (typically `() => window.close()`).
   */
  onBack?: () => void;

  /** OPTIONAL — additional class merged AFTER component classes (per Spaarke convention). */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles — `makeStyles` at MODULE SCOPE (NEVER inside component) per ADR-021
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    // Outer wrapper is purely structural — the visual depth lives on the two
    // nested cards (`headerCard` + `innerCard`) so they read as separate
    // sections in the Power Apps OOB style. Outer surface stays flush with the
    // host modal background.
    //
    // Generous outer padding so the two cards have visible breathing room
    // from the modal frame — matches the Power Apps OOB layout dimensions
    // (see notes/testing-screenshots/oob-layout-dimensions.jpg) and gives the
    // two cards the polished depth that the sprk_invoicespage drill-through
    // ships. **CRITICAL host requirement**: the Code Page shell `index.html`
    // MUST set `*, *::before, *::after { box-sizing: border-box; }` — otherwise
    // this padding adds to the 100%-of-viewport size and the grid overflows.
    // See sprk_invoicespage/index.html for the canonical reset.
    //
    // **Task 035-fix-iteration-3 (2026-06-03)**: paddings restored to L/M after
    // iteration 2 shrunk them to S/S and killed the visual card chrome. The
    // actual root cause of the iteration 1 overflow was a missing `box-sizing`
    // reset in EventsPage's index.html (since added) — NOT the padding values.
    // `minHeight: 0` retained as the canonical flex-overflow fix; without it,
    // the natural content height of the inner card defeats `flex: 1` shrinkage.
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    minHeight: 0,
    position: 'relative',
    backgroundColor: tokens.colorNeutralBackground2,
    overflow: 'hidden',
    rowGap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingBottom: tokens.spacingVerticalM,
  },
  gridScroll: {
    flex: 1,
    overflow: 'auto',
    position: 'relative',
    // Inset the rows away from the inner-card border so per-row bottom borders
    // stop short of the outer card edge (Power Apps OOB pattern — the grid
    // sits inside its container with breathing room on the left/right).
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    // Thin overlay scrollbar so the right edge looks clean even when content
    // overflows. Native overlay scrollbars on macOS/iOS already auto-hide;
    // these declarations cover Windows Chrome/Edge + Firefox.
    scrollbarWidth: 'thin',
    '::-webkit-scrollbar': {
      width: '8px',
      height: '8px',
    },
    '::-webkit-scrollbar-track': {
      backgroundColor: 'transparent',
    },
    '::-webkit-scrollbar-thumb': {
      backgroundColor: tokens.colorNeutralStroke2,
      borderRadius: '4px',
    },
    '::-webkit-scrollbar-thumb:hover': {
      backgroundColor: tokens.colorNeutralStroke1,
    },
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
    // Sits inside gridScroll BELOW the (always-rendered) FluentDataGrid header
    // row so the column-header chevron menus (Filter by / Sort) stay reachable
    // when a filter returns 0 rows. Power Apps OOB pattern.
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    columnGap: tokens.spacingVerticalS,
    rowGap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
    minHeight: '120px',
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
    // Power Apps OOB body row height ≈ 38px.
    minHeight: '38px',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground1,
  },
  headerCell: {
    // Power Apps OOB header row height ≈ 41px.
    minHeight: '41px',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
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
  /**
   * Header CARD — own card with border + matching depth. Holds:
   *   [← | ViewSelector ⌄]            [Refresh] [Delete] [⌄] [⋯]
   * Matches the grid card visual so the modal reads as two equal sections,
   * per `projects/spaarke-datagrid-framework-r1/notes/testing-screenshots/oob-view-modal.jpg`.
   *
   * `minWidth: 0` + `overflow: hidden` are critical — without them the right-
   * side CommandBar (with subtle buttons + overflow menu) can extend past the
   * dialog viewport when many commands are configured.
   */
  headerCard: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke3),
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
    flexShrink: 0,
    minWidth: 0,
    overflow: 'hidden',
  },
  /**
   * Inner grid CARD — wraps the FilterChipBar + grid header/body + footer. Same
   * border + depth as `headerCard` so the two sections read as equals.
   */
  innerCard: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke3),
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
    overflow: 'hidden',
  },
  /** Vertical divider between the back arrow and the view selector label. */
  backDivider: {
    width: '1px',
    height: '24px',
    backgroundColor: tokens.colorNeutralStroke2,
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  headerLeft: {
    display: 'flex',
    alignItems: 'center',
    minWidth: 0,
  },
  title: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    // Power Apps "Active Invoices" title is ~fontSizeBase500 (20px).
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
  },
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-start',
    // Footer is taller than a single row so the "Rows: N" text reads as a
    // distinct status band rather than a cell remnant.
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    flexShrink: 0,
    minHeight: '40px',
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
  /**
   * Sibling saved queries for the active entity (drives the ViewSelector menu).
   * Populated best-effort after entity resolution; failure is non-fatal.
   */
  availableViews: ReadonlyArray<SavedQuerySummary>;
  isLoading: boolean;
  error: Error | null;
}

const INITIAL_LOAD_STATE: ConfigLoadState = {
  configRecord: null,
  savedQuery: null,
  entityMetadata: null,
  availableViews: [],
  isLoading: true,
  error: null,
};

/**
 * Best-effort fetch of the `sprk_gridconfiguration` record (FR-DG-04 — non-existent
 * configIds MUST fall through gracefully). Returns `null` configRecord on miss.
 */
async function fetchConfigRecord(
  dataverseClient: IDataverseClient,
  configId: string
): Promise<DataGridConfiguration | null> {
  try {
    const rec = await dataverseClient.retrieveRecord<Record<string, unknown>>('sprk_gridconfiguration', configId, [
      'sprk_configjson',
    ]);
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
  fallbackEntityName: string | undefined
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
      const def = queries.find(q => q.isDefault) ?? queries[0];
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
export const DataGrid: React.FC<DataGridProps> = props => {
  const {
    configId,
    dataverseClient: dataverseClientProp,
    parentContext,
    hostFilters,
    onRecordsLoaded,
    onRecordOpen,
    onRecordAction: _onRecordAction,
    onCommandInvoke,
    overrides,
    theme = webLightTheme,
    onBack,
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
  /**
   * Active saved query id — `undefined` means "use the configRecord default
   * (configRecord.source.savedQueryId or the default of the savedquery-set)".
   * Set by the ViewSelector when the user picks a different view.
   * Reset to `undefined` whenever `configId` changes (different grid surface).
   */
  const [activeSavedQueryId, setActiveSavedQueryId] = React.useState<string | undefined>(undefined);
  React.useEffect(() => {
    setActiveSavedQueryId(undefined);
  }, [configId]);

  React.useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  React.useEffect(() => {
    let cancelled = false;
    setLoadState(prev => ({ ...prev, isLoading: true, error: null }));

    (async () => {
      try {
        const configRecord = await fetchConfigRecord(dataverseClient, configId);
        // If the user has switched views via the ViewSelector, that id takes
        // precedence over the configRecord's default savedQueryId.
        const savedQuery: SavedQueryResult | null = activeSavedQueryId
          ? await dataverseClient.retrieveSavedQuery(activeSavedQueryId).catch(() => null)
          : await resolveSource(dataverseClient, configRecord, undefined);
        const entityName =
          savedQuery?.entityName ??
          (configRecord?.source?.type === 'savedquery-set' ? configRecord.source.entityLogicalName : undefined);

        if (!entityName) {
          // Cannot fetch metadata without an entity name. Surface a graceful empty state.
          if (cancelled || !isMountedRef.current) return;
          setLoadState({
            configRecord,
            savedQuery,
            entityMetadata: null,
            availableViews: [],
            isLoading: false,
            error: new Error(
              `[DataGrid] Cannot resolve entityName from configId=${configId}. ` +
                'No savedquery, inline fetchXml, or savedquery-set entityLogicalName was available.'
            ),
          });
          return;
        }
        // Fetch metadata + sibling saved views in parallel. View list is best-effort;
        // on failure we fall back to a single-view ViewSelector (just the active view).
        const [entityMetadata, availableViews] = await Promise.all([
          dataverseClient.retrieveEntityMetadata(entityName),
          dataverseClient.retrieveSavedQueriesForEntity(entityName).catch(() => [] as SavedQuerySummary[]),
        ]);
        if (cancelled || !isMountedRef.current) return;
        setLoadState({
          configRecord,
          savedQuery,
          entityMetadata,
          availableViews,
          isLoading: false,
          error: null,
        });
      } catch (err) {
        if (cancelled || !isMountedRef.current) return;
        setLoadState({
          configRecord: null,
          savedQuery: null,
          entityMetadata: null,
          availableViews: [],
          isLoading: false,
          error: err instanceof Error ? err : new Error(String(err)),
        });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [dataverseClient, configId, refreshCounter, activeSavedQueryId]);

  // Once we have metadata, resolve the full configuration.
  const resolved: ResolvedConfig | null = React.useMemo(() => {
    if (!loadState.entityMetadata) return null;
    return resolveConfig(
      overrides,
      loadState.configRecord,
      loadState.entityMetadata,
      loadState.savedQuery?.layoutXml,
      loadState.savedQuery?.entityName
    );
  }, [loadState.entityMetadata, loadState.configRecord, loadState.savedQuery, overrides]);

  // Filter chip state (controlled inside DataGrid). Reset on configId or
  // activeSavedQueryId change (different grid / different view).
  const [chipState, setChipState] = React.useState<ChipState>({});
  React.useEffect(() => {
    setChipState({});
  }, [configId, activeSavedQueryId]);

  // Discover chip descriptors from the resolved filterChips config + metadata.
  // FALLBACK: when metadata is too thin to classify any column (older Xrm
  // clients return a slimmed-down attribute payload), synthesize a `text`
  // chip for each visible non-primary-id column so every header still gets
  // a working "Filter by" affordance. The OOB Power Apps grid always offers
  // Filter by, so the user's mental model expects it on every column.
  const chipDescriptors = React.useMemo(() => {
    if (!resolved || !loadState.entityMetadata) return [];
    const discovered = discoverChips(resolved.filterChips, resolved.columns, loadState.entityMetadata);
    if (discovered.length > 0) return discovered;
    return resolved.columns
      .filter(c => !c.hidden && c.name !== resolved.primaryIdAttribute)
      .map(c => ({
        kind: 'text' as const,
        attribute: c.name,
        label: c.label,
      }));
  }, [resolved, loadState.entityMetadata]);

  // Lazy load — only kicks off once we have entityName + fetchXml.
  // FetchXML composition order: base savedQuery → parent-context overlay →
  // host-filters overlay → chip augmentation. Each step is a pure string transform;
  // identical inputs ⇒ identical output (lazy-load reset detector relies on
  // referential stability).
  const fetchXml = React.useMemo(() => {
    let xml = loadState.savedQuery?.fetchXml ?? '';
    const parentFilter = resolved?.behavior?.parentContextFilter;
    if (xml && parentFilter) {
      xml = overlayParentContextFilter(xml, parentFilter, parentContext);
    }
    if (xml && hostFilters && hostFilters.length > 0) {
      xml = overlayHostFilters(xml, hostFilters);
    }
    if (xml && chipDescriptors.length > 0) {
      xml = augmentFetchXmlWithChips(xml, chipDescriptors, chipState);
    }
    // eslint-disable-next-line no-console
    console.info('[DataGrid] fetchXml composition', {
      parentContext,
      parentFilter,
      hasParentFilterMatch: Boolean(parentFilter && parentContext?.[parentFilter.parentContextKey]),
      hostFilterCount: hostFilters?.length ?? 0,
      chipStateKeys: Object.keys(chipState),
      fetchXml: xml,
    });
    return xml;
  }, [loadState.savedQuery, resolved?.behavior?.parentContextFilter, parentContext, hostFilters, chipDescriptors, chipState]);
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
      entries => {
        if (entries[0]?.isIntersecting) {
          fetchNextPage();
        }
      },
      { root: scrollContainerRef.current, rootMargin: '200px', threshold: 0 }
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [hasMore, isLoadingRows, fetchNextPage]);

  // ───────────────────────────────────────────────────────────────────────────
  // Host hook: notify when the records page resolves (task 033a — used by the
  // SpaarkeAi Calendar widget to derive per-date event counts for its calendar
  // strip dot indicators). Fires every time `records` identity changes (initial
  // load + each subsequent "load more" page). Intentionally does not gate on
  // `isLoadingRows` — host code typically wants the latest accumulated array
  // even mid-pagination.
  // ───────────────────────────────────────────────────────────────────────────
  React.useEffect(() => {
    if (typeof onRecordsLoaded === 'function') {
      onRecordsLoaded(records);
    }
  }, [records, onRecordsLoaded]);

  // ───────────────────────────────────────────────────────────────────────────
  // Build Fluent v9 table columns + sizing options from ResolvedConfig.columns
  // ───────────────────────────────────────────────────────────────────────────
  type GridItem = Record<string, unknown> & { _rowId: string };

  const visibleColumns: ReadonlyArray<ResolvedColumn> = React.useMemo(
    () => (resolved?.columns ?? []).filter(c => !c.hidden),
    [resolved]
  );

  // Quick lookup: attribute logical name → chip descriptor (for inline column filter).
  const chipDescriptorByAttribute: ReadonlyMap<string, ChipDescriptor> = React.useMemo(() => {
    const map = new Map<string, ChipDescriptor>();
    for (const d of chipDescriptors) map.set(d.attribute, d);
    return map;
  }, [chipDescriptors]);

  /**
   * Default record-open handler: opens the record's form in a new browser tab via
   * `window.open` against the MDA `main.aspx?pagetype=entityrecord&etn=…&id=…` URL.
   * The current modal stays open in the parent tab — matches OOB Power Apps
   * "open in new tab" behavior. Used only when the host did NOT pass `onRecordOpen`.
   */
  const defaultRecordOpen = React.useCallback(
    (recordId: string, _record: Record<string, unknown>, ctx: DataGridHostContext) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window.parent as any)?.Xrm ?? (window as any).Xrm;
      const clientUrl = xrm?.Utility?.getGlobalContext?.().getClientUrl?.();
      if (!clientUrl || !ctx.entityName || !recordId) return;
      const url =
        `${clientUrl}/main.aspx?pagetype=entityrecord` +
        `&etn=${encodeURIComponent(ctx.entityName)}&id=${encodeURIComponent(recordId)}`;
      window.open(url, '_blank', 'noopener,noreferrer');
    },
    []
  );
  const effectiveRecordOpen = onRecordOpen ?? defaultRecordOpen;

  const tableColumns: TableColumnDefinition<GridItem>[] = React.useMemo(() => {
    if (!resolved) return [];
    return visibleColumns.map(col =>
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
        renderHeaderCell: () => (
          <HeaderCellContent
            label={col.label}
            descriptor={chipDescriptorByAttribute.get(col.name)}
            state={chipState}
            onStateChange={setChipState}
            theme={theme}
            // Always passing `onSortChange` so the chevron menu always renders
            // (HeaderCellContent hides the trigger when BOTH descriptor and
            // onSortChange are undefined). The actual sort is still applied
            // by Fluent v9's native `sortable={true}` header click — the menu
            // items just give a discoverable surface for the same action.
            onSortChange={() => {
              // no-op — Fluent v9 native sort is wired via `sortable` on the
              // DataGrid below; future enhancement: pass through to Fluent
              // sort state explicitly so the menu can drive sort programmatically.
            }}
          />
        ),
        renderCell: item => {
          const value = item[col.name];

          if (col.isPrimaryName && resolved) {
            const idValue = String(item[resolved.primaryIdAttribute] ?? item._rowId ?? '');
            const label = renderCellValue(value, 'default') || '(no name)';
            return React.createElement(
              Link,
              {
                as: 'button',
                className: styles.primaryNameLink,
                onClick: () => {
                  effectiveRecordOpen(idValue, item, {
                    configId,
                    entityName: resolved.entityName,
                    parentContext,
                    selectedIds: Array.from(selectedRowIds).map(String),
                  });
                },
              },
              label
            );
          }

          // Host-supplied custom renderer wins over derived renderer.
          const customRenderer = resolved?.overrides.columnRenderers?.[col.name];
          if (customRenderer) {
            return customRenderer(value, item) as React.ReactNode;
          }
          return renderCellValue(value, col.renderer);
        },
      })
    );
  }, [
    resolved,
    visibleColumns,
    effectiveRecordOpen,
    configId,
    parentContext,
    selectedRowIds,
    styles.primaryNameLink,
    chipDescriptorByAttribute,
    chipState,
    theme,
  ]);

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
    []
  );

  const refresh = React.useCallback(() => {
    // Re-fetch from config (in case the underlying record changed) AND reset lazy load.
    setRefreshCounter(n => n + 1);
    resetLazyLoad();
  }, [resetLazyLoad]);

  // Command-bar dispatcher. `useCallback` MUST run unconditionally — declared
  // here BEFORE the loading/error early-returns so hook order stays stable
  // across renders (React error #310 otherwise).
  const handleCommandInvoke = React.useCallback(
    (commandId: string, ids: ReadonlyArray<string>) => {
      onCommandInvoke?.(commandId, Array.from(ids), {
        configId,
        entityName: resolved?.entityName ?? '',
        parentContext,
        selectedIds: Array.from(ids),
      });
    },
    [onCommandInvoke, configId, resolved?.entityName, parentContext]
  );

  // Selection ids as strings — passed into context for extensions.
  const selectedIds = React.useMemo(() => new Set<string>(Array.from(selectedRowIds).map(String)), [selectedRowIds]);

  // Density derives from configjson; consumer may toggle via a later command-bar primitive.
  const density: 'medium' | 'small' = resolved?.display.densityDefault === 'compact' ? 'small' : 'medium';

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
          <Text size={300}>{loadState.error?.message ?? 'DataGrid configuration could not be resolved.'}</Text>
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
  const selectedCount = selectedRowIds.size;
  const totalCount = records.length;
  const visibleTitle = resolved.display.title ?? loadState.savedQuery?.name ?? '';

  // ViewSelector data — sibling savedqueries surfaced as `SavedView[]`. Falls
  // back to a single-entry list (the active view) when sibling fetch failed.
  const selectorViews: ReadonlyArray<SavedView> =
    loadState.availableViews.length > 0
      ? loadState.availableViews.map(v => ({ id: v.id, name: v.name, isDefault: v.isDefault }))
      : loadState.savedQuery
        ? [{ id: '__active__', name: loadState.savedQuery.name || visibleTitle || 'View' }]
        : [];
  const currentViewId =
    activeSavedQueryId ??
    (loadState.savedQuery
      ? // Match the active savedQuery against availableViews by name; the savedQuery
        // result doesn't carry its own id field.
        (loadState.availableViews.find(v => v.name === loadState.savedQuery?.name)?.id ?? '__active__')
      : '');

  return (
    <DataGridContextProvider value={contextValue}>
      <div className={mergeClasses(styles.root, className)} aria-label={contextValue.currentView}>
        <div className={styles.headerCard}>
          <div className={styles.headerLeft}>
            {selectorViews.length > 0 ? (
              <ViewSelector
                views={selectorViews}
                activeViewId={currentViewId}
                onViewChange={setActiveSavedQueryId}
                onBack={onBack}
                theme={theme}
              />
            ) : (
              <span aria-hidden="true" />
            )}
          </div>
          <DataGridCommandBar
            config={resolved.commandBar}
            entityName={resolved.entityName}
            selectedIds={Array.from(selectedRowIds).map(String)}
            records={records}
            columns={visibleColumns}
            currentView={contextValue.currentView}
            refresh={refresh}
            theme={theme}
            // OOB pattern: top 3 actions (Refresh, Delete, View Switcher caret)
            // stay inline; the rest live in the `⋯` overflow menu. The header
            // card has `overflow: hidden` to clip safely if the dialog is too
            // narrow to fit all 3.
            inlineLimit={3}
            onCommandInvoke={handleCommandInvoke}
          />
        </div>

        <div className={styles.innerCard}>
          {/*
           * The horizontal FilterChipBar primitive is INTENTIONALLY not mounted
           * here. Filter UI lives inside the column-header chevron menu
           * (`HeaderCellContent` → `Filter by` → popover) — the Power Apps OOB
           * pattern. The FilterChipBar primitive remains exported from
           * `@spaarke/ui-components` for hosts that want a strip-style filter
           * row instead.
           */}

          <div className={styles.gridScroll} ref={scrollContainerRef}>
            {/*
             * The FluentDataGrid is ALWAYS rendered (even when there are zero
             * rows) so the column-header row — which hosts the chevron menus
             * for Sort / Filter by / Column width — stays visible. If we
             * unmounted it on the empty path, a filter that yielded 0 rows
             * would leave the user with no way back to clear the filter.
             * Power Apps OOB pattern: header always visible, body empty,
             * empty-state message sits below the (empty) body.
             */}
            <FluentDataGrid
              items={items}
              columns={tableColumns}
              selectionMode={
                resolved.behavior.selectionMode === 'none'
                  ? undefined
                  : resolved.behavior.selectionMode === 'single'
                    ? 'single'
                    : 'multiselect'
              }
              selectedItems={selectedRowIds}
              onSelectionChange={handleSelectionChange}
              sortable={resolved.behavior.enableSorting}
              resizableColumns={resolved.behavior.enableColumnResize}
              columnSizingOptions={columnSizingOptions}
              focusMode="composite"
              size={density}
              getRowId={(item: GridItem) => item._rowId}
              style={{ width: '100%' }}
              aria-label={contextValue.currentView}
            >
              <DataGridHeader>
                <DataGridRow selectionCell={{ checkboxIndicator: { 'aria-label': 'Select all rows' } }}>
                  {({ renderHeaderCell }) => (
                    <DataGridHeaderCell className={styles.headerCell}>{renderHeaderCell()}</DataGridHeaderCell>
                  )}
                </DataGridRow>
              </DataGridHeader>
              <DataGridBody<GridItem>>
                {({ item, rowId }) => (
                  <DataGridRow<GridItem>
                    key={rowId}
                    selectionCell={{ checkboxIndicator: { 'aria-label': 'Select row' } }}
                  >
                    {({ renderCell }) => <DataGridCell className={styles.cell}>{renderCell(item)}</DataGridCell>}
                  </DataGridRow>
                )}
              </DataGridBody>
            </FluentDataGrid>

            {isEmpty && (
              <div className={styles.emptyState}>
                <Text size={400} weight="semibold">
                  {resolved.display.emptyStateMessage ?? 'No records to display'}
                </Text>
              </div>
            )}

            {isLoadingRows && (
              <div className={styles.loadMoreContainer}>
                <Spinner size="small" label="Loading more..." />
              </div>
            )}

            {/* Infinite-scroll sentinel — IntersectionObserver wired above in useEffect. */}
            <div ref={sentinelRef} className={styles.sentinel} aria-hidden="true" />
          </div>

          <div className={styles.footer} role="status" aria-live="polite">
            <Text size={200}>
              Rows: {totalCount}
              {hasMore ? '+' : ''}
              {selectedCount > 0 ? ` • ${selectedCount} selected` : ''}
            </Text>
          </div>
        </div>
      </div>
    </DataGridContextProvider>
  );
};

export default DataGrid;
