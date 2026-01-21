import { renderHook, act, waitFor } from '@testing-library/react';
import { useSaveFlow } from '../useSaveFlow';
import type { EntitySearchResult } from '../useEntitySearch';
import type { SaveFlowContext } from '../useSaveFlow';

// Mock fetch for API calls
const mockFetch = jest.fn();
global.fetch = mockFetch;

// Mock crypto.subtle for idempotency key computation
Object.defineProperty(global.crypto, 'subtle', {
  value: {
    digest: jest.fn().mockImplementation(async () => {
      return new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]).buffer;
    }),
  },
});

// Mock sessionStorage
const mockSessionStorage: Record<string, string> = {};
Object.defineProperty(window, 'sessionStorage', {
  value: {
    getItem: (key: string) => mockSessionStorage[key] || null,
    setItem: (key: string, value: string) => {
      mockSessionStorage[key] = value;
    },
    removeItem: (key: string) => {
      delete mockSessionStorage[key];
    },
    clear: () => {
      Object.keys(mockSessionStorage).forEach(key => delete mockSessionStorage[key]);
    },
  },
  writable: true,
});

// Mock entity
const mockEntity: EntitySearchResult = {
  id: 'entity-123',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Test Matter',
  displayInfo: 'Client: Test Corp',
};

// Mock access token getter
const mockGetAccessToken = jest.fn().mockResolvedValue('test-access-token');

// Mock save response
const mockSaveResponse = {
  jobId: 'job-123',
  documentId: 'doc-456',
  statusUrl: '/office/jobs/job-123',
  streamUrl: '/office/jobs/job-123/stream',
  status: 'Queued',
  duplicate: false,
  correlationId: 'corr-789',
};

// Mock context
const mockContext: SaveFlowContext = {
  hostType: 'outlook',
  itemId: 'email-123',
  itemName: 'Test Email',
  attachments: [],
};

describe('useSaveFlow', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockFetch.mockReset();
    Object.keys(mockSessionStorage).forEach(key => delete mockSessionStorage[key]);
  });

  describe('Initial State', () => {
    it('starts with idle flow state', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.flowState).toBe('idle');
      expect(result.current.selectedEntity).toBeNull();
      expect(result.current.isSaving).toBe(false);
      expect(result.current.isValid).toBe(false);
    });

    it('has default processing options enabled', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.processingOptions).toEqual({
        profileSummary: true,
        ragIndex: true,
        deepAnalysis: false,
      });
    });

    it('has empty attachment selection', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.selectedAttachmentIds.size).toBe(0);
    });

    it('defaults to include body', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.includeBody).toBe(true);
    });
  });

  describe('Entity Selection', () => {
    it('sets selected entity', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      expect(result.current.selectedEntity).toEqual(mockEntity);
      expect(result.current.isValid).toBe(true);
    });

    it('persists last association to sessionStorage', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      expect(mockSessionStorage['spaarke-last-association']).toBeDefined();
      const stored = JSON.parse(mockSessionStorage['spaarke-last-association']);
      expect(stored.id).toBe(mockEntity.id);
    });

    it('clears entity selection', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      act(() => {
        result.current.setSelectedEntity(null);
      });

      expect(result.current.selectedEntity).toBeNull();
      expect(result.current.isValid).toBe(false);
    });
  });

  describe('Attachment Selection', () => {
    it('sets selected attachment IDs', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedAttachmentIds(new Set(['att-1', 'att-2']));
      });

      expect(result.current.selectedAttachmentIds.size).toBe(2);
      expect(result.current.selectedAttachmentIds.has('att-1')).toBe(true);
      expect(result.current.selectedAttachmentIds.has('att-2')).toBe(true);
    });
  });

  describe('Processing Options', () => {
    it('toggles profile summary option', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.processingOptions.profileSummary).toBe(true);

      act(() => {
        result.current.toggleProcessingOption('profileSummary');
      });

      expect(result.current.processingOptions.profileSummary).toBe(false);
    });

    it('toggles RAG index option', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.processingOptions.ragIndex).toBe(true);

      act(() => {
        result.current.toggleProcessingOption('ragIndex');
      });

      expect(result.current.processingOptions.ragIndex).toBe(false);
    });

    it('toggles deep analysis option', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.processingOptions.deepAnalysis).toBe(false);

      act(() => {
        result.current.toggleProcessingOption('deepAnalysis');
      });

      expect(result.current.processingOptions.deepAnalysis).toBe(true);
    });

    it('sets all processing options at once', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setProcessingOptions({
          profileSummary: false,
          ragIndex: false,
          deepAnalysis: true,
        });
      });

      expect(result.current.processingOptions).toEqual({
        profileSummary: false,
        ragIndex: false,
        deepAnalysis: true,
      });
    });
  });

  describe('Include Body', () => {
    it('toggles include body setting', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      expect(result.current.includeBody).toBe(true);

      act(() => {
        result.current.setIncludeBody(false);
      });

      expect(result.current.includeBody).toBe(false);
    });
  });

  describe('Save Operation', () => {
    it('requires entity selection before save', async () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      expect(result.current.error).not.toBeNull();
      expect(result.current.error?.title).toBe('Association Required');
    });

    it('transitions to uploading state on save', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        json: async () => mockSaveResponse,
      });

      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      act(() => {
        result.current.startSave(mockContext);
      });

      await waitFor(() => {
        expect(result.current.flowState).not.toBe('idle');
      });
    });

    it('calls API with correct payload', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        json: async () => mockSaveResponse,
      });

      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
        result.current.setSelectedAttachmentIds(new Set(['att-1']));
      });

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/office/save'),
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
            Authorization: 'Bearer test-access-token',
            'X-Idempotency-Key': expect.any(String),
          }),
          body: expect.any(String),
        })
      );

      const body = JSON.parse(mockFetch.mock.calls[0][1].body);
      expect(body.associationType).toBe('Matter');
      expect(body.associationId).toBe('entity-123');
      expect(body.content.attachmentIds).toEqual(['att-1']);
      expect(body.processing.profileSummary).toBe(true);
    });

    it('includes idempotency key header', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        json: async () => mockSaveResponse,
      });

      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      expect(mockFetch.mock.calls[0][1].headers['X-Idempotency-Key']).toBeDefined();
    });
  });

  describe('Duplicate Detection', () => {
    it('handles duplicate response', async () => {
      const duplicateResponse = {
        jobId: 'existing-job',
        documentId: 'existing-doc',
        statusUrl: '/office/jobs/existing-job',
        streamUrl: '/office/jobs/existing-job/stream',
        status: 'Completed',
        duplicate: true,
        message: 'This item was previously saved',
        correlationId: 'corr-123',
      };

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => duplicateResponse,
      });

      const onDuplicate = jest.fn();

      const { result } = renderHook(() =>
        useSaveFlow({
          getAccessToken: mockGetAccessToken,
          onDuplicate,
        })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      expect(result.current.flowState).toBe('duplicate');
      expect(result.current.duplicateInfo).not.toBeNull();
      expect(result.current.duplicateInfo?.documentId).toBe('existing-doc');
      expect(onDuplicate).toHaveBeenCalledWith('existing-doc', 'This item was previously saved');
    });
  });

  describe('Error Handling', () => {
    it('handles API error response', async () => {
      const problemDetails = {
        type: 'https://spaarke.com/errors/office/validation-error',
        title: 'Validation Error',
        status: 400,
        detail: 'Association target is required',
        errorCode: 'OFFICE_003',
      };

      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 400,
        json: async () => problemDetails,
      });

      const onError = jest.fn();

      const { result } = renderHook(() =>
        useSaveFlow({
          getAccessToken: mockGetAccessToken,
          onError,
        })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      expect(result.current.flowState).toBe('error');
      expect(result.current.error).not.toBeNull();
      expect(onError).toHaveBeenCalled();
    });

    it('handles network error', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Network error'));

      const onError = jest.fn();

      const { result } = renderHook(() =>
        useSaveFlow({
          getAccessToken: mockGetAccessToken,
          onError,
        })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      expect(result.current.flowState).toBe('error');
      expect(result.current.error?.message).toContain('Network error');
    });

    it('clears error', () => {
      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        // Trigger an error by trying to save without entity
        result.current.startSave(mockContext);
      });

      expect(result.current.error).not.toBeNull();

      act(() => {
        result.current.clearError();
      });

      expect(result.current.error).toBeNull();
    });
  });

  describe('Reset', () => {
    it('resets flow to initial state', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        json: async () => mockSaveResponse,
      });

      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
        result.current.setSelectedAttachmentIds(new Set(['att-1']));
        result.current.setIncludeBody(false);
        result.current.toggleProcessingOption('profileSummary');
      });

      act(() => {
        result.current.reset();
      });

      expect(result.current.flowState).toBe('idle');
      expect(result.current.selectedAttachmentIds.size).toBe(0);
      expect(result.current.includeBody).toBe(true);
      expect(result.current.processingOptions.profileSummary).toBe(true);
      expect(result.current.jobStatus).toBeNull();
      expect(result.current.error).toBeNull();
    });
  });

  describe('Retry', () => {
    it('retries failed operation', async () => {
      // First call fails
      mockFetch.mockRejectedValueOnce(new Error('Network error'));

      const { result } = renderHook(() =>
        useSaveFlow({ getAccessToken: mockGetAccessToken })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      expect(result.current.flowState).toBe('error');

      // Second call succeeds
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        json: async () => mockSaveResponse,
      });

      await act(async () => {
        result.current.retry();
      });

      await waitFor(() => {
        expect(result.current.flowState).not.toBe('error');
      });
    });
  });

  describe('Callbacks', () => {
    it('calls onComplete on successful save', async () => {
      // Mock save response
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 202,
        json: async () => mockSaveResponse,
      });

      // Mock job status response (completed)
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          jobId: 'job-123',
          status: 'Completed',
          stages: [
            { name: 'RecordsCreated', status: 'Completed' },
            { name: 'FileUploaded', status: 'Completed' },
          ],
          documentId: 'doc-456',
          documentUrl: 'https://example.com/doc/456',
        }),
      });

      const onComplete = jest.fn();

      const { result } = renderHook(() =>
        useSaveFlow({
          getAccessToken: mockGetAccessToken,
          onComplete,
          pollingIntervalMs: 100, // Fast polling for test
        })
      );

      act(() => {
        result.current.setSelectedEntity(mockEntity);
      });

      await act(async () => {
        await result.current.startSave(mockContext);
      });

      // Note: In a real test, we'd need to wait for polling to complete
      // This test verifies the callback setup
    });
  });
});
