/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Dev fallback backend (one-entry list) when VITE_BACKENDS is unset — task 041. */
  readonly VITE_BFF_API_URL?: string;
  /** Dev backend list (JSON array of { customerKey, displayName, bffBaseUrl, bffAppId, ciamBffAppId? }) — task 041. */
  readonly VITE_BACKENDS?: string;
  /** 'true' selects the T240c directory service client (a stub until T240c ships) — task 041. */
  readonly VITE_USE_DIRECTORY_SERVICE?: string;
  readonly VITE_MSAL_CLIENT_ID: string;
  readonly VITE_MSAL_TENANT_ID: string;
  /** Dev fallback CIAM scope: api://{CIAM BFF app id}/user_impersonation (task 041; SDAP.Access is refused). */
  readonly VITE_MSAL_BFF_SCOPE?: string;
  readonly VITE_DEV_MOCK?: string;
  /** Single-tenant A8 client (task 040): under A9 the guest-token link only (browser and Teams); optional. */
  readonly VITE_TEAMS_MSAL_CLIENT_ID?: string;
  readonly VITE_TEAMS_MSAL_BFF_SCOPE?: string;
  /** Spaarke's tenant id (a GUID) — the workforce authority is pinned to it (ADR-028 A8, task 040). */
  readonly VITE_TEAMS_MSAL_TENANT_ID?: string;
  /**
   * The multitenant workforce HOME sign-in client id (a GUID) - ADR-028 Amendment A9, task 058. A different application
   * from the single-tenant client above (VITE_TEAMS_MSAL_CLIENT_ID); optional, required for the Teams tab (task 075)
   * and the browser "company account" sign-in.
   */
  readonly VITE_WORKFORCE_HOME_MSAL_CLIENT_ID?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
