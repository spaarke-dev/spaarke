import * as React from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MsalProvider } from '@azure/msal-react';
import { FluentProvider, Text, tokens } from '@fluentui/react-components';
import { resolveCodePageTheme } from '@spaarke/ui-components/utils/themeStorage';
import { msalInstance } from './auth/msal-config';
import { detectTeamsHost, createTeamsHostAdapter } from './host/TeamsHostAdapter';
import { App } from './App';

// Power Pages Code Page SPA — React 18 with createRoot (bundled, not platform-provided).
// External users authenticate via Entra B2B — they are guest accounts in the main
// Spaarke workforce tenant and sign in with their existing Microsoft 365 credentials.
// MSAL (authorization code + PKCE) handles all token acquisition.
// See ADR-022 for the Code Page React 18 standard.
// See notes/auth-migration-b2b-msal.md for auth architecture details.
//
// HOST-DETECTION WIRING (task 012 - Teams host adapter + host-detection seam)
// -----------------------------------------------------------------------------
// This entry point branches on detectTeamsHost() BEFORE choosing a bootstrap path. The SAME <App />
// tree (the shared collaboration core - WorkspaceHomePage, ProjectPage, DocumentLibrary, ...) renders
// under EITHER host; only the bootstrap in this file + host/TeamsHostAdapter.ts differs (design.md D1
// - shared core + thin host adapter). Detection is a bounded-timeout race (see
// TeamsHostAdapter.detectTeamsHost) so it NEVER blocks the standalone browser boot path -
// bootstrapStandalone() below is the app's pre-task-012 behavior, unchanged, just extracted into its
// own function so main() can choose between the two (FR-15: no CIAM regression).

function getRootElement(): HTMLElement | null {
  const rootElement = document.getElementById('root');
  if (!rootElement) {
    console.error('[SecureProjectWorkspace] Root element not found');
  }
  return rootElement;
}

/** Standalone browser bootstrap - the CIAM path. Unchanged from the pre-task-012 implementation. */
async function bootstrapStandalone(root: Root): Promise<void> {
  if (import.meta.env.VITE_DEV_MOCK !== 'true') {
    // MSAL v3 requires explicit initialization before any token operations or rendering.
    // This processes any auth redirect response (auth code to tokens) before the app mounts.
    // Skipped in mock mode - MSAL can hang/error on localhost without a registered redirect URI.
    await msalInstance.initialize();
  }

  root.render(
    <React.StrictMode>
      <MsalProvider instance={msalInstance}>
        <App />
      </MsalProvider>
    </React.StrictMode>
  );
}

/**
 * Teams tab bootstrap. Delegates ALL Teams-specific concerns (app.initialize, context, theme,
 * deep-link, auth-strategy selection) to the host adapter - this function only sequences
 * "initialize, then render the SAME App" and handles the fail-loud error path.
 */
async function bootstrapTeams(root: Root): Promise<void> {
  const adapter = createTeamsHostAdapter();

  try {
    await adapter.initialize();
  } catch (err) {
    console.error('[SecureProjectWorkspace] Teams workforce authentication failed:', err);
    renderTeamsAuthError(root, err);
    return;
  }

  root.render(
    <React.StrictMode>
      <MsalProvider instance={adapter.getMsalInstance()}>
        <App />
      </MsalProvider>
    </React.StrictMode>
  );
}

/**
 * Fail-loud error state for a Teams bootstrap failure (acceptance criterion 4 - never silently
 * degrade to a blank/broken tab). Minimal Fluent v9 markup styled exclusively with semantic tokens
 * (ADR-021); intentionally NOT a feature component - this is bootstrap-path infrastructure, shown
 * only when Teams workforce auth cannot be established at all.
 */
function renderTeamsAuthError(root: Root, err: unknown): void {
  const message = err instanceof Error ? err.message : 'Sign-in to the Spaarke collaboration tab failed.';
  // The adapter writes the Teams-provided theme to localStorage BEFORE the auth step that can
  // fail (see TeamsHostAdapter.wireTheme), so resolveCodePageTheme() already reflects the correct
  // Teams theme here even on an auth failure - this error screen is never a jarring light-in-dark
  // mismatch (ADR-021).
  root.render(
    <React.StrictMode>
      <FluentProvider theme={resolveCodePageTheme()} style={{ height: '100%' }}>
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            height: '100dvh',
            rowGap: tokens.spacingVerticalM,
            padding: tokens.spacingVerticalXXL,
            textAlign: 'center',
            backgroundColor: tokens.colorNeutralBackground1,
            color: tokens.colorNeutralForeground1,
          }}
        >
          <Text size={500} weight="semibold">
            Sign-in failed
          </Text>
          <Text size={300} style={{ color: tokens.colorNeutralForeground3, maxWidth: '32rem' }}>
            {message}
          </Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground4 }}>
            Contact your Teams admin if this persists - your organization may not have consented to the Spaarke app yet.
          </Text>
        </div>
      </FluentProvider>
    </React.StrictMode>
  );
}

async function main() {
  const rootElement = getRootElement();
  if (!rootElement) return;
  const root = createRoot(rootElement);

  const inTeams = await detectTeamsHost();
  if (inTeams) {
    await bootstrapTeams(root);
  } else {
    await bootstrapStandalone(root);
  }
}

void main();
