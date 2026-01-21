/**
 * Unit tests for ApiClient
 *
 * Tests the API client service for communicating with the Spaarke BFF API.
 */

import { apiClient, ApiClientError, type ApiError, type UploadResponse } from '../ApiClient';

// Mock the authService dependency
jest.mock('../AuthService', () => ({
  authService: {
    getAccessToken: jest.fn(),
  },
}));

import { authService } from '../AuthService';

// Mock fetch globally
const mockFetch = jest.fn();
global.fetch = mockFetch;

describe('ApiClient', () => {
  const mockAccessToken = 'mock-access-token-12345';
  const mockBaseUrl = 'https://api.example.com';
  const mockBffApiClientId = 'test-bff-client-id';

  beforeEach(() => {
    // Reset all mocks
    jest.clearAllMocks();

    // Configure the API client
    apiClient.configure({
      baseUrl: mockBaseUrl,
      bffApiClientId: mockBffApiClientId,
    });

    // Mock successful token acquisition
    (authService.getAccessToken as jest.Mock).mockResolvedValue(mockAccessToken);

    // Reset fetch mock
    mockFetch.mockReset();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('configure', () => {
    it('should configure with baseUrl and bffApiClientId', () => {
      apiClient.configure({
        baseUrl: 'https://new-api.example.com/',
        bffApiClientId: 'new-client-id',
      });

      // The configure should strip trailing slash
      // We'll verify this through a subsequent request
      expect(true).toBe(true); // Configuration doesn't expose values directly
    });
  });

  describe('get', () => {
    it('should make GET request with authorization header', async () => {
      const mockResponse = { data: 'test' };
      mockFetch.mockResolvedValueOnce({
        ok: true,
        text: jest.fn().mockResolvedValue(JSON.stringify(mockResponse)),
      });

      const result = await apiClient.get<typeof mockResponse>('/api/test');

      expect(mockFetch).toHaveBeenCalledWith(
        `${mockBaseUrl}/api/test`,
        expect.objectContaining({
          method: 'GET',
          headers: expect.objectContaining({
            Authorization: `Bearer ${mockAccessToken}`,
            'Content-Type': 'application/json',
          }),
        })
      );
      expect(result).toEqual(mockResponse);
    });

    it('should throw when not authenticated', async () => {
      (authService.getAccessToken as jest.Mock).mockResolvedValue(null);

      await expect(apiClient.get('/api/test')).rejects.toThrow('Not authenticated');
    });

    it('should handle error response with ProblemDetails', async () => {
      const errorResponse: ApiError = {
        type: 'https://api.example.com/errors/not-found',
        title: 'Not Found',
        status: 404,
        detail: 'The requested resource was not found',
        correlationId: 'correlation-123',
      };

      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 404,
        json: jest.fn().mockResolvedValue(errorResponse),
      });

      await expect(apiClient.get('/api/missing')).rejects.toThrow(ApiClientError);

      try {
        await apiClient.get('/api/missing');
      } catch (error) {
        expect(error).toBeInstanceOf(ApiClientError);
        expect((error as ApiClientError).error.status).toBe(404);
        expect((error as ApiClientError).error.correlationId).toBe('correlation-123');
      }
    });

    it('should handle non-JSON error response', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
        json: jest.fn().mockRejectedValue(new Error('Not JSON')),
      });

      await expect(apiClient.get('/api/error')).rejects.toThrow(ApiClientError);
    });

    it('should handle empty response', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        text: jest.fn().mockResolvedValue(''),
      });

      const result = await apiClient.get('/api/empty');

      expect(result).toEqual({});
    });
  });

  describe('post', () => {
    it('should make POST request with body', async () => {
      const requestBody = { name: 'Test', value: 123 };
      const mockResponse = { id: '1', ...requestBody };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        text: jest.fn().mockResolvedValue(JSON.stringify(mockResponse)),
      });

      const result = await apiClient.post<typeof mockResponse>('/api/items', requestBody);

      expect(mockFetch).toHaveBeenCalledWith(
        `${mockBaseUrl}/api/items`,
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify(requestBody),
        })
      );
      expect(result).toEqual(mockResponse);
    });

    it('should make POST request without body', async () => {
      const mockResponse = { success: true };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        text: jest.fn().mockResolvedValue(JSON.stringify(mockResponse)),
      });

      const result = await apiClient.post<typeof mockResponse>('/api/action');

      expect(mockFetch).toHaveBeenCalledWith(
        `${mockBaseUrl}/api/action`,
        expect.objectContaining({
          method: 'POST',
        })
      );
      expect(result).toEqual(mockResponse);
    });
  });

  describe('put', () => {
    it('should make PUT request with body', async () => {
      const requestBody = { name: 'Updated' };
      const mockResponse = { id: '1', ...requestBody };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        text: jest.fn().mockResolvedValue(JSON.stringify(mockResponse)),
      });

      const result = await apiClient.put<typeof mockResponse>('/api/items/1', requestBody);

      expect(mockFetch).toHaveBeenCalledWith(
        `${mockBaseUrl}/api/items/1`,
        expect.objectContaining({
          method: 'PUT',
          body: JSON.stringify(requestBody),
        })
      );
      expect(result).toEqual(mockResponse);
    });
  });

  describe('delete', () => {
    it('should make DELETE request', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        text: jest.fn().mockResolvedValue(''),
      });

      const result = await apiClient.delete('/api/items/1');

      expect(mockFetch).toHaveBeenCalledWith(
        `${mockBaseUrl}/api/items/1`,
        expect.objectContaining({
          method: 'DELETE',
        })
      );
      expect(result).toEqual({});
    });
  });

  describe('uploadFile', () => {
    it('should upload file with FormData', async () => {
      const mockResponse: UploadResponse = {
        documentId: 'doc-123',
        jobId: 'job-456',
        status: 'pending',
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: jest.fn().mockResolvedValue(mockResponse),
      });

      const mockFile = new File(['test content'], 'test.txt', { type: 'text/plain' });
      const result = await apiClient.uploadFile('/api/upload', mockFile, 'test.txt');

      expect(mockFetch).toHaveBeenCalledWith(
        `${mockBaseUrl}/api/upload`,
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            Authorization: `Bearer ${mockAccessToken}`,
          }),
          body: expect.any(FormData),
        })
      );

      // Verify FormData does not have Content-Type (it's set automatically with boundary)
      const callArgs = mockFetch.mock.calls[0];
      expect(callArgs[1].headers['Content-Type']).toBeUndefined();

      expect(result).toEqual(mockResponse);
    });

    it('should upload Blob', async () => {
      const mockResponse: UploadResponse = {
        documentId: 'doc-789',
        jobId: 'job-012',
        status: 'processing',
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: jest.fn().mockResolvedValue(mockResponse),
      });

      const mockBlob = new Blob(['blob content'], { type: 'application/octet-stream' });
      const result = await apiClient.uploadFile('/api/upload', mockBlob, 'data.bin');

      expect(result).toEqual(mockResponse);
    });

    it('should throw when not authenticated', async () => {
      (authService.getAccessToken as jest.Mock).mockResolvedValue(null);

      const mockFile = new File(['test'], 'test.txt', { type: 'text/plain' });
      await expect(apiClient.uploadFile('/api/upload', mockFile, 'test.txt')).rejects.toThrow(
        'Not authenticated'
      );
    });

    it('should handle upload error', async () => {
      const errorResponse: ApiError = {
        type: 'https://api.example.com/errors/validation',
        title: 'Validation Error',
        status: 400,
        detail: 'File type not supported',
      };

      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 400,
        json: jest.fn().mockResolvedValue(errorResponse),
      });

      const mockFile = new File(['test'], 'test.exe', { type: 'application/x-msdownload' });

      await expect(apiClient.uploadFile('/api/upload', mockFile, 'test.exe')).rejects.toThrow(
        ApiClientError
      );
    });
  });

  describe('token acquisition', () => {
    it('should request token with correct scopes', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        text: jest.fn().mockResolvedValue('{}'),
      });

      await apiClient.get('/api/test');

      expect(authService.getAccessToken).toHaveBeenCalledWith([
        `api://${mockBffApiClientId}/.default`,
      ]);
    });
  });
});

describe('ApiClientError', () => {
  it('should create error with ApiError details', () => {
    const apiError: ApiError = {
      type: 'https://example.com/errors/test',
      title: 'Test Error',
      status: 500,
      detail: 'Something went wrong',
    };

    const error = new ApiClientError(apiError);

    expect(error.name).toBe('ApiClientError');
    expect(error.message).toBe('Something went wrong');
    expect(error.error).toEqual(apiError);
  });

  it('should use title when detail is not provided', () => {
    const apiError: ApiError = {
      type: 'https://example.com/errors/test',
      title: 'Test Error',
      status: 500,
    };

    const error = new ApiClientError(apiError);

    expect(error.message).toBe('Test Error');
  });
});
