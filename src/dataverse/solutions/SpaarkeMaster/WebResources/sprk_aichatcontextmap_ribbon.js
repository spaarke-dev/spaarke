/**
 * AI Chat Context Map - Ribbon Command Script
 *
 * PURPOSE: Provides "Refresh Cache" button for the sprk_aichatcontextmap admin form.
 *          Calls DELETE /api/ai/chat/context-mappings/cache to evict cached mappings from Redis.
 * WORKS WITH: sprk_aichatcontextmap entity (AI Chat Context Map)
 * DEPLOYMENT: Ribbon button on entity form and list view command bar
 *
 * @version 1.0.0
 * @namespace Spaarke.Commands.ChatContextMap
 */

// ============================================================================
// CONFIGURATION
// ============================================================================

var SPRK_CHAT_CONTEXT_MAP_CONFIG = {
    // BFF API URL - determined at runtime from environment
    bffApiUrl: null,

    // MSAL Configuration (shared across Spaarke webresources)
    msal: {
        clientId: "b36e9b91-ee7d-46e6-9f6a-376871cc9d54",
        bffAppId: "1e40baad-e065-4aea-a8d4-4b7ab273458c",
        tenantId: "a221a95e-6abc-4434-aecc-e48338a1b2f2",
        get authority() {
            return "https://login.microsoftonline.com/" + this.tenantId;
        },
        get scope() {
            return "api://" + this.bffAppId + "/SDAP.Access";
        },
        redirectUri: "https://spaarkedev1.crm.dynamics.com"
    },

    version: "1.0.0"
};

var SPRK_CHAT_CONTEXT_MAP_LOG = "[Spaarke.ChatContextMap]";

// ============================================================================
// INITIALIZATION
// ============================================================================

/**
 * Determine BFF API URL based on the current Dataverse environment.
 * Falls back to dev if environment is not recognized.
 */
function _sprkChatContextMap_initBffUrl() {
    if (SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl) {
        return; // Already initialized
    }

    try {
        var globalContext = Xrm.Utility.getGlobalContext();
        var clientUrl = globalContext.getClientUrl();

        if (clientUrl.indexOf("spaarkedev1.crm.dynamics.com") !== -1) {
            SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";
        } else if (clientUrl.indexOf("spaarkeuat.crm.dynamics.com") !== -1) {
            SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl = "https://spe-api-uat.azurewebsites.net";
        } else if (clientUrl.indexOf("spaarkeprod.crm.dynamics.com") !== -1) {
            SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl = "https://spe-api-prod.azurewebsites.net";
        } else {
            SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";
        }

        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "BFF API URL:", SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl);
    } catch (error) {
        console.error(SPRK_CHAT_CONTEXT_MAP_LOG, "Init failed:", error);
        SPRK_CHAT_CONTEXT_MAP_CONFIG.bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";
    }
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
        script.src = "https://alcdn.msauth.net/browser/2.38.3/js/msal-browser.min.js";
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
        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "refreshMappings: Starting v1.0.0");
        console.log(SPRK_CHAT_CONTEXT_MAP_LOG, "========================================");

        // Initialize BFF URL
        _sprkChatContextMap_initBffUrl();

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
