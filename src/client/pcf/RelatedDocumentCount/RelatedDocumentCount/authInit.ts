/**
 * Authentication initialization for RelatedDocumentCount PCF Control.
 *
 * Uses @spaarke/auth shared library for centralized MSAL token management.
 * Mirrors SemanticSearchControl's authInit.ts pattern.
 *
 * @spaarke/auth has NO React dependency — safe for React 16 PCF controls.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/**
 * Azure AD Application (Client) ID
 * From: Sparke DSM-SPE Dev 2 App Registration
 */
const CLIENT_ID = '170c98e1-d486-4355-bcbe-170454e0207c';

/**
 * Azure AD Tenant (Directory) ID
 */
const TENANT_ID = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';

/**
 * BFF Application ID (for scope construction)
 */
const BFF_APP_ID = '1e40baad-e065-4aea-a8d4-4b7ab273458c';

/**
 * Initialize @spaarke/auth with PCF-specific configuration.
 *
 * @param _bffApiUrl BFF API base URL (reserved for future use)
 */
export async function initializeAuth(_bffApiUrl: string): Promise<void> {
    console.info('[authInit] Initializing @spaarke/auth for RelatedDocumentCount...');

    const config: IAuthConfig = {
        clientId: CLIENT_ID,
        authority: `https://login.microsoftonline.com/${TENANT_ID}`,
        redirectUri: 'https://spaarkedev1.crm.dynamics.com',
        bffApiScope: `api://${BFF_APP_ID}/user_impersonation`,
    };

    await initAuth(config);

    console.info('[authInit] @spaarke/auth initialized successfully for RelatedDocumentCount');
}
