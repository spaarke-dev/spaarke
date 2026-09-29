import { SdapApiClient } from '../SdapApiClient';

describe('SdapApiClient', () => {
  describe('constructor', () => {
    it('should accept valid config', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client).toBeDefined();
    });

    it('should throw on missing baseUrl', () => {
      expect(() => {
        new SdapApiClient({ baseUrl: '' });
      }).toThrow('baseUrl is required');
    });

    it('should throw on invalid baseUrl', () => {
      expect(() => {
        new SdapApiClient({ baseUrl: 'not-a-url' });
      }).toThrow('baseUrl must be a valid URL');
    });

    it('should throw on negative timeout', () => {
      expect(() => {
        new SdapApiClient({
          baseUrl: 'https://api.example.com',
          timeout: -1,
        });
      }).toThrow('timeout must be >= 0');
    });

    it('should use default timeout if not specified', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client).toBeDefined();
      // @ts-expect-error - accessing private property for testing
      expect(client.timeout).toBe(300000);
    });

    it('should remove trailing slash from baseUrl', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com/',
      });

      // @ts-expect-error - accessing private property for testing
      expect(client.baseUrl).toBe('https://api.example.com');
    });
  });

  // `describe('uploadFile')` was re-pointed 2026-09-03 (task 076): `uploadFile(containerId, …)` is
  // DELETED along with the route it called. The two surviving contracts are asserted here instead —
  // and, more usefully, that the container-keyed one has NOT quietly come back.
  describe('upload contracts', () => {
    it('exposes the record-keyed and record-less methods, and no container-keyed one', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client.uploadFileForRecord).toBeDefined();
      expect(client.uploadFileWithoutRecord).toBeDefined();
      expect((client as unknown as Record<string, unknown>).uploadFile).toBeUndefined();
    });
  });

  describe('downloadFile', () => {
    it('should be defined', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client.downloadFile).toBeDefined();
    });
  });

  describe('deleteFile', () => {
    it('should be defined', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client.deleteFile).toBeDefined();
    });
  });

  describe('getFileMetadata', () => {
    it('should be defined', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client.getFileMetadata).toBeDefined();
    });
  });
});
