/**
 * lookupSearch — the Dataverse half of an inline record-header lookup.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * WHY THIS MODULE EXISTS (CLAUDE.md §11 justification)
 * ══════════════════════════════════════════════════════════════════════════
 * Rendering an OOB-shaped inline lookup needs three things the two existing
 * components deliberately do NOT have:
 *
 *  1. a search callback over the TARGET table,
 *  2. that table's primary id + primary NAME attributes, and
 *  3. an escalation into the OOB advanced dialog.
 *
 * None of them can live in `components/LookupField` — it is context-agnostic
 * by ADR-012 and has twelve consumers, most of them Code Pages where
 * `Xrm.WebApi` is absent entirely. None of them may live in
 * `RecordHeaderView.tsx` either: that file's contract is "presentation wiring,
 * not behavior", and the next header consumer would re-derive all three.
 *
 * So the machinery lands here, in the shared library, where every consumer
 * gets it — and `RecordHeaderView` keeps doing nothing but choosing renderers.
 *
 * ── The target's primary NAME attribute must be READ, never inferred ───────
 * The convention across Spaarke's taxonomy tables is NOT uniform:
 *
 *   sprk_projecttype_ref  → sprk_name             ← guessable pattern A
 *   sprk_eventtype_ref    → sprk_name             ← pattern A
 *   sprk_mattertype_ref   → sprk_mattertypename   ← pattern B
 *
 * R1's `MatterHeaderView` sidestepped this with a hard-coded `LOOKUP_META`
 * table, which is exactly what R2 exists to delete (FR-15: targets come from
 * metadata, never from constants). A second `retrieveEntityMetadata` call for
 * the target entity is therefore load-bearing, not an optimization target —
 * and it is page-session cached by `XrmDataverseClient`, so N lookup cells on
 * M headers still cost one round trip per distinct target.
 *
 * Standards: ADR-012 (shared-lib home, host-agnostic surface) · ADR-022
 * (React 16/17-safe hooks only) · NFR-05 (no `@spaarke/auth`) · NFR-06 (no
 * BFF — host-context `Xrm` only).
 *
 * @see FR-15 / FR-15a in projects/record-header-and-notepad-r2/spec.md
 * @see components/LookupField/LookupField.tsx — the presentation half
 */

import * as React from 'react';

import type { ILookupItem } from '../../types/LookupTypes';
import { XrmDataverseClient } from '../../services/XrmDataverseClient';
import { getXrm } from '../../utils/xrmContext';
import type { ILookupFieldValue } from './fields/LookupField';

/**
 * Rows fetched per search. Matches R1's proven `$top=10` — enough to fill the
 * dropdown's ~5.5 visible rows and signal "more below" by overflowing it.
 */
export const LOOKUP_SEARCH_PAGE_SIZE = 10;

/**
 * Escape a user-typed term for embedding in an OData string literal.
 *
 * OData escapes a single quote by DOUBLING it. Without this, a value such as
 * `O'Brien` terminates the literal early and the whole query 400s.
 */
export function escapeODataLiteral(value: string): string {
  return value.replace(/'/g, "''");
}

/**
 * Build the OData query string for a lookup search.
 *
 * PURE — no Xrm, no network — because this is the piece most likely to be got
 * subtly wrong, and a wrong query string fails as an opaque HTTP 400.
 *
 * An EMPTY query deliberately omits `$filter` entirely rather than emitting
 * `contains(name,'')`. That is what makes the browse affordance (clicking the
 * magnifier with nothing typed) return the target's first N rows instead of
 * an empty list — the OOB inline lookup's behaviour.
 *
 * TWO different escapes apply, and they are NOT interchangeable:
 *
 *  - `''` (OData literal escaping) is what protects a quote. `encodeURIComponent`
 *    deliberately leaves `'` alone — it is in the unescaped set alongside
 *    `-_.!~*()` — so without the doubling, `O'Brien` terminates the literal
 *    early and the whole query 400s.
 *  - `encodeURIComponent` is what protects the QUERY STRING: an unencoded `&`
 *    in `M & A` would terminate the `$filter` parameter outright.
 *
 * Order matters only in that the doubling must come first; encoding first
 * would be harmless for `'` today but would break if the escape set changed.
 * `Xrm.WebApi.retrieveMultipleRecords` appends this string to the request URL
 * verbatim, so both escapes are ours to apply. This is R1's proven shape.
 *
 * @param primaryIdAttribute   Target table's primary key attribute.
 * @param primaryNameAttribute Target table's primary NAME attribute — read
 *                             from metadata (see the file header).
 * @param query                Raw user input; trimmed here.
 * @param top                  Page size; defaults to {@link LOOKUP_SEARCH_PAGE_SIZE}.
 */
export function buildLookupSearchOptions(
  primaryIdAttribute: string,
  primaryNameAttribute: string,
  query: string,
  top: number = LOOKUP_SEARCH_PAGE_SIZE
): string {
  const trimmed = typeof query === 'string' ? query.trim() : '';
  const filter =
    trimmed.length > 0
      ? `&$filter=contains(${primaryNameAttribute},'${encodeURIComponent(escapeODataLiteral(trimmed))}')`
      : '';
  return (
    `?$select=${primaryIdAttribute},${primaryNameAttribute}` +
    filter +
    `&$orderby=${primaryNameAttribute} asc&$top=${top}`
  );
}

/** The two attribute names a lookup search needs from the TARGET table. */
export interface ILookupTargetKeys {
  idAttribute: string;
  nameAttribute: string;
}

/**
 * Resolve the target table's primary id + primary name attributes.
 *
 * Page-session cached inside `XrmDataverseClient`, so repeated calls for the
 * same target are free after the first.
 *
 * Returns `null` — never throws — when Xrm is unavailable, the metadata call
 * fails, or the payload omits either attribute. A lookup that cannot resolve
 * its target degrades to "no results", which is visible and recoverable; a
 * throw here would propagate out of the search callback and blank the header
 * (NFR-10).
 */
export async function resolveLookupTargetKeys(target: string): Promise<ILookupTargetKeys | null> {
  if (typeof target !== 'string' || target.length === 0) return null;

  try {
    const metadata = await new XrmDataverseClient().retrieveEntityMetadata(target);
    const idAttribute = metadata?.primaryIdAttribute ?? '';
    const nameAttribute = metadata?.primaryNameAttribute ?? '';

    if (!idAttribute || !nameAttribute) {
      // Loud, because this is silent otherwise: the cell renders an empty
      // dropdown and looks like the table has no rows.
      console.warn(`[lookupSearch] '${target}': metadata returned no primary id/name attribute — search disabled.`, {
        idAttribute,
        nameAttribute,
      });
      return null;
    }
    return { idAttribute, nameAttribute };
  } catch (err) {
    console.warn(`[lookupSearch] '${target}': entity metadata lookup failed — search disabled.`, err);
    return null;
  }
}

/**
 * Search the target table by its primary name.
 *
 * Shape copied from R1's `MatterHeaderView.searchLookup`, which has run in
 * production since v1.0.7 — minus the hard-coded `LOOKUP_META` table, which is
 * replaced by {@link resolveLookupTargetKeys}.
 *
 * Never throws: every failure resolves to `[]`, matching the "the field
 * degrades, never crashes" contract `LookupField` already guards with a test.
 */
export async function searchLookupTarget(
  target: string,
  query: string,
  top: number = LOOKUP_SEARCH_PAGE_SIZE
): Promise<ILookupItem[]> {
  const keys = await resolveLookupTargetKeys(target);
  if (!keys) return [];

  const xrm = getXrm();
  if (typeof xrm?.WebApi?.retrieveMultipleRecords !== 'function') {
    console.warn(`[lookupSearch] '${target}': Xrm.WebApi is unavailable — search returned no results.`);
    return [];
  }

  try {
    const options = buildLookupSearchOptions(keys.idAttribute, keys.nameAttribute, query, top);
    const result = await xrm.WebApi.retrieveMultipleRecords(target, options);
    const rows = (result?.entities ?? []) as Array<Record<string, unknown>>;

    return rows
      .map(row => ({
        id: String(row[keys.idAttribute] ?? ''),
        name: String(row[keys.nameAttribute] ?? ''),
      }))
      .filter(item => item.id.length > 0);
  } catch (err) {
    console.warn(`[lookupSearch] '${target}': search query failed.`, { query, error: err });
    return [];
  }
}

/**
 * Open the OOB advanced lookup dialog and return the picked record.
 *
 * THE single call site for `Xrm.Utility.lookupObjects` in the shared library —
 * `RecordHeaderLookupField` delegates here rather than keeping a second copy.
 * That consolidation is the point: the bug documented below recurred precisely
 * because the same lesson had been learned on a DIFFERENT Xrm namespace and
 * never applied to this one.
 *
 * Resolves `null` on cancel, on an empty result, and on any failure — the
 * caller stages nothing in those cases.
 *
 * @param target   Target table logical name (`targets[0]`).
 * @param logLabel Field label, used only to make the console warnings locatable.
 */
export async function openAdvancedLookup(target: string, logLabel: string): Promise<ILookupFieldValue | null> {
  try {
    const xrm = getXrm();
    if (typeof xrm?.Utility?.lookupObjects !== 'function') {
      // WARN, though it is a no-op: silently doing nothing on click is
      // indistinguishable from a dead control, and cost a full UAT round.
      console.warn(`[lookupSearch] "${logLabel}": Xrm.Utility.lookupObjects is unavailable — picker cannot open.`);
      return null;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Call `lookupObjects` DIRECTLY on `xrm.Utility` — never through a local
    // alias. Aliasing detaches the method from its receiver, and the Xrm
    // implementation reads `this._clientApiExecutor` internally, so a detached
    // call dies with:
    //   TypeError: Cannot read properties of undefined (reading '_clientApiExecutor')
    //
    // This is the SAME `this`-binding trap R1 hit on `Xrm.Navigation.navigateTo`,
    // where v1.0.2–v1.0.5 aliased the method and shipped four releases of a
    // silent no-op. Task 023 reintroduced it here in a different Xrm namespace,
    // and a `catch {}` swallowed the TypeError on every click, so the cell
    // merely looked read-only. See FAILURE-MODES G-14.
    //
    // Do NOT "simplify" this back to `const lookupObjects = xrm.Utility...`.
    // ══════════════════════════════════════════════════════════════════════
    const results = await xrm.Utility.lookupObjects({
      entityTypes: [target],
      defaultEntityType: target,
      allowMultiSelect: false,
    });

    if (!results || results.length === 0) return null;

    const picked = results[0];
    // Normalize the same way `CommunicationActionsApp.tsx:420` does, so picked
    // values compare consistently with `useRecordFieldValues` projections
    // (brace-stripped, lowercased GUID).
    const id = String(picked.id).replace(/[{}]/g, '').toLowerCase();
    return { id, name: picked.name, entityType: picked.entityType };
  } catch (err) {
    // Xrm surfaces its own error UX, so the "never throw on click" contract
    // holds — but do NOT swallow silently. A bare `catch {}` here made every
    // picker failure look identical to a control that simply is not wired.
    console.warn(`[lookupSearch] "${logLabel}": picker or save failed.`, { target, error: err });
    return null;
  }
}

/** What {@link useLookupTargetSearch} hands a lookup cell. */
export interface IUseLookupTargetSearchResult {
  /** Drop straight into `LookupField.onSearch`. Never rejects. */
  search: (query: string) => Promise<ILookupItem[]>;
  /**
   * Drop straight into `LookupField.onAdvanced`.
   *
   * `undefined` when there is no resolved target — which is what makes the
   * dropdown's **Advanced** footer disappear rather than render an action that
   * could not work.
   */
  openAdvanced: (() => Promise<void>) | undefined;
}

/**
 * Bind {@link searchLookupTarget} + {@link openAdvancedLookup} to one lookup cell.
 *
 * Safe to call unconditionally for EVERY field in a header — a cell with no
 * target does no work and issues no request, so the hook costs nothing on the
 * six non-lookup renderers. That matters because React forbids calling it
 * conditionally, and the renderer switch runs after it.
 *
 * React 16/17-safe: `useCallback` / `useMemo` only (ADR-022).
 *
 * @param target Target table logical name (`targets[0]`), or `undefined`.
 * @param label  Field label — console diagnostics only.
 * @param onPick Receives the record chosen in the ADVANCED dialog. The inline
 *               dropdown reports its own selections through `onChange`.
 */
export function useLookupTargetSearch(
  target: string | undefined,
  label: string,
  onPick: (item: ILookupItem | null) => void
): IUseLookupTargetSearchResult {
  const search = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => (target ? searchLookupTarget(target, query) : []),
    [target]
  );

  // Guards a rapid double-click from spawning two dialogs while the first is
  // still awaiting the user. A ref, not state — nothing rendered reflects it.
  const openingRef = React.useRef(false);

  const openAdvanced = React.useCallback(async (): Promise<void> => {
    if (!target || openingRef.current) return;
    openingRef.current = true;
    try {
      const picked = await openAdvancedLookup(target, label);
      // Cancel resolves `null`; staging a clear on cancel would silently
      // destroy the existing value.
      if (picked) onPick({ id: picked.id, name: picked.name });
    } finally {
      openingRef.current = false;
    }
  }, [target, label, onPick]);

  return React.useMemo(
    () => ({ search, openAdvanced: target ? openAdvanced : undefined }),
    [search, openAdvanced, target]
  );
}
