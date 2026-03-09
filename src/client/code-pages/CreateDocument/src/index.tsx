/**
 * CreateDocument -- React 18 Code Page Entry Point
 *
 * Opened via Xrm.Navigation.navigateTo:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_CreateDocument",
 *       data: "containerId=...&matterId=..." },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   )
 *
 * Authentication:
 *   Uses @spaarke/auth initAuth() for BFF token acquisition.
 *   Supports Xrm frame-walk and MSAL popup fallback.
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-008 - Endpoint filters for auth (Bearer token from Xrm SDK)
 * @see ADR-021 - Fluent UI v9 design system (React 18 createRoot for Code Pages)
 */

import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { initAuth } from "@spaarke/auth";
import { App } from "./App";

// ---------------------------------------------------------------------------
// Parse URL parameters
// ---------------------------------------------------------------------------

const rawUrlParams = new URLSearchParams(window.location.search);
const dataEnvelope = rawUrlParams.get("data");
const appParams = dataEnvelope
    ? new URLSearchParams(decodeURIComponent(dataEnvelope))
    : rawUrlParams;

const containerId = appParams.get("containerId") ?? "";
const matterId = appParams.get("matterId") ?? "";

// ---------------------------------------------------------------------------
// Initialize auth and render
// ---------------------------------------------------------------------------

async function bootstrap(): Promise<void> {
    try {
        await initAuth();
    } catch (err) {
        console.warn("[CreateDocument] Auth init failed, proceeding without pre-warmed token:", err);
    }

    const container = document.getElementById("root");
    if (!container) throw new Error("[CreateDocument] Root container #root not found in DOM.");

    createRoot(container).render(
        <FluentProvider theme={webLightTheme} style={{ height: "100%" }}>
            <App containerId={containerId} matterId={matterId} />
        </FluentProvider>
    );
}

void bootstrap();
