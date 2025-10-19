/**
 * SDAP API Client Factory
 *
 * Creates SdapApiClient instances with MSAL authentication integration.
 * Uses MsalAuthProvider for token acquisition with caching and proactive refresh.
 */

import { SdapApiClient } from './SdapApiClient';
import { MsalAuthProvider } from './auth/MsalAuthProvider';
import { logger } from '../utils/logger';

/**
 * Factory for creating SdapApiClient instances
 */
export class SdapApiClientFactory {
    /**
     * OAuth scopes for SDAP BFF API (SharePoint Embedded)
     *
     * SPE BFF API App Registration:
     * - Client ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
     * - Application ID URI: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
     */
    private static readonly SPE_BFF_API_SCOPES = ['api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'];

    /**
     * Create SDAP API Client with MSAL authentication
     *
     * @param baseUrl - SDAP BFF API base URL
     * @param timeout - Request timeout in milliseconds (default: 300000 = 5 minutes)
     * @returns Configured SdapApiClient instance
     */
    static create(
        baseUrl: string,
        timeout = 300000
    ): SdapApiClient {
        logger.info('SdapApiClientFactory', 'Creating SDAP API client with MSAL auth', {
            baseUrl,
            timeout
        });

        // Create token provider function that uses MSAL
        const getAccessToken = async (): Promise<string> => {
            try {
                logger.debug('SdapApiClientFactory', 'Retrieving access token via MSAL');

                // Get user access token from MSAL
                // This token represents the current user and will be used by SDAP BFF API
                // for On-Behalf-Of (OBO) flow to access SharePoint Embedded
                const authProvider = MsalAuthProvider.getInstance();

                // Wait for MSAL initialization if not ready yet (handles race condition)
                // This can happen if user clicks buttons before initializeMsalAsync() completes
                if (!authProvider.isInitializedState()) {
                    logger.info('SdapApiClientFactory', 'MSAL not yet initialized, waiting...');
                    await authProvider.initialize();
                    logger.info('SdapApiClientFactory', 'MSAL initialization complete');
                }

                const token = await authProvider.getToken(SdapApiClientFactory.SPE_BFF_API_SCOPES);

                logger.debug('SdapApiClientFactory', 'Access token retrieved successfully');

                return token;

            } catch (error) {
                logger.error('SdapApiClientFactory', 'Failed to retrieve access token', error);
                throw new Error('Failed to retrieve user access token via MSAL');
            }
        };

        return new SdapApiClient(baseUrl, getAccessToken, timeout);
    }

    /**
     * Create SDAP API Client for testing purposes
     *
     * @param baseUrl - SDAP BFF API base URL
     * @param staticToken - Static access token for testing
     * @param timeout - Request timeout in milliseconds
     * @returns Configured SdapApiClient instance
     */
    static createForTesting(
        baseUrl: string,
        staticToken: string,
        timeout = 300000
    ): SdapApiClient {
        logger.info('SdapApiClientFactory', 'Creating SDAP API client for testing', {
            baseUrl,
            timeout
        });

        const getAccessToken = async (): Promise<string> => {
            logger.debug('SdapApiClientFactory', 'Using static token for testing');
            return staticToken;
        };

        return new SdapApiClient(baseUrl, getAccessToken, timeout);
    }
}
