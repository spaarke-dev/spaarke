/**
 * relatedPrimary.test.tsx — owner UAT 2026-07-30 (item 8): single-primary "Related to".
 *
 * The "Related to" section models a SINGLE primary regarding record (associations[0], which the
 * BFF maps onto sprk_regardingrecord* at send time). Picking a record via "Link another record"
 * does NOT append silently — it opens a "set as primary regarding?" replace-confirm Dialog. On
 * confirm the pick is promoted to associations[0] (the GREEN primary chip); on cancel it is
 * discarded. This exercises the render/interaction wiring end-to-end (the reducer transition
 * itself is covered in EmailComposer.reducer.test.ts).
 */
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { ICommunicationAssociation } from '../../../services/communicationApi';
import type { IEmailComposerProps, IPickedRecord } from '../EmailComposer.types';

const noopFetch = jest.fn();

function renderComposer(overrides: Partial<IEmailComposerProps>) {
  const ref = React.createRef<import('../EmailComposer.types').IEmailComposerHandle>();
  const utils = renderWithProviders(
    <EmailComposer
      ref={ref}
      mode="compose"
      mount="page"
      authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      {...overrides}
    />
  );
  return { ref, ...utils };
}

describe('EmailComposer — single-primary "Related to" replace-confirm (item 8)', () => {
  const existingPrimary: ICommunicationAssociation = {
    entityType: 'sprk_matter',
    entityId: 'm-old',
    entityName: 'Old Matter',
  };
  const picked: IPickedRecord = { entityType: 'sprk_matter', id: 'm-new', name: 'New Matter' };

  it('picking a record opens the replace-confirm, and confirming promotes it to the GREEN primary', async () => {
    const onAddRelationship = jest.fn().mockResolvedValue(picked);
    const { ref, container } = renderComposer({ associations: [existingPrimary], onAddRelationship });

    // Existing primary is rendered as the green (data-primary) chip.
    expect(container.querySelector('[data-primary="true"]')?.textContent).toContain('Old Matter');

    // Pick another record via "Link another record".
    fireEvent.click(screen.getByRole('button', { name: /link another record/i }));

    // The replace-confirm Dialog opens and names the CURRENT primary it will replace.
    await screen.findByText('Set as primary regarding record');
    const replaceLine = screen.getByText(/Will replace the currently selected record/);
    expect(replaceLine.textContent).toContain('Old Matter');

    // Confirm → the pick becomes the primary (index 0); the old primary is dropped.
    fireEvent.click(screen.getByRole('button', { name: 'Set as primary' }));

    await waitFor(() => {
      const primary = container.querySelector('[data-primary="true"]');
      expect(primary?.textContent).toContain('New Matter');
    });
    // Single-primary model: the old primary was replaced, not kept.
    const associations = ref.current?.getState().associations ?? [];
    expect(associations).toHaveLength(1);
    expect(associations[0].entityId).toBe('m-new');
    expect(ref.current?.getState().isDirty).toBe(true);
  });

  it('cancelling the confirm discards the pick (nothing added)', async () => {
    const onAddRelationship = jest.fn().mockResolvedValue(picked);
    const { ref } = renderComposer({ associations: [existingPrimary], onAddRelationship });

    fireEvent.click(screen.getByRole('button', { name: /link another record/i }));
    await screen.findByText('Set as primary regarding record');
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByText('Set as primary regarding record')).not.toBeInTheDocument();
    });
    const associations = ref.current?.getState().associations ?? [];
    expect(associations).toHaveLength(1);
    expect(associations[0].entityId).toBe('m-old'); // unchanged
  });

  it('empty state: no primary yet → the confirm omits the "will replace" line', async () => {
    const onAddRelationship = jest.fn().mockResolvedValue(picked);
    renderComposer({ associations: [], onAddRelationship });

    // With no associations the affordance reads "Link a record".
    fireEvent.click(screen.getByRole('button', { name: /link a record/i }));
    await screen.findByText('Set as primary regarding record');
    // Nothing to replace → no replace line.
    expect(screen.queryByText(/Will replace the currently selected record/)).not.toBeInTheDocument();
  });
});
