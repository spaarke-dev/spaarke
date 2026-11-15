/**
 * The two optional evidence seams of What was found (spaarkeai-mcp-bff-api-r1 task 032 fix round), for a host that cannot open a record
 * in a modal or hold every evidence line (the MCP widget). Both are off by default: the Console shows neither.
 *
 *  - `evidenceLinks` + `openRecord`: an evidence line whose `ref` is `{table}:{guid}` gets "Open in Spaarke", which calls `openRecord(table, guid)`.
 *    A host that refuses to open says so (one polite status) until the next open succeeds.
 *    Never: a line the server withheld, a ref that names no record, a host with no `openRecord`, or the seam off.
 *  - `item.evidenceTruncation`: "Showing N of M evidence lines." (with "Open in Spaarke" when the host can), and an item whose lines
 *    could not be brought at all does not say "No evidence was recorded".
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { PATH_B_ID, pathBItem, pathBPlan } from '../__fixtures__/decisionWizardFixtures';
import type { DecisionEvidenceRef, DecisionWizardItem } from '../decisionWizardTypes';
import { makeLoader, makeService, renderWizard } from './decisionTestKit';

const MATTER = '22222222-2222-2222-2222-222222222222';
const COMM = 'aaaaaaaa-0000-4000-8000-000000000001';
const BUDGET = 'bbbbbbbb-0000-4000-8000-000000000002';

const line = (ref: string, text: string | null, extra: Partial<DecisionEvidenceRef> = {}): DecisionEvidenceRef => ({
  kind: 'x',
  ref,
  tier: 'Fact',
  text,
  ...extra,
});

const itemWith = (evidence: DecisionEvidenceRef[], extra: Partial<DecisionWizardItem> = {}): DecisionWizardItem => ({
  ...pathBItem,
  evidence,
  ...extra,
});

async function setup(item: DecisionWizardItem, props: Partial<Parameters<typeof renderWizard>[0]> = {}) {
  const view = renderWizard({
    service: makeService({ plans: { [PATH_B_ID]: pathBPlan } }),
    loadItem: makeLoader([item]),
    itemIds: [PATH_B_ID],
    startItemId: PATH_B_ID,
    ...props,
  });
  await screen.findByTestId('step-found');
  return view;
}

const OPEN_FAILED = 'Could not open the record in Spaarke.';
const links = () => screen.queryAllByRole('button', { name: /^open in spaarke/i });

describe('evidenceLinks', () => {
  const evidence = [
    line(`sprk_communication:${COMM}`, 'Invoice 4411 dated 3 Oct 2026'),
    line(`SPRK_Budget:${BUDGET.toUpperCase()}`, 'Spend is 112% of budget'),
  ];

  it('offers Open in Spaarke on each line that names a record, and opens that table and id', async () => {
    const openRecord = jest.fn(async () => undefined);
    await setup(itemWith(evidence), { evidenceLinks: true, openRecord });

    const found = links().filter(l => l.getAttribute('aria-label')?.startsWith('Open in Spaarke: '));
    expect(found).toHaveLength(2);
    expect(found[0]).toHaveAccessibleName('Open in Spaarke: Invoice 4411 dated 3 Oct 2026');
    fireEvent.click(found[0]);
    expect(openRecord).toHaveBeenLastCalledWith('sprk_communication', COMM);
    fireEvent.click(found[1]);
    // The table is lower-cased and the id bare lower-case: the host gets a clean logical name and guid.
    expect(openRecord).toHaveBeenLastCalledWith('sprk_budget', BUDGET);
    expect(screen.getByTestId('step-found')).toBeInTheDocument();
  });

  it('is off without the seam, without openRecord, and for a withheld line or a ref that names no record', async () => {
    const openRecord = jest.fn(async () => undefined);
    const mixed = itemWith([
      ...evidence,
      line(`sprk_matter:${MATTER}`, null, { withheld: true }),
      line('snapshot', 'A snapshot ref'),
      line('c-1', 'A short ref'),
      line(`sprk_x:not-a-guid`, 'A bad id'),
      line(`:${COMM}`, 'No table'),
      line(`sprk bad:${COMM}`, 'A bad table'),
    ]);

    const off = await setup(mixed, { openRecord }); // the Console: openRecord, no seam
    expect(links()).toHaveLength(0);
    off.unmount();

    const noOpen = await setup(mixed, { evidenceLinks: true }); // the seam without a way to open
    expect(links()).toHaveLength(0);
    noOpen.unmount();

    await setup(mixed, { evidenceLinks: true, openRecord });
    // Only the two real records: not the withheld one, not the five refs that name nothing.
    expect(links()).toHaveLength(2);
    expect(openRecord).not.toHaveBeenCalled();
  });

  it('a failing openRecord is logged, says so in one polite status, and the wizard keeps its place', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const err = new Error('host would not open it');
      const openRecord = jest.fn(async () => {
        throw err;
      });
      await setup(itemWith(evidence), { evidenceLinks: true, openRecord });
      expect(screen.queryByText(OPEN_FAILED)).toBeNull();
      fireEvent.click(links()[0]);
      await waitFor(() => expect(warn).toHaveBeenCalledWith('[DecisionWizard] openRecord failed', err));
      const status = await screen.findByText(OPEN_FAILED);
      expect(status).toHaveAttribute('role', 'status');
      fireEvent.click(links()[1]);
      await waitFor(() => expect(openRecord).toHaveBeenCalledTimes(2));
      expect(screen.getAllByText(OPEN_FAILED)).toHaveLength(1); // one status, however many opens failed
      expect(screen.getByTestId('step-found')).toBeInTheDocument();
    } finally {
      warn.mockRestore();
    }
  });

  it('a synchronous throw from openRecord shows the same status', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const openRecord = jest.fn(() => {
        throw new Error('no host');
      });
      await setup(itemWith(evidence), { evidenceLinks: true, openRecord });
      fireEvent.click(links()[0]);
      expect(await screen.findByText(OPEN_FAILED)).toBeInTheDocument();
    } finally {
      warn.mockRestore();
    }
  });

  it('the status is cleared by the next successful open', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      let fail = true;
      const openRecord = jest.fn(async () => {
        if (fail) throw new Error('host would not open it');
      });
      await setup(itemWith(evidence), { evidenceLinks: true, openRecord });
      fireEvent.click(links()[0]);
      expect(await screen.findByText(OPEN_FAILED)).toBeInTheDocument();
      fail = false;
      fireEvent.click(links()[0]);
      await waitFor(() => expect(screen.queryByText(OPEN_FAILED)).toBeNull());
    } finally {
      warn.mockRestore();
    }
  });

  it('a closed item shows its stored snapshot with the same links (the snapshot names records too)', async () => {
    const openRecord = jest.fn(async () => undefined);
    const closed = itemWith(evidence, {
      resolution: {
        resolutionType: 'Acted',
        factSnapshot: [line(`sprk_communication:${COMM}`, 'As it stood')],
      },
    });
    await setup(closed, { evidenceLinks: true, openRecord });
    expect(links()).toHaveLength(1);
    expect(links()[0]).toHaveAccessibleName('Open in Spaarke: As it stood');
  });
});

describe('evidenceTruncation', () => {
  const two = [line(`sprk_communication:${COMM}`, 'One'), line(`sprk_budget:${BUDGET}`, 'Two')];

  it('says "Showing N of M" when the host brought only the first lines, and links to the Console when it can', async () => {
    const openAll = jest.fn();
    await setup(itemWith(two, { evidenceTruncation: { total: 130, openAll } }));

    expect(screen.getByTestId('evidence-truncated')).toHaveTextContent('Showing 2 of 130 evidence lines.');
    fireEvent.click(screen.getByRole('button', { name: 'Open in Spaarke' }));
    expect(openAll).toHaveBeenCalledTimes(1);
  });

  it('has no link when the host offers none, and says so without a count when it has no total', async () => {
    await setup(itemWith(two, { evidenceTruncation: {} }));
    const note = screen.getByTestId('evidence-truncated');
    expect(note).toHaveTextContent('Showing the first 2 evidence lines; more are not shown.');
    expect(screen.queryByRole('button', { name: 'Open in Spaarke' })).toBeNull();
  });

  it('never says "No evidence was recorded" for an item whose lines could not be brought', async () => {
    await setup(itemWith([], { evidenceTruncation: { total: 0 } }));
    expect(screen.getByTestId('evidence-truncated')).toHaveTextContent(
      'The evidence for this item is too large to show here.'
    );
    expect(screen.queryByText('No evidence was recorded for this item.')).toBeNull();
  });

  it('says nothing without the seam (the Console), nothing for a closed item, and keeps "No evidence" for an item with none', async () => {
    const plain = await setup(itemWith(two));
    expect(screen.queryByTestId('evidence-truncated')).toBeNull();
    plain.unmount();

    const closed = await setup(
      itemWith(two, {
        evidenceTruncation: { total: 130 },
        resolution: { resolutionType: 'Acted', factSnapshot: [line(`sprk_budget:${BUDGET}`, 'As it stood')] },
      })
    );
    expect(screen.queryByTestId('evidence-truncated')).toBeNull();
    closed.unmount();

    await setup(itemWith([]));
    expect(screen.getByText('No evidence was recorded for this item.')).toBeInTheDocument();
    expect(screen.queryByTestId('evidence-truncated')).toBeNull();
  });
});
