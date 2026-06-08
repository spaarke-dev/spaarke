/**
 * Unit tests for communicationLookupService.
 *
 * Covers smart-todo-decoupling-r3 FR-27 / task 070 acceptance:
 *   - 200 → returns { communicationId, subject } from BFF response
 *   - 404 → returns null (treated by caller as "not saved", triggers save flow)
 *   - 5xx / network → throws (caller surfaces error)
 *   - undefined / empty internetMessageId → returns null without a network call
 *   - URL-encoding handles RFC-5322 ids with special chars (`<`, `>`, `@`, `+`)
 */

import {
  findCommunicationByMessageId,
} from '../communicationLookupService';
import { apiClient, ApiClientError } from '@shared/services';

// Mock the apiClient module.
jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockGet = apiClient.get as jest.Mock;

describe('communicationLookupService', () => {
  beforeEach(() => {
    mockGet.mockReset();
  });

  describe('findCommunicationByMessageId — inert behavior', () => {
    it('returns null without a network call when internetMessageId is undefined', async () => {
      const result = await findCommunicationByMessageId(undefined);
      expect(result).toBeNull();
      expect(mockGet).not.toHaveBeenCalled();
    });

    it('returns null without a network call when internetMessageId is empty', async () => {
      const result = await findCommunicationByMessageId('');
      expect(result).toBeNull();
      expect(mockGet).not.toHaveBeenCalled();
    });

    it('returns null without a network call when internetMessageId is whitespace', async () => {
      const result = await findCommunicationByMessageId('   ');
      expect(result).toBeNull();
      expect(mockGet).not.toHaveBeenCalled();
    });
  });

  describe('findCommunicationByMessageId — successful lookup', () => {
    it('returns { communicationId, subject } when BFF returns 200', async () => {
      mockGet.mockResolvedValueOnce({
        communicationId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        subject: 'Q4 planning kickoff',
      });

      const result = await findCommunicationByMessageId('<abc@host.example.com>');

      expect(result).toEqual({
        communicationId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        subject: 'Q4 planning kickoff',
      });
      expect(mockGet).toHaveBeenCalledTimes(1);
    });

    it('URL-encodes the internetMessageId in the endpoint path', async () => {
      mockGet.mockResolvedValueOnce({ communicationId: 'id-1', subject: 'x' });

      // RFC-5322 message ids commonly contain `<`, `>`, `@`, and `+`.
      await findCommunicationByMessageId('<a+b@host.example.com>');

      expect(mockGet).toHaveBeenCalledWith(
        expect.stringContaining(encodeURIComponent('<a+b@host.example.com>')),
      );
    });

    it('tolerates a server response that omits the subject', async () => {
      mockGet.mockResolvedValueOnce({
        communicationId: 'id-1',
        // subject missing
      });

      const result = await findCommunicationByMessageId('<x@y>');
      expect(result).toEqual({ communicationId: 'id-1', subject: '' });
    });

    it('returns null when the server response omits communicationId', async () => {
      mockGet.mockResolvedValueOnce({}); // empty payload

      const result = await findCommunicationByMessageId('<x@y>');
      expect(result).toBeNull();
    });
  });

  describe('findCommunicationByMessageId — 404 = not saved', () => {
    it('returns null when the BFF returns 404 (graceful "not saved" path)', async () => {
      mockGet.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Not Found',
          status: 404,
          detail: 'No sprk_communication found for the given internetMessageId',
        }),
      );

      const result = await findCommunicationByMessageId('<unknown@host>');
      expect(result).toBeNull();
    });
  });

  describe('findCommunicationByMessageId — errors propagate', () => {
    it('re-throws when the BFF returns 5xx', async () => {
      mockGet.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Internal Server Error',
          status: 500,
          detail: 'oops',
        }),
      );

      await expect(findCommunicationByMessageId('<x@y>')).rejects.toThrow(/oops/);
    });

    it('re-throws when the BFF returns 401', async () => {
      mockGet.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Unauthorized',
          status: 401,
        }),
      );

      await expect(findCommunicationByMessageId('<x@y>')).rejects.toThrow();
    });

    it('re-throws non-ApiClientError exceptions', async () => {
      mockGet.mockRejectedValueOnce(new TypeError('network blew up'));
      await expect(findCommunicationByMessageId('<x@y>')).rejects.toThrow('network blew up');
    });
  });
});
