# SDAP Shared Client - Integration Guide

**Package:** `@spaarke/sdap-client`
**Version:** 1.0.0
**Purpose:** Platform-specific integration instructions for using the SDAP Shared Client Library

---

## Table of Contents

1. [PCF Controls (Power Apps)](#pcf-controls-power-apps)
2. [Office.js Add-ins](#officejs-add-ins)
3. [React Web Applications](#react-web-applications)
4. [Angular Web Applications](#angular-web-applications)
5. [Vue.js Web Applications](#vuejs-web-applications)
6. [Node.js Server Applications](#nodejs-server-applications)
7. [Common Patterns](#common-patterns)
8. [Troubleshooting](#troubleshooting)

---

## PCF Controls (Power Apps)

### Installation

```bash
cd src/client/pcf/YourControl

# Option 1: From local tarball
npm install ../../../../shared/sdap-client/spaarke-sdap-client-1.0.0.tgz

# Option 2: From Azure Artifacts (future)
npm install @spaarke/sdap-client@1.0.0
```

### Token Provider Implementation

```typescript
// src/client/pcf/shared/PcfTokenProvider.ts
import { TokenProvider } from '@spaarke/sdap-client';
import { MsalAuthProvider } from '../UniversalQuickCreate/services/auth/MsalAuthProvider';

/**
 * PCF-specific token provider using MSAL authentication.
 */
export class PcfTokenProvider extends TokenProvider {
    constructor(private authProvider: MsalAuthProvider) {
        super();
    }

    /**
     * Get access token for BFF API from MSAL provider.
     */
    public async getToken(): Promise<string> {
        try {
            return await this.authProvider.getAccessToken();
        } catch (error) {
            console.error('[PcfTokenProvider] Failed to get token:', error);
            throw new Error('Authentication failed. Please refresh the page and try again.');
        }
    }
}
```

### Client Initialization

```typescript
// YourControl/index.ts
import { SdapApiClient } from '@spaarke/sdap-client';
import { PcfTokenProvider } from '../shared/PcfTokenProvider';
import { MsalAuthProvider } from './services/auth/MsalAuthProvider';

export class YourControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private sdapClient: SdapApiClient | null = null;
    private authProvider: MsalAuthProvider | null = null;
    private tokenProvider: PcfTokenProvider | null = null;

    public async init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): Promise<void> {
        // Initialize MSAL authentication
        this.authProvider = new MsalAuthProvider(
            context.parameters.clientAppId.raw!,
            context.parameters.bffAppId.raw!,
            context.parameters.tenantId.raw!
        );

        await this.authProvider.initialize();

        // Create token provider
        this.tokenProvider = new PcfTokenProvider(this.authProvider);

        // Initialize SDAP client
        this.sdapClient = new SdapApiClient({
            baseUrl: context.parameters.bffApiUrl.raw!,
            timeout: 300000 // 5 minutes
        });

        console.log('[YourControl] SDAP client initialized');
    }

    private async uploadFile(file: File, containerId: string): Promise<void> {
        if (!this.sdapClient || !this.tokenProvider) {
            throw new Error('Client not initialized');
        }

        try {
            // Get access token
            const token = await this.tokenProvider.getToken();

            // Upload file with progress tracking
            const result = await this.sdapClient.uploadFile(containerId, file, {
                onProgress: (percent) => {
                    console.log(`Upload progress: ${percent}%`);
                    this.updateProgressBar(percent);
                },
                signal: this.abortController.signal
            });

            console.log('[YourControl] Upload successful:', result.id);
            this.showSuccessMessage(`Uploaded ${result.name}`);

        } catch (error) {
            console.error('[YourControl] Upload failed:', error);
            this.showErrorMessage(`Upload failed: ${error.message}`);
        }
    }
}
```

### PCF Manifest Configuration

```xml
<!-- ControlManifest.Input.xml -->
<property name="bffApiUrl" display-name-key="BffApiUrl"
          of-type="SingleLine.Text" usage="input" required="true" />

<property name="clientAppId" display-name-key="ClientAppId"
          of-type="SingleLine.Text" usage="input" required="true" />

<property name="bffAppId" display-name-key="BffAppId"
          of-type="SingleLine.Text" usage="input" required="true" />

<property name="tenantId" display-name-key="TenantId"
          of-type="SingleLine.Text" usage="input" required="true" />
```

---

## Office.js Add-ins

### Installation

```bash
cd office-addin

npm install @spaarke/sdap-client@1.0.0
npm install @azure/msal-browser@3.0.0
```

### Token Provider Implementation

```typescript
// src/auth/OfficeTokenProvider.ts
import { TokenProvider } from '@spaarke/sdap-client';
import * as msal from '@azure/msal-browser';

/**
 * Office.js-specific token provider using MSAL Browser.
 */
export class OfficeTokenProvider extends TokenProvider {
    constructor(
        private msalInstance: msal.PublicClientApplication,
        private scopes: string[]
    ) {
        super();
    }

    /**
     * Get access token for BFF API with fallback to interactive login.
     */
    public async getToken(): Promise<string> {
        try {
            // Try silent token acquisition
            const account = this.msalInstance.getActiveAccount();
            if (!account) {
                throw new Error('No active account');
            }

            const result = await this.msalInstance.acquireTokenSilent({
                scopes: this.scopes,
                account
            });

            return result.accessToken;

        } catch (error) {
            console.warn('[OfficeTokenProvider] Silent token acquisition failed, trying popup');

            // Fallback to interactive popup
            const result = await this.msalInstance.acquireTokenPopup({
                scopes: this.scopes
            });

            return result.accessToken;
        }
    }
}
```

### Client Initialization (Word Add-in Example)

```typescript
// taskpane.ts
import { SdapApiClient } from '@spaarke/sdap-client';
import { OfficeTokenProvider } from './auth/OfficeTokenProvider';
import * as msal from '@azure/msal-browser';

let sdapClient: SdapApiClient;
let tokenProvider: OfficeTokenProvider;

Office.onReady(async () => {
    // Initialize MSAL
    const msalInstance = new msal.PublicClientApplication({
        auth: {
            clientId: 'YOUR_OFFICE_ADDIN_CLIENT_ID',
            authority: 'https://login.microsoftonline.com/YOUR_TENANT_ID',
            redirectUri: 'https://localhost:3000/taskpane.html'
        },
        cache: {
            cacheLocation: 'sessionStorage',
            storeAuthStateInCookie: false
        }
    });

    await msalInstance.initialize();

    // Handle redirect response
    await msalInstance.handleRedirectPromise();

    // Sign in if needed
    const accounts = msalInstance.getAllAccounts();
    if (accounts.length === 0) {
        await msalInstance.loginPopup({
            scopes: ['api://YOUR_BFF_APP_ID/user_impersonation']
        });
    } else {
        msalInstance.setActiveAccount(accounts[0]);
    }

    // Create token provider
    tokenProvider = new OfficeTokenProvider(
        msalInstance,
        ['api://YOUR_BFF_APP_ID/user_impersonation']
    );

    // Initialize SDAP client
    sdapClient = new SdapApiClient({
        baseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net',
        timeout: 300000
    });

    // Setup event handlers
    document.getElementById('uploadBtn').onclick = handleUpload;
});

async function handleUpload() {
    try {
        // Get current Word document as PDF
        const doc = await Word.run(async (context) => {
            const body = context.document.body;
            await context.sync();

            return await new Promise<Blob>((resolve, reject) => {
                Office.context.document.getFileAsync(
                    Office.FileType.Pdf,
                    (result) => {
                        if (result.status === Office.AsyncResultStatus.Succeeded) {
                            const file = result.value;
                            const slices: Blob[] = [];

                            const getSlice = (sliceIndex: number) => {
                                file.getSliceAsync(sliceIndex, (sliceResult) => {
                                    if (sliceResult.status === Office.AsyncResultStatus.Succeeded) {
                                        slices.push(new Blob([sliceResult.value.data]));

                                        if (sliceIndex < file.sliceCount - 1) {
                                            getSlice(sliceIndex + 1);
                                        } else {
                                            file.closeAsync();
                                            resolve(new Blob(slices, { type: 'application/pdf' }));
                                        }
                                    } else {
                                        reject(new Error(sliceResult.error.message));
                                    }
                                });
                            };

                            getSlice(0);
                        } else {
                            reject(new Error(result.error.message));
                        }
                    }
                );
            });
        });

        // Get matter ID from task pane UI
        const matterId = (document.getElementById('matterSelect') as HTMLSelectElement).value;

        // Upload document
        const result = await sdapClient.uploadFile(matterId, new File([doc], 'contract.pdf'), {
            onProgress: (percent) => {
                document.getElementById('progress').innerText = `${percent}%`;
            }
        });

        // Show success
        document.getElementById('status').innerText = `✅ Uploaded ${result.name}`;

    } catch (error) {
        console.error('[OfficeAddin] Upload failed:', error);
        document.getElementById('status').innerText = `❌ Upload failed: ${error.message}`;
    }
}
```

### Manifest.xml Configuration

```xml
<!-- manifest.xml -->
<WebApplicationInfo>
  <Id>YOUR_OFFICE_ADDIN_CLIENT_ID</Id>
  <Resource>api://YOUR_BFF_APP_ID</Resource>
  <Scopes>
    <Scope>user_impersonation</Scope>
  </Scopes>
</WebApplicationInfo>
```

---

## React Web Applications

### Installation

```bash
npm install @spaarke/sdap-client@1.0.0
npm install @azure/msal-browser@3.0.0
npm install @azure/msal-react@2.0.0
```

### Token Provider Implementation

```typescript
// src/lib/WebTokenProvider.ts
import { TokenProvider } from '@spaarke/sdap-client';
import { PublicClientApplication } from '@azure/msal-browser';

/**
 * React SPA token provider using MSAL Browser.
 */
export class WebTokenProvider extends TokenProvider {
    constructor(
        private msalInstance: PublicClientApplication,
        private scopes: string[]
    ) {
        super();
    }

    /**
     * Get access token for BFF API (silent acquisition).
     */
    public async getToken(): Promise<string> {
        const account = this.msalInstance.getActiveAccount();
        if (!account) {
            throw new Error('No active account. Please sign in.');
        }

        try {
            const result = await this.msalInstance.acquireTokenSilent({
                scopes: this.scopes,
                account
            });

            return result.accessToken;

        } catch (error) {
            console.error('[WebTokenProvider] Silent token acquisition failed:', error);

            // Redirect to login page
            await this.msalInstance.acquireTokenRedirect({
                scopes: this.scopes
            });

            throw new Error('Redirecting to login...');
        }
    }
}
```

### React Hook

```typescript
// src/hooks/useSdapClient.ts
import { useMemo } from 'react';
import { useMsal } from '@azure/msal-react';
import { SdapApiClient } from '@spaarke/sdap-client';
import { WebTokenProvider } from '../lib/WebTokenProvider';

/**
 * React hook for SDAP client instance.
 */
export function useSdapClient() {
    const { instance } = useMsal();

    return useMemo(() => {
        const tokenProvider = new WebTokenProvider(
            instance,
            [process.env.REACT_APP_SDAP_SCOPE!]
        );

        return new SdapApiClient({
            baseUrl: process.env.REACT_APP_SDAP_API_URL!,
            timeout: 300000
        });
    }, [instance]);
}
```

### Component Usage

```typescript
// src/components/DocumentUpload.tsx
import { useState } from 'react';
import { useSdapClient } from '../hooks/useSdapClient';
import { toast } from 'react-hot-toast';

interface DocumentUploadProps {
    projectId: string;
    onUploadComplete?: () => void;
}

export function DocumentUpload({ projectId, onUploadComplete }: DocumentUploadProps) {
    const sdapClient = useSdapClient();
    const [uploading, setUploading] = useState(false);
    const [progress, setProgress] = useState(0);

    const handleFileSelect = async (event: React.ChangeEvent<HTMLInputElement>) => {
        const files = event.target.files;
        if (!files || files.length === 0) return;

        setUploading(true);

        try {
            for (const file of Array.from(files)) {
                setProgress(0);

                const result = await sdapClient.uploadFile(projectId, file, {
                    onProgress: setProgress
                });

                toast.success(`Uploaded ${result.name}`);
            }

            onUploadComplete?.();

        } catch (error) {
            toast.error(`Upload failed: ${error.message}`);

        } finally {
            setUploading(false);
            setProgress(0);
        }
    };

    return (
        <div className="document-upload">
            <input
                type="file"
                multiple
                onChange={handleFileSelect}
                disabled={uploading}
            />

            {uploading && (
                <div className="progress-bar">
                    <div
                        className="progress-bar__fill"
                        style={{ width: `${progress}%` }}
                    />
                    <span>{progress}%</span>
                </div>
            )}
        </div>
    );
}
```

### MSAL Provider Setup

```typescript
// src/App.tsx
import { MsalProvider } from '@azure/msal-react';
import { PublicClientApplication } from '@azure/msal-browser';

const msalInstance = new PublicClientApplication({
    auth: {
        clientId: process.env.REACT_APP_CLIENT_ID!,
        authority: `https://login.microsoftonline.com/${process.env.REACT_APP_TENANT_ID}`,
        redirectUri: window.location.origin
    },
    cache: {
        cacheLocation: 'sessionStorage',
        storeAuthStateInCookie: false
    }
});

function App() {
    return (
        <MsalProvider instance={msalInstance}>
            <YourApp />
        </MsalProvider>
    );
}
```

---

## Common Patterns

### Uploading Multiple Files

```typescript
async function uploadMultipleFiles(
    sdapClient: SdapApiClient,
    containerId: string,
    files: FileList
): Promise<DriveItem[]> {
    const results: DriveItem[] = [];

    for (const file of Array.from(files)) {
        try {
            const result = await sdapClient.uploadFile(containerId, file, {
                onProgress: (percent) => {
                    console.log(`${file.name}: ${percent}%`);
                }
            });

            results.push(result);

        } catch (error) {
            console.error(`Failed to upload ${file.name}:`, error);
            // Continue with next file
        }
    }

    return results;
}
```

### Cancellation Support

```typescript
const abortController = new AbortController();

// Upload with cancellation
const uploadPromise = sdapClient.uploadFile(containerId, file, {
    onProgress: (percent) => console.log(percent),
    signal: abortController.signal
});

// Cancel button handler
document.getElementById('cancelBtn').onclick = () => {
    abortController.abort();
};

try {
    await uploadPromise;
} catch (error) {
    if (error.name === 'AbortError') {
        console.log('Upload cancelled by user');
    }
}
```

### Error Handling Best Practices

```typescript
async function uploadWithRetry(
    sdapClient: SdapApiClient,
    containerId: string,
    file: File,
    maxRetries = 3
): Promise<DriveItem> {
    let lastError: Error | null = null;

    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            return await sdapClient.uploadFile(containerId, file, {
                onProgress: (percent) => {
                    console.log(`Attempt ${attempt}: ${percent}%`);
                }
            });

        } catch (error) {
            lastError = error as Error;
            console.warn(`Upload attempt ${attempt} failed:`, error.message);

            if (attempt < maxRetries) {
                // Exponential backoff
                await new Promise(resolve => setTimeout(resolve, 1000 * Math.pow(2, attempt - 1)));
            }
        }
    }

    throw lastError!;
}
```

---

## Troubleshooting

### Issue: "No active account. Please sign in."

**Cause:** MSAL instance has no active account

**Solution:**
```typescript
const accounts = msalInstance.getAllAccounts();
if (accounts.length === 0) {
    await msalInstance.loginPopup();
} else {
    msalInstance.setActiveAccount(accounts[0]);
}
```

### Issue: "Upload timeout after 300000ms"

**Cause:** Large file upload exceeding timeout

**Solution:** Increase timeout for large files
```typescript
const sdapClient = new SdapApiClient({
    baseUrl: '...',
    timeout: 600000 // 10 minutes for very large files
});
```

### Issue: Chunked upload progress not updating

**Cause:** Progress callback not provided

**Solution:** Always provide onProgress callback
```typescript
await sdapClient.uploadFile(containerId, file, {
    onProgress: (percent) => {
        updateProgressBar(percent); // Update UI
    }
});
```

### Issue: "CORS error" in browser console

**Cause:** BFF API not configured for CORS

**Solution:** Ensure BFF API has CORS enabled for your origin
```csharp
// Program.cs
app.UseCors(policy => policy
    .WithOrigins("https://yourapp.com")
    .AllowAnyMethod()
    .AllowAnyHeader()
);
```

---

**Document Version:** 1.0
**Last Updated:** December 2, 2025
**Next:** See [MIGRATION-PLAN.md](./MIGRATION-PLAN.md) for step-by-step migration guide
