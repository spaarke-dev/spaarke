/**
 * Spaarke Email Actions
 * Version: 1.1.0
 * Description: Email form ribbon button handlers for Save to Document functionality
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
    window.Spaarke.Email = window.Spaarke.Email || {};
}

var Spaarke = window.Spaarke;

// =============================================================================
// CONFIGURATION
// =============================================================================

Spaarke.Email.Config = {
    // BFF API URL - determined by environment
    bffApiUrl: null,

    // MSAL Configuration (shared with Document operations)
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
        // BFF API scope
        get scope() {
            return "api://" + this.bffAppId + "/SDAP.Access";
        },
        // Redirect URI - must match Azure AD app registration
        redirectUri: "https://spaarkedev1.crm.dynamics.com"
    },

    // Version
    version: "1.1.0"
};

// =============================================================================
// INITIALIZATION
// =============================================================================

/**
 * Initialize the module
 * Determines BFF API URL based on environment
 */
Spaarke.Email.init = function() {
    try {
        // Determine environment from Dataverse URL
        var globalContext = Xrm.Utility.getGlobalContext();
        var clientUrl = globalContext.getClientUrl();

        if (clientUrl.includes('spaarkedev1.crm.dynamics.com')) {
            Spaarke.Email.Config.bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";
        } else if (clientUrl.includes('spaarkeuat.crm.dynamics.com')) {
            Spaarke.Email.Config.bffApiUrl = "https://spe-api-uat.azurewebsites.net";
        } else if (clientUrl.includes('spaarkeprod.crm.dynamics.com')) {
            Spaarke.Email.Config.bffApiUrl = "https://spe-api-prod.azurewebsites.net";
        } else {
            // Default to dev
            Spaarke.Email.Config.bffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";
        }

        console.log("[Spaarke.Email] Initialized v" + Spaarke.Email.Config.version);
        console.log("[Spaarke.Email] BFF API URL:", Spaarke.Email.Config.bffApiUrl);

        return true;
    } catch (error) {
        console.error("[Spaarke.Email] Init failed:", error);
        return false;
    }
};

// =============================================================================
// MSAL AUTHENTICATION (reuses Document module if available)
// =============================================================================

/**
 * MSAL instance (lazy initialized)
 */
Spaarke.Email._msalInstance = null;
Spaarke.Email._msalInitPromise = null;

/**
 * Load MSAL library from CDN if not already loaded
 * @returns {Promise<void>}
 */
Spaarke.Email._loadMsalLibrary = function() {
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
            console.log("[Spaarke.Email] MSAL library loaded");
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
 * @returns {Promise<msal.PublicClientApplication>}
 */
Spaarke.Email._initMsal = function() {
    // Reuse Document module's MSAL instance if available
    if (Spaarke.Document && Spaarke.Document._msalInstance) {
        console.log("[Spaarke.Email] Reusing Document module's MSAL instance");
        Spaarke.Email._msalInstance = Spaarke.Document._msalInstance;
        return Promise.resolve(Spaarke.Email._msalInstance);
    }

    if (Spaarke.Email._msalInitPromise) {
        return Spaarke.Email._msalInitPromise;
    }

    Spaarke.Email._msalInitPromise = Spaarke.Email._loadMsalLibrary()
        .then(function() {
            var tenantId = Spaarke.Email.Config.msal.tenantId;

            var authorityMetadataJson = JSON.stringify({
                "authorization_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/authorize",
                "token_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/token",
                "issuer": "https://login.microsoftonline.com/" + tenantId + "/v2.0",
                "jwks_uri": "https://login.microsoftonline.com/" + tenantId + "/discovery/v2.0/keys",
                "end_session_endpoint": "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/logout"
            });

            var config = {
                auth: {
                    clientId: Spaarke.Email.Config.msal.clientId,
                    authority: Spaarke.Email.Config.msal.authority,
                    redirectUri: Spaarke.Email.Config.msal.redirectUri,
                    navigateToLoginRequestUrl: false,
                    knownAuthorities: ["login.microsoftonline.com"],
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
                        logLevel: 3
                    }
                }
            };

            Spaarke.Email._msalInstance = new msal.PublicClientApplication(config);
            console.log("[Spaarke.Email] MSAL PublicClientApplication created");

            if (typeof Spaarke.Email._msalInstance.initialize === 'function') {
                return Spaarke.Email._msalInstance.initialize().then(function() {
                    console.log("[Spaarke.Email] MSAL initialized");
                    return Spaarke.Email._msalInstance.handleRedirectPromise();
                });
            } else {
                return Spaarke.Email._msalInstance.handleRedirectPromise();
            }
        })
        .then(function(redirectResponse) {
            if (redirectResponse) {
                console.log("[Spaarke.Email] Redirect response processed");
                Spaarke.Email._currentAccount = redirectResponse.account;
            } else {
                var accounts = Spaarke.Email._msalInstance.getAllAccounts();
                if (accounts.length > 0) {
                    Spaarke.Email._currentAccount = accounts[0];
                    console.log("[Spaarke.Email] Existing account found: " + accounts[0].username);
                }
            }
            return Spaarke.Email._msalInstance;
        });

    return Spaarke.Email._msalInitPromise;
};

/**
 * Current account cache
 */
Spaarke.Email._currentAccount = null;

/**
 * Get access token for BFF API
 * @returns {Promise<string>} Access token
 */
Spaarke.Email.getAccessToken = async function() {
    // Reuse Document module's token if available
    if (Spaarke.Document && Spaarke.Document.getAccessToken) {
        console.log("[Spaarke.Email] Using Document module's getAccessToken");
        return Spaarke.Document.getAccessToken();
    }

    var msalInstance = await Spaarke.Email._initMsal();
    var scope = Spaarke.Email.Config.msal.scope;

    try {
        if (Spaarke.Email._currentAccount) {
            console.log("[Spaarke.Email] Attempting acquireTokenSilent...");
            try {
                var silentRequest = {
                    scopes: [scope],
                    account: Spaarke.Email._currentAccount
                };
                var silentResponse = await msalInstance.acquireTokenSilent(silentRequest);
                console.log("[Spaarke.Email] acquireTokenSilent succeeded");
                return silentResponse.accessToken;
            } catch (silentError) {
                console.log("[Spaarke.Email] acquireTokenSilent failed, trying ssoSilent...");
            }
        }

        console.log("[Spaarke.Email] Attempting ssoSilent authentication...");
        try {
            var ssoRequest = { scopes: [scope] };
            var ssoResponse = await msalInstance.ssoSilent(ssoRequest);
            console.log("[Spaarke.Email] ssoSilent succeeded");
            if (ssoResponse.account) {
                Spaarke.Email._currentAccount = ssoResponse.account;
            }
            return ssoResponse.accessToken;
        } catch (ssoError) {
            console.log("[Spaarke.Email] ssoSilent failed:", ssoError.message);
        }

        console.log("[Spaarke.Email] Falling back to popup authentication...");
        var popupRequest = {
            scopes: [scope],
            loginHint: Spaarke.Email._currentAccount ? Spaarke.Email._currentAccount.username : undefined
        };
        var popupResponse = await msalInstance.acquireTokenPopup(popupRequest);
        console.log("[Spaarke.Email] Popup authentication succeeded");
        if (popupResponse.account) {
            Spaarke.Email._currentAccount = popupResponse.account;
        }
        return popupResponse.accessToken;

    } catch (error) {
        console.error("[Spaarke.Email] Token acquisition failed:", error);

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

Spaarke.Email.Utils = {
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
     * Get email info from form context
     * @param {object} formContext - Form context
     * @returns {object} Email info
     */
    getEmailInfo: function(formContext) {
        var emailId = formContext.data.entity.getId().replace(/[{}]/g, "");
        var subjectAttr = formContext.getAttribute("subject");
        var subject = subjectAttr ? subjectAttr.getValue() : "this email";

        return {
            id: emailId,
            subject: subject
        };
    },

    /**
     * Check if email is completed (statecode = 1)
     * @param {object} formContext - Form context
     * @returns {boolean} True if completed
     */
    isEmailCompleted: function(formContext) {
        try {
            var statecodeAttr = formContext.getAttribute("statecode");
            if (statecodeAttr) {
                var statecode = statecodeAttr.getValue();
                // Email statecode: 0 = Open, 1 = Completed, 2 = Canceled
                return statecode === 1;
            }
        } catch (e) {
            console.error("[Spaarke.Email] Error checking statecode:", e);
        }
        return false;
    }
};

// =============================================================================
// SAVE TO DOCUMENT (Main Action)
// =============================================================================

/**
 * Save email to document - called from ribbon button
 * Converts the email to an SDAP Document record with .eml file
 * @param {object} primaryControl - Form context
 */
Spaarke.Email.saveToDocument = async function(primaryControl) {
    var formContext = primaryControl;

    // Initialize if not done
    if (!Spaarke.Email.Config.bffApiUrl) {
        Spaarke.Email.init();
    }

    try {
        var emailInfo = Spaarke.Email.Utils.getEmailInfo(formContext);
        console.log("[Spaarke.Email] Save to Document requested for:", emailInfo.id, emailInfo.subject);

        // Verify email is completed
        if (!Spaarke.Email.Utils.isEmailCompleted(formContext)) {
            await Xrm.Navigation.openAlertDialog({
                text: "Only completed emails can be saved as documents. Please send or complete this email first.",
                confirmButtonLabel: "OK"
            });
            return;
        }

        // Show progress
        Xrm.Utility.showProgressIndicator("Converting email to document...");

        // Get access token
        var token = await Spaarke.Email.getAccessToken();

        // Call BFF API to convert email to document
        var correlationId = Spaarke.Email.Utils.newGuid();
        var response = await fetch(
            Spaarke.Email.Config.bffApiUrl + "/api/emails/convert-to-document",
            {
                method: "POST",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    emailId: emailInfo.id,
                    includeAttachments: true,
                    triggerSource: "RibbonButton"
                })
            }
        );

        Xrm.Utility.closeProgressIndicator();

        if (response.ok) {
            var result = await response.json();
            console.log("[Spaarke.Email] Email saved to document successfully:", result);

            // Success dialog with option to open the document
            var openDocument = await Xrm.Navigation.openConfirmDialog({
                title: "Email Saved",
                text: "Email \"" + emailInfo.subject + "\" has been saved as an SDAP document.\n\n" +
                      (result.attachmentCount > 0 ? result.attachmentCount + " attachment(s) were also saved as separate documents.\n\n" : "") +
                      "Would you like to open the document record?",
                confirmButtonLabel: "Open Document",
                cancelButtonLabel: "Close"
            });

            if (openDocument.confirmed && result.documentId) {
                // Navigate to the created document
                Xrm.Navigation.navigateTo({
                    pageType: "entityrecord",
                    entityName: "sprk_document",
                    entityId: result.documentId
                });
            }

        } else {
            var errorData;
            var responseText = "";
            try {
                responseText = await response.text();
                console.error("[Spaarke.Email] Save failed with status:", response.status);
                console.error("[Spaarke.Email] Response body:", responseText);
                errorData = JSON.parse(responseText);
            } catch (e) {
                console.error("[Spaarke.Email] Failed to parse error response:", e);
                errorData = { detail: responseText || "Unknown error occurred" };
            }

            if (response.status === 409) {
                // Already processed (duplicate)
                await Xrm.Navigation.openAlertDialog({
                    text: "This email has already been saved as a document.",
                    confirmButtonLabel: "OK"
                });
            } else if (response.status === 404) {
                // Email not found
                await Xrm.Navigation.openErrorDialog({
                    message: "Email not found or you don't have permission to access it."
                });
            } else {
                var errorMessage = errorData.detail || errorData.title || "Failed to save email as document";
                await Xrm.Navigation.openErrorDialog({
                    message: errorMessage
                });
            }
        }

    } catch (error) {
        console.error("[Spaarke.Email] Save to document error:", error);
        Xrm.Utility.closeProgressIndicator();

        await Xrm.Navigation.openErrorDialog({
            message: "Failed to save email as document: " + error.message
        });
    }
};

// =============================================================================
// ENABLE RULES (for ribbon button visibility)
// =============================================================================

/**
 * Enable rule for "Save to Document" button
 * Returns true if email is completed (statecode = 1)
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Email.canSaveToDocument = function(primaryControl) {
    // Initialize on ribbon load
    if (!Spaarke.Email.Config.bffApiUrl) {
        Spaarke.Email.init();
    }

    try {
        var formContext = primaryControl;
        if (!formContext || !formContext.getAttribute) {
            // Fallback for ribbon DisplayRules that don't pass context
            if (typeof Xrm !== 'undefined' && Xrm.Page && Xrm.Page.getAttribute) {
                formContext = Xrm.Page;
            }
        }

        if (formContext && formContext.getAttribute) {
            var statecodeAttr = formContext.getAttribute("statecode");
            if (statecodeAttr) {
                var statecode = statecodeAttr.getValue();
                // Email statecode: 0 = Open, 1 = Completed, 2 = Canceled
                var isCompleted = statecode === 1;
                console.log("[Spaarke.Email] canSaveToDocument - statecode:", statecode, "isCompleted:", isCompleted);
                return isCompleted;
            } else {
                console.log("[Spaarke.Email] canSaveToDocument - statecode attribute not found");
            }
        } else {
            console.log("[Spaarke.Email] canSaveToDocument - No form context available");
        }
    } catch (e) {
        console.log("[Spaarke.Email] canSaveToDocument error:", e);
    }

    // Default to disabled (hide button until we know email is completed)
    return false;
};

/**
 * Check if email is already archived (has associated sprk_document)
 * Used as DisplayRule - returns false to SHOW button, true to HIDE
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean} True if email is already archived (button should be hidden)
 */
Spaarke.Email.isEmailArchived = function(primaryControl) {
    try {
        var formContext = primaryControl;
        if (!formContext || !formContext.data) {
            // Fallback for ribbon rules that don't pass context
            if (typeof Xrm !== 'undefined' && Xrm.Page && Xrm.Page.data) {
                formContext = Xrm.Page;
            }
        }

        if (!formContext || !formContext.data || !formContext.data.entity) {
            console.log("[Spaarke.Email] isEmailArchived - No form context");
            return false; // Show button if we can't determine
        }

        var emailId = formContext.data.entity.getId().replace(/[{}]/g, "").toLowerCase();
        console.log("[Spaarke.Email] isEmailArchived - Checking email:", emailId);

        // Check cache first (set by async check)
        var cacheKey = "sprk_email_archived_" + emailId;
        var cachedResult = sessionStorage.getItem(cacheKey);
        if (cachedResult !== null) {
            var isArchived = cachedResult === "true";
            console.log("[Spaarke.Email] isEmailArchived - Cache hit:", isArchived);
            return isArchived;
        }

        // Trigger async check and cache result for next ribbon refresh
        Spaarke.Email._checkEmailArchivedAsync(emailId);

        // Default to false (show button) - async check will update cache
        return false;

    } catch (e) {
        console.error("[Spaarke.Email] isEmailArchived error:", e);
        return false; // Show button on error
    }
};

/**
 * Async check if email has associated document
 * @param {string} emailId - Email GUID
 * @returns {Promise<boolean>}
 */
Spaarke.Email._checkEmailArchivedAsync = async function(emailId) {
    var cacheKey = "sprk_email_archived_" + emailId;

    try {
        // Query Dataverse for sprk_document where _sprk_email_value = emailId
        var query = "?$filter=_sprk_email_value eq '" + emailId + "'&$select=sprk_documentid&$top=1";
        var result = await Xrm.WebApi.retrieveMultipleRecords("sprk_document", query);

        var isArchived = result.entities && result.entities.length > 0;
        console.log("[Spaarke.Email] _checkEmailArchivedAsync - Result:", isArchived, "for email:", emailId);

        // Cache the result
        sessionStorage.setItem(cacheKey, isArchived.toString());

        // If archived, refresh ribbon to update button visibility
        if (isArchived && typeof Xrm !== 'undefined' && Xrm.Page && Xrm.Page.ui) {
            Xrm.Page.ui.refreshRibbon();
        }

        return isArchived;

    } catch (e) {
        console.error("[Spaarke.Email] _checkEmailArchivedAsync error:", e);
        sessionStorage.setItem(cacheKey, "false");
        return false;
    }
};

/**
 * Display rule for "Archive Email" button
 * Returns true to SHOW button (email NOT archived)
 * @param {object} [primaryControl] - Form context (optional)
 * @returns {boolean}
 */
Spaarke.Email.canArchiveEmail = function(primaryControl) {
    // Show button only if email is NOT already archived
    return !Spaarke.Email.isEmailArchived(primaryControl);
};

// =============================================================================
// MODULE EXPORTS
// =============================================================================

console.log("[Spaarke.Email] Actions module loaded v" + Spaarke.Email.Config.version);
