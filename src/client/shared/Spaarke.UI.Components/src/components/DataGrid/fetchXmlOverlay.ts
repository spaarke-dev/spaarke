/**
 * fetchXmlOverlay — runtime FetchXML mutation helpers for the DataGrid framework.
 *
 * Exports:
 * - {@link overlayParentContextFilter} — injects a single declarative parent-context
 *   condition described by configjson `behavior.parentContextFilter`.
 * - {@link overlayHostFilters} — injects a host-supplied array of {@link HostFilterCondition}
 *   passed imperatively via `<DataGrid hostFilters={...} />`. Task 033a (FR-MIG-05
 *   expansion). Permanent third composition layer per user direction
 *   ("standard feature of the dataset grid").
 *
 * Why these are needed: Dataverse server-side validation REJECTS placeholder syntax
 * like `value='@MatterId'` in stored savedquery FetchXML (every condition value is
 * parsed as a typed literal at save time). The standard Power Platform pattern is to
 * store the BASE query and overlay runtime conditions at runtime.
 *
 * **Composition order** (applied in `DataGrid.tsx`):
 * ```
 *   base (savedquery / inline)
 *   → overlayParentContextFilter    (configjson-driven, declarative)
 *   → overlayHostFilters            (host-prop-driven, imperative)
 *   → augmentFetchXmlWithChips      (user-driven, from filter chip strip)
 * ```
 *
 * Triggered by task 020 (Matter-context savedqueries) deviation D-020-02 and task 033a
 * (Calendar widget filter row → DataGrid extension). See
 * `projects/spaarke-datagrid-framework-r1/notes/drafts/020-deviations.md` and
 * `projects/spaarke-datagrid-framework-r1/notes/drafts/033a-deviations.md`.
 *
 * @see ParentContextFilter — schema in `types/DataGridConfiguration.ts`
 * @see HostFilterCondition — defined below; used by `DataGridProps.hostFilters`
 * @see DataGrid.tsx — calls these helpers before passing fetchXml to useLazyLoad
 */
import type { ParentContextFilter } from '../../types/DataGridConfiguration';

/**
 * Parent-context value bag passed via the DataGrid's `parentContext` prop.
 * Open-ended record (keys are project-specific; e.g. `matterId`, `projectId`).
 */
export type DataGridParentContextLike = Record<string, unknown> | undefined;

/**
 * FetchXML operators supported by {@link HostFilterCondition}. Intentionally a
 * curated subset — the calendar widget filter row + the common drill-through
 * patterns are covered, and anything richer can graduate as a follow-up.
 *
 * Mirrors the standard FetchXML `<condition operator="..."/>` vocabulary:
 * https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml-schema
 */
export type HostFilterOperator =
  | 'eq'
  | 'neq'
  | 'in'
  | 'not-in'
  | 'gt'
  | 'lt'
  | 'ge'
  | 'le'
  | 'like'
  | 'not-like'
  | 'null'
  | 'not-null'
  | 'on'
  | 'on-or-after'
  | 'on-or-before'
  | 'between'
  | 'not-between'
  | 'eq-userid'
  | 'eq-userteams';

/**
 * A single host-injected FetchXML condition.
 *
 * The host (e.g. CalendarWorkspaceWidget) builds an array of these from its UI
 * state (filter row, calendar selection, etc.) and passes them via the DataGrid
 * `hostFilters` prop. The framework overlays them into the top-level
 * `<filter type='and'>` of the resolved FetchXML.
 *
 * Multi-value operators (`in` / `not-in` / `between` / `not-between`) accept an
 * array `value`. Each element renders as a child `<value>...</value>` node of the
 * `<condition>`. Single-value operators may pass either a scalar or a 1-element
 * array (the helper normalizes).
 *
 * `null` / `not-null` ignore `value` entirely.
 *
 * @example Calendar widget mapping
 * ```ts
 * const hostFilters: HostFilterCondition[] = [
 *   // Event Type dropdown → single lookup
 *   applied.eventTypeId && {
 *     attribute: 'sprk_eventtype_ref',
 *     operator: 'eq',
 *     value: applied.eventTypeId,
 *   },
 *   // Status dropdown → multi-optionset
 *   applied.statusValues?.length && {
 *     attribute: 'sprk_eventstatus',
 *     operator: 'in',
 *     value: applied.statusValues,
 *   },
 *   // Date range — From + To on a chosen date attribute
 *   applied.dateField && applied.dateFrom && applied.dateTo && {
 *     attribute: applied.dateField,
 *     operator: 'between',
 *     value: [applied.dateFrom, applied.dateTo],
 *   },
 * ].filter(Boolean) as HostFilterCondition[];
 * ```
 */
export interface HostFilterCondition {
  /** FetchXML attribute logical name. */
  attribute: string;
  /** FetchXML operator. See {@link HostFilterOperator}. */
  operator: HostFilterOperator;
  /**
   * Value(s) for the condition.
   * - Scalar (`string | number | boolean`) → renders as `value='...'` attribute.
   * - Array → renders as child `<value>...</value>` nodes (FetchXML `in` shape).
   * - Ignored when `operator` is `null` / `not-null` / `eq-userid` / `eq-userteams`.
   */
  value?: string | number | boolean | ReadonlyArray<string | number | boolean>;
}

/**
 * Overlay the parent-context filter onto a FetchXML string.
 *
 * Behavior:
 * - If `filter` is unset, returns the input unchanged.
 * - If `parentContext` is missing or the value at `parentContextKey` is null/undefined/empty,
 *   returns the input unchanged.
 * - Otherwise: parses the FetchXML, finds (or creates) the top-level `<filter type='and'>`
 *   directly under `<entity>`, appends a `<condition attribute='...' operator='...' value='...'/>`,
 *   and serializes back to string.
 * - On any parse error: returns the input unchanged (graceful degradation; the savedquery's
 *   base query still runs without the parent scoping).
 *
 * Idempotency: callers should NOT call this twice on the same string (would inject duplicate
 * conditions). DataGrid.tsx applies it once per (fetchXml, parentContext) pair via useMemo.
 *
 * @param fetchXml The savedquery's stored FetchXML (base shape, no parent filter).
 * @param filter   The {@link ParentContextFilter} declaration from configjson `behavior.parentContextFilter`.
 * @param parentContext The runtime `parentContext` prop on `<DataGrid />`.
 * @returns The fetchXml with the injected condition, or the original string if no overlay applies.
 */
export function overlayParentContextFilter(
  fetchXml: string,
  filter: ParentContextFilter | undefined,
  parentContext: DataGridParentContextLike
): string {
  if (!fetchXml || !filter) return fetchXml;

  const rawValue = parentContext?.[filter.parentContextKey];
  if (rawValue === undefined || rawValue === null || rawValue === '') return fetchXml;

  const value = String(rawValue);

  // Guard: if window.DOMParser is unavailable (some test environments / SSR), bail safely.
  if (typeof DOMParser === 'undefined') return fetchXml;

  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(fetchXml, 'text/xml');
    if (doc.querySelector('parsererror')) return fetchXml;

    const entityEl = doc.querySelector('entity');
    if (!entityEl) return fetchXml;

    // Find or create the top-level <filter type='and'> directly under <entity>.
    // Iterate entity's direct children rather than using :scope (some XML
    // implementations don't support :scope reliably).
    let filterEl: Element | null = null;
    for (const child of Array.from(entityEl.children)) {
      if (child.tagName.toLowerCase() === 'filter' && child.getAttribute('type') === 'and') {
        filterEl = child;
        break;
      }
    }
    if (!filterEl) {
      filterEl = doc.createElement('filter');
      filterEl.setAttribute('type', 'and');
      // Insert AFTER any <attribute>/<link-entity> declarations? Power Apps Maker tends
      // to place filters anywhere under <entity>; we append at the end for simplicity.
      entityEl.appendChild(filterEl);
    }

    const conditionEl = doc.createElement('condition');
    conditionEl.setAttribute('attribute', filter.attribute);
    conditionEl.setAttribute('operator', filter.operator ?? 'eq');
    conditionEl.setAttribute('value', value);
    filterEl.appendChild(conditionEl);

    return new XMLSerializer().serializeToString(doc);
  } catch {
    // Any unexpected error → graceful degradation.
    return fetchXml;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// overlayHostFilters — host-prop-driven (task 033a)
// ─────────────────────────────────────────────────────────────────────────────

const VALUELESS_OPERATORS = new Set<HostFilterOperator>(['null', 'not-null', 'eq-userid', 'eq-userteams']);
const MULTI_VALUE_OPERATORS = new Set<HostFilterOperator>(['in', 'not-in', 'between', 'not-between']);

/**
 * Overlay host-supplied filter conditions onto a FetchXML string.
 *
 * Sibling to {@link overlayParentContextFilter} — runs AFTER parent-context overlay
 * and BEFORE chip augmentation in the DataGrid's composition pipeline. The
 * conditions land in the same top-level `<filter type='and'>` directly under
 * `<entity>`, alongside any pre-existing savedquery / parent-context conditions
 * (FetchXML `and` semantics — all conditions combine).
 *
 * Behavior:
 * - `fetchXml` falsy OR `hostFilters` empty/undefined → returns input unchanged.
 * - Each condition is validated minimally (attribute + operator required;
 *   non-valueless operators require a value). Invalid entries are SKIPPED
 *   silently (graceful degradation; the rest of the query still runs).
 * - Single-value operators serialize as `<condition attribute='...' operator='...' value='...'/>`.
 * - Multi-value operators (`in`, `not-in`, `between`, `not-between`) serialize as
 *   `<condition><value>...</value><value>...</value></condition>`.
 * - Valueless operators (`null`, `not-null`, `eq-userid`, `eq-userteams`) serialize
 *   as `<condition attribute='...' operator='...'/>` (no value).
 * - On any parse / serialize error: returns input unchanged.
 *
 * Idempotency: callers should pass a referentially-stable array (typically via
 * `useMemo`) to avoid duplicate-condition injection across re-renders. The DataGrid
 * memoizes the entire composition pipeline.
 *
 * @param fetchXml The FetchXML string after parent-context overlay (or the base
 *   savedquery / inline FetchXML if no parent overlay applied).
 * @param hostFilters Imperative conditions supplied by the host via the
 *   `<DataGrid hostFilters={...} />` prop.
 * @returns The fetchXml with injected conditions, or the original string if no
 *   overlay applies / on error.
 */
export function overlayHostFilters(
  fetchXml: string,
  hostFilters: ReadonlyArray<HostFilterCondition> | undefined
): string {
  if (!fetchXml || !hostFilters || hostFilters.length === 0) return fetchXml;

  // Pre-filter: drop conditions missing required fields. Done BEFORE parsing
  // so we can short-circuit if everything is invalid.
  const valid: HostFilterCondition[] = [];
  for (const cond of hostFilters) {
    if (!cond || typeof cond.attribute !== 'string' || !cond.attribute) continue;
    if (typeof cond.operator !== 'string' || !cond.operator) continue;
    if (!VALUELESS_OPERATORS.has(cond.operator)) {
      // Non-valueless operators require *something* — empty arrays and
      // null/undefined are dropped.
      if (cond.value === undefined || cond.value === null) continue;
      if (Array.isArray(cond.value) && cond.value.length === 0) continue;
      if (typeof cond.value === 'string' && cond.value === '') continue;
    }
    valid.push(cond);
  }
  if (valid.length === 0) return fetchXml;

  if (typeof DOMParser === 'undefined') return fetchXml;

  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(fetchXml, 'text/xml');
    if (doc.querySelector('parsererror')) return fetchXml;

    const entityEl = doc.querySelector('entity');
    if (!entityEl) return fetchXml;

    let filterEl: Element | null = null;
    for (const child of Array.from(entityEl.children)) {
      if (child.tagName.toLowerCase() === 'filter' && child.getAttribute('type') === 'and') {
        filterEl = child;
        break;
      }
    }
    if (!filterEl) {
      filterEl = doc.createElement('filter');
      filterEl.setAttribute('type', 'and');
      entityEl.appendChild(filterEl);
    }

    for (const cond of valid) {
      const conditionEl = doc.createElement('condition');
      conditionEl.setAttribute('attribute', cond.attribute);
      conditionEl.setAttribute('operator', cond.operator);

      if (VALUELESS_OPERATORS.has(cond.operator)) {
        // No value emitted.
      } else if (MULTI_VALUE_OPERATORS.has(cond.operator)) {
        // FetchXML <value> child elements per FetchXML schema.
        const values = Array.isArray(cond.value) ? cond.value : [cond.value as string | number | boolean];
        for (const v of values) {
          const valueEl = doc.createElement('value');
          valueEl.textContent = String(v);
          conditionEl.appendChild(valueEl);
        }
      } else {
        // Single-value: render as `value='...'` attribute. If the host passed an
        // array for a single-value operator, take the first element (defensive).
        const v = Array.isArray(cond.value) ? cond.value[0] : cond.value;
        conditionEl.setAttribute('value', String(v));
      }

      filterEl.appendChild(conditionEl);
    }

    return new XMLSerializer().serializeToString(doc);
  } catch {
    return fetchXml;
  }
}
