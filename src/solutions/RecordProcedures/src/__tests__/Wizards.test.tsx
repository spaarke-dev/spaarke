import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { O21_LINE } from '../components/wizards/steps';
import type { ProcedureItem, RecordProceduresView, RecordRulesInForceView, RuleItem } from '../services/recordProceduresApi';
import { FENWICK, MATTER, MATTER_ID, OKAFOR, TODAY, V, WA_ID, mark, matterProcedures, matterRules, override, procedure, rule, waProcedures, waRules } from './fixtures';
import {
  button,
  chooseRadio,
  createdBody,
  next,
  openProcedure,
  openRule,
  previewBody,
  procedureBody,
  radio,
  ready,
  refusal,
  removedBody,
  renderHarness,
  respond,
  toChoices,
  toConfirm,
  typeReason,
} from './wizardHarness';

const parentOf = { ...MATTER, distance: 1 };

/** The Impact step's result area (the live region the answer or its refusal is in). */
const impactOf = (d: HTMLElement) => within(d).getByTestId('impact-result');

/** Pick an option of the one open Fluent dropdown inside the wizard. */
async function pick(d: HTMLElement, optionName: string | RegExp) {
  fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
  fireEvent.click(await screen.findByRole('option', { name: optionName, hidden: true }));
}

const versions = procedureBody([
  { id: 'ocg-v1', n: 1, status: 'Superseded', from: '2025-01-01', to: '2026-01-01' },
  { id: 'ocg-v2', n: 2, status: 'Active', from: '2026-01-01' },
  { id: 'ocg-v3', n: 3, status: 'Draft', from: null },
]);

const O21_TEXT = 'The procedures that apply to this invoice have changed; re-check to apply them.';

/** The write ends in the success screen, which says nothing was re-checked. */
async function finish(d: HTMLElement, label: string) {
  fireEvent.click(button(d, label));
  const body = await within(d).findByTestId('success-body');
  expect(within(d).getByTestId('success-o21')).toHaveTextContent(O21_TEXT);
  return body;
}

/** The dialog's title, read from the element it is labelled by (the dialog itself stays when the shell moves to another row). */
const titleOf = (d: HTMLElement) => document.getElementById(d.getAttribute('aria-labelledby') ?? '')?.textContent ?? null;

const routes = (writeCalls: Array<{ route: string }>) => writeCalls.map(c => c.route).filter(r => r !== 'procedure');

describe('the Procedure wizard sends the request 089\'s route expects, and re-reads both lists', () => {
  it('Pin a version: POST procedure-assignments { procedureId, mode: Pinned, pinnedVersionId, reason }; a Draft version is not offered', async () => {
    const { writeCalls, calls } = renderHarness({
      writes: respond({ procedure: () => versions, assign: () => createdBody({ id: 'row-new', mode: 'Pinned' }) }),
    });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Pin a version/);
    await waitFor(() => expect(writeCalls.map(c => c.route)).toContain('procedure'));
    fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
    expect(await screen.findByRole('option', { name: /v2/, hidden: true })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /v3/, hidden: true })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('option', { name: /^v1/, hidden: true }));
    typeReason(d, 'Keep the 2025 rates');
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-step')).toHaveTextContent('Keep the 2025 rates');

    const reads = calls.length;
    await finish(d, 'Save override');

    const assign = writeCalls.find(c => c.route === 'assign')!;
    expect(assign.method).toBe('POST');
    expect(assign.pathname).toBe(`/api/finance/matters/${MATTER_ID}/procedure-assignments`);
    expect(assign.headers).toEqual({ 'Content-Type': 'application/json' });
    expect(assign.body).toEqual({ procedureId: 'p-ocg', mode: 'Pinned', pinnedVersionId: 'ocg-v1', reason: 'Keep the 2025 rates' });
    expect(writeCalls.map(c => c.route)).toEqual(['procedure', 'assign']);

    // Both lists are read again, for the same firm and date.
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    expect(calls.slice(reads).map(c => c.leaf).sort()).toEqual(['procedures', 'rules-in-force']);
    for (const c of calls.slice(reads)) expect(c.query).toEqual(calls[0].query);
  });

  it('Exclude here: POST procedure-assignments { procedureId, mode: Excluded, reason }', async () => {
    const { writeCalls } = renderHarness({ writes: respond({ assign: () => createdBody({ mode: 'Excluded' }) }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Not travelling');
    await toConfirm(d);
    await finish(d, 'Save override');
    expect(writeCalls).toHaveLength(1);
    expect(writeCalls[0].body).toEqual({ procedureId: 'p-ocg', mode: 'Excluded', reason: 'Not travelling' });
  });

  it('Follow latest over a parent\'s pin: POST procedure-assignments { procedureId, mode: FollowLatest } and nothing else', async () => {
    const { writeCalls, calls } = renderHarness({
      kind: 'workassignment',
      procedures: waProcedures,
      rules: waRules,
      writes: respond({ assign: () => createdBody() }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    // The row is held by two conflicting pins: its current choice is the parents'.
    expect(radio(d, /^As set on the parent/)).toBeChecked();
    chooseRadio(d, /^Follow latest/);
    await toConfirm(d);
    const reads = calls.length;
    await finish(d, 'Save override');
    expect(writeCalls).toHaveLength(1);
    expect(writeCalls[0].pathname).toBe(`/api/finance/workassignments/${WA_ID}/procedure-assignments`);
    expect(writeCalls[0].body).toEqual({ procedureId: 'p-rate', mode: 'FollowLatest' });
    await waitFor(() => expect(calls.length).toBe(reads + 2));
  });

  it('Follow latest on an excluded row removes the override: GET its removal-preview, then DELETE overrides/{id}', async () => {
    const { writeCalls, calls } = renderHarness({
      writes: respond({ preview: () => previewBody({ overrideId: 'row-excl' }), remove: () => removedBody({ overrideId: 'row-excl' }) }),
    });
    const d = await openProcedure('p-travel', 'Fenwick travel policy');
    await toChoices(d);
    expect(radio(d, /^Exclude here/)).toBeChecked();
    chooseRadio(d, /^Follow latest/);
    await toConfirm(d);
    const reads = calls.length;
    await finish(d, 'Remove override');
    expect(routes(writeCalls)).toEqual(['preview', 'remove']);
    expect(writeCalls[0]).toMatchObject({ method: 'GET', pathname: `/api/finance/matters/${MATTER_ID}/overrides/row-excl/removal-preview`, body: undefined });
    expect(writeCalls[1]).toMatchObject({ method: 'DELETE', pathname: `/api/finance/matters/${MATTER_ID}/overrides/row-excl`, body: undefined });
    await waitFor(() => expect(calls.length).toBe(reads + 2));
  });

  it('Remove from this record: the preview, then DELETE overrides/{id}', async () => {
    const { writeCalls } = renderHarness({ writes: respond({ preview: () => previewBody(), remove: () => removedBody() }) });
    const d = await openProcedure('p-instr', 'Acme v. Northwind billing instructions');
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    await finish(d, 'Remove override');
    expect(writeCalls.map(c => `${c.method} ${c.pathname.split(`${MATTER_ID}/`)[1]}`)).toEqual(['GET overrides/row-instr/removal-preview', 'DELETE overrides/row-instr']);
  });

  it('As set on the parent: the record\'s own override is removed', async () => {
    const own = override({ rowId: 'own-pin', mode: 'Pinned', chainRecord: MATTER, pinnedVersion: V.rate1 });
    const par = override({ rowId: 'par-pin', mode: 'Pinned', chainRecord: parentOf, pinnedVersion: V.rate2 });
    const view: RecordProceduresView = {
      ...matterProcedures,
      gaps: [],
      procedures: [procedure({ id: 'p-x', name: 'Rates with a parent', appliesTo: 'Firm', firm: FENWICK, pinned: true, overrides: [own, par] })],
    };
    const { writeCalls } = renderHarness({ procedures: view, writes: respond({ preview: () => previewBody(), remove: () => removedBody() }) });
    const d = await openProcedure('p-x', 'Rates with a parent');
    await toChoices(d);
    expect(radio(d, /^Pin a version/)).toBeChecked();
    chooseRadio(d, /^As set on the parent/);
    await toConfirm(d);
    await finish(d, 'Remove override');
    expect(writeCalls.filter(c => c.route !== 'procedure').map(c => `${c.method} ${c.overrideId}`)).toEqual(['GET own-pin', 'DELETE own-pin']);
  });

  it('changing an existing override is its removal, then the new row, in that order', async () => {
    const { writeCalls } = renderHarness({
      writes: respond({ preview: () => previewBody(), remove: () => removedBody(), assign: () => createdBody({ mode: 'Excluded' }) }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    expect(radio(d, /^Pin a version/)).toBeChecked();
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Rates no longer apply');
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-step')).toHaveTextContent('current override is removed, then the new one is saved');
    await finish(d, 'Save override');
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'assign']);
    const ws = writeCalls.filter(c => c.route !== 'procedure');
    expect(ws[1].overrideId).toBe('row-pin');
    expect(ws[2].body).toEqual({ procedureId: 'p-rate', mode: 'Excluded', reason: 'Rates no longer apply' });
  });
});

describe('the rule wizard', () => {
  it('Exclude here, for every firm: POST rule-overrides/exclude { policyId, reason } with no firmId', async () => {
    const { writeCalls, calls } = renderHarness({ writes: respond({ exclude: () => createdBody({ kind: 'RuleExcluded' }) }) });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    expect(within(d).getByRole('combobox', { hidden: true })).toHaveTextContent('All firms');
    typeReason(d, 'Fenwick bills in a fixed format');
    await toConfirm(d);
    const reads = calls.length;
    await finish(d, 'Exclude rule');
    expect(writeCalls).toHaveLength(1);
    expect(writeCalls[0]).toMatchObject({ method: 'POST', pathname: `/api/finance/matters/${MATTER_ID}/rule-overrides/exclude` });
    expect(writeCalls[0].body).toEqual({ policyId: 'r-block', reason: 'Fenwick bills in a fixed format' });
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    expect(calls.slice(reads).map(c => c.leaf).sort()).toEqual(['procedures', 'rules-in-force']);
  });

  it('Exclude here, for one firm: the body carries that firm\'s id', async () => {
    const { writeCalls } = renderHarness({ writes: respond({ exclude: () => createdBody() }) });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    await pick(d, OKAFOR.name!);
    typeReason(d, 'Okafor invoices are exempt');
    await toConfirm(d);
    expect(within(d).getByTestId('confirm-step')).toHaveTextContent(OKAFOR.name!);
    await finish(d, 'Exclude rule');
    expect(writeCalls[0].body).toEqual({ policyId: 'r-block', reason: 'Okafor invoices are exempt', firmId: OKAFOR.id });
  });

  it('O-20: a rule that names one firm is excluded for that firm only: the field is fixed, and the id is sent', async () => {
    const firmRule = rule({ policyId: 'r-firm', code: 'FIRM1', name: 'Fenwick-only rule', firmScope: FENWICK, parameterSummary: 'Flag' });
    const { writeCalls } = renderHarness({ rules: { ...matterRules, rules: [firmRule] }, writes: respond({ exclude: () => createdBody() }) });
    const d = await openRule('r-firm', 'FIRM1 · Fenwick-only rule');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    const firmField = within(d).getByRole('combobox', { hidden: true });
    expect(firmField).toBeDisabled();
    expect(firmField).toHaveTextContent(FENWICK.name!);
    expect(within(d).getByText(/can only be excluded or changed for that firm \(O-20\)/)).toBeInTheDocument();
    typeReason(d, 'Rule is not relevant');
    await toConfirm(d);
    await finish(d, 'Exclude rule');
    expect(writeCalls[0].body).toEqual({ policyId: 'r-firm', reason: 'Rule is not relevant', firmId: FENWICK.id });
  });

  it('Run the published rule removes this record\'s own exclusion: preview, then DELETE', async () => {
    const { writeCalls } = renderHarness({ writes: respond({ preview: () => previewBody({ kind: 'RuleExcluded' }), remove: () => removedBody({ kind: 'RuleExcluded' }) }) });
    const d = await openRule('r-vague', 'VAGUE · Vague description');
    await toChoices(d);
    expect(radio(d, /^Exclude here/)).toBeChecked();
    chooseRadio(d, /^Run the published rule/);
    await toConfirm(d);
    await finish(d, 'Remove override');
    expect(writeCalls.map(c => `${c.method} ${c.overrideId}`)).toEqual(['GET row-vague', 'DELETE row-vague']);
  });

  it('a rule changed on this record is run as published through its override row; the preview\'s warning is shown before Confirm', async () => {
    const changed: RuleItem = rule({
      policyId: 'r-chg',
      code: 'HOURS',
      name: 'Excessive daily hours',
      recordRule: true,
      parameterSummary: '12 hours per timekeeper per day',
      marks: [mark('Changed', { distance: 0, chainRecord: MATTER, overrideRowId: 'chg-row', reason: 'Staffing', baseParameterSummary: '11 hours per timekeeper per day', stamp: { byId: 'u', byName: 'Dana', on: '2026-09-02T00:00:00Z' } })],
    });
    const warning = 'Removing this override will close the rule it changed and remove pending decisions on active invoices.';
    const { writeCalls } = renderHarness({
      rules: { ...matterRules, rules: [changed] },
      writes: respond({
        preview: () => previewBody({ kind: 'RuleChanged', closesRule: true, warning, affectedText: '4 open invoices you can read would have findings closed.', visibleOpenInvoices: 4 }),
        remove: () => removedBody({ kind: 'RuleChanged', ruleSwitchedOff: true, message: 'The record rule was switched off.' }),
      }),
    });
    const d = await openRule('r-chg', 'HOURS · Excessive daily hours');
    await toChoices(d);
    // Exclude is not offered over a change made here.
    expect(radio(d, /^Exclude here/)).toBeDisabled();
    chooseRadio(d, /^Run the published rule/);
    fireEvent.click(next(d));
    const preview = await within(d).findByTestId('removal-preview');
    expect(preview).toHaveTextContent(warning);
    expect(preview).toHaveTextContent('4 open invoices you can read would have findings closed.');
    expect(routes(writeCalls)).toEqual(['preview']);
    await waitFor(() => expect(next(d)).toBeEnabled());
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    const body = await finish(d, 'Remove override');
    expect(body).toHaveTextContent('The record rule was switched off.');
    expect(routes(writeCalls)).toEqual(['preview', 'remove']);
  });
});

describe('P-3: a rule a parent excluded', () => {
  const excludedByParent: RuleItem = rule({
    policyId: 'r-vague',
    code: 'VAGUE',
    name: 'Vague description',
    parameterSummary: 'Flag for review',
    inForce: false,
    marks: [mark('Excluded', { reason: 'Fenwick bills in a fixed format', firm: FENWICK, distance: 1, chainRecord: parentOf, overrideRowId: 'row-parent', stamp: { byId: 'u', byName: 'Dana Whitfield', on: '2026-09-02T00:00:00Z' } })],
  });
  const rules: RecordRulesInForceView = { ...waRules, rules: [excludedByParent] };

  it('shows Exclude as applied (As set on the parent, selected) and offers no Run the published rule: it is disabled, with the reason', async () => {
    const { writeCalls } = renderHarness({ kind: 'workassignment', procedures: waProcedures, rules });
    const d = await openRule('r-vague', 'VAGUE · Vague description');
    expect(within(d).getByTestId('excluded-note')).toHaveTextContent('Excluded for Fenwick & Calder LLP invoices on matter 2026-LIT-4471');
    await toChoices(d);

    expect(radio(d, /^As set on the parent/)).toBeChecked();
    expect(within(d).getByTestId('choice-parent')).toHaveTextContent('The rule is excluded on matter 2026-LIT-4471');
    expect(radio(d, /^Run the published rule/)).toBeDisabled();
    expect(within(d).getByTestId('choice-keep')).toHaveTextContent("Not available: the parent's rule exclusion applies to this record.");

    // Clicking the disabled choice changes nothing; nothing can be advanced to; nothing is sent.
    fireEvent.click(radio(d, /^Run the published rule/));
    expect(radio(d, /^As set on the parent/)).toBeChecked();
    expect(next(d)).toBeDisabled();
    expect(writeCalls).toHaveLength(0);
  });

  it('the same holds for a rule a parent CHANGED', async () => {
    const changedByParent: RuleItem = rule({
      policyId: 'r-chg',
      code: 'HOURS',
      recordRule: true,
      marks: [mark('Changed', { distance: 1, chainRecord: parentOf, overrideRowId: 'row-parent' })],
    });
    renderHarness({ kind: 'workassignment', procedures: waProcedures, rules: { ...waRules, rules: [changedByParent] } });
    const d = await openRule('r-chg', 'HOURS');
    await toChoices(d);
    expect(radio(d, /^As set on the parent/)).toBeChecked();
    expect(radio(d, /^Run the published rule/)).toBeDisabled();
    expect(within(d).getByTestId('choice-keep')).toHaveTextContent("the parent's rule change applies to this record");
  });
});

describe('Impact and the removal preview', () => {
  it('a removal shows 089\'s preview (the affected invoices) BEFORE Confirm; Next waits for it; nothing is deleted until Finish', async () => {
    let release!: (v: unknown) => void;
    const pending = new Promise(res => (release = res));
    const { writeCalls } = renderHarness({
      writes: respond({ preview: () => pending, remove: () => removedBody({ overrideId: 'row-excl' }) }),
    });
    const d = await openProcedure('p-travel', 'Fenwick travel policy');
    await toChoices(d);
    chooseRadio(d, /^Follow latest/);
    fireEvent.click(next(d));
    await within(d).findByTestId('impact-step');
    expect(await within(d).findByTestId('preview-loading')).toBeInTheDocument();
    expect(next(d)).toBeDisabled();
    expect(routes(writeCalls)).toEqual(['preview']);

    await act(async () => release(previewBody({ overrideId: 'row-excl', visibleOpenInvoices: 3 })));
    expect(await within(d).findByTestId('removal-preview')).toHaveTextContent('3 open invoices you can see are affected.');
    expect(next(d)).toBeEnabled();
    expect(routes(writeCalls)).toEqual(['preview']);
    fireEvent.click(next(d));
    await within(d).findByTestId('confirm-step');
    // Still nothing deleted at Confirm.
    expect(routes(writeCalls)).toEqual(['preview']);
    await finish(d, 'Remove override');
    expect(routes(writeCalls)).toEqual(['preview', 'remove']);
  });

  it('a plain removal says so when the preview has no count', async () => {
    renderHarness({ writes: respond({ preview: () => previewBody() }) });
    const d = await openProcedure('p-travel', 'Fenwick travel policy');
    await toChoices(d);
    chooseRadio(d, /^Follow latest/);
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('removal-preview')).toHaveTextContent('Removing this override closes no findings on open invoices.');
  });

  it('a change that removes nothing shows the row\'s own before and after and asks for no preview', async () => {
    const { writeCalls } = renderHarness({ writes: respond({ assign: () => createdBody() }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'r');
    fireEvent.click(next(d));
    const impact = await within(d).findByTestId('impact-step');
    expect(within(impact).getByTestId('impact-change')).toHaveTextContent('In force · v2→Excluded here');
    expect(within(impact).queryByTestId('removal-preview')).not.toBeInTheDocument();
    expect(writeCalls).toHaveLength(0);
  });

  it('O-21: every change says nothing is re-checked, on the Impact step and again on the success screen', async () => {
    renderHarness({ writes: respond({ assign: () => createdBody(), preview: () => previewBody(), remove: () => removedBody() }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'r');
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('o21-line')).toHaveTextContent(O21_LINE);
    expect(O21_LINE).toContain(`"${O21_TEXT}"`);
    expect(O21_LINE).toContain('Nothing is re-checked automatically.');
    await waitFor(() => expect(next(d)).toBeEnabled()); // 084d-4: Next waits for the dry run's answer
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('confirm-step')).toHaveTextContent('Nothing is re-checked automatically.');
    await finish(d, 'Save override');
  });
});

describe('a refusal keeps the wizard open and re-reads nothing', () => {
  const refusals: Array<[number, string, string]> = [
    [403, 'Forbidden', 'access.denied'],
    [404, 'Not Found', 'assignment.override_not_found'],
    [409, 'Conflict', 'assignment.duplicate'],
  ];

  it.each(refusals)('%s (%s) on an Exclude: the ProblemDetails title is shown, the wizard stays open on its last step, no list is re-read', async (status, title, code) => {
    const { writeCalls, calls } = renderHarness({ writes: respond({ exclude: () => refusal(status, title, code, { detail: 'Detail from the server.' }) }) });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'A reason');
    await toConfirm(d);
    const reads = calls.length;

    fireEvent.click(button(d, 'Exclude rule'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent(title);
    expect(alert).toHaveTextContent('Detail from the server.');

    // Still open, still on Confirm, can try again; no success screen, no re-read.
    expect(d).toBeInTheDocument();
    expect(within(d).getByTestId('confirm-step')).toBeInTheDocument();
    expect(button(d, 'Exclude rule')).toBeEnabled();
    expect(within(d).queryByTestId('success-body')).not.toBeInTheDocument();
    expect(calls.length).toBe(reads);
    expect(routes(writeCalls)).toEqual(['exclude']);

    // The user can retry; a second refusal is the same.
    fireEvent.click(button(d, 'Exclude rule'));
    await waitFor(() => expect(routes(writeCalls)).toEqual(['exclude', 'exclude']));
    expect(calls.length).toBe(reads);
  });

  it('a refused Pin (the first thing the route says) and a refused removal show their titles too', async () => {
    const pin = renderHarness({ writes: respond({ procedure: () => versions, assign: () => refusal(403, 'Forbidden', 'access.denied') }) });
    let d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    chooseRadio(d, /^Pin a version/);
    await pick(d, /^v1/);
    typeReason(d, 'r');
    await toConfirm(d);
    const reads = pin.calls.length;
    fireEvent.click(button(d, 'Save override'));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('Forbidden');
    expect(pin.calls.length).toBe(reads);
    pin.unmount();

    const removal = renderHarness({ writes: respond({ preview: () => previewBody(), remove: () => refusal(404, 'Not Found', 'assignment.override_not_found') }) });
    d = await openProcedure('p-instr', 'Acme v. Northwind billing instructions');
    await toChoices(d);
    chooseRadio(d, /^Remove from this record/);
    await toConfirm(d);
    const reads2 = removal.calls.length;
    fireEvent.click(button(d, 'Remove override'));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('Not Found');
    expect(removal.calls.length).toBe(reads2);
    expect(within(d).queryByTestId('success-body')).not.toBeInTheDocument();
  });

  // 084d-4 changed this: the preview is advisory (the dry run's refusal and the removal preview's alike), so a refusal no longer blocks Next.
  it('a refused removal PREVIEW (the fallback after a refused dry run) is ONE message with the preview\'s title, does not block Next, deletes nothing, and Retry asks the dry run again', async () => {
    let attempts = 0;
    const { writeCalls, dryRunCalls } = renderHarness({
      writes: respond({
        preview: () => (++attempts === 1 ? refusal(403, 'Forbidden', 'access.denied') : previewBody()),
        remove: () => removedBody(),
      }),
    });
    const d = await openProcedure('p-travel', 'Fenwick travel policy');
    await toChoices(d);
    chooseRadio(d, /^Follow latest/);
    fireEvent.click(next(d));
    expect(await within(d).findByTestId('preview-failed')).toHaveTextContent('Forbidden');
    expect(next(d)).toBeEnabled();
    // One message, not a warning bar plus an error alert.
    expect(within(d).queryByTestId('dryrun-failed')).not.toBeInTheDocument();
    expect(within(impactOf(d)).getAllByRole('alert', { hidden: true })).toHaveLength(1);
    expect(routes(writeCalls)).toEqual(['preview']);
    expect(dryRunCalls).toHaveLength(1);

    // Retry forgets both failures and asks the dry run again; it is refused again, so the preview is asked again and now answers.
    fireEvent.click(within(d).getByTestId('preview-retry'));
    expect(await within(d).findByTestId('removal-preview')).toBeInTheDocument();
    expect(dryRunCalls).toHaveLength(2);
    expect(routes(writeCalls)).toEqual(['preview', 'preview']);
    expect(next(d)).toBeEnabled();
  });

  it('a switch whose first call (the removal) succeeded and second was refused says so, and re-reads the lists because the record changed', async () => {
    const { writeCalls, calls } = renderHarness({
      writes: respond({ preview: () => previewBody(), remove: () => removedBody(), assign: () => refusal(409, 'Conflict', 'assignment.procedure_retired') }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'r');
    await toConfirm(d);
    const reads = calls.length;
    fireEvent.click(button(d, 'Save override'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Conflict');
    expect(alert).toHaveTextContent("The record's earlier override was removed but the new one was not saved");
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'assign']);
    await waitFor(() => expect(calls.length).toBe(reads + 2));
  });

  it('the wizard code reads nothing about the caller (role, privilege, permission): the route decides (control: the scan sees one)', () => {
    const root = path.join(__dirname, '..', 'components', 'wizards');
    // (`role="alert"` is an ARIA attribute, not a role of the caller: only the plural and the user-role names count.)
    const caller = /\b(caller|userRoles?|securityRoles?|getUserRoles|roles|privileges?|permissions?|isAdmin|canWrite|canEdit|canDelete|userSettings)\b/i;
    // Comments may say what the page does not do; the scan reads code only.
    const code = (src: string) => src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
    const files = fs.readdirSync(root).filter(f => /\.(ts|tsx)$/.test(f));
    expect(files.length).toBeGreaterThanOrEqual(6);
    for (const f of files) expect([f, caller.test(code(fs.readFileSync(path.join(root, f), 'utf8')))]).toEqual([f, false]);
    expect(caller.test('const roles = Xrm.Utility.getGlobalContext().userSettings.roles')).toBe(true);
    expect(caller.test('if (!user.canWrite) return;')).toBe(true);
  });
});

describe('the Change choice belongs to the rule wizard, and nothing here dry-runs a change', () => {
  it('a rule whose type this page has no form for lists Change for this record disabled with the reason, and no New values step', async () => {
    const { writeCalls } = renderHarness();
    const d = await openRule('r-block', 'BLOCK · Block billing');
    const steps = within(d).getAllByRole('listitem', { hidden: true }).map(li => li.textContent);
    expect(steps.join(' ')).not.toMatch(/New values/);
    await toChoices(d);
    expect(within(d).getAllByRole('radio', { hidden: true }).map(r => r.closest('[data-testid]')?.getAttribute('data-testid'))).toEqual(['choice-keep', 'choice-exclude', 'choice-change']);
    expect(radio(d, /^Change for this record/)).toBeDisabled();
    expect(within(d).getByTestId('choice-change')).toHaveTextContent(/no form for the rule type/);
    expect(within(d).queryByText(/dry.?run/i)).not.toBeInTheDocument();
    expect(writeCalls).toHaveLength(0);
  });

  it('the Procedure wizard has no Change choice either, on any of the six rows', async () => {
    renderHarness({ writes: respond({ procedure: () => versions }) });
    await ready();
    for (const id of ['p-ocg', 'p-rate', 'p-travel', 'p-instr', 'p-fee', 'p-next']) {
      fireEvent.click(screen.getByTestId(`procedure-row-${id}`));
      const d = await screen.findByRole('dialog', { hidden: true });
      await toChoices(d);
      const labels = within(d).getAllByRole('radio', { hidden: true }).map(r => (r as HTMLInputElement).labels?.[0]?.textContent ?? '');
      expect(labels.length).toBeGreaterThan(1);
      for (const l of labels) expect(l).not.toMatch(/^Change/i);
      fireEvent.click(button(d, 'Cancel'));
      await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());
    }
  });

  it('the dry-run route is named in dryRunApi.ts only: EVERY non-test source file under src is scanned (must-fire and must-not-fire controls), and the Procedure wizard has no change call at all', () => {
    // 084d-4 replaced 084b's "no dry run" guard: the Impact step now reads the dry run, through the one services module that names its route.
    // Review round 1: the scan walks the whole tree (as the write scan below does), not a list of files, so a new file cannot slip past it.
    const root = path.join(__dirname, '..');
    const forbidden = /['"`/]dry-run\b/i;
    // Comments may say what the page does not do; the scan reads code only.
    const stripComments = (src: string) => src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
    const namingTheRoute = (files: readonly string[], read: (f: string) => string): string[] => files.filter(f => forbidden.test(stripComments(read(f)))).map(f => f.replace(/\\/g, '/')).sort();
    const walk = (dir: string): string[] =>
      fs.readdirSync(path.join(root, dir), { withFileTypes: true }).flatMap(e => (e.isDirectory() ? (e.name === '__tests__' || e.name === 'node_modules' ? [] : walk(path.join(dir, e.name))) : [path.join(dir, e.name)]));
    const read = (f: string) => fs.readFileSync(path.join(root, f), 'utf8');
    const sources = walk('.').filter(f => /\.(ts|tsx)$/.test(f));
    expect(sources.length).toBeGreaterThan(30); // the scan sees the whole tree, not a short list
    expect(sources.map(f => f.replace(/\\/g, '/'))).toEqual(expect.arrayContaining(['main.tsx', 'services/dryRunApi.ts', 'components/wizards/steps.tsx', 'components/ProceduresPanel.tsx']));
    expect(namingTheRoute(sources, read)).toEqual(['services/dryRunApi.ts']);

    // Must fire: a second file that names the route (in a template, a string or a path) is caught.
    const fake: Record<string, string> = {
      'services/dryRunApi.ts': '`${base}/procedures/dry-run?x`',
      'components/Other.tsx': "fetch('/api/finance/matters/1/procedures/dry-run')",
      'components/Third.ts': 'const u = "dry-run";',
    };
    expect(namingTheRoute(Object.keys(fake), f => fake[f])).toEqual(['components/Other.tsx', 'components/Third.ts', 'services/dryRunApi.ts']);
    // Must not fire: a comment, and a name that only looks like it (dryRun, dry-running without a leading quote or slash).
    const quiet: Record<string, string> = {
      'a.ts': '// the dry-run route is named in dryRunApi.ts\nconst dryRun = 1;',
      'b.ts': '/* "dry-run" in a block comment */ const x = dryRunKey;',
      'c.ts': 'const label = data-dryrun-x; const y = a-dry-run;',
    };
    expect(namingTheRoute(Object.keys(quiet), f => quiet[f])).toEqual([]);

    const code = (f: string) => stripComments(read(f));
    // The change route is called from the one api module, and only the rule wizard's plan reaches it.
    expect(/changeRule|rule-overrides\/change/.test(code('components/wizards/ProcedureWizard.tsx'))).toBe(false);
    expect(/rule-overrides\/change/.test(code('services/overridesApi.ts')) || /changeRuleUrl/.test(code('services/overridesApi.ts'))).toBe(true);
  });

  it('the only writes in the page are in overridesApi.ts, and they are the routes of the task (control: the scan sees one); dryRunApi.ts POSTs the dry run only', () => {
    const root = path.join(__dirname, '..');
    const write = /method:\s*['"](POST|PUT|PATCH|DELETE)['"]/g;
    const walk = (dir: string): string[] => fs.readdirSync(path.join(root, dir), { withFileTypes: true }).flatMap(e => (e.isDirectory() ? (e.name === '__tests__' ? [] : walk(path.join(dir, e.name))) : [path.join(dir, e.name)]));
    const withWrites = walk('.').filter(f => /\.(ts|tsx)$/.test(f) && write.test(fs.readFileSync(path.join(root, f), 'utf8')));
    write.lastIndex = 0;
    // 084d-4: the dry run is a POST that writes nothing (084d-2a); its module names that one route and no other.
    expect(withWrites.map(f => f.replace(/\\/g, '/'))).toEqual(['services/dryRunApi.ts', 'services/overridesApi.ts']);
    const dry = fs.readFileSync(path.join(root, 'services/dryRunApi.ts'), 'utf8');
    expect([...dry.matchAll(/method:\s*['"](POST|PUT|PATCH|DELETE)['"]/g)].map(m => m[1])).toEqual(['POST']);
    expect(/procedure-assignments|rule-overrides|\/overrides\//.test(dry.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, ''))).toBe(false);
    expect(write.test("fetch(u, { method: 'DELETE' })")).toBe(true);
    const api = fs.readFileSync(path.join(root, 'services/overridesApi.ts'), 'utf8');
    expect([...api.matchAll(/`\$\{[^`]*\}\/(procedure-assignments|rule-overrides\/exclude|overrides\/|removal-preview)/g)].length).toBeGreaterThan(0);
    // No Dataverse write from the page.
    expect(/\.(createRecord|updateRecord|deleteRecord)\(/.test(api)).toBe(false);
  });
});

describe('‹ › through the list as it is filtered, and the form follows the row', () => {
  const pinOf = (n: string, v = V.rate1) => override({ rowId: `row-${n}`, mode: 'Pinned', chainRecord: MATTER, pinnedVersion: v });
  const list: ProcedureItem[] = [
    procedure({ id: 'pa', name: 'Alpha', appliesTo: 'Firm', firm: FENWICK, pinned: true, overrides: [pinOf('a')] }),
    procedure({ id: 'pb', name: 'Bravo' }),
    procedure({ id: 'pc', name: 'Charlie', appliesTo: 'Firm', firm: FENWICK, pinned: true, overrides: [pinOf('c')] }),
    procedure({ id: 'pd', name: 'Delta' }),
  ];
  const view: RecordProceduresView = { ...matterProcedures, gaps: [], procedures: list };

  it('walks the rows in the order shown; the ends are disabled', async () => {
    renderHarness({ procedures: view });
    const d = await openProcedure('pa', 'Alpha');
    expect(within(d).getByText('1 of 4')).toBeInTheDocument();
    expect(button(d, 'Previous record')).toBeDisabled();
    fireEvent.click(button(d, 'Next record'));
    // The dialog stays (the shell starts over in place); its title is the next row's.
    await waitFor(() => expect(titleOf(d)).toBe('Bravo'));
    expect(screen.getByText('2 of 4')).toBeInTheDocument();
    fireEvent.click(button(d, 'Previous record'));
    await waitFor(() => expect(titleOf(d)).toBe('Alpha'));
    expect(d).toBeInTheDocument();
  });

  it('only walks the rows the count card leaves; the form shows the NEW row\'s state, not the last row\'s', async () => {
    renderHarness({ procedures: view });
    await ready();
    fireEvent.click(screen.getByRole('button', { name: 'Pinned, 2 Procedures' }));
    fireEvent.click(screen.getByTestId('procedure-row-pa'));
    const d = await screen.findByRole('dialog', { name: 'Alpha', hidden: true });
    expect(within(d).getByText('1 of 2')).toBeInTheDocument();
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'typed on Alpha');

    fireEvent.click(button(d, 'Next record'));
    await waitFor(() => expect(titleOf(d)).toBe('Charlie'));
    expect(screen.getByText('2 of 2')).toBeInTheDocument();
    expect(button(d, 'Next record')).toBeDisabled();
    // The shell started over on the new row: first step, and the form is Charlie's (pinned), not Alpha's typing.
    expect(within(d).getByTestId('procedure-step')).toBeInTheDocument();
    await toChoices(d);
    expect(radio(d, /^Pin a version/)).toBeChecked();
    expect(within(d).queryByDisplayValue('typed on Alpha')).not.toBeInTheDocument();
  });

  it('closes explicitly (Cancel) and keeps the lists as they were', async () => {
    const { calls } = renderHarness({ procedures: view });
    const d = await openProcedure('pa', 'Alpha');
    const reads = calls.length;
    fireEvent.click(button(d, 'Cancel'));
    await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());
    expect(calls.length).toBe(reads);
    expect(screen.getByTestId('procedure-row-pa')).toBeInTheDocument();
  });
});

describe('the Procedure step shows what the server says', () => {
  it('state, type, invoices, every source and the overrides on this record with who and why', async () => {
    renderHarness();
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    const step = within(d).getByTestId('procedure-step');
    expect(step).toHaveTextContent('Pinned');
    expect(step).toHaveTextContent('pinned to v1, v2 available');
    expect(step).toHaveTextContent('Fenwick & Calder LLP invoices only');
    expect(step).toHaveTextContent('Assigned to this record (wins)');
    expect(step).toHaveTextContent('Firm · Fenwick & Calder LLP');
    expect(step).toHaveTextContent('Pinned to v1 here · Dana Whitfield, 2 Sep 2026');
    expect(step).toHaveTextContent('"Grandfathered 2025 rates"');
  });

  it('a held Procedure shows its gap text', async () => {
    renderHarness();
    const d = await openProcedure('p-fee', 'Northwind fee schedule');
    expect(within(d).getByTestId('procedure-step')).toHaveTextContent('its last version has ended');
  });
});

describe('theme', () => {
  it('the wizard files carry no colour literal (control: the scan sees one)', () => {
    const colour = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/;
    const root = path.join(__dirname, '..');
    const forms = fs.readdirSync(path.join(root, 'components/wizards/ruleForms')).map(f => `components/wizards/ruleForms/${f}`);
    expect(forms.length).toBeGreaterThan(14);
    for (const f of ['components/wizards/steps.tsx', 'components/wizards/WizardFrame.tsx', 'components/wizards/ProcedureWizard.tsx', 'components/wizards/RuleWizard.tsx', 'components/rowOpen.ts', ...forms]) {
      expect([f, colour.test(fs.readFileSync(path.join(root, f), 'utf8'))]).toEqual([f, false]);
    }
    expect(colour.test('color: #fff;')).toBe(true);
  });
});

// Keep the shared fixtures referenced so a fixture rename fails here, not silently.
void TODAY;
void waProcedures;
