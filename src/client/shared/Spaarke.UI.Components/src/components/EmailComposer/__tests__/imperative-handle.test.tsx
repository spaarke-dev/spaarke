/**
 * imperative-handle.test.tsx (task 023, W2)
 *
 * Behavior contract for the `IEmailComposerHandle` the engine exposes via
 * `forwardRef` — the surface every host (wizard / dialog / Code Page) drives:
 * `getState()`, `validate()`, `send()`, `saveDraft()`.
 *
 * Mocking is at the boundary only: `authenticatedFetch` (the network seam that
 * `sendCommunication()` calls) and the host-provided `onSaveDraftRequest`.
 * React internals are not mocked.
 */
import * as React from 'react';
import { act } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerHandle, IEmailComposerProps } from '../EmailComposer.types';

function okFetch(communicationId = 'comm-123') {
  return jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: async () => ({ communicationId }),
  } as unknown as Response);
}

function renderComposer(overrides: Partial<IEmailComposerProps> = {}) {
  const ref = React.createRef<IEmailComposerHandle>();
  const props: IEmailComposerProps = {
    mode: 'compose',
    mount: 'inline',
    authenticatedFetch: okFetch() as unknown as IEmailComposerProps['authenticatedFetch'],
    initialTo: ['recipient@example.com'],
    initialSubject: 'Subject',
    initialBody: 'Body',
    ...overrides,
  };
  renderWithProviders(<EmailComposer ref={ref} {...props} />);
  return { ref, props };
}

describe('IEmailComposerHandle.getState', () => {
  it('returns the current engine state snapshot', () => {
    const { ref } = renderComposer();
    const state = ref.current!.getState();
    expect(state.mode).toBe('compose');
    expect(state.to.map(r => r.email)).toEqual(['recipient@example.com']);
    expect(state.subject).toBe('Subject');
  });
});

describe('IEmailComposerHandle.validate', () => {
  it('returns ok for a complete compose', () => {
    const { ref } = renderComposer();
    let result!: ReturnType<IEmailComposerHandle['validate']>;
    act(() => {
      result = ref.current!.validate();
    });
    expect(result.ok).toBe(true);
    expect(result.errors).toEqual([]);
  });

  it('returns TO_REQUIRED for an empty recipient list (does not throw)', () => {
    const { ref } = renderComposer({ initialTo: [] });
    let result!: ReturnType<IEmailComposerHandle['validate']>;
    act(() => {
      result = ref.current!.validate();
    });
    expect(result.ok).toBe(false);
    expect(result.errors.map(e => e.code)).toContain('TO_REQUIRED');
  });
});

describe('IEmailComposerHandle.send', () => {
  it('sends a valid message through authenticatedFetch and fires onSent', async () => {
    const authenticatedFetch = okFetch('comm-999');
    const onSent = jest.fn();
    const { ref } = renderComposer({
      authenticatedFetch: authenticatedFetch as unknown as IEmailComposerProps['authenticatedFetch'],
      onSent,
    });

    let result!: { communicationId: string };
    await act(async () => {
      result = await ref.current!.send();
    });

    expect(result.communicationId).toBe('comm-999');
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(String(url)).toContain('/api/communications/send');
    expect(init.method).toBe('POST');
    expect(onSent).toHaveBeenCalledWith({ communicationId: 'comm-999' });
  });

  it('rejects without calling the network when validation fails', async () => {
    const authenticatedFetch = okFetch();
    const { ref } = renderComposer({
      initialTo: [],
      authenticatedFetch: authenticatedFetch as unknown as IEmailComposerProps['authenticatedFetch'],
    });

    await act(async () => {
      await expect(ref.current!.send()).rejects.toThrow(/validation failed/i);
    });
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });
});

describe('IEmailComposerHandle.saveDraft', () => {
  it('delegates to the host-provided onSaveDraftRequest and fires onSaveDraft', async () => {
    const onSaveDraftRequest = jest.fn().mockResolvedValue({ communicationId: 'draft-1' });
    const onSaveDraft = jest.fn();
    const { ref } = renderComposer({ onSaveDraftRequest, onSaveDraft });

    let result!: { communicationId: string };
    await act(async () => {
      result = await ref.current!.saveDraft();
    });

    expect(onSaveDraftRequest).toHaveBeenCalledTimes(1);
    expect(result.communicationId).toBe('draft-1');
    expect(onSaveDraft).toHaveBeenCalledWith({ communicationId: 'draft-1' });
  });

  it('throws when no onSaveDraftRequest handler is provided (no BFF draft endpoint yet)', async () => {
    const { ref } = renderComposer();
    await act(async () => {
      await expect(ref.current!.saveDraft()).rejects.toThrow(/no onSaveDraftRequest handler/i);
    });
  });
});
