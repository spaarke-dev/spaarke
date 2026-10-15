/**
 * AI Chat Context Map - Ribbon Command Script
 *
 * PURPOSE: Provides "Refresh Cache" button for the sprk_aichatcontextmap admin form.
 *          Calls DELETE /api/ai/chat/context-mappings/cache to evict cached mappings from Redis.
 * WORKS WITH: sprk_aichatcontextmap entity (AI Chat Context Map)
 * DEPLOYMENT: Ribbon button on entity form and list view command bar
 *
 * @version 1.1.0 - client id / tenant / redirect / BFF URL come from Dataverse environment
 *                 variables (no hardcoded dev values); scope is user_impersonation (#1453)
 * @namespace Spaarke.Commands.ChatContextMap
 */

// ============================================================================
// CONFIGURATION
// ============================================================================

var SPRK_CHAT_CONTEXT_MAP_CONFIG = {
    // BFF API URL - resolved at runtime from the sprk_BffApiBaseUrl environment variable
    bffApiUrl: null,

    // MSAL Configuration - resolved at runtime from Dataverse environment variables
    // (sprk_MsalClientId, sprk_BffApiAppId, sprk_TenantId) and the Dataverse org URL
    msal: {
        clientId: null,
        bffAppId: null,
        tenantId: null,
        get authority() {
            return "https://login.microsoftonline.com/" + this.tenantId;
        },
        get scope() {
            return "api://" + this.bffAppId + "/user_impersonation";
        },
        redirectUri: null
    },

    version: "1.1.0"
};

var SPRK_CHAT_CONTEXT_MAP_LOG = "[Spaarke.ChatContextMap]";

// ============================================================================
// INITIALIZATION
// ============================================================================

/**
 * Query one Dataverse Environment Variable by schema name (override value first, then default).
 * Same resolution as sprk_DocumentOperations.js / sprk_emailactions.js.
 * @param {string} schemaName
 * @returns {Promise<string|null>}
 */
function _sprkChatContextMap_getEnvVar(schemaName) {
    return Xrm.WebApi.retrieveMultipleRecords(
        "environmentvariabledefinition",
        "?$filter=schemaname eq '" + schemaName + "'" +
        "&$select=environmentvariabledefinitionid,defaultvalue" +
        "&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)"
    ).then(function (result) {
        if (result.entities && result.entities.length > 0) {
            var definition = result.entities[0];
            var values = definition.environmentvariabledefinition_environmentvariablevalue;
            if (values && values.length > 0 && values[0].value) {
                return values[0].value;
            }
            if (definition.defaultvalue) {
                return definition.defaultvalue;
            }
        }
        return null;
    });
}

/**
 * A usable single-tenant identifier: a GUID or a dotted domain. Rejects "organizations",
 * "common", "undefined", "null" and "" (the values that sign a B2B guest in against their
 * HOME tenant or build a malformed authority - #1453).
 * @param {*} value
 * @returns {boolean}
 */
function _sprkChatContextMap_isValidTenant(value) {
    if (typeof value !== "string") return false;
    var v = value.trim();
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) ||
        /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i.test(v);
}

var _sprkChatContextMap_configPromise = null;

/**
 * Resolve the BFF URL, MSAL client id, BFF app id and tenant from Dataverse Environment
 * Variables (sprk_BffApiBaseUrl, sprk_MsalClientId, sprk_BffApiAppId, sprk_TenantId) and the
 * redirect URI from the Dataverse org URL. No environment is hardcoded. Cached; a failure is
 * not cached so the next click retries.
 * @returns {Promise<void>}
 */
function _sprkChatContextMap_resolveConfig() {
    if (_sprkChatContextMap_configPromise) {
        return _sprkChatContextMap_configPromise;
    }

    var cfg = SPRK_CHAT_CONTEXT_MAP_CONFIG;
    _sprkChatContextMap_configPromise = Promise.all([
        _sprkChatContextMap_getEnvVar("sprk_BffApiBaseUrl"),
        _sprkChatContextMap_getEnvVar("sprk_MsalClientId"),
        _sprkChatContextMap_getEnvVar("sprk_BffApiAppId"),
        _sprkChatContextMap_getEnvVar("sprk_TenantId")
    ]).then(function (v) {
        var missing = [];
        if (!v[0]) missing.push("sprk_BffApiBaseUrl");
        if (!v[1]) missing.push("sprk_MsalClientId");
        if (!v[2]) missing.push("sprk_BffApiAppId");
        if (!_sprkChatContextMap_isValidTenant(v[3])) missing.push("sprk_TenantId (a tenant GUID or domain)");
        if (missing.length > 0) {
            throw new Error("Missing Dataverse environment variable(s): " + missing.join(", "));
        }

        // HOST ONLY: strip trailing slashes and a trailing /api (paths add /api themselves).
        cfg.bffApiUrl = v[0].replace(/\/+$/, "").replace(/\/api$/i, "");
        cfg.msal.clientId = v[1];
        cfg.msal.bffAppId = v[2];
        cfg.msal.tenantId = v[3].trim();
        cfg.msal.redirectUri = Xrm.Utility.getGlobalContext().getClientUrl().replace(/\/+$/, "");

        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "BFF API URL:", cfg.bffApiUrl);
    }).catch(function (error) {
        _sprkChatContextMap_configPromise = null;
        console.error(SPRK_CHAT_CONTEXT_MAP_LOG, "Config resolution failed:", error);
        throw error;
    });

    return _sprkChatContextMap_configPromise;
}

// ============================================================================
// MSAL AUTHENTICATION
// ============================================================================

var _sprkChatContextMap_msalInstance = null;
var _sprkChatContextMap_msalInitPromise = null;
var _sprkChatContextMap_currentAccount = null;

/**
 * Load MSAL library from CDN if not already available.
 * @returns {Promise<void>}
 */
function _sprkChatContextMap_loadMsal() {
    return new Promise(function (resolve, reject) {
        if (window.msal && window.msal.PublicClientApplication) {
            resolve();
            return;
        }

        var script = document.createElement("script");
        script.src = "https://alcdn.msauth.net/browser/2.38.0/js/msal-browser.min.js";
        script.crossOrigin = "anonymous";
        script.onload = function () {
            console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "MSAL library loaded");
            resolve();
        };
        script.onerror = function () {
            reject(new Error("Failed to load MSAL library"));
        };
        document.head.appendChild(script);
    });
}

/**
 * Initialize MSAL and return the client application instance.
 * @returns {Promise<msal.PublicClientApplication>}
 */
function _sprkChatContextMap_initMsal() {
    if (_sprkChatContextMap_msalInitPromise) {
        return _sprkChatContextMap_msalInitPromise;
    }

    _sprkChatContextMap_msalInitPromise = _sprkChatContextMap_loadMsal()
        .then(function () {
            var tenantId = SPRK_CHAT_CONTEXT_MAP_CONFIG.msal.tenantId;
            var authorityMetadataJson = JSON.stringify({
                "authorization_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/authorize",
                "token_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/token",
                "issuer": "https://login.microsoftonline.com/" + tenantId + "/v2.0"
            });

            var msalConfig = {
                auth: {
                    clientId: SPRK_CHAT_CONTEXT_MAP_CONFIG.msal.clientId,
                    authority: SPRK_CHAT_CONTEXT_MAP_CONFIG.msal.authority,
                    redirectUri: SPRK_CHAT_CONTEXT_MAP_CONFIG.msal.redirectUri,
                    knownAuthorities: ["login.microsoftonline.com"],
                    authorityMetadata: authorityMetadataJson
                },
                cache: {
                    cacheLocation: "sessionStorage",
                    storeAuthStateInCookie: false
                }
            };

            _sprkChatContextMap_msalInstance = new msal.PublicClientApplication(msalConfig);
            console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "MSAL initialized");
            return _sprkChatContextMap_msalInstance;
        });

    return _sprkChatContextMap_msalInitPromise;
}

/**
 * Acquire an access token for the BFF API.
 * Tries silent acquisition first, then falls back to popup.
 * @returns {Promise<string>} Access token
 */
async function _sprkChatContextMap_getAccessToken() {
    await _sprkChatContextMap_resolveConfig();
    var msalInstance = await _sprkChatContextMap_initMsal();
    var scope = SPRK_CHAT_CONTEXT_MAP_CONFIG.msal.scope;

    // Try silent token acquisition
    if (_sprkChatContextMap_currentAccount) {
        try {
            var silentResponse = await msalInstance.acquireTokenSilent({
                scopes: [scope],
                account: _sprkChatContextMap_currentAccount
            });
            return silentResponse.accessToken;
        } catch (silentError) {
            console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "Silent token failed, trying SSO...");
        }
    }

    // Try SSO silent
    try {
        var ssoResponse = await msalInstance.ssoSilent({ scopes: [scope] });
        if (ssoResponse.account) {
            _sprkChatContextMap_currentAccount = ssoResponse.account;
        }
        return ssoResponse.accessToken;
    } catch (ssoError) {
        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "SSO silent failed, using popup...");
    }

    // Fallback to popup
    var popupResponse = await msalInstance.acquireTokenPopup({ scopes: [scope] });
    if (popupResponse.account) {
        _sprkChatContextMap_currentAccount = popupResponse.account;
    }
    return popupResponse.accessToken;
}

// ============================================================================
// MAIN COMMAND FUNCTION
// ============================================================================

/**
 * Refresh Mappings Cache - called by ribbon button.
 * Calls DELETE /api/ai/chat/context-mappings/cache to evict all cached
 * context mappings from Redis, forcing a reload from Dataverse on next request.
 */
async function refreshMappings() {
    try {
        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "========================================");
        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "refreshMappings: Starting v" + SPRK_CHAT_CONTEXT_MAP_CONFIG.version);
        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "========================================");

        // Resolve config (BFF URL, MSAL client / tenant) from Dataverse environment variables
        await _sprkChatContextMap_resolveConfig();

        // Show progress
        Xrm.Utility.showProgressIndicator("Refreshing context mapping cache...");

        // Get auth token
        var token = await _sprkChatContextMap_getAccessToken();

        // Call DELETE endpoint
        var url = SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl + "/api/ai/chat/context-mappings/cache";
        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "Calling:", url);

        var response = await fetch(url, {
            method: "DELETE",
            headers: {
                "Authorization": "Bearer " + token,
                "Content-Type": "application/json"
            }
        });

        // Close progress
        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "Cache evicted successfully");

            // Show success notification
            Xrm.App.addGlobalNotification({
                type: 2, // Success
                level: 1, // Success level
                message: "Context mapping cache has been refreshed. New mappings will be loaded from Dataverse on next request.",
                showCloseButton: true,
                priority: 1
            }).then(function (notificationId) {
                // Auto-dismiss after 5 seconds
                setTimeout(function () {
                    Xrm.App.clearGlobalNotification(notificationId);
                }, 5000);
            });
        } else {
            var errorText = "";
            try {
                var errorBody = await response.json();
                errorText = errorBody.title || errorBody.detail || response.statusText;
            } catch (_) {
                errorText = response.status + " " + response.statusText;
            }

            console.error(SPRK_CHAT_CONTEXT_MAP_LOG, "Cache eviction failed:", errorText);

            Xrm.Navigation.openAlertDialog({
                title: "Cache Refresh Failed",
                text: "Failed to refresh context mapping cache: " + errorText
            });
        }
    } catch (error) {
        // Close progress on error
        try { Xrm.Utility.closeProgressIndicator(); } catch (_) { /* ignore */ }

        console.error(SPRK_CHAT_CONTEXT_MAP_LOG, "refreshMappings error:", error);

        Xrm.Navigation.openAlertDialog({
            title: "Cache Refresh Error",
            text: "An unexpected error occurred while refreshing the cache: " + (error.message || "Unknown error")
        });
    }
}

// ============================================================================
// ENABLE/VISIBILITY RULES
// ============================================================================

/**
 * Enable rule: Always enabled (admin form only, no conditional logic needed)
 * @returns {boolean}
 */
function Spaarke_EnableRefreshMappings() {
    return true;
}
