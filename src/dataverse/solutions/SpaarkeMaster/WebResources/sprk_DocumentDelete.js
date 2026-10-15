/**
 * Spaarke Document Delete
 * Version: 1.0.0
 * Description: Delete document via BFF API with MSAL authentication
 *
 * ADR-006 Exception: Approved for ribbon delete button
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
        // Client Application ID (PCF client app registration)
        clientId: "ed44fb14-7d2a-476c-8e1b-7e43e3cfecb0",
        // BFF Application ID (for scope construction)
        bffAppId: "8c60b44e-5fc7-471c-940e-0f77f395c18d",
        // Azure AD Tenant ID
        tenantId: "e2e89f3a-98bf-4db2-b149-5b3fe72e8fe7",
        // Authority URL
        get authority() {
            return `https://login.microsoftonline.com/${this.tenantId}`;
        },
        // BFF API scope
        get scope() {
            return `api://${this.bffAppId}/user_impersonation`;
        }
    },

    // Version
    version: "1.0.0"
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
 * @returns {Promise<msal.PublicClientApplication>}
 */
Spaarke.Document._initMsal = function() {
    if (Spaarke.Document._msalInitPromise) {
        return Spaarke.Document._msalInitPromise;
    }

    Spaarke.Document._msalInitPromise = Spaarke.Document._loadMsalLibrary()
        .then(function() {
            var config = {
                auth: {
                    clientId: Spaarke.Document.Config.msal.clientId,
                    authority: Spaarke.Document.Config.msal.authority,
                    redirectUri: window.location.origin
                },
                cache: {
                    cacheLocation: "sessionStorage",
                    storeAuthStateInCookie: false
                }
            };

            Spaarke.Document._msalInstance = new msal.PublicClientApplication(config);
            console.log("[Spaarke.Document] MSAL initialized");

            // Handle redirect promise (in case returning from login)
            return Spaarke.Document._msalInstance.handleRedirectPromise();
        })
        .then(function() {
            return Spaarke.Document._msalInstance;
        });

    return Spaarke.Document._msalInitPromise;
};

/**
 * Get access token for BFF API
 * Uses SSO silent flow with popup fallback
 * @returns {Promise<string>} Access token
 */
Spaarke.Document.getAccessToken = async function() {
    var msalInstance = await Spaarke.Document._initMsal();
    var scope = Spaarke.Document.Config.msal.scope;

    var request = {
        scopes: [scope]
    };

    try {
        // Try silent token acquisition first
        var accounts = msalInstance.getAllAccounts();
        if (accounts.length > 0) {
            request.account = accounts[0];
            var response = await msalInstance.acquireTokenSilent(request);
            console.log("[Spaarke.Document] Token acquired silently");
            return response.accessToken;
        }

        // No accounts - try SSO silent
        var ssoResponse = await msalInstance.ssoSilent(request);
        console.log("[Spaarke.Document] Token acquired via SSO");
        return ssoResponse.accessToken;

    } catch (error) {
        console.log("[Spaarke.Document] Silent auth failed, trying popup:", error.message);

        // Fall back to popup
        try {
            var popupResponse = await msalInstance.acquireTokenPopup(request);
            console.log("[Spaarke.Document] Token acquired via popup");
            return popupResponse.accessToken;
        } catch (popupError) {
            console.error("[Spaarke.Document] Popup auth failed:", popupError);
            throw new Error("Authentication failed. Please try again.");
        }
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
        // Get document info
        var documentId = formContext.data.entity.getId().replace(/[{}]/g, "");
        var nameAttr = formContext.getAttribute("sprk_documentname") || formContext.getAttribute("sprk_name");
        var documentName = nameAttr ? nameAttr.getValue() : "this document";

        console.log("[Spaarke.Document] Delete requested for:", documentId, documentName);

        // 1. Show confirmation dialog
        var confirmResult = await Xrm.Navigation.openConfirmDialog({
            title: "Delete Document?",
            text: "This will permanently delete \"" + documentName + "\" and its file from storage.\n\nThis action cannot be undone.",
            confirmButtonLabel: "Delete",
            cancelButtonLabel: "Cancel"
        });

        if (!confirmResult.confirmed) {
            console.log("[Spaarke.Document] Delete cancelled by user");
            return;
        }

        // 2. Show progress
        Xrm.Utility.showProgressIndicator("Deleting document...");

        // 3. Get access token
        var token = await Spaarke.Document.getAccessToken();

        // 4. Call BFF API to delete
        var correlationId = Spaarke.Document.Utils.newGuid();
        var response = await fetch(
            Spaarke.Document.Config.bffApiUrl + "/api/documents/" + documentId,
            {
                method: "DELETE",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-Id": correlationId,
                    "Content-Type": "application/json"
                }
            }
        );

        // 5. Handle response
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

/**
 * Enable rule for delete button
 * Always returns true - actual permission check happens on click
 * @returns {boolean}
 */
Spaarke.Document.canDelete = function() {
    // Initialize on ribbon load
    if (!Spaarke.Document.Config.bffApiUrl) {
        Spaarke.Document.init();
    }
    return true;
};

// =============================================================================
// MODULE EXPORTS
// =============================================================================

console.log("[Spaarke.Document] Delete module loaded v" + Spaarke.Document.Config.version);
