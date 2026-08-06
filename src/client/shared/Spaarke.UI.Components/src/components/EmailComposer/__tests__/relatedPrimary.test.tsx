/**
 * relatedPrimary.test.tsx — compose "Related to" is an IN-MEMORY, MULTI-association list
 * (owner UAT 2026-07-31 — supersedes the R2 single-primary replace-confirm).
 *
 * No `sprk_communication` record exists yet in compose/reply/forward, so associations live in
 * reducer state and ride the send payload. "Link another record" APPENDS each pick (deduped by
 * entityType+entityId); the chip × REMOVES one. index 0 is the primary the BFF maps onto
 * sprk_regardingrecord* at send. There is NO replace-confirm dialog anymore.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { ICommunicationAssociation } from '../../../services/communicationApi';
import type { IEmailComposerProps, IPickedRecord, IEmailComposerHandle } from '../EmailComposer.types';

const noopFetch = jest.fn();

function renderComposer(overrides: Partial<IEmailComposerProps>) {
  const ref = React.createRef<IEmailComposerHandle>();
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

describe('EmailComposer — in-memory multi "Related to" (owner UAT 2026-07-31)', () => {
  const existing: ICommunicationAssociation = {
    entityType: 'sprk_matter',
    entityId: 'm-old',
    entityName: 'Old Matter',
  };
  const second: ICommunicationAssociation = { entityType: 'sprk_matter', entityId: 'm-2', entityName: 'Second Matter' };
  const picked: IPickedRecord = { entityType: 'sprk_matter', id: 'm-new', name: 'New Matter' };

  it('shows the "Link another record" tile even when an association already exists', () => {
    renderComposer({ associations: [existing], onAddRelationship: jest.fn().mockResolvedValue(null) });
    expect(screen.getByRole('button', { name: /link another record/i })).toBeInTheDocument();
  });

  it('picking a record APPENDS it to the in-memory list (both kept, no replace-confirm)', async () => {
    const onAddRelationship = jest.fn().mockResolvedValue(picked);
    const { ref } = renderComposer({ associations: [existing], onAddRelationship });

    fireEvent.click(screen.getByRole('button', { name: /link another record/i }));

    await waitFor(() => expect(ref.current?.getState().associations).toHaveLength(2));
    const ids = (ref.current?.getState().associations ?? []).map(a => a.entityId);
    expect(ids).toEqual(['m-old', 'm-new']); // appended; the existing one is NOT replaced
    expect(screen.queryByText(/Set as primary/i)).not.toBeInTheDocument();
    expect(ref.current?.getState().isDirty).toBe(true);
  });

  it('the chip × removes that association from the in-memory list', async () => {
    const { ref } = renderComposer({ associations: [existing, second], onAddRelationship: jest.fn() });
    expect(ref.current?.getState().associations).toHaveLength(2);

    fireEvent.click(screen.getAllByLabelText('Remove')[0]);

    await waitFor(() => expect(ref.current?.getState().associations).toHaveLength(1));
    expect(ref.current?.getState().associations[0].entityId).toBe('m-2');
  });

  it('empty state reads "Link a record" and appends the first pick', async () => {
    const onAddRelationship = jest.fn().mockResolvedValue(picked);
    const { ref } = renderComposer({ associations: [], onAddRelationship });

    fireEvent.click(screen.getByRole('button', { name: /link a record/i }));

    await waitFor(() => expect(ref.current?.getState().associations).toHaveLength(1));
    expect(ref.current?.getState().associations[0].entityId).toBe('m-new');
  });

  it('appending the SAME record twice dedups (no duplicate)', async () => {
    const onAddRelationship = jest.fn().mockResolvedValue(picked);
    const { ref } = renderComposer({ associations: [], onAddRelationship });

    fireEvent.click(screen.getByRole('button', { name: /link a record/i }));
    await waitFor(() => expect(ref.current?.getState().associations).toHaveLength(1));

    fireEvent.click(screen.getByRole('button', { name: /link another record/i }));
    await waitFor(() => expect(onAddRelationship).toHaveBeenCalledTimes(2));
    expect(ref.current?.getState().associations).toHaveLength(1); // deduped
  });
});
