/**
 * useEmailViews.test.ts
 *
 * Unit tests for the Email surface's view-selection wiring hook
 * (email-communication-solution-r5 task 031, FR-04). Covers the closed
 * acceptance set from the task POML:
 *   - default view resolves to "Email — Inbox" on open
 *   - falls back to the entity default view when "Email — Inbox" is absent
 *   - switching the view re-runs that view's FetchXML UNMODIFIED and
 *     re-populates `rows`
 *   - a view-load failure surfaces as `error`, never throws
 *   - no saved views at all surfaces a descriptive `error` (escalation gap),
 *     never throws
 */
import { renderHook, waitFor, act } from '@testing-library/react';
import type {
  IDataverseClient,
  SavedQuerySummary,
  SavedQueryResult,
  FetchMultipleResult,
} from '@spaarke/ui-components';
import { useEmailViews, EMAIL_INBOX_VIEW_NAME } from '../useEmailViews';
import { ensureAssociationColumns } from '../ensureAssociationColumns';

/** Builds a stub `IDataverseClient` — only the 3 methods `useEmailViews` calls are exercised. */
function makeStubClient(overrides: {
  savedQueries?: SavedQuerySummary[];
  savedQueriesError?: Error;
  fetchXmlByViewId?: Record<string, string>;
  rowsByViewId?: Record<string, Record<string, unknown>[]>;
  retrieveMultipleError?: Error;
}): IDataverseClient & {
  retrieveSavedQueriesForEntity: jest.Mock;
  retrieveSavedQuery: jest.Mock;
  retrieveMultipleRecords: jest.Mock;
} {
  const retrieveSavedQueriesForEntity = jest.fn(async (_entityName: string): Promise<SavedQuerySummary[]> => {
    if (overrides.savedQueriesError) throw overrides.savedQueriesError;
    return overrides.savedQueries ?? [];
  });

  const retrieveSavedQuery = jest.fn(async (savedQueryId: string): Promise<SavedQueryResult> => {
    return {
      entityName: 'sprk_communication',
      name: savedQueryId,
      layoutXml: '<grid />',
      fetchXml: overrides.fetchXmlByViewId?.[savedQueryId] ?? `<fetch><entity name="sprk_communication" /></fetch>`,
    };
  });

  const retrieveMultipleRecords = jest.fn(
    async (_entityName: string, fetchXml: string): Promise<FetchMultipleResult<Record<string, unknown>>> => {
      if (overrides.retrieveMultipleError) throw overrides.retrieveMultipleError;
      // Find which view this fetchXml belongs to so the stub returns matching rows.
      // The hook injects the association columns via `ensureAssociationColumns`
      // before running the view, so match the stored FetchXML through the SAME
      // augmentation (the augmentation is deterministic for a given input).
      const viewId = Object.entries(overrides.fetchXmlByViewId ?? {}).find(
        ([, xml]) => ensureAssociationColumns(xml) === fetchXml
      )?.[0];
      const entities = (viewId && overrides.rowsByViewId?.[viewId]) ?? [];
      return { entities, moreRecords: false };
    }
  );

  return {
    retrieveSavedQueriesForEntity,
    retrieveSavedQuery,
    retrieveMultipleRecords,
    retrieveEntityMetadata: jest.fn(),
    retrieveRecord: jest.fn(),
  } as unknown as IDataverseClient & {
    retrieveSavedQueriesForEntity: jest.Mock;
    retrieveSavedQuery: jest.Mock;
    retrieveMultipleRecords: jest.Mock;
  };
}

describe('useEmailViews', () => {
  it('defaults to the "Email — Inbox" saved view on open and loads its rows', async () => {
    const inboxId = 'view-inbox';
    const sentId = 'view-sent';
    const client = makeStubClient({
      savedQueries: [
        { id: sentId, name: 'Email — Sent', isDefault: false, queryType: 0 },
        { id: inboxId, name: EMAIL_INBOX_VIEW_NAME, isDefault: false, queryType: 0 },
      ],
      fetchXmlByViewId: {
        [inboxId]:
          '<fetch><entity name="sprk_communication"><filter><condition attribute="sprk_communicationtype" operator="eq" value="100000000" /></filter></entity></fetch>',
        [sentId]: '<fetch><entity name="sprk_communication" /></fetch>',
      },
      rowsByViewId: {
        [inboxId]: [{ sprk_communicationid: 'row-1' }, { sprk_communicationid: 'row-2' }],
        [sentId]: [{ sprk_communicationid: 'row-9' }],
      },
    });

    const { result } = renderHook(() => useEmailViews(client));

    await waitFor(() => expect(result.current.selectedViewId).toBe(inboxId));
    await waitFor(() => expect(result.current.rows).toHaveLength(2));

    expect(result.current.error).toBeNull();
    expect(result.current.views.map(v => v.id)).toEqual(expect.arrayContaining([inboxId, sentId]));
    // Preserves the view's own Email predicate — the hook injects the association
    // columns (for the card status dot) but never adds its own Email filter.
    expect(client.retrieveMultipleRecords).toHaveBeenCalledWith(
      'sprk_communication',
      expect.stringContaining('sprk_communicationtype')
    );
    // The association columns the card status dot needs are injected into the run.
    expect(client.retrieveMultipleRecords).toHaveBeenCalledWith(
      'sprk_communication',
      expect.stringContaining('sprk_associationstatus')
    );
  });

  it('falls back to the entity default view when "Email — Inbox" is absent', async () => {
    const defaultId = 'view-all-email';
    const client = makeStubClient({
      savedQueries: [
        { id: 'view-drafts', name: 'Email — Drafts', isDefault: false, queryType: 0 },
        { id: defaultId, name: 'All Email', isDefault: true, queryType: 0 },
      ],
      fetchXmlByViewId: { [defaultId]: '<fetch><entity name="sprk_communication" /></fetch>' },
      rowsByViewId: { [defaultId]: [{ sprk_communicationid: 'row-1' }] },
    });

    const { result } = renderHook(() => useEmailViews(client));

    await waitFor(() => expect(result.current.selectedViewId).toBe(defaultId));
    expect(result.current.error).toBeNull();
  });

  it("re-runs the newly selected view's FetchXML and re-populates rows on view switch", async () => {
    const inboxId = 'view-inbox';
    const sentId = 'view-sent';
    const sentFetchXml =
      '<fetch><entity name="sprk_communication"><filter><condition attribute="sprk_communicationtype" operator="eq" value="100000000" /><condition attribute="sprk_direction" operator="eq" value="100000001" /></filter></entity></fetch>';
    const client = makeStubClient({
      savedQueries: [
        { id: inboxId, name: EMAIL_INBOX_VIEW_NAME, isDefault: false, queryType: 0 },
        { id: sentId, name: 'Email — Sent', isDefault: false, queryType: 0 },
      ],
      fetchXmlByViewId: {
        [inboxId]: '<fetch><entity name="sprk_communication" /></fetch>',
        [sentId]: sentFetchXml,
      },
      rowsByViewId: {
        [inboxId]: [{ sprk_communicationid: 'row-1' }, { sprk_communicationid: 'row-2' }],
        [sentId]: [{ sprk_communicationid: 'row-9' }],
      },
    });

    const { result } = renderHook(() => useEmailViews(client));
    await waitFor(() => expect(result.current.selectedViewId).toBe(inboxId));
    await waitFor(() => expect(result.current.rows).toHaveLength(2));

    act(() => {
      result.current.setSelectedViewId(sentId);
    });

    await waitFor(() => expect(result.current.selectedViewId).toBe(sentId));
    await waitFor(() => expect(result.current.rows).toEqual([{ sprk_communicationid: 'row-9' }]));

    // The Sent view's own filter/sort/columns are preserved — only the association
    // columns are injected (no extra Email predicate added on top).
    expect(client.retrieveMultipleRecords).toHaveBeenLastCalledWith(
      'sprk_communication',
      ensureAssociationColumns(sentFetchXml)
    );
  });

  it('surfaces a view-list load failure via `error` instead of throwing', async () => {
    const client = makeStubClient({ savedQueriesError: new Error('boom') });

    const { result } = renderHook(() => useEmailViews(client));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error?.message).toBe('boom');
    expect(result.current.rows).toEqual([]);
  });

  it('surfaces a descriptive error (never throws) when no saved views resolve at all', async () => {
    const client = makeStubClient({ savedQueries: [] });

    const { result } = renderHook(() => useEmailViews(client));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error?.message).toMatch(/No saved views found/);
    expect(result.current.selectedViewId).toBeUndefined();
  });

  it('surfaces a FetchXML run failure via `error` instead of throwing', async () => {
    const inboxId = 'view-inbox';
    const client = makeStubClient({
      savedQueries: [{ id: inboxId, name: EMAIL_INBOX_VIEW_NAME, isDefault: false, queryType: 0 }],
      fetchXmlByViewId: { [inboxId]: '<fetch><entity name="sprk_communication" /></fetch>' },
      retrieveMultipleError: new Error('fetch failed'),
    });

    const { result } = renderHook(() => useEmailViews(client));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error?.message).toBe('fetch failed');
  });
});
