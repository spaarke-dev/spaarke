/**
 * composerSendPlacement.test.tsx — owner UAT 2026-08-03 item 1.
 *
 * The primary Send control lives in the compose header's "From:" row (Outlook-style),
 * NOT in the bottom "Composer actions" bar. The bottom bar keeps Cancel + Save Draft.
 */
import * as React from 'react';
import { within, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerProps } from '../EmailComposer.types';

const noopFetch = jest.fn();

function renderComposer(overrides: Partial<IEmailComposerProps> = {}) {
  return renderWithProviders(
    <EmailComposer
      mode="compose"
      mount="dialog"
      authenticatedFetch={noopFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      onCancel={jest.fn()}
      {...overrides}
    />
  );
}

describe('EmailComposer — Send button placement (item 1)', () => {
  it('renders Send inside the "From" group, not the bottom action bar', () => {
    renderComposer();

    const fromGroup = screen.getByRole('group', { name: 'From' });
    expect(within(fromGroup).getByRole('button', { name: /send/i })).toBeInTheDocument();

    // The bottom "Composer actions" bar has Cancel + Save Draft but NO Send.
    const actionBar = screen.getByRole('region', { name: 'Composer actions' });
    expect(within(actionBar).getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(within(actionBar).getByRole('button', { name: 'Save Draft' })).toBeInTheDocument();
    expect(within(actionBar).queryByRole('button', { name: /send/i })).toBeNull();
  });
});
