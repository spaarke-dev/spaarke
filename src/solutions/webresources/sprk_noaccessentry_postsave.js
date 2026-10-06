/**
 * No Access Entry main form - Post-Save enforcement (unified-access-control-r2 task 143, GitHub #1066)
 *
 * After a No Access entry (sprk_noaccessentry) is saved, asks the BFF to enforce it NOW: the direct shares it walls
 * off on secure records are removed within seconds (owner round 3 R3: "immediate on save - the form calls the BFF").
 * The 5-minute NoAccessShareReconciliationJob is the safety net for anything this call does not reach.
 *
 * Web Resource Name: sprk_/scripts/noaccessentry_postsave.js
 *
 * Form Events (registered by task 154, which delivers the usable entry form - NOT registered by task 143):
 * - OnLoad: Spaarke.NoAccessEntry.onLoad (pass execution context) - registers the OnPostSave handler
 * - Library order: sprk_/scripts/bff_auth.js FIRST, then this library
 *
 * What the user sees (text-only form notifications - owner O1 final):
 * - "not enforced: you do not hold Write on ..." when the entry's author lacks Write on a covered record (owner N5);
 * - "access through a team or a role cannot be removed per person ..." (owner N2) - Teams/SPA hides the record;
 * - "kept: the last person who can open ..." when a removal would leave a secure record with nobody (owner S5);
 * - the server's own message when enforcement could not be completed. The job retries within 5 minutes.
 *
 * Constraints:
 * - addOnPostSave (not addOnSave): the record is saved first, and the save is NEVER blocked or failed by this call.
 * - The request carries ONLY the entry id; the server reads everything else from the entry.
 * - Never throws; a missing BFF URL, auth helper or token skips the call (the job enforces within 5 minutes).
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.NoAccessEntry = Spaarke.NoAccessEntry || {};

Spaarke.NoAccessEntry.Config = {
    /** @type {string|null} Resolved BFF base URL (sprk_BffApiBaseUrl environment variable). */
    apiBaseUrl: null,
    enforcePath: "/api/v1/external-access/no-access/enforce",
    notificationId: "sprk_noaccess_enforcement",
    version: "1.0.0"
};

Spaarke.NoAccessEntry._cachedApiBaseUrl = null;

/** Resolves the BFF base URL from the sprk_BffApiBaseUrl environment variable (value override, else default). */
Spaarke.NoAccessEntry.getApiBaseUrl = function () {
    if (Spaarke.NoAccessEntry._cachedApiBaseUrl) {
        return Promise.resolve(Spaarke.NoAccessEntry._cachedApiBaseUrl);
    }

    return Xrm.WebApi.retrieveMultipleRecords(
        "environmentvariabledefinition",
        "?$filter=schemaname eq 'sprk_BffApiBaseUrl'&$select=environmentvariabledefinitionid,defaultvalue"
    ).then(function (definitions) {
        if (!definitions.entities || definitions.entities.length === 0) {
            throw new Error("Environment variable sprk_BffApiBaseUrl is not defined.");
        }

        var definition = definitions.entities[0];
        return Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariablevalue",
            "?$filter=_environmentvariabledefinitionid_value eq '" + definition.environmentvariabledefinitionid +
            "'&$select=value"
        ).then(function (values) {
            var value = values.entities && values.entities.length > 0 ? values.entities[0].value : definition.defaultvalue;
            if (!value) {
                throw new Error("Environment variable sprk_BffApiBaseUrl has no value.");
            }

            Spaarke.NoAccessEntry._cachedApiBaseUrl = value.replace(/\/+$/, "");
            return Spaarke.NoAccessEntry._cachedApiBaseUrl;
        });
    });
};

/** OnLoad: registers the post-save handler. Never throws. */
Spaarke.NoAccessEntry.onLoad = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        formContext.data.entity.addOnPostSave(Spaarke.NoAccessEntry.onPostSave);
        Spaarke.NoAccessEntry.getApiBaseUrl().then(function (url) {
            Spaarke.NoAccessEntry.Config.apiBaseUrl = url;
        }).catch(function (error) {
            console.error("[No Access] Could not resolve the BFF URL; enforcement falls to the 5-minute job.", error);
        });
    } catch (error) {
        console.error("[No Access] Error in onLoad:", error);
    }
};

/** OnPostSave: fire-and-forget enforcement. Never throws, never blocks the save. */
Spaarke.NoAccessEntry.onPostSave = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        var entryId = (formContext.data.entity.getId() || "").replace(/[{}]/g, "");
        if (!entryId) {
            return;
        }

        Spaarke.NoAccessEntry._enforce(formContext, entryId);
    } catch (error) {
        console.error("[No Access] Error in onPostSave:", error);
    }
};

Spaarke.NoAccessEntry._notify = function (formContext, text, level) {
    try {
        formContext.ui.clearFormNotification(Spaarke.NoAccessEntry.Config.notificationId);
        if (text) {
            formContext.ui.setFormNotification(text, level, Spaarke.NoAccessEntry.Config.notificationId);
        }
    } catch (error) {
        console.error("[No Access] Could not show the notification:", error);
    }
};

/** Builds the user-facing summary from a 200 report. Text only (owner O1). */
Spaarke.NoAccessEntry._summarize = function (report) {
    var messages = [];
    var removed = (report.removed || []).length;
    if (removed > 0) {
        messages.push("Removed " + removed + " direct share(s) this entry walls off.");
    }

    var notEnforced = report.notEnforced || [];
    if (notEnforced.some(function (n) { return n.reason === "author-lacks-write"; })) {
        messages.push("Not enforced on some records: you do not hold Write on them, so their shares were not removed.");
    }

    if (notEnforced.some(function (n) { return n.reason === "author-not-a-person"; })) {
        messages.push("Not enforced: the entry's author is not an enabled person.");
    }

    if ((report.notEnforceable || []).length > 0) {
        messages.push("Some access comes from a team or a role and cannot be removed per person; Teams and the " +
            "portal hide the record, the model-driven app may still show it.");
    }

    if (report.truncated) {
        messages.push("The entry covers more than one pass handles; the rest is enforced within 5 minutes.");
    }

    return messages.join(" ");
};

Spaarke.NoAccessEntry._enforce = async function (formContext, entryId) {
    try {
        var baseUrl = Spaarke.NoAccessEntry.Config.apiBaseUrl || await Spaarke.NoAccessEntry.getApiBaseUrl();
        if (typeof Spaarke.BffAuth === "undefined" || !Spaarke.BffAuth.getToken) {
            console.error("[No Access] Spaarke.BffAuth is not loaded (register sprk_/scripts/bff_auth.js first); " +
                "the 5-minute job will enforce this entry.");
            return;
        }

        var token = await Spaarke.BffAuth.getToken(baseUrl);
        if (!token) {
            console.warn("[No Access] No BFF token; the 5-minute job will enforce this entry.");
            return;
        }

        var response = await fetch(baseUrl + Spaarke.NoAccessEntry.Config.enforcePath, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "Authorization": "Bearer " + token
            },
            body: JSON.stringify({ entryId: entryId })
        });

        var body = null;
        try {
            body = await response.json();
        } catch (parseError) {
            body = null;
        }

        if (response.ok) {
            var summary = Spaarke.NoAccessEntry._summarize(body || {});
            Spaarke.NoAccessEntry._notify(formContext, summary, summary.indexOf("Not enforced") >= 0 ? "WARNING" : "INFO");
            return;
        }

        var detail = body && body.detail
            ? body.detail
            : "This entry could not be enforced now (" + response.status + "); it is retried within 5 minutes.";
        Spaarke.NoAccessEntry._notify(formContext, detail, response.status === 409 ? "WARNING" : "ERROR");
    } catch (error) {
        console.error("[No Access] Enforcement call failed; the 5-minute job will enforce this entry.", error);
        Spaarke.NoAccessEntry._notify(formContext,
            "This entry could not be enforced now; it is retried within 5 minutes.", "WARNING");
    }
};
