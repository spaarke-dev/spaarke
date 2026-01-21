/**
 * Office API Client Unit Tests
 *
 * Tests for the typed API client service.
 */

import {
  createOfficeApiClient,
  OfficeApiClientImpl,
  OfficeApiError,
  type IOfficeApiClient,
  type SaveRequest,
  type EntitySearchParams,
  type DocumentSearchParams,
  type ShareLinksRequest,
  type ShareAttachRequest,
  type QuickCreateMatterRequest,
} from '../index';
import type { INaaAuthService, TokenResult, NaaAuthState } from '../../auth';

// Mock auth service
function createMockAuthService(
  overrides?: Partial<INaaAuthService>
): INaaAuthService {
  const defaultState: NaaAuthState = {
    isAuthenticating: false,
    isAuthenticated: true,
    account: null,
    error: null,
  };

  return {
    initialize: jest.fn().mockResolvedValue(undefined),
    isNaaSupported: jest.fn().mockReturnValue(true),
    isInitialized: jest.fn().mockReturnValue(true),
    isAuthenticated: jest.fn().mockReturnValue(true),
    getAccount: jest.fn().mockReturnValue(null),
    signIn: jest.fn().mockResolvedValue({ accessToken: 'mock-token' }),
    signOut: jest.fn().mockResolvedValue(undefined),
    getAccessToken: jest.fn().mockResolvedValue({
      accessToken: 'mock-access-token',
      expiresOn: new Date(Date.now() + 3600000),
      scopes: ['api://test/user_impersonation'],
      fromCache: true,
    } as TokenResult),
    getAuthState: jest.fn().mockReturnValue(defaultState),
    onAuthStateChange: jest.fn().mockReturnValue(() => {}),
    ...overrides,
  };
}

// Mock fetch
const mockFetch = jest.fn();
global.fetch = mockFetch;

describe('OfficeApiClient', () => {
  let client: IOfficeApiClient & { configure: (config: Partial<{ baseUrl: string; timeout: number }>) => void };
  let mockAuthService: INaaAuthService;

  beforeEach(() => {
    jest.clearAllMocks();
    mockAuthService = createMockAuthService();
    client = createOfficeApiClient(
      { baseUrl: 'https://api.test.com', timeout: 5000 },
      mockAuthService
    );
  });

  describe('save', () => {
    const saveRequest: SaveRequest = {
      sourceType: 'OutlookEmail',
      associationType: 'Matter',
      associationId: '12345678-1234-1234-1234-123456789012',
      content: {
        emailId: 'AAMkAGI2...',
        includeBody: true,
        attachmentIds: ['ATT001', 'ATT002'],
      },
      processing: {
        profileSummary: true,
        ragIndex: true,
      },
    };

    it('should send POST request to /office/save with auth header', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              jobId: 'job-123',
              documentId: 'doc-123',
              statusUrl: '/office/jobs/job-123',
              streamUrl: '/office/jobs/job-123/stream',
              status: 'Queued',
              duplicate: false,
              correlationId: 'corr-123',
            })
          ),
      });

      const response = await client.save(saveRequest);

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const [url, config] = mockFetch.mock.calls[0];
      expect(url).toBe('https://api.test.com/office/save');
      expect(config.method).toBe('POST');
      expect(config.headers.Authorization).toBe('Bearer mock-access-token');
      expect(config.headers['Content-Type']).toBe('application/json');
      expect(config.headers['X-Correlation-Id']).toBeDefined();
      expect(config.headers['X-Idempotency-Key']).toBeDefined();

      expect(response.jobId).toBe('job-123');
      expect(response.status).toBe('Queued');
      expect(response.duplicate).toBe(false);
    });

    it('should handle duplicate detection response (200 OK)', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              jobId: 'existing-job',
              documentId: 'existing-doc',
              statusUrl: '/office/jobs/existing-job',
              streamUrl: '/office/jobs/existing-job/stream',
              status: 'Completed',
              duplicate: true,
              message: 'This item was previously saved',
              correlationId: 'corr-456',
            })
          ),
      });

      const response = await client.save(saveRequest);

      expect(response.duplicate).toBe(true);
      expect(response.message).toBe('This item was previously saved');
    });

    it('should use provided idempotency key', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              jobId: 'job-123',
              documentId: 'doc-123',
              statusUrl: '/office/jobs/job-123',
              streamUrl: '/office/jobs/job-123/stream',
              status: 'Queued',
              duplicate: false,
              correlationId: 'corr-123',
            })
          ),
      });

      await client.save(saveRequest, { idempotencyKey: 'my-custom-key' });

      const [, config] = mockFetch.mock.calls[0];
      expect(config.headers['X-Idempotency-Key']).toBe('my-custom-key');
    });
  });

  describe('getJobStatus', () => {
    it('should send GET request to /office/jobs/{jobId}', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              jobId: 'job-123',
              status: 'Running',
              stages: [
                { name: 'RecordsCreated', status: 'Completed' },
                { name: 'FileUploaded', status: 'Running' },
                { name: 'ProfileSummary', status: 'Pending' },
              ],
              documentId: 'doc-123',
            })
          ),
      });

      const response = await client.getJobStatus('job-123');

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const [url, config] = mockFetch.mock.calls[0];
      expect(url).toBe('https://api.test.com/office/jobs/job-123');
      expect(config.method).toBe('GET');
      expect(config.headers.Authorization).toBe('Bearer mock-access-token');

      expect(response.jobId).toBe('job-123');
      expect(response.status).toBe('Running');
      expect(response.stages).toHaveLength(3);
    });

    it('should encode special characters in jobId', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(JSON.stringify({ jobId: 'job/with/slashes', status: 'Completed', stages: [] })),
      });

      await client.getJobStatus('job/with/slashes');

      const [url] = mockFetch.mock.calls[0];
      expect(url).toBe('https://api.test.com/office/jobs/job%2Fwith%2Fslashes');
    });
  });

  describe('searchEntities', () => {
    it('should send GET request with query parameters', async () => {
      const params: EntitySearchParams = {
        q: 'acme',
        type: ['Matter', 'Account'],
        limit: 10,
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              results: [
                {
                  id: 'entity-1',
                  entityType: 'Matter',
                  logicalName: 'sprk_matter',
                  name: 'Acme Case',
                },
              ],
              totalCount: 1,
              hasMore: false,
            })
          ),
      });

      const response = await client.searchEntities(params);

      const [url] = mockFetch.mock.calls[0];
      expect(url).toContain('/office/search/entities');
      expect(url).toContain('q=acme');
      expect(url).toContain('type=Matter%2CAccount');
      expect(url).toContain('limit=10');

      expect(response.results).toHaveLength(1);
      expect(response.results[0].name).toBe('Acme Case');
    });

    it('should handle string type parameter', async () => {
      const params: EntitySearchParams = {
        q: 'test',
        type: 'Matter',
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(JSON.stringify({ results: [], totalCount: 0, hasMore: false })),
      });

      await client.searchEntities(params);

      const [url] = mockFetch.mock.calls[0];
      expect(url).toContain('type=Matter');
    });
  });

  describe('searchDocuments', () => {
    it('should send GET request with query parameters', async () => {
      const params: DocumentSearchParams = {
        q: 'contract',
        contentTypes: ['application/pdf', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'],
        associationType: 'Matter',
        limit: 20,
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              results: [
                {
                  id: 'doc-1',
                  name: 'Contract Draft',
                  fileName: 'contract.pdf',
                  contentType: 'application/pdf',
                  size: 123456,
                  createdOn: '2026-01-20T10:00:00Z',
                  modifiedOn: '2026-01-20T10:00:00Z',
                },
              ],
              totalCount: 1,
              hasMore: false,
            })
          ),
      });

      const response = await client.searchDocuments(params);

      const [url] = mockFetch.mock.calls[0];
      expect(url).toContain('/office/search/documents');
      expect(url).toContain('q=contract');
      expect(url).toContain('associationType=Matter');
      expect(url).toContain('limit=20');

      expect(response.results).toHaveLength(1);
      expect(response.results[0].fileName).toBe('contract.pdf');
    });
  });

  describe('quickCreate', () => {
    it('should send POST request to /office/quickcreate/{entityType}', async () => {
      const data: QuickCreateMatterRequest = {
        name: 'New Matter',
        description: 'Test matter',
        clientId: 'client-123',
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 201,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              id: 'matter-123',
              entityType: 'Matter',
              logicalName: 'sprk_matter',
              name: 'New Matter',
              url: 'https://org.crm.dynamics.com/main.aspx?...',
            })
          ),
      });

      const response = await client.quickCreate('matter', data);

      const [url, config] = mockFetch.mock.calls[0];
      expect(url).toBe('https://api.test.com/office/quickcreate/matter');
      expect(config.method).toBe('POST');
      expect(JSON.parse(config.body)).toEqual(data);

      expect(response.id).toBe('matter-123');
      expect(response.entityType).toBe('Matter');
    });
  });

  describe('shareLinks', () => {
    it('should send POST request to /office/share/links', async () => {
      const request: ShareLinksRequest = {
        documentIds: ['doc-1', 'doc-2'],
        recipients: ['user@example.com'],
        grantAccess: true,
        role: 'ViewOnly',
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              links: [
                { documentId: 'doc-1', url: 'https://share.link/1', title: 'Doc 1' },
                { documentId: 'doc-2', url: 'https://share.link/2', title: 'Doc 2' },
              ],
              invitations: [],
            })
          ),
      });

      const response = await client.shareLinks(request);

      const [url, config] = mockFetch.mock.calls[0];
      expect(url).toBe('https://api.test.com/office/share/links');
      expect(config.method).toBe('POST');

      expect(response.links).toHaveLength(2);
    });
  });

  describe('shareAttach', () => {
    it('should send POST request to /office/share/attach', async () => {
      const request: ShareAttachRequest = {
        documentIds: ['doc-1'],
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              attachments: [
                {
                  documentId: 'doc-1',
                  filename: 'file.pdf',
                  contentType: 'application/pdf',
                  size: 12345,
                  downloadUrl: 'https://download.url/token',
                  urlExpiry: '2026-01-20T11:00:00Z',
                },
              ],
            })
          ),
      });

      const response = await client.shareAttach(request);

      expect(response.attachments).toHaveLength(1);
      expect(response.attachments[0].downloadUrl).toContain('https://download.url');
    });
  });

  describe('getRecent', () => {
    it('should send GET request to /office/recent', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(
            JSON.stringify({
              recentAssociations: [
                { id: 'matter-1', entityType: 'Matter', name: 'Recent Matter', lastUsed: '2026-01-20T10:00:00Z' },
              ],
              recentDocuments: [],
              favorites: [],
            })
          ),
      });

      const response = await client.getRecent();

      const [url] = mockFetch.mock.calls[0];
      expect(url).toBe('https://api.test.com/office/recent');

      expect(response.recentAssociations).toHaveLength(1);
    });
  });

  describe('error handling', () => {
    it('should throw OfficeApiError for ProblemDetails response', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 400,
        statusText: 'Bad Request',
        headers: new Headers({ 'Content-Type': 'application/problem+json' }),
        json: () =>
          Promise.resolve({
            type: 'https://spaarke.com/errors/office/validation-error',
            title: 'Validation Error',
            status: 400,
            detail: 'Association target is required',
            errorCode: 'OFFICE_003',
            correlationId: 'corr-789',
          }),
      });

      await expect(client.getRecent()).rejects.toThrow(OfficeApiError);

      try {
        await client.getRecent();
      } catch (error) {
        if (error instanceof OfficeApiError) {
          expect(error.errorCode).toBe('OFFICE_003');
          expect(error.status).toBe(400);
          expect(error.correlationId).toBe('corr-789');
          expect(error.getUserMessage()).toContain('association');
        }
      }
    });

    it('should handle rate limiting (429)', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 429,
        statusText: 'Too Many Requests',
        headers: new Headers({
          'Content-Type': 'application/json',
          'Retry-After': '30',
        }),
        json: () =>
          Promise.resolve({
            type: '/rate-limited',
            title: 'Too Many Requests',
            status: 429,
            detail: 'Rate limit exceeded',
          }),
      });

      try {
        await client.getRecent();
        fail('Should have thrown');
      } catch (error) {
        expect(error).toBeInstanceOf(OfficeApiError);
        const apiError = error as OfficeApiError;
        expect(apiError.isRateLimited).toBe(true);
        expect(apiError.retryAfterSeconds).toBe(30);
      }
    });

    it('should handle authentication errors (401)', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 401,
        statusText: 'Unauthorized',
        headers: new Headers({ 'Content-Type': 'application/json' }),
        json: () =>
          Promise.resolve({
            type: 'https://spaarke.com/errors/unauthorized',
            title: 'Unauthorized',
            status: 401,
          }),
      });

      try {
        await client.getRecent();
        fail('Should have thrown');
      } catch (error) {
        expect(error).toBeInstanceOf(OfficeApiError);
        const apiError = error as OfficeApiError;
        expect(apiError.isAuthError).toBe(true);
        expect(apiError.status).toBe(401);
      }
    });

    it('should handle network errors', async () => {
      mockFetch.mockRejectedValueOnce(new TypeError('Failed to fetch'));

      try {
        await client.getRecent();
        fail('Should have thrown');
      } catch (error) {
        expect(error).toBeInstanceOf(OfficeApiError);
        const apiError = error as OfficeApiError;
        expect(apiError.isNetworkError).toBe(true);
      }
    });

    it('should throw auth error when auth service not initialized', async () => {
      const uninitializedAuthService = createMockAuthService({
        isInitialized: jest.fn().mockReturnValue(false),
      });

      const uninitializedClient = createOfficeApiClient(
        { baseUrl: 'https://api.test.com' },
        uninitializedAuthService
      );

      await expect(uninitializedClient.getRecent()).rejects.toThrow(OfficeApiError);

      try {
        await uninitializedClient.getRecent();
      } catch (error) {
        if (error instanceof OfficeApiError) {
          expect(error.isAuthError).toBe(true);
        }
      }
    });
  });

  describe('cancellation', () => {
    it('should support request cancellation via AbortSignal', async () => {
      const controller = new AbortController();

      // Delay the fetch response so we can abort
      mockFetch.mockImplementationOnce(() => {
        return new Promise((resolve, reject) => {
          controller.signal.addEventListener('abort', () => {
            reject(new DOMException('Aborted', 'AbortError'));
          });
        });
      });

      const promise = client.getRecent({ signal: controller.signal });

      // Abort immediately
      controller.abort();

      await expect(promise).rejects.toThrow(OfficeApiError);
    });
  });

  describe('configuration', () => {
    it('should allow reconfiguration after construction', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(JSON.stringify({ recentAssociations: [], recentDocuments: [], favorites: [] })),
      });

      client.configure({ baseUrl: 'https://new-api.test.com' });

      await client.getRecent();

      const [url] = mockFetch.mock.calls[0];
      expect(url).toBe('https://new-api.test.com/office/recent');
    });

    it('should remove trailing slash from base URL', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers({ 'Content-Type': 'application/json' }),
        text: () =>
          Promise.resolve(JSON.stringify({ recentAssociations: [], recentDocuments: [], favorites: [] })),
      });

      client.configure({ baseUrl: 'https://api.test.com/' });

      await client.getRecent();

      const [url] = mockFetch.mock.calls[0];
      expect(url).toBe('https://api.test.com/office/recent');
    });
  });
});
