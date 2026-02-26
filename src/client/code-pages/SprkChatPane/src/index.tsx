/**
 * SprkChatPane -- React 19 Code Page Entry Point
 *
 * Opened as a Dataverse side pane HTML web resource via:
 *   Xrm.App.sidePanes.createPane({
 *     title: "SprkChat",
 *     paneId: "sprkchat",
 *     canClose: true,
 *     imageSrc: "...",
 *     webresourceName: "sprk_sprkchatpane",
 *     data: "entityType=sprk_matter&entityId=...&playbookId=...&sessionId=..."
 *   })
 *
 * URL parameters (via Dataverse data envelope):
 *   entityType  - Dataverse entity logical name for record context (optional)
 *   entityId    - Entity record ID for contextual chat (optional)
 *   playbookId  - AI playbook ID for guided interactions (optional)
 *   sessionId   - Existing chat session to resume (optional)
 *   theme       - Theme override: light | dark | highcontrast (optional)
 *   apiBaseUrl  - BFF API base URL override (optional; defaults to dev endpoint)
 *
 * Authentication:
 *   Token acquisition is handled independently by App.tsx via authService.ts.
 *   The authService uses Xrm.Utility.getGlobalContext() to acquire Bearer tokens
 *   for the BFF API. No tokens are passed via URL parameters or BroadcastChannel.
 *
 * Theme detection follows 4-level priority (replaces PH-010-B light-theme-only):
 *   1. User preference (localStorage 'spaarke-theme')
 *   2. URL parameter (?theme=dark|light|highcontrast)
 *   3. Xrm frame-walk (Dataverse host theme)
 *   4. System preference (prefers-color-scheme media query)
 *
 * @see ADR-008 - Endpoint filters for auth (token acquisition via Xrm SDK)
 * @see ADR-021 - Fluent UI v9 design system (React 19 createRoot for Code Pages)
 * @see ADR-022 - PCF platform libraries (does NOT apply to Code Pages)
 */

import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { App } from "./App";
import { detectTheme, setupThemeListener } from "./ThemeProvider";

// ---------------------------------------------------------------------------
// Parse URL parameters (Dataverse data envelope unwrap)
// ---------------------------------------------------------------------------

const rawUrlParams = new URLSearchParams(window.location.search);
const dataEnvelope = rawUrlParams.get("data");
const appParams = dataEnvelope
    ? new URLSearchParams(decodeURIComponent(dataEnvelope))
    : rawUrlParams;

const entityType = appParams.get("entityType") ?? "";
const entityId = appParams.get("entityId") ?? "";
const playbookId = appParams.get("playbookId") ?? "";
const sessionId = appParams.get("sessionId") ?? "";

// ---------------------------------------------------------------------------
// API configuration
// ---------------------------------------------------------------------------

// Resolve BFF API base URL: URL param > Xrm global context > fallback
function resolveApiBaseUrl(): string {
    // Check URL param first
    const urlApi = appParams.get("apiBaseUrl");
    if (urlApi) return urlApi;

    // Try Xrm global context for Dataverse environment URL
    try {
        /* eslint-disable @typescript-eslint/no-explicit-any */
        const xrm = (window as any).Xrm ?? (window.parent as any)?.Xrm;
        if (xrm?.Utility?.getGlobalContext) {
            const clientUrl = xrm.Utility.getGlobalContext().getClientUrl();
            if (clientUrl) {
                // The BFF API URL is not the Dataverse URL itself but we
                // return the known dev endpoint; in production this would be
                // read from a Dataverse environment variable or configuration.
            }
        }
        /* eslint-enable @typescript-eslint/no-explicit-any */
    } catch {
        /* cross-origin or unavailable */
    }

    // Default to the known dev BFF API endpoint
    return "https://spe-api-dev-67e2xz.azurewebsites.net";
}

const apiBaseUrl = resolveApiBaseUrl();

// NOTE: accessToken is no longer resolved here. Authentication is handled
// independently by App.tsx via services/authService.ts, which uses
// Xrm.Utility.getGlobalContext() at runtime. This avoids transmitting
// tokens through the render pipeline and supports automatic refresh.

// ---------------------------------------------------------------------------
// Theme detection (4-level priority — replaces PH-010-B)
// ---------------------------------------------------------------------------

let theme = detectTheme(appParams);

// Set body background to match detected theme — prevents white flash in dark mode.
// Uses the theme object's actual colorNeutralBackground1 value (a resolved hex/rgb string),
// NOT tokens.* (which are CSS variable references that only work inside FluentProvider).
const bgColor = (theme as Record<string, string>).colorNeutralBackground1;
if (bgColor) {
    document.body.style.backgroundColor = bgColor;
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

const container = document.getElementById("root");
if (!container) throw new Error("[SprkChatPane] Root container #root not found in DOM.");

const root = createRoot(container);

function renderApp(): void {
    root.render(
        <FluentProvider theme={theme} style={{ height: "100%" }}>
            <App
                entityType={entityType}
                entityId={entityId}
                playbookId={playbookId}
                sessionId={sessionId}
                apiBaseUrl={apiBaseUrl}
            />
        </FluentProvider>
    );
}

// Initial render
renderApp();

// ---------------------------------------------------------------------------
// Theme change listener — re-render on system/user preference change
// ---------------------------------------------------------------------------

setupThemeListener(() => {
    theme = detectTheme(appParams);
    const updatedBg = (theme as Record<string, string>).colorNeutralBackground1;
    if (updatedBg) {
        document.body.style.backgroundColor = updatedBg;
    }
    renderApp();
});
