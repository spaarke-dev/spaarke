/**
 * SDAP API Client Factory
 *
 * Creates SdapApiClient instances with @spaarke/auth authentication integration.
 * Uses getAuthProvider() for token acquisition with caching and proactive refresh.
 *
 * MIGRATION NOTE: This factory now uses @spaarke/auth instead of local MsalAuthProvider.
 * Token acquisition is handled by the shared SpaarkeAuthProvider singleton.
 */

import { SdapApiClient } from './SdapApiClient';
import { getAuthProvider } from '@spaarke/auth';
import { logger } from '../utils/logger';

/**
 * Factory for creating SdapApiClient instances
 */
export class SdapApiClientFactory {
  /**
   * Create SDAP API Client with @spaarke/auth authentication
   *
   * @param baseUrl - SDAP BFF API base URL
   * @param timeout - Request timeout in milliseconds (default: 300000 = 5 minutes)
   * @returns Configured SdapApiClient instance
   */
  static create(baseUrl: string, timeout = 300000): SdapApiClient {
    logger.info('SdapApiClientFactory', 'Creating SDAP API client with @spaarke/auth', {
      baseUrl,
      timeout,
    });

    // Create token provider function that uses @spaarke/auth
    const getAccessToken = async (): Promise<string> => {
      try {
        logger.debug('SdapApiClientFactory', 'Retrieving access token via @spaarke/auth');

        // Get user access token from @spaarke/auth
        // This token represents the current user and will be used by SDAP BFF API
        // for On-Behalf-Of (OBO) flow to access SharePoint Embedded
        const token = await getAuthProvider().getAccessToken();

        logger.debug('SdapApiClientFactory', 'Access token retrieved successfully');

        return token;
      } catch (error) {
        logger.error('SdapApiClientFactory', 'Failed to retrieve access token', error);
        throw new Error('Failed to retrieve user access token via @spaarke/auth');
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
  static createForTesting(baseUrl: string, staticToken: string, timeout = 300000): SdapApiClient {
    logger.info('SdapApiClientFactory', 'Creating SDAP API client for testing', {
      baseUrl,
      timeout,
    });

    const getAccessToken = async (): Promise<string> => {
      logger.debug('SdapApiClientFactory', 'Using static token for testing');
      return staticToken;
    };

    return new SdapApiClient(baseUrl, getAccessToken, timeout);
  }
}
