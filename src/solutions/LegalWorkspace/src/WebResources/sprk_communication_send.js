/**
 * Communication Send Command - Send Email via BFF
 *
 * Web resource for the sprk_communication entity Send command bar button.
 *
 * Main Form Button:
 * - Send: Opens send mode selection (My Mailbox / shared accounts), then
 *   collects form data and sends email via BFF POST /api/communications/send
 *
 * Enable Rule:
 * - isStatusDraft: Button enabled only when statuscode = 1 (Draft)
 *
 * Communication Status Values (statuscode):
 * - 1: Draft, 659490001: Queued, 659490002: Send, 659490003: Delivered,
 *   659490004: Failed, 659490005: Bounded, 659490006: Recalled
 *
 * Send Mode (Phase 7):
 * - "user": Send from user's own mailbox via OBO (My Mailbox)
 * - "sharedMailbox": Send from a shared mailbox account
 *
 * Association lookup fields (8 entity types):
 * - sprk_regardingmatter, sprk_regardingproject, sprk_regardingorganization,
 *   sprk_regardingperson, sprk_regardinganalysis, sprk_regardingbudget,
 *   sprk_regardinginvoice, sprk_regardingworkassignment
 *
 * BFF API: POST /api/communications/send
 * Error format: ProblemDetails (RFC 7807) with errorCode extension (ADR-019)
 *
 * Deployment:
 * 1. Upload as web resource: sprk_communication_send
 * 2. Add ribbon XML to Communication entity (see communication-ribbon-config.md)
 * 3. Publish customizations
 *
 * @see src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs
 * @see docs/data-model/sprk_communication-data-schema.md
 * @see projects/email-communication-solution-r1/notes/communication-ribbon-config.md
 */

/* eslint-disable no-undef */
"use strict";

// Namespace for Spaarke Communication commands
var Sprk = Sprk || {};
Sprk.Communication = Sprk.Communication || {};
Sprk.Communication.Send = Sprk.Communication.Send || {};

/**
 * Communication Status values (standard Dataverse statuscode)
 */
Sprk.Communication.Send.StatusCode = {
    DRAFT: 1,
    QUEUED: 659490001,
    SEND: 659490002,
    DELIVERED: 659490003,
    FAILED: 659490004,
    BOUNDED: 659490005,
    RECALLED: 659490006
};

/**
 * Notification unique IDs for form notifications
 */
Sprk.Communication.Send._NOTIFICATION_IDS = {
    SUCCESS: "sprk_communication_send_success",
    ERROR: "sprk_communication_send_error",
    VALIDATION: "sprk_communication_send_validation",
    PROGRESS: "sprk_communication_send_progress"
};

/**
 * Regarding lookup field mappings.
 * Maps Dataverse lookup field logical names to BFF entity type names.
 */
Sprk.Communication.Send._REGARDING_FIELDS = [
    { field: "sprk_regardingmatter", entityType: "sprk_matter" },
    { field: "sprk_regardingproject", entityType: "sprk_project" },
    { field: "sprk_regardingorganization", entityType: "sprk_organization" },
    { field: "sprk_regardingperson", entityType: "contact" },
    { field: "sprk_regardinganalysis", entityType: "sprk_analysis" },
    { field: "sprk_regardingbudget", entityType: "sprk_budget" },
    { field: "sprk_regardinginvoice", entityType: "sprk_invoice" },
    { field: "sprk_regardingworkassignment", entityType: "sprk_workassignment" }
];

/**
 * Send Mode values for the BFF API sendMode field.
 * Determines whether to send from a shared mailbox (app-only) or user mailbox (OBO).
 */
Sprk.Communication.Send.SendMode = {
    SHARED_MAILBOX: "sharedMailbox",
    USER: "user"
};

/**
 * MSAL Configuration for BFF API authentication.
 * Reuses the same app registration as Spaarke.Email / Document modules.
 * Required for acquiring bearer tokens for cross-origin BFF API calls
 * and for user-mode OBO token flow.
 */
Sprk.Communication.Send._MSAL_CONFIG = {
    // Client Application ID (SPE-File-Viewer-PCF app registration)
    clientId: "b36e9b91-ee7d-46e6-9f6a-376871cc9d54",
    // BFF Application ID (SDAP-BFF-SPE-API for scope construction)
    bffAppId: "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    // Azure AD Tenant ID
    tenantId: "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    // Redirect URI - must match Azure AD app registration
    redirectUri: "https://spaarkedev1.crm.dynamics.com"
};

/**
 * Cached MSAL instance and account.
 * @private
 */
Sprk.Communication.Send._msalInstance = null;
Sprk.Communication.Send._msalInitPromise = null;
Sprk.Communication.Send._currentAccount = null;

/**
 * Cached send-enabled accounts from sprk_communicationaccount.
 * @private
 */
Sprk.Communication.Send._cachedSendAccounts = null;
Sprk.Communication.Send._sendAccountsLoading = false;

// -----------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------

/**
 * Get the BFF API base URL.
 *
 * Resolution order:
 * 1. Dataverse environment variable (sprk_BffApiBaseUrl) if available
 * 2. Fallback to the default BFF URL
 *
 * @returns {string} The BFF API base URL (no trailing slash)
 */
Sprk.Communication.Send._getBffBaseUrl = function () {
    // Check for cached value
    if (Sprk.Communication.Send._cachedBffBaseUrl) {
        return Sprk.Communication.Send._cachedBffBaseUrl;
    }

    // Default BFF URL - configurable via Dataverse environment variable
    var defaultUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";

    try {
        // Attempt to read from Dataverse environment variable
        // This is set via Power Platform admin center or solution import
        var globalContext = Xrm.Utility.getGlobalContext();
        var clientUrl = globalContext.getClientUrl();

        // Use the Dataverse WebApi to read environment variable
        // This is async but we cache the result for subsequent calls
        Sprk.Communication.Send._loadBffBaseUrlAsync(defaultUrl);

        // Return default on first call; cached value used on subsequent calls
        return Sprk.Communication.Send._cachedBffBaseUrl || defaultUrl;
    } catch (e) {
        console.warn("[Communication Send] Could not determine BFF base URL, using default:", e);
        return defaultUrl;
    }
};

/**
 * Asynchronously load BFF base URL from Dataverse environment variable.
 * Caches the result for subsequent calls.
 * @param {string} defaultUrl - Fallback URL if environment variable not found
 * @private
 */
Sprk.Communication.Send._loadBffBaseUrlAsync = function (defaultUrl) {
    if (Sprk.Communication.Send._bffUrlLoading) {
        return;
    }
    Sprk.Communication.Send._bffUrlLoading = true;

    try {
        Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariabledefinition",
            "?$filter=schemaname eq 'sprk_BffApiBaseUrl'&$select=environmentvariabledefinitionid" +
            "&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)"
        ).then(function (result) {
            if (result.entities && result.entities.length > 0) {
                var definition = result.entities[0];
                var values = definition.environmentvariabledefinition_environmentvariablevalue;
                if (values && values.length > 0 && values[0].value) {
                    // Remove trailing slash if present
                    Sprk.Communication.Send._cachedBffBaseUrl = values[0].value.replace(/\/+$/, "");
                    console.log("[Communication Send] BFF URL from environment variable:", Sprk.Communication.Send._cachedBffBaseUrl);
                    return;
                }
            }
            Sprk.Communication.Send._cachedBffBaseUrl = defaultUrl;
            console.log("[Communication Send] Using default BFF URL:", defaultUrl);
        }).catch(function () {
            Sprk.Communication.Send._cachedBffBaseUrl = defaultUrl;
            console.log("[Communication Send] Environment variable lookup failed, using default BFF URL");
        });
    } catch (e) {
        Sprk.Communication.Send._cachedBffBaseUrl = defaultUrl;
    }
};

// -----------------------------------------------------------------------
// Enable Rules
// -----------------------------------------------------------------------

/**
 * Enable rule: Check if the current record is in Draft status.
 * The Send button should only be enabled when statuscode = 1 (Draft).
 *
 * @param {Object} formContext - The form context (PrimaryControl)
 * @returns {boolean} True if statuscode is Draft (1)
 */
Sprk.Communication.Send.isStatusDraft = function (formContext) {
    if (!formContext || !formContext.getAttribute) {
        return false;
    }

    var statusCodeAttr = formContext.getAttribute("statuscode");
    if (!statusCodeAttr) {
        return false;
    }

    var currentStatus = statusCodeAttr.getValue();
    return currentStatus === Sprk.Communication.Send.StatusCode.DRAFT;
};

// -----------------------------------------------------------------------
// Send Command Handler
// -----------------------------------------------------------------------

/**
 * Send Communication button click handler.
 *
 * Opens a send mode selection dialog (My Mailbox or shared accounts),
 * collects form data, builds the SendCommunicationRequest payload with
 * sendMode and fromMailbox fields, calls POST /api/communications/send
 * via fetch, and handles the response.
 *
 * On success: shows success notification, updates statuscode to Send (659490002),
 * saves and refreshes the form.
 *
 * On error: parses ProblemDetails response and shows error notification.
 * Special handling for expired OBO tokens (401) with user-friendly message.
 *
 * @param {Object} executionContext - The execution context from ribbon command
 */
Sprk.Communication.Send.sendCommunication = function (executionContext) {
    // Get form context - ribbon commands pass PrimaryControl directly
    var formContext = executionContext;
    if (executionContext && executionContext.getFormContext) {
        formContext = executionContext.getFormContext();
    }

    // Clear any previous notifications
    Sprk.Communication.Send._clearNotifications(formContext);

    // Validate required fields
    var validationResult = Sprk.Communication.Send._validateForm(formContext);
    if (!validationResult.isValid) {
        formContext.ui.setFormNotification(
            validationResult.message,
            "WARNING",
            Sprk.Communication.Send._NOTIFICATION_IDS.VALIDATION
        );
        return;
    }

    // Show send mode selection dialog, then proceed with send
    Sprk.Communication.Send._showSendModeDialog(formContext).then(function (sendModeResult) {
        if (!sendModeResult) {
            // User cancelled the dialog
            console.log("[Communication Send] Send cancelled by user.");
            return;
        }

        // Show progress notification
        formContext.ui.setFormNotification(
            "Sending communication...",
            "INFO",
            Sprk.Communication.Send._NOTIFICATION_IDS.PROGRESS
        );

        // Collect form data and build request payload with send mode
        var request = Sprk.Communication.Send._buildRequest(formContext, sendModeResult);

        // Get auth token and send
        Sprk.Communication.Send._sendRequest(formContext, request);
    }).catch(function (error) {
        console.error("[Communication Send] Send mode selection failed:", error);
        formContext.ui.setFormNotification(
            "Could not load send options. Please try again.",
            "ERROR",
            Sprk.Communication.Send._NOTIFICATION_IDS.ERROR
        );
    });
};

// -----------------------------------------------------------------------
// Internal: Form Data Collection
// -----------------------------------------------------------------------

/**
 * Validate required form fields before sending.
 * @param {Object} formContext - The form context
 * @returns {{isValid: boolean, message: string}} Validation result
 * @private
 */
Sprk.Communication.Send._validateForm = function (formContext) {
    var toAttr = formContext.getAttribute("sprk_to");
    var subjectAttr = formContext.getAttribute("sprk_subject");
    var bodyAttr = formContext.getAttribute("sprk_body");

    var missingFields = [];

    if (!toAttr || !toAttr.getValue() || toAttr.getValue().trim() === "") {
        missingFields.push("To");
    }
    if (!subjectAttr || !subjectAttr.getValue() || subjectAttr.getValue().trim() === "") {
        missingFields.push("Subject");
    }
    if (!bodyAttr || !bodyAttr.getValue() || bodyAttr.getValue().trim() === "") {
        missingFields.push("Body");
    }

    if (missingFields.length > 0) {
        return {
            isValid: false,
            message: "Required fields are missing: " + missingFields.join(", ")
        };
    }

    return { isValid: true, message: "" };
};

/**
 * Build SendCommunicationRequest payload from form data.
 * Includes sendMode and fromMailbox from the send mode selection result.
 *
 * @param {Object} formContext - The form context
 * @param {Object} sendModeResult - The send mode selection result
 * @param {string} sendModeResult.sendMode - "user" or "sharedMailbox"
 * @param {string|null} sendModeResult.fromMailbox - Email address for shared mailbox mode
 * @returns {Object} SendCommunicationRequest JSON payload
 * @private
 */
Sprk.Communication.Send._buildRequest = function (formContext, sendModeResult) {
    // Collect email fields
    var toValue = Sprk.Communication.Send._getFieldValue(formContext, "sprk_to");
    var ccValue = Sprk.Communication.Send._getFieldValue(formContext, "sprk_cc");
    var subjectValue = Sprk.Communication.Send._getFieldValue(formContext, "sprk_subject");
    var bodyValue = Sprk.Communication.Send._getFieldValue(formContext, "sprk_body");

    // Parse comma/semicolon-delimited recipient lists into arrays
    var toArray = Sprk.Communication.Send._parseRecipients(toValue);
    var ccArray = ccValue ? Sprk.Communication.Send._parseRecipients(ccValue) : null;

    // Get communication type (sprk_communiationtype - intentional typo in Dataverse)
    var communicationType = "Email";
    var typeAttr = formContext.getAttribute("sprk_communiationtype");
    if (typeAttr && typeAttr.getValue() !== null) {
        var typeValue = typeAttr.getValue();
        // Map option set values to string: 100000000=Email, 100000001=TeamsMessage, etc.
        switch (typeValue) {
            case 100000000: communicationType = "Email"; break;
            case 100000001: communicationType = "TeamsMessage"; break;
            case 100000002: communicationType = "SMS"; break;
            case 100000003: communicationType = "Notification"; break;
            default: communicationType = "Email";
        }
    }

    // Collect associations from regarding lookup fields
    var associations = Sprk.Communication.Send._collectAssociations(formContext);

    // Get correlation ID if present
    var correlationId = Sprk.Communication.Send._getFieldValue(formContext, "sprk_correlationid");

    // Build the request payload matching SendCommunicationRequest DTO
    var request = {
        to: toArray,
        subject: subjectValue,
        body: bodyValue,
        bodyFormat: "HTML",
        communicationType: communicationType
    };

    // Include send mode from user selection (Phase 7)
    if (sendModeResult) {
        request.sendMode = sendModeResult.sendMode;

        // Include fromMailbox only when sending via shared mailbox
        if (sendModeResult.sendMode === Sprk.Communication.Send.SendMode.SHARED_MAILBOX &&
            sendModeResult.fromMailbox) {
            request.fromMailbox = sendModeResult.fromMailbox;
        }
    }

    if (ccArray && ccArray.length > 0) {
        request.cc = ccArray;
    }

    if (associations && associations.length > 0) {
        request.associations = associations;
    }

    if (correlationId) {
        request.correlationId = correlationId;
    }

    return request;
};

/**
 * Get the string value of a form attribute.
 * @param {Object} formContext - The form context
 * @param {string} fieldName - Logical name of the attribute
 * @returns {string|null} Field value or null
 * @private
 */
Sprk.Communication.Send._getFieldValue = function (formContext, fieldName) {
    var attr = formContext.getAttribute(fieldName);
    if (!attr) {
        return null;
    }
    var val = attr.getValue();
    return val !== null && val !== undefined ? String(val) : null;
};

/**
 * Parse a comma/semicolon-delimited string of email addresses into an array.
 * @param {string} value - Comma or semicolon delimited email string
 * @returns {string[]} Array of trimmed, non-empty email addresses
 * @private
 */
Sprk.Communication.Send._parseRecipients = function (value) {
    if (!value) {
        return [];
    }
    return value.split(/[;,]/)
        .map(function (email) { return email.trim(); })
        .filter(function (email) { return email.length > 0; });
};

/**
 * Collect associations from the regarding lookup fields on the form.
 * Iterates through all 8 regarding fields and builds CommunicationAssociation objects
 * for any that have values.
 *
 * @param {Object} formContext - The form context
 * @returns {Array} Array of association objects ({entityType, entityId, entityName})
 * @private
 */
Sprk.Communication.Send._collectAssociations = function (formContext) {
    var associations = [];

    Sprk.Communication.Send._REGARDING_FIELDS.forEach(function (mapping) {
        var attr = formContext.getAttribute(mapping.field);
        if (!attr) {
            return;
        }

        var lookupValue = attr.getValue();
        if (!lookupValue || lookupValue.length === 0) {
            return;
        }

        var lookupRecord = lookupValue[0];
        associations.push({
            entityType: mapping.entityType,
            entityId: lookupRecord.id.replace(/[{}]/g, ""),
            entityName: lookupRecord.name || null
        });
    });

    return associations;
};

// -----------------------------------------------------------------------
// Internal: API Call
// -----------------------------------------------------------------------

/**
 * Send the request to the BFF API endpoint.
 * Acquires an auth token from the Dataverse context and calls POST /api/communications/send.
 *
 * @param {Object} formContext - The form context
 * @param {Object} request - The SendCommunicationRequest payload
 * @private
 */
Sprk.Communication.Send._sendRequest = function (formContext, request) {
    var bffBaseUrl = Sprk.Communication.Send._getBffBaseUrl();
    var url = bffBaseUrl + "/api/communications/send";

    // Get the bearer token via MSAL for BFF authentication.
    // Required for both send modes:
    // - SharedMailbox: BFF validates caller identity
    // - User: BFF uses token for OBO exchange to send as the user
    Sprk.Communication.Send._getAuthToken().then(function (token) {
        if (!token) {
            // Token acquisition failed — warn user
            // This is particularly important for user-mode sends where OBO requires a valid token
            console.warn("[Communication Send] No auth token acquired; request may fail for user-mode sends");
        }

        var headers = {
            "Content-Type": "application/json",
            "Accept": "application/json"
        };

        if (token) {
            headers["Authorization"] = "Bearer " + token;
        }

        return fetch(url, {
            method: "POST",
            headers: headers,
            body: JSON.stringify(request)
        });
    }).then(function (response) {
        if (response.ok) {
            return response.json().then(function (data) {
                Sprk.Communication.Send._handleSuccess(formContext, data);
            });
        } else {
            return response.json().then(function (problemDetails) {
                Sprk.Communication.Send._handleError(formContext, problemDetails, response.status);
            }).catch(function () {
                // Response body was not valid JSON
                Sprk.Communication.Send._handleError(formContext, {
                    title: "Send Failed",
                    detail: "The server returned status " + response.status + " with an unexpected response format.",
                    status: response.status,
                    errorCode: "UNKNOWN_ERROR"
                }, response.status);
            });
        }
    }).catch(function (error) {
        // Network error or fetch failure
        console.error("[Communication Send] Fetch failed:", error);
        Sprk.Communication.Send._clearNotifications(formContext);
        formContext.ui.setFormNotification(
            "Network error: Unable to reach the communication service. Please check your connection and try again.",
            "ERROR",
            Sprk.Communication.Send._NOTIFICATION_IDS.ERROR
        );
    });
};

/**
 * Load MSAL library from CDN if not already loaded.
 * Reuses the same CDN source as Spaarke.Email module.
 * @returns {Promise<void>}
 * @private
 */
Sprk.Communication.Send._loadMsalLibrary = function () {
    return new Promise(function (resolve, reject) {
        // Check if already loaded (by this module or Spaarke.Email)
        if (typeof msal !== "undefined" && msal.PublicClientApplication) {
            resolve();
            return;
        }

        // Load from CDN
        var script = document.createElement("script");
        script.src = "https://alcdn.msauth.net/browser/2.38.0/js/msal-browser.min.js";
        script.onload = function () {
            console.log("[Communication Send] MSAL library loaded");
            resolve();
        };
        script.onerror = function () {
            reject(new Error("Failed to load MSAL library"));
        };
        document.head.appendChild(script);
    });
};

/**
 * Initialize MSAL PublicClientApplication instance.
 * Reuses Spaarke.Email or Spaarke.Document MSAL instance if available.
 * @returns {Promise<Object>} MSAL instance
 * @private
 */
Sprk.Communication.Send._initMsal = function () {
    // Reuse existing MSAL instance from other Spaarke modules if available
    if (typeof Spaarke !== "undefined") {
        if (Spaarke.Email && Spaarke.Email._msalInstance) {
            console.log("[Communication Send] Reusing Spaarke.Email MSAL instance");
            Sprk.Communication.Send._msalInstance = Spaarke.Email._msalInstance;
            Sprk.Communication.Send._currentAccount = Spaarke.Email._currentAccount;
            return Promise.resolve(Sprk.Communication.Send._msalInstance);
        }
        if (Spaarke.Document && Spaarke.Document._msalInstance) {
            console.log("[Communication Send] Reusing Spaarke.Document MSAL instance");
            Sprk.Communication.Send._msalInstance = Spaarke.Document._msalInstance;
            return Promise.resolve(Sprk.Communication.Send._msalInstance);
        }
    }

    if (Sprk.Communication.Send._msalInitPromise) {
        return Sprk.Communication.Send._msalInitPromise;
    }

    var cfg = Sprk.Communication.Send._MSAL_CONFIG;

    Sprk.Communication.Send._msalInitPromise = Sprk.Communication.Send._loadMsalLibrary()
        .then(function () {
            var authorityMetadataJson = JSON.stringify({
                "authorization_endpoint": "https://login.microsoftonline.com/" + cfg.tenantId + "/oauth2/v2.0/authorize",
                "token_endpoint": "https://login.microsoftonline.com/" + cfg.tenantId + "/oauth2/v2.0/token",
                "issuer": "https://login.microsoftonline.com/" + cfg.tenantId + "/v2.0",
                "jwks_uri": "https://login.microsoftonline.com/" + cfg.tenantId + "/discovery/v2.0/keys",
                "end_session_endpoint": "https://login.microsoftonline.com/" + cfg.tenantId + "/oauth2/v2.0/logout"
            });

            var config = {
                auth: {
                    clientId: cfg.clientId,
                    authority: "https://login.microsoftonline.com/" + cfg.tenantId,
                    redirectUri: cfg.redirectUri,
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
                        loggerCallback: function (level, message, containsPii) {
                            if (containsPii) return;
                            if (level <= 1) { // Only log errors and warnings
                                console.log("[Communication Send MSAL] " + message);
                            }
                        },
                        logLevel: 3
                    }
                }
            };

            Sprk.Communication.Send._msalInstance = new msal.PublicClientApplication(config);
            console.log("[Communication Send] MSAL PublicClientApplication created");

            if (typeof Sprk.Communication.Send._msalInstance.initialize === "function") {
                return Sprk.Communication.Send._msalInstance.initialize().then(function () {
                    return Sprk.Communication.Send._msalInstance.handleRedirectPromise();
                });
            } else {
                return Sprk.Communication.Send._msalInstance.handleRedirectPromise();
            }
        })
        .then(function (redirectResponse) {
            if (redirectResponse) {
                console.log("[Communication Send] Redirect response processed");
                Sprk.Communication.Send._currentAccount = redirectResponse.account;
            } else {
                var accounts = Sprk.Communication.Send._msalInstance.getAllAccounts();
                if (accounts.length > 0) {
                    Sprk.Communication.Send._currentAccount = accounts[0];
                    console.log("[Communication Send] Existing account found:", accounts[0].username);
                }
            }
            return Sprk.Communication.Send._msalInstance;
        });

    return Sprk.Communication.Send._msalInitPromise;
};

/**
 * Get an authentication token for the BFF API.
 *
 * Uses MSAL to acquire a bearer token for cross-origin BFF API calls.
 * Token is required for both shared mailbox sends (BFF validates caller)
 * and user-mode sends (BFF uses OBO to send as the user).
 *
 * Token acquisition strategy:
 * 1. acquireTokenSilent (cached token)
 * 2. ssoSilent (Dataverse session SSO)
 * 3. acquireTokenPopup (interactive fallback)
 *
 * @returns {Promise<string|null>} Promise resolving to auth token or null
 * @private
 */
Sprk.Communication.Send._getAuthToken = function () {
    var cfg = Sprk.Communication.Send._MSAL_CONFIG;
    var scope = "api://" + cfg.bffAppId + "/SDAP.Access";

    return Sprk.Communication.Send._initMsal().then(function (msalInstance) {
        // Strategy 1: Silent token acquisition from cache
        if (Sprk.Communication.Send._currentAccount) {
            console.log("[Communication Send] Attempting acquireTokenSilent...");
            return msalInstance.acquireTokenSilent({
                scopes: [scope],
                account: Sprk.Communication.Send._currentAccount
            }).then(function (response) {
                console.log("[Communication Send] acquireTokenSilent succeeded");
                return response.accessToken;
            }).catch(function () {
                console.log("[Communication Send] acquireTokenSilent failed, trying ssoSilent...");
                return Sprk.Communication.Send._getAuthTokenSsoFallback(msalInstance, scope);
            });
        }

        // No cached account, try SSO directly
        return Sprk.Communication.Send._getAuthTokenSsoFallback(msalInstance, scope);
    }).catch(function (error) {
        console.error("[Communication Send] Auth token acquisition failed:", error);
        return null;
    });
};

/**
 * SSO and popup fallback for token acquisition.
 * @param {Object} msalInstance - The MSAL instance
 * @param {string} scope - The token scope
 * @returns {Promise<string|null>} Access token or null
 * @private
 */
Sprk.Communication.Send._getAuthTokenSsoFallback = function (msalInstance, scope) {
    // Strategy 2: SSO silent (uses Dataverse session)
    return msalInstance.ssoSilent({ scopes: [scope] }).then(function (response) {
        console.log("[Communication Send] ssoSilent succeeded");
        if (response.account) {
            Sprk.Communication.Send._currentAccount = response.account;
        }
        return response.accessToken;
    }).catch(function () {
        // Strategy 3: Interactive popup
        console.log("[Communication Send] Falling back to popup authentication...");
        var popupRequest = {
            scopes: [scope],
            loginHint: Sprk.Communication.Send._currentAccount
                ? Sprk.Communication.Send._currentAccount.username
                : undefined
        };
        return msalInstance.acquireTokenPopup(popupRequest).then(function (response) {
            console.log("[Communication Send] Popup authentication succeeded");
            if (response.account) {
                Sprk.Communication.Send._currentAccount = response.account;
            }
            return response.accessToken;
        });
    });
};

// -----------------------------------------------------------------------
// Internal: Send Mode Selection
// -----------------------------------------------------------------------

/**
 * Query send-enabled shared accounts from sprk_communicationaccount.
 * Results are cached for the duration of the session.
 *
 * Queries: sprk_communicationaccount where sprk_sendenableds = true
 * Returns: Array of {name, email, isDefault} objects
 *
 * @returns {Promise<Array>} Array of send-enabled account objects
 * @private
 */
Sprk.Communication.Send._loadSendEnabledAccounts = function () {
    // Return cached results if available
    if (Sprk.Communication.Send._cachedSendAccounts !== null) {
        return Promise.resolve(Sprk.Communication.Send._cachedSendAccounts);
    }

    // Prevent concurrent requests
    if (Sprk.Communication.Send._sendAccountsLoading) {
        return new Promise(function (resolve) {
            // Poll until cache is populated
            var checkInterval = setInterval(function () {
                if (Sprk.Communication.Send._cachedSendAccounts !== null) {
                    clearInterval(checkInterval);
                    resolve(Sprk.Communication.Send._cachedSendAccounts);
                }
            }, 100);

            // Timeout after 10 seconds
            setTimeout(function () {
                clearInterval(checkInterval);
                resolve([]);
            }, 10000);
        });
    }

    Sprk.Communication.Send._sendAccountsLoading = true;

    // Query sprk_communicationaccount where send is enabled
    // Uses actual Dataverse field names: sprk_sendenableds (trailing 's'), sprk_emailaddress, etc.
    var filter = "?$filter=sprk_sendenableds eq true and statecode eq 0" +
        "&$select=sprk_name,sprk_emailaddress,sprk_displayname,sprk_isdefaultsender,sprk_accounttype" +
        "&$orderby=sprk_isdefaultsender desc,sprk_name asc";

    return Xrm.WebApi.retrieveMultipleRecords("sprk_communicationaccount", filter)
        .then(function (result) {
            var accounts = [];

            if (result.entities && result.entities.length > 0) {
                result.entities.forEach(function (entity) {
                    accounts.push({
                        name: entity.sprk_displayname || entity.sprk_name || entity.sprk_emailaddress,
                        email: entity.sprk_emailaddress,
                        isDefault: entity.sprk_isdefaultsender === true,
                        accountType: entity.sprk_accounttype
                    });
                });
            }

            Sprk.Communication.Send._cachedSendAccounts = accounts;
            Sprk.Communication.Send._sendAccountsLoading = false;
            console.log("[Communication Send] Loaded " + accounts.length + " send-enabled accounts");
            return accounts;
        })
        .catch(function (error) {
            console.error("[Communication Send] Failed to load send-enabled accounts:", error);
            Sprk.Communication.Send._cachedSendAccounts = [];
            Sprk.Communication.Send._sendAccountsLoading = false;
            return [];
        });
};

/**
 * Show send mode selection dialog to the user.
 *
 * Presents a dialog with options:
 * - "My Mailbox" (sendMode: "user") - sends from the user's own mailbox via OBO
 * - Shared account options (sendMode: "sharedMailbox") - sends from shared mailbox
 *
 * Uses Xrm.Navigation.openAlertDialog for the selection UI since model-driven
 * apps do not support custom HTML dialogs. The dialog presents a numbered list
 * and uses a confirm dialog to capture the choice.
 *
 * When only one shared account exists, uses a simple confirm dialog.
 * When multiple shared accounts exist, presents numbered options.
 *
 * @param {Object} formContext - The form context
 * @returns {Promise<Object|null>} Send mode result or null if cancelled
 *   - {sendMode: "user"} for My Mailbox
 *   - {sendMode: "sharedMailbox", fromMailbox: "email@..."} for shared mailbox
 * @private
 */
Sprk.Communication.Send._showSendModeDialog = function (formContext) {
    return Sprk.Communication.Send._loadSendEnabledAccounts().then(function (accounts) {
        // If no shared accounts configured, default to user mode without dialog
        if (!accounts || accounts.length === 0) {
            console.log("[Communication Send] No shared accounts configured, defaulting to user mode");
            return { sendMode: Sprk.Communication.Send.SendMode.USER };
        }

        // Find the default shared account
        var defaultAccount = null;
        for (var i = 0; i < accounts.length; i++) {
            if (accounts[i].isDefault) {
                defaultAccount = accounts[i];
                break;
            }
        }
        // Fall back to first account if no default
        if (!defaultAccount && accounts.length > 0) {
            defaultAccount = accounts[0];
        }

        // Build the option list for display
        var optionLines = [];
        optionLines.push("1. My Mailbox (send as yourself)");
        for (var j = 0; j < accounts.length; j++) {
            var acct = accounts[j];
            var label = acct.name + " (" + acct.email + ")";
            if (acct.isDefault) {
                label += " [Default]";
            }
            optionLines.push((j + 2) + ". " + label);
        }

        var dialogText = "Choose how to send this email:\n\n" +
            optionLines.join("\n") +
            "\n\nClick OK to send from the default shared mailbox" +
            (defaultAccount ? " (" + defaultAccount.email + ")" : "") +
            ", or Cancel to send from your own mailbox.";

        // Use confirm dialog: OK = default shared mailbox, Cancel = My Mailbox
        // For simplicity in the Dataverse dialog constraint, we use two-option confirm.
        // If multiple shared accounts exist, the user can pre-set the from field
        // on the form (sprk_from) to override which shared account to use.
        return Xrm.Navigation.openConfirmDialog({
            title: "Send Mode",
            text: dialogText,
            confirmButtonLabel: "Send from Shared Mailbox",
            cancelButtonLabel: "Send from My Mailbox"
        }).then(function (dialogResult) {
            if (dialogResult.confirmed) {
                // User chose shared mailbox mode
                // Check if from field has a pre-set email for shared account override
                var fromFieldValue = Sprk.Communication.Send._getFieldValue(formContext, "sprk_from");
                var selectedMailbox = null;

                if (fromFieldValue) {
                    // Check if the from field matches a known shared account
                    var fromEmail = fromFieldValue.trim().toLowerCase();
                    for (var k = 0; k < accounts.length; k++) {
                        if (accounts[k].email.toLowerCase() === fromEmail) {
                            selectedMailbox = accounts[k].email;
                            break;
                        }
                    }
                }

                // Default to the default shared account if no valid override
                if (!selectedMailbox && defaultAccount) {
                    selectedMailbox = defaultAccount.email;
                }

                console.log("[Communication Send] User selected shared mailbox mode:", selectedMailbox);
                return {
                    sendMode: Sprk.Communication.Send.SendMode.SHARED_MAILBOX,
                    fromMailbox: selectedMailbox
                };
            } else {
                // User chose My Mailbox (user mode)
                console.log("[Communication Send] User selected My Mailbox mode");
                return { sendMode: Sprk.Communication.Send.SendMode.USER };
            }
        });
    });
};

// -----------------------------------------------------------------------
// Internal: Response Handling
// -----------------------------------------------------------------------

/**
 * Handle a successful send response.
 * Updates the form status to Send (659490002), shows success notification,
 * saves and refreshes the form.
 *
 * @param {Object} formContext - The form context
 * @param {Object} response - The SendCommunicationResponse from BFF
 * @private
 */
Sprk.Communication.Send._handleSuccess = function (formContext, response) {
    console.log("[Communication Send] Send successful:", response);

    // Clear progress notification
    Sprk.Communication.Send._clearNotifications(formContext);

    // Update statuscode to Send (659490002)
    var statusCodeAttr = formContext.getAttribute("statuscode");
    if (statusCodeAttr) {
        statusCodeAttr.setValue(Sprk.Communication.Send.StatusCode.SEND);
    }

    // Update sent timestamp if response includes it
    if (response.sentAt) {
        var sentAtAttr = formContext.getAttribute("sprk_sentat");
        if (sentAtAttr) {
            sentAtAttr.setValue(new Date(response.sentAt));
        }
    }

    // Update Graph message ID
    if (response.graphMessageId) {
        var graphIdAttr = formContext.getAttribute("sprk_graphmessageid");
        if (graphIdAttr) {
            graphIdAttr.setValue(response.graphMessageId);
        }
    }

    // Update from field
    if (response.from) {
        var fromAttr = formContext.getAttribute("sprk_from");
        if (fromAttr) {
            fromAttr.setValue(response.from);
        }
    }

    // Show success notification
    formContext.ui.setFormNotification(
        "Communication sent successfully.",
        "INFO",
        Sprk.Communication.Send._NOTIFICATION_IDS.SUCCESS
    );

    // Save the form to persist the status change
    formContext.data.save().then(function () {
        // Refresh form to reflect the new status (read mode, button disabled)
        formContext.data.refresh(false);

        // Auto-clear success notification after 5 seconds
        setTimeout(function () {
            try {
                formContext.ui.clearFormNotification(
                    Sprk.Communication.Send._NOTIFICATION_IDS.SUCCESS
                );
            } catch (e) {
                // Form may have been navigated away
            }
        }, 5000);
    }).catch(function (error) {
        console.error("[Communication Send] Save after send failed:", error);
        formContext.ui.setFormNotification(
            "Email was sent, but the form failed to save. Please refresh and verify the status.",
            "WARNING",
            Sprk.Communication.Send._NOTIFICATION_IDS.ERROR
        );
    });
};

/**
 * Handle an error response from the BFF.
 * Parses ProblemDetails (RFC 7807) and displays a user-friendly error notification.
 *
 * ProblemDetails format:
 * {
 *   type: "https://spaarke.com/errors/{code}",
 *   title: "Short description",
 *   detail: "Detailed message",
 *   status: 400,
 *   errorCode: "INVALID_SENDER",
 *   correlationId: "abc123"
 * }
 *
 * @param {Object} formContext - The form context
 * @param {Object} problemDetails - The ProblemDetails error response
 * @param {number} httpStatus - The HTTP status code
 * @private
 */
Sprk.Communication.Send._handleError = function (formContext, problemDetails, httpStatus) {
    console.error("[Communication Send] Send failed:", httpStatus, problemDetails);

    // Clear progress notification
    Sprk.Communication.Send._clearNotifications(formContext);

    // Handle expired OBO token specifically (401 Unauthorized)
    // This occurs when the user's delegated token has expired and the BFF
    // cannot complete the OBO exchange for user-mode sends.
    if (httpStatus === 401) {
        var errorCode401 = problemDetails.errorCode || problemDetails.code || "";
        var isOboError = errorCode401 === "OBO_TOKEN_EXPIRED" ||
            errorCode401 === "UNAUTHORIZED" ||
            (problemDetails.detail && problemDetails.detail.toLowerCase().indexOf("token") !== -1);

        if (isOboError) {
            // Clear MSAL cached account to force re-authentication on next attempt
            Sprk.Communication.Send._currentAccount = null;
            Sprk.Communication.Send._msalInitPromise = null;

            formContext.ui.setFormNotification(
                "Your session has expired. Please refresh the page and try again.",
                "ERROR",
                Sprk.Communication.Send._NOTIFICATION_IDS.ERROR
            );
            return;
        }

        // Generic 401 - still show refresh message
        formContext.ui.setFormNotification(
            "Authentication failed. Please refresh the page and try again.",
            "ERROR",
            Sprk.Communication.Send._NOTIFICATION_IDS.ERROR
        );
        return;
    }

    // Build user-friendly error message from ProblemDetails
    var title = problemDetails.title || "Send Failed";
    var detail = problemDetails.detail || "An unexpected error occurred while sending the communication.";
    var errorCode = problemDetails.errorCode || problemDetails.code || "";
    var correlationId = problemDetails.correlationId || "";

    // Format: "title: detail"
    var errorMessage = title + ": " + detail;

    // Append error code and correlation ID for support reference
    if (errorCode) {
        errorMessage += " (Error: " + errorCode + ")";
    }
    if (correlationId) {
        errorMessage += " [Ref: " + correlationId + "]";
    }

    formContext.ui.setFormNotification(
        errorMessage,
        "ERROR",
        Sprk.Communication.Send._NOTIFICATION_IDS.ERROR
    );
};

/**
 * Clear all form notifications set by this web resource.
 * @param {Object} formContext - The form context
 * @private
 */
Sprk.Communication.Send._clearNotifications = function (formContext) {
    try {
        var ids = Sprk.Communication.Send._NOTIFICATION_IDS;
        formContext.ui.clearFormNotification(ids.SUCCESS);
        formContext.ui.clearFormNotification(ids.ERROR);
        formContext.ui.clearFormNotification(ids.VALIDATION);
        formContext.ui.clearFormNotification(ids.PROGRESS);
    } catch (e) {
        // Ignore errors when clearing notifications
    }
};

/* eslint-enable no-undef */
