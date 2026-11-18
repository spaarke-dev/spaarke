/**
 * R4 task 015 — where `useRecord` gets a record: the per-opening reader, the entity's own reader, or the module fetch; and the
 * seed an opener already holds.
 *
 * Order (decision 4): per-opening reader -> entity `read` -> module fetch. A seed is shown as ready at the first attempt with no
 * read at all; a later attempt (Refresh) reads through the reader as usual. `RecordNotFoundError` and a 404 are "not found";
 * a 403 is "denied"; anything else is a retryable error.
 *
 * Substituted: the network seam only (`bffApiCall`).
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall };
});

import { ApiError } from '../src/types';
import {
  RECORD_DETAIL_ENTITIES,
  type DetailRecord,
  type RecordDetailEntity,
} from '../src/components/detail/RecordDetailFields';
import { RecordNotFoundError, useRecord } from '../src/components/record/recordReads';

const ID = '00000000-0000-4000-8000-000000000801';
const MATTER = RECORD_DETAIL_ENTITIES.sprk_matter;
const EVENT = RECORD_DETAIL_ENTITIES.sprk_event;

const row = (name: string): DetailRecord => ({ sprk_eventid: ID, sprk_name: name });
const fetchCalls = (): number =>
  h.bffApiCall.mock.calls.filter(c => String(c[0]).endsWith('/api/dataverse/fetch')).length;

beforeEach(() => {
  h.bffApiCall.mockReset();
  h.bffApiCall.mockResolvedValue({
    entities: [{ sprk_matterid: ID, sprk_mattername: 'From the fetch' }],
    moreRecords: false,
  });
});

describe('useRecord: the source of the record', () => {
  it('an entity without `read` reads the module fetch (unchanged)', async () => {
    const { result } = renderHook(() => useRecord(MATTER, ID, 0));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(fetchCalls()).toBe(1);
  });

  it("the entity's own `read` wins over the module fetch (the sprk_event entry reads GET /events/{id})", async () => {
    const read = vi.fn().mockResolvedValue(row('From the entity reader'));
    const cfg: RecordDetailEntity = { ...MATTER, read };
    const { result } = renderHook(() => useRecord(cfg, ID, 0));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(read).toHaveBeenCalledWith(ID);
    expect(fetchCalls()).toBe(0);
    expect(result.current).toEqual({ status: 'ready', record: row('From the entity reader') });
  });

  it("a per-opening reader wins over the entity's own `read`, and nothing else is called", async () => {
    const entityRead = vi.fn().mockResolvedValue(row('entity'));
    const opening = vi.fn().mockResolvedValue(row('opening'));
    const cfg: RecordDetailEntity = { ...MATTER, read: entityRead };
    const { result } = renderHook(() => useRecord(cfg, ID, 0, { read: opening }));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(opening).toHaveBeenCalledWith(ID);
    expect(entityRead).not.toHaveBeenCalled();
    expect(fetchCalls()).toBe(0);
    expect(result.current).toEqual({ status: 'ready', record: row('opening') });
  });

  it('the sprk_event entity has its own reader (it is not on the module fetch seam)', () => {
    expect(typeof EVENT.read).toBe('function');
    expect(typeof MATTER.read).toBe('undefined');
  });
});

describe('useRecord: the seed', () => {
  it('a seed is ready at once at attempt 0 with NO read at all', () => {
    const opening = vi.fn().mockResolvedValue(row('re-read'));
    const { result } = renderHook(() => useRecord(EVENT, ID, 0, { seed: row('seed'), read: opening }));
    expect(result.current).toEqual({ status: 'ready', record: row('seed') });
    expect(opening).not.toHaveBeenCalled();
    expect(h.bffApiCall).not.toHaveBeenCalled();
  });

  it('a later attempt (Refresh) reads through the per-opening reader and replaces the seed', async () => {
    const opening = vi.fn().mockResolvedValue(row('re-read'));
    const { result, rerender } = renderHook(
      ({ attempt }) => useRecord(EVENT, ID, attempt, { seed: row('seed'), read: opening }),
      {
        initialProps: { attempt: 0 },
      }
    );
    expect(opening).not.toHaveBeenCalled();
    rerender({ attempt: 1 });
    await waitFor(() => expect(result.current).toEqual({ status: 'ready', record: row('re-read') }));
    expect(opening).toHaveBeenCalledTimes(1);
  });

  it('a new function identity for the reader on re-render does not re-read (it is read through a ref)', async () => {
    const calls: string[] = [];
    const { result, rerender } = renderHook(
      ({ tag }) =>
        useRecord(EVENT, ID, 0, {
          read: async () => {
            calls.push(tag);
            return row(tag);
          },
        }),
      { initialProps: { tag: 'a' } }
    );
    await waitFor(() => expect(result.current.status).toBe('ready'));
    rerender({ tag: 'b' });
    rerender({ tag: 'c' });
    expect(calls).toEqual(['a']);
  });
});

describe("useRecord: what a reader's failure means", () => {
  it.each([
    ['RecordNotFoundError', () => new RecordNotFoundError('gone'), 'notfound'],
    ['an ApiError 404', () => new ApiError(404, ''), 'notfound'],
    ['an ApiError 403', () => new ApiError(403, ''), 'denied'],
    ['an ApiError 500', () => new ApiError(500, ''), 'error'],
    ['a plain Error', () => new Error('boom'), 'error'],
  ] as const)('%s -> %s', async (_name, make, expected) => {
    const { result } = renderHook(() => useRecord(EVENT, ID, 0, { read: async () => Promise.reject(make()) }));
    await waitFor(() => expect(result.current.status).toBe(expected));
    if (expected === 'error') expect(result.current).toMatchObject({ retryable: true });
  });

  it('a reader that throws synchronously is mapped the same way', async () => {
    const { result } = renderHook(() =>
      useRecord(EVENT, ID, 0, {
        read: () => {
          throw new RecordNotFoundError('gone');
        },
      })
    );
    await waitFor(() => expect(result.current.status).toBe('notfound'));
  });

  it('a malformed id is never sent to any reader (must-not-fire)', async () => {
    const opening = vi.fn();
    const { result } = renderHook(() => useRecord(EVENT, 'not-a-guid', 0, { read: opening }));
    await waitFor(() => expect(result.current.status).toBe('error'));
    expect(result.current).toMatchObject({ retryable: false });
    expect(opening).not.toHaveBeenCalled();
    expect(h.bffApiCall).not.toHaveBeenCalled();
  });
});
