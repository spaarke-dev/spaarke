/**
 * #2973: the shared host wiring for the Email surface's Dataverse mounts (SpaarkeAi widget, LegalWorkspace section,
 * sprk_emailpage). The real compose-handler factory runs; only the Xrm reads (current user's mailbox, recipient
 * search) are faked.
 */
import { act, renderHook, waitFor } from '@testing-library/react';

jest.mock('../createXrmEmailComposeHandlers', () => ({
  ...jest.requireActual('../createXrmEmailComposeHandlers'),
  resolveCurrentUserEmail: jest.fn(),
}));
jest.mock('../../../services/userLookup', () => ({
  searchUsersAndContacts: jest.fn(),
}));

import { resolveCurrentUserEmail } from '../createXrmEmailComposeHandlers';
import { searchUsersAndContacts } from '../../../services/userLookup';
import { XrmDataverseClient } from '../../../services/XrmDataverseClient';
import { useXrmEmailHostProps, type UseXrmEmailHostPropsOptions } from '../useXrmEmailHostProps';

const mockResolveEmail = resolveCurrentUserEmail as jest.MockedFunction<typeof resolveCurrentUserEmail>;
const mockSearch = searchUsersAndContacts as jest.MockedFunction<typeof searchUsersAndContacts>;
const authenticatedFetch = jest.fn() as unknown as NonNullable<UseXrmEmailHostPropsOptions['authenticatedFetch']>;

beforeEach(() => {
  mockResolveEmail.mockReset().mockResolvedValue('user@contoso.example');
  mockSearch.mockReset().mockResolvedValue([]);
});

describe('useXrmEmailHostProps', () => {
  it('returns every prop the Email surface takes from its host, with the BFF handlers once the URL is known', async () => {
    const { result } = renderHook(() =>
      useXrmEmailHostProps({ authenticatedFetch, bffBaseUrl: 'https://bff.example' })
    );
    await waitFor(() => expect(result.current.fromMailbox).toBe('user@contoso.example'));

    const props = result.current;
    expect(props.dataverseClient).toBeInstanceOf(XrmDataverseClient);
    expect(props.dataService).toBeDefined();
    expect(props.navigationService).toBeDefined();
    expect(props.dataverseUrl).toBe('');
    for (const handler of [
      'onSearchRecipients',
      'onLookupRecipients',
      'onLookupRecord',
      'onAddRelationship',
      'onUploadLocalAttachment',
      'onResolveShareLink',
      'onListEmailTemplates',
      'onRenderEmailTemplate',
      'onDraftWithAi',
    ] as const) {
      expect(typeof props[handler]).toBe('function');
    }
    expect(Array.isArray(props.recordLookupCatalog)).toBe(true);
    expect(props.recordLookupCatalog.length).toBeGreaterThan(0);
  });

  it('leaves out the BFF-backed handlers while the URL is unresolved, and adds them when it resolves', async () => {
    const { result, rerender } = renderHook((opts: UseXrmEmailHostPropsOptions) => useXrmEmailHostProps(opts), {
      initialProps: { authenticatedFetch, bffBaseUrl: null },
    });
    await waitFor(() => expect(result.current.fromMailbox).toBeDefined());
    expect(result.current.onUploadLocalAttachment).toBeUndefined();
    expect(result.current.onRenderEmailTemplate).toBeUndefined();
    expect(result.current.onDraftWithAi).toBeUndefined();
    const adapters = [result.current.dataverseClient, result.current.dataService, result.current.navigationService];

    rerender({ authenticatedFetch, bffBaseUrl: 'https://bff.example' });

    expect(typeof result.current.onUploadLocalAttachment).toBe('function');
    expect(typeof result.current.onRenderEmailTemplate).toBe('function');
    expect(typeof result.current.onDraftWithAi).toBe('function');
    // The adapters are created once per mount.
    expect([result.current.dataverseClient, result.current.dataService, result.current.navigationService]).toEqual(
      adapters
    );
    expect(result.current.dataverseClient).toBe(adapters[0]);
  });

  it('treats an empty BFF URL as unresolved', async () => {
    const { result } = renderHook(() => useXrmEmailHostProps({ authenticatedFetch, bffBaseUrl: '' }));
    await waitFor(() => expect(result.current.fromMailbox).toBeDefined());
    expect(result.current.onDraftWithAi).toBeUndefined();
  });

  it('returns the same object across renders when nothing changed', async () => {
    const { result, rerender } = renderHook(() =>
      useXrmEmailHostProps({ authenticatedFetch, bffBaseUrl: 'https://bff.example' })
    );
    await waitFor(() => expect(result.current.fromMailbox).toBeDefined());
    const first = result.current;
    rerender();
    expect(result.current).toBe(first);
  });

  it('searches recipients through the mount data service', async () => {
    const { result } = renderHook(() => useXrmEmailHostProps({ authenticatedFetch, bffBaseUrl: null }));
    await waitFor(() => expect(result.current.fromMailbox).toBeDefined());
    await act(async () => {
      await result.current.onSearchRecipients('ali');
    });
    expect(mockSearch).toHaveBeenCalledWith(result.current.dataService, 'ali');
  });
});
