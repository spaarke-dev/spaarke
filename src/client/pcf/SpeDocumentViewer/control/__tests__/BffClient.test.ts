/**
 * SpeDocumentViewer BffClient — URL construction goes through buildBffApiUrl
 * (constraints/auth.md "BFF Base URL Convention"), so a host-only base URL with or
 * without a trailing slash yields exactly one `/api/` segment.
 */

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => {
  // The real helper (library source; its dist is ESM, which Jest's CJS runtime cannot load),
  // so the test exercises the canonical rule rather than a copy of it.
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { buildBffApiUrl } = require('../../../../shared/Spaarke.Auth/src/buildBffApiUrl');
  class ApiError extends Error {
    constructor(
      public status: number,
      public problemDetails?: unknown
    ) {
      super(`HTTP ${status}`);
    }
  }
  return {
    authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
    buildBffApiUrl,
    ApiError,
  };
});

import { BffClient } from '../BffClient';

const DOC_ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

beforeEach(() => {
  mockAuthenticatedFetch.mockReset();
  mockAuthenticatedFetch.mockResolvedValue({ json: async () => ({ documentInfo: { name: 'a.docx' } }) });
});

// The host-only base comes from getApiBaseUrl(), which strips the env var's `/api` suffix.
it.each(['https://bff.example.net', 'https://bff.example.net/'])(
  'calls /api/documents/{id}/view-url exactly once-prefixed for base %p',
  async base => {
    await new BffClient(base).getViewUrl(DOC_ID, 'corr-1');

    expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(1);
    expect(mockAuthenticatedFetch.mock.calls[0][0]).toBe(`https://bff.example.net/api/documents/${DOC_ID}/view-url`);
  }
);

it('builds the v1 download URL with a single /api segment', () => {
  expect(new BffClient('https://bff.example.net/').getDownloadUrl(DOC_ID)).toBe(
    `https://bff.example.net/api/v1/documents/${DOC_ID}/download`
  );
});

it('refuses to build a URL from an empty base (no relative-URL fallback)', () => {
  expect(() => new BffClient('').getDownloadUrl(DOC_ID)).toThrow(/baseUrl is empty/);
});
