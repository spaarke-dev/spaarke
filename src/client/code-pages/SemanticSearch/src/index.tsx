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
import { detectTheme, isDarkTheme } from "./providers/ThemeProvider";
import { parseUrlParams } from "./utils/parseUrlParams";
import { msalAuthProvider } from "./services/auth/MsalAuthProvider";

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

const theme = detectTheme(themeParams);
const isDark = isDarkTheme(themeParams);

// Set body background to match detected theme — prevents white flash in dark mode
document.body.style.backgroundColor = isDark ? "#292929" : "#ffffff";

// ---------------------------------------------------------------------------
// Initialize MSAL then Render
// ---------------------------------------------------------------------------

const container = document.getElementById("root");
if (!container) throw new Error("[SemanticSearch] Root container #root not found in DOM.");

function renderApp(): void {
    createRoot(container!).render(
        <FluentProvider theme={theme} style={{ height: "100%" }}>
            <App
                initialQuery={initialQuery}
                initialDomain={initialDomain}
                initialScope={initialScope}
                initialEntityId={initialEntityId}
                initialSavedSearchId={initialSavedSearchId}
                isDark={isDark}
            />
        </FluentProvider>
    );
}

// Initialize MSAL before rendering so auth is ready for API calls
msalAuthProvider.initialize()
    .then(() => {
        console.info("[SemanticSearch] MSAL initialized, rendering app.");
        renderApp();
    })
    .catch((err) => {
        console.error("[SemanticSearch] MSAL init failed, rendering anyway.", err);
        renderApp();
    });
