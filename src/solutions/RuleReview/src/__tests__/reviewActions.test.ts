import {
  countProposals,
  decideProposal,
  diffProposals,
  draftFinished,
  publishNotice,
  runDraft,
  saveEdit,
} from '../services/reviewActions';
import { toFailure } from '../services/ruleSetApi';
import { POLICY_ID, SET_ID, detail, mockApi, ok, problem, proposal } from './testKit';

const fail = (status: number, code: string | null, extra: Record<string, unknown> = {}) => ({
  ok: false as const,
  failure: toFailure(problem(status, code, extra)),
});

describe('decideProposal (076 re-check K3)', () => {
  it('Accept sends the version the page displayed and reloads on success', async () => {
    const api = mockApi();
    const shown = proposal({ rowVersion: '4711' });
    api.accept.mockResolvedValue(ok(proposal({ state: 'Accepted', rowVersion: null })));
    api.get.mockResolvedValue(ok(detail([proposal({ state: 'Accepted', rowVersion: '4712' })])));

    const outcome = await decideProposal(api, SET_ID, shown, true);

    expect(api.accept).toHaveBeenCalledWith(SET_ID, POLICY_ID, '4711');
    expect(api.reject).not.toHaveBeenCalled();
    expect(outcome.kind).toBe('decided');
    if (outcome.kind === 'decided') expect(outcome.detail?.proposals[0].rowVersion).toBe('4712');
  });

  it('Reject sends the version the page displayed', async () => {
    const api = mockApi();
    api.reject.mockResolvedValue(ok(proposal({ state: 'Rejected', rowVersion: null })));
    api.get.mockResolvedValue(ok(detail()));

    await decideProposal(api, SET_ID, proposal({ rowVersion: '99' }), false);

    expect(api.reject).toHaveBeenCalledWith(SET_ID, POLICY_ID, '99');
    expect(api.accept).not.toHaveBeenCalled();
  });

  it('on 409 concurrent_edit nothing is decided: the rule set is reloaded and what changed is reported', async () => {
    const api = mockApi();
    const shown = proposal({ rowVersion: '4711', consequence: 'Flag', ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 10 } });
    // Admin B edited the rule: a new version, a new cap and a new consequence.
    const changed = proposal({
      rowVersion: '4800', versionNumber: 2, consequence: 'Reject', ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 8 },
    });
    api.accept.mockResolvedValue(fail(409, 'ruleset.concurrent_edit'));
    api.get.mockResolvedValue(ok(detail([changed])));

    const outcome = await decideProposal(api, SET_ID, shown, true);

    expect(outcome.kind).toBe('stale');
    if (outcome.kind !== 'stale') return;
    expect(outcome.detail.proposals[0].rowVersion).toBe('4800'); // the page now holds the token it must decide on next
    expect(outcome.changes.map(c => c.field)).toEqual(['Version', 'Parameter maxHoursPerDay', 'Consequence']);
    expect(outcome.changes.find(c => c.field === 'Consequence')).toMatchObject({ before: 'Flag', after: 'Reject' });
    expect(api.get).toHaveBeenCalledTimes(1);
  });

  it('a stale decision whose visible fields did not change reports no field changes', async () => {
    const api = mockApi();
    api.accept.mockResolvedValue(fail(409, 'ruleset.concurrent_edit'));
    api.get.mockResolvedValue(ok(detail([proposal({ rowVersion: '5000' })])));

    const outcome = await decideProposal(api, SET_ID, proposal({ rowVersion: '4711' }), true);

    expect(outcome.kind).toBe('stale');
    if (outcome.kind === 'stale') expect(outcome.changes).toEqual([]);
  });

  it('an unchanged rule decides: no stale outcome', async () => {
    const api = mockApi();
    api.accept.mockResolvedValue(ok(proposal({ state: 'Accepted', rowVersion: null })));
    api.get.mockResolvedValue(ok(detail()));
    expect((await decideProposal(api, SET_ID, proposal(), true)).kind).toBe('decided');
  });

  it('other 409s (proposal in force) are failures, not a reload', async () => {
    const api = mockApi();
    api.accept.mockResolvedValue(fail(409, 'ruleset.proposal_in_force'));

    const outcome = await decideProposal(api, SET_ID, proposal(), true);

    expect(outcome.kind).toBe('failed');
    expect(api.get).not.toHaveBeenCalled();
  });

  it('a 400 carries its per-rule errors to the page', async () => {
    const api = mockApi();
    api.accept.mockResolvedValue(
      fail(400, 'ruleset.invalid', { errors: [{ policyId: POLICY_ID, rule: 'DailyHoursCap', field: 'maxHoursPerDay', message: 'too big', reason: null }] })
    );

    const outcome = await decideProposal(api, SET_ID, proposal(), true);

    expect(outcome.kind).toBe('failed');
    if (outcome.kind === 'failed') expect(outcome.failure.errors[0].field).toBe('maxHoursPerDay');
  });

  it('a view without a token is never sent: the page reloads instead', async () => {
    const api = mockApi();
    api.get.mockResolvedValue(ok(detail()));

    const outcome = await decideProposal(api, SET_ID, proposal({ rowVersion: null }), true);

    expect(outcome.kind).toBe('stale');
    expect(api.accept).not.toHaveBeenCalled();
    expect(api.reject).not.toHaveBeenCalled();
  });

  it('a refused decision whose reload also fails reports the refusal', async () => {
    const api = mockApi();
    api.accept.mockResolvedValue(fail(409, 'ruleset.concurrent_edit'));
    api.get.mockResolvedValue(fail(500, null));

    const outcome = await decideProposal(api, SET_ID, proposal(), true);

    expect(outcome.kind).toBe('failed');
    if (outcome.kind === 'failed') expect(outcome.failure.status).toBe(409);
  });
});

describe('diffProposals', () => {
  it('is empty when nothing visible differs', () => {
    expect(diffProposals(proposal(), proposal({ rowVersion: '1' }))).toEqual([]);
  });

  it('names a changed clause quote and an added parameter', () => {
    const lines = diffProposals(proposal(), proposal({ clauseQuote: 'New quote', ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 10, extra: 1 } }));
    expect(lines.map(l => l.field)).toEqual(['Parameter extra', 'Clause quote']);
  });
});

describe('saveEdit', () => {
  it('reloads after a save', async () => {
    const api = mockApi();
    api.edit.mockResolvedValue(ok({ proposal: proposal({ rowVersion: null }), createdNewVersion: true }));
    api.get.mockResolvedValue(ok(detail()));

    const outcome = await saveEdit(api, SET_ID, POLICY_ID, { consequence: 'Reject' });

    expect(outcome.kind).toBe('saved');
    if (outcome.kind === 'saved') expect(outcome.createdNewVersion).toBe(true);
    expect(api.get).toHaveBeenCalledTimes(1);
  });

  it('reloads on 409 and saves nothing', async () => {
    const api = mockApi();
    api.edit.mockResolvedValue(fail(409, 'ruleset.concurrent_edit'));
    api.get.mockResolvedValue(ok(detail()));
    expect((await saveEdit(api, SET_ID, POLICY_ID, { consequence: 'Reject' })).kind).toBe('stale');
  });
});

describe('runDraft', () => {
  const instant = { sleep: async () => undefined, intervalMs: 1 };

  it('starts the run, then polls until the not-converted list appears', async () => {
    const api = mockApi();
    api.draft.mockResolvedValue(ok({ ruleSetId: SET_ID, jobId: 'j', statusUrl: '/s' }));
    api.get
      .mockResolvedValueOnce(ok(detail([], { notConverted: null })))
      .mockResolvedValueOnce(ok(detail([], { notConverted: null })))
      .mockResolvedValueOnce(ok(detail([proposal()], { notConverted: [] })));

    const progress: number[] = [];
    const outcome = await runDraft(api, SET_ID, { ...instant, onProgress: d => progress.push(d.proposals.length) });

    expect(outcome.kind).toBe('finished');
    expect(api.get).toHaveBeenCalledTimes(3);
    expect(progress).toEqual([0, 0, 1]);
  });

  it('an empty not-converted list means finished; null means not yet', () => {
    expect(draftFinished(detail([], { notConverted: [] }))).toBe(true);
    expect(draftFinished(detail([], { notConverted: null }))).toBe(false);
  });

  it('already_drafted is not an error: it reads the current rules', async () => {
    const api = mockApi();
    api.draft.mockResolvedValue(fail(409, 'ruleset.already_drafted'));
    api.get.mockResolvedValue(ok(detail([proposal()], { notConverted: [] })));
    expect((await runDraft(api, SET_ID, instant)).kind).toBe('finished');
  });

  it('a refused start (not an admin) fails without polling', async () => {
    const api = mockApi();
    api.draft.mockResolvedValue(fail(403, null));
    const outcome = await runDraft(api, SET_ID, instant);
    expect(outcome.kind).toBe('failed');
    expect(api.get).not.toHaveBeenCalled();
  });

  it('times out when the run never finishes', async () => {
    const api = mockApi();
    api.draft.mockResolvedValue(ok({ ruleSetId: SET_ID, jobId: 'j', statusUrl: '/s' }));
    api.get.mockResolvedValue(ok(detail([], { notConverted: null })));
    let clock = 0;
    const outcome = await runDraft(api, SET_ID, {
      intervalMs: 10, timeoutMs: 25, sleep: async ms => { clock += ms; }, now: () => clock,
    });
    expect(outcome.kind).toBe('timeout');
  });

  it('stops polling when cancelled', async () => {
    const api = mockApi();
    api.draft.mockResolvedValue(ok({ ruleSetId: SET_ID, jobId: 'j', statusUrl: '/s' }));
    api.get.mockResolvedValue(ok(detail([], { notConverted: null })));
    const outcome = await runDraft(api, SET_ID, { ...instant, isCancelled: () => true });
    expect(outcome.kind).toBe('timeout');
    expect(api.get).toHaveBeenCalledTimes(1);
  });
});

describe('publishNotice (D-66)', () => {
  it('carries the exact sentence only when clause quotes are visible to firms', () => {
    expect(publishNotice(true)).toContain('Clause quotes will be visible to firms.');
    expect(publishNotice(false)).not.toContain('Clause quotes will be visible to firms.');
    expect(publishNotice(false)).not.toContain('visible to firms');
  });
});

describe('countProposals', () => {
  it('counts what a publish would put in force', () => {
    const counts = countProposals(
      detail([
        proposal({ policyId: 'a', state: 'Accepted', inForce: false }),
        proposal({ policyId: 'b', state: 'Accepted', inForce: true }),
        proposal({ policyId: 'c', state: 'Proposed' }),
        proposal({ policyId: 'd', state: 'Rejected' }),
      ])
    );
    expect(counts).toEqual({ total: 4, proposed: 1, accepted: 2, rejected: 1, toPublish: 1 });
  });
});
