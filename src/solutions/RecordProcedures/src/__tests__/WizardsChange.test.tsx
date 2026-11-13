/**
 * Change for this record (ebilling-r1 task 084c), through the real panel, the real wizard shell and the real clients, with a fake BFF.
 * Rule → On this record → New values → Impact → Confirm; the server's refusal puts its field errors on the form fields.
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { RuleItem } from '../services/recordProceduresApi';
import { FENWICK, MATTER, MATTER_ID, OKAFOR, WRITE_ALL, mark, matterRules, rule } from './fixtures';
import { button, chooseRadio, createdBody, next, openRule, previewBody, radio, refusal, removedBody, renderHarness, respond, toChoices, typeReason } from './wizardHarness';

const hoursRule = (over: Partial<RuleItem> = {}): RuleItem =>
  rule({ policyId: 'r-hours', code: 'HOURS', name: 'Excessive daily hours', ruleType: 'DailyHoursCap', parameterSummary: '11 hours per timekeeper per day', ruleSetId: 'rs-hours', ...over });
const TITLE = 'HOURS · Excessive daily hours';
const withRules = (...rules: RuleItem[]) => ({ ...matterRules, rules });

const published = (body: Record<string, unknown> | null = { type: 'DailyHoursCap', maxHoursPerDay: 11 }, over: Record<string, unknown> = {}) => ({
  proposals: [{ policyId: 'R-HOURS', versionId: 'v-2', versionNumber: 2, inForce: true, ruleType: 'DailyHoursCap', ruleBody: body, ...over }],
});

const changedBody = (over: Record<string, unknown> = {}) => ({
  override: createdBody({ kind: 'RuleChanged' }),
  recordRule: { ruleSetId: 'rs-new', policyId: 'pol-new', code: 'HOURS', versionId: 'v', versionNumber: 1, basedOnVersionId: 'v-2', scope: 'Record', firmId: null },
  closeComplete: true,
  affectedText: null,
  visibleOpenInvoices: null,
  message: null,
  ...over,
});

const routes = (writeCalls: Array<{ route: string }>) => writeCalls.map(c => c.route).filter(r => r !== 'procedure');
const stepLabels = (d: HTMLElement) => within(d).getAllByRole('listitem', { hidden: true }).map(li => li.textContent ?? '').join(' | ');
const hoursInput = (d: HTMLElement) => within(d).getByLabelText(/Most hours per day/) as HTMLInputElement;

/** Choose Change with a reason, then go to New values and wait for the published values to land in the form. */
async function toValues(d: HTMLElement, reason = 'Matter staffing') {
  await toChoices(d);
  chooseRadio(d, /^Change for this record/);
  typeReason(d, reason);
  fireEvent.click(next(d));
  await within(d).findByTestId('values-step');
  await waitFor(() => expect(within(d).queryByTestId('values-loading')).not.toBeInTheDocument());
}

const base = (extra: Record<string, unknown> = {}) => ({
  rules: withRules(hoursRule(), ...(extra.more as RuleItem[] | undefined ?? [])),
});

describe('Change for this record: the flow', () => {
  it('adds the New values step between On this record and Impact, only while Change is chosen', async () => {
    renderHarness({ ...base(), writes: respond({ ruleset: () => published() }) });
    const d = await openRule('r-hours', TITLE);
    expect(stepLabels(d)).not.toMatch(/New values/);
    await toChoices(d);
    chooseRadio(d, /^Change for this record/);
    await waitFor(() => expect(stepLabels(d)).toMatch(/On this record.*New values.*Impact.*Confirm/));
    chooseRadio(d, /^Exclude here/);
    await waitFor(() => expect(stepLabels(d)).not.toMatch(/New values/));
  });

  it('prefills the form from the published values, sends the whole new body as JSON text with the reason, and re-reads both lists', async () => {
    const { writeCalls, calls } = renderHarness({ ...base(), writes: respond({ ruleset: () => published(), change: () => changedBody({ message: 'The record rule was saved.' }) }) });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    expect(routes(writeCalls)).toEqual(['ruleset']);
    expect(writeCalls[0]).toMatchObject({ method: 'GET', pathname: '/api/finance/rulesets/rs-hours' });
    expect(hoursInput(d).value).toBe('11');
    // Unchanged values are not a change: Next waits.
    expect(within(d).getByText(/These are the published values/)).toBeInTheDocument();
    expect(next(d)).toBeDisabled();

    fireEvent.change(hoursInput(d), { target: { value: '12' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    fireEvent.click(next(d));
    const confirm = await within(d).findByTestId('confirm-step');
    expect(confirm).toHaveTextContent('Most hours per day 12');
    expect(confirm).toHaveTextContent('Matter staffing');
    expect(routes(writeCalls)).toEqual(['ruleset']);

    const reads = calls.length;
    fireEvent.click(button(d, 'Save record rule'));
    const body = await within(d).findByTestId('success-body');
    expect(body).toHaveTextContent('The record rule was saved.');
    expect(within(d).getByTestId('success-o21')).toHaveTextContent('The procedures that apply to this invoice have changed; re-check to apply them.');
    expect(routes(writeCalls)).toEqual(['ruleset', 'change']);
    expect(writeCalls[1]).toMatchObject({ method: 'POST', pathname: `/api/finance/matters/${MATTER_ID}/rule-overrides/change` });
    const sent = writeCalls[1].body as { policyId: string; reason: string; ruleBody: string; firmId?: string };
    expect(sent).toEqual({ policyId: 'r-hours', reason: 'Matter staffing', ruleBody: JSON.stringify({ type: 'DailyHoursCap', maxHoursPerDay: 12 }) });
    expect(typeof sent.ruleBody).toBe('string');
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    expect(calls.slice(reads).map(c => c.leaf).sort()).toEqual(['procedures', 'rules-in-force']);
  });

  it('a firm scope chosen on the first step is sent as firmId', async () => {
    const { writeCalls } = renderHarness({ ...base(), writes: respond({ ruleset: () => published(), change: () => changedBody() }) });
    const d = await openRule('r-hours', TITLE);
    await toChoices(d);
    chooseRadio(d, /^Change for this record/);
    fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name!, hidden: true }));
    typeReason(d, 'Okafor staffing');
    fireEvent.click(next(d));
    await within(d).findByTestId('values-step');
    await waitFor(() => expect(hoursInput(d).value).toBe('11'));
    fireEvent.change(hoursInput(d), { target: { value: '9.5' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Save record rule'));
    await within(d).findByTestId('success-body');
    expect(writeCalls[1].body).toEqual({ policyId: 'r-hours', reason: 'Okafor staffing', firmId: OKAFOR.id, ruleBody: JSON.stringify({ type: 'DailyHoursCap', maxHoursPerDay: 9.5 }) });
  });

  it('a rule excluded on this record is un-excluded first (preview, DELETE), then changed, and the preview is shown before Confirm', async () => {
    const excluded = hoursRule({ inForce: false, marks: [mark('Excluded', { distance: 0, chainRecord: MATTER, overrideRowId: 'row-x', reason: 'old', firm: FENWICK })] });
    const { writeCalls } = renderHarness({
      rules: withRules(excluded),
      writes: respond({ ruleset: () => published(), preview: () => previewBody({ kind: 'RuleExcluded' }), remove: () => removedBody({ kind: 'RuleExcluded' }), change: () => changedBody() }),
    });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    fireEvent.change(hoursInput(d), { target: { value: '8' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Save record rule'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['ruleset', 'preview', 'remove', 'change']);
    expect(writeCalls[2]).toMatchObject({ method: 'DELETE', overrideId: 'row-x' });
  });
});

describe('Change for this record: the server is the authority', () => {
  it('a 400 puts each field error on its field, keeps the wizard open, and re-reads nothing; Back to New values shows them', async () => {
    const errors = [
      { policyId: 'r-hours', rule: 'DailyHoursCap', field: 'ruleBody', message: '/maxHoursPerDay: value must be at most 24', reason: 'schema' },
      { field: 'ruleBody', message: 'The rule is not valid for this record.', reason: 'record' },
    ];
    const { writeCalls, calls } = renderHarness({
      ...base(),
      writes: respond({ ruleset: () => published(), change: () => refusal(400, 'Rule change is not valid', 'ruleoverride.invalid', { errors }) }),
    });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    fireEvent.change(hoursInput(d), { target: { value: '12' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    const reads = calls.length;

    fireEvent.click(button(d, 'Save record rule'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Rule change is not valid');
    expect(within(d).getByTestId('confirm-step')).toBeInTheDocument();
    expect(within(d).queryByTestId('success-body')).not.toBeInTheDocument();
    expect(calls.length).toBe(reads);
    expect(routes(writeCalls)).toEqual(['ruleset', 'change']);
    expect(alert).toHaveTextContent(/Go back to New values/i);

    fireEvent.click(button(d, 'Back'));
    fireEvent.click(button(d, 'Back'));
    await within(d).findByTestId('values-step');
    const fieldError = within(d).getByText('value must be at most 24');
    expect(fieldError).toBeInTheDocument();
    // The error is on the field it names: inside the same Field as the input.
    expect(hoursInput(d).getAttribute('aria-describedby') ?? '').toContain(fieldError.id);
    expect(hoursInput(d)).toHaveAttribute('aria-invalid', 'true');
    expect(within(d).getByTestId('values-refused')).toHaveTextContent('The rule is not valid for this record.');
    expect(calls.length).toBe(reads);

    // An edit clears the stale error.
    fireEvent.change(hoursInput(d), { target: { value: '13' } });
    await waitFor(() => expect(within(d).queryByText('value must be at most 24')).not.toBeInTheDocument());
  });

  it('a 409 duplicate is shown with the hint to remove the earlier override; nothing is re-read', async () => {
    const { calls } = renderHarness({
      ...base(),
      writes: respond({ ruleset: () => published(), change: () => refusal(409, 'Conflict', 'assignment.duplicate', { detail: 'Already there.' }) }),
    });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    fireEvent.change(hoursInput(d), { target: { value: '12' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    const reads = calls.length;
    fireEvent.click(button(d, 'Save record rule'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Conflict');
    expect(alert).toHaveTextContent('Already there.');
    expect(calls.length).toBe(reads);
  });

  it('the page\'s own checks keep an out-of-range value from being sent at all (the server still decides the rest)', async () => {
    const { writeCalls } = renderHarness({ ...base(), writes: respond({ ruleset: () => published() }) });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    fireEvent.change(hoursInput(d), { target: { value: '30' } });
    await waitFor(() => expect(within(d).getByText(/must be at most 24/)).toBeInTheDocument());
    expect(next(d)).toBeDisabled();
    expect(routes(writeCalls)).toEqual(['ruleset']);
  });

  it('when the published values cannot be read the form says so and starts empty; the user can still enter the values', async () => {
    const { writeCalls } = renderHarness({ ...base(), writes: respond({ ruleset: () => published(null, { inForce: false }), change: () => changedBody() }) });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    expect(within(d).getByTestId('values-unread')).toHaveTextContent(/could not be read/);
    expect(hoursInput(d).value).toBe('');
    fireEvent.change(hoursInput(d), { target: { value: '12' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    expect(routes(writeCalls)).toEqual(['ruleset']);
  });
});

describe('Change for this record: who is offered it', () => {
  it('a rate card rule (ApprovedRate) offers no Change: the choice is disabled with the reason and the pin hint', async () => {
    renderHarness({ rules: withRules(hoursRule({ ruleType: 'ApprovedRate' })) });
    const d = await openRule('r-hours', TITLE);
    await toChoices(d);
    expect(radio(d, /^Change for this record/)).toBeDisabled();
    expect(within(d).getByTestId('choice-change')).toHaveTextContent(/pin that version of the Procedure/i);
  });

  it('a rule with no values (ApprovedTimekeeper) offers no Change either', async () => {
    renderHarness({ rules: withRules(hoursRule({ ruleType: 'ApprovedTimekeeper' })) });
    const d = await openRule('r-hours', TITLE);
    await toChoices(d);
    expect(radio(d, /^Change for this record/)).toBeDisabled();
    expect(within(d).getByTestId('choice-change')).toHaveTextContent(/no values to change/i);
  });

  it('without the change right the choice is disabled with the reason, and the others still work', async () => {
    renderHarness({ rules: { ...withRules(hoursRule()), permissions: { ...WRITE_ALL, change: false } }, procedures: undefined });
    const d = await openRule('r-hours', TITLE);
    await toChoices(d);
    expect(radio(d, /^Change for this record/)).toBeDisabled();
    expect(within(d).getByTestId('choice-change')).toHaveTextContent('Not available to you: you cannot change a rule on this record.');
    expect(radio(d, /^Exclude here/)).toBeEnabled();
  });

  it('a rule already changed on this record cannot be changed again until it is run as published', async () => {
    const changedHere = hoursRule({ recordRule: true, marks: [mark('Changed', { distance: 0, chainRecord: MATTER, overrideRowId: 'chg', reason: 'x', baseParameterSummary: '11' })] });
    renderHarness({ rules: withRules(changedHere) });
    const d = await openRule('r-hours', TITLE);
    await toChoices(d);
    expect(radio(d, /^Change for this record/)).toBeDisabled();
    expect(within(d).getByTestId('choice-change')).toHaveTextContent(/Run the published rule first/);
  });
});

describe('Change for this record: AddedFee lists the allowances the server cannot check, disabled, with the reason', () => {
  it('Not allowed and Allowed after N days are in the list but cannot be chosen; Capped can', async () => {
    const fee = hoursRule({ policyId: 'r-fee', code: 'FEE', name: 'Added fee', ruleType: 'AddedFee', ruleSetId: 'rs-fee' });
    renderHarness({
      rules: withRules(fee),
      writes: respond({ ruleset: () => ({ proposals: [{ policyId: 'r-fee', versionId: 'v', versionNumber: 1, inForce: true, ruleType: 'AddedFee', ruleBody: { type: 'AddedFee', feeKind: 'lateFee', allowance: 'cap', feeCodes: ['LATE1'], capAmount: 500 } }] }) }),
    });
    const d = await openRule('r-fee', 'FEE · Added fee');
    await toValues(d);
    const allowance = within(d).getByTestId('rule-field-allowance');
    fireEvent.click(allowance);
    const notAllowed = await screen.findByRole('option', { name: /Not allowed/, hidden: true });
    const afterDays = await screen.findByRole('option', { name: /Allowed after N days/, hidden: true });
    const capped = await screen.findByRole('option', { name: /^Capped/, hidden: true });
    expect(notAllowed).toHaveAttribute('aria-disabled', 'true');
    expect(afterDays).toHaveAttribute('aria-disabled', 'true');
    expect(capped).not.toHaveAttribute('aria-disabled', 'true');
    expect(within(d).getByText(/cannot check this form yet/)).toBeInTheDocument();
  });
});
