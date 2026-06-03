/**
 * fetchXmlOverlay — runtime FetchXML mutation helpers for the DataGrid framework.
 *
 * Currently exports `overlayParentContextFilter` which injects a parent-context
 * filter condition into a savedquery's stored FetchXML. Why this is needed: Dataverse
 * server-side validation REJECTS placeholder syntax like `value='@MatterId'` in stored
 * savedquery FetchXML (every condition value is parsed as a typed literal at save time).
 * The standard Power Platform pattern is to store the BASE query and overlay the parent
 * filter at runtime.
 *
 * Triggered by task 020 (Matter-context savedqueries) deviation D-020-02. See
 * `projects/spaarke-datagrid-framework-r1/notes/drafts/020-deviations.md`.
 *
 * @see ParentContextFilter — schema in `types/DataGridConfiguration.ts`
 * @see DataGrid.tsx — calls this helper before passing fetchXml to useLazyLoad
 */
import type { ParentContextFilter } from '../../types/DataGridConfiguration';

/**
 * Parent-context value bag passed via the DataGrid's `parentContext` prop.
 * Open-ended record (keys are project-specific; e.g. `matterId`, `projectId`).
 */
export type DataGridParentContextLike = Record<string, unknown> | undefined;

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
