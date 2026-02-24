/**
 * DocumentRelationshipViewer — React 19 Code Page Entry Point
 *
 * Opened as HTML web resource dialog via:
 *   Xrm.Navigation.navigateTo(
 *     { pageType: "webresource", webresourceName: "sprk_documentrelationshipviewer", data: "documentId=...&tenantId=..." },
 *     { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
 *   )
 */

import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { App } from "./App";

// Dataverse navigateTo({ pageType: "webresource", data: "k=v&k2=v2" }) wraps
// the caller's data string inside a single `?data=encodedString` query param.
// We unwrap it here so App.tsx always sees flat params (documentId, tenantId, etc.)
const urlParams = new URLSearchParams(window.location.search);
const dataEnvelope = urlParams.get("data");
const params = dataEnvelope
    ? new URLSearchParams(decodeURIComponent(dataEnvelope))
    : urlParams;

// Check for explicit theme parameter from caller (e.g. PCF control passes theme=light/dark),
// then fall back to OS-level dark mode preference
const themeParam = params.get("theme");
const isDark = themeParam
    ? themeParam === "dark"
    : (window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false);

const container = document.getElementById("root");
if (!container) throw new Error("[DocumentRelationshipViewer] Root container #root not found in DOM.");

createRoot(container).render(
    <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
        <App params={params} isDark={isDark} />
    </FluentProvider>
);
