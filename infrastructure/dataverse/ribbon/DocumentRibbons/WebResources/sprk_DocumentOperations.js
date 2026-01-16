/**
 * Spaarke Document Operations
 * Version: 1.23.0
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
    version: "1.23.0",

    // Document Status Codes (statuscode field values)
    statusCode: {
        DRAFT: 1,
        CHECKED_IN: 421500002,
        CHECKED_OUT: 421500001,
        LOCKED: 2
    }
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
    },

    /**
     * Update document status code in Dataverse
     * @param {string} documentId - Document ID (GUID)
     * @param {number} statusCode - Status code value from Spaarke.Document.Config.statusCode
     * @returns {Promise<boolean>} Success
     */
    updateDocumentStatus: async function(documentId, statusCode) {
        try {
            console.log("[Spaarke.Document] Updating document status:", documentId, "to", statusCode);

            var result = await Xrm.WebApi.updateRecord("sprk_document", documentId, {
                "statuscode": statusCode
            });

            console.log("[Spaarke.Document] Document status updated successfully");
            return true;
        } catch (error) {
            console.error("[Spaarke.Document] Failed to update document status:", error);
            // Don't throw - status update failure shouldn't block the operation
            return false;
        }
    }
};

// =============================================================================
// CHOICE DIALOG (ADR-023 Pattern)
// =============================================================================

/**
 * Choice Dialog - Follows ADR-023 pattern for rich option buttons
 * Creates a Fluent-styled modal with vertically stacked option buttons
 *
 * @param {object} config - Dialog configuration
 * @param {string} config.title - Dialog title
 * @param {string|HTMLElement} config.message - Message content (can include HTML for contact links)
 * @param {Array<object>} config.options - Array of options { id, icon, title, description }
 * @param {string} [config.cancelText="Cancel"] - Cancel button text
 * @returns {Promise<string|null>} Selected option ID or null if cancelled
 */
Spaarke.Document.showChoiceDialog = function(config) {
    return new Promise(function(resolve) {
        // Create overlay
        var overlay = document.createElement('div');
        overlay.className = 'sprk-choice-dialog-overlay';
        overlay.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.4);' +
            'display:flex;align-items:center;justify-content:center;z-index:10000;font-family:"Segoe UI",sans-serif;';

        // Create dialog surface
        var dialog = document.createElement('div');
        dialog.className = 'sprk-choice-dialog';
        dialog.style.cssText = 'background:#fff;border-radius:8px;box-shadow:0 25px 65px rgba(0,0,0,0.35);' +
            'max-width:480px;width:90%;max-height:90vh;overflow:auto;';

        // Dialog header
        var header = document.createElement('div');
        header.style.cssText = 'padding:20px 24px 0;display:flex;justify-content:space-between;align-items:center;';

        var title = document.createElement('h2');
        title.style.cssText = 'margin:0;font-size:20px;font-weight:600;color:#242424;';
        title.textContent = config.title;

        var closeBtn = document.createElement('button');
        closeBtn.style.cssText = 'background:none;border:none;font-size:20px;cursor:pointer;color:#616161;padding:4px;';
        closeBtn.innerHTML = '&times;';
        closeBtn.onclick = function() { cleanup(null); };

        header.appendChild(title);
        header.appendChild(closeBtn);

        // Dialog content
        var content = document.createElement('div');
        content.style.cssText = 'padding:16px 24px;';

        // Message
        var messageDiv = document.createElement('div');
        messageDiv.style.cssText = 'color:#424242;font-size:14px;line-height:1.5;margin-bottom:16px;';
        if (typeof config.message === 'string') {
            messageDiv.innerHTML = config.message;
        } else {
            messageDiv.appendChild(config.message);
        }
        content.appendChild(messageDiv);

        // Options container
        var optionsDiv = document.createElement('div');
        optionsDiv.style.cssText = 'display:flex;flex-direction:column;gap:8px;';

        // Create option buttons (ADR-023 style: outline buttons with icon + title + description)
        config.options.forEach(function(option) {
            var btn = document.createElement('button');
            btn.style.cssText = 'display:flex;align-items:center;gap:12px;width:100%;padding:12px 16px;' +
                'background:#fff;border:1px solid #d1d1d1;border-radius:4px;cursor:pointer;text-align:left;' +
                'transition:all 0.1s ease;min-height:64px;';
            btn.onmouseover = function() { this.style.borderColor = '#0078d4'; this.style.background = '#f5f5f5'; };
            btn.onmouseout = function() { this.style.borderColor = '#d1d1d1'; this.style.background = '#fff'; };
            btn.onclick = function() { cleanup(option.id); };

            // Icon
            var iconSpan = document.createElement('span');
            iconSpan.style.cssText = 'font-size:24px;color:#0078d4;flex-shrink:0;width:24px;text-align:center;';
            iconSpan.innerHTML = option.icon;
            btn.appendChild(iconSpan);

            // Text container
            var textDiv = document.createElement('div');
            textDiv.style.cssText = 'display:flex;flex-direction:column;gap:2px;overflow:hidden;';

            var titleSpan = document.createElement('span');
            titleSpan.style.cssText = 'font-weight:600;color:#242424;font-size:14px;';
            titleSpan.textContent = option.title;
            textDiv.appendChild(titleSpan);

            var descSpan = document.createElement('span');
            descSpan.style.cssText = 'color:#616161;font-size:12px;line-height:1.4;';
            descSpan.textContent = option.description;
            textDiv.appendChild(descSpan);

            btn.appendChild(textDiv);
            optionsDiv.appendChild(btn);
        });

        content.appendChild(optionsDiv);

        // Dialog footer with Cancel button
        var footer = document.createElement('div');
        footer.style.cssText = 'padding:16px 24px 20px;display:flex;justify-content:flex-end;border-top:1px solid #e0e0e0;margin-top:8px;';

        var cancelBtn = document.createElement('button');
        cancelBtn.style.cssText = 'padding:8px 20px;background:#fff;border:1px solid #d1d1d1;border-radius:4px;' +
            'cursor:pointer;font-size:14px;color:#242424;';
        cancelBtn.textContent = config.cancelText || 'Cancel';
        cancelBtn.onmouseover = function() { this.style.background = '#f5f5f5'; };
        cancelBtn.onmouseout = function() { this.style.background = '#fff'; };
        cancelBtn.onclick = function() { cleanup(null); };
        footer.appendChild(cancelBtn);

        // Assemble dialog
        dialog.appendChild(header);
        dialog.appendChild(content);
        dialog.appendChild(footer);
        overlay.appendChild(dialog);

        // Cleanup function
        function cleanup(result) {
            document.body.removeChild(overlay);
            document.removeEventListener('keydown', escHandler);
            resolve(result);
        }

        // ESC key handler
        function escHandler(e) {
            if (e.key === 'Escape') {
                cleanup(null);
            }
        }
        document.addEventListener('keydown', escHandler);

        // Add to DOM
        document.body.appendChild(overlay);

        // Focus first option button for accessibility
        var firstBtn = optionsDiv.querySelector('button');
        if (firstBtn) {
            firstBtn.focus();
        }
    });
};

/**
 * Show document locked dialog with three options: View Only, Download, Cancel
 * Plus contact link for the person who has it checked out
 * Follows ADR-023 Choice Dialog pattern
 *
 * @param {object} checkoutInfo - Info about who has the document checked out
 * @param {string} checkoutInfo.name - Name of person who checked out
 * @param {string} [checkoutInfo.email] - Email of person who checked out
 * @param {string} documentName - Name of the document
 * @returns {Promise<string|null>} 'view' | 'download' | null
 */
Spaarke.Document.showDocumentLockedDialog = function(checkoutInfo, documentName) {
    // Build message with optional contact link
    var messageHtml = '<strong>"' + documentName + '"</strong> is currently checked out by <strong>' +
        checkoutInfo.name + '</strong>.';

    if (checkoutInfo.email) {
        var subject = encodeURIComponent('Request: Document Access - ' + documentName);
        var body = encodeURIComponent('Hi ' + checkoutInfo.name + ',\n\nI need to access the document "' +
            documentName + '" which is currently checked out to you.\n\nCould you please let me know when you\'ll be done, or check it back in if you\'re finished?\n\nThank you!');
        var mailtoUrl = 'mailto:' + checkoutInfo.email + '?subject=' + subject + '&body=' + body;

        messageHtml += '<br><br><a href="' + mailtoUrl + '" style="color:#0078d4;text-decoration:none;">' +
            '📧 Contact ' + checkoutInfo.name + '</a>';
    }

    var options = [
        {
            id: 'view',
            icon: '👁️',
            title: 'View Only',
            description: 'Open the document to view. Any changes you make will not be saved.'
        },
        {
            id: 'download',
            icon: '📥',
            title: 'Download Copy',
            description: 'Download a local copy to edit offline. Changes won\'t sync back automatically.'
        }
    ];

    return Spaarke.Document.showChoiceDialog({
        title: 'Document Locked',
        message: messageHtml,
        options: options,
        cancelText: 'Cancel'
    });
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
 * @param {string} [prefetchedToken] - Optional pre-fetched access token (to avoid MSAL popup issues)
 * @returns {Promise<object>} Checkout status
 */
Spaarke.Document.getCheckoutStatus = async function(documentId, prefetchedToken) {
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
        // Use prefetched token if provided, otherwise get a new one
        var token = prefetchedToken || await Spaarke.Document.getAccessToken();
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

            // Update document status to "Checked Out" in Dataverse
            await Spaarke.Document.Utils.updateDocumentStatus(
                docInfo.id,
                Spaarke.Document.Config.statusCode.CHECKED_OUT
            );

            // Clear cache
            Spaarke.Document.clearCheckoutStatusCache(docInfo.id);

            // v1.19.0: Show dialog, then navigate to refresh page
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

            // Update document status to "Checked In" in Dataverse
            await Spaarke.Document.Utils.updateDocumentStatus(
                docInfo.id,
                Spaarke.Document.Config.statusCode.CHECKED_IN
            );

            // Clear cache
            Spaarke.Document.clearCheckoutStatusCache(docInfo.id);

            // v1.19.0: Show dialog, then navigate to refresh page
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

            // Update document status to "Checked In" in Dataverse (back to previous state)
            await Spaarke.Document.Utils.updateDocumentStatus(
                docInfo.id,
                Spaarke.Document.Config.statusCode.CHECKED_IN
            );

            // Clear cache
            Spaarke.Document.clearCheckoutStatusCache(docInfo.id);

            // v1.19.0: Show dialog, then navigate to refresh page
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
// CHECKOUT HELPER (for Open in Web/Desktop)
// =============================================================================

/**
 * Perform checkout without showing success dialog
 * Used by openInWeb/openInDesktop when user chooses to check out first
 * @param {string} documentId - Document ID
 * @param {string} documentName - Document name for error messages
 * @returns {Promise<boolean>} Success
 */
Spaarke.Document._performCheckout = async function(documentId, documentName) {
    console.log("[Spaarke.Document] Performing checkout for:", documentId, documentName);

    try {
        // Get access token
        var token = await Spaarke.Document.getAccessToken();

        // Call BFF API to checkout
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + documentId + "/checkout",
            {
                method: "POST",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                }
            }
        );

        if (response.ok) {
            console.log("[Spaarke.Document] Checkout successful");

            // Update document status to "Checked Out" in Dataverse
            await Spaarke.Document.Utils.updateDocumentStatus(
                documentId,
                Spaarke.Document.Config.statusCode.CHECKED_OUT
            );

            // Clear cache
            Spaarke.Document.clearCheckoutStatusCache(documentId);

            return true;

        } else {
            var errorData;
            try {
                errorData = await response.json();
            } catch (e) {
                errorData = { detail: "Unknown error occurred" };
            }

            if (response.status === 409) {
                // Already checked out
                var lockedMessage = "This document is already checked out";
                if (errorData.checkedOutBy) {
                    lockedMessage += " by " + errorData.checkedOutBy.name;
                }
                lockedMessage += ".";
                throw new Error(lockedMessage);
            } else {
                throw new Error(errorData.detail || errorData.title || "Failed to check out document");
            }
        }

    } catch (error) {
        console.error("[Spaarke.Document] Checkout failed:", error);
        throw error;
    }
};

/**
 * Check if document is currently checked out (synchronous check from form attribute)
 * @param {object} formContext - Form context
 * @returns {object} { isCheckedOut: boolean, isCheckedOutByMe: boolean }
 */
Spaarke.Document._getCheckoutState = function(formContext) {
    try {
        var checkedOutByAttr = formContext.getAttribute("sprk_checkedoutby");
        if (checkedOutByAttr) {
            var checkedOutBy = checkedOutByAttr.getValue();
            if (checkedOutBy && checkedOutBy.length > 0 && checkedOutBy[0]) {
                // Document is checked out
                var currentUserId = Xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "").toLowerCase();
                var checkedOutUserId = checkedOutBy[0].id.replace(/[{}]/g, "").toLowerCase();
                var isCurrentUser = currentUserId === checkedOutUserId;
                return {
                    isCheckedOut: true,
                    isCheckedOutByMe: isCurrentUser,
                    checkedOutByName: checkedOutBy[0].name
                };
            }
        }
    } catch (e) {
        console.log("[Spaarke.Document] _getCheckoutState error:", e);
    }
    return { isCheckedOut: false, isCheckedOutByMe: false, checkedOutByName: null };
};

// =============================================================================
// OPEN IN WEB (Office Online)
// =============================================================================

/**
 * Office file extensions that support Office Online viewing
 * (matches SpeDocumentViewer's comprehensive list)
 */
Spaarke.Document.OFFICE_EXTENSIONS = [
    // Word
    '.docx', '.doc', '.docm', '.dot', '.dotx', '.dotm',
    // Excel
    '.xlsx', '.xls', '.xlsm', '.xlsb', '.xlt', '.xltx', '.xltm',
    // PowerPoint
    '.pptx', '.ppt', '.pptm', '.pot', '.potx', '.potm', '.pps', '.ppsx', '.ppsm'
];

/**
 * Open document in Office Online (new browser tab)
 * Called from ribbon button
 * Shows checkout prompt if document is not already checked out
 * If locked by another user, shows ADR-023 choice dialog with View/Download/Cancel options
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.openInWeb = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Open in Web requested for:", docInfo.id, docInfo.name);

        // CRITICAL: Pre-fetch token immediately while in user click context
        // This ensures MSAL can open popup if needed (before any async dialogs)
        console.log("[Spaarke.Document] Pre-fetching token in user click context...");
        var prefetchedToken = await Spaarke.Document.getAccessToken();
        console.log("[Spaarke.Document] Token pre-fetched successfully");

        // Check current checkout state
        var checkoutState = Spaarke.Document._getCheckoutState(formContext);
        console.log("[Spaarke.Document] Checkout state:", checkoutState);

        // Track if we need to proceed with opening
        var shouldOpen = true;
        var didCheckout = false;

        // If document is not checked out, ask user if they want to check it out first
        if (!checkoutState.isCheckedOut) {
            var confirmResult = await Xrm.Navigation.openConfirmDialog({
                title: "Check Out Document?",
                text: "Do you want to check out \"" + docInfo.name + "\" before opening?\n\n" +
                      "Checking out will prevent others from editing while you have it open.",
                confirmButtonLabel: "Check Out & Open",
                cancelButtonLabel: "Open Without Checkout"
            });

            if (confirmResult.confirmed) {
                // User wants to check out first
                Xrm.Utility.showProgressIndicator("Checking out document...");

                try {
                    await Spaarke.Document._performCheckout(docInfo.id, docInfo.name);
                    console.log("[Spaarke.Document] Document checked out before opening");
                    didCheckout = true;
                    Xrm.Utility.closeProgressIndicator();
                } catch (checkoutError) {
                    Xrm.Utility.closeProgressIndicator();
                    await Xrm.Navigation.openErrorDialog({
                        message: checkoutError.message
                    });
                    return;
                }
            }
        } else if (!checkoutState.isCheckedOutByMe) {
            // Document is checked out by someone else - show ADR-023 choice dialog
            console.log("[Spaarke.Document] Document locked, fetching checkout details...");

            // Get full checkout info including email from BFF API (using prefetched token)
            var checkoutInfo = { name: checkoutState.checkedOutByName, email: null };
            try {
                var apiStatus = await Spaarke.Document.getCheckoutStatus(docInfo.id, prefetchedToken);
                if (apiStatus.checkedOutBy && apiStatus.checkedOutBy.email) {
                    checkoutInfo.email = apiStatus.checkedOutBy.email;
                }
            } catch (e) {
                console.log("[Spaarke.Document] Could not fetch checkout email:", e);
            }

            // Show ADR-023 styled choice dialog
            var choice = await Spaarke.Document.showDocumentLockedDialog(checkoutInfo, docInfo.name);
            console.log("[Spaarke.Document] User choice:", choice);

            if (choice === 'download') {
                // User chose to download instead
                await Spaarke.Document.downloadDocument(primaryControl);
                return;
            } else if (choice !== 'view') {
                // User cancelled
                return;
            }
            // choice === 'view' - continue to open
        }

        // Show progress
        Xrm.Utility.showProgressIndicator("Getting document link...");

        // Use the prefetched token for API call
        var token = prefetchedToken;

        // Call BFF API to get open links
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + docInfo.id + "/open-links",
            {
                method: "GET",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                }
            }
        );

        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            var data = await response.json();

            if (data.webUrl) {
                console.log("[Spaarke.Document] Opening in Office Online:", data.webUrl);
                // Open in new tab with security attributes
                window.open(data.webUrl, '_blank', 'noopener,noreferrer');

                // If we checked out, refresh the form to show updated status
                if (didCheckout) {
                    Spaarke.Document.refreshForm(formContext, docInfo.id);
                }
            } else {
                console.warn("[Spaarke.Document] No web URL available for document");
                await Xrm.Navigation.openErrorDialog({
                    message: "This document cannot be opened in Office Online."
                });
            }

        } else {
            var errorData;
            try {
                errorData = await response.json();
            } catch (e) {
                errorData = { detail: "Unknown error occurred" };
            }

            var errorMessage = errorData.detail || errorData.title || "Failed to get document link";
            await Xrm.Navigation.openErrorDialog({
                message: errorMessage
            });
        }

    } catch (error) {
        console.error("[Spaarke.Document] Open in Web error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to open document: " + error.message
        });
    }
};

// =============================================================================
// OPEN IN DESKTOP (Native App)
// =============================================================================

/**
 * Open document in desktop application (Word, Excel, PowerPoint)
 * Called from ribbon button
 * Shows checkout prompt if document is not already checked out
 * If locked by another user, shows ADR-023 choice dialog with View/Download/Cancel options
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.openInDesktop = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Open in Desktop requested for:", docInfo.id, docInfo.name);

        // CRITICAL: Pre-fetch token immediately while in user click context
        // This ensures MSAL can open popup if needed (before any async dialogs)
        console.log("[Spaarke.Document] Pre-fetching token in user click context...");
        var prefetchedToken = await Spaarke.Document.getAccessToken();
        console.log("[Spaarke.Document] Token pre-fetched successfully");

        // Check current checkout state
        var checkoutState = Spaarke.Document._getCheckoutState(formContext);
        console.log("[Spaarke.Document] Checkout state:", checkoutState);

        // Track if we checked out
        var didCheckout = false;

        // If document is not checked out, ask user if they want to check it out first
        if (!checkoutState.isCheckedOut) {
            var confirmResult = await Xrm.Navigation.openConfirmDialog({
                title: "Check Out Document?",
                text: "Do you want to check out \"" + docInfo.name + "\" before opening?\n\n" +
                      "Checking out will prevent others from editing while you have it open.",
                confirmButtonLabel: "Check Out & Open",
                cancelButtonLabel: "Open Without Checkout"
            });

            if (confirmResult.confirmed) {
                // User wants to check out first
                Xrm.Utility.showProgressIndicator("Checking out document...");

                try {
                    await Spaarke.Document._performCheckout(docInfo.id, docInfo.name);
                    console.log("[Spaarke.Document] Document checked out before opening");
                    didCheckout = true;
                    Xrm.Utility.closeProgressIndicator();
                } catch (checkoutError) {
                    Xrm.Utility.closeProgressIndicator();
                    await Xrm.Navigation.openErrorDialog({
                        message: checkoutError.message
                    });
                    return;
                }
            }
        } else if (!checkoutState.isCheckedOutByMe) {
            // Document is checked out by someone else - show ADR-023 choice dialog
            console.log("[Spaarke.Document] Document locked, fetching checkout details...");

            // Get full checkout info including email from BFF API (using prefetched token)
            var checkoutInfo = { name: checkoutState.checkedOutByName, email: null };
            try {
                var apiStatus = await Spaarke.Document.getCheckoutStatus(docInfo.id, prefetchedToken);
                if (apiStatus.checkedOutBy && apiStatus.checkedOutBy.email) {
                    checkoutInfo.email = apiStatus.checkedOutBy.email;
                }
            } catch (e) {
                console.log("[Spaarke.Document] Could not fetch checkout email:", e);
            }

            // Show ADR-023 styled choice dialog
            var choice = await Spaarke.Document.showDocumentLockedDialog(checkoutInfo, docInfo.name);
            console.log("[Spaarke.Document] User choice:", choice);

            if (choice === 'download') {
                // User chose to download instead
                await Spaarke.Document.downloadDocument(primaryControl);
                return;
            } else if (choice !== 'view') {
                // User cancelled
                return;
            }
            // choice === 'view' - continue to open
        }

        // Show progress
        Xrm.Utility.showProgressIndicator("Getting document link...");

        // Use the prefetched token for API call
        var token = prefetchedToken;

        // Call BFF API to get open links
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + docInfo.id + "/open-links",
            {
                method: "GET",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                }
            }
        );

        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            var data = await response.json();

            if (data.desktopUrl) {
                console.log("[Spaarke.Document] Opening in desktop app:", data.desktopUrl);
                // Use location.href to trigger protocol handler (ms-word:, ms-excel:, etc.)
                window.location.href = data.desktopUrl;

                // If we checked out, refresh the form to show updated status
                if (didCheckout) {
                    // Small delay to allow the protocol handler to start
                    setTimeout(function() {
                        Spaarke.Document.refreshForm(formContext, docInfo.id);
                    }, 500);
                }
            } else {
                console.warn("[Spaarke.Document] No desktop URL available for document");
                await Xrm.Navigation.openErrorDialog({
                    message: "This document cannot be opened in a desktop application."
                });
            }

        } else {
            var errorData;
            try {
                errorData = await response.json();
            } catch (e) {
                errorData = { detail: "Unknown error occurred" };
            }

            var errorMessage = errorData.detail || errorData.title || "Failed to get document link";
            await Xrm.Navigation.openErrorDialog({
                message: errorMessage
            });
        }

    } catch (error) {
        console.error("[Spaarke.Document] Open in Desktop error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to open document: " + error.message
        });
    }
};

// =============================================================================
// DOWNLOAD DOCUMENT
// =============================================================================

/**
 * Download document - called from ribbon button
 * Uses BFF API to download document file
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.downloadDocument = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Download requested for:", docInfo.id, docInfo.name);

        // Show progress
        Xrm.Utility.showProgressIndicator("Downloading document...");

        // Get access token
        var token = await Spaarke.Document.getAccessToken();

        // Call BFF API to get download blob
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + docInfo.id + "/download",
            {
                method: "GET",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId
                }
            }
        );

        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            // Get filename from Content-Disposition header or use document name
            var filename = docInfo.name || "document";
            var contentDisposition = response.headers.get("Content-Disposition");
            if (contentDisposition) {
                var match = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
                if (match && match[1]) {
                    filename = match[1].replace(/['"]/g, '');
                }
            }

            // Create blob and trigger download
            var blob = await response.blob();
            var url = window.URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);

            console.log("[Spaarke.Document] Document downloaded:", filename);

        } else {
            var errorData;
            try {
                errorData = await response.json();
            } catch (e) {
                errorData = { detail: "Unknown error occurred" };
            }

            var errorMessage = errorData.detail || errorData.title || "Failed to download document";
            await Xrm.Navigation.openErrorDialog({
                message: errorMessage
            });
        }

    } catch (error) {
        console.error("[Spaarke.Document] Download error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to download document: " + error.message
        });
    }
};

/**
 * Enable/Display rule for Download button
 * Always returns true (download is available for all document types)
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canDownload = function(primaryControl) {
    // Download is always available for any document type
    return true;
};

// =============================================================================
// REFRESH FORM
// =============================================================================

/**
 * Refresh form and ribbon - called from ribbon button
 * Simple wrapper for form refresh with ribbon update
 * @param {object} primaryControl - Form context
 */
Spaarke.Document.refreshDocument = function(primaryControl) {
    var formContext = primaryControl;

    try {
        var docInfo = Spaarke.Document.Utils.getDocumentInfo(formContext);
        console.log("[Spaarke.Document] Refresh requested for:", docInfo.id, docInfo.name);

        // Clear checkout status cache to get fresh data
        Spaarke.Document.clearCheckoutStatusCache(docInfo.id);

        // Refresh form data and ribbon
        Spaarke.Document.refreshForm(formContext, docInfo.id);

    } catch (error) {
        console.error("[Spaarke.Document] Refresh error:", error);
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

/**
 * Enable/Display rule for Open in Web button
 * Returns true only for Office file types that have Office Online viewers
 * Note: For ribbon DisplayRules/EnableRules, primaryControl may not be provided.
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canOpenInWeb = function(primaryControl) {
    try {
        // Use provided context or fall back to Xrm.Page
        var formContext = primaryControl;
        if (!formContext || !formContext.getAttribute) {
            if (typeof Xrm !== 'undefined' && Xrm.Page && Xrm.Page.getAttribute) {
                formContext = Xrm.Page;
            }
        }

        if (formContext && formContext.getAttribute) {
            // Check file extension from document name or file extension field
            var fileExtension = null;

            // Try sprk_fileextension field first (if it exists)
            var extAttr = formContext.getAttribute("sprk_fileextension");
            if (extAttr) {
                fileExtension = extAttr.getValue();
            }

            // Fall back to extracting from document name
            if (!fileExtension) {
                var nameAttr = formContext.getAttribute("sprk_documentname") || formContext.getAttribute("sprk_name");
                if (nameAttr) {
                    var name = nameAttr.getValue();
                    if (name) {
                        var lastDot = name.lastIndexOf('.');
                        if (lastDot > 0) {
                            fileExtension = name.substring(lastDot).toLowerCase();
                        }
                    }
                }
            }

            if (fileExtension) {
                fileExtension = fileExtension.toLowerCase();
                // Ensure it starts with a dot
                if (!fileExtension.startsWith('.')) {
                    fileExtension = '.' + fileExtension;
                }

                var isOfficeFile = Spaarke.Document.OFFICE_EXTENSIONS.indexOf(fileExtension) !== -1;
                console.log("[Spaarke.Document] canOpenInWeb - extension:", fileExtension, "isOffice:", isOfficeFile);
                return isOfficeFile;
            }

            console.log("[Spaarke.Document] canOpenInWeb - no file extension found");
        }
    } catch (e) {
        console.log("[Spaarke.Document] canOpenInWeb error:", e);
    }

    // Default to hidden (hide Open in Web button for unknown file types)
    return false;
};

/**
 * Enable/Display rule for Open in Desktop button
 * Returns true for Office file types (Word, Excel, PowerPoint)
 * Note: For ribbon DisplayRules/EnableRules, primaryControl may not be provided.
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canOpenInDesktop = function(primaryControl) {
    // Same logic as canOpenInWeb - both require Office file types
    console.log("[Spaarke.Document] canOpenInDesktop - delegating to canOpenInWeb");
    return Spaarke.Document.canOpenInWeb(primaryControl);
};

/**
 * Enable/Display rule for Refresh button
 * Always returns true (refresh is always available)
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Document.canRefresh = function(primaryControl) {
    // Refresh is always available
    return true;
};

// =============================================================================
// MODULE EXPORTS
// =============================================================================

console.log("[Spaarke.Document] Operations module loaded v" + Spaarke.Document.Config.version);

// v1.23.0 Changes:
// - CRITICAL FIX: Pre-fetch MSAL token at start of openInWeb/openInDesktop
// - This ensures token acquisition happens in user click context (before async dialogs)
// - Fixes MSAL timeout when document is locked by another user (popup blocked issue)
// - getCheckoutStatus now accepts optional prefetchedToken parameter

// v1.22.0 Changes:
// - ADR-023: Enhanced document locked dialog with View Only / Download / Cancel options
// - Added showChoiceDialog() - reusable ADR-023 compliant choice dialog
// - Added showDocumentLockedDialog() - specialized dialog for locked documents
// - Added email contact link when document is locked by another user
// - Fetches checkout user email from BFF API for contact link

// v1.21.0 Changes:
// - Added status code updates on checkout/checkin/discard (statuscode field)
// - Added checkout prompt dialog when opening documents in Web or Desktop
// - Added _performCheckout and _getCheckoutState helper functions

