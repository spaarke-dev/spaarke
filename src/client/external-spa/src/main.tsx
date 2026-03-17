import * as React from "react";
import { createRoot } from "react-dom/client";
import { MsalProvider } from "@azure/msal-react";
import { App } from "./App";
import { msalInstance, initializeMsal } from "./auth/msal-auth";

// Note: This is a Power Pages Code Page SPA — not a PCF control.
// It uses React 18 with createRoot (bundled, not platform-provided).
// External users authenticate via Azure AD B2B guest accounts (MSAL redirect flow).
// See ADR-022 for the Code Page React 18 standard.
// See ADR-026 for the full-page Code Page build pattern (viteSingleFile).

async function bootstrap(): Promise<void> {
  // MSAL must be initialised before rendering (handles redirect promise)
  await initializeMsal();

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

bootstrap().catch((err) =>
  console.error("[SecureProjectWorkspace] Bootstrap failed", err)
);
