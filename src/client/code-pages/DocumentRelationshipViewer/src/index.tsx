/**
 * DocumentRelationshipViewer — React 19 Code Page Entry Point
 *
 * Opened as HTML web resource dialog via:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_documentrelationshipviewer", data: "documentId=...&tenantId=..." },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   )
 *
 * When embedded on a form (no data params), resolves documentId from Xrm form
 * context and tenantId from @spaarke/auth after MSAL initialization.
 */

import { createRoot } from "react-dom/client";
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from "@fluentui/react-components";
import { App } from "./App";
import { initializeAuth, getAuthProvider } from "./services/authInit";

// Dataverse navigateTo({ pageType: "webresource", data: "k=v&k2=v2" }) wraps
// the caller's data string inside a single `?data=encodedString` query param.
// We unwrap it here so App.tsx always sees flat params (documentId, tenantId, etc.)
const urlParams = new URLSearchParams(window.location.search);
const dataEnvelope = urlParams.get("data");
const params = dataEnvelope
  ? new URLSearchParams(decodeURIComponent(dataEnvelope))
  : urlParams;

// When embedded on a form (no URL params), resolve from Xrm form context
/* eslint-disable @typescript-eslint/no-explicit-any */
if (!params.get("documentId")) {
  try {
    const xrm = (window.parent as any)?.Xrm ?? (window as any)?.Xrm;
    if (xrm) {
      const formContext = xrm.Page;
      const entityId = formContext?.data?.entity
        ?.getId?.()
        ?.replace(/[{}]/g, "");
      if (entityId) params.set("documentId", entityId);
    }
  } catch {
    /* cross-origin or unavailable */
  }
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// Check for explicit theme parameter from caller (e.g. PCF control passes theme=light/dark),
// then fall back to OS-level dark mode preference
const themeParam = params.get("theme");
const isDark = themeParam
  ? themeParam === "dark"
  : (window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false);

const container = document.getElementById("root");
if (!container)
  throw new Error(
    "[DocumentRelationshipViewer] Root container #root not found in DOM.",
  );

const root = createRoot(container);

// Initialize auth first so we can resolve tenantId if not in URL params
async function bootstrap(): Promise<void> {
  if (!params.get("tenantId")) {
    try {
      await initializeAuth();
      const tenantId = await getAuthProvider().getTenantId();
      if (tenantId) params.set("tenantId", tenantId);
    } catch (err) {
      console.warn(
        "[DocumentRelationshipViewer] Could not resolve tenantId from auth:",
        err,
      );
    }
  }

  root.render(
    <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
      <App params={params} isDark={isDark} />
    </FluentProvider>,
  );
}

bootstrap();
