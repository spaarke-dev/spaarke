/**
 * The wizard's inline type-ahead for Person and Lookup parameters, over MCP (spaarkeai-mcp-bff-api-r1 task 032, FR-13).
 *
 * The Console opens the platform's side-pane lookup for these fields. A widget in a host frame cannot (no `Xrm`, no `openLookup`), so the
 * shared field keeps its inline search and this is its `lookup`: it asks `spaarke_search_decision_lookup`, which finds the candidates for the
 * parameter of ONE Work Item's plan as the signed-in user (the tool re-checks the Signal's access, resolves the plan and reads Dataverse on
 * every call, so a call is not free and the same budget is shared with the commit: 60 a minute).
 *
 * Therefore a search is made only
 *  - after the user has stopped typing for `LOOKUP_DEBOUNCE_MS` (300 ms): the debounce here is the guarantee; the shared field debounces
 *    too, but it also searches at once on focus and from its search button, and neither of those may reach the tool sooner;
 *  - for a query of at least `LOOKUP_MIN_CHARS` (2) characters: an empty or one-letter query would ask the server for everything, so it
 *    finds nothing and asks nothing. (The Console's empty-query "browse the candidates" is therefore not offered here.)
 * A search superseded by a newer one for the same field is dropped without a call and resolves with what the NEWER search resolves with
 * (never with nothing: the field applies answers as they arrive, so a superseded search answering `[]` after the newer one would wipe its
 * candidates). The same goes for a superseded search that was already asked and is answered late. The shared field also ignores an answer
 * that is not the newest search's; this keeps the rule true for anything calling the type-ahead directly. `cancel()` drops whatever is
 * waiting (the wizard closed).
 *
 * A failure rejects with the tool's message. The field words it ("Search could not be completed. Try again.", `failureText`) instead of
 * "No results found", and asks for at least `minChars` letters, saying so instead of "No results found" for a shorter one.
 */
import type { ILookupItem } from '@spaarke/ui-components/types/LookupTypes';
import type { DecisionLookupSearch } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardTypes';
import type { ResponseFetchFn } from '@spaarke/ui-components/utils/fetchTypes';
import { LOOKUP_ROUTE } from './wizardFetch';

export const LOOKUP_DEBOUNCE_MS = 300;
export const LOOKUP_MIN_CHARS = 2;
/** What the field says when a search fails (throttled, offline, refused): never "No results found", which would read as "there are none". */
export const LOOKUP_FAILURE_TEXT = 'Search could not be completed. Try again.';
/** The tool's limit on the typed text. */
export const LOOKUP_MAX_CHARS = 100;

export type LookupTypeAhead = DecisionLookupSearch & {
  /** Drops every search that is waiting out its debounce (each resolves with no candidates). */
  cancel: () => void;
};

interface Waiting {
  timer: ReturnType<typeof setTimeout>;
  settle: (items: ILookupItem[] | Promise<ILookupItem[]>) => void;
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/** The tool's `candidates` as the shared field's items; a candidate without an id or a name is skipped, never invented. */
function toItems(body: unknown, person: boolean): ILookupItem[] {
  if (!isRecord(body) || !Array.isArray(body.candidates)) return [];
  const items: ILookupItem[] = [];
  for (const candidate of body.candidates) {
    if (!isRecord(candidate)) continue;
    const { id, name, email } = candidate;
    if (typeof id !== 'string' || id === '' || typeof name !== 'string' || name === '') continue;
    items.push({
      id,
      name,
      ...(typeof email === 'string' && email !== '' ? { email } : {}),
      ...(person ? { entityType: 'contact' as const } : {}),
    });
  }
  return items;
}

async function detailOf(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json();
    if (isRecord(body) && typeof body.detail === 'string' && body.detail) return body.detail;
  } catch {
    // fall through to the generic sentence
  }
  return 'The search could not be completed.';
}

/** `search` is the wizard bridge's fetch (a `ResponseFetchFn` over the lookup tool); `debounceMs` exists so a test need not wait. */
export function createLookupTypeAhead(
  search: ResponseFetchFn,
  debounceMs: number = LOOKUP_DEBOUNCE_MS
): LookupTypeAhead {
  // One waiting search per field of one item: a new one replaces it.
  const waiting = new Map<string, Waiting>();
  // The newest request of each field (a counter), so an answer that arrives after a newer request is not used as the field's answer.
  const latest = new Map<string, number>();
  // The newest request's own answer, which a superseded request resolves with.
  const newest = new Map<string, Promise<ILookupItem[]>>();

  const drop = (key: string) => {
    const previous = waiting.get(key);
    if (!previous) return;
    clearTimeout(previous.timer);
    waiting.delete(key);
    previous.settle([]);
  };

  const base = (request => {
    const signalId = request.signalId;
    const query = request.query.trim().slice(0, LOOKUP_MAX_CHARS);
    const key = `${signalId ?? ''}|${request.parameterCode}`;
    // A newer request for the field supersedes the waiting one, including a request too short to search (the user deleted text),
    // and one already asked: that one resolves with the newer request's answer (`latest` has moved on).
    const previous = waiting.get(key);
    if (previous) {
      clearTimeout(previous.timer);
      waiting.delete(key);
    }
    const mine = (latest.get(key) ?? 0) + 1;
    latest.set(key, mine);
    const answer =
      !signalId || query.length < LOOKUP_MIN_CHARS
        ? Promise.resolve<ILookupItem[]>([])
        : ask(signalId, query, key, mine, request);
    newest.set(key, answer);
    previous?.settle(answer);
    return answer;
  }) as DecisionLookupSearch;

  /** The search itself: waits out the debounce, then asks the tool. */
  function ask(
    signalId: string,
    query: string,
    key: string,
    mine: number,
    request: Parameters<DecisionLookupSearch>[0]
  ): Promise<ILookupItem[]> {
    return new Promise<ILookupItem[]>((resolve, reject) => {
      const timer = setTimeout(() => {
        waiting.delete(key);
        const params = new URLSearchParams({ signalId, parameterCode: request.parameterCode, query });
        search(`${LOOKUP_ROUTE}?${params.toString()}`, { method: 'GET' }).then(
          async response => {
            if (!response.ok) {
              reject(new Error(await detailOf(response)));
              return;
            }
            const body: unknown = await response.json();
            // Superseded while the tool was answering: say what the newer search says (nothing, once `cancel()` has run).
            resolve(latest.get(key) === mine ? toItems(body, request.kind === 'Person') : (newest.get(key) ?? []));
          },
          () => reject(new Error('The search could not be completed.'))
        );
      }, debounceMs);
      waiting.set(key, { timer, settle: resolve });
    });
  }

  const lookup = base as LookupTypeAhead;
  lookup.minChars = LOOKUP_MIN_CHARS;
  lookup.failureText = LOOKUP_FAILURE_TEXT;
  lookup.cancel = () => {
    for (const key of [...waiting.keys()]) drop(key);
    latest.clear(); // an answer still on its way is dropped when it arrives
    newest.clear();
  };
  return lookup;
}
