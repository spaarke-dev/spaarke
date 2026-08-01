/**
 * uatR2Chrome.test.tsx — owner UAT R2 items 8 + 11, with item 8 SUPERSEDED by
 * 2026-07-31 item 4 (standard modal chrome).
 *
 * Item 8 → item 4: the chromed header renders the STANDARD Spaarke modal window
 *   controls — maximize/restore AND a close 'X' — in the upper-right, via the shared
 *   `ModalWindowControls`. (R2 had briefly removed the X; the owner reversed that to
 *   standardize expand + X across all modals.) Close routes to the same onCancel the
 *   action-bar Cancel uses. The maximize button appears only when onToggleMaximize is
 *   wired; the close button only when onCancel (onClose) is wired.
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

describe('EmailComposer — standard modal window controls (item 4, supersedes R2 item 8)', () => {
  it('renders BOTH the maximize control and a close X in the header, and close calls onCancel', () => {
    const onCancel = jest.fn();
    renderComposer({ onToggleMaximize: jest.fn(), onCancel });
    expect(screen.getByRole('button', { name: /maximize dialog/i })).toBeInTheDocument();
    const closeBtn = screen.getByRole('button', { name: /^close$/i });
    expect(closeBtn).toBeInTheDocument();
    // The header close routes to the SAME onCancel the action-bar Cancel button uses.
    fireEvent.click(closeBtn);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('renders the close X (but no maximize) when only onCancel is wired', () => {
    renderComposer({ onCancel: jest.fn() });
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
    expect(screen.getByRole('button', { name: /^close$/i })).toBeInTheDocument();
    // onCancel still also reaches the action-bar Cancel button.
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
