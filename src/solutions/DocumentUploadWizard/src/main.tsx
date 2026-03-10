/**
 * DocumentUploadWizard -- React 18 Code Page Entry Point
 *
 * Opened via Xrm.Navigation.navigateTo:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_DocumentUploadWizard",
 *       data: "parentEntityType=sprk_document&parentEntityId=...&parentEntityName=...&containerId=..." },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   )
 *
 * Authentication:
 *   Uses @spaarke/auth initAuth() for BFF token acquisition.
 *   Supports Xrm frame-walk and MSAL popup fallback.
 *
 * Theme Detection:
 *   Reads data-theme attribute set by the inline script in index.html.
 *   Falls back to webLightTheme if detection fails.
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-008 - Endpoint filters for auth (Bearer token from Xrm SDK)
 * @see ADR-021 - Fluent UI v9 design system (React 18 createRoot for Code Pages)
 * @see ADR-022 - React 18 with createRoot for Code Pages
 */

import { createRoot } from "react-dom/client";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
} from "@fluentui/react-components";
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

const parentEntityType = appParams.get("parentEntityType") ?? "";
const parentEntityId = appParams.get("parentEntityId") ?? "";
const parentEntityName = appParams.get("parentEntityName") ?? "";
const containerId = appParams.get("containerId") ?? "";

// ---------------------------------------------------------------------------
// Theme detection
// ---------------------------------------------------------------------------

function resolveTheme() {
    const dataTheme = document.documentElement.getAttribute("data-theme");
    return dataTheme === "dark" ? webDarkTheme : webLightTheme;
}

// ---------------------------------------------------------------------------
// Initialize auth and render
// ---------------------------------------------------------------------------

async function bootstrap(): Promise<void> {
    try {
        await initAuth();
    } catch (err) {
        console.warn("[DocumentUploadWizard] Auth init failed, proceeding without pre-warmed token:", err);
    }

    const container = document.getElementById("root");
    if (!container) throw new Error("[DocumentUploadWizard] Root container #root not found in DOM.");

    createRoot(container).render(
        <FluentProvider theme={resolveTheme()} style={{ height: "100%" }}>
            <App
                parentEntityType={parentEntityType}
                parentEntityId={parentEntityId}
                parentEntityName={parentEntityName}
                containerId={containerId}
            />
        </FluentProvider>
    );
}

void bootstrap();
