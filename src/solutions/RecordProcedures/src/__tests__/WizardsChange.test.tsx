/**
 * Change for this record (ebilling-r1 task 084c), through the real panel, the real wizard shell and the real clients, with a fake BFF.
 * Rule → On this record → New values → Impact → Confirm; the server's refusal puts its field errors on the form fields.
 */

import '@testing-library/jest-dom';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { RuleItem } from '../services/recordProceduresApi';
import { FENWICK, MATTER, MATTER_ID, OKAFOR, WRITE_ALL, mark, matterRules, rule } from './fixtures';
import { NO_VALUES_TEXT } from '../components/wizards/RuleWizard';
import { RECHANGE_NOT_NAMED } from '../components/wizards/plan';
import { DUPLICATE_REJECT_NEEDS_INVOICE_NUMBER, FEE_KIND_LOCKED } from '../components/wizards/ruleForms';
import { button, chooseRadio, createdBody, next, openRule, previewBody, radio, refusal, removedBody, renderHarness, respond, toChoices, typeReason } from './wizardHarness';

const hoursRule = (over: Partial<RuleItem> = {}): RuleItem =>
  rule({ policyId: 'r-hours', code: 'HOURS', name: 'Excessive daily hours', ruleType: 'DailyHoursCap', parameterSummary: '11 hours per timekeeper per day', ruleSetId: 'rs-hours', ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 11 }, ...over });
const TITLE = 'HOURS · Excessive daily hours';
const withRules = (...rules: RuleItem[]) => ({ ...matterRules, rules });

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

/** Choose Change with a reason, then go to New values (prefilled from the row: no read is made). */
async function toValues(d: HTMLElement, reason = 'Matter staffing') {
  await toChoices(d);
  chooseRadio(d, /^Change for this record/);
  typeReason(d, reason);
  fireEvent.click(next(d));
  await within(d).findByTestId('values-step');
}

const base = (extra: Record<string, unknown> = {}) => ({
  rules: withRules(hoursRule(), ...(extra.more as RuleItem[] | undefined ?? [])),
});

describe('Change for this record: the flow', () => {
  it('adds the New values step between On this record and Impact, only while Change is chosen', async () => {
    renderHarness({ ...base() });
    const d = await openRule('r-hours', TITLE);
    expect(stepLabels(d)).not.toMatch(/New values/);
    await toChoices(d);
    chooseRadio(d, /^Change for this record/);
    await waitFor(() => expect(stepLabels(d)).toMatch(/On this record.*New values.*Impact.*Confirm/));
    chooseRadio(d, /^Exclude here/);
    await waitFor(() => expect(stepLabels(d)).not.toMatch(/New values/));
  });

  it('prefills the form from the row (no rule-set read), sends the whole new body as JSON text with the reason, and re-reads both lists', async () => {
    const { writeCalls, calls } = renderHarness({ ...base(), writes: respond({ change: () => changedBody({ message: 'The record rule was saved.' }) }) });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    expect(routes(writeCalls)).toEqual([]);
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
    expect(routes(writeCalls)).toEqual([]);

    const reads = calls.length;
    fireEvent.click(button(d, 'Save record rule'));
    const body = await within(d).findByTestId('success-body');
    expect(body).toHaveTextContent('The record rule was saved.');
    expect(within(d).getByTestId('success-o21')).toHaveTextContent('The procedures that apply to this invoice have changed; re-check to apply them.');
    expect(routes(writeCalls)).toEqual(['change']);
    expect(writeCalls[0]).toMatchObject({ method: 'POST', pathname: `/api/finance/matters/${MATTER_ID}/rule-overrides/change` });
    const sent = writeCalls[0].body as { policyId: string; reason: string; ruleBody: string; firmId?: string };
    expect(sent).toEqual({ policyId: 'r-hours', reason: 'Matter staffing', ruleBody: JSON.stringify({ type: 'DailyHoursCap', maxHoursPerDay: 12 }) });
    expect(typeof sent.ruleBody).toBe('string');
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    expect(calls.slice(reads).map(c => c.leaf).sort()).toEqual(['procedures', 'rules-in-force']);
  });

  it('a firm scope chosen on the first step is sent as firmId', async () => {
    const { writeCalls } = renderHarness({ ...base(), writes: respond({ change: () => changedBody() }) });
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
    expect(writeCalls[0].body).toEqual({ policyId: 'r-hours', reason: 'Okafor staffing', firmId: OKAFOR.id, ruleBody: JSON.stringify({ type: 'DailyHoursCap', maxHoursPerDay: 9.5 }) });
  });

  it('a rule excluded on this record is un-excluded first (preview, DELETE), then changed, and the preview is shown before Confirm', async () => {
    const excluded = hoursRule({ inForce: false, marks: [mark('Excluded', { distance: 0, chainRecord: MATTER, overrideRowId: 'row-x', reason: 'old', firm: FENWICK })] });
    const { writeCalls } = renderHarness({
      rules: withRules(excluded),
      writes: respond({ preview: () => previewBody({ kind: 'RuleExcluded' }), remove: () => removedBody({ kind: 'RuleExcluded' }), change: () => changedBody() }),
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
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'change']);
    expect(writeCalls[1]).toMatchObject({ method: 'DELETE', overrideId: 'row-x' });
  });
});

describe('Change for this record: the server is the authority', () => {
  it('a 400 puts each field error on its field, keeps the wizard open, and re-reads nothing; Back to New values shows them', async () => {
    const errors = [
      { policyId: 'r-hours', rule: 'DailyHoursCap', field: 'ruleBody', message: '/maxHoursPerDay: 30 should be at most 24', reason: 'schema' },
      { field: 'ruleBody', message: 'The rule is not valid for this record.', reason: 'record' },
    ];
    const { writeCalls, calls } = renderHarness({
      ...base(),
      writes: respond({ change: () => refusal(400, 'Rule change is not valid', 'ruleoverride.invalid', { errors }) }),
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
    expect(routes(writeCalls)).toEqual(['change']);
    expect(alert).toHaveTextContent(/Go back to New values/i);

    fireEvent.click(button(d, 'Back'));
    fireEvent.click(button(d, 'Back'));
    await within(d).findByTestId('values-step');
    const fieldError = within(d).getByText('30 should be at most 24');
    expect(fieldError).toBeInTheDocument();
    // The error is on the field it names: inside the same Field as the input.
    expect(hoursInput(d).getAttribute('aria-describedby') ?? '').toContain(fieldError.id);
    expect(hoursInput(d)).toHaveAttribute('aria-invalid', 'true');
    expect(within(d).getByTestId('values-refused')).toHaveTextContent('The rule is not valid for this record.');
    expect(calls.length).toBe(reads);

    // An edit clears the stale error.
    fireEvent.change(hoursInput(d), { target: { value: '13' } });
    await waitFor(() => expect(within(d).queryByText('30 should be at most 24')).not.toBeInTheDocument());
  });

  it('a 409 duplicate is shown with the hint to remove the earlier override; nothing is re-read', async () => {
    const { calls } = renderHarness({
      ...base(),
      writes: respond({ change: () => refusal(409, 'Conflict', 'assignment.duplicate', { detail: 'Already there.' }) }),
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
    const { writeCalls } = renderHarness({ ...base() });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    fireEvent.change(hoursInput(d), { target: { value: '30' } });
    await waitFor(() => expect(within(d).getByText(/must be at most 24/)).toBeInTheDocument());
    expect(next(d)).toBeDisabled();
    expect(routes(writeCalls)).toEqual([]);
  });

  it('084c fix 1 (F1-2) negative: with no body from the read model the step says why, shows no form, and Next stays off (never a form missing published values)', async () => {
    const { writeCalls } = renderHarness({ rules: withRules(hoursRule({ ruleBody: null })), writes: respond({ change: () => changedBody() }) });
    const d = await openRule('r-hours', TITLE);
    await toValues(d);
    expect(within(d).getByTestId('values-unread')).toHaveTextContent(NO_VALUES_TEXT);
    expect(within(d).queryByLabelText(/Most hours per day/)).not.toBeInTheDocument();
    expect(next(d)).toBeDisabled();
    expect(routes(writeCalls)).toEqual([]);
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

  it('a rule changed on this record whose mark does not name the published rule (an older BFF) cannot be changed again until it is run as published', async () => {
    const changedHere = hoursRule({ recordRule: true, marks: [mark('Changed', { distance: 0, chainRecord: MATTER, overrideRowId: 'chg', reason: 'x', baseParameterSummary: '11' })] });
    renderHarness({ rules: withRules(changedHere) });
    const d = await openRule('r-hours', TITLE);
    await toChoices(d);
    expect(radio(d, /^Change for this record/)).toBeDisabled();
    expect(within(d).getByTestId('choice-change')).toHaveTextContent(RECHANGE_NOT_NAMED);
  });
});

describe('Change for this record: AddedFee lists the allowances the server cannot check, disabled, with the reason', () => {
  it('Not allowed and Allowed after N days are in the list but cannot be chosen; Capped can', async () => {
    const fee = hoursRule({ policyId: 'r-fee', code: 'FEE', name: 'Added fee', ruleType: 'AddedFee', ruleSetId: 'rs-fee', ruleBody: { type: 'AddedFee', feeKind: 'lateFee', allowance: 'cap', feeCodes: ['LATE1'], capAmount: 500 } });
    renderHarness({ rules: withRules(fee) });
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

// ── 084c fix round 1 ─────────────────────────────────────────────────────────

/** A rule changed on this record (9 hours) whose base rule HOURS ('r-hours') is published at 11 hours, or at 10 hours in v3 when stale. */
const changedRow = (stale: boolean) =>
  hoursRule({
    policyId: 'r-hours-rec',
    recordRule: true,
    parameterSummary: '9 hours per timekeeper per day',
    ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 9 },
    marks: [
      mark('Changed', {
        distance: 0,
        chainRecord: MATTER,
        overrideRowId: 'chg',
        reason: 'Matter staffing',
        recordRuleId: 'r-hours-rec',
        basePolicyId: 'r-hours',
        baseCode: 'HOURS',
        basedOn: { id: 'v-2', number: 2 },
        currentBase: stale ? { id: 'v-3', number: 3 } : { id: 'v-2', number: 2 },
        baseParameterSummary: stale ? '10 hours per timekeeper per day' : '11 hours per timekeeper per day',
        baseRuleBody: { type: 'DailyHoursCap', maxHoursPerDay: stale ? 10 : 11 },
      }),
      ...(stale ? [mark('Stale', { recordRuleId: 'r-hours-rec', basedOn: { id: 'v-2', number: 2 }, currentBase: { id: 'v-3', number: 3 } })] : []),
    ],
  });

describe('084c fix 1 (F1-1): a rule changed on this record can be changed again', () => {
  it.each([
    ['Changed', false],
    ['Changed and Stale', true],
  ])('%s: the form starts from this record\'s values, "Start from the published values" loads the base rule\'s, and the save removes the change, then changes the BASE rule', async (_label, stale) => {
    const { writeCalls } = renderHarness({
      rules: withRules(changedRow(stale)),
      writes: respond({ preview: () => previewBody({ kind: 'RuleChanged' }), remove: () => removedBody({ kind: 'RuleChanged' }), change: () => changedBody() }),
    });
    const d = await openRule('r-hours-rec', TITLE);
    await toValues(d);
    expect(hoursInput(d).value).toBe('9');
    expect(within(d).getByTestId('values-current')).toHaveTextContent('current values');
    expect(within(d).getByText(/These are this record’s current values/)).toBeInTheDocument();
    expect(next(d)).toBeDisabled();

    fireEvent.click(within(d).getByTestId('start-from-published'));
    await waitFor(() => expect(hoursInput(d).value).toBe(stale ? '10' : '11'));
    expect(within(d).getByText(/to run them, choose Run the published rule/)).toBeInTheDocument();
    expect(next(d)).toBeDisabled();

    fireEvent.change(hoursInput(d), { target: { value: '12' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('confirm-step')).toHaveTextContent("This record's current change is removed first.");
    fireEvent.click(button(d, 'Save record rule'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'change']);
    expect(writeCalls[1]).toMatchObject({ method: 'DELETE', overrideId: 'chg' });
    expect(writeCalls[2].body).toEqual({ policyId: 'r-hours', reason: 'Matter staffing', ruleBody: JSON.stringify({ type: 'DailyHoursCap', maxHoursPerDay: 12 }) });
  });
});

describe('084c fix 1 (F1-2): the form starts from the read model\'s body, optional values included', () => {
  it('ExpenseRule with two codes and a cap is prefilled from the row, and a changed cap is sent with both codes', async () => {
    const expense = rule({
      policyId: 'r-exp',
      code: 'EXP',
      name: 'Expenses',
      ruleType: 'ExpenseRule',
      parameterSummary: 'E101 and E102 not reimbursable; at most 250 per item',
      ruleBody: { type: 'ExpenseRule', nonReimbursableCodes: ['E101', 'E102'], maxAmountPerItem: 250 },
    });
    const { writeCalls } = renderHarness({ rules: withRules(expense), writes: respond({ change: () => changedBody() }) });
    const d = await openRule('r-exp', 'EXP · Expenses');
    await toValues(d);
    expect((within(d).getByLabelText(/Non-reimbursable expense codes/) as HTMLTextAreaElement).value).toBe('E101, E102');
    const cap = within(d).getByLabelText(/Most per expense item/) as HTMLInputElement;
    expect(cap.value).toBe('250');
    expect(next(d)).toBeDisabled();
    fireEvent.change(cap, { target: { value: '300' } });
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    fireEvent.click(button(d, 'Save record rule'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['change']);
    expect(JSON.parse((writeCalls[0].body as { ruleBody: string }).ruleBody)).toEqual({ type: 'ExpenseRule', nonReimbursableCodes: ['E101', 'E102'], maxAmountPerItem: 300 });
  });
});

describe('084c fix 1 (F1-3): a multi-choice field is a set', () => {
  it('an unchanged Duplicate whose published order is not the vocabulary\'s keeps Next off and shows no "Published rule" hint; picking again in another order changes nothing', async () => {
    const dup = rule({ policyId: 'r-dup', code: 'DUP', name: 'Duplicates', ruleType: 'Duplicate', ruleBody: { type: 'Duplicate', matchFields: ['amount', 'timekeeper'] } });
    renderHarness({ rules: withRules(dup) });
    const d = await openRule('r-dup', 'DUP · Duplicates');
    await toValues(d);
    expect(within(d).getByText(/These are the published values/)).toBeInTheDocument();
    expect(next(d)).toBeDisabled();
    expect(within(d).queryByText(/Published rule:/)).not.toBeInTheDocument();
    expect(within(d).queryByTestId('published-values')).not.toBeInTheDocument();

    const amount = within(d).getByRole('checkbox', { name: 'Amount', hidden: true });
    fireEvent.click(amount);
    fireEvent.click(amount);
    await waitFor(() => expect(within(d).getByText(/These are the published values/)).toBeInTheDocument());
    expect(next(d)).toBeDisabled();
    expect(within(d).queryByText(/Published rule:/)).not.toBeInTheDocument();
  });
});

describe('084c fix 1: a set of checkboxes in one field', () => {
  it('each checkbox has its own id and is named by its own label; the group is named by the field and described by its hint', async () => {
    const dup = rule({ policyId: 'r-dup', code: 'DUP', name: 'Duplicates', ruleType: 'Duplicate', ruleBody: { type: 'Duplicate', matchFields: ['amount'] } });
    renderHarness({ rules: withRules(dup) });
    const d = await openRule('r-dup', 'DUP · Duplicates');
    await toValues(d);
    const group = within(d).getByRole('group', { name: 'Fields compared', hidden: true });
    expect(group.getAttribute('aria-describedby')).toBeTruthy();
    const boxes = within(group).getAllByRole('checkbox', { hidden: true });
    expect(boxes).toHaveLength(6);
    expect(new Set(boxes.map(b => b.id)).size).toBe(6);
    for (const name of ['Firm invoice number', 'Timekeeper', 'Line date', 'Hours', 'Narrative', 'Amount']) {
      expect(within(group).getByRole('checkbox', { name, hidden: true })).toBeInTheDocument();
    }
  });
});

describe('084c fix 1 (F3-4): a partial save of a Change', () => {
  it('own Exclude: preview, DELETE accepted, change refused 400: the partial message does not say "go back", the lists are re-read, and Finish is held', async () => {
    let removed = false;
    const excluded = hoursRule({ inForce: false, marks: [mark('Excluded', { distance: 0, chainRecord: MATTER, overrideRowId: 'row-x', reason: 'old' })] });
    const errors = [{ policyId: 'r-hours', rule: 'DailyHoursCap', field: 'ruleBody', message: '/maxHoursPerDay: 30 should be at most 24', reason: 'schema' }];
    const { writeCalls, calls } = renderHarness({
      // After the removal the row is gone from the re-read: the wizard is held on a row that is out of date.
      rules: () => (removed ? withRules() : withRules(excluded)),
      writes: respond({
        preview: () => previewBody({ kind: 'RuleExcluded' }),
        remove: () => {
          removed = true;
          return removedBody({ kind: 'RuleExcluded' });
        },
        change: () => refusal(400, 'Rule change is not valid', 'ruleoverride.invalid', { errors }),
      }),
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
    const reads = calls.length;

    fireEvent.click(button(d, 'Save record rule'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Rule change is not valid');
    expect(alert).toHaveTextContent("The record's earlier exclusion was removed but the change was not saved. Close this window and open the rule again.");
    expect(alert).not.toHaveTextContent(/Go back to New values/);
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    await act(async () => {});
    expect(button(d, 'Save record rule')).toBeDisabled();
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'change']);
  });
});

describe('084c fix 1 (item 6): a Duplicate that Rejects keeps Firm invoice number', () => {
  it.each([
    ['Reject', true],
    ['Flag', false],
  ])('consequence %s: un-ticking Firm invoice number is refused on the field before sending: %s', async (consequence, refused) => {
    const dup = rule({
      policyId: 'r-dup',
      code: 'DUP',
      name: 'Duplicates',
      ruleType: 'Duplicate',
      consequence,
      ruleBody: { type: 'Duplicate', matchFields: ['firmInvoiceNumber', 'amount'] },
    });
    const { writeCalls } = renderHarness({ rules: withRules(dup) });
    const d = await openRule('r-dup', 'DUP · Duplicates');
    await toValues(d);
    fireEvent.click(within(d).getByRole('checkbox', { name: 'Firm invoice number', hidden: true }));
    if (refused) {
      await waitFor(() => expect(within(d).getByText(DUPLICATE_REJECT_NEEDS_INVOICE_NUMBER)).toBeInTheDocument());
      expect(next(d)).toBeDisabled();
    } else {
      await waitFor(() => expect(next(d)).toBeEnabled());
      expect(within(d).queryByText(DUPLICATE_REJECT_NEEDS_INVOICE_NUMBER)).not.toBeInTheDocument();
    }
    expect(routes(writeCalls)).toEqual([]);
  });
});

describe('084c fix 1 (item 9): AddedFee cannot move to another precedence key', () => {
  it.each([
    ['lateFee', false],
    ['other', true],
  ])('fee kind %s: the fee kind is read-only with the reason; the fee codes are read-only too: %s', async (feeKind, codesLocked) => {
    const fee = rule({
      policyId: 'r-fee',
      code: 'FEE',
      name: 'Added fee',
      ruleType: 'AddedFee',
      ruleBody: { type: 'AddedFee', feeKind, allowance: 'cap', feeCodes: ['LATE1'], capAmount: 500 },
    });
    renderHarness({ rules: withRules(fee) });
    const d = await openRule('r-fee', 'FEE · Added fee');
    await toValues(d);
    expect(within(d).getByTestId('rule-field-feeKind')).toBeDisabled();
    expect(within(d).getByTestId('values-step')).toHaveTextContent(FEE_KIND_LOCKED);
    if (codesLocked) expect(within(d).getByTestId('rule-field-feeCodes')).toBeDisabled();
    else expect(within(d).getByTestId('rule-field-feeCodes')).toBeEnabled();
    expect(within(d).getByTestId('rule-field-capAmount')).toBeEnabled();
  });
});
