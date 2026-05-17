/// <reference types="vite/client" />

/**
 * Vite build-time environment variables for SpaarkeAi Code Page.
 *
 * These are embedded in the bundle at build time, not at runtime.
 * All VITE_* variables are public — do not place secrets here.
 */
interface ImportMetaEnv {
  /**
   * BFF API base URL — used as fallback when resolveRuntimeConfig() cannot
   * reach Xrm (direct URL access without the Dataverse MDA shell, no localStorage cache).
   *
   * Set in .env (development) or via CI/CD pipeline env var (production).
   * Example: https://spe-api-dev-67e2xz.azurewebsites.net
   */
  readonly VITE_BFF_BASE_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
