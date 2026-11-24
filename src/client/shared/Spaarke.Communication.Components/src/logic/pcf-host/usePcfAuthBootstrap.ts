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
  const { controlLabel, webApi, manifestClientAppId, manifestBffAppId, manifestApiBaseUrl, env, getClientUrl } = opts;

  React.useEffect(() => {
    const run = { cancelled: false };
    void (async () => {
      try {
        const result = await bootstrapPcfAuth({
          controlLabel,
          webApi,
          manifestClientAppId,
          manifestBffAppId,
          manifestApiBaseUrl,
          env,
          getClientUrl,
        });
        if (run.cancelled) return;
        if (result.status === 'not-configured') {
          setAuthError(result.message);
          return;
        }
        setBffBaseUrl(result.baseUrl);
        setAuthReady(true);
      } catch (err) {
        if (!run.cancelled) setAuthError(err instanceof Error ? err.message : 'Authentication failed to initialize.');
      }
    })();
    return () => {
      run.cancelled = true;
    };
    // env / getClientUrl / controlLabel are module-level constants, stable for the control's life.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [manifestClientAppId, manifestBffAppId, manifestApiBaseUrl, webApi]);

  return { authReady, authError, bffBaseUrl };
}
