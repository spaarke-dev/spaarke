/**
 * Task 084d-4: every wizard's Impact step reads the dry run (084d-2a/2b). The request each wizard sends, each state of the answer
 * (Now -> "If saved now", the D-170 and section 15 warnings, the close message, a count, a null count, the R6 503, another refusal), that a
 * refusal never blocks Confirm, that the removal preview is superseded, and the race of two asks.
 */

import '@testing-library/jest-dom';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { O21_LINE } from '../components/wizards/steps';
import { COUNT_UNKNOWN, R6_TEXT } from '../components/wizards/impact';
import type { ProcedureItem, RecordProceduresView, RecordRulesInForceView, RuleItem } from '../services/recordProceduresApi';
import { MATTER, MATTER_ID, TODAY, mark, matterProcedures, matterRules, override, rule } from './fixtures';
import { button, chooseRadio, createdBody, next, openProcedure, openRule, previewBody, refusal, removedBody, renderHarness, respond, toChoices, typeReason } from './wizardHarness';
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
