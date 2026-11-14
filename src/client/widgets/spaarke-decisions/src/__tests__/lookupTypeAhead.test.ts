/**
 * The wizard's inline type-ahead (task 032, FR-13). The tool behind it is rate-limited (60 a minute, shared with the commit) and re-checks
 * access on every call, so the contract is about WHEN it asks:
 *  - nothing before the debounce (300 ms) has passed; one call after it, for the last text typed
 *  - negative: fewer than 2 characters never asks, and a search without a Work Item never asks
 *  - a superseded search is dropped without a call and an answer that arrives after a newer request is dropped
 *  - candidates become the shared field's items; a failure rejects with the tool's message (the field shows no matches for it)
 */
import {
  createLookupTypeAhead,
  LOOKUP_DEBOUNCE_MS,
  LOOKUP_MAX_CHARS,
  LOOKUP_MIN_CHARS,
} from '../wizard/lookupTypeAhead';
import type { DecisionLookupRequest } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardTypes';

const SIGNAL = '50000001-0000-4000-8000-000000000000';

const request = (over: Partial<DecisionLookupRequest> = {}): DecisionLookupRequest => ({
  kind: 'Lookup',
  entity: 'sprk_budget',
  parameterCode: 'budget',
  query: 'fy26',
  core: { recordType: 'sprk_matter', recordId: 'a1' },
  signalId: SIGNAL,
  ...over,
});

interface Answer {
  ok?: boolean;
  body?: unknown;
}
const answer = ({ ok = true, body = { candidates: [] } }: Answer = {}) =>
  ({ ok, json: async () => body }) as unknown as Response;

/** A search double that answers at once, and records the addresses it was asked. */
function immediate(next: () => Response = () => answer()) {
  const search = jest.fn(async (_url: string, _init?: RequestInit) => next());
  return search;
}

const params = (url: string) => new URL(url, 'http://x.test').searchParams;

beforeEach(() => jest.useFakeTimers());
afterEach(() => jest.useRealTimers());

describe('when it asks', () => {
  it('is 300 ms and 2 characters, the limits the contract names', () => {
    expect(LOOKUP_DEBOUNCE_MS).toBe(300);
    expect(LOOKUP_MIN_CHARS).toBe(2);
  });

  it('makes no call before the debounce has passed, and exactly one at it', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    const pending = lookup(request());

    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS - 1);
    expect(search).not.toHaveBeenCalled();

    await jest.advanceTimersByTimeAsync(1);
    expect(search).toHaveBeenCalledTimes(1);
    await expect(pending).resolves.toEqual([]);
  });

  it('asks the lookup address with the Work Item, the parameter and the text', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    void lookup(request({ query: 'fy26 lit', parameterCode: 'budget' }));
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);

    const [url, init] = search.mock.calls[0];
    expect(url.startsWith('/api/v1/decision-lookup?')).toBe(true);
    expect(Object.fromEntries(params(url))).toEqual({ signalId: SIGNAL, parameterCode: 'budget', query: 'fy26 lit' });
    expect(init).toMatchObject({ method: 'GET' });
  });

  it('is a trailing debounce: typing on keeps pushing the call back, and only the last text is asked', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    const first = lookup(request({ query: 'ac' }));
    await jest.advanceTimersByTimeAsync(200);
    const second = lookup(request({ query: 'acm' }));
    await jest.advanceTimersByTimeAsync(200);
    const third = lookup(request({ query: 'acme' }));
    await jest.advanceTimersByTimeAsync(299);
    expect(search).not.toHaveBeenCalled();
    await jest.advanceTimersByTimeAsync(1);

    expect(search).toHaveBeenCalledTimes(1);
    expect(params(search.mock.calls[0][0]).get('query')).toBe('acme');
    // The two superseded ones were dropped without a call and settled with no candidates.
    await expect(first).resolves.toEqual([]);
    await expect(second).resolves.toEqual([]);
    await expect(third).resolves.toEqual([]);
  });

  it.each([[''], [' '], ['a'], [' a ']])(
    'never asks for %p: fewer than 2 characters finds nothing and asks nothing',
    async query => {
      const search = immediate();
      const lookup = createLookupTypeAhead(search);
      await expect(lookup(request({ query }))).resolves.toEqual([]);
      await jest.advanceTimersByTimeAsync(5 * LOOKUP_DEBOUNCE_MS);
      expect(search).not.toHaveBeenCalled();
    }
  );

  it('shortening the text below 2 characters drops the search that was waiting', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    const waiting = lookup(request({ query: 'acme' }));
    await jest.advanceTimersByTimeAsync(100);
    await expect(lookup(request({ query: 'a' }))).resolves.toEqual([]);
    await jest.advanceTimersByTimeAsync(5 * LOOKUP_DEBOUNCE_MS);
    expect(search).not.toHaveBeenCalled();
    await expect(waiting).resolves.toEqual([]);
  });

  it('never asks without the Work Item the tool is scoped by', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    await expect(lookup(request({ signalId: undefined }))).resolves.toEqual([]);
    await jest.advanceTimersByTimeAsync(5 * LOOKUP_DEBOUNCE_MS);
    expect(search).not.toHaveBeenCalled();
  });

  it('trims the text, and sends no more than the tool accepts', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    void lookup(request({ query: `  ${'x'.repeat(LOOKUP_MAX_CHARS + 50)}  ` }));
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    expect(params(search.mock.calls[0][0]).get('query')).toBe('x'.repeat(LOOKUP_MAX_CHARS));
  });

  it('keeps fields apart: typing in one never delays or drops another', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    void lookup(request({ parameterCode: 'budget', query: 'fy26' }));
    void lookup(request({ parameterCode: 'outsideFirm', query: 'acme' }));
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    expect(search.mock.calls.map(c => params(c[0]).get('parameterCode')).sort()).toEqual(['budget', 'outsideFirm']);
  });

  it('cancel drops what is waiting without a call (the wizard closed)', async () => {
    const search = immediate();
    const lookup = createLookupTypeAhead(search);
    const waiting = lookup(request());
    lookup.cancel();
    await expect(waiting).resolves.toEqual([]);
    await jest.advanceTimersByTimeAsync(5 * LOOKUP_DEBOUNCE_MS);
    expect(search).not.toHaveBeenCalled();
  });

  it('cancel also drops an answer that was already on its way', async () => {
    let finish!: (r: Response) => void;
    const search = jest.fn(() => new Promise<Response>(resolve => (finish = resolve)));
    const lookup = createLookupTypeAhead(search);
    const inFlight = lookup(request());
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    expect(search).toHaveBeenCalledTimes(1);

    lookup.cancel();
    finish(answer({ body: { candidates: [{ id: 'b1', name: 'FY26 budget' }] } }));
    await expect(inFlight).resolves.toEqual([]);
  });
});

describe('a late answer never lands over a newer one', () => {
  it('drops the answer to the first request when a newer one was made after it was sent', async () => {
    const finishers: Array<(r: Response) => void> = [];
    const search = jest.fn(() => new Promise<Response>(resolve => finishers.push(resolve)));
    const lookup = createLookupTypeAhead(search);

    const older = lookup(request({ query: 'fy' }));
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    const newer = lookup(request({ query: 'fy26' }));
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    expect(search).toHaveBeenCalledTimes(2);

    // The newer answer comes first, then the older one: the older is dropped.
    finishers[1](answer({ body: { candidates: [{ id: 'b2', name: 'FY26 budget' }] } }));
    finishers[0](answer({ body: { candidates: [{ id: 'b1', name: 'FY25 budget' }] } }));
    await expect(newer).resolves.toEqual([{ id: 'b2', name: 'FY26 budget' }]);
    await expect(older).resolves.toEqual([]);
  });
});

describe('the answer', () => {
  it('turns candidates into the field items, with the email a person has and the contact type for a Person', async () => {
    const body = {
      candidates: [
        { id: 'c1', name: 'Dana Whitfield', email: 'dana@firm.example' },
        { id: 'c2', name: 'No Email' },
      ],
    };
    const lookup = createLookupTypeAhead(immediate(() => answer({ body })));
    const people = lookup(request({ kind: 'Person', entity: null, parameterCode: 'assignee', query: 'dana' }));
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    await expect(people).resolves.toEqual([
      { id: 'c1', name: 'Dana Whitfield', email: 'dana@firm.example', entityType: 'contact' },
      { id: 'c2', name: 'No Email', entityType: 'contact' },
    ]);

    const lookups = lookup(request({ query: 'fy26' }));
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    await expect(lookups).resolves.toEqual([
      { id: 'c1', name: 'Dana Whitfield', email: 'dana@firm.example' },
      { id: 'c2', name: 'No Email' },
    ]);
  });

  it('skips a candidate with no id or no name, and an answer that is not a candidate list is no matches', async () => {
    const body = { candidates: [{ id: '', name: 'x' }, { id: 'c1' }, 'nope', null, { id: 'c3', name: 'Real' }] };
    const lookup = createLookupTypeAhead(immediate(() => answer({ body })));
    const found = lookup(request());
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    await expect(found).resolves.toEqual([{ id: 'c3', name: 'Real' }]);

    const odd = createLookupTypeAhead(immediate(() => answer({ body: { somethingElse: true } })));
    const none = odd(request());
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    await expect(none).resolves.toEqual([]);
  });

  it("rejects with the tool's own message when it refused", async () => {
    const refusal = answer({ ok: false, body: { detail: 'Too many searches. Wait a moment.' } });
    const lookup = createLookupTypeAhead(immediate(() => refusal));
    const failed = lookup(request());
    const settled = expect(failed).rejects.toThrow('Too many searches. Wait a moment.');
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    await settled;
  });

  it('rejects with a plain sentence when the refusal has no message, and when the host did not answer at all', async () => {
    const bare = createLookupTypeAhead(
      immediate(
        () =>
          ({
            ok: false,
            json: async () => {
              throw new Error('no body');
            },
          }) as unknown as Response
      )
    );
    const first = bare(request());
    const firstSettled = expect(first).rejects.toThrow('The search could not be completed.');
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    await firstSettled;

    const gone = createLookupTypeAhead(
      jest.fn(async () => {
        throw new Error('host closed');
      })
    );
    const second = gone(request());
    const secondSettled = expect(second).rejects.toThrow('The search could not be completed.');
    await jest.advanceTimersByTimeAsync(LOOKUP_DEBOUNCE_MS);
    await secondSettled;
  });
});
