/**
 * Spaarke BFF Auth Helper - shared MSAL silent-SSO token acquisition for classic web resources
 *
 * Web Resource Name: sprk_/scripts/bff_auth.js
 *
 * PURPOSE (task 030 / spec FR-17, code-quality-and-assurance-r3):
 * The BFF's authenticated endpoints (e.g. the Finance recalculate-grades endpoints closed by
 * task 023, and the Scorecard sibling) REQUIRE an Azure AD bearer token. Classic Dataverse web
 * resources cannot `import` the npm `@spaarke/auth` package, so this single shared helper mirrors
 * that package's canonical MSAL silent-SSO flow (see
 * src/client/shared/Spaarke.Auth/src/strategies/BrowserMsalStrategy.ts) ONCE, and every KPI/rollup
 * caller consumes it instead of inlining ~120 lines of MSAL bootstrap per file.
 *
 * It is the horizontal de-duplication of the flow first landed inline in
 * sprk_subgrid_parent_rollup.js v2.0.0 (task 023).
 *
 * =============================================================================
 * FORM-LIBRARY REGISTRATION (REQUIRED — this is a deployment step, not code)
 * =============================================================================
 * This helper defines NO form event handlers. It only exposes `Spaarke.BffAuth`. Every form that
 * hosts a caller which consumes `Spaarke.BffAuth` MUST register this web resource
 * (`sprk_/scripts/bff_auth.js`) as a form library **ordered BEFORE** the caller's library, so the
 * `Spaarke.BffAuth` namespace exists when the caller's OnLoad/OnPostSave handler runs. Callers are
 * defensive: if this helper is not loaded they log an error and skip the API call gracefully (the
 * form never breaks), but the recalculate will not run until the library is registered.
 *
 * Consumers (register this library before each on its form):
 *   - sprk_/scripts/matter_kpi_refresh.js       (Matter main form)
 *   - sprk_/scripts/kpi_subgrid_refresh.js      (Matter + Project main forms)
 *   - sprk_/scripts/kpiassessment_quickcreate.js (KPI Assessment Quick Create form)
 *   - sprk_/scripts/subgrid_parent_rollup.js     (may migrate off its inline copy in a future
 *                                                 form-registration-coordinated deployment)
 *
 * =============================================================================
 * ⚠ CANNOT BE VALIDATED OFFLINE
 * =============================================================================
 * The silent-SSO flow requires a live Dataverse form/iframe with a signed-in user session and
 * these deployment prerequisites on the BFF app registration (the AzureAd:ClientId returned by
 * /api/config/client):
 *   - an SPA redirect URI registered for the Dataverse org origin (window.location.origin), and
 *   - admin consent for the api://{clientId}/user_impersonation scope.
 * Verify in a live environment before relying on it. `node --check` passes (syntax only). Same
 * live-validation caveat as task 023.
 *
 * NO interactive popup is ever triggered (ADR-028 INV-5): a form load is not explicit user intent
 * to authenticate, so on a cold cache getToken() returns null and the caller skips the API call.
 *
 * @see .claude/adr/ADR-028-spaarke-auth-architecture.md
 * @see src/solutions/webresources/sprk_subgrid_parent_rollup.js (reference inline impl, task 023)
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.BffAuth = Spaarke.BffAuth || {};

// =============================================================================
// CONFIGURATION
// =============================================================================

/** Version for console logging. */
Spaarke.BffAuth._version = "1.0.0"; // 1.0.0 — initial shared helper (task 030 / FR-17)

/**
 * MSAL v2 CDN pin (Microsoft-hosted). Classic web resources cannot bundle npm modules; a CDN
 * <script> is the standard shared-lib load path here. Pin matches sprk_subgrid_parent_rollup.js.
 */
Spaarke.BffAuth._MSAL_CDN_URL = "https://alcdn.msauth.net/browser/2.38.4/js/msal-browser.min.js";

/** Cached MSAL PublicClientApplication instance (module-level). */
Spaarke.BffAuth._msalInstance = null;

/** Cached MSAL client config from /api/config/client, keyed by apiBaseUrl (module-level). */
Spaarke.BffAuth._msalConfig = null;

/** In-flight MSAL.js CDN load promise (module-level, load-once). */
Spaarke.BffAuth._msalLoadPromise = null;

// =============================================================================
// MSAL BOOTSTRAP
// =============================================================================

/**
 * Dynamically load the MSAL.js browser UMD bundle from the Microsoft CDN (once).
 * @returns {Promise<Object>} resolves to the global `msal` namespace.
 */
Spaarke.BffAuth._loadMsal = function () {
    if (typeof window !== "undefined" && window.msal) {
        return Promise.resolve(window.msal);
    }
    if (Spaarke.BffAuth._msalLoadPromise) {
        return Spaarke.BffAuth._msalLoadPromise;
    }
    Spaarke.BffAuth._msalLoadPromise = new Promise(function (resolve, reject) {
        try {
            var script = document.createElement("script");
            script.src = Spaarke.BffAuth._MSAL_CDN_URL;
            script.async = true;
            script.onload = function () {
                if (window.msal) {
                    resolve(window.msal);
                } else {
                    reject(new Error("MSAL.js loaded but window.msal is undefined"));
                }
            };
            script.onerror = function () {
                reject(new Error("Failed to load MSAL.js from " + Spaarke.BffAuth._MSAL_CDN_URL));
            };
            document.head.appendChild(script);
        } catch (e) {
            reject(e);
        }
    });
    return Spaarke.BffAuth._msalLoadPromise;
};

/**
 * Fetch the anonymous MSAL bootstrap config from the BFF (cached module-level).
 * @param {string} apiBaseUrl - BFF API base URL.
 * @returns {Promise<{clientId:string, authority:string, scopes:string[]}>}
 */
Spaarke.BffAuth._getMsalConfig = function (apiBaseUrl) {
    if (Spaarke.BffAuth._msalConfig) {
        return Promise.resolve(Spaarke.BffAuth._msalConfig);
    }
    return fetch(apiBaseUrl + "/api/config/client", {
        method: "GET",
        headers: { "Accept": "application/json" }
    }).then(function (resp) {
        if (!resp.ok) {
            throw new Error("/api/config/client returned " + resp.status);
        }
        return resp.json();
    }).then(function (cfg) {
        Spaarke.BffAuth._msalConfig = {
            clientId: cfg.msalClientId,
            authority: cfg.msalAuthority,
            scopes: cfg.msalScopes || []
        };
        return Spaarke.BffAuth._msalConfig;
    });
};

/**
 * Resolve a UPN login hint for ssoSilent. Prefers MSAL's own cached account username
 * (authoritative UPN); falls back to Xrm userSettings.userName. Returns undefined if neither
 * yields a value (ssoSilent then relies on the Entra session cookie alone).
 */
Spaarke.BffAuth._resolveLoginHint = function (msalInstance) {
    try {
        var accounts = msalInstance.getAllAccounts();
        if (accounts && accounts.length > 0 && accounts[0].username) {
            return accounts[0].username;
        }
    } catch (e) { /* ignore */ }
    try {
        var ctx = Xrm.Utility.getGlobalContext();
        var settings = ctx && ctx.userSettings ? ctx.userSettings : null;
        if (settings && settings.userName) {
            return settings.userName;
        }
    } catch (e) { /* ignore */ }
    return undefined;
};

// =============================================================================
// PUBLIC API
// =============================================================================

/**
 * Acquire a BFF access token via MSAL silent SSO. Returns null on any failure (callers skip the
 * API call gracefully — never throws, never triggers an interactive popup, ADR-028 INV-5).
 *
 * @param {string} apiBaseUrl - BFF API base URL.
 * @returns {Promise<string|null>} access token, or null if acquisition failed.
 */
Spaarke.BffAuth.getToken = function (apiBaseUrl) {
    if (!apiBaseUrl) {
        return Promise.resolve(null);
    }
    return Spaarke.BffAuth._getMsalConfig(apiBaseUrl).then(function (cfg) {
        return Spaarke.BffAuth._loadMsal().then(function (msal) {
            if (!Spaarke.BffAuth._msalInstance) {
                Spaarke.BffAuth._msalInstance = new msal.PublicClientApplication({
                    auth: {
                        clientId: cfg.clientId,
                        authority: cfg.authority,
                        redirectUri: window.location.origin
                    },
                    cache: {
                        cacheLocation: "localStorage",   // INV-1 — survives tab/browser close
                        storeAuthStateInCookie: true     // INV-2 — ssoSilent under 3rd-party cookie blocking
                    }
                });
            }
            var instance = Spaarke.BffAuth._msalInstance;
            var scopes = cfg.scopes;

            // 1. acquireTokenSilent with a cached account (refresh-token-backed).
            var accounts = instance.getAllAccounts();
            var silent = (accounts && accounts.length > 0)
                ? instance.acquireTokenSilent({ scopes: scopes, account: accounts[0] })
                : Promise.reject(new Error("no cached account"));

            return silent.then(function (result) {
                return result && result.accessToken ? result.accessToken : null;
            }).catch(function () {
                // 2. ssoSilent with a UPN login hint (uses the Entra session cookie).
                var loginHint = Spaarke.BffAuth._resolveLoginHint(instance);
                var req = loginHint ? { scopes: scopes, loginHint: loginHint } : { scopes: scopes };
                return instance.ssoSilent(req).then(function (result) {
                    return result && result.accessToken ? result.accessToken : null;
                });
                // NOTE: intentionally NO acquireTokenPopup fallback (ADR-028 INV-5).
            });
        });
    }).catch(function (error) {
        console.warn("[BffAuth] BFF token acquisition failed (caller will skip the call):", error);
        return null;
    });
};

/**
 * Convenience wrapper: acquire a token and issue a fetch with the Authorization: Bearer header
 * attached. Returns null (WITHOUT calling fetch) if no token could be acquired, so callers can
 * skip gracefully rather than sending an unauthenticated request that would just 401.
 *
 * @param {string} url - Absolute request URL.
 * @param {Object} options - fetch options (headers merged; method/body preserved).
 * @param {string} apiBaseUrl - BFF API base URL (used to resolve MSAL config + token).
 * @returns {Promise<Response|null>} the fetch Response, or null if no token was acquired.
 */
Spaarke.BffAuth.authenticatedFetch = function (url, options, apiBaseUrl) {
    options = options || {};
    return Spaarke.BffAuth.getToken(apiBaseUrl).then(function (token) {
        if (!token) {
            return null;
        }
        var headers = {};
        if (options.headers) {
            for (var k in options.headers) {
                if (Object.prototype.hasOwnProperty.call(options.headers, k)) {
                    headers[k] = options.headers[k];
                }
            }
        }
        headers["Authorization"] = "Bearer " + token;
        var merged = {};
        for (var p in options) {
            if (Object.prototype.hasOwnProperty.call(options, p)) {
                merged[p] = options[p];
            }
        }
        merged.headers = headers;
        return fetch(url, merged);
    });
};

/* eslint-enable no-undef */
