/**
 * FetchXML Builders — pure FetchXML-string helpers (no WebAPI / Xrm / ComponentFramework).
 *
 * VHVU-050: split out of ViewDataService so the "pure vs executor" boundary is
 * explicit. These helpers only manipulate FetchXML strings + substitute runtime
 * parameters — they never touch `webApi`, `window.Xrm`, or PCF context. The
 * WebAPI executors (getViewFetchXml, resolveQuery, fetchEventsFromView, …) stay
 * in ViewDataService.ts.
 *
 * NOTE on placement (directional deviation from VHVU-050 POML step 1): the POML
 * suggested moving these into `@spaarke/visuals`, but the project CLAUDE.md
 * non-negotiable is "@spaarke/visuals is presentational only — no FetchXML", and
 * after the props-in refactor NOTHING in @spaarke/visuals consumes these helpers
 * (only the PCF-side containers do). So they stay PCF-side here. ViewDataService
 * re-exports them for existing importers (DataAggregationService, containers).
 */

import { logger } from '../utils/logger';

/**
 * Inject a context filter condition into FetchXML.
 * Adds a filter condition for the specified field = recordId
 * without modifying the original saved view.
 *
 * @param fetchXml - Original FetchXML string
 * @param fieldName - The lookup field to filter on (e.g., "_sprk_matterid_value")
 * @param recordId - The record ID to filter by
 * @returns Modified FetchXML string with injected filter
 */
export function injectContextFilter(fetchXml: string, fieldName: string, recordId: string): string {
  const cleanId = recordId.replace(/[{}]/g, '');

  // Build the condition to inject
  const condition = `<condition attribute="${fieldName}" operator="eq" value="${cleanId}" />`;

  // Strategy: Find the <entity> element and inject a filter inside it.
  // If there's already a <filter> at the entity level, wrap both in an AND filter.
  // We use string manipulation instead of DOMParser to avoid browser compatibility issues
  // in PCF environments where DOMParser may not be available.

  // Check if there's already a top-level <filter in the entity
  const entityMatch = fetchXml.match(/<entity\s[^>]*>/i);
  if (!entityMatch) {
    logger.warn('FetchXmlBuilders', 'Could not find <entity> in FetchXML, appending filter');
    // Fallback: try to inject before </fetch>
    return fetchXml.replace(/<\/fetch>/i, `<filter type="and">${condition}</filter></fetch>`);
  }

  const entityTagEnd = fetchXml.indexOf('>', fetchXml.indexOf(entityMatch[0])) + 1;

  // v1.4.14 — Walk past every `<link-entity>...</link-entity>` block so the
  // first `<filter>` we find is the entity-level filter on `<entity>`, NOT
  // a filter nested INSIDE a link-entity (whose conditions would apply to
  // the linked table, not the queried entity). The previous logic naively
  // took the first `<filter>` in the FetchXML, which fails for queries that
  // declare a link-entity BEFORE the entity-level filter (the FetchXML's
  // own canonical ordering) — Dataverse then rejects the query with
  // "<linkedEntity> doesn't contain attribute <contextfield>" because the
  // injected condition lands on the wrong table.
  //
  // Strategy: find the offset of the last top-level `</link-entity>` inside
  // the entity body and search for `<filter>` from there. Link-entities are
  // not nested inside other link-entities at the entity level in any
  // FetchXML we currently author, so a simple lastIndexOf is sufficient.
  const beforeEntityClose = fetchXml.lastIndexOf('</entity>');
  const entityContents =
    beforeEntityClose > entityTagEnd
      ? fetchXml.substring(entityTagEnd, beforeEntityClose)
      : fetchXml.substring(entityTagEnd);
  const lastLinkEntityCloseIdx = entityContents.lastIndexOf('</link-entity>');
  const searchStartInContents = lastLinkEntityCloseIdx >= 0 ? lastLinkEntityCloseIdx + '</link-entity>'.length : 0;
  const afterEntity = entityContents.substring(searchStartInContents);
  const existingFilterMatch = afterEntity.match(/(<filter[^>]*>)/i);
  // Absolute base offset for translating positions back to `fetchXml`.
  const searchBaseOffset = entityTagEnd + searchStartInContents;

  if (existingFilterMatch) {
    // There's already an entity-level filter — wrap existing filter content
    // and our condition in an AND. Position is relative to `fetchXml` via
    // the searchBaseOffset computed above (which skips past link-entities).
    const filterPos = searchBaseOffset + afterEntity.indexOf(existingFilterMatch[0]);
    const filterTag = existingFilterMatch[0];

    // Check if existing filter already has type="and"
    if (filterTag.includes('type="and"') || filterTag.includes("type='and'")) {
      // Just inject our condition right after the opening <filter> tag
      const insertPos = filterPos + filterTag.length;
      return fetchXml.substring(0, insertPos) + condition + fetchXml.substring(insertPos);
    }

    // Existing filter has a different type (e.g., "or") - wrap both in an AND
    // Find the matching </filter> for this filter element
    const closingFilterPos = findMatchingClose(fetchXml, filterPos, 'filter');
    if (closingFilterPos >= 0) {
      const existingFilter = fetchXml.substring(filterPos, closingFilterPos + '</filter>'.length);
      const wrappedFilter = `<filter type="and">${condition}${existingFilter}</filter>`;
      return (
        fetchXml.substring(0, filterPos) + wrappedFilter + fetchXml.substring(closingFilterPos + '</filter>'.length)
      );
    }
  }

  // No existing entity-level filter — insert one right BEFORE </entity>
  // (i.e., AFTER any link-entity blocks). This keeps the new filter at the
  // entity level so its conditions apply to the queried entity, not a link.
  if (beforeEntityClose > entityTagEnd) {
    return (
      fetchXml.substring(0, beforeEntityClose) +
      `<filter type="and">${condition}</filter>` +
      fetchXml.substring(beforeEntityClose)
    );
  }

  // Fallback: prepend a filter right after the opening <entity> tag.
  return (
    fetchXml.substring(0, entityTagEnd) + `<filter type="and">${condition}</filter>` + fetchXml.substring(entityTagEnd)
  );
}

/**
 * Find the matching closing tag position for a given opening tag position.
 * Handles nested elements of the same type.
 */
function findMatchingClose(xml: string, openPos: number, tagName: string): number {
  const openTag = new RegExp(`<${tagName}[\\s>]`, 'gi');
  const closeTag = `</${tagName}>`;
  let depth = 0;
  let pos = openPos;

  while (pos < xml.length) {
    const nextClose = xml.indexOf(closeTag, pos);
    if (nextClose < 0) return -1;

    // Count opening tags between current position and next close
    openTag.lastIndex = pos + 1;
    let match;
    while ((match = openTag.exec(xml)) !== null && match.index < nextClose) {
      depth++;
    }

    if (depth === 0) {
      return nextClose;
    }

    depth--;
    pos = nextClose + closeTag.length;
  }

  return -1;
}

/**
 * Inject required attribute elements into FetchXML to ensure chart-needed
 * columns are returned even when not included in the saved view's column set.
 *
 * Handles WebAPI → FetchXML field name conversion:
 *   _sprk_eventtype_ref_value → sprk_eventtype_ref (lookup fields)
 *   sprk_regardingrecordid     → sprk_regardingrecordid (text fields, no-op)
 *
 * Skips injection when the view uses <all-attributes />.
 *
 * @param fetchXml - The view's FetchXML string
 * @param requiredColumns - WebAPI property names needed for chart aggregation
 * @returns FetchXML with missing attributes injected
 */
export function injectRequiredAttributes(fetchXml: string, requiredColumns: string[]): string {
  if (!requiredColumns || requiredColumns.length === 0) return fetchXml;

  // <all-attributes /> means every column is returned — nothing to inject
  if (/<all-attributes\s*\/?>/i.test(fetchXml)) {
    logger.debug('FetchXmlBuilders', 'View uses <all-attributes />, skipping attribute injection');
    return fetchXml;
  }

  const entityMatch = fetchXml.match(/<entity\s[^>]*>/i);
  if (!entityMatch) return fetchXml;

  const entityTagEnd = fetchXml.indexOf('>', fetchXml.indexOf(entityMatch[0])) + 1;

  // Collect missing attributes
  const toInject: string[] = [];

  for (const col of requiredColumns) {
    // Convert WebAPI property name → FetchXML attribute name
    // _sprk_eventtype_ref_value → sprk_eventtype_ref
    const fetchXmlAttr = col
      .replace(/^_/, '')
      .replace(/_value$/, '')
      .toLowerCase();

    // Check if attribute already exists in the view's FetchXML
    const exists = new RegExp(`<attribute\\s[^>]*name=["']${fetchXmlAttr}["']`, 'i').test(fetchXml);

    if (!exists) {
      toInject.push(fetchXmlAttr);
    }
  }

  if (toInject.length === 0) return fetchXml;

  const attrXml = toInject.map(a => `<attribute name="${a}" />`).join('');

  logger.info('FetchXmlBuilders', `Injecting required attributes into view FetchXML: ${toInject.join(', ')}`);

  return fetchXml.substring(0, entityTagEnd) + attrXml + fetchXml.substring(entityTagEnd);
}

/**
 * Apply max items (top count) to FetchXML
 */
export function applyMaxItems(fetchXml: string, maxItems: number): string {
  // Check if <fetch> already has a "top" attribute
  const fetchTagMatch = fetchXml.match(/<fetch([^>]*)>/i);
  if (!fetchTagMatch) return fetchXml;

  const fetchAttrs = fetchTagMatch[1];

  if (/\btop\s*=/i.test(fetchAttrs)) {
    // Replace existing top value
    return fetchXml.replace(/(<fetch[^>]*)\btop\s*=\s*["']\d+["']([^>]*>)/i, `$1top="${maxItems}"$2`);
  }

  // Add top attribute
  return fetchXml.replace(/(<fetch)([^>]*>)/i, `$1 top="${maxItems}"$2`);
}

// ──────────────────────────────────────────────────
// Parameter Substitution (Task 041)
// ──────────────────────────────────────────────────

/**
 * Runtime parameters available for FetchXML substitution
 */
export interface ISubstitutionParams {
  /** Current record ID from form context */
  contextRecordId?: string;
  /** Current user's system user ID */
  currentUserId?: string;
}

/**
 * Substitute parameter placeholders in a FetchXML string.
 *
 * Supported placeholders:
 * - {contextRecordId} → current record ID from form context
 * - {currentUserId}   → current Dataverse system user ID
 * - {currentDate}     → today's date in ISO format (YYYY-MM-DD)
 * - {currentDateTime} → current date/time in ISO format
 *
 * Parameters from sprk_fetchxmlparams JSON are also substituted.
 *
 * @param fetchXml - FetchXML string with placeholders
 * @param params - Runtime parameter values
 * @param paramMappings - Optional JSON string of additional param mappings from sprk_fetchxmlparams
 * @returns FetchXML with placeholders replaced
 */
export function substituteParameters(fetchXml: string, params: ISubstitutionParams, paramMappings?: string): string {
  let result = fetchXml;

  // Built-in parameters
  const now = new Date();
  const builtInParams: Record<string, string> = {
    contextRecordId: params.contextRecordId?.replace(/[{}]/g, '') || '',
    currentUserId: params.currentUserId?.replace(/[{}]/g, '') || '',
    currentDate: now.toISOString().split('T')[0], // YYYY-MM-DD
    currentDateTime: now.toISOString(),
  };

  // Replace built-in placeholders
  for (const [key, value] of Object.entries(builtInParams)) {
    if (value) {
      result = result.replace(new RegExp(`\\{${key}\\}`, 'g'), value);
    }
  }

  // Parse and apply custom parameter mappings from sprk_fetchxmlparams
  if (paramMappings) {
    try {
      const mappings = JSON.parse(paramMappings) as Record<string, string>;
      for (const [key, value] of Object.entries(mappings)) {
        if (typeof value === 'string') {
          result = result.replace(new RegExp(`\\{${key}\\}`, 'g'), value);
        }
      }
    } catch {
      logger.warn('FetchXmlBuilders', 'Failed to parse fetchXmlParams JSON', {
        paramMappings,
      });
    }
  }

  // Log any remaining unresolved placeholders
  const remaining = result.match(/\{[a-zA-Z_]+\}/g);
  if (remaining) {
    logger.warn('FetchXmlBuilders', 'Unresolved placeholders in FetchXML', {
      remaining,
    });
  }

  return result;
}
