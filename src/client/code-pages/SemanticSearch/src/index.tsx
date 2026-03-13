/**
 * SemanticSearch -- React 19 Code Page Entry Point
 *
 * Opened as a full-page HTML web resource via Dataverse sitemap or command bar:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_semanticsearch",
 *       data: "query=...&domain=documents&scope=global&theme=light" },
 *     { target: 1 }
 *   )
 *
 * URL parameters (via Dataverse data envelope):
 *   theme         - Theme override: light | dark | highcontrast (optional)
 *   query         - Pre-filled search query (optional)
 *   domain        - Active search domain tab: documents | matters | projects | invoices (optional, default: documents)
 *   scope         - Search scope filter (optional)
 *   entityId      - Entity record ID for contextual search (optional)
 *   savedSearchId - Saved search to load on startup (optional)
 *
 * Theme detection follows ADR-026 4-level priority:
 *   1. URL param (?theme=dark|light|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme)
 *   3. System preference (prefers-color-scheme)
 *   4. Default: light
 */

import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { App } from "./App";
import {
  detectTheme,
  isDarkTheme,
  setupThemeListener,
} from "./providers/ThemeProvider";
import { parseUrlParams } from "./utils/parseUrlParams";
import { initializeAuth } from "./services/authInit";

// ---------------------------------------------------------------------------
// Parse URL parameters (ADR-026 data envelope unwrap)
// ---------------------------------------------------------------------------

const appParams = parseUrlParams();

const initialQuery = appParams.query ?? "";
const initialDomain = appParams.domain ?? "documents";
const initialScope = appParams.scope ?? "";
const initialEntityId = appParams.entityId ?? "";
const initialSavedSearchId = appParams.savedSearchId ?? "";

// ---------------------------------------------------------------------------
// Theme detection (ADR-026 4-level priority)
// ---------------------------------------------------------------------------

// Theme provider requires raw URLSearchParams — pass the unwrapped params
const rawUrlParams = new URLSearchParams(window.location.search);
const dataEnvelope = rawUrlParams.get("data");
const themeParams = dataEnvelope
  ? new URLSearchParams(decodeURIComponent(dataEnvelope))
  : rawUrlParams;

let theme = detectTheme(themeParams);
let isDark = isDarkTheme(themeParams);

// Set body background to match detected theme — prevents white flash in dark mode
document.body.style.backgroundColor = isDark ? "#292929" : "#ffffff";

// ---------------------------------------------------------------------------
// Initialize MSAL then Render
// ---------------------------------------------------------------------------

const container = document.getElementById("root");
if (!container)
  throw new Error("[SemanticSearch] Root container #root not found in DOM.");

const root = createRoot(container);

function renderApp(): void {
  root.render(
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <App
        initialQuery={initialQuery}
        initialDomain={initialDomain}
        initialScope={initialScope}
        initialEntityId={initialEntityId}
        initialSavedSearchId={initialSavedSearchId}
        isDark={isDark}
      />
    </FluentProvider>,
  );
}

// Initialize @spaarke/auth before rendering so tokens are ready for API calls.
// Uses multi-tenant authority and window.location.origin — no hardcoded tenant/redirect.
initializeAuth()
  .then(() => {
    console.info("[SemanticSearch] Auth initialized, rendering app.");
    renderApp();
  })
  .catch((err) => {
    console.error("[SemanticSearch] Auth init failed, rendering anyway.", err);
    renderApp();
  });

// ---------------------------------------------------------------------------
// Theme change listener — re-render on system/user preference change
// ---------------------------------------------------------------------------

setupThemeListener(() => {
  theme = detectTheme(themeParams);
  isDark = isDarkTheme(themeParams);
  document.body.style.backgroundColor = isDark ? "#292929" : "#ffffff";
  renderApp();
});
