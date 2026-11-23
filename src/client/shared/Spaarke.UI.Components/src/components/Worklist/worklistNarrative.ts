/**
 * worklistNarrative - the Briefing narrative over the worklist (task 062; FR-31, FR-34; W-10 as amended).
 *
 * The narrative is prose WRITTEN BY THE ASSISTANT over facts the list itself holds: how many items are to decide and how many are to do.
 * The two lanes are the only categories sent. Past due is a SUBSET of the Do lane, not a third lane (DI-161), so it is never a
 * category: the one summary line is built from the counts here (`worklistSummaryLine`) and the Assistant writes the bullets. It never
 * decides membership or order (the Signal read route owns both, D-42) and it SUMMARISES Do
 * volume rather than listing it, so the request below carries counts only: no record name, no date and no item at all. A narrative that
 * is given no name cannot name an item that is not on the list, and the server's entity-name scrubber still checks the text it gets back.
 *
 * Know items are never Work Items (row-contract requirement 4), so nothing here reads or counts them.
 *
 * Pure: no React, no fetching. `worklistService.narrate` sends the request; `WorklistWidget` shows the text while no filter is on.
 */

import { daysBetweenLocalMidnight, parseDueDate } from '../../utils/dateLocal';
import type { WorklistData } from './worklistService';

/** One category count, the wire shape of the BFF's `NotificationCategoryDto`. */
export interface WorklistNarrativeCategory {
  name: string;
  count: number;
  unreadCount: number;
}

/**
 * The body of `POST /api/ai/daily-briefing/narrate` for the worklist: counts only. `tldrFacts` is the ground truth the model may
 * assert; it holds no date and no record name. `priorityItems` and `channels` are empty on purpose: nothing is listed.
 */
export interface WorklistNarrativeRequest {
  categories: WorklistNarrativeCategory[];
  priorityItems: [];
  totalNotificationCount: number;
  channels: [];
  tldrFacts: {
    totalNotificationCount: number;
    categoryCounts: WorklistNarrativeCategory[];
    priorityItemCount: 0;
    keyDates: [];
    recordNames: [];
  };
}

/** The narrative as shown: one sentence and a few short bullets. No item id, no link, no recommended action. */
export interface WorklistNarrativeText {
  summary: string;
  keyTakeaways: string[];
}

export const DECIDE_CATEGORY = 'Items to decide';
export const DO_CATEGORY = 'Items to do';

const count = (name: string, n: number): WorklistNarrativeCategory => ({ name, count: n, unreadCount: n });

/**
 * The request for the list as read, or `null` when the list is empty (nothing to say, so no call is made). The counts are the
 * lanes' own: the same items the count cards describe. A Do item is past due when its date-only due date is before `today`'s
 * local calendar day (the same comparison the row's due state uses).
 */
function pastDueCount(doItems: readonly { dueDate?: string | null }[], today: Date): number {
  return doItems.filter(i => {
    const due = parseDueDate(i.dueDate);
    return due !== null && daysBetweenLocalMidnight(today, due) < 0;
  }).length;
}

/**
 * The one summary line, from the lanes' own counts (DI-161): "241 Work Items: 192 to decide and 49 to do. 49 of the 49 to do are past
 * due." Two lanes at most, past due said of the Do items only, and the items are Work Items (never "notifications"). `null` for an empty
 * list. Deterministic, so it cannot say a count the list does not hold; the Assistant's own summary is not shown.
 */
export function worklistSummaryLine(data: WorklistData | undefined, today: Date): string | null {
  if (!data) return null;
  const decide = data.Decide.reduce((n, g) => n + g.items.length, 0);
  const doItems = data.Do.flatMap(g => g.items);
  const total = decide + doItems.length;
  if (total === 0) return null;
  const items = `${total} Work ${total === 1 ? 'Item' : 'Items'}`;
  const lanes = [
    decide > 0 ? `${decide} to decide` : null,
    doItems.length > 0 ? `${doItems.length} to do` : null,
  ].filter(Boolean);
  let line = `${items}: ${lanes.join(' and ')}.`;
  const pastDue = pastDueCount(doItems, today);
  if (pastDue > 0) {
    line +=
      pastDue === doItems.length
        ? doItems.length === 1
          ? ' It is past due.'
          : ' All of the items to do are past due.'
        : ` ${pastDue} of the ${doItems.length} to do ${pastDue === 1 ? 'is' : 'are'} past due.`;
  }
  return line;
}

export function buildWorklistNarrativeRequest(
  data: WorklistData | undefined,
  today: Date
): WorklistNarrativeRequest | null {
  if (!data) return null;
  const decide = data.Decide.reduce((n, g) => n + g.items.length, 0);
  const doItems = data.Do.flatMap(g => g.items);
  if (decide + doItems.length === 0) return null;

  // One category per lane, never more categories than lanes: past due is a subset of Do, carried by the summary line, not sent.
  const categories = [count(DECIDE_CATEGORY, decide), count(DO_CATEGORY, doItems.length)];
  const total = decide + doItems.length;
  return {
    categories,
    priorityItems: [],
    totalNotificationCount: total,
    channels: [],
    tldrFacts: {
      totalNotificationCount: total,
      categoryCounts: categories,
      priorityItemCount: 0,
      keyDates: [],
      recordNames: [],
    },
  };
}

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null;

/**
 * The narrative from a `/narrate` answer, or `null` to show none. FAIL CLOSED: an answer whose entity-name scrub removed any term
 * (the model named something it was not given) is dropped whole, as is one with no summary. A legacy `topAction`, any `itemRefs`
 * and every channel narrative are ignored: this surface shows one summary and bullets, and nothing that picks or links an item.
 */
export function acceptNarrative(body: unknown): WorklistNarrativeText | null {
  if (!isObject(body)) return null;
  const meta = body._validationMetadata;
  if (isObject(meta) && Array.isArray(meta.removedTerms) && meta.removedTerms.length > 0) return null;
  const tldr = body.tldr;
  if (!isObject(tldr)) return null;
  const summary = typeof tldr.summary === 'string' ? tldr.summary.trim() : '';
  if (!summary) return null;
  const keyTakeaways = Array.isArray(tldr.keyTakeaways)
    ? tldr.keyTakeaways.filter((t): t is string => typeof t === 'string' && t.trim().length > 0).map(t => t.trim())
    : [];
  return { summary, keyTakeaways };
}

// A narrative is reused for the same counts for a few minutes, so reopening the worklist does not call the model again.
const CACHE_TTL_MS = 10 * 60 * 1000;
const cache = new Map<string, { at: number; text: WorklistNarrativeText }>();

export function cachedNarrative(key: string, now: number = Date.now()): WorklistNarrativeText | null {
  const hit = cache.get(key);
  if (!hit) return null;
  if (now - hit.at > CACHE_TTL_MS) {
    cache.delete(key);
    return null;
  }
  return hit.text;
}

export function rememberNarrative(key: string, text: WorklistNarrativeText, now: number = Date.now()): void {
  cache.set(key, { at: now, text });
}

/** Empties the cache (tests; and a host that signs the user out). */
export function clearNarrativeCache(): void {
  cache.clear();
}
