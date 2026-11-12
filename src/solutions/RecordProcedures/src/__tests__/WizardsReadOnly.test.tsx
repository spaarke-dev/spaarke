/**
 * Task 084g (EB-D-221): the wizards open read-only from the `permissions` the two list reads return. With a false flag every write choice is
 * disabled with the reason and there is no Confirm (no Impact, no Finish); with true flags the wizard is exactly 084b's, and a refusal at Finish
 * is still shown with its title (a flag is a hint, U-2); a body without `permissions` (an older BFF) is all false.
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { READ_ONLY_LINE } from '../components/wizards/steps';
import { wizardReadOnly } from '../services/model';
import { readPermissions, readProceduresView, readRulesView, type WritePermissions } from '../services/recordProceduresApi';
import { WRITE_ALL, matterProcedures, matterRules } from './fixtures';
import { button, chooseRadio, createdBody, next, openProcedure, openRule, refusal, renderHarness, respond, toChoices, toConfirm, typeReason } from './wizardHarness';

const NONE: WritePermissions = { assignOrExclude: false, remove: false, change: false };

/** The two list answers with these `permissions` (`undefined`: the field is absent, as from a BFF before 084g). One argument: both answers. */
function answers(procedures: unknown, ...rulesArg: unknown[]) {
  const rules = rulesArg.length > 0 ? rulesArg[0] : procedures;
  const shape = (view: object, permissions: unknown) => {
    const { permissions: _drop, ...rest } = view as { permissions?: unknown };
    return () => (permissions === undefined ? rest : { ...rest, permissions });
  };
  return { procedures: shape(matterProcedures, procedures), rules: shape(matterRules, rules) };
}

const stepLabels = (d: HTMLElement) =>
  within(d)
    .getAllByRole('listitem', { hidden: true })
    .map(li => li.textContent ?? '')
    .join(' | ');

/** The read-only wizard: the row, then its choices all disabled with the reason; no Impact, no Confirm, no Finish; Close closes; nothing written. */
async function expectReadOnly(d: HTMLElement, writeCalls: unknown[]) {
  expect(stepLabels(d)).toMatch(/On this record/);
  expect(stepLabels(d)).not.toMatch(/Impact|Confirm/);
  await toChoices(d);
  expect(within(d).getByTestId('read-only-reason')).toHaveTextContent(READ_ONLY_LINE);
  const radios = within(d).getAllByRole('radio', { hidden: true });
  expect(radios.length).toBeGreaterThan(0);
  for (const r of radios) expect(r).toBeDisabled();
  expect(within(d).queryByLabelText(/^Reason/)).not.toBeInTheDocument();
  expect(within(d).queryByRole('button', { name: /^(Next|Save override|Remove override|Exclude rule|Finish|Cancel)$/, hidden: true })).not.toBeInTheDocument();
  expect(within(d).queryByTestId('impact-step')).not.toBeInTheDocument();
  expect(within(d).queryByTestId('confirm-step')).not.toBeInTheDocument();

  // Back to the row, forward again, then Close.
  fireEvent.click(button(d, 'Back'));
  await within(d).findByTestId(/^(procedure|rule)-step$/);
  fireEvent.click(next(d));
  await within(d).findByTestId('mode-step');
  // The footer's Close (the header's × is also named Close).
  fireEvent.click(within(d).getAllByRole('button', { name: 'Close', hidden: true }).find(b => b.textContent === 'Close')!);
  await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());
  expect(writeCalls).toHaveLength(0);
}

describe('084g: a caller who may not write opens the wizards read-only', () => {
  it('the Procedure wizard: every choice disabled with "You can view this, but not change it.", no Impact, no Confirm, nothing written', async () => {
    const { writeCalls } = renderHarness(answers(NONE));
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await expectReadOnly(d, writeCalls);
  });

  it('the rule wizard: the same', async () => {
    const { writeCalls } = renderHarness(answers(NONE));
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await expectReadOnly(d, writeCalls);
  });

  // Each wizard can plan an assignment or exclusion AND a removal (a switch is both): either flag false is read-only. `change` is 084c's.
  const readOnlyWhen: Array<[string, unknown, unknown]> = [
    ['assignOrExclude false', { ...WRITE_ALL, assignOrExclude: false }, WRITE_ALL],
    ['remove false', { ...WRITE_ALL, remove: false }, WRITE_ALL],
    ['the rules answer false, the Procedures answer true', WRITE_ALL, NONE],
    ['the Procedures answer false, the rules answer true', NONE, WRITE_ALL],
    ['no permissions in either answer (an older BFF)', undefined, undefined],
    ['no permissions in one answer', WRITE_ALL, undefined],
    ['flags that are not the boolean true', { assignOrExclude: 'true', remove: 1, change: true }, { assignOrExclude: 'true', remove: 1, change: true }],
    ['permissions that are not an object', true, true],
  ];

  it.each(readOnlyWhen)('read-only (negative) when %s', async (_name, procedures, rules) => {
    const { writeCalls } = renderHarness(answers(procedures, rules));
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await expectReadOnly(d, writeCalls);
  });
});

describe('084g: a caller who may write gets 084b', () => {
  it.each([
    ['every flag true', WRITE_ALL],
    ['change false (task 084c opens nothing yet)', { ...WRITE_ALL, change: false }],
  ])('%s: choices enabled, no reason, the four steps, and an Exclude reaches Confirm and saves', async (_name, permissions) => {
    const { writeCalls } = renderHarness({
      ...answers(permissions),
      writes: respond({ assign: () => createdBody({ mode: 'Excluded' }) }),
    });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    expect(stepLabels(d)).toMatch(/Impact/);
    expect(stepLabels(d)).toMatch(/Confirm/);
    await toChoices(d);
    expect(within(d).queryByTestId('read-only-reason')).not.toBeInTheDocument();
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Not travelling');
    await toConfirm(d);
    fireEvent.click(button(d, 'Save override'));
    await waitFor(() => expect(writeCalls).toHaveLength(1));
  });

  it('U-2: a true flag is a hint; a 403 from the write route at Finish is shown with its title and the wizard stays open on Confirm', async () => {
    const { writeCalls } = renderHarness({ ...answers(WRITE_ALL), writes: respond({ exclude: () => refusal(403, 'Forbidden', 'access.denied') }) });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'A reason');
    await toConfirm(d);
    fireEvent.click(button(d, 'Exclude rule'));
    const alert = await within(d).findByRole('alert', { hidden: true });
    expect(alert).toHaveTextContent('Forbidden');
    expect(within(d).getByTestId('confirm-step')).toBeInTheDocument();
    expect(writeCalls).toHaveLength(1);
  });
});

describe('084g: reading permissions', () => {
  it('each flag is true only when the server said true; a missing or malformed object is all false', () => {
    expect(readPermissions(WRITE_ALL)).toEqual(WRITE_ALL);
    expect(readPermissions(undefined)).toEqual(NONE);
    expect(readPermissions(null)).toEqual(NONE);
    expect(readPermissions([true, true, true])).toEqual(NONE);
    expect(readPermissions({ assignOrExclude: 'true', remove: 1, change: true })).toEqual({ assignOrExclude: false, remove: false, change: true });
  });

  it('both views carry permissions, all false when the body has none', () => {
    const { permissions: _p, ...procedures } = matterProcedures;
    const { permissions: _r, ...rules } = matterRules;
    expect(readProceduresView(procedures)?.permissions).toEqual(NONE);
    expect(readRulesView(rules)?.permissions).toEqual(NONE);
    expect(readProceduresView({ ...procedures, permissions: WRITE_ALL })?.permissions).toEqual(WRITE_ALL);
    expect(readRulesView({ ...rules, permissions: WRITE_ALL })?.permissions).toEqual(WRITE_ALL);
  });

  it('wizardReadOnly: writable only when every answer gives assignOrExclude and remove; no answer is read-only', () => {
    expect(wizardReadOnly(WRITE_ALL, WRITE_ALL)).toBe(false);
    expect(wizardReadOnly({ ...WRITE_ALL, change: false })).toBe(false);
    expect(wizardReadOnly(WRITE_ALL, { ...WRITE_ALL, remove: false })).toBe(true);
    expect(wizardReadOnly({ ...WRITE_ALL, assignOrExclude: false })).toBe(true);
    expect(wizardReadOnly()).toBe(true);
  });
});
