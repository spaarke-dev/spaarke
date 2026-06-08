/**
 * FeedTodoSyncContext.test.tsx — Unit tests for the R3 cross-block notification bus.
 *
 * Scope (R3 FR-14 acceptance criteria):
 *   1. Payload type uses `todoId`; no `eventId` in the new path.
 *   2. Cross-block update propagates within one render cycle.
 *   3. No stale state regression across blocks.
 *
 * Runtime status (2026-06-08):
 *   LegalWorkspace does not yet have a test runner configured. These tests
 *   are authored against the standard @testing-library/react + Vitest API so
 *   they can be picked up when the package adds a runner. Until then the
 *   file documents the contract and serves as a vetted reference for the
 *   shape consumers must respect.
 *
 * To execute today, copy the FeedTodoSyncProvider + useFeedTodoSync exports
 * into the @spaarke/ui-components workspace (which has Jest) — the bus is
 * stateless and has no LegalWorkspace-specific dependencies.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

import * as React from 'react';
import { renderHook, act } from '@testing-library/react';
import {
  FeedTodoSyncProvider,
  type FeedTodoSyncListener,
  type IFeedTodoSyncContextValue,
} from '../FeedTodoSyncContext';
import { useFeedTodoSync } from '../../hooks/useFeedTodoSync';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function wrapper({ children }: { children: React.ReactNode }) {
  return <FeedTodoSyncProvider>{children}</FeedTodoSyncProvider>;
}

// ---------------------------------------------------------------------------
// Contract: payload shape
// ---------------------------------------------------------------------------

describe('FeedTodoSyncContext — payload contract (FR-14)', () => {
  it('exposes notifyTodoChange(todoId, isActive) and subscribe(listener)', () => {
    const { result } = renderHook(() => useFeedTodoSync(), { wrapper });
    const ctx: IFeedTodoSyncContextValue = result.current;

    expect(typeof ctx.notifyTodoChange).toBe('function');
    expect(typeof ctx.subscribe).toBe('function');
    // Negative: legacy event-id surface must not exist (OS-1).
    expect((ctx as any).isFlagged).toBeUndefined();
    expect((ctx as any).toggleFlag).toBeUndefined();
    expect((ctx as any).initFlags).toBeUndefined();
    expect((ctx as any).getFlaggedCount).toBeUndefined();
    expect((ctx as any).isPending).toBeUndefined();
    expect((ctx as any).getError).toBeUndefined();
    expect((ctx as any)._flagsSnapshot).toBeUndefined();
  });

  it('forwards (todoId, isActive=true) to subscribers', () => {
    const { result } = renderHook(() => useFeedTodoSync(), { wrapper });
    const received: Array<[string, boolean]> = [];
    const listener: FeedTodoSyncListener = (todoId, isActive) => {
      received.push([todoId, isActive]);
    };

    act(() => {
      result.current.subscribe(listener);
    });
    act(() => {
      result.current.notifyTodoChange('todo-1', true);
    });

    expect(received).toEqual([['todo-1', true]]);
  });

  it('forwards (todoId, isActive=false) — dismissed / completed / deleted', () => {
    const { result } = renderHook(() => useFeedTodoSync(), { wrapper });
    const received: Array<[string, boolean]> = [];
    act(() => {
      result.current.subscribe((id, isActive) => received.push([id, isActive]));
    });

    act(() => {
      result.current.notifyTodoChange('todo-2', false);
    });

    expect(received).toEqual([['todo-2', false]]);
  });
});

// ---------------------------------------------------------------------------
// Contract: fan-out + propagation (FR-14 — one-render-cycle)
// ---------------------------------------------------------------------------

describe('FeedTodoSyncContext — propagation (FR-14)', () => {
  it('invokes every subscriber synchronously on notify (one render cycle)', () => {
    const { result } = renderHook(() => useFeedTodoSync(), { wrapper });
    const a: string[] = [];
    const b: string[] = [];
    const c: string[] = [];

    act(() => {
      result.current.subscribe((id) => a.push(id));
      result.current.subscribe((id) => b.push(id));
      result.current.subscribe((id) => c.push(id));
    });

    act(() => {
      result.current.notifyTodoChange('todo-x', true);
    });

    expect(a).toEqual(['todo-x']);
    expect(b).toEqual(['todo-x']);
    expect(c).toEqual(['todo-x']);
  });

  it('unsubscribe removes the listener — no stale fan-out', () => {
    const { result } = renderHook(() => useFeedTodoSync(), { wrapper });
    const received: string[] = [];
    let unsubscribe: () => void = () => {};

    act(() => {
      unsubscribe = result.current.subscribe((id) => received.push(id));
    });
    act(() => unsubscribe());
    act(() => {
      result.current.notifyTodoChange('todo-y', true);
    });

    expect(received).toEqual([]);
  });

  it('continues fan-out when one subscriber throws', () => {
    const { result } = renderHook(() => useFeedTodoSync(), { wrapper });
    const survivor: string[] = [];
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    act(() => {
      result.current.subscribe(() => {
        throw new Error('subscriber explodes');
      });
      result.current.subscribe((id) => survivor.push(id));
    });

    act(() => {
      result.current.notifyTodoChange('todo-z', false);
    });

    expect(survivor).toEqual(['todo-z']);
    expect(errorSpy).toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});

// ---------------------------------------------------------------------------
// Contract: no-provider safety
// ---------------------------------------------------------------------------

describe('useFeedTodoSync — no-op fallback when outside provider', () => {
  it('returns a safe no-op shape when no provider is present', () => {
    const { result } = renderHook(() => useFeedTodoSync());
    const ctx = result.current;

    expect(typeof ctx.notifyTodoChange).toBe('function');
    expect(typeof ctx.subscribe).toBe('function');

    // Calling notify must not throw and must reach no subscribers.
    const received: string[] = [];
    const unsubscribe = ctx.subscribe((id) => received.push(id));
    ctx.notifyTodoChange('todo-noop', true);
    unsubscribe();

    expect(received).toEqual([]);
  });
});
