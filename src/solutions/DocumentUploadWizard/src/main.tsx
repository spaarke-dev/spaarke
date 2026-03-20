/**
 * DocumentUploadWizard -- React 18 Code Page Entry Point
 *
 * Opened via Xrm.Navigation.navigateTo:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_DocumentUploadWizard",
 *       data: "parentEntityType=sprk_document&parentEntityId=...&parentEntityName=...&containerId=..." },
 *     { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" } }
 *   )
 *
 * Authentication:
 *   Resolves BFF URL, MSAL client ID, and OAuth scope from Dataverse
 *   Environment Variables at runtime via resolveRuntimeConfig(). Then
 *   initializes @spaarke/auth with those values (no build-time config).
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
import { resolveRuntimeConfig, initAuth } from "@spaarke/auth";
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
// Resolve runtime config, initialize auth, and render
// ---------------------------------------------------------------------------

async function bootstrap(): Promise<void> {
    // 1. Resolve BFF URL, MSAL client ID, and OAuth scope from Dataverse
    //    Environment Variables (pre-auth — uses session cookie, not MSAL).
    const runtimeConfig = await resolveRuntimeConfig();

    // 2. Set window globals so downstream modules (e.g., DocumentUploadWizardDialog)
    //    can read bffBaseUrl synchronously without re-querying Dataverse.
    window.__SPAARKE_BFF_BASE_URL__ = runtimeConfig.bffBaseUrl;
    window.__SPAARKE_MSAL_CLIENT_ID__ = runtimeConfig.msalClientId;

    // 3. Initialize auth with runtime-resolved values
    try {
        await initAuth({
            clientId: runtimeConfig.msalClientId,
            bffApiScope: runtimeConfig.bffOAuthScope,
            bffBaseUrl: runtimeConfig.bffBaseUrl,
        });
    } catch (err) {
        console.warn("[DocumentUploadWizard] Auth init failed, proceeding without pre-warmed token:", err);
    }

    // 4. Render
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
