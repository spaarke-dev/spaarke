/**
 * mountWizardPage / WizardPageInitializing
 * ----------------------------------------
 * The loading shell and `createRoot` mount that every standalone wizard Code Page
 * repeated around `useWizardPageBootstrap`. Lives next to the hook so the pages
 * carry only what is specific to their wizard.
 */
import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import type { resolveCodePageTheme } from './themeStorage';

/** Spinner-less "Initializing..." shell shown until `@spaarke/auth` is ready. */
export function WizardPageInitializing({
  theme,
}: {
  theme: ReturnType<typeof resolveCodePageTheme>;
}): React.ReactElement {
  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
        <span>Initializing...</span>
      </div>
    </FluentProvider>
  );
}

/** Mounts `App` into `#root` under StrictMode; logs `[logPrefix] Root element not found` otherwise. */
export function mountWizardPage(logPrefix: string, App: React.ComponentType): void {
  const rootElement = document.getElementById('root');
  if (rootElement) {
    createRoot(rootElement).render(
      <React.StrictMode>
        <App />
      </React.StrictMode>
    );
  } else {
    console.error(`[${logPrefix}] Root element not found`);
  }
}
