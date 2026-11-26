/**
 * The first wizard step leads with the finding (owner decision D-227): no "What was found" heading, the h2 IS the finding,
 * the meta line holds the subject (when it adds something), the timing and the core record. The stepper label stays.
 * Negative: the wizard's title (a `headline` consumer) is still the subject's name.
 */

import '@testing-library/jest-dom';
import { screen, within } from '@testing-library/react';
import { matterCore, pathBItem, pathBPlan, pathBRow } from '../__fixtures__/decisionWizardFixtures';
import type { DecisionWizardItem } from '../decisionWizardTypes';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

async function open(item: DecisionWizardItem) {
  const service = makeService({ plans: { [item.row.signalId]: pathBPlan } });
  renderWizard({
    service,
    loadItem: makeLoader([item]),
    itemIds: [item.row.signalId],
    startItemId: item.row.signalId,
  });
  return screen.findByTestId('step-found');
}

describe('FoundStep leads with the finding', () => {
  it('has no "What was found" heading inside the step; its h2 is the finding', async () => {
    const found = await open(pathBItem);
    expect(within(found).queryByText('What was found')).toBeNull();
    expect(within(found).queryByRole('heading', { name: /what was found/i })).toBeNull();
    const h2 = within(found).getByRole('heading', { level: 2 });
    expect(h2).toBe(within(found).getByTestId('found-finding'));
    expect(h2).toHaveTextContent('Fee or scope change');
  });

  it('keeps the stepper label "What was found"', async () => {
    await open(pathBItem);
    const labels = within(screen.getByRole('navigation', { name: 'Wizard steps' }))
      .getAllByRole('listitem')
      .map(li => (li.textContent ?? '').trim());
    expect(labels[0]).toBe('What was found');
  });

  it('puts the subject, then the timing, then the core record in the meta line, and not the rule short name', async () => {
    const found = await open(pathBItem);
    const meta = within(found).getByTestId('found-meta');
    expect(meta.textContent).toBe('Invoice 4411 from Acme LLP · 3d · Matter: Acme v. Beta');
    expect(meta.textContent).not.toContain('Fee or scope change');
  });

  it('leaves the subject out of the meta line when it is the core record name', async () => {
    const found = await open({ ...pathBItem, row: { ...pathBRow, subjectName: matterCore.name } });
    expect(within(found).getByTestId('found-meta').textContent).toBe('3d · Matter: Acme v. Beta');
    expect(within(found).getByTestId('found-finding')).toHaveTextContent('Fee or scope change');
  });

  it('leads with the title, then shows the subject, when the rule has no short name', async () => {
    const found = await open({ ...pathBItem, row: { ...pathBRow, ruleShortName: null } });
    expect(within(found).getByTestId('found-finding')).toHaveTextContent('Fee or scope change with no budget revision');
    expect(within(found).getByTestId('found-meta').textContent).toContain('Invoice 4411 from Acme LLP');
  });

  it('does not repeat the subject in the meta line when the finding fell back to it', async () => {
    const found = await open({ ...pathBItem, row: { ...pathBRow, ruleShortName: null, title: null } });
    expect(within(found).getByTestId('found-finding')).toHaveTextContent('Invoice 4411 from Acme LLP');
    expect(within(found).getByTestId('found-meta').textContent).toBe('3d · Matter: Acme v. Beta');
  });

  it('still says "Not filed on a record" for an item with no core', async () => {
    const found = await open({ ...pathBItem, core: null, otherOpenItems: [] });
    expect(within(found).getByTestId('found-meta').textContent).toBe(
      'Invoice 4411 from Acme LLP · 3d · Not filed on a record'
    );
  });

  it('negative: the wizard title is still the subject name, not the finding', async () => {
    await open(pathBItem);
    expect(await screen.findByRole('dialog', { name: /Invoice 4411 from Acme LLP/ })).toBeInTheDocument();
  });
});
