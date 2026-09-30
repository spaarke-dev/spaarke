/**
 * Unit tests for useFindRecordMatches (spaarkeai-word-add-in-r1 task 077, gap (a) — FR-16's records half).
 *
 * Covers the records BRIDGE, which is what task 077 adds:
 * - The seed: taken from the document's own AI profile (keywords → TL;DR → summary), never invented, and
 *   capped to the route's 1000-character query limit.
 * - The request goes to `POST /api/ai/search/records` — the per-row-authorized route task 077's constraint
 *   requires. (The server-side negative case — a record the caller cannot read is not returned — lives in
 *   `tests/integration/contract/Api/Ai/RecordSearchRowAuthorizationContractTests.cs`, because only the
 *   server can prove a trim; this client renders whatever the route returns.)
 * - Paging (ADR-051): a SHORT but non-empty page is not the end, because the server's authorization trim
 *   shortens pages; the next request advances by the REQUESTED window; an empty page ends paging.
 */

import React from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import { apiClient } from '@shared/services';
import {
  deriveRecordSearchSeed,
  MAX_RECORD_OFFSET,
  RECORD_PAGE_SIZE,
  useFindRecordMatches,
  type RecordSearchSeed,
} from '../useFindRecordMatches';
import type { DocumentProfileOutcome } from '../useDocumentProfile';

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return { ...actual, apiClient: { get: jest.fn(), post: jest.fn() } };
});

const mockPost = apiClient.post as jest.Mock;

// jsdom has no IntersectionObserver. Capture each observer's callback so a test can simulate the records
// sentinel scrolling into view.
let observerCallbacks: IntersectionObserverCallback[] = [];
class IntersectionObserverMock {
  constructor(callback: IntersectionObserverCallback) {
    observerCallbacks.push(callback);
  }
  observe(): void {
    /* no-op */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    /* no-op */
  }
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).IntersectionObserver = IntersectionObserverMock;

function scrollSentinelIntoView(): void {
  // The most recent observer is the live one — the hook re-creates it after every page.
  const latest = observerCallbacks[observerCallbacks.length - 1];
  latest?.([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);
}

function completedProfile(fields: { keywords?: string; tldr?: string; summary?: string }): DocumentProfileOutcome {
  return {
    kind: 'completed',
    keywords: fields.keywords ?? '',
    tldr: fields.tldr ?? '',
    summary: fields.summary ?? '',
    documentType: 'Contract',
  };
}

function page(count: number, startAt = 0) {
  return {
    results: Array.from({ length: count }, (_, i) => ({
      recordId: `rec-${startAt + i}`,
      recordType: 'sprk_matter',
      recordName: `Matter ${startAt + i}`,
    })),
    metadata: { totalCount: count, searchTime: 1, hybridMode: 'rrf' },
  };
}

/** Renders the sentinel exactly as FindResultsList does, so the hook's observer has a node to watch. */
function Harness({ seed }: { seed: RecordSearchSeed | null }): React.ReactElement {
  const result = useFindRecordMatches(seed);
  return (
    <div>
      <span data-testid="status">{result.status}</span>
      <span data-testid="count">{result.records.length}</span>
      <span data-testid="has-more">{String(result.hasMore)}</span>
      {result.status === 'ready' && result.hasMore && <div ref={result.sentinelRef} data-testid="sentinel" />}
    </div>
  );
}

/**
 * The production shape: the hook runs in FindView, but the sentinel is rendered by FindResultsList, which
 * mounts only once the DOCUMENTS request has loaded — so the sentinel can appear AFTER records are ready.
 */
function LateSentinelHarness({
  seed,
  showSentinel,
}: {
  seed: RecordSearchSeed;
  showSentinel: boolean;
}): React.ReactElement {
  const result = useFindRecordMatches(seed);
  return (
    <div>
      <span data-testid="status">{result.status}</span>
      {showSentinel && result.status === 'ready' && result.hasMore && (
        <div ref={result.sentinelRef} data-testid="sentinel" />
      )}
    </div>
  );
}

beforeEach(() => {
  mockPost.mockReset();
  observerCallbacks = [];
});

describe('deriveRecordSearchSeed — the bridge is seeded by the document’s own AI profile', () => {
  it('prefers keywords, then the TL;DR, then the summary — and names which one it used', () => {
    expect(
      deriveRecordSearchSeed(completedProfile({ keywords: 'indemnity, Delaware', tldr: 't', summary: 's' }))
    ).toEqual({ query: 'indemnity, Delaware', source: 'keywords' });
    expect(deriveRecordSearchSeed(completedProfile({ keywords: '  ', tldr: 'An NDA.', summary: 's' }))).toEqual({
      query: 'An NDA.',
      source: 'tldr',
    });
    expect(deriveRecordSearchSeed(completedProfile({ summary: 'A long summary.' }))).toEqual({
      query: 'A long summary.',
      source: 'summary',
    });
  });

  it('gives NO seed rather than inventing one, when the profile is not complete or has no text', () => {
    // Inventing a query (e.g. from the title) would reintroduce the low-signal seed task 034 rejected.
    expect(deriveRecordSearchSeed({ kind: 'loading' })).toBeNull();
    expect(deriveRecordSearchSeed({ kind: 'status', status: 'Pending' })).toBeNull();
    expect(deriveRecordSearchSeed({ kind: 'no-identity' })).toBeNull();
    expect(deriveRecordSearchSeed(completedProfile({}))).toBeNull();
  });

  it('caps the query at the route’s 1000-character limit, on a word boundary', () => {
    const seed = deriveRecordSearchSeed(completedProfile({ summary: 'word '.repeat(400) }));
    expect(seed).not.toBeNull();
    expect(seed!.query.length).toBeLessThanOrEqual(1000);
    expect(seed!.query.endsWith('word')).toBe(true);
  });
});

describe('useFindRecordMatches — request and paging', () => {
  const SEED: RecordSearchSeed = { query: 'indemnity, Delaware', source: 'keywords' };

  it('asks ONLY the per-row-authorized record search route, with the seed and all three record types', async () => {
    mockPost.mockResolvedValueOnce(page(2));
    render(<Harness seed={SEED} />);

    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('ready'));
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost).toHaveBeenCalledWith('/api/ai/search/records', {
      query: 'indemnity, Delaware',
      recordTypes: ['sprk_matter', 'sprk_project', 'sprk_invoice'],
      options: { limit: RECORD_PAGE_SIZE, offset: 0 },
    });
  });

  it('a SHORT page is not the end: scrolling fetches the next window, advanced by the REQUESTED size; an empty page ends it', async () => {
    // 3 < 25: what page 1 looks like after the server trims 22 unreadable rows — or genuinely has 3.
    // Under ADR-051's literal "short page ⇒ end" rule paging would stop here. It must not.
    mockPost.mockResolvedValueOnce(page(3)).mockResolvedValueOnce(page(0));
    render(<Harness seed={SEED} />);

    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('3'));
    expect(screen.getByTestId('has-more').textContent).toBe('true');

    act(() => scrollSentinelIntoView());
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(2));
    // offset 25 (the pre-filter window), NOT 3 (rows that survived) — advancing by the returned count would
    // re-request rows the server already evaluated.
    expect(mockPost.mock.calls[1][1]).toMatchObject({ options: { limit: RECORD_PAGE_SIZE, offset: RECORD_PAGE_SIZE } });

    await waitFor(() => expect(screen.getByTestId('has-more').textContent).toBe('false'));
    expect(screen.queryByTestId('sentinel')).toBeNull();
    expect(screen.getByTestId('count').textContent).toBe('3');
  });

  it('still pages when the sentinel mounts AFTER records are ready — i.e. records answered before documents', async () => {
    // Regression for a race found in code review: with an object ref the observer effect ran while the
    // sentinel did not exist yet, bailed, and was never re-run — records stopped at page 1 forever.
    mockPost.mockResolvedValueOnce(page(RECORD_PAGE_SIZE)).mockResolvedValueOnce(page(0));
    const { rerender } = render(<LateSentinelHarness seed={SEED} showSentinel={false} />);

    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('ready'));
    expect(observerCallbacks).toHaveLength(0);

    // The documents land; FindResultsList mounts and renders the records sentinel.
    rerender(<LateSentinelHarness seed={SEED} showSentinel />);
    await waitFor(() => expect(observerCallbacks.length).toBeGreaterThan(0));

    act(() => scrollSentinelIntoView());
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(2));
    expect(mockPost.mock.calls[1][1]).toMatchObject({ options: { offset: RECORD_PAGE_SIZE } });
  });

  it('stops before requesting an offset the route would reject', async () => {
    // The route's [Range(0, 1000)] on offset: asking for more would be a 400, not an empty page.
    expect(MAX_RECORD_OFFSET).toBe(1000);
    const pagesToCap = Math.floor(MAX_RECORD_OFFSET / RECORD_PAGE_SIZE) + 1; // offsets 0 … 1000
    for (let i = 0; i < pagesToCap; i++) {
      mockPost.mockResolvedValueOnce(page(RECORD_PAGE_SIZE, i * RECORD_PAGE_SIZE));
    }
    render(<Harness seed={SEED} />);

    for (let i = 1; i < pagesToCap; i++) {
      await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(i));
      await waitFor(() => expect(screen.queryByTestId('sentinel')).not.toBeNull());
      act(() => scrollSentinelIntoView());
    }
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(pagesToCap));
    await waitFor(() => expect(screen.getByTestId('has-more').textContent).toBe('false'));

    const offsets = mockPost.mock.calls.map(call => call[1].options.offset);
    expect(Math.max(...offsets)).toBe(MAX_RECORD_OFFSET);
  });
});
