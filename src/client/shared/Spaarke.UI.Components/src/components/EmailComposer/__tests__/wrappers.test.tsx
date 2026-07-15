/**
 * wrappers.test.tsx (task 023, W2)
 *
 * The three semantic wrappers (task 021) are "thin by contract" (ADR-045): they
 * add no business logic, only lock the `mount` shape and map host callbacks.
 * These tests assert exactly that contract:
 *   - SendEmailStep  → mount='inline', forwards the composer handle, default mode 'compose'
 *   - SendEmailDialog → mount='dialog', open-gated Dialog chrome, Cancel → onClose
 *   - SendEmailPage  → mount='page', requires mode, Cancel → onCancel(onClose)
 */
import * as React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SendEmailStep } from '../wrappers/SendEmailStep';
import { SendEmailDialog } from '../wrappers/SendEmailDialog';
import { SendEmailPage } from '../wrappers/SendEmailPage';
import type { IEmailComposerHandle } from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

const authenticatedFetch = jest.fn() as unknown as AuthenticatedFetchFn;
const BFF = 'https://bff.example.com';

const COMPOSER_ACTIONS = { name: 'Composer actions' } as const;

describe('SendEmailStep', () => {
  it('locks mount to inline and forwards the composer handle', () => {
    const ref = React.createRef<IEmailComposerHandle>();
    renderWithProviders(<SendEmailStep ref={ref} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);

    expect(ref.current).not.toBeNull();
    expect(typeof ref.current!.send).toBe('function');
    const state = ref.current!.getState();
    expect(state.mount).toBe('inline');
    // Defaults mode to 'compose' when not supplied.
    expect(state.mode).toBe('compose');
  });

  it('renders no internal action bar (inline — the wizard frame owns navigation)', () => {
    renderWithProviders(<SendEmailStep authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);
    expect(screen.queryByRole('region', COMPOSER_ACTIONS)).toBeNull();
  });

  it('passes initial* props through to the engine', () => {
    const ref = React.createRef<IEmailComposerHandle>();
    renderWithProviders(
      <SendEmailStep
        ref={ref}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={BFF}
        initialTo={['a@example.com']}
        initialSubject="Hi"
      />
    );
    const state = ref.current!.getState();
    expect(state.to.map(r => r.email)).toEqual(['a@example.com']);
    expect(state.subject).toBe('Hi');
  });
});

describe('SendEmailDialog', () => {
  it('renders the composer inside Dialog chrome (with action bar) when open', () => {
    renderWithProviders(
      <SendEmailDialog open onClose={jest.fn()} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    // dialog mount renders the engine's own action bar.
    expect(screen.getByRole('region', COMPOSER_ACTIONS)).toBeInTheDocument();
  });

  it('does not render composer content when closed', () => {
    renderWithProviders(
      <SendEmailDialog open={false} onClose={jest.fn()} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    expect(screen.queryByRole('region', COMPOSER_ACTIONS)).toBeNull();
  });

  it('maps the composer Cancel action to onClose', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <SendEmailDialog open onClose={onClose} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe('SendEmailPage', () => {
  it('locks mount to page and renders the required mode', () => {
    renderWithProviders(<SendEmailPage mode="compose" authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);
    // page mount renders the engine action bar + the compose header.
    expect(screen.getByRole('region', COMPOSER_ACTIONS)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'New Email' })).toBeInTheDocument();
  });

  it('maps onClose onto the engine Cancel action', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <SendEmailPage mode="compose" onClose={onClose} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
