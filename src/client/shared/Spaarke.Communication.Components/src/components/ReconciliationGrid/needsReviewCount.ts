/**
 * needsReviewCount.ts - the ONE count source for "emails awaiting a match confirmation".
 *
 * The reconciliation tab ("Needs Review") is `ReconciliationGrid` over the `sprk_gridconfiguration`
 * record NEEDS_REVIEW_CONFIG_ID. This module reads that SAME record, takes its `source.fetchXml`, and
 * counts the rows that query selects (an aggregate count over the identical entity, filter and
 * link-entities - only attributes and ordering are dropped). The worklist's aggregate card uses it, so
 * its number equals the tab's by construction. It never counts `sprk_associationstatus` itself and does
 * not use the BFF queue-feed (capped at 200, access-filtered differently).
 *
 * Task: spaarke-ontology-platform-r1, task 054 (FR-29).
 */
import * as React from 'react';
import type { IDataverseClient } from '@spaarke/ui-components';
import { NEEDS_REVIEW_CONFIG_ID } from './ReconciliationGrid';

const COUNT_ALIAS = 'sprk_needsreviewcount';

/**
 * Turns a grid configuration's FetchXML into an aggregate count of the same rows: the entity, filter and
 * link-entities are kept verbatim; attributes, ordering and paging are removed. Returns null when the
 * FetchXML cannot be parsed.
 */
export function buildCountFetchXml(configFetchXml: string): string | null {
  try {
    const doc = new DOMParser().parseFromString(configFetchXml, 'text/xml');
    if (doc.getElementsByTagName('parsererror').length > 0) return null;
    const fetch = doc.documentElement;
    const entity = Array.from(fetch.children).find(c => c.tagName === 'entity');
    const entityName = entity?.getAttribute('name');
    if (!entity || !entityName) return null;

    const strip = (el: Element): void => {
      Array.from(el.children).forEach(child => {
        if (child.tagName === 'attribute' || child.tagName === 'all-attributes' || child.tagName === 'order') {
          el.removeChild(child);
        } else if (child.tagName === 'link-entity') {
          strip(child);
        }
      });
    };
    strip(entity);

    ['count', 'page', 'top', 'paging-cookie', 'returntotalrecordcount'].forEach(a => fetch.removeAttribute(a));
    fetch.setAttribute('aggregate', 'true');
    const countAttr = doc.createElement('attribute');
    countAttr.setAttribute('name', `${entityName}id`);
    countAttr.setAttribute('alias', COUNT_ALIAS);
    countAttr.setAttribute('aggregate', 'count');
    entity.insertBefore(countAttr, entity.firstChild);
    return new XMLSerializer().serializeToString(doc);
  } catch {
    return null;
  }
}

/** Reads the Needs Review config record and returns its `source.fetchXml` (null when absent/unparseable). */
async function loadConfigFetchXml(client: IDataverseClient, configId: string): Promise<string | null> {
  const rec = await client.retrieveRecord<Record<string, unknown>>('sprk_gridconfiguration', configId, [
    'sprk_configjson',
  ]);
  const raw = rec?.['sprk_configjson'];
  if (typeof raw !== 'string') return null;
  const parsed = JSON.parse(raw) as { source?: { fetchXml?: unknown } };
  const xml = parsed?.source?.fetchXml;
  return typeof xml === 'string' && xml.length > 0 ? xml : null;
}

/**
 * Count of emails the Needs Review tab lists. Resolves to null (rendered as "Missing", never 0) when
 * the configuration or the count cannot be read.
 */
export async function loadNeedsReviewCount(
  client: IDataverseClient,
  configId: string = NEEDS_REVIEW_CONFIG_ID
): Promise<number | null> {
  try {
    const configXml = await loadConfigFetchXml(client, configId);
    const countXml = configXml ? buildCountFetchXml(configXml) : null;
    if (!countXml) return null;
    const entityName = /<entity\b[^>]*\bname\s*=\s*['"]([^'"]+)['"]/i.exec(countXml)?.[1];
    if (!entityName) return null;
    const result = await client.retrieveMultipleRecords<Record<string, unknown>>(entityName, countXml);
    const value = result.entities?.[0]?.[COUNT_ALIAS];
    const n = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(n) ? n : null;
  } catch {
    return null;
  }
}

/** Hook form of {@link loadNeedsReviewCount}: `{ count, loading }`; count is null until loaded or on failure. */
export function useNeedsReviewCount(
  client: IDataverseClient | undefined,
  configId: string = NEEDS_REVIEW_CONFIG_ID
): { count: number | null; loading: boolean } {
  const [state, setState] = React.useState<{ count: number | null; loading: boolean }>({
    count: null,
    loading: !!client,
  });
  React.useEffect(() => {
    if (!client) {
      setState({ count: null, loading: false });
      return;
    }
    let cancelled = false;
    setState(s => ({ ...s, loading: true }));
    void loadNeedsReviewCount(client, configId).then(count => {
      if (!cancelled) setState({ count, loading: false });
    });
    return () => {
      cancelled = true;
    };
  }, [client, configId]);
  return state;
}
