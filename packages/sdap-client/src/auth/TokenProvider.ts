/**
 * Token provider for SDAP API authentication.
 *
 * In PCF controls, tokens are automatically provided by the Dataverse context.
 * This is a placeholder for future token management logic.
 */
export class TokenProvider {
    /**
     * Get authentication token for SDAP API.
     *
     * In PCF controls, this will use context.webAPI to get the token.
     * For now, returns empty string (authentication handled by browser session).
     */
    public async getToken(): Promise<string> {
        // In PCF context, token is handled by Dataverse authentication
        // The browser session cookie will be used for authentication
        return '';
    }
}
