/**
 * What the two search tools answered, mapped onto the props of the Console's `SearchResultsWidget` (spaarkeai-mcp-bff-api-r2 task 035,
 * FR-16). Pure: no React, no host, no clock.
 *
 * The tools' `structuredContent` (task 022, `notes/022-decisions.md`):
 *  - `spaarke_search_documents`: `{ items: [{ id, name, fileType?, documentType?, parentType?, parentName?, snippet?, openLink? }],
 *    limit, offset, hasMore, truncated, nextOffset, warnings? }`;
 *  - `spaarke_find_records`: `{ items: [{ id, name, type, snippet?, referenceNumbers?, openLink? }], limit, offset, hasMore, truncated, nextOffset }`;
 *  - a failure: `{ reasonCode, message, status, correlationId? }` (fixed, user-safe text).
 * Paging (022's contract): `hasMore` means "the page came back full" and `false` is NOT the end of the list. The only continuation is
 * `nextOffset` (null: none is advised). A document search over every record never advises one; the tool tells the model to narrow the search
 * to one matter, project or invoice, and this widget says the same.
 *
 * Gaps against the component's props (`SearchResultItem`: `id, title, excerpt, score, url?`; `SearchResultsData`: `query, results`):
 *  - There is NO score: the tools expose none (ADR-015, 022). `score` is a required number, so this module sets it to 0 and the shell hides the
 *    badge (a "0%" badge would say the hit is irrelevant). See notes/035-decisions.md D-035-score.
 *  - There is no `query` in the result: the shell takes it from the arguments of the call that opened the widget (`toolInput`).
 *  - The component has one text line per hit (`excerpt`), so the hit's file type, matter or record type and reference numbers are written
 *    into it as one plain line, before the snippet.
 */
import type { SearchResultItem } from '../../../../shared/Spaarke.AI.Widgets/src/outputs/output-widgets/SearchResultsWidget';

export const SEARCH_DOCUMENTS_TOOL = 'spaarke_search_documents';
export const FIND_RECORDS_TOOL = 'spaarke_find_records';

/** The index's warning code for a page that may be incomplete (`SearchWarningCode.PartialResults`). */
export const PARTIAL_RESULTS_CODE = 'PARTIAL_RESULTS';

export type SearchKind = 'documents' | 'records';

/** One hit, as the widget keeps it (text fields only; `link` is already checked). */
export interface SearchHit {
  id: string;
  title: string;
  excerpt: string;
  link?: string;
}

/** One successful page of hits. */
export interface SearchPage {
  kind: SearchKind;
  hits: SearchHit[];
  /** The page size the tool used. */
  limit: number;
  /** The offset this page started at (0 for a first page). */
  offset: number;
  /** The offset to ask for next, or null when the tool advised none (the end, a short page, or a search over every record). */
  nextOffset: number | null;
  /** The tool left hits out to keep its result small. */
  truncated: boolean;
  /** The index said this page may be incomplete. */
  partial: boolean;
}

export type ParsedSearchResult = { status: 'page'; page: SearchPage } | { status: 'error'; message: string };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined;
}

/**
 * A link the widget may hand the host: an absolute http(s) URL, or nothing. The tools only ever build the Dataverse record URL, but the
 * widget still refuses any other scheme (a `javascript:` or `data:` value must never reach `openLink`).
 */
export function safeLink(value: unknown): string | undefined {
  const raw = text(value);
  if (!raw) return undefined;
  try {
    const url = new URL(raw);
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.toString() : undefined;
  } catch {
    return undefined;
  }
}

function sentenceCase(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function lineOf(details: Array<string | undefined>, snippet: string | undefined): string {
  const head = details.filter((d): d is string => !!d).join(' · ');
  return [head, snippet].filter(Boolean).join(' — ');
}

function documentHit(item: Record<string, unknown>): SearchHit | undefined {
  const id = text(item.id);
  if (!id) return undefined;
  const parentName = text(item.parentName);
  const parentType = text(item.parentType);
  const parent = parentName ? `in ${parentType ? `${parentType} ` : ''}${parentName}` : undefined;
  return {
    id,
    title: text(item.name) ?? 'Untitled document',
    excerpt: lineOf([text(item.fileType), parent], text(item.snippet)),
    link: safeLink(item.openLink),
  };
}

function recordHit(item: Record<string, unknown>): SearchHit | undefined {
  const id = text(item.id);
  if (!id) return undefined;
  const type = text(item.type);
  const references = Array.isArray(item.referenceNumbers)
    ? item.referenceNumbers.map(text).filter((r): r is string => !!r)
    : [];
  return {
    id,
    title: text(item.name) ?? 'Untitled record',
    excerpt: lineOf(
      [type ? sentenceCase(type) : undefined, references.length ? `ref ${references.join(', ')}` : undefined],
      text(item.snippet)
    ),
    link: safeLink(item.openLink),
  };
}

/** Records carry a `type` ("matter", "project", "invoice"); documents never do (they have `fileType` and `parentType`). */
function kindOf(items: Array<Record<string, unknown>>): SearchKind {
  return items.some(i => typeof i.type === 'string') ? 'records' : 'documents';
}

const COULD_NOT_LOAD = 'The search results could not be loaded.';

/**
 * Reads one tool answer: the `structuredContent` the host delivered as the opening result, or a later call's. `failure` carries what the
 * transport knows about a call that failed (its fixed text).
 */
export function parseSearchResult(
  structured: unknown,
  failure?: { isError?: boolean; message?: string }
): ParsedSearchResult {
  if (isRecord(structured) && Array.isArray(structured.items) && failure?.isError !== true) {
    const items = structured.items.filter(isRecord);
    const kind = kindOf(items);
    const seen = new Set<string>();
    const hits: SearchHit[] = [];
    for (const item of items) {
      const hit = kind === 'records' ? recordHit(item) : documentHit(item);
      if (hit && !seen.has(hit.id)) {
        seen.add(hit.id);
        hits.push(hit);
      }
    }
    const next = structured.nextOffset;
    const warnings = Array.isArray(structured.warnings) ? structured.warnings : [];
    return {
      status: 'page',
      page: {
        kind,
        hits,
        limit:
          typeof structured.limit === 'number' && Number.isInteger(structured.limit) && structured.limit > 0
            ? structured.limit
            : hits.length,
        offset:
          typeof structured.offset === 'number' && Number.isInteger(structured.offset) && structured.offset > 0
            ? structured.offset
            : 0,
        nextOffset: typeof next === 'number' && Number.isInteger(next) && next > 0 ? next : null,
        truncated: structured.truncated === true,
        partial: warnings.includes(PARTIAL_RESULTS_CODE),
      },
    };
  }

  // A failure the tool described (its message says what to fix, never record text), or the transport's fixed text.
  const message = (isRecord(structured) ? text(structured.message) : undefined) ?? text(failure?.message);
  return { status: 'error', message: message ?? COULD_NOT_LOAD };
}

/** The component's props for the hits shown so far. `score` is 0 and never shown (see the file header). */
export function toResultItems(hits: SearchHit[]): SearchResultItem[] {
  return hits.map(hit => ({ id: hit.id, title: hit.title, excerpt: hit.excerpt, score: 0, url: hit.link }));
}

/** The text the component prints after "Search results for": the call's own query, or a neutral phrase when the host sent no arguments. */
export function queryOf(toolInput: Record<string, unknown> | undefined): string {
  return text(toolInput?.query) ?? 'your search';
}

/** Which notices the shown pages need (each a fixed sentence; the widget never invents a count). */
export interface PageNotices {
  /** The tool left hits out, or the index may be incomplete. */
  incomplete: string[];
  /** Why the list ends here, when the tool advised no continuation. Empty when a "Load more" is offered, or nothing was found. */
  ending: string | null;
}

export const NARROW_ADVICE = 'There may be more matches. To see more, search inside one matter, project or invoice.';
export const MAY_BE_MORE = 'This may not be every match.';
export const ASK_FOR_MORE = 'There may be more matches. Ask for the next page in the chat.';
export const NO_MORE_AFTER_THESE = 'No more matches after these.';
export const PAGING_LIMIT = 'That is as far as paging goes. For more, narrow the search with a more specific query.';

/** The largest offset the tools accept (`SearchToolSupport.MaxOffset`, REST's own bound). The widget never asks for a page beyond it. */
export const MAX_OFFSET = 1000;

/**
 * What to tell the reader about the newest page. `hasContinuation` is whether the widget offers "Load more" (a `nextOffset` AND the call's
 * arguments, which a further call needs). A list the tool advised no offset for says why: a full or cut page over every record needs a
 * narrower search; a short page may still hide matches (rows are checked for access after ranking), so it is never called the end.
 * An empty page is the end of a list the reader paged into (`pageCount` > 1) or one that opened past its start (`page.offset` > 0), and says
 * so; an empty first page says nothing here (the component says "No results found.").
 */
export function noticesFor(
  page: SearchPage,
  anyPartial: boolean,
  hasContinuation: boolean,
  pageCount = 1
): PageNotices {
  const incomplete: string[] = [];
  if (page.truncated) incomplete.push('Some results were left out to keep this list short.');
  if (anyPartial) incomplete.push('These results may be incomplete.');

  if (page.hits.length === 0) {
    if (pageCount > 1) return { incomplete, ending: NO_MORE_AFTER_THESE };
    if (page.offset > 0) return { incomplete, ending: `No more matches after the first ${page.offset}.` };
    return { incomplete, ending: null };
  }
  if (hasContinuation) return { incomplete, ending: null };
  // An advised offset above the tools' own bound would be refused: say to narrow the search instead of pointing at a page that cannot be asked for.
  if (page.nextOffset !== null && page.nextOffset > MAX_OFFSET) return { incomplete, ending: PAGING_LIMIT };
  // The tool advised a next page but the widget cannot ask for it (the host sent no arguments): point at the chat instead of a dead button.
  if (page.nextOffset !== null) return { incomplete, ending: ASK_FOR_MORE };
  const full = page.hits.length >= page.limit || page.truncated;
  // A full or cut page with no offset is either a document search over every record (always from offset 0) or a list the tools stopped
  // paging at their bound (they advise no offset above MAX_OFFSET). Only the first needs "search inside one record"; the second may already be.
  if (full && page.offset + page.limit > MAX_OFFSET) return { incomplete, ending: PAGING_LIMIT };
  return { incomplete, ending: full ? NARROW_ADVICE : MAY_BE_MORE };
}

/** The arguments of the next call: the call's own arguments for this tool, with the new offset. Null when the widget cannot repeat the call. */
export function nextCallArguments(
  kind: SearchKind,
  toolInput: Record<string, unknown> | undefined,
  nextOffset: number
): { tool: string; args: Record<string, unknown> } | null {
  const query = text(toolInput?.query);
  if (!query) return null;
  // The tools refuse an offset above MAX_OFFSET (INVALID_OFFSET): never ask for one, whatever the result advised.
  if (nextOffset > MAX_OFFSET) return null;

  const args: Record<string, unknown> = { query, offset: nextOffset };
  if (typeof toolInput?.limit === 'number') args.limit = toolInput.limit;
  if (kind === 'documents') {
    const entityType = text(toolInput?.entityType);
    const entityId = text(toolInput?.entityId);
    if (entityType) args.entityType = entityType;
    if (entityId) args.entityId = entityId;
    return { tool: SEARCH_DOCUMENTS_TOOL, args };
  }
  if (Array.isArray(toolInput?.recordTypes)) args.recordTypes = toolInput.recordTypes;
  return { tool: FIND_RECORDS_TOOL, args };
}
