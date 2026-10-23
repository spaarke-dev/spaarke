/**
 * DecisionWizard side-pane lookups and the subject link (task 158; D-182, UAT round 1).
 *  - Every Person and Lookup field opens the host's side-pane lookup (`openLookup`) when the host provides one: single select,
 *    scoped by what the host builds from the request (entity, parameter code, core record), the pick fills the field exactly as
 *    the inline pick does (same value sent to the commit route). Cancelling changes nothing.
 *  - Without `openLookup` the inline `LookupField` stays (must-not-fire).
 *  - D-18: a required Lookup with exactly one candidate is still pre-selected and the pane is not opened.
 *  - *What was found* shows the subject's name as a link that calls `openRecord(entity, id)`; plain text without `openRecord`
 *    or without an id.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { DecisionOpenLookup, DecisionOpenRecord, DecisionReview } from '../decisionWizardTypes';
import { DecisionWizard } from '../DecisionWizard';
import {
  BUDGET_A,
  BUDGET_B,
  CONTACT,
  PATH_B_ID,
  doItem,
  DO_ID,
  doPlan,
  matterCore,
  pathBItem,
  pathBPlan,
} from '../__fixtures__/decisionWizardFixtures';
import { makeLoader, makeLookup, makeService, renderWizard } from './decisionTestKit';
import { createXrmNavigationService } from '../../../utils/adapters/xrmNavigationServiceAdapter';

const enter = (element: HTMLElement, value: string) => fireEvent.change(element, { target: { value } });
const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const click = (element: Element) => fireEvent.click(element);

const plans = { [PATH_B_ID]: pathBPlan, [DO_ID]: doPlan };

function setup(items = [pathBItem], overrides: Partial<React.ComponentProps<typeof DecisionWizard>> = {}) {
  const service = makeService({ plans });
  const view = renderWizard({
    service,
    loadItem: makeLoader(items),
    itemIds: items.map(i => i.row.signalId),
    startItemId: items[0].row.signalId,
    ...overrides,
  });
  return { service, ...view };
}

const loaded = () => screen.findByTestId('step-found');
const next = () => click(button('Next'));

/** Found -> Send budget inquiry (skipped) -> Revise budget. */
async function toReviseBudget() {
  await loaded();
  next();
  await screen.findByTestId('step-action-send-budget-inquiry');
  click(button('Skip'));
  await screen.findByTestId('step-action-revise-budget');
  enter(screen.getByRole('textbox', { name: /^New budget/ }), '250000');
  enter(screen.getByRole('textbox', { name: /^Reason for revision/ }), 'Appeal added');
}

/** From the current step to Confirm: Next when it is enabled (the step is taken), else Skip. */
async function toConfirm() {
  for (let i = 0; i < 8 && !screen.queryByTestId('step-confirm'); i++) {
    const nextButton = screen.getByRole('button', { name: /^next$/i }) as HTMLButtonElement;
    if (!nextButton.disabled) next();
    else click(button('Skip'));
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  return screen.findByTestId('step-confirm');
}

const pick = (item: { id: string; name: string } | null) =>
  jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async () => item);

describe('side-pane lookup (D-182)', () => {
  it('a budget Lookup opens the pane once with its entity and the item core; the pick becomes the value', async () => {
    const openLookup = pick(BUDGET_B);
    const lookup = makeLookup([BUDGET_A, BUDGET_B]);
    setup([pathBItem], { openLookup, lookup });
    await toReviseBudget();

    const field = screen.getByRole('textbox', { name: /^Budget to revise/ });
    expect(button('Next')).toBeDisabled();
    click(field);

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    expect(openLookup).toHaveBeenCalledWith({
      kind: 'Lookup',
      entity: 'sprk_budget',
      parameterCode: 'budget',
      core: { recordType: matterCore.recordType, recordId: matterCore.recordId },
    });
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_B.name));
    expect(button('Next')).toBeEnabled();
    // The inline type-ahead is not used to pick.
    expect(screen.queryByPlaceholderText(/search budget to revise/i)).toBeNull();
  });

  it('a Person field opens the pane for contact, and both picks reach the commit route as ids', async () => {
    const openLookup = jest.fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>(async req =>
      req.kind === 'Person' ? CONTACT : BUDGET_B
    );
    const { service } = setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(button('Next')).toBeEnabled());
    next();

    await screen.findByTestId('step-next');
    click(screen.getByRole('checkbox', { name: /add to do/i }));
    next();
    await screen.findByTestId('step-next-add-todo');
    enter(screen.getByRole('textbox', { name: /^To Do/ }), 'Chase the budget reply');
    click(screen.getByRole('textbox', { name: /^Assigned to/ }));
    await waitFor(() =>
      expect(openLookup).toHaveBeenCalledWith({
        kind: 'Person',
        entity: 'contact',
        parameterCode: 'assignee',
        core: { recordType: matterCore.recordType, recordId: matterCore.recordId },
      })
    );
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Assigned to/ })).toHaveValue(CONTACT.name));
    fireEvent.change(screen.getByLabelText(/^Due/), { target: { value: '2026-10-20' } });
    next();

    await screen.findByTestId('step-confirm');
    click(button('Record decision'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    const review = service.commit.mock.calls[0][1] as DecisionReview;
    expect(review.actions.find(a => a.code === 'revise-budget')?.values).toMatchObject({ budget: BUDGET_B.id });
    expect(review.nextSteps?.[0].values).toMatchObject({ assignee: CONTACT.id });
  }, 30000);

  it('cancelling the pane leaves the field unchanged and Next stays disabled for an empty required field', async () => {
    const openLookup = pick(null);
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(''));
    expect(button('Next')).toBeDisabled();
  });

  it('cancelling does not clear a value that was already picked', async () => {
    const openLookup = jest
      .fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>()
      .mockResolvedValueOnce(BUDGET_B)
      .mockResolvedValueOnce(null);
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_B.name));
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(2));
    expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_B.name);
    expect(button('Next')).toBeEnabled();
  });

  it('an optional Lookup (Outside firm) also opens the pane, scoped to its entity', async () => {
    const firm = { id: 'ffffffff-0000-0000-0000-000000000001', name: 'Acme LLP' };
    const openLookup = pick(firm);
    setup([pathBItem], { openLookup });
    await loaded();
    next();
    await screen.findByTestId('step-action-send-budget-inquiry');
    click(screen.getByRole('textbox', { name: /^Outside firm/ }));
    await waitFor(() =>
      expect(openLookup).toHaveBeenCalledWith(
        expect.objectContaining({ kind: 'Lookup', entity: 'sprk_organization', parameterCode: 'outsideFirm' })
      )
    );
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Outside firm/ })).toHaveValue(firm.name));
  });

  it('a Not filed item passes a null core', async () => {
    const openLookup = pick(null);
    const item = { ...pathBItem, core: null };
    setup([item], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(openLookup).toHaveBeenCalledWith(expect.objectContaining({ core: null })));
  });

  it('without openLookup the inline type-ahead renders and picks as before (must-not-fire)', async () => {
    const lookup = makeLookup([BUDGET_A, BUDGET_B]);
    setup([pathBItem], { lookup });
    await toReviseBudget();
    expect(screen.getByPlaceholderText(/search budget to revise/i)).toBeInTheDocument();
    expect(button('Next')).toBeDisabled();
  });

  it('D-18: a required Lookup with exactly one candidate is pre-selected and the pane is not opened', async () => {
    const openLookup = pick(BUDGET_B);
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A]) });
    await toReviseBudget();
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_A.name));
    expect(button('Next')).toBeEnabled();
    expect(openLookup).not.toHaveBeenCalled();
  });

  it('a second activation while the pane is open does not open another', async () => {
    let resolve: (v: { id: string; name: string } | null) => void = () => undefined;
    const openLookup = jest.fn(
      () =>
        new Promise<{ id: string; name: string } | null>(r => {
          resolve = r;
        })
    );
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    const field = screen.getByRole('textbox', { name: /^Budget to revise/ });
    click(field);
    click(field);
    expect(openLookup).toHaveBeenCalledTimes(1);
    resolve(BUDGET_A);
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_A.name));
  });

  it('a pane that fails is treated as a cancel: the field is unchanged and can be tried again', async () => {
    const openLookup = jest
      .fn<ReturnType<DecisionOpenLookup>, Parameters<DecisionOpenLookup>>()
      .mockRejectedValueOnce(new Error('pane failed'))
      .mockResolvedValueOnce(BUDGET_B);
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue('');
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_B.name));
  });

  it('F1: an optional Lookup picked through the pane can be cleared, and the commit carries no value for it', async () => {
    const firm = { id: 'ffffffff-0000-0000-0000-000000000001', name: 'Acme LLP' };
    const openLookup = pick(firm);
    const { service } = setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await loaded();
    next();
    await screen.findByTestId('step-action-send-budget-inquiry');
    // No value yet: nothing to clear.
    expect(screen.queryByRole('button', { name: 'Clear Outside firm' })).toBeNull();
    click(screen.getByRole('textbox', { name: /^Outside firm/ }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Outside firm/ })).toHaveValue(firm.name));

    click(screen.getByRole('button', { name: 'Clear Outside firm' }));
    expect(screen.getByRole('textbox', { name: /^Outside firm/ })).toHaveValue('');
    expect(screen.queryByRole('button', { name: 'Clear Outside firm' })).toBeNull();

    enter(screen.getByRole('textbox', { name: /^To\b/ }), 'budgets@acme.example');
    await toConfirm();
    click(button('Record decision'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    const review = service.commit.mock.calls[0][1] as DecisionReview;
    const inquiry = review.actions.find(a => a.code === 'send-budget-inquiry');
    expect(inquiry?.taken).toBe(true);
    expect(inquiry?.values).not.toHaveProperty('outsideFirm');
    expect(JSON.stringify(review)).not.toContain(firm.id);
  }, 30000);

  it('F1 (must-fire): clearing a required Lookup empties it and disables Next', async () => {
    const openLookup = pick(BUDGET_B);
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(button('Next')).toBeEnabled());

    click(screen.getByRole('button', { name: 'Clear Budget to revise' }));
    expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue('');
    expect(button('Next')).toBeDisabled();
  });

  it('K2 (must-fire): a table the host does not list keeps the inline field; the pane is not used for it', async () => {
    const openLookup = Object.assign(pick(BUDGET_B), { supportedEntities: ['contact'] as readonly string[] });
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    expect(screen.getByPlaceholderText(/search budget to revise/i)).toBeInTheDocument();
    expect(button('Next')).toBeDisabled();
    expect(openLookup).not.toHaveBeenCalled();
  });

  it('K2 (must-not-fire): a table the host lists uses the pane', async () => {
    const openLookup = Object.assign(pick(BUDGET_B), {
      supportedEntities: ['sprk_budget', 'contact'] as readonly string[],
    });
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await toReviseBudget();
    expect(screen.queryByPlaceholderText(/search budget to revise/i)).toBeNull();
    click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_B.name));
  });

  it('K2: the Outside firm (sprk_organization) stays inline when only budgets and contacts are listed', async () => {
    const openLookup = Object.assign(pick(null), {
      supportedEntities: ['sprk_budget', 'contact'] as readonly string[],
    });
    setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
    await loaded();
    next();
    await screen.findByTestId('step-action-send-budget-inquiry');
    expect(screen.getByPlaceholderText(/search outside firm/i)).toBeInTheDocument();
  });

  it('a braced, upper-case GUID from the platform pane is stored as the bare lower-case id, as the inline pick stores it', async () => {
    const lookupObjects = jest
      .fn()
      .mockResolvedValue([{ id: `{${BUDGET_B.id.toUpperCase()}}`, name: BUDGET_B.name, entityType: 'sprk_budget' }]);
    (window as { Xrm?: unknown }).Xrm = { WebApi: {}, Utility: { lookupObjects } };
    try {
      const nav = createXrmNavigationService();
      const openLookup: DecisionOpenLookup = async request => {
        const results = await nav.openLookup({
          entityType: request.entity,
          entityTypes: [request.entity],
          allowMultiSelect: false,
        });
        return results[0] ? { id: results[0].id, name: results[0].name } : null;
      };
      const { service } = setup([pathBItem], { openLookup, lookup: makeLookup([BUDGET_A, BUDGET_B]) });
      await toReviseBudget();
      click(screen.getByRole('textbox', { name: /^Budget to revise/ }));
      await waitFor(() => expect(screen.getByRole('textbox', { name: /^Budget to revise/ })).toHaveValue(BUDGET_B.name));
      expect(lookupObjects).toHaveBeenCalledTimes(1);
      await toConfirm();
      click(button('Record decision'));
      await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
      const review = service.commit.mock.calls[0][1] as DecisionReview;
      const budget = review.actions.find(a => a.code === 'revise-budget')?.values?.budget;
      expect(budget).toBe(BUDGET_B.id);
      expect(budget).toMatch(/^[0-9a-f-]{36}$/);
    } finally {
      delete (window as { Xrm?: unknown }).Xrm;
    }
  }, 30000);

  it('a Person field on a Do item opens the pane for contact (Reassign)', async () => {
    const openLookup = pick(CONTACT);
    setup([doItem], { openLookup });
    await loaded();
    next();
    await screen.findByTestId('step-action-mark-complete');
    click(button('Skip'));
    await screen.findByTestId('step-action-reschedule');
    click(button('Skip'));
    await screen.findByTestId('step-action-reassign');
    click(screen.getByRole('textbox', { name: /^Reassign to/ }));
    await waitFor(() =>
      expect(openLookup).toHaveBeenCalledWith(expect.objectContaining({ kind: 'Person', entity: 'contact' }))
    );
    await waitFor(() => expect(screen.getByRole('textbox', { name: /^Reassign to/ })).toHaveValue(CONTACT.name));
  });
});

describe('the subject link in What was found (D-182)', () => {
  it('shows the subject name as a link (a Fluent Link, which renders a button without an href) that calls openRecord with its entity and id', async () => {
    const openRecord = jest.fn<ReturnType<DecisionOpenRecord>, Parameters<DecisionOpenRecord>>(async () => undefined);
    setup([pathBItem], { openRecord });
    const found = await loaded();
    const link = within(found).getByRole('button', { name: 'Acme v. Beta' }) as HTMLElement;
    click(link);
    expect(openRecord).toHaveBeenCalledTimes(1);
    expect(openRecord).toHaveBeenCalledWith('sprk_matter', matterCore.recordId);
    // The wizard keeps its place: the same step is still showing.
    expect(screen.getByTestId('step-found')).toBeInTheDocument();
    expect(found).toHaveTextContent('Matter:');
  });

  it('is plain text without openRecord (must-not-fire)', async () => {
    setup([pathBItem]);
    const found = await loaded();
    expect(within(found).queryByRole('button', { name: 'Acme v. Beta' })).toBeNull();
    expect(found).toHaveTextContent('Matter: Acme v. Beta');
  });

  it('is plain text when the subject has no id', async () => {
    const openRecord = jest.fn(async () => undefined);
    const item = { ...pathBItem, core: { ...matterCore, recordId: '' } };
    setup([item], { openRecord });
    const found = await loaded();
    expect(within(found).queryByRole('button', { name: 'Acme v. Beta' })).toBeNull();
    expect(found).toHaveTextContent('Matter: Acme v. Beta');
  });

  it('a Not filed item shows no link', async () => {
    const openRecord = jest.fn(async () => undefined);
    setup([{ ...pathBItem, core: null }], { openRecord });
    const found = await loaded();
    expect(within(found).queryByRole('button', { name: 'Acme v. Beta' })).toBeNull();
    expect(found).toHaveTextContent('Not filed on a record');
  });

  it('a failing openRecord does not close or change the wizard', async () => {
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const openRecord = jest.fn(async () => {
      throw new Error('dialog failed');
    });
    setup([pathBItem], { openRecord });
    const found = await loaded();
    click(within(found).getByRole('button', { name: 'Acme v. Beta' }));
    await waitFor(() => expect(openRecord).toHaveBeenCalled());
    expect(screen.getByTestId('step-found')).toBeInTheDocument();
    jest.restoreAllMocks();
  });

  it('F2: a rejected openRecord is logged with console.warn, not swallowed', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const err = new Error('dialog failed');
      const openRecord = jest.fn(async () => {
        throw err;
      });
      setup([pathBItem], { openRecord });
      const found = await loaded();
      click(within(found).getByRole('button', { name: 'Acme v. Beta' }));
      await waitFor(() => expect(warn).toHaveBeenCalledWith('[DecisionWizard] openRecord failed', err));
    } finally {
      warn.mockRestore();
    }
  });

  it('F2: an openRecord that throws synchronously is logged too', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const err = new Error('sync failure');
      const openRecord = jest.fn(() => {
        throw err;
      });
      setup([pathBItem], { openRecord });
      const found = await loaded();
      click(within(found).getByRole('button', { name: 'Acme v. Beta' }));
      expect(warn).toHaveBeenCalledWith('[DecisionWizard] openRecord failed', err);
      expect(screen.getByTestId('step-found')).toBeInTheDocument();
    } finally {
      warn.mockRestore();
    }
  });
});
