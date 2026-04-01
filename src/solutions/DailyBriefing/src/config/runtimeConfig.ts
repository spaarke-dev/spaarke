import type { IRuntimeConfig } from "@spaarke/auth";

let _config: IRuntimeConfig | null = null;
let _resolveReady: (() => void) | null = null;
const _readyPromise = new Promise<void>((resolve) => {
  _resolveReady = resolve;
});

export function setRuntimeConfig(config: IRuntimeConfig): void {
  _config = config;
  if (typeof window !== "undefined") {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_BFF_BASE_URL__ = config.bffBaseUrl;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_MSAL_CLIENT_ID__ = config.msalClientId;
  }
  _resolveReady?.();
}

/** Resolves once setRuntimeConfig() has been called. */
export function waitForConfig(): Promise<void> {
  return _readyPromise;
}

function getConfig(): IRuntimeConfig {
  if (!_config) {
    throw new Error(
      "[DailyBriefing] Runtime config not initialized. Call setRuntimeConfig() in main.tsx before using getters."
    );
  }
  return _config;
}

export function getBffBaseUrl(): string {
  return getConfig().bffBaseUrl;
}

export function getBffOAuthScope(): string {
  return getConfig().bffOAuthScope;
}

export function getMsalClientId(): string {
  return getConfig().msalClientId;
}
