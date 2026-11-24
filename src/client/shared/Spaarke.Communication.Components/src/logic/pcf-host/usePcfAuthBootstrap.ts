/**
 * `usePcfAuthBootstrap` — the React host-component half of the PCF auth bootstrap (#2967). React 16-safe
 * (`useEffect` / `useState` only). Runs once per set of manifest inputs; ignores a result that arrives after unmount.
 */
import * as React from 'react';
import { bootstrapPcfAuth, type IPcfAuthBootstrapOptions } from './authBootstrap';

export interface IPcfAuthState {
  authReady: boolean;
  authError: string | null;
  /** Host-only BFF base URL, set once auth is ready. */
  bffBaseUrl: string;
}

export function usePcfAuthBootstrap<TWebApi>(opts: IPcfAuthBootstrapOptions<TWebApi>): IPcfAuthState {
  const [authReady, setAuthReady] = React.useState(false);
  const [authError, setAuthError] = React.useState<string | null>(null);
  const [bffBaseUrl, setBffBaseUrl] = React.useState('');
  const { controlLabel, webApi, manifestClientAppId, manifestBffAppId, manifestApiBaseUrl, env, getXrm } = opts;

  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const result = await bootstrapPcfAuth({
          controlLabel,
          webApi,
          manifestClientAppId,
          manifestBffAppId,
          manifestApiBaseUrl,
          env,
          getXrm,
        });
        if (cancelled) return;
        if (result.status === 'not-configured') {
          setAuthError(result.message);
          return;
        }
        setBffBaseUrl(result.baseUrl);
        setAuthReady(true);
      } catch (err) {
        if (!cancelled) setAuthError(err instanceof Error ? err.message : 'Authentication failed to initialize.');
      }
    })();
    return () => {
      cancelled = true;
    };
    // env / getXrm / controlLabel are module-level constants, stable for the control's life.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [manifestClientAppId, manifestBffAppId, manifestApiBaseUrl, webApi]);

  return { authReady, authError, bffBaseUrl };
}
