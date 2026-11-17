/**
 * 084b fix round 1: what the wizards do on screen (items 2, 3, 4, 5, 7 and 8 of the review). Each test fails on the code before its fix.
 */

import '@testing-library/jest-dom';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { ProcedureItem, RecordProceduresView, RuleItem } from '../services/recordProceduresApi';
import { MATTER, V, mark, matterProcedures, matterRules, override, procedure, rule } from './fixtures';
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
  refusal,
  removedBody,
  renderHarness,
  respond,
  toChoices,
  toConfirm,
  typeReason,
} from './wizardHarness';

const titleOf = (d: HTMLElement) => document.getElementById(d.getAttribute('aria-labelledby') ?? '')?.textContent ?? null;
const routes = (writeCalls: Array<{ route: string }>) => writeCalls.map(c => c.route).filter(r => r !== 'procedure');

async function pick(d: HTMLElement, optionName: string | RegExp) {
  fireEvent.click(await within(d).findByRole('combobox', { hidden: true }));
  fireEvent.click(await screen.findByRole('option', { name: optionName, hidden: true }));
}

describe('item 2: while a save runs, the header ‹ › and × do not move or close the wizard', () => {
  const pinOf = (n: string) => override({ rowId: `row-${n}`, mode: 'Pinned', chainRecord: MATTER, pinnedVersion: V.rate1 });
  const list: ProcedureItem[] = [
    procedure({ id: 'pa', name: 'Alpha', appliesTo: 'Firm', pinned: true, overrides: [pinOf('a')] }),
    procedure({ id: 'pb', name: 'Bravo' }),
  ];
  const view: RecordProceduresView = { ...matterProcedures, gaps: [], procedures: list };

  it('Save on Alpha, then › and ×: nothing moves, and the result lands under Alpha', async () => {
    let release!: (v: unknown) => void;
    const pending = new Promise(res => (release = res));
    const { writeCalls } = renderHarness({
      procedures: view,
      writes: respond({ preview: () => previewBody(), remove: () => removedBody(), assign: () => pending }),
    });
    const d = await openProcedure('pa', 'Alpha');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Not travelling');
    await toConfirm(d);
    fireEvent.click(button(d, 'Save override'));
    await waitFor(() => expect(routes(writeCalls)).toEqual(['preview', 'remove', 'assign']));

    // The save is running (the shell's own footer buttons are off).
    expect(button(d, 'Saving…')).toBeDisabled();
    fireEvent.click(button(d, 'Next record'));
    fireEvent.click(button(d, 'Close'));
    await act(async () => {});
    expect(titleOf(d)).toBe('Alpha');
    expect(screen.getByText('1 of 2')).toBeInTheDocument();
    expect(d).toBeInTheDocument();

    await act(async () => release(createdBody({ mode: 'Excluded' })));
    const body = await within(d).findByTestId('success-body');
    expect(body).toHaveTextContent('Alpha on matter');
    expect(titleOf(d)).toBe('Alpha');

    // Once the save is over, the explicit close works again.
    fireEvent.click(within(d).getByTestId('wizard-done'));
    await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());
  });

  it('after a refused save the header works again', async () => {
    const { writeCalls } = renderHarness({
      procedures: view,
      writes: respond({ assign: () => refusal(403, 'Forbidden', 'access.denied') }),
    });
    const d = await openProcedure('pb', 'Bravo');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'r');
    await toConfirm(d);
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByRole('alert', { hidden: true });
    expect(routes(writeCalls)).toEqual(['assign']);
    fireEvent.click(button(d, 'Previous record'));
    await waitFor(() => expect(titleOf(d)).toBe('Alpha'));
  });
});

describe('item 3: a rule changed on this record opens with nothing selected, and says what removing the change does', () => {
  const changed: RuleItem = rule({
    policyId: 'r-chg',
    code: 'HOURS',
    name: 'Excessive daily hours',
    recordRule: true,
    parameterSummary: '12 hours per timekeeper per day',
    marks: [mark('Changed', { distance: 0, chainRecord: MATTER, overrideRowId: 'chg-row', reason: 'Staffing', baseParameterSummary: '11 hours per timekeeper per day' })],
  });

  it('neither "Run the published rule" nor "Exclude here" is selected, Next is off, and the caption sets the changed values against the published ones', async () => {
    renderHarness({ rules: { ...matterRules, rules: [changed] } });
    const d = await openRule('r-chg', 'HOURS · Excessive daily hours');
    await toChoices(d);
    expect(radio(d, /^Run the published rule/)).not.toBeChecked();
    expect(radio(d, /^Exclude here/)).not.toBeChecked();
    expect(next(d)).toBeDisabled();
    const caption = within(d).getByTestId('choice-keep');
    expect(caption).toHaveTextContent('Removes this record\'s change');
    expect(caption).toHaveTextContent('11 hours per timekeeper per day');
    expect(caption).toHaveTextContent('12 hours per timekeeper per day');
    expect(caption).not.toHaveTextContent('As published');
  });
});

describe('item 4: a pin the route would refuse never reaches the removal', () => {
  const versions = procedureBody([
    { id: 'rate-v1', n: 1, status: 'Retired', from: '2025-01-01', to: '2026-01-01' },
    { id: 'rate-v2', n: 2, status: 'Active', from: '2026-01-01' },
  ]);

  it('the record\'s own pin is of a Retired version: editing its reason does not enable Next; choosing a pinnable version does', async () => {
    const { writeCalls } = renderHarness({ writes: respond({ procedure: () => versions }) });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    expect(radio(d, /^Pin a version/)).toBeChecked();
    await waitFor(() => expect(writeCalls.map(c => c.route)).toContain('procedure'));
    typeReason(d, 'A new reason');
    expect(await within(d).findByText(/can no longer be pinned/)).toBeInTheDocument();
    expect(next(d)).toBeDisabled();

    await pick(d, /^v2/);
    await waitFor(() => expect(next(d)).toBeEnabled());
  });

  it('the Reason fields stop at the server\'s 2000 characters', async () => {
    renderHarness();
    const proc = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(proc);
    chooseRadio(proc, /^Exclude here/);
    expect(within(proc).getByLabelText(/^Reason/)).toHaveAttribute('maxlength', '2000');
    fireEvent.click(button(proc, 'Cancel'));
    await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());

    const rule = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(rule);
    chooseRadio(rule, /^Exclude here/);
    expect(within(rule).getByLabelText(/^Reason/)).toHaveAttribute('maxlength', '2000');
  });
});

describe('item 5: after a partial save the retry sends only what is left', () => {
  const afterRemoval: RecordProceduresView = {
    ...matterProcedures,
    procedures: matterProcedures.procedures.map(p => (p.id === 'p-rate' ? { ...p, overrides: [], pinned: false, newerVersion: null, sources: p.sources.filter(s => s.kind !== 'ManualOwn') } : p)),
  };

  it('Pin -> Exclude: the removal succeeds, the create is refused; a second Save is the create alone (no second DELETE, no 404)', async () => {
    let removed = false;
    let assigns = 0;
    const { writeCalls, calls } = renderHarness({
      procedures: () => (removed ? afterRemoval : matterProcedures),
      writes: respond({
        preview: () => previewBody(),
        remove: () => {
          removed = true;
          return removedBody();
        },
        assign: () => (++assigns === 1 ? refusal(409, 'Conflict', 'assignment.concurrent_edit') : createdBody({ mode: 'Excluded' })),
      }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Rates no longer apply');
    await toConfirm(d);
    const reads = calls.length;

    fireEvent.click(button(d, 'Save override'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent("The record's earlier override was removed but the new one was not saved");
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    // The wizard now holds the re-read row, so Finish is on again and sends only the create.
    await waitFor(() => expect(button(d, 'Save override')).toBeEnabled());
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'assign', 'assign']);
  });

  it('when the re-read no longer has the row, Finish stays off (a retry would delete a row that is gone)', async () => {
    let removed = false;
    const gone: RecordProceduresView = { ...matterProcedures, procedures: matterProcedures.procedures.filter(p => p.id !== 'p-rate') };
    const { writeCalls, calls } = renderHarness({
      procedures: () => (removed ? gone : matterProcedures),
      writes: respond({
        preview: () => previewBody(),
        remove: () => {
          removed = true;
          return removedBody();
        },
        assign: () => refusal(409, 'Conflict', 'assignment.procedure_retired'),
      }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'r');
    await toConfirm(d);
    const reads = calls.length;
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByRole('alert', { hidden: true });
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    await act(async () => {});
    expect(button(d, 'Save override')).toBeDisabled();
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'assign']);
  });
});

describe('item 7: removing this record\'s exclusion does not promise the rule is in force', () => {
  it('the choice and the success screen say the rule runs again unless a parent excludes it, and that the lists show the result', async () => {
    renderHarness({ writes: respond({ preview: () => previewBody({ kind: 'RuleExcluded' }), remove: () => removedBody({ kind: 'RuleExcluded' }) }) });
    const d = await openRule('r-vague', 'VAGUE · Vague description');
    await toChoices(d);
    chooseRadio(d, /^Run the published rule/);
    expect(within(d).getByTestId('choice-keep')).toHaveTextContent('unless a parent excludes it. The lists show the result.');
    await toConfirm(d);
    fireEvent.click(button(d, 'Remove override'));
    const body = await within(d).findByTestId('success-body');
    expect(body).toHaveTextContent('Runs again unless a parent excludes it');
    expect(body).toHaveTextContent('The lists show the result.');
    expect(body).not.toHaveTextContent('In force');
  });
});

describe('item 8: a duplicate exclusion says another firm may hold it', () => {
  it('409 assignment.duplicate on Exclude adds the hint to the refusal; other refusals do not', async () => {
    let n = 0;
    renderHarness({
      writes: respond({ exclude: () => (++n === 1 ? refusal(409, 'Conflict', 'assignment.duplicate') : refusal(403, 'Forbidden', 'access.denied')) }),
    });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'A reason');
    await toConfirm(d);
    fireEvent.click(button(d, 'Exclude rule'));
    expect(await within(d).findByRole('alert', { hidden: true })).toHaveTextContent('An exclusion for another firm may exist on this record; switch the firm to see it.');

    fireEvent.click(button(d, 'Exclude rule'));
    await waitFor(() => expect(within(d).getByRole('alert', { hidden: true })).toHaveTextContent('Forbidden'));
    expect(within(d).getByRole('alert', { hidden: true })).not.toHaveTextContent('another firm');
  });
});
