/**
 * uatR2Chrome.test.tsx — owner UAT 2026-07-30 R2 items 8 + 11.
 *
 * Item 8: the chromed header no longer renders a close 'X' button; the maximize/restore control
 *   stays (the modal is closed via ComposerActionBar's Cancel/Close, wired to the same onCancel).
 * Item 11: in compose ("New") mode with ZERO associations the "Related to" section still renders
 *   the "Link another record" affordance (reads "Link a record" in the empty state) and invoking
 *   it calls the host's onAddRelationship.
 */
import * as React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerProps } from '../EmailComposer.types';

const noopFetch = jest.fn();

function renderComposer(overrides: Partial<IEmailComposerProps>) {
  return renderWithProviders(
    <EmailComposer
      mode="compose"
      mount="dialog"
      authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      {...overrides}
    />
  );
}

describe('EmailComposer — chromed header window controls (R2 item 8)', () => {
  it('renders the maximize control but NOT a close X in the header', () => {
    renderComposer({ onToggleMaximize: jest.fn(), onCancel: jest.fn() });
    expect(screen.getByRole('button', { name: /maximize dialog/i })).toBeInTheDocument();
    // The close 'X' was removed — the modal is closed via the action-bar Cancel button.
    expect(screen.queryByRole('button', { name: /close dialog/i })).toBeNull();
  });

  it('renders no header window-control cluster when only onCancel is wired (no maximize)', () => {
    renderComposer({ onCancel: jest.fn() });
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /close dialog/i })).toBeNull();
    // onCancel still reaches the action-bar Cancel button.
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });
});

describe('EmailComposer — "Related to" in compose/New mode with zero associations (R2 item 11)', () => {
  it('shows the "Link a record" affordance with no associations and invokes onAddRelationship', () => {
    const onAddRelationship = jest.fn().mockResolvedValue(null);
    renderComposer({ associations: [], onAddRelationship });

    const linkTile = screen.getByRole('button', { name: /link a record/i });
    expect(linkTile).toBeInTheDocument();

    fireEvent.click(linkTile);
    expect(onAddRelationship).toHaveBeenCalledTimes(1);
  });

  it('still renders the "Link a record" affordance even when showAssociations is false', () => {
    renderComposer({ associations: [], showAssociations: false, onAddRelationship: jest.fn() });
    expect(screen.getByRole('button', { name: /link a record/i })).toBeInTheDocument();
  });
});
