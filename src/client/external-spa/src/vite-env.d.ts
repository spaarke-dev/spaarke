/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_BFF_API_URL: string;
  readonly VITE_MSAL_CLIENT_ID: string;
  readonly VITE_MSAL_TENANT_ID: string;
  readonly VITE_MSAL_BFF_SCOPE: string;
  readonly VITE_DEV_MOCK?: string;
  /** Teams collaboration host (task 012) — optional; required only when running inside Teams. */
  readonly VITE_TEAMS_MSAL_CLIENT_ID?: string;
  readonly VITE_TEAMS_MSAL_BFF_SCOPE?: string;
  /** Spaarke's tenant id (a GUID) — the workforce authority is pinned to it (ADR-028 A7, task 040). */
  readonly VITE_TEAMS_MSAL_TENANT_ID?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
