/**
 * Unit tests for useLazyResults (spaarkeai-word-add-in-r1 task 034, Path 2).
 *
 * Covers:
 * - `hasMore` is true only while unrevealed rows remain, and false once every item is shown.
 * - Sentinel-driven reveal via a mocked `IntersectionObserver` (jsdom provides none natively) — the
 *   sentinel `ref` is attached to a REAL rendered DOM node (not set imperatively from outside), which
 *   is what makes the hook's effect actually observe it, mirroring how `FindResultsList` uses it.
 * - A new `items` array resets the reveal window back to the first chunk.
 * - `revealNext` never exceeds `items.length` regardless of how many times it fires.
 * - The hook itself issues no network calls — it only slices an array already in memory.
 */

import React from 'react';
import { render, act } from '@testing-library/react';
import { useLazyResults, type UseLazyResultsResult } from '../useLazyResults';

// jsdom does not implement IntersectionObserver. This mock captures every constructed observer's
// callback so a test can manually simulate the sentinel intersecting the viewport, and records
// observe()/disconnect() calls so a test can assert the hook stops observing once `hasMore` is false.
let constructedCallbacks: IntersectionObserverCallback[] = [];
let observeCalls = 0;
let disconnectCalls = 0;

class IntersectionObserverMock {
  constructor(callback: IntersectionObserverCallback) {
    constructedCallbacks.push(callback);
  }
  observe(): void {
    observeCalls += 1;
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    disconnectCalls += 1;
  }
}

function fireIntersection(isIntersecting = true): void {
  const entry = { isIntersecting } as IntersectionObserverEntry;
  constructedCallbacks.forEach(cb => cb([entry], {} as IntersectionObserver));
}

beforeEach(() => {
  constructedCallbacks = [];
  observeCalls = 0;
  disconnectCalls = 0;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).IntersectionObserver = IntersectionObserverMock;
});

/** Mounts the hook inside a real component so `sentinelRef` attaches to an actual DOM node. */
function Harness({
  items,
  chunkSize,
  onResult,
}: {
  items: number[];
  chunkSize: number;
  onResult: (result: UseLazyResultsResult<number>) => void;
}): React.ReactElement {
  const result = useLazyResults(items, chunkSize);
  onResult(result);
  return <div ref={result.sentinelRef} data-testid="sentinel" />;
}

describe('useLazyResults', () => {
  it('reveals the first chunk immediately and hasMore is true while items remain unrevealed', () => {
    const items = Array.from({ length: 45 }, (_, i) => i);
    let latest: UseLazyResultsResult<number> | undefined;

    render(<Harness items={items} chunkSize={20} onResult={r => (latest = r)} />);

    expect(latest!.visibleItems).toEqual(items.slice(0, 20));
    expect(latest!.hasMore).toBe(true);
  });

  it('a set that fits in one chunk starts with hasMore false', () => {
    const items = Array.from({ length: 10 }, (_, i) => i);
    let latest: UseLazyResultsResult<number> | undefined;

    render(<Harness items={items} chunkSize={20} onResult={r => (latest = r)} />);

    expect(latest!.visibleItems).toEqual(items);
    expect(latest!.hasMore).toBe(false);
  });

  it('an empty array starts with hasMore false and no visible items', () => {
    let latest: UseLazyResultsResult<number> | undefined;

    render(<Harness items={[]} chunkSize={20} onResult={r => (latest = r)} />);

    expect(latest!.visibleItems).toEqual([]);
    expect(latest!.hasMore).toBe(false);
  });

  it('does not observe once hasMore is false (nothing left to reveal)', () => {
    const items = Array.from({ length: 5 }, (_, i) => i); // fits in one chunk of 20
    render(<Harness items={items} chunkSize={20} onResult={() => {}} />);

    expect(observeCalls).toBe(0);
  });

  it('sentinel-driven reveal: the IntersectionObserver firing reveals the next chunk', () => {
    const items = Array.from({ length: 45 }, (_, i) => i);
    let latest: UseLazyResultsResult<number> | undefined;

    render(<Harness items={items} chunkSize={20} onResult={r => (latest = r)} />);

    expect(latest!.visibleItems).toHaveLength(20);
    expect(observeCalls).toBeGreaterThan(0);

    act(() => {
      fireIntersection(true);
    });
    expect(latest!.visibleItems).toHaveLength(40);
    expect(latest!.hasMore).toBe(true);

    act(() => {
      fireIntersection(true);
    });
    // Clamped to items.length (45), not 60 — the hook never over-reveals.
    expect(latest!.visibleItems).toHaveLength(45);
    expect(latest!.hasMore).toBe(false);
  });

  it('hasMore becomes false only once every item has been revealed, and stops observing then', () => {
    const items = Array.from({ length: 25 }, (_, i) => i);
    let latest: UseLazyResultsResult<number> | undefined;

    render(<Harness items={items} chunkSize={20} onResult={r => (latest = r)} />);
    expect(latest!.hasMore).toBe(true); // 20 of 25 revealed

    act(() => {
      fireIntersection(true);
    });
    expect(latest!.visibleItems).toHaveLength(25);
    expect(latest!.hasMore).toBe(false);
    expect(disconnectCalls).toBeGreaterThan(0); // the "nothing left to reveal" effect cleanup fired
  });

  it('revealNext never grows visibleItems past items.length no matter how many times it fires', () => {
    const items = Array.from({ length: 22 }, (_, i) => i);
    let latest: UseLazyResultsResult<number> | undefined;

    render(<Harness items={items} chunkSize={20} onResult={r => (latest = r)} />);

    act(() => {
      latest!.revealNext();
      latest!.revealNext();
      latest!.revealNext();
    });

    expect(latest!.visibleItems).toHaveLength(22);
    expect(latest!.hasMore).toBe(false);
  });

  it('a NEW items array resets the reveal window back to the first chunk', () => {
    const itemsA = Array.from({ length: 45 }, (_, i) => i);
    let latest: UseLazyResultsResult<number> | undefined;

    const { rerender } = render(<Harness items={itemsA} chunkSize={20} onResult={r => (latest = r)} />);

    act(() => {
      latest!.revealNext();
    });
    expect(latest!.visibleItems).toHaveLength(40);

    const itemsB = Array.from({ length: 45 }, (_, i) => i + 1000); // a different document's results
    rerender(<Harness items={itemsB} chunkSize={20} onResult={r => (latest = r)} />);

    expect(latest!.visibleItems).toEqual(itemsB.slice(0, 20));
    expect(latest!.hasMore).toBe(true);
  });
});
