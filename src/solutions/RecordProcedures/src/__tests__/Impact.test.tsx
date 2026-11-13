/**
 * Task 084d-4: every wizard's Impact step reads the dry run (084d-2a/2b). The request each wizard sends, each state of the answer
 * (Now -> "If saved now", the D-170 and section 15 warnings, the close message, a count, a null count, the R6 503, another refusal), that a
 * refusal never blocks Confirm, that the removal preview is superseded, and the race of two asks.
 */

import '@testing-library/jest-dom';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { O21_LINE } from '../components/wizards/steps';
import { ADVISORY_LINE, COUNT_UNKNOWN, R6_TEXT, UNCHECKED_CLOSE_LINE } from '../components/wizards/impact';
import type { ProcedureItem, RecordProceduresView, RecordRulesInForceView, RuleItem } from '../services/recordProceduresApi';
import { MATTER, MATTER_ID, OKAFOR, TODAY, mark, matterProcedures, matterRules, override, rule } from './fixtures';
import { button, chooseRadio, createdBody, next, openProcedure, openRule, previewBody, ready, refusal, removedBody, renderHarness, respond, toChoices, toConfirm, typeReason } from './wizardHarness';
import type { WriteCall } from './testKit';

/** The server's fixed disclosure notes (RecordProcedureReadService.cs): one of them says, in the negative, "will succeed". */
const NOTES = [
  'This preview does not check who will own the new rows, so saving can still be refused for that reason.',
  'Now and After are read separately; a change someone else saves in between shows as part of the difference.',
  'After shows what the lists would show if this change were saved now. It does not mean that saving will succeed.',
];
const REMOVE_CHANGED_WARNING = 'Removing this override will close the rule it changed and remove pending decisions on active invoices.';

/** The OCG excluded here, and BLOCK (one of its rules) gone from the rules in force. */
const ocgExcluded = (): ProcedureItem => ({
  ...matterProcedures.procedures[0],
  status: 'Excluded',
  overrides: [override({ rowId: 'row-new', mode: 'Excluded', chainRecord: MATTER, decisive: true })],
});
const afterProcedures: RecordProceduresView = { ...matterProcedures, procedures: [ocgExcluded(), ...matterProcedures.procedures.slice(1)] };
const afterRules: RecordRulesInForceView = { ...matterRules, rules: matterRules.rules.filter(r => r.policyId !== 'r-block') };

function dryRunView(over: Record<string, unknown> = {}) {
  return {
    record: MATTER,
    date: TODAY,
    firm: null,
    firmSource: 'Record',
    proposal: { kind: 'Assign', overrideKind: 'Procedure', replacesOverrideId: null, overrideId: null, proposedRowIds: ['row-new'] },
    now: { procedures: matterProcedures, rules: matterRules },
    after: { procedures: afterProcedures, rules: afterRules },
    afterLabel: 'If saved now',
    disclosure: { predictsOwnerRefusal: false, readAtOneInstant: false, requestFirmAsked: false, notes: NOTES },
    closes: { d170: false, kinds: [], warning: null, message: null },
    affectedOpenInvoices: { count: 3, text: '3 open invoices you can see are affected.', basis: 'ResolutionChanged' },
    ...over,
  };
}

/** Open the OCG, choose Exclude here with a reason, and go to Impact. */
async function excludeOcg(d: HTMLElement, reason = 'Not for this matter') {
  await toChoices(d);
  chooseRadio(d, /^Exclude here/);
  typeReason(d, reason);
  fireEvent.click(next(d));
  return within(d).findByTestId('impact-step');
}

const ocg = () => openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');

describe('the Impact step reads the dry run', () => {
  it('sends the plan as the route\'s request for the lists\' firm and date, and shows Now -> "If saved now" for both lists, the count and the O-21 line', async () => {
    const { dryRunCalls, writeCalls } = renderHarness({ dryRun: () => dryRunView(), writes: respond({ assign: () => createdBody() }) });
    const d = await ocg();
    const impact = await excludeOcg(d);

    const answer = await within(impact).findByTestId('dryrun');
    expect(dryRunCalls).toHaveLength(1);
    expect(dryRunCalls[0].method).toBe('POST');
    expect(dryRunCalls[0].pathname).toBe(`/api/finance/matters/${MATTER_ID}/procedures/dry-run`);
    // The lists were read with the record's billing firm (the server default), so no firmId is sent; the date is the lists' date.
    expect(new URL(dryRunCalls[0].url).searchParams.get('firmId')).toBeNull();
    expect(new URL(dryRunCalls[0].url).searchParams.get('date')).toBe(TODAY);
    expect(dryRunCalls[0].body).toEqual({ kind: 'Assign', assign: { procedureId: 'p-ocg', mode: 'Excluded', reason: 'Not for this matter' } });

    expect(within(answer).getByTestId('dryrun-label')).toHaveTextContent('Now → If saved now');
    const procRows = within(answer).getAllByTestId('impact-procedures-row');
    expect(procRows).toHaveLength(1);
    expect(procRows[0]).toHaveTextContent('Acme Outside Counsel GuidelinesIn force · v2→Excluded here · v2');
    const ruleRows = within(answer).getAllByTestId('impact-rules-row');
    expect(ruleRows).toHaveLength(1);
    expect(ruleRows[0]).toHaveTextContent('BLOCK · Block billingIn force · v1→Not listed');
    expect(within(answer).getByTestId('impact-affected')).toHaveTextContent('3 open invoices you can see are affected.');
    expect(within(impact).getByTestId('o21-line')).toHaveTextContent(O21_LINE);
    // The choice's own guess is replaced by the answer; nothing closes, so no warning shows.
    expect(within(impact).queryByTestId('impact-change')).not.toBeInTheDocument();
    expect(within(answer).queryByTestId('impact-d170')).not.toBeInTheDocument();
    expect(within(answer).queryByTestId('impact-warning')).not.toBeInTheDocument();
    expect(writeCalls).toHaveLength(0);
  });

  it('nothing presents After as "will succeed": the label is the server\'s, and the only "will succeed" is the server\'s own negation', async () => {
    renderHarness({ dryRun: () => dryRunView() });
    const d = await ocg();
    const impact = await excludeOcg(d);
    await within(impact).findByTestId('dryrun');
    const text = impact.textContent ?? '';
    expect(within(impact).getByTestId('dryrun-notes')).toHaveTextContent(NOTES[2]);
    expect(text.replace(NOTES[2], '')).not.toMatch(/succeed|guarantee/i);
    expect(text).toContain('If saved now');
  });

  it('D-170: removing a changed rule shows the warning that open findings close (the server\'s section 15 text), and the removal preview is not asked', async () => {
    const changed: RuleItem = rule({
      policyId: 'r-hours-rec',
      code: 'HOURS',
      name: 'Daily hours',
      recordRule: true,
      marks: [mark('Changed', { distance: 0, overrideRowId: 'row-hours', basePolicyId: 'r-hours', recordRuleId: 'r-hours-rec' })],
    });
    const rules = { ...matterRules, rules: [changed] };
    const { dryRunCalls, writeCalls } = renderHarness({
      rules,
      dryRun: () =>
        dryRunView({
          now: { procedures: matterProcedures, rules },
          after: { procedures: matterProcedures, rules: { ...matterRules, rules: [] } },
          closes: { d170: true, kinds: ['ChangedRuleRemoved'], warning: REMOVE_CHANGED_WARNING, message: null },
          affectedOpenInvoices: { count: 2, text: '2 open invoices you can see are affected.', basis: 'FindingsClosed' },
        }),
      writes: respond({ preview: () => previewBody(), remove: () => removedBody() }),
    });
    const d = await openRule('r-hours-rec', 'HOURS · Daily hours');
    await toChoices(d);
    chooseRadio(d, /^Run the published rule/);
    fireEvent.click(next(d));
    const d170 = await within(d).findByTestId('impact-d170');
    expect(d170).toHaveTextContent('Open findings close');
    expect(d170).toHaveTextContent(REMOVE_CHANGED_WARNING);
    expect(within(d).getByTestId('impact-affected')).toHaveTextContent('2 open invoices you can see are affected.');
    expect(dryRunCalls[0].body).toEqual({ kind: 'Remove', overrideId: 'row-hours' });
    // Superseded: the dry run answered with the same fields, so 089's removal preview is not asked.
    expect(writeCalls.map(c => c.route)).toEqual([]);
  });

  it('a warning without d170 and the change\'s close message are shown as the server wrote them', async () => {
    renderHarness({ dryRun: () => dryRunView({ closes: { d170: false, kinds: ['BaseRuleSuperseded'], warning: 'Server warning.', message: 'Server close message.' } }) });
    const d = await ocg();
    const impact = await excludeOcg(d);
    expect(await within(impact).findByTestId('impact-warning')).toHaveTextContent('Server warning.');
    expect(within(impact).getByTestId('impact-message')).toHaveTextContent('Server close message.');
    expect(within(impact).queryByTestId('impact-d170')).not.toBeInTheDocument();
  });

  it('O-K1b: a null count says "can\'t be counted", never 0 or a blank', async () => {
    renderHarness({ dryRun: () => dryRunView({ affectedOpenInvoices: { count: null, text: null, basis: 'ResolutionChanged' } }) });
    const d = await ocg();
    const impact = await excludeOcg(d);
    const line = await within(impact).findByTestId('impact-affected');
    expect(line).toHaveTextContent(COUNT_UNKNOWN);
    expect(line.textContent).toContain("can't be counted");
    expect(line.textContent).not.toMatch(/\b0\b/);
  });
});

describe('a refused dry run is shown and never blocks Confirm (the write route decides)', () => {
  it('R6: a 503 procedures.unavailable for a rule exclude that names a firm says the preview is not available for this firm; the save still goes', async () => {
    const { writeCalls } = renderHarness({
      dryRun: () => refusal(503, 'Procedures are unavailable', 'procedures.unavailable', { detail: 'The Procedures and rules for this record could not be read right now.' }),
      writes: respond({ exclude: () => createdBody() }),
    });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
    fireEvent.click(await screen.findByRole('option', { name: 'Okafor Lane LLP', hidden: true }));
    typeReason(d, 'Okafor invoices are exempt');
    fireEvent.click(next(d));
    const failed = await within(d).findByTestId('dryrun-failed');
    expect(failed).toHaveTextContent(R6_TEXT);
    expect(failed).not.toHaveTextContent('could not be read right now');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Exclude rule'));
    await within(d).findByTestId('success-body');
    expect(writeCalls.map(c => c.route)).toEqual(['exclude']);
  });

  it('a 503 procedures.unavailable for a request that names no firm is outside R6: the server\'s title and detail are shown', async () => {
    renderHarness({ dryRun: () => refusal(503, 'Procedures are unavailable', 'procedures.unavailable', { detail: 'Could not be read right now.' }) });
    const d = await ocg();
    const impact = await excludeOcg(d);
    const failed = await within(impact).findByTestId('dryrun-failed');
    expect(failed).toHaveTextContent('Procedures are unavailable');
    expect(failed).toHaveTextContent('Could not be read right now.');
    expect(failed).not.toHaveTextContent(R6_TEXT);
  });

  it('another refusal (403) shows the ProblemDetails title and detail, the choice line, and Confirm and the save still work', async () => {
    const { writeCalls } = renderHarness({
      dryRun: () => refusal(403, 'Forbidden', 'sdap.access.deny.privilege', { detail: 'You cannot do that here.' }),
      writes: respond({ assign: () => createdBody() }),
    });
    const d = await ocg();
    const impact = await excludeOcg(d);
    const failed = await within(impact).findByTestId('dryrun-failed');
    expect(failed).toHaveTextContent('Forbidden');
    expect(failed).toHaveTextContent('You cannot do that here.');
    expect(within(impact).getByTestId('impact-change')).toHaveTextContent('In force · v2→Excluded here');
    expect(within(impact).getByTestId('o21-line')).toBeInTheDocument();
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(writeCalls.map(c => c.route)).toEqual(['assign']);
  });
});

describe('the proposal changes: a new ask, and a stale answer is dropped', () => {
  it('Back, a new reason, Next: asked again; the first ask\'s late answer never shows; the same proposal again is not re-asked', async () => {
    const pending: Array<{ call: WriteCall; resolve: (v: unknown) => void }> = [];
    const { dryRunCalls } = renderHarness({ dryRun: call => new Promise(resolve => pending.push({ call, resolve })) });
    const d = await ocg();
    await excludeOcg(d, 'first reason');
    await waitFor(() => expect(pending).toHaveLength(1));
    expect(within(d).getByTestId('dryrun-loading')).toBeInTheDocument();
    expect(next(d)).toBeDisabled();

    fireEvent.click(button(d, 'Back'));
    typeReason(d, 'second reason');
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(pending).toHaveLength(2));
    expect((pending[1].call.body as { assign: { reason: string } }).assign.reason).toBe('second reason');

    // The second ask answers first and is shown.
    await act(async () => pending[1].resolve(dryRunView({ affectedOpenInvoices: { count: 2, text: 'CURRENT answer.', basis: 'ResolutionChanged' } })));
    expect(await within(d).findByTestId('impact-affected')).toHaveTextContent('CURRENT answer.');

    // The first ask answers late: it is stale and dropped. The current answer stays, and nothing is asked again.
    await act(async () => pending[0].resolve(dryRunView({ affectedOpenInvoices: { count: 1, text: 'STALE answer.', basis: 'ResolutionChanged' } })));
    expect(within(d).queryByText('STALE answer.')).not.toBeInTheDocument();
    expect(within(d).getByTestId('impact-affected')).toHaveTextContent('CURRENT answer.');
    expect(within(d).queryByTestId('dryrun-loading')).not.toBeInTheDocument();
    expect(dryRunCalls).toHaveLength(2);

    // Back and Next with the same proposal: the answer held is shown, nothing is asked again.
    fireEvent.click(button(d, 'Back'));
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('impact-affected')).toHaveTextContent('CURRENT answer.');
    expect(dryRunCalls).toHaveLength(2);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Review round 1 (2026-11-13)
// ─────────────────────────────────────────────────────────────────────────────

/** A plan that removes an override: the Procedure the record pins, "Remove from this record". Needs `writes` for the removal preview and the save. */
const instr = () => openProcedure('p-instr', 'Acme v. Northwind billing instructions');
async function removeInstr(d: HTMLElement) {
  await toChoices(d);
  chooseRadio(d, /^Remove from this record/);
  fireEvent.click(next(d));
  return within(d).findByTestId('impact-step');
}
const NEVER = () => new Promise<never>(() => undefined);
const routesOf = (calls: WriteCall[]) => calls.map(c => c.route);
const removalWrites = (preview: () => unknown = () => previewBody()) => respond({ preview, remove: () => removedBody() });

describe('a firm picked in the panel is the dry run\'s firm', () => {
  it('the dry-run URL carries the picked firm\'s firmId (the billing-firm default sends none)', async () => {
    const { dryRunCalls } = renderHarness({
      procedures: q => (q.firmId ? { ...matterProcedures, firm: OKAFOR, firmSource: 'Query', procedures: matterProcedures.procedures.filter(p => p.id !== 'p-instr') } : matterProcedures),
      rules: q => (q.firmId ? { ...matterRules, firm: OKAFOR, firmSource: 'Query' } : matterRules),
      dryRun: () => dryRunView(),
    });
    await ready();
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
    // The Okafor reading has no p-instr row: the lists were re-read for the picked firm.
    await waitFor(() => expect(screen.queryByTestId('procedure-row-p-instr')).not.toBeInTheDocument());
    const d = await ocg();
    await excludeOcg(d);
    await within(d).findByTestId('dryrun');
    expect(dryRunCalls).toHaveLength(1);
    expect(new URL(dryRunCalls[0].url).searchParams.get('firmId')).toBe(OKAFOR.id.toLowerCase());
    expect(new URL(dryRunCalls[0].url).searchParams.get('date')).toBe(TODAY);
  });
});

describe('a dry run that does not answer', () => {
  it('is given up on at the timeout: "took too long", Next is not held, Retry asks again, and the save goes', async () => {
    let n = 0;
    const { dryRunCalls, writeCalls } = renderHarness({
      timeoutMs: 80,
      dryRun: () => (++n === 1 ? NEVER() : dryRunView()),
      writes: respond({ assign: () => createdBody() }),
    });
    const d = await ocg();
    const impact = await excludeOcg(d);
    expect(within(impact).getByTestId('dryrun-loading')).toBeInTheDocument();
    expect(next(d)).toBeDisabled();

    const failed = await within(impact).findByTestId('dryrun-failed');
    expect(failed).toHaveTextContent('The preview took too long to come back.');
    expect(within(failed).getByTestId('dryrun-advisory')).toHaveTextContent(ADVISORY_LINE);
    expect(next(d)).toBeEnabled();

    // Retry asks again, and the answer is shown.
    fireEvent.click(within(d).getByTestId('dryrun-retry'));
    expect(await within(d).findByTestId('dryrun')).toBeInTheDocument();
    expect(dryRunCalls).toHaveLength(2);
    expect(within(d).queryByTestId('dryrun-failed')).not.toBeInTheDocument();

    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(routesOf(writeCalls)).toEqual(['assign']);
  });

  it('Confirm is reachable after a timeout without ever retrying, and the save goes', async () => {
    const { writeCalls } = renderHarness({ timeoutMs: 80, dryRun: NEVER, writes: respond({ assign: () => createdBody() }) });
    const d = await ocg();
    await excludeOcg(d);
    await within(d).findByTestId('dryrun-failed');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    // A timed-out preview of a plan that removes nothing says nothing is unchecked.
    expect(within(d).queryByTestId('confirm-unchecked')).not.toBeInTheDocument();
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(routesOf(writeCalls)).toEqual(['assign']);
  });

  it('closing the wizard abandons the read (its abort signal fires); an answer after that changes nothing and reports nothing', async () => {
    const pending: Array<(v: unknown) => void> = [];
    const logged = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      const { mock } = renderHarness({ dryRun: () => new Promise(resolve => pending.push(resolve)) });
      const d = await ocg();
      await excludeOcg(d);
      await waitFor(() => expect(pending).toHaveLength(1));
      const call = mock.mock.calls.find(c => String(c[0]).includes('/dry-run'))!;
      const signal = (call[1] as RequestInit).signal as AbortSignal;
      expect(signal.aborted).toBe(false);

      fireEvent.click(button(d, 'Cancel'));
      await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());
      expect(signal.aborted).toBe(true);

      await act(async () => pending[0](dryRunView()));
      expect(logged).not.toHaveBeenCalled();
    } finally {
      logged.mockRestore();
    }
  });
});

describe('a failed dry run has a Retry that asks again', () => {
  it('Retry forgets the cached failure for that proposal and shows the new answer; the failure also stays put until then (Back and Next do not re-ask)', async () => {
    let n = 0;
    const { dryRunCalls } = renderHarness({ dryRun: () => (++n === 1 ? refusal(403, 'Forbidden', 'sdap.access.deny.privilege', { detail: 'No.' }) : dryRunView({ affectedOpenInvoices: { count: 4, text: 'AFTER RETRY.', basis: 'ResolutionChanged' } })) });
    const d = await ocg();
    const impact = await excludeOcg(d);
    expect(await within(impact).findByTestId('dryrun-failed')).toHaveTextContent('Forbidden');
    expect(within(impact).getByRole('alert', { hidden: true })).toBe(within(impact).getByTestId('dryrun-failed'));

    fireEvent.click(button(d, 'Back'));
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('dryrun-failed')).toBeInTheDocument();
    expect(dryRunCalls).toHaveLength(1);

    fireEvent.click(within(d).getByTestId('dryrun-retry'));
    expect(await within(d).findByTestId('impact-affected')).toHaveTextContent('AFTER RETRY.');
    expect(dryRunCalls).toHaveLength(2);
  });
});

describe('answers are kept by the request they answer', () => {
  it('a late answer to an earlier proposal never replaces the live one: each proposal keeps its own, and nothing is asked twice', async () => {
    const pending: Array<(v: unknown) => void> = [];
    const { dryRunCalls } = renderHarness({ dryRun: () => new Promise(resolve => pending.push(resolve)) });
    const d = await ocg();
    await excludeOcg(d, 'first reason');
    await waitFor(() => expect(pending).toHaveLength(1));

    fireEvent.click(button(d, 'Back'));
    typeReason(d, 'second reason');
    fireEvent.click(next(d));
    await waitFor(() => expect(pending).toHaveLength(2));

    await act(async () => pending[1](dryRunView({ affectedOpenInvoices: { count: 2, text: 'ANSWER TO SECOND.', basis: 'ResolutionChanged' } })));
    expect(await within(d).findByTestId('impact-affected')).toHaveTextContent('ANSWER TO SECOND.');
    // The first proposal's answer comes late. It does not evict the live one.
    await act(async () => pending[0](dryRunView({ affectedOpenInvoices: { count: 1, text: 'ANSWER TO FIRST.', basis: 'ResolutionChanged' } })));
    expect(within(d).getByTestId('impact-affected')).toHaveTextContent('ANSWER TO SECOND.');

    // Going to the first proposal shows ITS answer, going back to the second shows its own: no new ask for either.
    fireEvent.click(button(d, 'Back'));
    typeReason(d, 'first reason');
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('impact-affected')).toHaveTextContent('ANSWER TO FIRST.');
    fireEvent.click(button(d, 'Back'));
    typeReason(d, 'second reason');
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('impact-affected')).toHaveTextContent('ANSWER TO SECOND.');
    expect(dryRunCalls).toHaveLength(2);
  });
});

describe('the removal preview stands in only after a dry run refused with 403, 404 or 503', () => {
  it.each([403, 404, 503])('a %i asks 089\'s removal preview, and its answer is the one message shown', async status => {
    const { writeCalls } = renderHarness({ dryRun: () => refusal(status, 'Refused', 'x.refused'), writes: removalWrites(() => previewBody({ closesRule: true, warning: REMOVE_CHANGED_WARNING })) });
    const d = await instr();
    await removeInstr(d);
    const shown = await within(d).findByTestId('removal-preview');
    expect(shown).toHaveTextContent(REMOVE_CHANGED_WARNING);
    expect(routesOf(writeCalls)).toEqual(['preview']);
    // ONE message: the preview alone, never "The preview is not shown" over a preview that is shown.
    expect(within(d).queryByText('The preview is not shown')).not.toBeInTheDocument();
    expect(within(d).queryByTestId('dryrun-failed')).not.toBeInTheDocument();
    expect(within(d).queryByTestId('preview-failed')).not.toBeInTheDocument();
    expect(within(d).getByTestId('removal-preview-note')).toBeInTheDocument();
  });

  it.each([
    ['400', () => refusal(400, 'Bad request', 'procedures.invalid_query')],
    ['409', () => refusal(409, 'Conflict', 'assignment.conflict')],
    ['a network failure', () => new Error('Failed to fetch')],
  ])('%s does not ask the removal preview: the dry run\'s own failure is shown, with Retry', async (_name, answer) => {
    const { writeCalls } = renderHarness({ dryRun: answer as () => unknown, writes: removalWrites() });
    const d = await instr();
    await removeInstr(d);
    expect(await within(d).findByTestId('dryrun-failed')).toBeInTheDocument();
    expect(within(d).getByTestId('dryrun-retry')).toBeInTheDocument();
    expect(within(d).queryByTestId('preview-loading')).not.toBeInTheDocument();
    expect(within(d).queryByTestId('removal-preview')).not.toBeInTheDocument();
    expect(routesOf(writeCalls)).toEqual([]);
  });

  it('a timeout does not ask the removal preview either', async () => {
    const { writeCalls } = renderHarness({ timeoutMs: 80, dryRun: NEVER, writes: removalWrites() });
    const d = await instr();
    await removeInstr(d);
    expect(await within(d).findByTestId('dryrun-failed')).toHaveTextContent('took too long');
    expect(routesOf(writeCalls)).toEqual([]);
  });

  it('a removal preview that does not answer is given up on at the timeout too, so Next is not held for it', async () => {
    renderHarness({ timeoutMs: 80, dryRun: () => refusal(503, 'Unavailable', 'procedures.unavailable'), writes: respond({ preview: NEVER }) });
    const d = await instr();
    await removeInstr(d);
    expect(next(d)).toBeDisabled();
    const failed = await within(d).findByTestId('preview-failed');
    expect(failed).toHaveTextContent('took too long');
    expect(next(d)).toBeEnabled();
  });

  it('both reads refused is ONE message (one alert) naming both, with the advisory line in its own element', async () => {
    renderHarness({ dryRun: () => refusal(503, 'Unavailable', 'procedures.unavailable', { detail: 'Not now.' }), writes: respond({ preview: () => refusal(403, 'Forbidden', 'access.denied') }) });
    const d = await instr();
    const impact = await removeInstr(d);
    const bar = await within(impact).findByTestId('preview-failed');
    expect(within(impact).getAllByRole('alert', { hidden: true })).toEqual([bar]);
    expect(within(impact).queryByTestId('dryrun-failed')).not.toBeInTheDocument();
    expect(within(impact).getAllByText('The preview is not shown')).toHaveLength(1);
    expect(within(bar).getByTestId('dryrun-failed-text')).toHaveTextContent('Unavailable');
    expect(within(bar).getByTestId('preview-failed-text')).toHaveTextContent('could not be checked either');
    expect(within(bar).getByTestId('preview-failed-text')).toHaveTextContent('Forbidden');
    expect(within(bar).getByTestId('dryrun-advisory')).toHaveTextContent(ADVISORY_LINE);
    expect(within(bar).getByTestId('dryrun-failed-text').textContent).not.toContain(ADVISORY_LINE);
  });
});

describe('the advisory line is its own sentence', () => {
  it('a refusal with a title and no detail does not run into the advisory line', async () => {
    renderHarness({ dryRun: () => refusal(409, 'Conflict', 'assignment.conflict') });
    const d = await ocg();
    const impact = await excludeOcg(d);
    const failed = await within(impact).findByTestId('dryrun-failed');
    expect(within(failed).getByTestId('dryrun-failed-text').textContent).toBe('Conflict');
    expect(within(failed).getByTestId('dryrun-advisory').textContent).toBe(ADVISORY_LINE);
  });
});

describe('the client composes no findings text', () => {
  it('a number with no text from the server is shown as the number alone; the server\'s text is shown when there is one', async () => {
    renderHarness({ dryRun: () => dryRunView({ affectedOpenInvoices: { count: 3, text: null, basis: 'ResolutionChanged' } }) });
    const d = await ocg();
    const impact = await excludeOcg(d);
    const line = await within(impact).findByTestId('impact-affected');
    expect(line.textContent).toBe('3');
  });
});

describe('Confirm says when what a removal closes could not be checked', () => {
  it('both the dry run and the removal preview refused: one line on Confirm, and the removal can still be saved', async () => {
    const { writeCalls } = renderHarness({
      dryRun: () => refusal(503, 'Unavailable', 'procedures.unavailable'),
      writes: respond({ preview: () => refusal(403, 'Forbidden', 'access.denied'), remove: () => removedBody() }),
    });
    const d = await instr();
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-unchecked')).toHaveTextContent(UNCHECKED_CLOSE_LINE);
    fireEvent.click(button(d, 'Remove override'));
    await within(d).findByTestId('success-body');
    expect(routesOf(writeCalls)).toEqual(['preview', 'remove']);
  });

  it('a dry run that failed without the fallback being asked (a conflict) on a removal says it too', async () => {
    renderHarness({ dryRun: () => refusal(409, 'Conflict', 'assignment.conflict'), writes: removalWrites() });
    const d = await instr();
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-unchecked')).toHaveTextContent(UNCHECKED_CLOSE_LINE);
  });

  it('not said when the dry run answered, when the removal preview answered, or when the plan removes nothing', async () => {
    // The dry run answered.
    const answered = renderHarness({ dryRun: () => dryRunView({ proposal: { kind: 'Remove', overrideKind: 'Procedure', replacesOverrideId: null, overrideId: 'row', proposedRowIds: [] } }), writes: removalWrites() });
    let d = await instr();
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    expect(within(d).queryByTestId('confirm-unchecked')).not.toBeInTheDocument();
    answered.unmount();

    // The dry run refused and the removal preview answered.
    const fallback = renderHarness({ dryRun: () => refusal(503, 'Unavailable', 'procedures.unavailable'), writes: removalWrites() });
    d = await instr();
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    expect(within(d).queryByTestId('confirm-unchecked')).not.toBeInTheDocument();
    fallback.unmount();

    // A plan that removes nothing, with the dry run refused.
    renderHarness({ dryRun: () => refusal(503, 'Unavailable', 'procedures.unavailable'), writes: respond({ assign: () => createdBody() }) });
    d = await ocg();
    await excludeOcg(d);
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    expect(within(d).queryByTestId('confirm-unchecked')).not.toBeInTheDocument();
  });
});

describe('the Impact result area is a live region', () => {
  it('is polite and busy while the read is in flight, and not busy once it has answered or failed; a failure is an alert', async () => {
    const pending: Array<(v: unknown) => void> = [];
    renderHarness({ dryRun: () => new Promise(resolve => pending.push(resolve)) });
    const d = await ocg();
    const impact = await excludeOcg(d);
    const region = within(impact).getByTestId('impact-result');
    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(region).toHaveAttribute('aria-busy', 'true');
    await waitFor(() => expect(pending).toHaveLength(1));
    await act(async () => pending[0](dryRunView()));
    await within(impact).findByTestId('dryrun');
    expect(region).toHaveAttribute('aria-busy', 'false');
    expect(within(region).queryByRole('alert', { hidden: true })).not.toBeInTheDocument();
  });

  it('a failed dry run is role="alert" inside the live region', async () => {
    renderHarness({ dryRun: () => refusal(403, 'Forbidden', 'x') });
    const d = await ocg();
    const impact = await excludeOcg(d);
    const region = within(impact).getByTestId('impact-result');
    const bar = await within(region).findByRole('alert', { hidden: true });
    expect(bar).toBe(within(region).getByTestId('dryrun-failed'));
    expect(region).toHaveAttribute('aria-busy', 'false');
  });
});
