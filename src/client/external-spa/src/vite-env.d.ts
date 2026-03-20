/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_BFF_API_URL: string;
  readonly VITE_MSAL_CLIENT_ID: string;
  readonly VITE_MSAL_TENANT_ID: string;
  readonly VITE_MSAL_BFF_SCOPE: string;
  readonly VITE_PORTAL_URL?: string;
  readonly VITE_DEV_MOCK?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
