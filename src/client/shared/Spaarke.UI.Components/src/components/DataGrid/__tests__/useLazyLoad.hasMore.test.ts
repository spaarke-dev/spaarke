/**
 * useLazyLoad — `hasMore` page-fullness fallback (email-communication-intelligence-r2
 * UAT round 5, item 1). The MDA `Xrm.WebApi` client RESPECTS the injected FetchXML
 * `page`/`count` but STRIPS the `@Microsoft.Dynamics.CRM.morerecords` + paging-cookie
 * annotations, so `moreRecords` is always `false` there and infinite-scroll never
 * advanced past page 1. The hook now infers `hasMore` from page-fullness when the flag
 * is absent: a page filled to `pageSize` has a successor; a short/empty page is the end.
 */
import { renderHook, waitFor, act } from '@testing-library/react';
import { useLazyLoad } from '../useLazyLoad';
import type { IDataverseClient, FetchMultipleResult } from '../../../services/IDataverseClient';

const FETCH = '<fetch><entity name="sprk_communication" /></fetch>';

function rows(n: number): Record<string, unknown>[] {
  return Array.from({ length: n }, (_v, i) => ({ id: `r${i}` }));
}

/** A client whose successive pages are supplied by `pages` (each a FetchMultipleResult). */
function clientReturning(pages: FetchMultipleResult[]): IDataverseClient {
  let call = 0;
  return {
    retrieveMultipleRecords: jest.fn(async () => pages[Math.min(call++, pages.length - 1)]),
  } as unknown as IDataverseClient;
}

describe('useLazyLoad hasMore — page-fullness fallback (item 1)', () => {
  it('a FULL page with moreRecords=false (Xrm strips the flag) still reports hasMore=true', async () => {
    const client = clientReturning([{ entities: rows(50), moreRecords: false }]);
    const { result } = renderHook(() =>
      useLazyLoad({ dataverseClient: client, entityName: 'sprk_communication', fetchXml: FETCH, pageSize: 50 })
    );
    await waitFor(() => expect(result.current.records).toHaveLength(50));
    expect(result.current.hasMore).toBe(true);
  });

  it('a SHORT page (fewer than pageSize) reports hasMore=false', async () => {
    const client = clientReturning([{ entities: rows(30), moreRecords: false }]);
    const { result } = renderHook(() =>
      useLazyLoad({ dataverseClient: client, entityName: 'sprk_communication', fetchXml: FETCH, pageSize: 50 })
    );
    await waitFor(() => expect(result.current.records).toHaveLength(30));
    expect(result.current.hasMore).toBe(false);
  });

  it('paginates full pages then stops on the short final page', async () => {
    const client = clientReturning([
      { entities: rows(50), moreRecords: false }, // page 1 — full → more
      { entities: rows(50), moreRecords: false }, // page 2 — full → more
      { entities: rows(20), moreRecords: false }, // page 3 — short → end
    ]);
    const { result } = renderHook(() =>
      useLazyLoad({ dataverseClient: client, entityName: 'sprk_communication', fetchXml: FETCH, pageSize: 50 })
    );
    await waitFor(() => expect(result.current.records).toHaveLength(50));
    expect(result.current.hasMore).toBe(true);

    act(() => result.current.fetchNextPage());
    await waitFor(() => expect(result.current.records).toHaveLength(100));
    expect(result.current.hasMore).toBe(true);

    act(() => result.current.fetchNextPage());
    await waitFor(() => expect(result.current.records).toHaveLength(120));
    expect(result.current.hasMore).toBe(false);
  });

  it('honors an explicit moreRecords=true (BFF client) regardless of page fullness', async () => {
    const client = clientReturning([{ entities: rows(10), moreRecords: true }]);
    const { result } = renderHook(() =>
      useLazyLoad({ dataverseClient: client, entityName: 'sprk_communication', fetchXml: FETCH, pageSize: 50 })
    );
    await waitFor(() => expect(result.current.records).toHaveLength(10));
    expect(result.current.hasMore).toBe(true);
  });
});
