/**
 * SDAP API Client Factory
 *
 * Creates SdapApiClient instances with MSAL authentication integration.
 * Uses MsalAuthProvider for token acquisition with caching and proactive refresh.
 *
 * Updated for shared library: imports SdapApiClient from @spaarke/ui-components.
 */

import { SdapApiClient } from '@spaarke/ui-components/src/services/document-upload';
import { MsalAuthProvider } from './auth/MsalAuthProvider';
import { logger } from '../utils/logger';

/**
 * Factory for creating SdapApiClient instances.
 *
 * OAuth scopes are resolved at runtime from MsalAuthProvider.getBffApiScopes()
 * rather than hardcoded. The BFF API App ID URI comes from the Dataverse
 * environment variable sprk_BffApiAppId.
 */
export class SdapApiClientFactory {

  /**
   * Create SDAP API Client with MSAL authentication
   *
   * @param baseUrl - SDAP BFF API base URL
   * @param timeout - Request timeout in milliseconds (default: 300000 = 5 minutes)
   * @returns Configured SdapApiClient instance
   */
  static create(baseUrl: string, timeout = 300000): SdapApiClient {
    logger.info('SdapApiClientFactory', 'Creating SDAP API client with MSAL auth', {
      baseUrl,
      timeout,
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

        const token = await authProvider.getToken(authProvider.getBffApiScopes());

        logger.debug('SdapApiClientFactory', 'Access token retrieved successfully');

        return token;
      } catch (error) {
        logger.error('SdapApiClientFactory', 'Failed to retrieve access token', error);
        throw new Error('Failed to retrieve user access token via MSAL');
      }
    };

    return new SdapApiClient({
      baseUrl,
      getAccessToken,
      timeout,
      logger,
      onUnauthorized: () => {
        // Clear MSAL cache to force fresh token acquisition on 401
        MsalAuthProvider.getInstance().clearCache();
      },
    });
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

    return new SdapApiClient({
      baseUrl,
      getAccessToken,
      timeout,
      logger,
    });
  }
}
