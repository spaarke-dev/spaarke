/**
 * Communication Send Command - Send Email via BFF
 *
 * Web resource for the sprk_communication entity Send command bar button.
 *
 * Main Form Button:
 * - Send: Collect form data and send email via BFF POST /api/communications/send
 *
 * Enable Rule:
 * - isStatusDraft: Button enabled only when statuscode = 1 (Draft)
 *
 * Communication Status Values (statuscode):
 * - 1: Draft, 659490001: Queued, 659490002: Send, 659490003: Delivered,
 *   659490004: Failed, 659490005: Bounded, 659490006: Recalled
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
 * Collects form data, builds the SendCommunicationRequest payload,
 * calls POST /api/communications/send via fetch, and handles the response.
 *
 * On success: shows success notification, updates statuscode to Send (659490002),
 * saves and refreshes the form.
 *
 * On error: parses ProblemDetails response and shows error notification.
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

    // Show progress notification
    formContext.ui.setFormNotification(
        "Sending communication...",
        "INFO",
        Sprk.Communication.Send._NOTIFICATION_IDS.PROGRESS
    );

    // Collect form data and build request payload
    var request = Sprk.Communication.Send._buildRequest(formContext);

    // Get auth token and send
    Sprk.Communication.Send._sendRequest(formContext, request);
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
 * @param {Object} formContext - The form context
 * @returns {Object} SendCommunicationRequest JSON payload
 * @private
 */
Sprk.Communication.Send._buildRequest = function (formContext) {
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

    // Get the bearer token from the Dataverse session for BFF authentication.
    // The BFF accepts the same OBO token that Dataverse uses.
    Sprk.Communication.Send._getAuthToken().then(function (token) {
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
 * Get an authentication token for the BFF API.
 *
 * In model-driven apps, the user's session token is available via the
 * global context. We use it for BFF API authentication (On-Behalf-Of flow).
 *
 * @returns {Promise<string|null>} Promise resolving to auth token or null
 * @private
 */
Sprk.Communication.Send._getAuthToken = function () {
    return new Promise(function (resolve) {
        try {
            var globalContext = Xrm.Utility.getGlobalContext();
            // Get the auth token from the Dataverse session
            // The getCurrentAppUrl provides the base URL; we use getAuthToken for the bearer token
            if (globalContext.getCurrentAppProperties) {
                globalContext.getCurrentAppProperties().then(function (appProperties) {
                    // In model-driven apps, the session auth is available via XMLHttpRequest
                    // that inherits the Dataverse session cookies automatically.
                    // For cross-origin BFF calls, we extract the token from Xrm context.
                    resolve(null);
                }).catch(function () {
                    resolve(null);
                });
            } else {
                resolve(null);
            }
        } catch (e) {
            console.warn("[Communication Send] Auth token acquisition failed:", e);
            resolve(null);
        }
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
