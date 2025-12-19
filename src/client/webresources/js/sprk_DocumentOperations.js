/**
 * Spaarke Document Operations
 * Version: 1.18.0
 * Description: Document checkout/checkin operations via BFF API with MSAL authentication
 *
 * ADR-006 Exception: Approved for ribbon button invocation
 *
 * Dependencies: MSAL.js (loaded from CDN)
 *
 * Copyright (c) 2025 Spaarke
 */

"use strict";

// Namespace declaration
if (typeof window !== 'undefined') {
    window.Spaarke = window.Spaarke || {};
    window.Spaarke.Document = window.Spaarke.Document || {};
}

var Spaarke = window.Spaarke;

// =============================================================================
// CONFIGURATION
// =============================================================================

Spaarke.Document.Config = {
    // BFF API URL - determined by environment
    bffApiUrl: null,

    // MSAL Configuration
    msal: {
        // Client Application ID (SPE-File-Viewer-PCF app registration)
        clientId: "b36e9b91-ee7d-46e6-9f6a-376871cc9d54",
        // BFF Application ID (SDAP-BFF-SPE-API for scope construction)
        bffAppId: "1e40baad-e065-4aea-a8d4-4b7ab273458c",
        // Azure AD Tenant ID
        tenantId: "a221a95e-6abc-4434-aecc-e48338a1b2f2",
        // Authority URL
        get authority() {
            return "https://login.microsoftonline.com/" + this.tenantId;
        },
        // BFF API scope - CRITICAL: Use named scope, NOT .default or user_impersonation
        get scope() {
            return "api://" + this.bffAppId + "/SDAP.Access";
        },
        // Redirect URI - CRITICAL: Must be static and match Azure AD app registration
        // This must match the Dataverse org URL, NOT window.location.origin
        redirectUri: "https://spaarkedev1.crm.dynamics.com"
    },

    // Version
    version: "1.18.0"
};

// =============================================================================
// INITIALIZATION
// =============================================================================

/**
 * Initialize the module
 * Determines BFF API URL based on environment
 */
Spaarke.Document.init = function() {
    try {
        // Determine environment from Dataverse URL
        var globalContext = Xrm.Utility.getGlobalContext();
        var clientUrl = globalContext.getClientUrl();

        if (clientUrl.includes('spaarkedev1.crm.dynamics.com')) {
            Spaarke.Document.Config.bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";
        } else if (clientUrl.includes('spaarkeuat.crm.dynamics.com')) {
            Spaarke.Document.Config.bffApiUrl = "https://spe-api-uat.azurewebsites.net";
        } else if (clientUrl.includes('spaarkeprod.crm.dynamics.com')) {
            Spaarke.Document.Config.bffApiUrl = "https://spe-api-prod.azurewebsites.net";
        } else {
            // Default to dev
            Spaarke.Document.Config.bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";
        }

        console.log("[Spaarke.Document] Initialized v" + Spaarke.Document.Config.version);
        console.log("[Spaarke.Document] BFF API URL:", Spaarke.Document.Config.bffApiUrl);

        return true;
    } catch (error) {
        console.error("[Spaarke.Document] Init failed:", error);
        return false;
    }
};

// =============================================================================
// MSAL AUTHENTICATION
// =============================================================================

/**
 * MSAL instance (lazy initialized)
 */
Spaarke.Document._msalInstance = null;
Spaarke.Document._msalInitPromise = null;

/**
 * Load MSAL library from CDN if not already loaded
 * @returns {Promise<void>}
 */
Spaarke.Document._loadMsalLibrary = function() {
    return new Promise(function(resolve, reject) {
        // Check if already loaded
        if (window.msal && window.msal.PublicClientApplication) {
            resolve();
            return;
        }

        // Load from CDN
        var script = document.createElement('script');
        script.src = 'https://alcdn.msauth.net/browser/2.38.0/js/msal-browser.min.js';
        script.onload = function() {
            console.log("[Spaarke.Document] MSAL library loaded");
            resolve();
        };
        script.onerror = function() {
            reject(new Error("Failed to load MSAL library"));
        };
        document.head.appendChild(script);
    });
};

/**
 * Initialize MSAL instance
 * CRITICAL: Must call initialize() before using MSAL in v3+
 * @returns {Promise<msal.PublicClientApplication>}
 */
Spaarke.Document._initMsal = function() {
    if (Spaarke.Document._msalInitPromise) {
        return Spaarke.Document._msalInitPromise;
    }

    Spaarke.Document._msalInitPromise = Spaarke.Document._loadMsalLibrary()
        .then(function() {
            var tenantId = Spaarke.Document.Config.msal.tenantId;

            // Pre-build authority metadata to skip endpoint discovery
            // This avoids the openid-configuration fetch that fails in Dataverse iframe context
            var authorityMetadataJson = JSON.stringify({
                "authorization_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/authorize",
                "token_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/token",
                "issuer": "https://login.microsoftonline.com/" + tenantId + "/v2.0",
                "jwks_uri": "https://login.microsoftonline.com/" + tenantId + "/discovery/v2.0/keys",
                "end_session_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/logout"
            });

            var config = {
                auth: {
                    clientId: Spaarke.Document.Config.msal.clientId,
                    authority: Spaarke.Document.Config.msal.authority,
                    // CRITICAL: Static redirect URI matching Azure AD app registration
                    redirectUri: Spaarke.Document.Config.msal.redirectUri,
                    navigateToLoginRequestUrl: false,
                    // Provide known authorities to avoid endpoint discovery issues
                    knownAuthorities: ["login.microsoftonline.com"],
                    // CRITICAL: Provide metadata directly to skip discovery fetch
                    authorityMetadata: authorityMetadataJson
                },
                cache: {
                    cacheLocation: "sessionStorage",
                    storeAuthStateInCookie: false
                },
                system: {
                    loggerOptions: {
                        loggerCallback: function(level, message, containsPii) {
                            if (containsPii) return;
                            console.log("[MSAL] " + message);
                        },
                        logLevel: 3 // Warning level
                    }
                }
            };

            Spaarke.Document._msalInstance = new msal.PublicClientApplication(config);
            console.log("[Spaarke.Document] MSAL PublicClientApplication created");

            // CRITICAL: Must call initialize() in MSAL v3+
            // Note: MSAL 2.x from CDN may not have this, so check if method exists
            if (typeof Spaarke.Document._msalInstance.initialize === 'function') {
                return Spaarke.Document._msalInstance.initialize().then(function() {
                    console.log("[Spaarke.Document] MSAL initialized");
                    return Spaarke.Document._msalInstance.handleRedirectPromise();
                });
            } else {
                // MSAL 2.x doesn't require initialize()
                return Spaarke.Document._msalInstance.handleRedirectPromise();
            }
        })
        .then(function(redirectResponse) {
            if (redirectResponse) {
                console.log("[Spaarke.Document] Redirect response processed");
                Spaarke.Document._currentAccount = redirectResponse.account;
            } else {
                // Check for existing accounts
                var accounts = Spaarke.Document._msalInstance.getAllAccounts();
                if (accounts.length > 0) {
                    Spaarke.Document._currentAccount = accounts[0];
                    console.log("[Spaarke.Document] Existing account found: " + accounts[0].username);
                }
            }
            return Spaarke.Document._msalInstance;
        });

    return Spaarke.Document._msalInitPromise;
};

/**
 * Current account cache
 */
Spaarke.Document._currentAccount = null;

/**
 * Get access token for BFF API
 * Uses SSO silent flow with popup fallback
 * Flow:
 * 1. Try acquireTokenSilent with cached account (fastest)
 * 2. Try ssoSilent (discover account from browser session)
 * 3. Fall back to popup (for consent/MFA)
 * @returns {Promise<string>} Access token
 */
Spaarke.Document.getAccessToken = async function() {
    var msalInstance = await Spaarke.Document._initMsal();
    var scope = Spaarke.Document.Config.msal.scope;

    try {
        // Step 1: Try acquireTokenSilent with cached account (fastest path)
        if (Spaarke.Document._currentAccount) {
            console.log("[Spaarke.Document] Attempting acquireTokenSilent with cached account...");
            try {
                var silentRequest = {
                    scopes: [scope],
                    account: Spaarke.Document._currentAccount
                };
                var silentResponse = await msalInstance.acquireTokenSilent(silentRequest);
                console.log("[Spaarke.Document] acquireTokenSilent succeeded");
                return silentResponse.accessToken;
            } catch (silentError) {
                console.log("[Spaarke.Document] acquireTokenSilent failed, trying ssoSilent...");
                // Fall through to ssoSilent
            }
        }

        // Step 2: Try ssoSilent (discover account from browser session)
        console.log("[Spaarke.Document] Attempting ssoSilent authentication...");
        try {
            var ssoRequest = {
                scopes: [scope]
            };
            var ssoResponse = await msalInstance.ssoSilent(ssoRequest);
            console.log("[Spaarke.Document] ssoSilent succeeded");
            // Update cached account
            if (ssoResponse.account) {
                Spaarke.Document._currentAccount = ssoResponse.account;
            }
            return ssoResponse.accessToken;
        } catch (ssoError) {
            console.log("[Spaarke.Document] ssoSilent failed:", ssoError.message);
            // Fall through to popup
        }

        // Step 3: Fall back to popup (requires user interaction)
        console.log("[Spaarke.Document] Falling back to popup authentication...");
        var popupRequest = {
            scopes: [scope],
            loginHint: Spaarke.Document._currentAccount ? Spaarke.Document._currentAccount.username : undefined
        };
        var popupResponse = await msalInstance.acquireTokenPopup(popupRequest);
        console.log("[Spaarke.Document] Popup authentication succeeded");
        // Update cached account
        if (popupResponse.account) {
            Spaarke.Document._currentAccount = popupResponse.account;
        }
        return popupResponse.accessToken;

    } catch (error) {
        console.error("[Spaarke.Document] Token acquisition failed:", error);

        // Handle specific popup errors
        if (error.message && error.message.includes('popup_window_error')) {
            throw new Error("Popup blocked. Please allow popups for this site and try again.");
        }
        if (error.message && error.message.includes('user_cancelled')) {
            throw new Error("Authentication cancelled by user");
        }

        throw new Error("Authentication failed: " + (error.message || "Unknown error"));
    }
};

// =============================================================================
// UTILITY FUNCTIONS
// =============================================================================

Spaarke.Document.Utils = {
    /**
     * Generate a new GUID
     * @returns {string} GUID
     */
    newGuid: function() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            var r = Math.random() * 16 | 0;
            var v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    },

    /**
     * Get document info from form context
     * @param {object} formContext - Form context
     * @returns {object} Document info
     */
    getDocumentInfo: function(formContext) {
        var documentId = formContext.data.entity.getId().replace(/[{}]/g, "");
        var nameAttr = formContext.getAttribute("sprk_documentname") || formContext.getAttribute("sprk_name");
        var documentName = nameAttr ? nameAttr.getValue() : "this document";

        return {
            id: documentId,
            name: documentName
        };
    }
};

// =============================================================================
// CHECKOUT STATUS CACHE
// =============================================================================

/**
 * Cached checkout status for enable rule evaluation
 * Key: documentId, Value: { isCheckedOut, isCheckedOutByMe, checkedOutBy, timestamp }
 */
Spaarke.Document._checkoutStatusCache = {};

/**
 * Get checkout status (cached for 30 seconds)
 * @param {string} documentId - Document ID
 * @returns {Promise<object>} Checkout status
 */
Spaarke.Document.getCheckoutStatus = async function(documentId) {
    var cached = Spaarke.Document._checkoutStatusCache[documentId];
    var now = Date.now();

    // Return cached if less than 30 seconds old
    if (cached && (now - cached.timestamp) < 30000) {
        return cached;
    }

    // Initialize if needed
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var token = await Spaarke.Document.getAccessToken();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + documentId + "/checkout-status",
            {
                method: "GET",
                headers: {
                    "Authorization": "Bearer " + token,
                    "Content-Type": "application/json"
                }
            }
        );

        if (response.ok) {
            var data = await response.json();
            var status = {
                isCheckedOut: data.isCheckedOut || false,
                isCheckedOutByMe: data.isCheckedOutByCurrentUser || false,
                checkedOutBy: data.checkedOutBy || null,
                timestamp: now
            };
            Spaarke.Document._checkoutStatusCache[documentId] = status;
            return status;
        }
    } catch (error) {
        console.error("[Spaarke.Document] Failed to get checkout status:", error);
    }

    // Default to not checked out if we can't determine
    return { isCheckedOut: false, isCheckedOutByMe: false, checkedOutBy: null, timestamp: now };
};

/**
 * Clear checkout status cache for a document
 * @param {string} documentId - Document ID
 */
Spaarke.Document.clearCheckoutStatusCache = function(documentId) {
    delete Spaarke.Document._checkoutStatusCache[documentId];
};

/**
 * Pending refresh info - stored at window level for persistence across async contexts
 */
Spaarke.Document._pendingRefresh = null;

/**
 * Refresh form data and ribbon after an operation
 * Uses formContext.data.refresh with fallback to page navigation
 * @param {object} formContext - Form context
 * @param {string} documentId - Document ID for navigation fallback
 */
Spaarke.Document.refreshForm = function(formContext, documentId) {
    console.log("[Spaarke.Document] >>> refreshForm ENTERED for document:", documentId);
    console.log("[Spaarke.Document] formContext valid:", !!formContext);
    console.log("[Spaarke.Document] formContext.data valid:", !!(formContext && formContext.data));

    try {
        if (!formContext || !formContext.data) {
            console.error("[Spaarke.Document] formContext is invalid!");
            // Try navigating directly as fallback
            console.log("[Spaarke.Document] Using navigation fallback...");
            Xrm.Navigation.navigateTo({
                pageType: "entityrecord",
                entityName: "sprk_document",
                entityId: documentId
            });
            return;
        }

        // Try standard refresh first (synchronous call, returns Promise)
        console.log("[Spaarke.Document] Calling formContext.data.refresh(true)...");
        formContext.data.refresh(true).then(function() {
            console.log("[Spaarke.Document] formContext.data.refresh() SUCCEEDED");
            // Refresh ribbon
            console.log("[Spaarke.Document] Calling formContext.ui.refreshRibbon()...");
            formContext.ui.refreshRibbon();
            console.log("[Spaarke.Document] Ribbon refresh completed!");
        }).catch(function(refreshError) {
            console.warn("[Spaarke.Document] formContext.data.refresh() FAILED:", refreshError);
            console.log("[Spaarke.Document] Falling back to page navigation...");
            Xrm.Navigation.navigateTo({
                pageType: "entityrecord",
                entityName: "sprk_document",
                entityId: documentId
            });
        });

    } catch (syncError) {
        console.error("[Spaarke.Document] refreshForm threw synchronously:", syncError);
        // Last resort fallback
        try {
            Xrm.Navigation.navigateTo({
                pageType: "entityrecord",
                entityName: "sprk_document",
                entityId: documentId
            });
        } catch (navError) {
            console.error("[Spaarke.Document] Navigation also failed:", navError);
        }
    }
};

// =============================================================================
// CHECKOUT DOCUMENT
// =============================================================================

/**
 * Check out document - called from ribbon button
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.checkoutDocument = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Checkout requested for:", docInfo.id, docInfo.name);

        // Show progress
        Xrm.Utility.showProgressIndicator("Checking out document...");

        // Get access token
        var token = await Spaarke.Document.getAccessToken();

        // Call BFF API to checkout
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + docInfo.id + "/checkout",
            {
                method: "POST",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                }
            }
        );

        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            console.log("[Spaarke.Document] Document checked out successfully");

            // Clear cache
            Spaarke.Document.clearCheckoutStatusCache(docInfo.id);

            // v1.18.0: Show dialog, then navigate to refresh page
            var capturedDocId = docInfo.id;
            console.log("[Spaarke.Document] Showing success dialog...");

            await Xrm.Navigation.openAlertDialog({
                text: "Document checked out successfully. You can now edit the document.",
                confirmButtonLabel: "OK"
            });

            console.log("[Spaarke.Document] Dialog closed, navigating to refresh page...");
            Xrm.Navigation.navigateTo({
                pageType: "entityrecord",
                entityName: "sprk_document",
                entityId: capturedDocId
            });

        } else {
            var errorData;
            var responseText = "";
            try {
                responseText = await response.text();
                console.error("[Spaarke.Document] Checkout failed with status:", response.status);
                console.error("[Spaarke.Document] Response body:", responseText);
                errorData = JSON.parse(responseText);
            } catch (e) {
                console.error("[Spaarke.Document] Failed to parse error response:", e);
                errorData = { detail: responseText || "Unknown error occurred" };
            }

            if (response.status === 409) {
                // Already checked out
                var lockedMessage = "This document is already checked out";
                if (errorData.checkedOutBy) {
                    lockedMessage += " by " + errorData.checkedOutBy.name;
                }
                lockedMessage += ".";

                await Xrm.Navigation.openErrorDialog({
                    message: lockedMessage
                });
            } else {
                var errorMessage = errorData.detail || errorData.title || "Failed to check out document";
                await Xrm.Navigation.openErrorDialog({
                    message: errorMessage
                });
            }
        }

    } catch (error) {
        console.error("[Spaarke.Document] Checkout error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to check out document: " + error.message
        });
    }
};

// =============================================================================
// CHECKIN DOCUMENT
// =============================================================================

/**
 * Check in document - called from ribbon button
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.checkinDocument = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Checkin requested for:", docInfo.id, docInfo.name);

        // Prompt for comment (optional)
        var commentResult = await Xrm.Navigation.openDialog("sprk_CheckinCommentDialog", {
            height: 250,
            width: 450,
            position: 1
        }, {}).catch(function() {
            // Dialog cancelled or not found - use simple prompt
            return null;
        });

        var comment = "";
        if (commentResult && commentResult.parameters && commentResult.parameters.comment) {
            comment = commentResult.parameters.comment;
        } else {
            // Fallback: Use confirm dialog with implied empty comment
            var confirmResult = await Xrm.Navigation.openConfirmDialog({
                title: "Check In Document",
                text: "Check in \"" + docInfo.name + "\"?\n\nThis will save your changes and make the document available for others to edit.",
                confirmButtonLabel: "Check In",
                cancelButtonLabel: "Cancel"
            });

            if (!confirmResult.confirmed) {
                console.log("[Spaarke.Document] Checkin cancelled by user");
                return;
            }
        }

        // Show progress
        Xrm.Utility.showProgressIndicator("Checking in document...");

        // Get access token
        var token = await Spaarke.Document.getAccessToken();

        // Call BFF API to checkin
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + docInfo.id + "/checkin",
            {
                method: "POST",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    comment: comment
                })
            }
        );

        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            console.log("[Spaarke.Document] Document checked in successfully");

            // Clear cache
            Spaarke.Document.clearCheckoutStatusCache(docInfo.id);

            // v1.18.0: Show dialog, then navigate to refresh page
            var capturedDocId = docInfo.id;
            console.log("[Spaarke.Document] Showing success dialog...");

            await Xrm.Navigation.openAlertDialog({
                text: "Document checked in successfully.",
                confirmButtonLabel: "OK"
            });

            console.log("[Spaarke.Document] Dialog closed, navigating to refresh page...");
            Xrm.Navigation.navigateTo({
                pageType: "entityrecord",
                entityName: "sprk_document",
                entityId: capturedDocId
            });

        } else {
            var errorData;
            try {
                errorData = await response.json();
            } catch (e) {
                errorData = { detail: "Unknown error occurred" };
            }

            var errorMessage = errorData.detail || errorData.title || "Failed to check in document";
            await Xrm.Navigation.openErrorDialog({
                message: errorMessage
            });
        }

    } catch (error) {
        console.error("[Spaarke.Document] Checkin error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to check in document: " + error.message
        });
    }
};

// =============================================================================
// DISCARD CHECKOUT
// =============================================================================

/**
 * Discard checkout - called from ribbon button
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.discardCheckout = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Discard requested for:", docInfo.id, docInfo.name);

        // Confirm discard
        var confirmResult = await Xrm.Navigation.openConfirmDialog({
            title: "Discard Checkout?",
            text: "Discard checkout for \"" + docInfo.name + "\"?\n\nAny unsaved changes will be lost.",
            confirmButtonLabel: "Discard",
            cancelButtonLabel: "Cancel"
        });

        if (!confirmResult.confirmed) {
            console.log("[Spaarke.Document] Discard cancelled by user");
            return;
        }

        // Show progress
        Xrm.Utility.showProgressIndicator("Discarding checkout...");

        // Get access token
        var token = await Spaarke.Document.getAccessToken();

        // Call BFF API to discard
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + docInfo.id + "/discard",
            {
                method: "POST",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                }
            }
        );

        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            console.log("[Spaarke.Document] Checkout discarded successfully");

            // Clear cache
            Spaarke.Document.clearCheckoutStatusCache(docInfo.id);

            // v1.18.0: Show dialog, then navigate to refresh page
            var capturedDocId = docInfo.id;
            console.log("[Spaarke.Document] Showing success dialog...");

            await Xrm.Navigation.openAlertDialog({
                text: "Checkout discarded. The document has been restored to its previous state.",
                confirmButtonLabel: "OK"
            });

            console.log("[Spaarke.Document] Dialog closed, navigating to refresh page...");
            Xrm.Navigation.navigateTo({
                pageType: "entityrecord",
                entityName: "sprk_document",
                entityId: capturedDocId
            });

        } else {
            var errorData;
            try {
                errorData = await response.json();
            } catch (e) {
                errorData = { detail: "Unknown error occurred" };
            }

            var errorMessage = errorData.detail || errorData.title || "Failed to discard checkout";
            await Xrm.Navigation.openErrorDialog({
                message: errorMessage
            });
        }

    } catch (error) {
        console.error("[Spaarke.Document] Discard error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to discard checkout: " + error.message
        });
    }
};

// =============================================================================
// DELETE DOCUMENT
// =============================================================================

/**
 * Delete document - called from ribbon button
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.deleteDocument = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Delete requested for:", docInfo.id, docInfo.name);

        // Show confirmation dialog
        var confirmResult = await Xrm.Navigation.openConfirmDialog({
            title: "Delete Document?",
            text: "This will permanently delete \"" + docInfo.name + "\" and its file from storage.\n\nThis action cannot be undone.",
            confirmButtonLabel: "Delete",
            cancelButtonLabel: "Cancel"
        });

        if (!confirmResult.confirmed) {
            console.log("[Spaarke.Document] Delete cancelled by user");
            return;
        }

        // Show progress
        Xrm.Utility.showProgressIndicator("Deleting document...");

        // Get access token
        var token = await Spaarke.Document.getAccessToken();

        // Call BFF API to delete
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + docInfo.id,
            {
                method: "DELETE",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                }
            }
        );

        // Handle response
        if (response.ok) {
            // Success - navigate to document grid
            Xrm.Utility.closeProgressIndicator();

            console.log("[Spaarke.Document] Document deleted successfully");

            // Show brief success message then navigate
            await Xrm.Navigation.openAlertDialog({
                text: "Document deleted successfully.",
                confirmButtonLabel: "OK"
            });

            // Navigate to document list
            Xrm.Navigation.navigateTo({
                pageType: "entitylist",
                entityName: "sprk_document"
            });

        } else {
            // Handle error response
            var errorData;
            try {
                errorData = await response.json();
            } catch (e) {
                errorData = { detail: "Unknown error occurred" };
            }

            Xrm.Utility.closeProgressIndicator();

            // Check for specific error types
            if (response.status === 409) {
                // Document is locked (checked out)
                var lockedMessage = "This document is currently checked out and cannot be deleted.\n\n";
                if (errorData.checkedOutBy) {
                    lockedMessage += "Checked out by: " + errorData.checkedOutBy.name;
                } else {
                    lockedMessage += "Please wait for it to be checked in.";
                }

                await Xrm.Navigation.openErrorDialog({
                    message: lockedMessage
                });

            } else if (response.status === 404) {
                // Document not found
                await Xrm.Navigation.openErrorDialog({
                    message: "Document not found. It may have already been deleted."
                });

                // Still navigate away since document doesn't exist
                Xrm.Navigation.navigateTo({
                    pageType: "entitylist",
                    entityName: "sprk_document"
                });

            } else {
                // Other error
                var errorMessage = errorData.detail || errorData.title || "Failed to delete document";
                await Xrm.Navigation.openErrorDialog({
                    message: errorMessage
                });
            }
        }

    } catch (error) {
        console.error("[Spaarke.Document] Delete error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to delete document: " + error.message
        });
    }
};

// =============================================================================
// ENABLE RULES (for ribbon button visibility)
// =============================================================================

/**
 * Enable/Display rule for Checkout button
 * Returns true if document is NOT checked out
 * Note: For ribbon DisplayRules/EnableRules, primaryControl may not be provided.
 *       Falls back to Xrm.Page for backward compatibility.
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canCheckout = function(primaryControl) {
    // Initialize on ribbon load
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    // For synchronous enable rules, check form attribute
    try {
        // Use provided context or fall back to Xrm.Page (deprecated but needed for ribbon rules)
        var formContext = primaryControl;
        if (!formContext || !formContext.getAttribute) {
            // Fallback for ribbon DisplayRules that don't pass context
            if (typeof Xrm !== 'undefined' && Xrm.Page && Xrm.Page.getAttribute) {
                formContext = Xrm.Page;
            }
        }

        if (formContext && formContext.getAttribute) {
            // Check sprk_checkedoutby lookup - if it has a value, document is checked out
            var checkedOutByAttr = formContext.getAttribute("sprk_checkedoutby");
            if (checkedOutByAttr) {
                var checkedOutBy = checkedOutByAttr.getValue();
                var isCheckedOut = checkedOutBy && checkedOutBy.length > 0;
                console.log("[Spaarke.Document] canCheckout - sprk_checkedoutby has value:", isCheckedOut);
                // Can checkout only if NOT currently checked out
                return !isCheckedOut;
            } else {
                console.log("[Spaarke.Document] canCheckout - sprk_checkedoutby attribute not found on form");
            }
        } else {
            console.log("[Spaarke.Document] canCheckout - No form context available");
        }
    } catch (e) {
        console.log("[Spaarke.Document] canCheckout error:", e);
    }

    // Default to enabled (show Check Out button)
    return true;
};

/**
 * Enable/Display rule for Checkin button
 * Returns true if document is checked out BY CURRENT USER
 * Note: For ribbon DisplayRules/EnableRules, primaryControl may not be provided.
 *       Falls back to Xrm.Page for backward compatibility.
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canCheckin = function(primaryControl) {
    // Initialize on ribbon load
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        // Use provided context or fall back to Xrm.Page (deprecated but needed for ribbon rules)
        var formContext = primaryControl;
        if (!formContext || !formContext.getAttribute) {
            // Fallback for ribbon DisplayRules that don't pass context
            if (typeof Xrm !== 'undefined' && Xrm.Page && Xrm.Page.getAttribute) {
                formContext = Xrm.Page;
            }
        }

        if (formContext && formContext.getAttribute) {
            // Check sprk_checkedoutby lookup - if it has a value, document is checked out
            var checkedOutByAttr = formContext.getAttribute("sprk_checkedoutby");
            if (checkedOutByAttr) {
                var checkedOutBy = checkedOutByAttr.getValue();
                if (checkedOutBy && checkedOutBy.length > 0 && checkedOutBy[0]) {
                    // Document is checked out - check if by current user
                    var currentUserId = Xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "").toLowerCase();
                    var checkedOutUserId = checkedOutBy[0].id.replace(/[{}]/g, "").toLowerCase();
                    var isCurrentUser = currentUserId === checkedOutUserId;
                    console.log("[Spaarke.Document] canCheckin - checkedOutBy:", checkedOutBy[0].name, "isCurrentUser:", isCurrentUser);
                    return isCurrentUser;
                } else {
                    console.log("[Spaarke.Document] canCheckin - document not checked out (sprk_checkedoutby is empty)");
                }
            } else {
                console.log("[Spaarke.Document] canCheckin - sprk_checkedoutby attribute not found on form");
            }
        } else {
            console.log("[Spaarke.Document] canCheckin - No form context available");
        }
    } catch (e) {
        console.log("[Spaarke.Document] canCheckin error:", e);
    }

    // Default to hidden (don't show Check In button)
    return false;
};

/**
 * Enable/Display rule for Discard button
 * Returns true if document is checked out BY CURRENT USER
 * Note: For ribbon DisplayRules/EnableRules, primaryControl may not be provided.
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canDiscard = function(primaryControl) {
    // Same logic as canCheckin
    console.log("[Spaarke.Document] canDiscard - delegating to canCheckin");
    return Spaarke.Document.canCheckin(primaryControl);
};

/**
 * Enable/Display rule for Delete button
 * Returns true if document is NOT checked out
 * Note: For ribbon DisplayRules/EnableRules, primaryControl may not be provided.
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canDelete = function(primaryControl) {
    // Same logic as canCheckout (can delete if not checked out)
    console.log("[Spaarke.Document] canDelete - delegating to canCheckout");
    return Spaarke.Document.canCheckout(primaryControl);
};

// =============================================================================
// MODULE EXPORTS
// =============================================================================

console.log("[Spaarke.Document] Operations module loaded v" + Spaarke.Document.Config.version);
