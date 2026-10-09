/** Jest mock for `@spaarke/auth` (no real MSAL in unit tests). */

export interface IAuthConfig {
  clientId: string;
  tenantId?: string;
  redirectUri?: string;
  bffApiScope?: string;
  bffBaseUrl?: string;
  proactiveRefresh?: boolean;
}

/** Mirrors the library's `isValidTenant`: a GUID or a dotted domain; never an alias or "undefined". */
export const isValidTenant = (value: unknown): value is string => {
  if (typeof value !== 'string') return false;
  const v = value.trim();
  if (['common', 'organizations', 'consumers', 'undefined', 'null'].includes(v.toLowerCase())) return false;
  return (
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) ||
    /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i.test(v)
  );
};

export const initAuth = jest.fn(async (_config: IAuthConfig): Promise<void> => undefined);

/**
 * Mock `authenticatedFetch`. Default: 200 OK with a preview-url + open-links
 * shaped JSON body so both AttachmentApiService calls resolve. Tests may
 * override via `mockAuthenticatedFetch`.
 */
export const authenticatedFetch = jest.fn(async (url: string): Promise<Response> => {
  const isOpenLinks = url.includes('/open-links');
  const body = isOpenLinks
    ? { desktopUrl: 'ms-word:ofe|u|https://spe/file.docx', webUrl: 'https://spe/file', mimeType: 'x', fileName: 'file' }
    : {
        previewUrl: 'https://preview/embed',
        documentInfo: { name: 'file', mimeType: 'x' },
        checkoutStatus: null,
        correlationId: 'c1',
      };
  return {
    ok: true,
    status: 200,
    json: async () => body,
  } as unknown as Response;
});
