import { SdapApiClient } from '../SdapApiClient';

describe('SdapApiClient', () => {
    describe('constructor', () => {
        it('should accept valid config', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com'
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
                    timeout: -1
                });
            }).toThrow('timeout must be >= 0');
        });

        it('should use default timeout if not specified', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com'
            });

            expect(client).toBeDefined();
            // @ts-expect-error - accessing private property for testing
            expect(client.timeout).toBe(300000);
        });

        it('should remove trailing slash from baseUrl', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com/'
            });

            // @ts-expect-error - accessing private property for testing
            expect(client.baseUrl).toBe('https://api.example.com');
        });
    });

    describe('uploadFile', () => {
        it('should be defined', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com'
            });

            expect(client.uploadFile).toBeDefined();
        });
    });

    describe('downloadFile', () => {
        it('should be defined', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com'
            });

            expect(client.downloadFile).toBeDefined();
        });
    });

    describe('deleteFile', () => {
        it('should be defined', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com'
            });

            expect(client.deleteFile).toBeDefined();
        });
    });

    describe('getFileMetadata', () => {
        it('should be defined', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com'
            });

            expect(client.getFileMetadata).toBeDefined();
        });
    });
});
