/**
 * The optional lookup seams of the decision wizard (spaarkeai-mcp-bff-api-r1 task 032 fix round): a host's `lookup` may carry
 * `minChars` (the shortest text it searches) and `failureText` (what to say when a search fails). Both are carried through the wizard's
 * item-scoped wrapper to the inline field. Without them (the Console's search) nothing changes: any text is searched, and a failed
 * search reads "No results found" as it always did.
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { BUDGET_A, BUDGET_B, PATH_B_ID, pathBItem, pathBPlan } from '../__fixtures__/decisionWizardFixtures';
import type { DecisionLookupRequest, DecisionLookupSearch } from '../decisionWizardTypes';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });

/** To the Revise budget step's budget field, with the host's `lookup`. */
async function toBudgetField(lookup: DecisionLookupSearch) {
  renderWizard({
    service: makeService({ plans: { [PATH_B_ID]: pathBPlan } }),
    loadItem: makeLoader([pathBItem]),
    lookup,
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
  });
  await screen.findByTestId('step-found');
  fireEvent.click(button('Next'));
  fireEvent.change(screen.getByRole('textbox', { name: /^To\b/ }), { target: { value: 'a@b.example' } });
  fireEvent.click(button('Next'));
  await screen.findByTestId('step-action-revise-budget');
  return screen.getByPlaceholderText(/search budget to revise/i);
}

const type = (field: HTMLElement, text: string) => {
  fireEvent.focus(field);
  fireEvent.change(field, { target: { value: text } });
};

describe('lookup seams carried to the field', () => {
  it('minChars: one letter asks nothing and says how many are needed, not "No results found"', async () => {
    const asked: DecisionLookupRequest[] = [];
    const lookup: DecisionLookupSearch = Object.assign(
      async (req: DecisionLookupRequest) => {
        asked.push(req);
        return [BUDGET_A, BUDGET_B]; // two: a single candidate would be pre-selected and the field would be a chip
      },
      { minChars: 2 }
    );
    const field = await toBudgetField(lookup);
    asked.length = 0; // the empty browse on focus is not what is asserted

    type(field, 'f');

    expect(await screen.findByText('Type at least 2 letters')).toBeInTheDocument();
    expect(screen.queryByText('No results found')).toBeNull();
    await new Promise(resolve => setTimeout(resolve, 450));
    expect(asked.filter(r => r.query !== '')).toHaveLength(0);
  });

  it('failureText: a rejected search says so instead of "No results found"', async () => {
    const quiet = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const lookup: DecisionLookupSearch = Object.assign(
      async (req: DecisionLookupRequest) => {
        if (req.query === '') return [];
        throw new Error('429');
      },
      { failureText: 'Search could not be completed. Try again.' }
    );
    const field = await toBudgetField(lookup);

    type(field, 'fy26');

    expect(
      await screen.findByText('Search could not be completed. Try again.', undefined, { timeout: 3000 })
    ).toBeInTheDocument();
    expect(screen.queryByText('No results found')).toBeNull();
    quiet.mockRestore();
  });

  it('without the seams (the Console) a failed search still reads "No results found", and one letter is searched', async () => {
    const quiet = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const asked: string[] = [];
    const lookup: DecisionLookupSearch = async req => {
      asked.push(req.query);
      if (req.query === '') return [];
      throw new Error('429');
    };
    const field = await toBudgetField(lookup);

    type(field, 'f');

    expect(await screen.findByText('No results found', undefined, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.queryByText(/Type at least/)).toBeNull();
    await waitFor(() => expect(asked).toContain('f'));
    quiet.mockRestore();
  });
});
