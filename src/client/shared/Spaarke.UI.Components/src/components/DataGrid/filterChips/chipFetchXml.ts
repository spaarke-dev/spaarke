/**
 * chipFetchXml — runtime FetchXML mutation: injects chip-derived
 * `<condition>` elements into the top-level `<filter type='and'>` under the
 * savedquery's `<entity>` node.
 *
 * Mirrors the DOMParser / XMLSerializer pattern used by
 * `fetchXmlOverlay.ts#overlayParentContextFilter` (find-or-create the top-level
 * AND filter, append conditions, serialize). Graceful: on any parse error or
 * unexpected exception the function returns the input string unchanged —
 * the chip filter is dropped but the savedquery still runs.
 *
 * Per-{@link ChipKind} mapping:
 *
 * | kind        | FetchXML emitted                                              |
 * |-------------|---------------------------------------------------------------|
 * | `text`      | `<condition attribute='X' operator='like' value='%V%'/>`      |
 * | `optionset` | 1 val: `operator='eq'`; N: `operator='in'` w/ `<value>` kids  |
 * | `lookup`    | `operator='in'` with `<value>` children (record IDs)          |
 * | `daterange` | `on-or-after` AND/OR `on-or-before` (whichever bound is set)  |
 * | `bool`      | `<condition attribute='X' operator='eq' value='1'/>` (or '0') |
 *
 * **Idempotency**: callers SHOULD apply this once per (fetchXml, state) pair;
 * applying twice will duplicate conditions. The DataGrid integrates this via
 * useMemo on the chip state, matching the pattern used by
 * `overlayParentContextFilter`.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §6.4
 * **FR**: FR-DG-07, FR-DG-13
 *
 * @see fetchXmlOverlay.ts — sibling parent-context overlay (same pattern)
 * @see types.ts            — {@link ChipDescriptor} + {@link ChipState}
 */

import type { ChipDescriptor, ChipState, ChipValue } from './types';

// ─────────────────────────────────────────────────────────────────────────────
// Public API
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Augment a FetchXML string with the chips' applied filter conditions.
 *
 * Behavior:
 * - Empty state (no entries, or every entry undefined) → returns input unchanged.
 * - Missing `<entity>` element → returns input unchanged.
 * - Any parse error / unexpected exception → returns input unchanged (graceful).
 * - Otherwise: parses the FetchXML, finds-or-creates the top-level
 *   `<filter type='and'>` under `<entity>`, appends one or more `<condition>`
 *   elements per active chip, and serializes back to a string.
 *
 * @param baseFetchXml The savedquery's stored FetchXML (or the result of
 *                     `overlayParentContextFilter`, if used together).
 * @param descriptors  The chip descriptors produced by {@link discoverChips}.
 *                     Only chips with a matching entry in `state` produce
 *                     conditions; descriptors without active state are ignored.
 * @param state        The user's currently-applied filter values.
 * @returns The augmented FetchXML, or the input string when no augmentation
 *          applies.
 */
export function augmentFetchXmlWithChips(
  baseFetchXml: string,
  descriptors: readonly ChipDescriptor[],
  state: ChipState
): string {
  if (!baseFetchXml) return baseFetchXml;

  // Fast-path: nothing to add.
  const activeAttributes = collectActiveAttributes(state);
  if (activeAttributes.length === 0) return baseFetchXml;
  if (descriptors.length === 0) return baseFetchXml;

  // Guard: SSR / test envs without DOMParser.
  if (typeof DOMParser === 'undefined') return baseFetchXml;

  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(baseFetchXml, 'text/xml');
    if (doc.querySelector('parsererror')) return baseFetchXml;

    const entityEl = doc.querySelector('entity');
    if (!entityEl) return baseFetchXml;

    // Find or create the top-level <filter type='and'> directly under <entity>.
    const filterEl = findOrCreateAndFilter(doc, entityEl);

    // Build a quick lookup so descriptor order drives condition ordering.
    let appended = 0;
    for (const desc of descriptors) {
      const value = state[desc.attribute];
      if (!value) continue;
      // Defensive: descriptor and state value must agree on kind.
      if (value.kind !== desc.kind) continue;
      const added = appendConditions(doc, filterEl, desc, value);
      appended += added;
    }

    if (appended === 0) return baseFetchXml;

    return new XMLSerializer().serializeToString(doc);
  } catch {
    return baseFetchXml;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Internals
// ─────────────────────────────────────────────────────────────────────────────

function collectActiveAttributes(state: ChipState): string[] {
  const out: string[] = [];
  for (const key of Object.keys(state)) {
    if (state[key] !== undefined) out.push(key);
  }
  return out;
}

function findOrCreateAndFilter(doc: Document, entityEl: Element): Element {
  for (const child of Array.from(entityEl.children)) {
    if (child.tagName.toLowerCase() === 'filter' && child.getAttribute('type') === 'and') {
      return child;
    }
  }
  const created = doc.createElement('filter');
  created.setAttribute('type', 'and');
  entityEl.appendChild(created);
  return created;
}

/**
 * Append one or more `<condition>` elements for a single chip into the
 * provided `<filter>` parent. Returns the number of conditions appended
 * (0 when the value is empty / out-of-range).
 */
function appendConditions(doc: Document, filterEl: Element, descriptor: ChipDescriptor, value: ChipValue): number {
  switch (value.kind) {
    case 'text': {
      const text = value.value?.trim();
      if (!text) return 0;
      const cond = doc.createElement('condition');
      cond.setAttribute('attribute', descriptor.attribute);
      // Honor the operator the user picked in the "Filter by" dropdown:
      // - `equals`   → `<condition operator='eq' value='X'/>`
      // - `begins`   → `<condition operator='like' value='X%'/>`
      // - `contains` (default) → `<condition operator='like' value='%X%'/>`
      switch (value.op) {
        case 'equals':
          cond.setAttribute('operator', 'eq');
          cond.setAttribute('value', text);
          break;
        case 'begins':
          cond.setAttribute('operator', 'like');
          cond.setAttribute('value', `${text}%`);
          break;
        default:
          cond.setAttribute('operator', 'like');
          cond.setAttribute('value', `%${text}%`);
          break;
      }
      filterEl.appendChild(cond);
      return 1;
    }
    case 'optionset': {
      const values = value.values ?? [];
      if (values.length === 0) return 0;
      if (values.length === 1) {
        const cond = doc.createElement('condition');
        cond.setAttribute('attribute', descriptor.attribute);
        cond.setAttribute('operator', 'eq');
        cond.setAttribute('value', String(values[0]));
        filterEl.appendChild(cond);
        return 1;
      }
      const cond = doc.createElement('condition');
      cond.setAttribute('attribute', descriptor.attribute);
      cond.setAttribute('operator', 'in');
      for (const v of values) {
        const vEl = doc.createElement('value');
        vEl.textContent = String(v);
        cond.appendChild(vEl);
      }
      filterEl.appendChild(cond);
      return 1;
    }
    case 'lookup': {
      const ids = value.recordIds ?? [];
      if (ids.length === 0) return 0;
      const cond = doc.createElement('condition');
      cond.setAttribute('attribute', descriptor.attribute);
      cond.setAttribute('operator', 'in');
      for (const id of ids) {
        const vEl = doc.createElement('value');
        vEl.textContent = String(id);
        cond.appendChild(vEl);
      }
      filterEl.appendChild(cond);
      return 1;
    }
    case 'daterange': {
      let appended = 0;
      if (value.from) {
        const cond = doc.createElement('condition');
        cond.setAttribute('attribute', descriptor.attribute);
        cond.setAttribute('operator', 'on-or-after');
        cond.setAttribute('value', value.from);
        filterEl.appendChild(cond);
        appended += 1;
      }
      if (value.to) {
        const cond = doc.createElement('condition');
        cond.setAttribute('attribute', descriptor.attribute);
        cond.setAttribute('operator', 'on-or-before');
        cond.setAttribute('value', value.to);
        filterEl.appendChild(cond);
        appended += 1;
      }
      return appended;
    }
    case 'bool': {
      const cond = doc.createElement('condition');
      cond.setAttribute('attribute', descriptor.attribute);
      cond.setAttribute('operator', 'eq');
      cond.setAttribute('value', value.value ? '1' : '0');
      filterEl.appendChild(cond);
      return 1;
    }
    default:
      return 0;
  }
}
