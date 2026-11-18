/**
 * Task 084h-4: the note on the writes of `overridesApi.ts` and the plan runner, below the wizard (the wizard's own tests are WizardNote.test.tsx).
 */

import { MAX_NOTE_CHARS, createOverridesApi, noteProblem, noteToSend, notRecorded } from '../services/overridesApi';
import { MAX_REASON_CHARS, executePlan } from '../components/wizards/plan';
import { BASE, jsonResponse } from './testKit';

const REC = '4471AAAA-0000-4000-8000-000000000001';

function api(answer: unknown) {
  const fetchFn = jest.fn(async (_url: string, _init?: RequestInit) => jsonResponse(answer));
  return { fetchFn, client: createOverridesApi(fetchFn as never, BASE) };
}
const bodyOf = (fetchFn: jest.Mock, i = 0) => JSON.parse(String(fetchFn.mock.calls[i][1]?.body)) as Record<string, unknown>;

describe('noteToSend / noteProblem', () => {
  it.each([
    [undefined, undefined],
    [null, undefined],
    ['', undefined],
    ['   ', undefined],
    [' \n\t ', undefined],
    ['  hello  ', 'hello'],
    ['a\nb', 'a\nb'],
  ])('noteToSend(%j) is %j', (input, out) => {
    expect(noteToSend(input as string | null | undefined)).toBe(out);
  });

  it('the limit is the reason\'s 2000, counted after trimming', () => {
    expect(MAX_NOTE_CHARS).toBe(2000);
    expect(MAX_NOTE_CHARS).toBe(MAX_REASON_CHARS);
    expect(noteProblem('x'.repeat(2000))).toBeNull();
    expect(noteProblem(` ${'x'.repeat(2000)} `)).toBeNull();
    expect(noteProblem('x'.repeat(2001))).toMatch(/at most 2000/);
  });

  it.each(['\u0000', '\u0007', '\u001B', '\u007F', '\u0085'])('refuses the control character %j', c => {
    expect(noteProblem(`a${c}b`)).toMatch(/control character/);
  });

  it.each(['\n', '\r', '\t', '\r\n'])('allows %j (newline, carriage return, tab)', c => {
    expect(noteProblem(`a${c}b`)).toBeNull();
  });
});

describe('the three POST bodies and the DELETE', () => {
  it('assign, exclude and change carry note when typed (trimmed) and no note key when blank', async () => {
    const a = api({ id: 'r' });
    await a.client.assignProcedure('matters', REC, { procedureId: 'P', mode: 'FollowLatest', note: ' n ' });
    await a.client.assignProcedure('matters', REC, { procedureId: 'P', mode: 'FollowLatest', note: '  ' });
    await a.client.assignProcedure('matters', REC, { procedureId: 'P', mode: 'FollowLatest' });
    expect(bodyOf(a.fetchFn, 0)).toEqual({ procedureId: 'P', mode: 'FollowLatest', note: 'n' });
    expect(bodyOf(a.fetchFn, 1)).toEqual({ procedureId: 'P', mode: 'FollowLatest' });
    expect(bodyOf(a.fetchFn, 2)).toEqual({ procedureId: 'P', mode: 'FollowLatest' });

    const e = api({ id: 'r' });
    await e.client.excludeRule('matters', REC, { policyId: 'R', reason: 'why', note: 'n' });
    await e.client.excludeRule('matters', REC, { policyId: 'R', reason: 'why', note: '' });
    expect(bodyOf(e.fetchFn, 0)).toEqual({ policyId: 'R', reason: 'why', note: 'n' });
    expect(bodyOf(e.fetchFn, 1)).toEqual({ policyId: 'R', reason: 'why' });

    const c = api({ override: { id: 'r' }, recordRule: {} });
    await c.client.changeRule('matters', REC, { policyId: 'R', reason: 'why', ruleBody: '{}', note: 'n' });
    await c.client.changeRule('matters', REC, { policyId: 'R', reason: 'why', ruleBody: '{}', note: ' ' });
    expect(bodyOf(c.fetchFn, 0)).toEqual({ policyId: 'R', reason: 'why', ruleBody: '{}', note: 'n' });
    expect(bodyOf(c.fetchFn, 1)).toEqual({ policyId: 'R', reason: 'why', ruleBody: '{}' });
  });

  it('DELETE: with a note, a JSON body { note } and a JSON content type; without one, no body and no headers', async () => {
    const { fetchFn, client } = api({ overrideId: 'row' });
    await client.removeOverride('matters', REC, 'row', '  gone  ');
    await client.removeOverride('matters', REC, 'row', '   ');
    await client.removeOverride('matters', REC, 'row');
    const [url, withNote] = fetchFn.mock.calls[0];
    expect(url).toBe(`https://bff.example.com/api/finance/matters/${REC.toLowerCase()}/overrides/row`);
    expect(withNote?.method).toBe('DELETE');
    expect(withNote?.headers).toEqual({ 'Content-Type': 'application/json' });
    expect(JSON.parse(String(withNote?.body))).toEqual({ note: 'gone' });
    for (const i of [1, 2]) {
      const init = fetchFn.mock.calls[i][1];
      expect(init).toEqual({ method: 'DELETE' });
      expect(init?.body).toBeUndefined();
    }
  });
});

describe('executePlan: the note goes on every call, and NotRecorded is read from each answer', () => {
  const fakeApi = (answers: { remove?: unknown; assign?: unknown }) => {
    const calls: Array<{ kind: string; note?: string }> = [];
    return {
      calls,
      api: {
        removeOverride: async (_c: string, _r: string, _o: string, note?: string) => {
          calls.push({ kind: 'remove', note });
          return { ok: true, value: answers.remove ?? {} };
        },
        assignProcedure: async (_c: string, _r: string, b: { note?: string }) => {
          calls.push({ kind: 'assign', note: b.note });
          return { ok: true, value: answers.assign ?? {} };
        },
      } as never,
    };
  };
  const plan = { steps: [{ kind: 'remove' as const, overrideId: 'o' }, { kind: 'assign' as const, procedureId: 'p', mode: 'FollowLatest' as const }] };

  it('sends the same note on both calls of a Pin-to-Follow plan', async () => {
    const f = fakeApi({});
    const run = await executePlan(f.api, 'matters', REC, plan, 'same note');
    expect(run.ok).toBe(true);
    expect(f.calls).toEqual([{ kind: 'remove', note: 'same note' }, { kind: 'assign', note: 'same note' }]);
  });

  it('notRecorded is true only for a NotRecorded answer on any call; Queued, Recorded and a missing member leave it false', async () => {
    const status = async (remove: unknown, assign: unknown) => (await executePlan(fakeApi({ remove, assign }).api, 'matters', REC, plan)).notRecorded;
    expect(await status({ decisionRecord: { status: 'NotRecorded' } }, {})).toBe(true);
    expect(await status({}, { decisionRecord: { status: 'NotRecorded' } })).toBe(true);
    expect(await status({ decisionRecord: { status: 'Queued' } }, { decisionRecord: { status: 'Recorded' } })).toBe(false);
    expect(await status({}, {})).toBe(false);
    expect(await status({ decisionRecord: null }, { decisionRecord: undefined })).toBe(false);
    expect(notRecorded(undefined)).toBe(false);
    expect(notRecorded(null)).toBe(false);
  });
});
