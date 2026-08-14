/**
 * urlParse — pure MDA URL-shape parser for "+ Add bookmark"
 * (spaarke-side-pane-navigation-history-r1 task 051, spec FR-08 / OQ-7).
 *
 * Parses a pasted/typed string into a discriminated {@link ParsedBookmarkInput}:
 * an MDA record URL (`etn` + `id` query params) -> {@link ParsedRecordTarget};
 * an MDA entitylist/view URL (`viewid` query param) -> {@link ParsedViewTarget};
 * any other syntactically-valid `http(s)` URL -> {@link ParsedWeblinkTarget}
 * (raw fallback, OQ-7); anything that is not a URL at all -> {@link ParsedReject}
 * with a friendly reason. `bookmarkService.ts` (task 051) is the only caller —
 * this module has NO `Xrm` dependency and performs no I/O, so it is safe to
 * unit-test in isolation (`__tests__/urlParse.test.ts`).
 *
 * MDA URL param shapes this parser targets (documented Dataverse Client API
 * `main.aspx` deep-link params — see
 * https://learn.microsoft.com/power-apps/user/url-parameters):
 *   - Record:      `?...&pagetype=entityrecord&etn={logicalName}&id={guid}`
 *   - Entity list: `?...&pagetype=entitylist&etn={logicalName}&viewid={guid}[&viewtype=...]`
 * `viewid` is treated as the discriminating signal for the view branch (not
 * `pagetype` alone) — a URL that carries `viewid` is unambiguously a
 * saved-view deep link even if `pagetype` is missing/misspelled; `etn`+`id`
 * without `viewid` is treated as the record branch. Anything else
 * (an entitylist URL with no `viewid`, an `entityrecord` URL missing `id`,
 * or any other Dataverse-looking-but-incomplete URL) falls through to the
 * `weblink` branch rather than being rejected — it IS a valid, storable
 * `http(s)` URL, just not one this parser can resolve to a labeled logical
 * target (see the module's decision-order comment on {@link parseBookmarkInput}).
 *
 * Both `?query` and `#fragment` param placement are checked defensively
 * (`collectParams`) — most MDA deep links place params in the query string,
 * but this parser also reads a `#`-prefixed fragment containing `key=value`
 * pairs (with or without its own leading `?`) so a URL copied from a
 * hash-routed context still parses. Query-string values win over
 * fragment-supplied values on key collision.
 *
 * GUID normalization (`etn`/`id`/`viewid`) reuses
 * `navItemRepository.ts`'s exported {@link normalizeGuid} (strip braces,
 * lowercase) — a pasted URL may carry a `%7B...%7D`-encoded, uppercase GUID
 * (`URLSearchParams` decodes the `%7B`/`%7D` automatically), so this handles
 * that edge case without a second implementation.
 *
 * @see bookmarkService.ts (task 051) — the only caller; maps a `ParsedBookmarkInput` to a `sprk_navitem` write
 * @see navItemRepository.ts — `NavItemPageType`/`normalizeGuid` reused here rather than re-declared
 */

import { NavItemPageType, normalizeGuid } from '@spaarke/ui-components/services/navigator/navItemRepository';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** An MDA record deep link (`etn` + `id`) — a labeled, security-trimmable record target. */
export interface ParsedRecordTarget {
  kind: 'record';
  etn: string;
  id: string;
  /** Always {@link NavItemPageType.EntityRecord}. */
  pageType: number;
}

/** An MDA entitylist/saved-view deep link (`viewid`, optionally scoped to `etn`). */
export interface ParsedViewTarget {
  kind: 'view';
  etn?: string;
  viewId: string;
}

/** Any other syntactically-valid `http(s)` URL — stored raw, opens in a new tab. */
export interface ParsedWeblinkTarget {
  kind: 'weblink';
  url: string;
}

/** The input string is not a URL at all — no row should be created. */
export interface ParsedReject {
  kind: 'reject';
  /** Friendly, user-facing rejection message — safe to render directly. */
  reason: string;
}

export type ParsedBookmarkInput = ParsedRecordTarget | ParsedViewTarget | ParsedWeblinkTarget | ParsedReject;

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

function tryParseUrl(input: string): URL | null {
  try {
    return new URL(input);
  } catch {
    return null;
  }
}

/**
 * Merge query params from `url.search` AND (defensively) a `#`-fragment that
 * itself carries `key=value` pairs — see module docblock. Query-string values
 * take precedence on key collision (checked first, fragment only fills gaps).
 */
function collectParams(url: URL): URLSearchParams {
  const merged = new URLSearchParams(url.search);

  const hash = url.hash.startsWith('#') ? url.hash.slice(1) : url.hash;
  if (hash) {
    const queryPart = hash.includes('?') ? hash.slice(hash.indexOf('?') + 1) : hash;
    if (queryPart.includes('=')) {
      const hashParams = new URLSearchParams(queryPart);
      hashParams.forEach((value, key) => {
        if (!merged.has(key)) merged.set(key, value);
      });
    }
  }

  return merged;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Parse a pasted/typed string per the module docblock's decision order:
 *   1. Not a syntactically valid URL, or not `http(s)` -> {@link ParsedReject}.
 *   2. `viewid` param present -> {@link ParsedViewTarget} (`etn` carried if present).
 *   3. `etn` + `id` params both present -> {@link ParsedRecordTarget}.
 *   4. Otherwise -> {@link ParsedWeblinkTarget} (raw URL fallback, OQ-7).
 */
export function parseBookmarkInput(rawInput: string): ParsedBookmarkInput {
  const input = rawInput.trim();
  if (!input) {
    return { kind: 'reject', reason: 'Enter a URL to bookmark.' };
  }

  const url = tryParseUrl(input);
  if (!url) {
    return {
      kind: 'reject',
      reason: "That doesn't look like a URL. Paste a page link to bookmark it.",
    };
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    return { kind: 'reject', reason: 'Only http(s) links can be bookmarked.' };
  }

  const params = collectParams(url);
  const etn = params.get('etn')?.trim().toLowerCase() || undefined;
  const id = params.get('id')?.trim() || undefined;
  const viewId = params.get('viewid')?.trim() || undefined;

  if (viewId) {
    return {
      kind: 'view',
      etn,
      viewId: normalizeGuid(viewId),
    };
  }

  if (etn && id) {
    return {
      kind: 'record',
      etn,
      id: normalizeGuid(id),
      pageType: NavItemPageType.EntityRecord,
    };
  }

  return { kind: 'weblink', url: url.toString() };
}
