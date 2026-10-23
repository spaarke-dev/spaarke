/**
 * EmailComposer.composeOnly.test.tsx - spaarke-ontology-platform-r1 task 160 (D-179; owner 2026-10-23).
 *
 * `composeOnly` is the opt-in for a host that only COLLECTS an email and sends it itself later (the decision wizard's budget
 * inquiry: the commit route sends at Record decision). It hides the Send button, the From row (switcher and static text) and the
 * External / Internal Only tags, and the composer's own `send()` and `saveDraft()` refuse with no network call.
 *
 * Must-not-fire: a default mount and `SendEmailDialog` (which sets `hostOwnsChrome` and keeps Send) are unchanged.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import { SendEmailDialog } from '../wrappers/SendEmailDialog';
import type { IEmailComposerHandle, IEmailComposerProps } from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

function renderComposer(overrides: Partial<IEmailComposerProps> = {}, ref?: React.Ref<IEmailComposerHandle>) {
  const authenticatedFetch = jest.fn().mockResolvedValue({ ok: true, json: async () => ({ communicationId: 'c-1' }) });
  const utils = renderWithProviders(
    <EmailComposer
      ref={ref}
      mode="compose"
      mount="inline"
      authenticatedFetch={authenticatedFetch as unknown as IEmailComposerProps['authenticatedFetch']}
      initialTo={['counsel@firm.example']}
      initialSubject="Budget inquiry: Acme"
      initialBody="Please explain."
      initialBodyFormat="PlainText"
      {...overrides}
    />
  );
  return { ...utils, authenticatedFetch };
}

describe('EmailComposer composeOnly (task 160)', () => {
  it('hides Send, the From row and the External / Internal Only tags, and keeps the fields', () => {
    renderComposer({ composeOnly: true, hostOwnsChrome: true, sendMode: 'user', fromMailbox: 'me@spaarke.example' });

    expect(screen.queryByRole('button', { name: /send/i })).toBeNull();
    expect(screen.queryByRole('group', { name: 'From' })).toBeNull();
    expect(screen.queryByText('From:')).toBeNull();
    expect(screen.queryByText(/me@spaarke\.example/)).toBeNull();
    expect(screen.queryByRole('radiogroup', { name: 'Who can see this message' })).toBeNull();

    expect(screen.getByText('counsel@firm.example')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Subject' })).toHaveValue('Budget inquiry: Acme');
  });

  it('hides the From switcher too when the host does not fix the send mode', () => {
    renderComposer({ composeOnly: true });

    expect(screen.queryByRole('button', { name: 'From mailbox' })).toBeNull();
    expect(screen.queryByRole('button', { name: /send/i })).toBeNull();
  });

  it('send() refuses with no network call', async () => {
    const ref = React.createRef<IEmailComposerHandle>();
    const onSent = jest.fn();
    const onError = jest.fn();
    const { authenticatedFetch } = renderComposer({ composeOnly: true, onSent, onError }, ref);

    await expect(ref.current!.send()).rejects.toThrow(/compose-only/i);
    expect(authenticatedFetch).not.toHaveBeenCalled();
    expect(onSent).not.toHaveBeenCalled();
    expect(onError).not.toHaveBeenCalled();
  });

  it('saveDraft() refuses without calling the host draft handler', async () => {
    const ref = React.createRef<IEmailComposerHandle>();
    const onSaveDraftRequest = jest.fn().mockResolvedValue({ communicationId: 'd-1' });
    renderComposer({ composeOnly: true, onSaveDraftRequest }, ref);

    await expect(ref.current!.saveDraft()).rejects.toThrow(/compose-only/i);
    expect(onSaveDraftRequest).not.toHaveBeenCalled();
  });

  it('must-not-fire: a default mount keeps Send, the From row and the tags', () => {
    renderComposer({ mount: 'dialog', onCancel: jest.fn() });

    expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'From' })).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'Who can see this message' })).toBeInTheDocument();
  });

  it('must-not-fire: a default mount still sends through its own send()', async () => {
    const ref = React.createRef<IEmailComposerHandle>();
    const { authenticatedFetch } = renderComposer({ bffBaseUrl: 'https://bff.example.com' }, ref);

    await expect(ref.current!.send()).resolves.toEqual({ communicationId: 'c-1' });
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
  });

  it('must-not-fire: SendEmailDialog (hostOwnsChrome) keeps Send, the From row and the tags', () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <SendEmailDialog
          open
          onClose={jest.fn()}
          authenticatedFetch={jest.fn() as unknown as AuthenticatedFetchFn}
          initialTo={['alice@example.com']}
          initialBodyFormat="PlainText"
        />
      </FluentProvider>
    );

    expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'From' })).toBeInTheDocument();
    expect(screen.getByRole('radiogroup', { name: 'Who can see this message' })).toBeInTheDocument();
  });
});
