import {
  canConfirmReject,
  checkRejectionReason,
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

  it('Reject sends the version the page displayed and the trimmed reason', async () => {
    const api = mockApi();
    api.reject.mockResolvedValue(ok(proposal({ state: 'Rejected', rowVersion: null, rejectionReason: 'Wrong limit.' })));
    api.get.mockResolvedValue(ok(detail()));

    await decideProposal(api, SET_ID, proposal({ rowVersion: '99' }), false, '  Wrong limit.\n');

    expect(api.reject).toHaveBeenCalledWith(SET_ID, POLICY_ID, '99', 'Wrong limit.');
    expect(api.accept).not.toHaveBeenCalled();
  });

  it.each([['nothing', ''], ['only white space', '   \n\t '], ['over 2,000 characters', 'x'.repeat(2001)]])(
    'Reject with %s is refused without a request (D-113)',
    async (_label, reason) => {
      const api = mockApi();

      const outcome = await decideProposal(api, SET_ID, proposal(), false, reason);

      expect(outcome.kind).toBe('failed');
      if (outcome.kind === 'failed') expect(outcome.failure.status).toBe(400);
      expect(api.reject).not.toHaveBeenCalled();
      expect(api.get).not.toHaveBeenCalled();
    }
  );

  it('Reject without any reason argument is refused: a caller that skips the dialog cannot reject without one', async () => {
    const api = mockApi();

    const outcome = await decideProposal(api, SET_ID, proposal(), false);

    expect(outcome.kind).toBe('failed');
    expect(api.reject).not.toHaveBeenCalled();
  });

  it('Accept never needs, and never sends, a reason', async () => {
    const api = mockApi();
    api.accept.mockResolvedValue(ok(proposal({ state: 'Accepted', rowVersion: null })));
    api.get.mockResolvedValue(ok(detail()));

    const outcome = await decideProposal(api, SET_ID, proposal({ rowVersion: '5' }), true, 'ignored');

    expect(outcome.kind).toBe('decided');
    expect(api.accept).toHaveBeenCalledWith(SET_ID, POLICY_ID, '5');
  });

  it('a rejected rule whose reason changed is reported as a change when the page reloads', async () => {
    const before = proposal({ state: 'Rejected', rejectionReason: 'Old reason' });
    const after = proposal({ state: 'Rejected', rejectionReason: 'New reason' });

    expect(diffProposals(before, after)).toEqual([{ field: 'Rejection reason', before: 'Old reason', after: 'New reason' }]);
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

  it('a stale decision on a rule that was published meanwhile (409 proposal_in_force) also reloads, so the page shows it in force', async () => {
    const api = mockApi();
    api.reject.mockResolvedValue(fail(409, 'ruleset.proposal_in_force'));
    api.get.mockResolvedValue(ok(detail([proposal({ state: 'Accepted', inForce: true, rowVersion: '5000' })])));

    const outcome = await decideProposal(api, SET_ID, proposal({ rowVersion: '4711' }), false, 'Wrong limit.');

    expect(outcome.kind).toBe('stale');
    if (outcome.kind === 'stale') {
      expect(outcome.detail.proposals[0].inForce).toBe(true);
      expect(outcome.changes.map(c => c.field)).toContain('Status');
    }
  });

  it('a non-409 failure (500) is a failure and does not reload', async () => {
    const api = mockApi();
    api.accept.mockResolvedValue(fail(500, null));

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

describe('the reject dialog: Reject is disabled until a reason is entered (D-113)', () => {
  it.each([
    ['empty', ''],
    ['only white space', '  \t\n '],
    ['over 2,000 characters once trimmed', 'x'.repeat(2001)],
  ])('is disabled for a reason that is %s', (_label, text) => {
    expect(canConfirmReject(text, false)).toBe(false);
  });

  it.each([
    ['one character', 'x'],
    ['a sentence', 'The guideline allows 12 hours a day.'],
    ['exactly 2,000 characters', 'x'.repeat(2000)],
    ['2,000 characters between spaces', ` ${'x'.repeat(2000)} `],
  ])('is enabled for a reason that is %s', (_label, text) => {
    expect(canConfirmReject(text, false)).toBe(true);
  });

  it('is disabled while a decision is in flight, whatever the reason', () => {
    expect(canConfirmReject('A real reason', true)).toBe(false);
  });

  it('reports how far over the limit the reason is, and sends it trimmed', () => {
    expect(checkRejectionReason(` ${'x'.repeat(2003)} `)).toMatchObject({ valid: false, over: 3 });
    expect(checkRejectionReason('  why  ')).toEqual({ reason: 'why', valid: true, over: 0 });
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

  it('stops polling when cancelled while waiting, without one more read', async () => {
    const api = mockApi();
    api.draft.mockResolvedValue(ok({ ruleSetId: SET_ID, jobId: 'j', statusUrl: '/s' }));
    api.get.mockResolvedValue(ok(detail([], { notConverted: null })));
    let cancelled = false;
    const outcome = await runDraft(api, SET_ID, { intervalMs: 1, sleep: async () => { cancelled = true; }, isCancelled: () => cancelled });
    expect(outcome.kind).toBe('timeout');
    expect(api.get).toHaveBeenCalledTimes(1);
  });

  it('a page that was left before the first read issues no read', async () => {
    const api = mockApi();
    api.draft.mockResolvedValue(ok({ ruleSetId: SET_ID, jobId: 'j', statusUrl: '/s' }));
    const outcome = await runDraft(api, SET_ID, { ...instant, isCancelled: () => true });
    expect(outcome.kind).toBe('timeout');
    expect(api.get).not.toHaveBeenCalled();
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
