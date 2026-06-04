/**
 * useWorkspaceLayouts — consolidated hook unit tests (R4 task 051 / C-3).
 *
 * Covers the FR-13 acceptance scenarios:
 *   - Cache hit (fast hydration from sessionStorage)
 *   - Cache miss (BFF fetch path)
 *   - fallbackLayout used on empty list + network error
 *   - parseLayoutJson invoked when provided; omitted when not
 *   - Auth-not-ready deferral (isAuthenticated=false)
 *   - 401 / 403 error paths (warns, sets error, falls back if provided)
 *   - invalidateLayoutCache clears the namespace
 *   - cacheKeyPrefix namespace isolation (LW + SpaarkeAi don't collide)
 *
 * Standards:
 *   - ADR-028 (function-based auth — tests inject `authenticatedFetch` mock)
 *   - ADR-012 (shared-lib hook is context-agnostic; tests don't reference
 *     LegalWorkspace or SpaarkeAi specifics)
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import {
  useWorkspaceLayouts,
  invalidateLayoutCache,
  type WorkspaceLayoutDto,
  type AuthenticatedFetch,
} from '../useWorkspaceLayouts';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const FIXTURE_LAYOUT_USER: WorkspaceLayoutDto = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'My Custom Layout',
  layoutTemplateId: '3-row-mixed',
  sectionsJson: JSON.stringify({ schemaVersion: 1, rows: [] }),
  isDefault: true,
  sortOrder: 0,
  isSystem: false,
  modifiedOn: '2026-05-26T10:00:00+00:00',
};

const FIXTURE_LAYOUT_SYSTEM: WorkspaceLayoutDto = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'System Default',
  layoutTemplateId: '3-row-mixed',
  sectionsJson: JSON.stringify({ schemaVersion: 1, rows: [] }),
  isDefault: false,
  sortOrder: 1,
  isSystem: true,
  modifiedOn: '1970-01-01T00:00:00+00:00',
};

const FALLBACK_LAYOUT: WorkspaceLayoutDto = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'Hardcoded Fallback',
  layoutTemplateId: '3-row-mixed',
  sectionsJson: JSON.stringify({ schemaVersion: 1, rows: [] }),
  isDefault: true,
  sortOrder: 0,
  isSystem: true,
  modifiedOn: '1970-01-01T00:00:00+00:00',
};

// ---------------------------------------------------------------------------
// Mock helpers
// ---------------------------------------------------------------------------

function mockOkResponse<T>(body: T): Response {
  return {
    ok: true,
    status: 200,
    json: async () => body,
  } as Response;
}

function mockErrorResponse(status: number): Response {
  return {
    ok: false,
    status,
    json: async () => ({}),
  } as Response;
}

function createMockFetch(responses: Record<string, Response>): jest.MockedFunction<AuthenticatedFetch> {
  return jest.fn(async (url: string) => {
    // Match on path suffix so tests don't have to specify the full base url
    for (const [pathSuffix, response] of Object.entries(responses)) {
      if (url.endsWith(pathSuffix)) return response;
    }
    throw new Error(`Unexpected fetch URL: ${url}`);
  });
}

// ---------------------------------------------------------------------------
// Lifecycle: clear sessionStorage between tests
// ---------------------------------------------------------------------------

beforeEach(() => {
  sessionStorage.clear();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Cache hit
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — cache hit', () => {
  it('hydrates immediately from sessionStorage cache when present', async () => {
    // Pre-populate the LW cache namespace
    sessionStorage.setItem('sprk:workspace:layoutsList', JSON.stringify([FIXTURE_LAYOUT_USER]));
    sessionStorage.setItem('sprk:workspace:activeLayout', JSON.stringify(FIXTURE_LAYOUT_USER));

    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        cacheKeyPrefix: 'sprk:workspace',
      })
    );

    // Cache hit means initial render has data + isLoading=false
    expect(result.current.layouts).toEqual([FIXTURE_LAYOUT_USER]);
    expect(result.current.activeLayout).toEqual(FIXTURE_LAYOUT_USER);
    expect(result.current.isLoading).toBe(false);

    // Background revalidation still fires
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
  });
});

// ---------------------------------------------------------------------------
// Cache miss
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — cache miss', () => {
  it('fetches from BFF when sessionStorage is empty', async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        cacheKeyPrefix: 'sprk:workspace',
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.layouts).toEqual([FIXTURE_LAYOUT_USER]);
    expect(result.current.activeLayout).toEqual(FIXTURE_LAYOUT_USER);
    expect(fetchMock).toHaveBeenCalledTimes(2); // list + default in parallel
  });
});

// ---------------------------------------------------------------------------
// Fallback layout
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — fallbackLayout', () => {
  it('renders fallbackLayout when list is empty AND no default resolved', async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([]),
      '/workspace/layouts/default': mockErrorResponse(404),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        fallbackLayout: FALLBACK_LAYOUT,
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.layouts).toEqual([FALLBACK_LAYOUT]);
    expect(result.current.activeLayout).toEqual(FALLBACK_LAYOUT);
  });

  it('renders fallbackLayout on network error', async () => {
    const fetchMock = jest.fn(async () => {
      throw new Error('Network failure');
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        fallbackLayout: FALLBACK_LAYOUT,
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.activeLayout).toEqual(FALLBACK_LAYOUT);
    expect(result.current.error).toBeTruthy();
    expect(result.current.status).toBe('error');
  });

  it('renders empty when no fallbackLayout supplied + network error', async () => {
    const fetchMock = jest.fn(async () => {
      throw new Error('Network failure');
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        // No fallbackLayout — SpaarkeAi-style degrade-to-empty.
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.layouts).toEqual([]);
    expect(result.current.activeLayout).toBeNull();
    expect(result.current.error).toBeTruthy();
  });
});

// ---------------------------------------------------------------------------
// parseLayoutJson
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — parseLayoutJson', () => {
  it('invokes parseLayoutJson when supplied and returns parsedActiveLayout', async () => {
    const parser = jest.fn((raw: unknown) => ({
      parsed: true,
      raw,
    }));

    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        parseLayoutJson: parser,
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(parser).toHaveBeenCalled();
    expect(result.current.parsedActiveLayout).toMatchObject({ parsed: true });
  });

  it('returns parsedActiveLayout=undefined when no parser supplied', async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.parsedActiveLayout).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Auth-not-ready deferral
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — auth deferral', () => {
  it('does not fetch when isAuthenticated=false', () => {
    const fetchMock = jest.fn();

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock as unknown as AuthenticatedFetch,
        isAuthenticated: false,
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(result.current.isLoading).toBe(true);
  });

  it('does not fetch when bffBaseUrl is empty', () => {
    const fetchMock = jest.fn();

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: '',
        authenticatedFetch: fetchMock as unknown as AuthenticatedFetch,
        isAuthenticated: true,
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(result.current.isLoading).toBe(true);
  });

  it("renders fallbackLayout immediately when bffBaseUrl='' AND fallback supplied", () => {
    const fetchMock = jest.fn();

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: '',
        authenticatedFetch: fetchMock as unknown as AuthenticatedFetch,
        isAuthenticated: true,
        fallbackLayout: FALLBACK_LAYOUT,
      })
    );

    expect(fetchMock).not.toHaveBeenCalled();
    expect(result.current.activeLayout).toEqual(FALLBACK_LAYOUT);
    expect(result.current.isLoading).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// 401 / 403 error paths
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — 401/403 paths', () => {
  it('warns + treats list as empty when list endpoint returns 403', async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockErrorResponse(403),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const warnSpy = jest.spyOn(console, 'warn').mockImplementation();

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    // Default endpoint succeeded, so active layout is set; list is empty
    expect(result.current.activeLayout).toEqual(FIXTURE_LAYOUT_USER);
    expect(warnSpy).toHaveBeenCalled();

    warnSpy.mockRestore();
  });

  it('warns + falls through cascade when default endpoint returns 401', async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER, FIXTURE_LAYOUT_SYSTEM]),
      '/workspace/layouts/default': mockErrorResponse(401),
    });

    const warnSpy = jest.spyOn(console, 'warn').mockImplementation();

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    // Default endpoint failed; cascade picked first default (FIXTURE_LAYOUT_USER)
    expect(result.current.activeLayout).toEqual(FIXTURE_LAYOUT_USER);
    expect(warnSpy).toHaveBeenCalled();

    warnSpy.mockRestore();
  });
});

// ---------------------------------------------------------------------------
// invalidateLayoutCache
// ---------------------------------------------------------------------------

describe('invalidateLayoutCache', () => {
  it('removes both cache keys for the namespace', () => {
    sessionStorage.setItem('sprk:workspace:activeLayout', JSON.stringify(FIXTURE_LAYOUT_USER));
    sessionStorage.setItem('sprk:workspace:layoutsList', JSON.stringify([FIXTURE_LAYOUT_USER]));

    invalidateLayoutCache('sprk:workspace');

    expect(sessionStorage.getItem('sprk:workspace:activeLayout')).toBeNull();
    expect(sessionStorage.getItem('sprk:workspace:layoutsList')).toBeNull();
  });

  it('does NOT touch other namespaces (LW vs SpaarkeAi isolation)', () => {
    sessionStorage.setItem('spaarke.ai.workspace.activeLayout', JSON.stringify(FIXTURE_LAYOUT_USER));
    sessionStorage.setItem('sprk:workspace:activeLayout', JSON.stringify(FIXTURE_LAYOUT_USER));

    invalidateLayoutCache('sprk:workspace');

    expect(sessionStorage.getItem('sprk:workspace:activeLayout')).toBeNull();
    expect(sessionStorage.getItem('spaarke.ai.workspace.activeLayout')).toBeTruthy();
  });

  it("uses default 'sprk:workspace' prefix when called without arg", () => {
    sessionStorage.setItem('sprk:workspace:activeLayout', JSON.stringify(FIXTURE_LAYOUT_USER));
    invalidateLayoutCache();
    expect(sessionStorage.getItem('sprk:workspace:activeLayout')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// cacheKeyPrefix namespace isolation
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — cacheKeyPrefix isolation', () => {
  it("writes LW prefix as 'sprk:workspace:activeLayout' (colon joiner)", async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        cacheKeyPrefix: 'sprk:workspace',
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(sessionStorage.getItem('sprk:workspace:activeLayout')).toBeTruthy();
    expect(sessionStorage.getItem('spaarke.ai.workspace.activeLayout')).toBeNull();
  });

  it("writes SpaarkeAi prefix as 'spaarke.ai.workspace.activeLayout' (dot joiner)", async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        cacheKeyPrefix: 'spaarke.ai.workspace',
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(sessionStorage.getItem('spaarke.ai.workspace.activeLayout')).toBeTruthy();
    expect(sessionStorage.getItem('sprk:workspace:activeLayout')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// embedded flag
// ---------------------------------------------------------------------------

describe('useWorkspaceLayouts — embedded flag', () => {
  it('does NOT write cache when embedded=true', async () => {
    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        embedded: true,
        cacheKeyPrefix: 'sprk:workspace',
      })
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(sessionStorage.getItem('sprk:workspace:activeLayout')).toBeNull();
    expect(sessionStorage.getItem('sprk:workspace:layoutsList')).toBeNull();
  });

  it('does NOT read cache when embedded=true', async () => {
    sessionStorage.setItem(
      'sprk:workspace:activeLayout',
      JSON.stringify(FIXTURE_LAYOUT_SYSTEM) // pretend cache has different layout
    );
    sessionStorage.setItem('sprk:workspace:layoutsList', JSON.stringify([FIXTURE_LAYOUT_SYSTEM]));

    const fetchMock = createMockFetch({
      '/workspace/layouts': mockOkResponse([FIXTURE_LAYOUT_USER]),
      '/workspace/layouts/default': mockOkResponse(FIXTURE_LAYOUT_USER),
    });

    const { result } = renderHook(() =>
      useWorkspaceLayouts({
        bffBaseUrl: 'https://bff.test',
        authenticatedFetch: fetchMock,
        isAuthenticated: true,
        embedded: true,
        cacheKeyPrefix: 'sprk:workspace',
      })
    );

    // embedded=true means cache is ignored — start fresh from fetch
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.activeLayout).toEqual(FIXTURE_LAYOUT_USER);
  });
});
