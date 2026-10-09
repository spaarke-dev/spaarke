/**
 * useRelatedDocumentGraphData — failures as `@spaarke/auth`'s authenticatedFetch delivers them (THROWN
 * ApiError / AuthError, never a returned non-OK Response). A 404 is "no relationships yet" (empty graph,
 * count 0, no error); 401/403 is the permission sentence; another status is the count-load sentence.
 */
import { renderHook, act } from '@testing-library/react-hooks';
import { useRelatedDocumentGraphData } from '../useRelatedDocumentGraphData';

jest.mock('@spaarke/auth', () => jest.requireActual('./spaarkeAuthDouble'));

const API_BASE = 'https://spe-api-dev.azurewebsites.net';
const DOC_ID = 'abc-123-def-456';

function response(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn().mockResolvedValue(body),
  } as unknown as Response;
}

const graph = {
  nodes: [
    { id: 'src', type: 'source', depth: 0, data: { label: 'Source' } },
    { id: 'r1', type: 'related', depth: 1, data: { label: 'Related', similarity: 0.9 } },
  ],
  edges: [{ id: 'e1', source: 'src', target: 'r1', data: { similarity: 0.9, relationshipType: 'semantic' } }],
  metadata: { totalResults: 1 },
};

let fetchMock: jest.Mock;

beforeEach(() => {
  fetchMock = jest.fn();
  global.fetch = fetchMock;
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe('useRelatedDocumentGraphData — thrown failures', () => {
  it('a 404 after a loaded graph clears it to an empty graph, count 0, no error', async () => {
    fetchMock.mockResolvedValueOnce(response(graph));
    const { result, waitForNextUpdate } = renderHook(() =>
      useRelatedDocumentGraphData(DOC_ID, undefined, API_BASE, true)
    );
    await waitForNextUpdate();
    expect(result.current.nodes).toHaveLength(2);
    expect(result.current.count).toBe(1);

    fetchMock.mockResolvedValueOnce(response({ title: 'Not Found', status: 404 }, 404));
    await act(async () => {
      result.current.refetch();
      await waitForNextUpdate();
    });

    expect(result.current.error).toBeNull();
    expect(result.current.count).toBe(0);
    expect(result.current.nodes).toEqual([]);
    expect(result.current.edges).toEqual([]);
    expect(result.current.lastUpdated).toBeInstanceOf(Date);
  });

  it.each([401, 403])('a %i shows the permission sentence', async status => {
    fetchMock.mockResolvedValueOnce(response({}, status));
    const { result, waitForNextUpdate } = renderHook(() =>
      useRelatedDocumentGraphData(DOC_ID, undefined, API_BASE, true)
    );
    await waitForNextUpdate();
    expect(result.current.error).toBe("You don't have permission to view related documents.");
  });

  it('a 500 shows the count-load sentence', async () => {
    fetchMock.mockResolvedValueOnce(response({}, 500));
    const { result, waitForNextUpdate } = renderHook(() =>
      useRelatedDocumentGraphData(DOC_ID, undefined, API_BASE, true)
    );
    await waitForNextUpdate();
    expect(result.current.error).toBe('Failed to load related document count.');
  });

  it('a network failure keeps the generic sentence', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    const { result, waitForNextUpdate } = renderHook(() =>
      useRelatedDocumentGraphData(DOC_ID, undefined, API_BASE, true)
    );
    await waitForNextUpdate();
    expect(result.current.error).toBe('Unable to load related documents. Please try again.');
  });
});
