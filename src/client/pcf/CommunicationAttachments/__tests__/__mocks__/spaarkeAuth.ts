/** Jest mock for `@spaarke/auth` (no real MSAL in unit tests). */

export interface IAuthConfig {
  clientId: string;
  redirectUri?: string;
  bffApiScope?: string;
  bffBaseUrl?: string;
  proactiveRefresh?: boolean;
}

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
