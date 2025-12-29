# MSAL Client Pattern

> **Domain**: OAuth / Client-Side Authentication
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-006

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/UniversalQuickCreate/control/services/auth/msalConfig.ts` | Configuration |
| `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` | Token acquisition |
| `src/client/pcf/UniversalQuickCreate/control/services/SdapApiClientFactory.ts` | API client factory |

---

## MSAL Configuration

```typescript
import { Configuration, LogLevel } from "@azure/msal-browser";

const CLIENT_ID = "{PCF_APP_CLIENT_ID}";
const TENANT_ID = "{AZURE_TENANT_ID}";
const REDIRECT_URI = "https://{org}.crm.dynamics.com";

export const msalConfig: Configuration = {
    auth: {
        clientId: CLIENT_ID,
        authority: `https://login.microsoftonline.com/${TENANT_ID}`,
        redirectUri: REDIRECT_URI,
        navigateToLoginRequestUrl: false
    },
    cache: {
        cacheLocation: "sessionStorage",  // Cleared on tab close
        storeAuthStateInCookie: false
    },
    system: {
        loggerOptions: {
            loggerCallback: (level, message, containsPii) => {
                if (containsPii) return;  // Never log PII
                if (level === LogLevel.Error) console.error(`[MSAL] ${message}`);
            },
            logLevel: LogLevel.Warning
        }
    }
};

export const loginRequest = {
    scopes: ["api://{BFF_API_APP_ID}/user_impersonation"]
};
```

---

## Singleton Auth Provider

```typescript
export class MsalAuthProvider implements IAuthProvider {
    private static instance: MsalAuthProvider;
    private msalInstance: PublicClientApplication | null = null;
    private currentAccount: AccountInfo | null = null;
    private isInitialized = false;

    public static getInstance(): MsalAuthProvider {
        if (!MsalAuthProvider.instance) {
            MsalAuthProvider.instance = new MsalAuthProvider();
        }
        return MsalAuthProvider.instance;
    }

    public async initialize(): Promise<void> {
        if (this.isInitialized) return;

        validateMsalConfig();
        this.msalInstance = new PublicClientApplication(msalConfig);

        // REQUIRED in MSAL.js v3+
        await this.msalInstance.initialize();

        // Handle redirect response
        const redirectResponse = await this.msalInstance.handleRedirectPromise();
        if (redirectResponse) {
            this.currentAccount = redirectResponse.account;
        }

        // Set active account if available
        if (!this.currentAccount) {
            const accounts = this.msalInstance.getAllAccounts();
            if (accounts.length > 0) {
                this.currentAccount = accounts[0];
            }
        }

        this.isInitialized = true;
    }
}
```

---

## Token Acquisition Flow

```typescript
public async getToken(scopes: string[]): Promise<string> {
    if (!this.msalInstance) {
        throw new Error("MSAL not initialized");
    }

    // 1. Check local cache
    const cached = this.getCachedToken(scopes);
    if (cached) return cached;

    // 2. Try silent acquisition
    try {
        const response = await this.acquireTokenSilent(scopes);
        this.cacheToken(response);
        return response.accessToken;

    } catch (error) {
        // 3. Fallback to popup
        if (error instanceof InteractionRequiredAuthError) {
            const response = await this.acquireTokenPopup(scopes);
            this.cacheToken(response);
            return response.accessToken;
        }
        throw error;
    }
}
```

### Silent Token Acquisition

```typescript
private async acquireTokenSilent(scopes: string[]): Promise<AuthenticationResult> {
    // Option 1: With known account
    if (this.currentAccount) {
        try {
            return await this.msalInstance!.acquireTokenSilent({
                scopes,
                account: this.currentAccount
            });
        } catch {
            // Fall through to ssoSilent
        }
    }

    // Option 2: SSO discovery from browser session
    const response = await this.msalInstance!.ssoSilent({ scopes });
    if (response.account) {
        this.currentAccount = response.account;
    }
    return response;
}
```

### Popup Token Acquisition

```typescript
private async acquireTokenPopup(scopes: string[]): Promise<AuthenticationResult> {
    const response = await this.msalInstance!.acquireTokenPopup({
        scopes,
        loginHint: this.currentAccount?.username
    });

    if (response.account) {
        this.currentAccount = response.account;
    }
    return response;
}
```

---

## API Client Factory Usage

```typescript
export class SdapApiClientFactory {
    private static readonly BFF_API_SCOPES = [
        'api://{BFF_APP_ID}/user_impersonation'
    ];

    static create(baseUrl: string): SdapApiClient {
        const getAccessToken = async (): Promise<string> => {
            const authProvider = MsalAuthProvider.getInstance();

            if (!authProvider.isInitializedState()) {
                await authProvider.initialize();
            }

            return authProvider.getToken(SdapApiClientFactory.BFF_API_SCOPES);
        };

        return new SdapApiClient(baseUrl, getAccessToken);
    }
}
```

---

## PCF Integration

```typescript
// In PCF init()
public async init(context, notifyOutputChanged, state, container): Promise<void> {
    // Initialize MSAL early (non-blocking)
    this.initializeMsalAsync().catch(err => {
        console.error("MSAL init failed", err);
    });

    // Continue with PCF rendering
    this.renderComponent();
}

private async initializeMsalAsync(): Promise<void> {
    const authProvider = MsalAuthProvider.getInstance();
    await authProvider.initialize();
}
```

---

## Key Points

1. **Singleton pattern** - Only one MSAL instance per page
2. **Initialize async** - Don't block PCF init
3. **Silent first** - Always try silent before popup
4. **sessionStorage** - Cleared on tab close (security)
5. **Handle redirects** - Call `handleRedirectPromise()` on init

---

## Related Patterns

- [OAuth Scopes](oauth-scopes.md) - Scope configuration
- [Token Caching](token-caching.md) - Client-side caching

---

**Lines**: ~125
