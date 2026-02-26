/**
 * AnalysisWorkspace -- React 19 Code Page Entry Point
 *
 * Opened as a full-page HTML web resource via Dataverse command bar or SprkChat:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_analysisworkspace",
 *       data: "analysisId=...&documentId=...&tenantId=..." },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   )
 *
 * URL parameters (via Dataverse data envelope):
 *   analysisId  - Analysis session ID to load or resume (required)
 *   documentId  - Document ID for contextual analysis (optional)
 *   tenantId    - SharePoint Embedded tenant/container ID (optional)
 *   theme       - Theme override: light | dark | highcontrast (optional)
 *
 * Authentication (task 066):
 *   Token acquisition is handled independently by AuthProvider + authService.ts.
 *   The authService uses Xrm.Utility.getGlobalContext() to acquire Bearer tokens
 *   for the BFF API. No tokens are passed via URL parameters or BroadcastChannel.
 *
 * Theme detection follows 4-level priority (resolves PH-060-B light-theme-only):
 *   1. URL parameter (?theme=dark|light|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme)
 *   3. System preference (prefers-color-scheme media query)
 *   4. Default: webLightTheme
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-008 - Endpoint filters for auth (token acquisition via Xrm SDK)
 * @see ADR-021 - Fluent UI v9 design system (React 19 createRoot for Code Pages)
 * @see ADR-022 - PCF platform libraries (does NOT apply to Code Pages)
 */

import { useEffect } from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { App } from "./App";
import { AuthProvider } from "./context/AuthContext";
import { useThemeDetection } from "./hooks/useThemeDetection";

// ---------------------------------------------------------------------------
// Parse URL parameters (Dataverse data envelope unwrap)
// ---------------------------------------------------------------------------

const rawUrlParams = new URLSearchParams(window.location.search);
const dataEnvelope = rawUrlParams.get("data");
const appParams = dataEnvelope
    ? new URLSearchParams(decodeURIComponent(dataEnvelope))
    : rawUrlParams;

const analysisId = appParams.get("analysisId") ?? "";
const documentId = appParams.get("documentId") ?? "";
const tenantId = appParams.get("tenantId") ?? "";

// ---------------------------------------------------------------------------
// ThemeRoot -- wrapper component that uses useThemeDetection hook
// ---------------------------------------------------------------------------

/**
 * ThemeRoot wraps the application in a FluentProvider with dynamic theme
 * detection. Uses the useThemeDetection hook to resolve the current theme
 * from the 4-level priority chain and re-renders on OS/user preference changes.
 *
 * Also syncs the document body background color with the resolved theme to
 * prevent a white flash when the page loads in dark mode.
 */
function ThemeRoot(): JSX.Element {
    const { theme } = useThemeDetection(appParams);

    // Sync body background with resolved theme to prevent white flash in dark mode.
    // Uses the theme object's actual colorNeutralBackground1 value (a resolved hex/rgb string),
    // NOT tokens.* (which are CSS variable references that only work inside FluentProvider).
    useEffect(() => {
        const bgColor = (theme as Record<string, string>).colorNeutralBackground1;
        if (bgColor) {
            document.body.style.backgroundColor = bgColor;
        }
    }, [theme]);

    return (
        <FluentProvider theme={theme} style={{ height: "100%" }}>
            <AuthProvider>
                <App
                    analysisId={analysisId}
                    documentId={documentId}
                    tenantId={tenantId}
                />
            </AuthProvider>
        </FluentProvider>
    );
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------

const container = document.getElementById("root");
if (!container) throw new Error("[AnalysisWorkspace] Root container #root not found in DOM.");

createRoot(container).render(<ThemeRoot />);
