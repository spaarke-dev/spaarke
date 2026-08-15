/**
 * Generic Subgrid Parent Rollup - Configuration-driven
 *
 * Web Resource Name: sprk_/scripts/subgrid_parent_rollup.js
 *
 * A reusable web resource for any parent-child rollup scenario.
 * Register on any parent entity form's OnLoad event with a JSON
 * configuration string to define the subgrid name, API endpoint,
 * and entity type mapping.
 *
 * Supports multiple subgrids on the same form (each gets its own
 * instance keyed by subgridName).
 *
 * Form Events:
 *   Event: OnLoad
 *   Library: sprk_/scripts/subgrid_parent_rollup.js
 *   Function: Spaarke.SubgridRollup.onLoad
 *   Pass execution context: Yes
 *   Parameters: JSON config string (see below)
 *
 * Config parameter format:
 * {
 *   "subgridName": "subgrid_kpiassessments",
 *   "apiPathTemplate": "/api/{entityPath}/{entityId}/recalculate-grades",
 *   "entityPathMap": { "sprk_matter": "matters", "sprk_project": "projects" },
 *   "refreshDelayMs": 1500
 * }
 *
 * AUTH (task 023 / spec FR-09): calls the BFF over an Azure AD bearer token acquired via
 * @spaarke/auth (MSAL silent SSO). The Finance recalculate endpoints are no longer anonymous.
 * See the "BFF AUTHENTICATION" section below for the flow + live-validation caveat.
 *
 * @see .claude/patterns/webresource/subgrid-parent-rollup.md
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.SubgridRollup = Spaarke.SubgridRollup || {};

// =============================================================================
// CONFIGURATION
// =============================================================================

/** Per-subgrid instance state. Keyed by subgridName. */
Spaarke.SubgridRollup._instances = {};

/** Version for console logging. */
Spaarke.SubgridRollup._version = "2.0.0"; // 2.0.0 — @spaarke/auth Bearer token (task 023 / FR-09)

// =============================================================================
// ENVIRONMENT VARIABLE RESOLUTION
// =============================================================================

/** Cached BFF API base URL (module-level, shared across all instances). */
Spaarke.SubgridRollup._cachedApiBaseUrl = null;

/**
 * Resolve the BFF API base URL from Dataverse Environment Variables.
 * Queries environmentvariabledefinition + environmentvariablevalue for
 * "sprk_BffApiBaseUrl". Caches the result in a module-level variable so
 * subsequent calls (and multiple subgrid instances) skip the query.
 *
 * @returns {Promise<string>} BFF API base URL
 * @throws {Error} If the environment variable is not configured
 */
Spaarke.SubgridRollup._getApiBaseUrl = function () {
    // Return cached value immediately (wrapped in resolved promise)
    if (Spaarke.SubgridRollup._cachedApiBaseUrl) {
        return Promise.resolve(Spaarke.SubgridRollup._cachedApiBaseUrl);
    }

    var schemaName = "sprk_BffApiBaseUrl";

    // Query the environment variable definition
    return Xrm.WebApi.retrieveMultipleRecords(
        "environmentvariabledefinition",
        "?$filter=schemaname eq '" + schemaName + "'&$select=environmentvariabledefinitionid,defaultvalue"
    ).then(function (definitionResult) {
        if (!definitionResult.entities || definitionResult.entities.length === 0) {
            throw new Error(
                '[SubgridRollup] Required environment variable "' + schemaName + '" not found in Dataverse. ' +
                'Ensure it is defined as an Environment Variable Definition in the solution.'
            );
        }

        var definition = definitionResult.entities[0];
        var definitionId = definition.environmentvariabledefinitionid;
        var defaultValue = definition.defaultvalue || null;

        // Query for an override value
        return Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariablevalue",
            "?$filter=_environmentvariabledefinitionid_value eq '" + definitionId + "'&$select=value"
        ).then(function (valueResult) {
            var finalValue = null;
            if (valueResult.entities && valueResult.entities.length > 0) {
                finalValue = valueResult.entities[0].value;
            } else {
                finalValue = defaultValue;
            }

            if (!finalValue) {
                throw new Error(
                    '[SubgridRollup] Environment variable "' + schemaName + '" has no value. ' +
                    'Set a default value on the definition or create an Environment Variable Value override.'
                );
            }

            // Cache for subsequent calls
            Spaarke.SubgridRollup._cachedApiBaseUrl = finalValue;
            console.log("[SubgridRollup] Resolved BFF URL from env var: " + finalValue);
            return finalValue;
        });
    });
};

// =============================================================================
// BFF AUTHENTICATION (@spaarke/auth — MSAL silent SSO)
// =============================================================================
//
// Task 023 (code-quality-and-assurance-r3, spec FR-09): the BFF Finance recalculate
// endpoints now REQUIRE an Azure AD bearer token (no longer AllowAnonymous). This classic
// web resource cannot import the npm `@spaarke/auth` package, so it mirrors that package's
// canonical MSAL flow (see src/client/shared/Spaarke.Auth/src/strategies/BrowserMsalStrategy.ts):
//
//   1. GET {apiBaseUrl}/api/config/client (anonymous) → { msalClientId, msalAuthority, msalScopes }
//   2. Load @azure/msal-browser (v2 UMD) from the Microsoft CDN (classic web resources cannot
//      bundle npm modules; a CDN <script> is the standard shared-lib load path here).
//   3. acquireTokenSilent (cached account) → ssoSilent (UPN login hint, uses the existing
//      Entra session cookie). NO interactive popup is ever triggered (ADR-028 INV-5): a form
//      load is not explicit user intent to authenticate, so on a cold cache we return null and
//      the API call is skipped gracefully rather than popping a sign-in window.
//
// ⚠️ CANNOT BE VALIDATED OFFLINE. This silent-SSO flow requires a live Dataverse form/iframe
//    with a signed-in user session and the following deployment prerequisites on the BFF app
//    registration (AzureAd:ClientId returned by /api/config/client):
//      - an SPA redirect URI registered for the Dataverse org origin (window.location.origin), and
//      - admin consent for the api://{clientId}/user_impersonation scope.
//    Verify in a live environment before relying on it. If token acquisition fails for any
//    reason, the recalculate call is skipped (best-effort) and logged — the form never breaks.
//
// MSAL v2 CDN pin (Subresource-Integrity-friendly, Microsoft-hosted).
Spaarke.SubgridRollup._MSAL_CDN_URL = "https://alcdn.msauth.net/browser/2.38.4/js/msal-browser.min.js";

/** Cached MSAL PublicClientApplication instance (module-level). */
Spaarke.SubgridRollup._msalInstance = null;

/** Cached MSAL client config from /api/config/client (module-level). */
Spaarke.SubgridRollup._msalConfig = null;

/** In-flight MSAL.js CDN load promise (module-level, load-once). */
Spaarke.SubgridRollup._msalLoadPromise = null;

/**
 * Dynamically load the MSAL.js browser UMD bundle from the Microsoft CDN (once).
 * @returns {Promise<Object>} resolves to the global `msal` namespace.
 */
Spaarke.SubgridRollup._loadMsal = function () {
    if (typeof window !== "undefined" && window.msal) {
        return Promise.resolve(window.msal);
    }
    if (Spaarke.SubgridRollup._msalLoadPromise) {
        return Spaarke.SubgridRollup._msalLoadPromise;
    }
    Spaarke.SubgridRollup._msalLoadPromise = new Promise(function (resolve, reject) {
        try {
            var script = document.createElement("script");
            script.src = Spaarke.SubgridRollup._MSAL_CDN_URL;
            script.async = true;
            script.onload = function () {
                if (window.msal) {
                    resolve(window.msal);
                } else {
                    reject(new Error("MSAL.js loaded but window.msal is undefined"));
                }
            };
            script.onerror = function () {
                reject(new Error("Failed to load MSAL.js from " + Spaarke.SubgridRollup._MSAL_CDN_URL));
            };
            document.head.appendChild(script);
        } catch (e) {
            reject(e);
        }
    });
    return Spaarke.SubgridRollup._msalLoadPromise;
};

/**
 * Fetch the anonymous MSAL bootstrap config from the BFF (cached module-level).
 * @param {string} apiBaseUrl - BFF API base URL.
 * @returns {Promise<{clientId:string, authority:string, scopes:string[]}>}
 */
Spaarke.SubgridRollup._getMsalConfig = function (apiBaseUrl) {
    if (Spaarke.SubgridRollup._msalConfig) {
        return Promise.resolve(Spaarke.SubgridRollup._msalConfig);
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
        Spaarke.SubgridRollup._msalConfig = {
            clientId: cfg.msalClientId,
            authority: cfg.msalAuthority,
            scopes: cfg.msalScopes || []
        };
        return Spaarke.SubgridRollup._msalConfig;
    });
};

/**
 * Resolve a UPN login hint for ssoSilent. Prefers MSAL's own cached account username
 * (authoritative UPN); falls back to Xrm userSettings.userName. Returns undefined if
 * neither yields a value (ssoSilent then relies on the Entra session cookie alone).
 */
Spaarke.SubgridRollup._resolveLoginHint = function (msalInstance) {
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

/**
 * Acquire a BFF access token via MSAL silent SSO. Returns null on any failure (caller
 * skips the API call gracefully — never throws, never triggers an interactive popup).
 *
 * @param {string} apiBaseUrl - BFF API base URL.
 * @returns {Promise<string|null>} access token, or null if acquisition failed.
 */
Spaarke.SubgridRollup._acquireBffToken = function (apiBaseUrl) {
    return Spaarke.SubgridRollup._getMsalConfig(apiBaseUrl).then(function (cfg) {
        return Spaarke.SubgridRollup._loadMsal().then(function (msal) {
            if (!Spaarke.SubgridRollup._msalInstance) {
                Spaarke.SubgridRollup._msalInstance = new msal.PublicClientApplication({
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
            var instance = Spaarke.SubgridRollup._msalInstance;
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
                var loginHint = Spaarke.SubgridRollup._resolveLoginHint(instance);
                var req = loginHint ? { scopes: scopes, loginHint: loginHint } : { scopes: scopes };
                return instance.ssoSilent(req).then(function (result) {
                    return result && result.accessToken ? result.accessToken : null;
                });
                // NOTE: intentionally NO acquireTokenPopup fallback (ADR-028 INV-5).
            });
        });
    }).catch(function (error) {
        console.warn("[SubgridRollup] BFF token acquisition failed (call will be skipped):", error);
        return null;
    });
};

// =============================================================================
// FORM EVENT HANDLER
// =============================================================================

/**
 * OnLoad event handler. Register on any parent entity form.
 *
 * @param {Object} executionContext - Execution context (pass execution context = yes)
 * @param {string} configJson - JSON configuration string passed as event handler parameter
 */
Spaarke.SubgridRollup.onLoad = function (executionContext, configJson) {
    try {
        var formContext = executionContext.getFormContext();

        // Parse configuration. Some Power Apps form designers pre-parse the OnLoad
        // parameter and pass the JSON as an already-parsed object; others pass the
        // raw string. Accept both.
        var config;
        if (configJson && typeof configJson === "object") {
            config = configJson;
        } else {
            try {
                config = JSON.parse(configJson || "{}");
            } catch (parseError) {
                console.error("[SubgridRollup] Invalid config JSON:", configJson, parseError);
                return;
            }
        }

        // Validate required fields
        if (!config.subgridName || !config.apiPathTemplate) {
            console.error("[SubgridRollup] Config must include subgridName and apiPathTemplate");
            return;
        }

        // Apply defaults
        config.refreshDelayMs = config.refreshDelayMs || 1500;
        config.entityPathMap = config.entityPathMap || {};

        // Resolve entity path for API URL
        var entityName = formContext.data.entity.getEntityName();
        var entityPath = config.entityPathMap[entityName] || entityName;

        var key = config.subgridName;

        // Resolve BFF URL from Dataverse Environment Variables (async)
        Spaarke.SubgridRollup._getApiBaseUrl().then(function (apiBaseUrl) {
            // Create instance state (supports multiple subgrids per form)
            Spaarke.SubgridRollup._instances[key] = {
                lastRowCount: -1,
                refreshTimer: null,
                config: config,
                entityPath: entityPath,
                apiBaseUrl: apiBaseUrl
            };

            // Wait for subgrid to render, then attach listener
            Spaarke.SubgridRollup._waitForSubgrid(formContext, key, 0);

            console.log("[SubgridRollup:" + key + "] v" + Spaarke.SubgridRollup._version +
                " initialized for " + entityName + " (" + entityPath + ") → " + apiBaseUrl);
        }).catch(function (error) {
            console.error("[SubgridRollup:" + key + "] Failed to resolve BFF URL from environment variables:", error);
        });
    } catch (error) {
        console.error("[SubgridRollup] Error in onLoad:", error);
    }
};

// =============================================================================
// SUBGRID LISTENER
// =============================================================================

/**
 * Wait for the subgrid control to become available, then attach the listener.
 * Retries up to 10 times with 500ms intervals.
 */
Spaarke.SubgridRollup._waitForSubgrid = function (formContext, key, attempt) {
    var inst = Spaarke.SubgridRollup._instances[key];
    var subgrid = formContext.getControl(inst.config.subgridName);

    if (subgrid) {
        // Capture initial row count
        try {
            var grid = subgrid.getGrid();
            if (grid) {
                inst.lastRowCount = grid.getTotalRecordCount();
            }
        } catch (e) {
            // Grid data may not be loaded yet
        }

        // Attach subgrid OnLoad listener
        subgrid.addOnLoad(function () {
            Spaarke.SubgridRollup._onSubgridChange(formContext, subgrid, key);
        });

        console.log("[SubgridRollup:" + key + "] Listener attached. Rows: " + inst.lastRowCount);
        return;
    }

    if (attempt < 10) {
        setTimeout(function () {
            Spaarke.SubgridRollup._waitForSubgrid(formContext, key, attempt + 1);
        }, 500);
    } else {
        console.warn("[SubgridRollup:" + key + "] Subgrid not found after 10 attempts.");
    }
};

/**
 * Called when the subgrid refreshes. Checks if the row count changed
 * and triggers the API call + form refresh if so.
 */
Spaarke.SubgridRollup._onSubgridChange = function (formContext, subgrid, key) {
    var inst = Spaarke.SubgridRollup._instances[key];
    try {
        var currentCount = -1;
        try {
            currentCount = subgrid.getGrid().getTotalRecordCount();
        } catch (e) {
            return; // Grid in loading state
        }

        // Only act on actual data changes (prevents infinite refresh loops)
        if (currentCount !== inst.lastRowCount && currentCount >= 0) {
            console.log("[SubgridRollup:" + key + "] Rows: " +
                inst.lastRowCount + " → " + currentCount);
            inst.lastRowCount = currentCount;

            var entityId = formContext.data.entity.getId().replace(/[{}]/g, "");
            Spaarke.SubgridRollup._callApiAndRefresh(formContext, key, entityId);
        }
    } catch (error) {
        console.warn("[SubgridRollup:" + key + "] Error in subgrid handler:", error);
    }
};

// =============================================================================
// API CALL + FORM REFRESH
// =============================================================================

/**
 * Call the BFF calculator API, then refresh the form data after a delay.
 * Debounces rapid subgrid events.
 */
Spaarke.SubgridRollup._callApiAndRefresh = function (formContext, key, entityId) {
    var inst = Spaarke.SubgridRollup._instances[key];

    // Debounce: cancel any pending operation
    if (inst.refreshTimer) {
        clearTimeout(inst.refreshTimer);
        inst.refreshTimer = null;
    }

    // Build API URL from template
    var apiUrl = inst.apiBaseUrl +
        inst.config.apiPathTemplate
            .replace("{entityPath}", inst.entityPath)
            .replace("{entityId}", entityId);

    console.log("[SubgridRollup:" + key + "] POST " + apiUrl);

    // Task 023 / FR-09: acquire a BFF access token via @spaarke/auth (MSAL silent SSO) and
    // attach it as a Bearer header. If acquisition fails, skip the call gracefully (the BFF
    // endpoint requires auth; sending an unauthenticated request would just 401).
    Spaarke.SubgridRollup._acquireBffToken(inst.apiBaseUrl).then(function (token) {
        if (!token) {
            console.warn("[SubgridRollup:" + key + "] No BFF token acquired; skipping recalculate call.");
            return;
        }

        fetch(apiUrl, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "Authorization": "Bearer " + token
            }
        }).then(function (response) {
        if (response.ok) {
            console.log("[SubgridRollup:" + key + "] API succeeded. Refreshing in " +
                inst.config.refreshDelayMs + "ms...");

            // Wait for Dataverse to commit, then refresh form data
            inst.refreshTimer = setTimeout(function () {
                inst.refreshTimer = null;
                formContext.data.refresh(false).then(
                    function () {
                        console.log("[SubgridRollup:" + key + "] Form refreshed.");
                    },
                    function (err) {
                        console.warn("[SubgridRollup:" + key + "] Refresh failed:", err);
                    }
                );
            }, inst.config.refreshDelayMs);
        } else {
            console.warn("[SubgridRollup:" + key + "] API returned " + response.status);
        }
        }).catch(function (error) {
            console.warn("[SubgridRollup:" + key + "] API call failed:", error);
        });
    });
};

/* eslint-enable no-undef */
