import * as React from "react";
import { createRoot } from "react-dom/client";
import { MsalProvider } from "@azure/msal-react";
import { msalInstance } from "./auth/msal-config";
import { App } from "./App";

// Power Pages Code Page SPA — React 18 with createRoot (bundled, not platform-provided).
// External users authenticate via Entra B2B — they are guest accounts in the main
// Spaarke workforce tenant and sign in with their existing Microsoft 365 credentials.
// MSAL (authorization code + PKCE) handles all token acquisition.
// See ADR-022 for the Code Page React 18 standard.
// See notes/auth-migration-b2b-msal.md for auth architecture details.

async function main() {
  // MSAL v3 requires explicit initialization before any token operations or rendering.
  // This processes any auth redirect response (auth code → tokens) before the app mounts.
  await msalInstance.initialize();

  const rootElement = document.getElementById("root");
  if (!rootElement) {
    console.error("[SecureProjectWorkspace] Root element not found");
    return;
  }

  createRoot(rootElement).render(
    <React.StrictMode>
      <MsalProvider instance={msalInstance}>
        <App />
      </MsalProvider>
    </React.StrictMode>
  );
}

void main();
