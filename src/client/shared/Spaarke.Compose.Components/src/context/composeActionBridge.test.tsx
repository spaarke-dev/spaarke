/**
 * composeActionBridge.test.tsx — FR-13 (task 046) cross-pane dispatch bridge.
 *
 * Covers the AC1 hand-off contract at the bridge layer: the Assistant registers
 * its serial dispatcher; the workspace section forwards `enqueue`; a DIRECT call
 * (not a PaneEventBus event) reaches the registered dispatcher. Negative cases:
 * null outside a provider, reject-before-register, and clear-on-unregister.
 */

import * as React from 'react';
import { renderHook, act, waitFor } from '@testing-library/react';
import type { ComposeActionEnqueue } from '../widgets/ComposeAiToolbar';
import {
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  useRegisterComposeActionDispatcher,
} from './composeActionBridge';

const wrapper = ({ children }: { children?: React.ReactNode }): React.JSX.Element => (
  <ComposeActionBridgeProvider>{children}</ComposeActionBridgeProvider>
);

describe('composeActionBridge', () => {
  it('returns null when rendered outside a provider (standalone fallback)', () => {
    const { result } = renderHook(() => useComposeActionBridge());
    expect(result.current).toBeNull();
  });

  it('starts with no dispatcher; enqueue rejects until one registers', async () => {
    const { result } = renderHook(() => useComposeActionBridge(), { wrapper });
    expect(result.current).not.toBeNull();
    expect(result.current!.hasDispatcher).toBe(false);
    await expect(result.current!.enqueue({ id: 'x', bindingId: 'b' })).rejects.toThrow(
      /no host dispatcher/i
    );
  });

  it('registering a dispatcher flips hasDispatcher and delegates enqueue DIRECTLY', async () => {
    const dispatcher: ComposeActionEnqueue = jest
      .fn()
      .mockResolvedValue({ streamId: 's1', status: 'complete' });

    const { result } = renderHook(
      () => {
        const bridge = useComposeActionBridge();
        useRegisterComposeActionDispatcher(dispatcher);
        return bridge;
      },
      { wrapper }
    );

    await waitFor(() => expect(result.current!.hasDispatcher).toBe(true));

    const request = { id: 'compose-draft-alternative#1', bindingId: 'binding-1', args: { slots: { selectionText: 'x' } } };
    const res = await result.current!.enqueue(request);

    expect(dispatcher).toHaveBeenCalledTimes(1);
    expect(dispatcher).toHaveBeenCalledWith(request);
    expect(res).toEqual({ streamId: 's1', status: 'complete' });
  });

  it('the enqueue reference is STABLE across dispatcher re-registration (no toolbar churn)', async () => {
    let currentDispatcher: ComposeActionEnqueue = jest.fn().mockResolvedValue({ streamId: 'a', status: 'complete' });

    const { result, rerender } = renderHook(
      ({ d }: { d: ComposeActionEnqueue }) => {
        const bridge = useComposeActionBridge();
        useRegisterComposeActionDispatcher(d);
        return bridge;
      },
      { wrapper, initialProps: { d: currentDispatcher } }
    );

    await waitFor(() => expect(result.current!.hasDispatcher).toBe(true));
    const enqueueRef1 = result.current!.enqueue;

    // Swap the host dispatcher (e.g. after a session change re-binds it).
    const nextDispatcher: ComposeActionEnqueue = jest.fn().mockResolvedValue({ streamId: 'b', status: 'complete' });
    currentDispatcher = nextDispatcher;
    act(() => rerender({ d: nextDispatcher }));

    await waitFor(() => expect(result.current!.hasDispatcher).toBe(true));
    const enqueueRef2 = result.current!.enqueue;

    // Stable identity — ComposeWorkspace's useMemo-bound toolbar wiring does not churn.
    expect(enqueueRef2).toBe(enqueueRef1);

    // Latest dispatcher is used.
    await result.current!.enqueue({ id: 'y', bindingId: 'binding-2' });
    expect(nextDispatcher).toHaveBeenCalledTimes(1);
  });

  it('clears the dispatcher when cleared (mirrors registrant unmount cleanup)', async () => {
    const dispatcher: ComposeActionEnqueue = jest.fn().mockResolvedValue({ streamId: 's', status: 'complete' });
    const { result } = renderHook(() => useComposeActionBridge(), { wrapper });

    // Register (what useRegisterComposeActionDispatcher's effect does on mount).
    act(() => result.current!.setDispatcher(dispatcher));
    await waitFor(() => expect(result.current!.hasDispatcher).toBe(true));

    // Clear (what the effect cleanup does on unmount).
    act(() => result.current!.setDispatcher(null));
    await waitFor(() => expect(result.current!.hasDispatcher).toBe(false));
    await expect(result.current!.enqueue({ id: 'z', bindingId: 'b' })).rejects.toThrow(
      /no host dispatcher/i
    );
  });
});
