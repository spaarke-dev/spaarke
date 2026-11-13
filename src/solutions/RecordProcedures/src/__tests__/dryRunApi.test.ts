/**
 * Task 084d-4: the dry-run client. The request each plan maps to (every kind, and the two-call replacements), the body and route, the
 * reader, and the plans the wizards actually make (none of them lacks a dry-run equivalent).
 */

import { createDryRunApi, dryRunBody, dryRunRequestOf, dryRunUrl, readDryRunView, requestFirmOf, type DryRunRequest } from '../services/dryRunApi';
import { planProcedure, planRule, procedureChoices, type Plan, type ProcedureChoice, type RuleChoice } from '../components/wizards/plan';
import { BASE, jsonResponse, problem } from './testKit';
import { FENWICK, MATTER_ID, matterProcedures, matterRules, projectProcedures, projectRules, waProcedures, waRules } from './fixtures';

const map = (plan: Plan) => dryRunRequestOf(plan);

describe('every plan maps to the one request the dry-run route takes', () => {
  const cases: Array<[string, Plan, DryRunRequest]> = [
    ['remove (Remove)', { steps: [{ kind: 'remove', overrideId: 'row-1' }] }, { kind: 'Remove', overrideId: 'row-1' }],
    [
      'follow latest (Assign)',
      { steps: [{ kind: 'assign', procedureId: 'p1', mode: 'FollowLatest' }] },
      { kind: 'Assign', assign: { procedureId: 'p1', mode: 'FollowLatest', pinnedVersionId: undefined, reason: undefined } },
    ],
    [
      'pin (Assign Pinned)',
      { steps: [{ kind: 'assign', procedureId: 'p1', mode: 'Pinned', pinnedVersionId: 'v1', reason: 'Keep rates' }] },
      { kind: 'Assign', assign: { procedureId: 'p1', mode: 'Pinned', pinnedVersionId: 'v1', reason: 'Keep rates' } },
    ],
    [
      'exclude a Procedure (Assign Excluded)',
      { steps: [{ kind: 'assign', procedureId: 'p1', mode: 'Excluded', reason: 'n/a' }] },
      { kind: 'Assign', assign: { procedureId: 'p1', mode: 'Excluded', pinnedVersionId: undefined, reason: 'n/a' } },
    ],
    [
      'rule exclude (ExcludeRule)',
      { steps: [{ kind: 'excludeRule', policyId: 'r1', reason: 'r', firmId: FENWICK.id }] },
      { kind: 'ExcludeRule', excludeRule: { policyId: 'r1', reason: 'r', firmId: FENWICK.id } },
    ],
    [
      'rule change (ChangeRule)',
      { steps: [{ kind: 'changeRule', policyId: 'r1', reason: 'r', ruleBody: '{"cap":1}' }] },
      { kind: 'ChangeRule', changeRule: { policyId: 'r1', reason: 'r', firmId: undefined, ruleBody: '{"cap":1}' } },
    ],
    [
      'switch: remove, then pin (Assign + replacesOverrideId)',
      { steps: [{ kind: 'remove', overrideId: 'row-own' }, { kind: 'assign', procedureId: 'p1', mode: 'Pinned', pinnedVersionId: 'v2', reason: 'x' }] },
      { kind: 'Assign', assign: { procedureId: 'p1', mode: 'Pinned', pinnedVersionId: 'v2', reason: 'x' }, replacesOverrideId: 'row-own' },
    ],
    [
      'replace an Exclude (ExcludeRule + replacesOverrideId)',
      { steps: [{ kind: 'remove', overrideId: 'row-ex' }, { kind: 'excludeRule', policyId: 'r1', reason: 'new' }] },
      { kind: 'ExcludeRule', excludeRule: { policyId: 'r1', reason: 'new', firmId: undefined }, replacesOverrideId: 'row-ex' },
    ],
    [
      're-change (ChangeRule + replacesOverrideId)',
      { steps: [{ kind: 'remove', overrideId: 'row-chg' }, { kind: 'changeRule', policyId: 'base', reason: 'r', ruleBody: '{}' }] },
      { kind: 'ChangeRule', changeRule: { policyId: 'base', reason: 'r', firmId: undefined, ruleBody: '{}' }, replacesOverrideId: 'row-chg' },
    ],
  ];

  it.each(cases)('%s', (_, plan, expected) => {
    expect(map(plan)).toEqual({ ok: true, request: expected });
  });

  it('an empty plan asks nothing; a shape the route does not take is not sent (two creates, create then remove, two removals, three calls)', () => {
    expect(map({ steps: [] })).toEqual({ ok: false, reason: 'empty' });
    const assign = { kind: 'assign', procedureId: 'p', mode: 'FollowLatest' } as const;
    const remove = { kind: 'remove', overrideId: 'x' } as const;
    for (const steps of [[assign, assign], [assign, remove], [remove, remove], [remove, assign, assign]]) {
      expect(map({ steps: [...steps] })).toEqual({ ok: false, reason: 'unsupported' });
    }
  });

  it('the body drops what is not set, so it is exactly ProcedureDryRunRequest', () => {
    const r = map({ steps: [{ kind: 'remove', overrideId: 'row-own' }, { kind: 'assign', procedureId: 'p1', mode: 'FollowLatest' }] });
    if (!r.ok) throw new Error('mapped');
    expect(JSON.parse(dryRunBody(r.request))).toEqual({ kind: 'Assign', replacesOverrideId: 'row-own', assign: { procedureId: 'p1', mode: 'FollowLatest' } });
  });

  it('R6: the firm the request names is the rule exclude\'s or change\'s firmId; an assignment names none', () => {
    expect(requestFirmOf({ kind: 'ExcludeRule', excludeRule: { policyId: 'r', reason: 'x', firmId: FENWICK.id } })).toBe(FENWICK.id);
    expect(requestFirmOf({ kind: 'ChangeRule', changeRule: { policyId: 'r', reason: 'x', firmId: FENWICK.id, ruleBody: '{}' } })).toBe(FENWICK.id);
    expect(requestFirmOf({ kind: 'ExcludeRule', excludeRule: { policyId: 'r', reason: 'x' } })).toBeNull();
    expect(requestFirmOf({ kind: 'Assign', assign: { procedureId: 'p', mode: 'FollowLatest' } })).toBeNull();
  });
});

// Justification (beyond the stated contract): proves the brief's STOP condition does not fire, i.e. no plan the wizards make lacks a dry run.
describe('every plan the two wizards make over the fixtures has a dry-run equivalent', () => {
  it('Procedure and rule choices, with complete inputs, never map to "unsupported"', () => {
    const procedureChoicesAll: ProcedureChoice[] = ['parent', 'follow', 'pin', 'exclude', 'remove'];
    const ruleChoicesAll: RuleChoice[] = ['parent', 'keep', 'exclude', 'change', 'changed'];
    let mapped = 0;
    for (const view of [matterProcedures, projectProcedures, waProcedures]) {
      for (const p of view.procedures) {
        for (const c of procedureChoicesAll.filter(x => procedureChoices(p).includes(x))) {
          const plan = planProcedure(p, c, { versionId: 'v-x', reason: 'r', pinnable: ['v-x'] });
          const m = map(plan);
          expect([p.id, c, m.ok || m.reason]).toEqual([p.id, c, plan.steps.length === 0 ? 'empty' : true]);
          if (m.ok) mapped += 1;
        }
      }
    }
    for (const view of [matterRules, projectRules, waRules]) {
      for (const r of view.rules) {
        for (const c of ruleChoicesAll) {
          const plan = planRule(r, c, { reason: 'r', firmId: FENWICK.id, ruleBody: '{}' });
          const m = map(plan);
          expect([r.policyId, c, m.ok || m.reason]).toEqual([r.policyId, c, plan.steps.length === 0 ? 'empty' : true]);
          if (m.ok) mapped += 1;
        }
      }
    }
    expect(mapped).toBeGreaterThan(10);
  });
});

describe('the route and the reader', () => {
  it('POSTs to the same prefix and query as the list reads', async () => {
    const fetchFn = jest.fn(async () => jsonResponse(view()));
    const api = createDryRunApi(fetchFn as never, BASE);
    const r = await api.dryRun('matters', MATTER_ID.toUpperCase(), { firmId: FENWICK.id, date: '2026-10-21' }, { kind: 'Remove', overrideId: 'row-1' });
    expect(r.ok).toBe(true);
    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`https://bff.example.com/api/finance/matters/${MATTER_ID}/procedures/dry-run?firmId=${FENWICK.id}&date=2026-10-21`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ kind: 'Remove', overrideId: 'row-1' });
    expect(dryRunUrl(BASE, 'workassignments', 'abc', { date: '2026-01-02' })).toBe('https://bff.example.com/api/finance/workassignments/abc/procedures/dry-run?date=2026-01-02');
  });

  it('a refusal is a WriteFailure with the ProblemDetails title, detail and errorCode', async () => {
    const fetchFn = jest.fn(async () => {
      throw problem(503, 'procedures.unavailable', { title: 'Procedures are unavailable', detail: 'Not now.' });
    });
    const r = await createDryRunApi(fetchFn as never, BASE).dryRun('matters', MATTER_ID, { date: '2026-10-21' }, { kind: 'Remove', overrideId: 'x' });
    expect(r).toEqual({ ok: false, failure: expect.objectContaining({ status: 503, errorCode: 'procedures.unavailable', title: 'Procedures are unavailable', detail: 'Not now.' }) });
  });

  it('a body that is not the view is a failure; a count that is not a number reads as null (never 0)', async () => {
    const bad = await createDryRunApi(jest.fn(async () => jsonResponse({ now: {} })) as never, BASE).dryRun('matters', MATTER_ID, { date: 'd' }, { kind: 'Remove', overrideId: 'x' });
    expect(bad.ok).toBe(false);
    expect(readDryRunView(view({ affectedOpenInvoices: { count: null, text: null, basis: 'ResolutionChanged' } }))!.affectedOpenInvoices.count).toBeNull();
    expect(readDryRunView(view({ affectedOpenInvoices: { text: null, basis: 'ResolutionChanged' } }))!.affectedOpenInvoices.count).toBeNull();
    expect(readDryRunView(view({ affectedOpenInvoices: { count: 0, text: '0 open invoices you can see are affected.', basis: 'ResolutionChanged' } }))!.affectedOpenInvoices.count).toBe(0);
  });
});

function view(over: Record<string, unknown> = {}) {
  return {
    record: matterProcedures.record,
    date: '2026-10-21',
    firm: null,
    firmSource: 'None',
    proposal: { kind: 'Remove', overrideKind: 'Procedure', replacesOverrideId: null, overrideId: 'x', proposedRowIds: [] },
    now: { procedures: matterProcedures, rules: matterRules },
    after: { procedures: matterProcedures, rules: matterRules },
    afterLabel: 'If saved now',
    disclosure: { predictsOwnerRefusal: false, readAtOneInstant: false, requestFirmAsked: false, notes: [] },
    closes: { d170: false, kinds: [], warning: null, message: null },
    affectedOpenInvoices: { count: 0, text: '0 open invoices you can see are affected.', basis: 'ResolutionChanged' },
    ...over,
  };
}
