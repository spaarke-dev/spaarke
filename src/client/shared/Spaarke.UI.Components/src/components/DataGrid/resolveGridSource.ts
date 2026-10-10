/**
 * resolveGridSource - how a DataGrid turns a `sprk_gridconfiguration` id into its base query.
 *
 * Moved VERBATIM out of DataGrid.tsx (no behaviour change) so that other consumers that must agree with
 * a grid's rows - e.g. an aggregate count that points at the grid - call the SAME resolution instead of
 * re-reading the record a second way (spaarke-ontology-platform-r1 task 054, review round 2 F2).
 */
import type { IDataverseClient, SavedQueryResult } from '../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../types/DataGridConfiguration';
import { isValidDataGridConfiguration } from '../../types/DataGridConfiguration';

/**
 * Best-effort fetch of the `sprk_gridconfiguration` record (FR-DG-04 — non-existent
 * configIds MUST fall through gracefully). Returns `null` configRecord on miss.
 */
export async function fetchConfigRecord(
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
 * Which host the resolution runs for. `'external'` is the external SPA (outside counsel): there a grid never
 * lists the entity's saved queries (unified-access-control-r2 task 157). REQUIRED on {@link resolveSource} so
 * a caller has to say which it is; it cannot be forgotten.
 */
export type GridSourceHost = 'internal' | 'external';

/**
 * Resolve `source` into a `{ entityName, fetchXml, layoutXml }` triple via savedquery
 * lookup, inline literal, or savedquery-set discovery + first match.
 *
 * `host === 'external'` refuses a savedquery-set source (returns null WITHOUT listing the entity's saved
 * queries); the one configured `savedquery` id and an inline source resolve as on the internal host. This
 * function never takes a caller-chosen view. The internal host's behaviour is unchanged.
 */
export async function resolveSource(
  dataverseClient: IDataverseClient,
  configRecord: DataGridConfiguration | null,
  fallbackEntityName: string | undefined,
  host: GridSourceHost
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
    if (host === 'external') return null;
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

export function extractEntityFromFetchXml(fetchXml: string): string | undefined {
  if (!fetchXml) return undefined;
  // Primary: DOMParser (browser). Fall back to a regex when DOMParser is
  // unavailable/returns a parsererror (owner UAT 2026-08-12 #4C — a valid inline
  // fetchXml was yielding an empty entityName → the "Cannot resolve entityName"
  // error; the regex makes extraction robust to DOMParser quirks/XML-decl/BOM/
  // entity-encoded quotes). Matches `<entity name="…">` or `name='…'`.
  try {
    if (typeof DOMParser !== 'undefined') {
      const parser = new DOMParser();
      const doc = parser.parseFromString(fetchXml, 'text/xml');
      if (!doc.querySelector('parsererror')) {
        const name = doc.querySelector('entity')?.getAttribute('name');
        if (name) return name;
      }
    }
  } catch {
    /* fall through to regex */
  }
  const m = /<entity\b[^>]*\bname\s*=\s*['"]([^'"]+)['"]/i.exec(fetchXml);
  return m?.[1] ?? undefined;
}
