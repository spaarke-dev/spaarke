/**
 * AnalysisWorkspace -- React 19 Code Page Entry Point
 *
 * Supports two hosting modes:
 *
 *   1. Embedded on sprk_analysis form (primary):
 *      Added as a Web Resource control on the form. Dataverse passes record context
 *      via URL parameters when "Pass record object-type code and unique identifier
 *      as parameters" is checked in the form designer:
 *        ?id={analysisGuid}&typename=sprk_analysis&...
 *
 *   2. Opened via Xrm.Navigation.navigateTo (programmatic):
 *      Xrm.Navigation.navigateTo(
 *        { pageType: "webresource", webresourceName: "sprk_AnalysisWorkspace",
 *          data: "analysisId=...&documentId=..." },
 *        { target: 2, width: { value: 95, unit: "%" }, height: { value: 95, unit: "%" } }
 *      )
 *
 * Parameter resolution order:
 *   analysisId: data.analysisId → URL "id" (Dataverse form pass-through) → parent Xrm form context
 *   documentId: data.documentId → parent Xrm lookup field (sprk_sourcedocumentid)
 *   tenantId:   data.tenantId → parent Xrm organizationSettings.tenantId
 *
 * Authentication:
 *   Token acquisition is handled by AuthProvider + authService.ts.
 *   The authService walks the frame hierarchy (window → parent → top) to find
 *   Xrm.Utility.getGlobalContext() and acquire Bearer tokens for the BFF API.
 *
 * Theme detection follows 4-level priority:
 *   1. URL parameter (?theme=dark|light|highcontrast)
 *   2. Xrm frame-walk (Dataverse host theme)
 *   3. System preference (prefers-color-scheme media query)
 *   4. Default: webLightTheme
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-008 - Endpoint filters for auth (token acquisition via Xrm SDK)
 * @see ADR-021 - Fluent UI v9 design system (React 19 createRoot for Code Pages)
 */

import { useEffect } from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { App } from "./App";
import { AuthProvider } from "./context/AuthContext";
import { useThemeDetection } from "./hooks/useThemeDetection";

// ---------------------------------------------------------------------------
// Parse URL parameters (multi-source resolution)
// ---------------------------------------------------------------------------

const rawUrlParams = new URLSearchParams(window.location.search);
const dataEnvelope = rawUrlParams.get("data");
const appParams = dataEnvelope
    ? new URLSearchParams(decodeURIComponent(dataEnvelope))
    : rawUrlParams;

/**
 * Resolve analysisId from available sources:
 *   1. Explicit "analysisId" param (navigateTo data envelope)
 *   2. Dataverse form pass-through "id" param (embedded web resource with
 *      "Pass record object-type code and unique identifier" checked)
 *   3. Parent Xrm form context (fallback for embedded without pass-through)
 */
function resolveAnalysisId(): string {
    // Source 1: Explicit analysisId (navigateTo)
    const explicit = appParams.get("analysisId");
    if (explicit) return explicit;

    // Source 2: Dataverse "id" param (form web resource pass-through)
    const dvId = rawUrlParams.get("id");
    if (dvId) return dvId.replace(/[{}]/g, "").toLowerCase();

    // Source 3: Parent Xrm form context (embedded iframe)
    try {
        /* eslint-disable @typescript-eslint/no-explicit-any */
        const frames: Window[] = [];
        try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
        try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

        for (const frame of frames) {
            try {
                const xrm = (frame as any).Xrm;
                if (xrm?.Page?.data?.entity) {
                    const id = xrm.Page.data.entity.getId();
                    if (id) return id.replace(/[{}]/g, "").toLowerCase();
                }
            } catch { /* unavailable */ }
        }
        /* eslint-enable @typescript-eslint/no-explicit-any */
    } catch { /* frame access error */ }

    return "";
}

/**
 * Resolve documentId from available sources:
 *   1. Explicit "documentId" param (navigateTo data envelope)
 *   2. Parent Xrm form lookup field (sprk_sourcedocumentid on Analysis form)
 */
function resolveDocumentId(): string {
    const explicit = appParams.get("documentId");
    if (explicit) return explicit;

    try {
        /* eslint-disable @typescript-eslint/no-explicit-any */
        const frames: Window[] = [];
        try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
        try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

        for (const frame of frames) {
            try {
                const xrm = (frame as any).Xrm;
                const attr = xrm?.Page?.getAttribute?.("sprk_sourcedocumentid");
                if (attr) {
                    const val = attr.getValue();
                    if (Array.isArray(val) && val.length > 0 && val[0].id) {
                        return val[0].id.replace(/[{}]/g, "").toLowerCase();
                    }
                }
            } catch { /* unavailable */ }
        }
        /* eslint-enable @typescript-eslint/no-explicit-any */
    } catch { /* frame access error */ }

    return "";
}

/**
 * Resolve tenantId from available sources:
 *   1. Explicit "tenantId" param (navigateTo data envelope)
 *   2. Xrm organizationSettings.tenantId (frame-walk)
 */
function resolveTenantId(): string {
    const explicit = appParams.get("tenantId");
    if (explicit) return explicit;

    try {
        /* eslint-disable @typescript-eslint/no-explicit-any */
        const frames: Window[] = [window];
        try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
        try { if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!); } catch { /* cross-origin */ }

        for (const frame of frames) {
            try {
                const xrm = (frame as any).Xrm;
                const tid = xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId;
                if (tid) return tid.replace(/[{}]/g, "").toLowerCase();
            } catch { /* unavailable */ }
        }
        /* eslint-enable @typescript-eslint/no-explicit-any */
    } catch { /* frame access error */ }

    return "";
}

const analysisId = resolveAnalysisId();
const documentId = resolveDocumentId();
const tenantId = resolveTenantId();

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
