/**
 * CommunicationPage — React 19 Code Page entry point (ADR-026).
 *
 * Opened as an HTML web resource via:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_communicationpage",
 *       data: "mode=view&id=<commGuid>" },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   )
 *
 * Bootstrap sequence:
 *   1. Parse the `data=` URL contract (reference §7.3).
 *   2. Bootstrap @spaarke/auth v2 (resolveRuntimeConfig → initAuth → authenticatedFetch).
 *      NO self-MSAL, NO token snapshot, NO accessToken prop (ADR-028).
 *   3. Resolve theme (ADR-021 4-level chain via shared util) and render.
 *
 * The record read + type-driven layout switch live in App / CommunicationLayout.
 */

import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, type Theme } from '@fluentui/react-components';
import { resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components';
import { App } from './App';
import { parseFromLocation } from './services/parseParams';
import { initializeAuth, type IResolvedAuth } from './services/authInit';

// 1. Parse the `data=` URL contract.
const params = parseFromLocation(window.location.search);

const container = document.getElementById('root');
if (!container) {
  throw new Error('[CommunicationPage] Root container #root not found in DOM.');
}
const root = createRoot(container);

/** Theme-aware shell — re-renders when the Dataverse theme toggles (ADR-021). */
function Root({ auth }: { auth: IResolvedAuth }): React.JSX.Element {
  const [theme, setTheme] = React.useState<Theme>(() => resolveCodePageTheme());

  React.useEffect(() => {
    // Returns an unsubscribe fn; re-resolve on any theme-source change.
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <App params={params} authenticatedFetch={auth.authenticatedFetch} bffBaseUrl={auth.bffBaseUrl} />
    </FluentProvider>
  );
}

async function bootstrap(): Promise<void> {
  // 2. Bootstrap @spaarke/auth v2 (URL auth overrides win over runtime config).
  const auth = await initializeAuth(params.auth);

  // 3. Render.
  root.render(<Root auth={auth} />);
}

void bootstrap();
