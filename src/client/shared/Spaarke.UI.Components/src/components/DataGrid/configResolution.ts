/**
 * configResolution — pure three-tier config resolution for the DataGrid framework.
 *
 * Implements design.md §6.4: resolves the effective grid configuration by composing,
 * in priority order:
 *   1. `overrides` prop on `<DataGrid />` (highest)
 *   2. `sprk_gridconfiguration.sprk_configjson` record body (parsed)
 *   3. Entity metadata defaults (`primaryNameAttribute`, attribute types)
 *   4. layoutXml column declarations (column ids + widths)
 *   5. Framework defaults (lowest)
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6.4
 * **FR**: FR-DG-04 (three-tier resolution)
 *
 * This module is INTENTIONALLY pure — no React, no I/O, no Dataverse. It is the
 * single point in the framework that decides "what should the grid look like" and
 * is therefore the easiest unit to exhaustively test.
 *
 * A `configId` that points to a non-existent record manifests as `configRecord =
 * null` here, and resolution falls through gracefully to entity-metadata defaults.
 */

import type {
  DataGridConfiguration,
  BehaviorConfig,
  ColumnOverride,
  DisplayConfig,
  FilterChipsConfig,
  CommandBarConfig,
  RowOpenConfig,
  SecondaryAction,
} from '../../types/DataGridConfiguration';
import type { EntityMetadata } from '../../services/IDataverseClient';

// ─────────────────────────────────────────────────────────────────────────────
// Overrides shape (from props.overrides on <DataGrid />)
// ─────────────────────────────────────────────────────────────────────────────

/** Per-host explicit overrides — the highest-priority resolution tier. */
export interface DataGridOverrides {
  /** Custom cell renderers keyed by field logical name. */
  columnRenderers?: {
    [fieldName: string]: (value: unknown, record: Record<string, unknown>) => unknown;
  };
  /** Badge appearance overrides keyed by option-set value. */
  statusBadgeMap?: { [optionValue: number]: 'filled' | 'outline' | 'tint' };
  /** Restrict filter chips to this subset of columns. */
  filterChipsAllowlist?: string[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Resolved column descriptor — what the DataGrid renderer actually consumes
// ─────────────────────────────────────────────────────────────────────────────

/** Effective per-column descriptor after three-tier resolution. */
export interface ResolvedColumn {
  /** Logical name (column id). */
  name: string;
  /** Display label — from configjson override, then metadata fallback, then logical name. */
  label: string;
  /** Pixel width — from configjson override, then layoutXml, then framework default 150. */
  width: number;
  /** Renderer kind — from configjson override, then derived from attribute type. */
  renderer: string;
  /** Horizontal alignment. */
  align: 'left' | 'center' | 'right';
  /** Whether this column is hidden (true ⇒ excluded from render). */
  hidden: boolean;
  /** Whether this column hosts the primary-name "open record" link. */
  isPrimaryName: boolean;
  /** Optional tooltip on the header cell. */
  tooltip?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// ResolvedConfig — the output shape consumed by <DataGrid />
// ─────────────────────────────────────────────────────────────────────────────

/** The fully-resolved grid configuration after merging all four tiers. */
export interface ResolvedConfig {
  /** Effective entity logical name (from savedquery, configjson.source, or metadata). */
  entityName: string;
  /** Display configuration (title, icon, density, empty state). */
  display: Required<Pick<DisplayConfig, 'densityDefault'>> & DisplayConfig;
  /** Effective behavior knobs — every field non-optional after merge. */
  behavior: Required<BehaviorConfig>;
  /** Filter chips config — `mode = 'auto'` if not configured. */
  filterChips: FilterChipsConfig;
  /** Command bar config (may be empty). */
  commandBar: CommandBarConfig;
  /** Row open behavior — undefined ⇒ host caller decides. */
  rowOpen?: RowOpenConfig;
  /** Secondary actions list — never undefined (empty array if none). */
  secondaryActions: ReadonlyArray<SecondaryAction>;
  /** Ordered, fully-resolved columns to render. */
  columns: ReadonlyArray<ResolvedColumn>;
  /** Pass-through of the original overrides for renderers + filter-chip allowlist. */
  overrides: DataGridOverrides;
  /** Primary id attribute (used to derive `getRowId`). */
  primaryIdAttribute: string;
  /** Primary name attribute (used to wire the open-record link). */
  primaryNameAttribute: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Framework defaults
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Default behavior. `parentContextFilter` is intentionally kept optional in the
 * resolved shape — it's a per-grid configuration (consumed when present, omitted
 * when absent). Other fields have sensible framework defaults.
 */
type ResolvedBehavior = Required<Omit<BehaviorConfig, 'parentContextFilter'>>
  & Pick<BehaviorConfig, 'parentContextFilter'>;

const FRAMEWORK_DEFAULT_BEHAVIOR: ResolvedBehavior = {
  selectionMode: 'multi',
  // Lazy-load contexts default to 100 per FR-DG-12 ("page size default 100 ...
  // override via configjson.behavior.pageSize"). Non-lazy users may still override.
  pageSize: 100,
  enableSorting: true,
  enableColumnResize: true,
  enableKeyboardNavigation: true,
  // parentContextFilter: undefined by default (caller's configjson opts in)
};

const FRAMEWORK_DEFAULT_DENSITY: 'comfortable' | 'compact' = 'comfortable';

/** Default column width when layoutXml omits a `width` attr. */
const FRAMEWORK_DEFAULT_COLUMN_WIDTH = 150;

// ─────────────────────────────────────────────────────────────────────────────
// layoutXml parsing — extracts ordered column ids + widths
// ─────────────────────────────────────────────────────────────────────────────

interface LayoutColumn {
  name: string;
  width: number;
  isFirstCell: boolean;
  /** Optional `<row id="...">` attribute on the parent row. */
  rowId?: string;
}

/**
 * Parse a layoutXml string into an ordered list of column descriptors.
 *
 * Tolerant of malformed input — returns `[]` on any parse error.
 *
 * Recognises `<grid><row id="..."><cell name="..." width="..." isfirstcell="..." />`
 * structure. The `row[@id]` attribute, when present, is captured because the
 * framework's `getRowId` derivation can use it (Phase A/B detail — see
 * `useLazyLoad` consumer code).
 */
export function parseLayoutColumns(
  layoutXml: string | undefined,
): { columns: LayoutColumn[]; rowIdAttribute: string | undefined } {
  if (!layoutXml || typeof layoutXml !== 'string') {
    return { columns: [], rowIdAttribute: undefined };
  }
  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(layoutXml, 'text/xml');
    if (doc.querySelector('parsererror')) {
      return { columns: [], rowIdAttribute: undefined };
    }
    const row = doc.querySelector('row');
    const rowIdAttribute = row?.getAttribute('id') ?? undefined;
    const cells = doc.querySelectorAll('cell');
    const columns: LayoutColumn[] = [];
    cells.forEach((cell, index) => {
      const name = cell.getAttribute('name');
      if (!name) return;
      const widthRaw = cell.getAttribute('width');
      const widthParsed = widthRaw ? Number.parseInt(widthRaw, 10) : NaN;
      columns.push({
        name,
        width: Number.isFinite(widthParsed) && widthParsed > 0
          ? widthParsed
          : FRAMEWORK_DEFAULT_COLUMN_WIDTH,
        isFirstCell: cell.getAttribute('isfirstcell') === 'true' || index === 0,
        rowId: rowIdAttribute,
      });
    });
    return { columns, rowIdAttribute };
  } catch {
    return { columns: [], rowIdAttribute: undefined };
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Renderer derivation from attribute type — used when configjson does not override
// ─────────────────────────────────────────────────────────────────────────────

function rendererFromAttributeType(
  attributeType: string | undefined,
  format: string | undefined,
): string {
  if (!attributeType) return 'default';
  switch (attributeType) {
    case 'Money':
      return 'currency';
    case 'Decimal':
      return format === 'Percentage' ? 'percentage' : 'default';
    case 'DateTime':
      return format === 'DateOnly' ? 'date' : 'datetime';
    case 'Picklist':
    case 'Status':
    case 'State':
    case 'Boolean':
      return 'badge';
    case 'Lookup':
      return 'link';
    default:
      return 'default';
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Resolution — the binding three-tier merge
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Resolve the effective grid configuration from up to four input tiers.
 *
 * **Pure function — no side effects, no React, no I/O.** Easy to unit test.
 *
 * Inputs:
 * @param overrides    Caller-provided overrides (highest priority). Pass `{}` for none.
 * @param configRecord Parsed contents of `sprk_gridconfiguration.sprk_configjson`,
 *                     or `null` when the config record is missing / failed to parse.
 *                     **A missing record is NOT an error** — the resolver simply
 *                     falls through to lower tiers, satisfying FR-DG-04 ("a configId
 *                     pointing to a non-existent record MUST still render").
 * @param entityMetadata Projected entity metadata. **Required** — the resolver
 *                       cannot compute a column list or primary-name link without it.
 * @param layoutXml    Raw layoutXml string from the savedquery (or `configRecord.source.inline.layoutXml`).
 *                     May be absent if `configRecord.source.type !== 'savedquery'` and
 *                     no inline layout is provided — in that case columns are derived
 *                     from entity-metadata attributes (best-effort fallback).
 * @param entityNameFromSavedQuery The entity name returned by `retrieveSavedQuery`.
 *                     Falls back to `configRecord.source.entityLogicalName` for
 *                     `savedquery-set`, or `entityMetadata` primary id-derived guess.
 */
export function resolveConfig(
  overrides: DataGridOverrides | undefined,
  configRecord: DataGridConfiguration | null,
  entityMetadata: EntityMetadata,
  layoutXml: string | undefined,
  entityNameFromSavedQuery?: string,
): ResolvedConfig {
  const safeOverrides: DataGridOverrides = overrides ?? {};

  // Tier 1: entity name
  const entityName =
    entityNameFromSavedQuery ??
    (configRecord?.source?.type === 'savedquery-set'
      ? configRecord.source.entityLogicalName
      : undefined) ??
    // last-ditch: derive from primaryIdAttribute (e.g., `sprk_eventid` ⇒ `sprk_event`)
    entityMetadata.primaryIdAttribute.replace(/id$/i, '');

  // Tier 2: behavior — framework defaults < configjson < (overrides currently unused for behavior)
  const behavior: Required<BehaviorConfig> = {
    ...FRAMEWORK_DEFAULT_BEHAVIOR,
    ...(configRecord?.behavior ?? {}),
  } as Required<BehaviorConfig>;

  // Tier 3: display
  const display: Required<Pick<DisplayConfig, 'densityDefault'>> & DisplayConfig = {
    densityDefault: configRecord?.display?.densityDefault ?? FRAMEWORK_DEFAULT_DENSITY,
    ...(configRecord?.display ?? {}),
  };

  // Tier 4: filter chips
  const filterChips: FilterChipsConfig =
    configRecord?.filterChips ?? { mode: 'auto' };

  // Tier 5: command bar (just pass through; framework defaults filled by command-bar primitive)
  const commandBar: CommandBarConfig = configRecord?.commandBar ?? {};

  // Tier 6: rowOpen + secondaryActions
  const rowOpen = configRecord?.rowOpen;
  const secondaryActions = configRecord?.secondaryActions ?? [];

  // Tier 7: columns — merge layoutXml × metadata × configjson per-column overrides
  const layout = parseLayoutColumns(layoutXml);
  const configColumnOverrides: Record<string, ColumnOverride> = configRecord?.columns ?? {};

  const columnsFromLayout = layout.columns.map((layoutCol) =>
    buildResolvedColumn(layoutCol, entityMetadata, configColumnOverrides[layoutCol.name]),
  );

  // Fallback: if layoutXml is empty (e.g., savedquery-set with no loaded savedquery yet,
  // OR a missing savedquery record), synthesize columns from metadata attributes —
  // primary name first, then up to 9 other non-system attributes.
  const columns =
    columnsFromLayout.length > 0
      ? columnsFromLayout
      : synthesizeColumnsFromMetadata(entityMetadata, configColumnOverrides);

  return {
    entityName,
    display,
    behavior,
    filterChips,
    commandBar,
    rowOpen,
    secondaryActions,
    columns,
    overrides: safeOverrides,
    primaryIdAttribute: entityMetadata.primaryIdAttribute,
    primaryNameAttribute: entityMetadata.primaryNameAttribute,
  };
}

function buildResolvedColumn(
  layoutCol: LayoutColumn,
  entityMetadata: EntityMetadata,
  override: ColumnOverride | undefined,
): ResolvedColumn {
  const attrMeta = entityMetadata.attributes[layoutCol.name];
  const isPrimaryName =
    layoutCol.name === entityMetadata.primaryNameAttribute ||
    attrMeta?.isPrimaryName === true ||
    layoutCol.isFirstCell;

  const derivedRenderer = isPrimaryName
    ? 'link'
    : rendererFromAttributeType(attrMeta?.attributeType, attrMeta?.format);

  return {
    name: layoutCol.name,
    label: override?.label ?? humanizeLogicalName(layoutCol.name),
    width: override?.width ?? layoutCol.width,
    renderer: override?.renderer ?? derivedRenderer,
    align: override?.align ?? defaultAlignFor(attrMeta?.attributeType),
    hidden: override?.hidden === true,
    isPrimaryName,
    tooltip: override?.tooltip,
  };
}

function synthesizeColumnsFromMetadata(
  entityMetadata: EntityMetadata,
  configColumnOverrides: Record<string, ColumnOverride>,
): ResolvedColumn[] {
  const result: ResolvedColumn[] = [];
  const seen = new Set<string>();

  // Primary name first
  const primaryName = entityMetadata.primaryNameAttribute;
  if (primaryName && entityMetadata.attributes[primaryName]) {
    result.push(
      buildResolvedColumn(
        {
          name: primaryName,
          width: FRAMEWORK_DEFAULT_COLUMN_WIDTH,
          isFirstCell: true,
        },
        entityMetadata,
        configColumnOverrides[primaryName],
      ),
    );
    seen.add(primaryName);
  }

  // Up to 9 other attributes — skip primary id and audit attributes
  const skipSet = new Set<string>([
    entityMetadata.primaryIdAttribute,
    'createdon',
    'modifiedon',
    'createdby',
    'modifiedby',
    'ownerid',
    'statecode',
    'statuscode',
    'versionnumber',
  ]);
  for (const [attrName] of Object.entries(entityMetadata.attributes)) {
    if (result.length >= 10) break;
    if (seen.has(attrName)) continue;
    if (skipSet.has(attrName)) continue;
    result.push(
      buildResolvedColumn(
        {
          name: attrName,
          width: FRAMEWORK_DEFAULT_COLUMN_WIDTH,
          isFirstCell: result.length === 0,
        },
        entityMetadata,
        configColumnOverrides[attrName],
      ),
    );
    seen.add(attrName);
  }

  return result;
}

function defaultAlignFor(
  attributeType: string | undefined,
): 'left' | 'center' | 'right' {
  if (attributeType === 'Money' || attributeType === 'Decimal' || attributeType === 'Integer') {
    return 'right';
  }
  return 'left';
}

function humanizeLogicalName(logicalName: string): string {
  // 'sprk_eventname' → 'Event Name'; 'name' → 'Name'.
  // Best-effort fallback when metadata DisplayName not available in this projection.
  const stripped = logicalName.replace(/^[a-z]+_/, '');
  return stripped
    .replace(/([A-Z])/g, ' $1')
    .replace(/_/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .split(' ')
    .map((w) => (w.length > 0 ? w[0].toUpperCase() + w.slice(1) : w))
    .join(' ');
}
