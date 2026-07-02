/**
 * DataGridConfiguration — the v1.0 schema for `sprk_gridconfiguration.sprk_configjson`,
 * consumed by the new `<DataGrid configId={...} />` component.
 *
 * Discriminator: `_version: '1.0'`.
 *
 * **Not to be confused with** `IGridConfigJson` in `types/ConfigurationTypes.ts` —
 * that is the LEGACY schema used by the pre-R1 `DatasetGrid` components, the
 * `UniversalDatasetGrid` PCF, and the current SemanticSearch code page. Both schemas
 * coexist during phases A–E. The legacy schema retires when its last consumer
 * migrates (Phase E migrates SemanticSearch; Phase F retires DatasetGrid + UDG PCF).
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6.3
 * **Naming deviation from spec**: spec FR-DG-03 named the file `GridConfigJson.ts` and
 * the type `GridConfigJson_v1_0`. Renamed during task 001 for semantic clarity and to
 * avoid grep / refactor collision with the existing `IGridConfigJson`. See
 * `projects/spaarke-datagrid-framework-r1/notes/drafts/001-deviations.md`.
 *
 * @see IDataverseClient — the Dataverse contract that resolves this configuration
 * @see IGridConfigJson — legacy schema (pre-R1); still in use through Phase E
 */

// ─────────────────────────────────────────────────────────────────────────────
// Source — where the grid gets its FetchXML + layoutXml
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Reference one specific savedquery by GUID. Common case for drill-throughs and
 * fixed views.
 */
export interface SourceSavedQuery {
  type: 'savedquery';
  savedQueryId: string;
}

/**
 * Provide FetchXML + layoutXml inline. Used when the configuration owns the query
 * (no savedquery record) — e.g. SemanticSearch post-migration (Phase E).
 */
export interface SourceInline {
  type: 'inline';
  fetchXml: string;
  layoutXml: string;
}

/**
 * Auto-discover the active savedqueries for an entity at render time
 * (via `IDataverseClient.retrieveSavedQueriesForEntity`). Eliminates config
 * drift when a Dataverse admin adds a view.
 */
export interface SourceSavedQuerySet {
  type: 'savedquery-set';
  entityLogicalName: string;
}

export type SourceConfig = SourceSavedQuery | SourceInline | SourceSavedQuerySet;

// ─────────────────────────────────────────────────────────────────────────────
// Display — visual chrome overrides
// ─────────────────────────────────────────────────────────────────────────────

export interface DisplayConfig {
  /** Override the savedquery name in the grid header. */
  title?: string;
  /** Fluent v9 icon name (e.g. `'Calendar20Regular'`). */
  icon?: string;
  /** Default density — Fluent v9 `<DataGrid size>` value. */
  densityDefault?: 'comfortable' | 'compact';
  /** Custom empty-state message (default: framework's localized fallback). */
  emptyStateMessage?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Filter chips — auto-derived from metadata unless overridden
// ─────────────────────────────────────────────────────────────────────────────

export type FilterChipKind = 'optionset-multi' | 'lookup-multi' | 'date-range' | 'text' | 'bool';

export type BadgeAppearance = 'filled' | 'outline' | 'tint';

/**
 * Explicit filter chip definition. Used when `filterChips.mode = 'explicit'`.
 */
export interface ExplicitFilterChip {
  /** Logical name of the attribute the chip filters on. */
  field: string;
  kind: FilterChipKind;
  /** Override the auto-derived display label. */
  label?: string;
  /**
   * Override the auto-derived value source.
   * - `systemusers` for owner / assigned-to-style lookups
   * - `entity` for arbitrary lookup target overrides
   */
  valueSource?: { type: 'systemusers' } | { type: 'entity'; entity: string; nameField: string };
  /** Optional per-option badge appearance overrides keyed by option value. */
  valueColors?: Record<number, BadgeAppearance>;
}

export interface FilterChipsConfig {
  /**
   * - `auto`: every layoutXml column whose `AttributeType` matches the filter chip
   *   set (`OptionSet | Status | State | Lookup | DateTime | Boolean`) becomes a chip.
   * - `allowlist`: only the listed logical names become chips.
   * - `denylist`: every chip-eligible column EXCEPT the listed names.
   * - `explicit`: only the chips in `explicit[]`, in declaration order.
   */
  mode: 'auto' | 'allowlist' | 'denylist' | 'explicit';
  allowlist?: string[];
  denylist?: string[];
  explicit?: ExplicitFilterChip[];
  /** Default `true`. Shows the "Clear all" chip to the right of the strip. */
  showClearAll?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Command bar — built-in actions + custom registry
// ─────────────────────────────────────────────────────────────────────────────

export type CommandBarAction =
  | 'create-form'
  | 'delete-selected'
  | 'refresh'
  | 'export-excel'
  | 'edit-columns'
  | 'edit-filters'
  | 'custom';

export interface CommandBarItem {
  id: string;
  label: string;
  /** Fluent v9 icon name. */
  icon: string;
  action: CommandBarAction;
  /** Required when `action === 'custom'` — resolves against `registerCommandHandler`. */
  customHandlerId?: string;
  /** Selection requirement for the button to be enabled. */
  requiresSelection?: 'single' | 'multi' | false;
  /** Optional security gate — checked via `PrivilegeService`. */
  privilege?: 'Read' | 'Write' | 'Create' | 'Delete';
  /** Fluent v9 `Button` appearance. */
  appearance?: 'subtle' | 'primary' | 'secondary';
  /** Render a vertical divider before this item. */
  divider?: boolean;
}

export interface CommandBarConfig {
  /** Left-aligned, always-visible items. */
  primary?: CommandBarItem[];
  /** Right-aligned / overflow-menu items. */
  secondary?: CommandBarItem[];
  /**
   * Toggle framework default commands per slot. Omitted = framework default
   * (typically `true` when the entity permits the action).
   */
  showDefaultCommands?: {
    newRecord?: boolean;
    delete?: boolean;
    refresh?: boolean;
    exportExcel?: boolean;
    editColumns?: boolean;
    editFilters?: boolean;
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Row open — what happens when the user clicks the primary-name link
// ─────────────────────────────────────────────────────────────────────────────

export type RowOpenType = 'sidePane' | 'wizard' | 'navigateToForm' | 'formDialog' | 'dialog' | 'webResource' | 'custom';

/**
 * `rowOpen` is intentionally a flat shape (not a discriminated union per type)
 * because the configjson is hand-authored by humans in Dataverse — keeping the
 * schema flat reduces editing friction. The framework's resolver inspects only the
 * fields relevant to the chosen `type`.
 *
 * **Default handlers** are defined in design.md §7.2.
 *
 * **`type = "webResource"` is the bug-fix path** for the EventsPage record-link-
 * not-opening issue in dialog mode (uses `Xrm.Navigation.navigateTo({pageType:"webresource"...})`).
 */
export interface RowOpenConfig {
  type: RowOpenType;

  // type = 'sidePane'
  paneId?: string;
  paneTitle?: string;
  webResourceName?: string;
  /** Side pane width in pixels. */
  width?: number;

  // type = 'wizard'
  /** Wizard component registered via `registerWizard`. */
  wizardName?: string;

  // type = 'dialog'
  /** Dialog component registered via `registerDialog`. */
  dialogComponent?: string;

  // type = 'formDialog' — Dataverse-native centered modal dialog of the
  // entity record form (Xrm.Navigation.navigateTo with target=2).
  //
  // R2 note (ai-spaarke-ai-workspace-UI-r2, FR-20): the framework now emits
  // a single Layout 1 modal size (85% × 85%, position 1) for every row-open
  // regardless of `rowOpen.type`. The per-record `formDialogWidthPercent` and
  // `formDialogHeightPercent` overrides are retained in the schema for
  // backward-compatible deserialization but are IGNORED by `defaultRecordOpen`.
  /** @deprecated R2 FR-20: ignored by `defaultRecordOpen`; retained for schema compatibility. */
  formDialogWidthPercent?: number;
  /** @deprecated R2 FR-20: ignored by `defaultRecordOpen`; retained for schema compatibility. */
  formDialogHeightPercent?: number;

  /**
   * R2 FR-01: optional form GUID passed as `pageInput.formId` on the
   * `Xrm.Navigation.navigateTo` call. When set, opens the specified form
   * variant; when absent, opens the user's default main form for the entity.
   */
  formId?: string;

  // type = 'webResource'
  webResource?: string;
  /** Field names from the row + parentContext to forward as URL params. */
  dataParams?: string[];

  // type = 'custom'
  customHandlerId?: string;

  /** Keys from `parentContext` to propagate to the opened surface. */
  passContext?: string[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Secondary actions — per-row + bulk action affordances
// ─────────────────────────────────────────────────────────────────────────────

export type SecondaryActionKind = 'ai-assistant' | 'playbook' | 'wizard' | 'navigate' | 'custom';

export interface SecondaryAction {
  id: string;
  label: string;
  /** Fluent v9 icon name. */
  icon: string;
  kind: SecondaryActionKind;
  requiresSelection?: 'single' | 'multi' | false;
  /** Optional security gate. */
  privilege?: 'Read' | 'Write' | 'Create' | 'Delete' | 'Append' | 'AppendTo';
  /** When to surface the action in the UI. */
  visible?: 'always' | 'row-hover' | 'bulk-only';

  // kind-specific configuration
  aiAssistantId?: string;
  playbookId?: string;
  wizardName?: string;
  navigateTarget?: { entity: string; idField: string };
  customHandlerId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Columns — per-column overrides keyed by logical name
// ─────────────────────────────────────────────────────────────────────────────

export type ColumnRendererKind =
  | 'default'
  | 'currency'
  | 'percentage'
  | 'badge'
  | 'link'
  | 'date'
  | 'datetime'
  | 'avatar'
  | 'icon'
  | string; // open-ended for host-registered custom renderers

export interface ColumnOverride {
  /** Override the auto-derived display label. */
  label?: string;
  /** Pixel width override. */
  width?: number;
  renderer?: ColumnRendererKind;
  align?: 'left' | 'center' | 'right';
  tooltip?: string;
  hidden?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Parent-context filter overlay (task 020 D-020-02 follow-up)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Declares how the framework should overlay a parent-context filter onto the
 * resolved FetchXML before fetching records. Used by drill-through Custom Pages
 * that scope a grid to a parent record (e.g. Matter Health → KPIs for the
 * current Matter).
 *
 * Why this exists: Dataverse server-side validation REJECTS placeholder syntax
 * like `value='@MatterId'` in stored savedquery FetchXML — every condition
 * value is parsed as a typed literal at save time. The standard Power Platform
 * pattern is to store the BASE query (no parent filter) and have the consumer
 * inject the parent filter at runtime. This config + the framework's
 * `overlayParentContextFilter` helper implement that pattern.
 *
 * **Example**: for the Matter Health drill-through, the consuming
 * Custom Page mounts `<DataGrid configId="..." parentContext={{ matterId: "..." }}/>`,
 * and the configjson includes:
 * ```json
 * "behavior": {
 *   "parentContextFilter": { "attribute": "sprk_matter", "parentContextKey": "matterId" }
 * }
 * ```
 * The framework injects `<filter type='and'><condition attribute='sprk_matter' operator='eq' value='{matterId}'/></filter>` into the savedquery's FetchXML before calling `dataverseClient.retrieveMultipleRecords()`.
 */
export interface ParentContextFilter {
  /** FetchXML attribute logical name to filter on (e.g. `'sprk_matter'`). */
  attribute: string;
  /** Key in `parentContext` to read the filter value from (e.g. `'matterId'`). */
  parentContextKey: string;
  /** Filter operator. Default `'eq'`. */
  operator?: 'eq' | 'neq' | 'in' | 'eq-userid' | 'eq-userteams';
}

// ─────────────────────────────────────────────────────────────────────────────
// Behavior — grid-level interaction knobs
// ─────────────────────────────────────────────────────────────────────────────

export interface BehaviorConfig {
  /** Default `'multi'`. */
  selectionMode?: 'none' | 'single' | 'multi';
  /** Default `50` per design.md (callers may override; framework also accepts 100 default for lazy-load contexts). */
  pageSize?: number;
  /** Default `true`. */
  enableSorting?: boolean;
  /** Default `true`. */
  enableColumnResize?: boolean;
  /** Default `true`. */
  enableKeyboardNavigation?: boolean;
  /**
   * Parent-context filter overlay. When set AND `parentContext[parentContextKey]`
   * is present, the framework injects a `<condition>` into the savedquery's
   * top-level `<filter type='and'>` before calling `dataverseClient.retrieveMultipleRecords()`.
   * See {@link ParentContextFilter} for details + rationale.
   */
  parentContextFilter?: ParentContextFilter;
}

// ─────────────────────────────────────────────────────────────────────────────
// Top-level configuration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Top-level v1.0 configuration consumed by `<DataGrid configId={...} />`.
 *
 * Authored as the body of `sprk_gridconfiguration.sprk_configjson` and parsed at
 * render time. Invalid configurations DO NOT throw — see {@link isValidDataGridConfiguration}.
 */
export interface DataGridConfiguration {
  readonly _version: '1.0';
  readonly source: SourceConfig;
  readonly display?: DisplayConfig;
  readonly filterChips?: FilterChipsConfig;
  readonly commandBar?: CommandBarConfig;
  readonly rowOpen?: RowOpenConfig;
  readonly secondaryActions?: ReadonlyArray<SecondaryAction>;
  readonly columns?: Readonly<Record<string, ColumnOverride>>;
  readonly behavior?: BehaviorConfig;
}

// ─────────────────────────────────────────────────────────────────────────────
// Runtime validation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Runtime guard for {@link DataGridConfiguration} v1.0.
 *
 * **Does NOT throw** on invalid input — returns `false`. Callers (the DataGrid
 * core in task 003) react by logging via `console.warn` and falling back to
 * framework defaults derived from entity metadata + layoutXml. This satisfies
 * spec FR-DG-03 acceptance: "invalid JSON rejected with a non-throwing fallback."
 *
 * The guard validates only the discriminators (`_version` and `source.type`) —
 * intentionally shallow. Deep shape errors surface as undefined property reads
 * downstream and degrade gracefully into default behavior.
 *
 * @param value - Anything (typically `JSON.parse(sprk_configjson)`).
 * @returns `true` if `value` matches the v1.0 discriminators, `false` otherwise.
 */
export function isValidDataGridConfiguration(value: unknown): value is DataGridConfiguration {
  if (value === null || typeof value !== 'object') {
    return false;
  }
  const obj = value as Record<string, unknown>;
  if (obj._version !== '1.0') {
    return false;
  }
  if (obj.source === null || typeof obj.source !== 'object') {
    return false;
  }
  const source = obj.source as Record<string, unknown>;
  if (source.type !== 'savedquery' && source.type !== 'inline' && source.type !== 'savedquery-set') {
    return false;
  }
  return true;
}
