/**
 * Project / Matter / Work Assignment main forms - Assigned-To access on save (unified-access-control-r2 task 142,
 * GitHub #1065)
 *
 * After the record is saved, asks the BFF to apply the Assigned-To rule to it NOW (owner round 2 Q5 + round 3 R3:
 * "immediate on save - the form calls the BFF"): every contact or organization named in an "Assigned *" column gets
 * Collaborate access (a removable grant, or a share for a contact linked to an internal user), a secure record SUGGESTS
 * instead (owner A3), an assignment that changed loses its unmodified automatic access (owner A4), and the record's
 * No Access entries are re-applied in the same call (task 143). The 5-minute AssignedAccessReconciliationJob is the
 * safety net for anything this call does not reach (grid edits, imports, a failed call).
 *
 * Web Resource Name: sprk_/scripts/assignedaccess_postsave.js
 *
 * Form Events (sprk_project, sprk_matter and sprk_workassignment MAIN forms):
 * - OnLoad: Spaarke.AssignedAccess.onLoad (pass execution context) - registers the OnPostSave handler
 * - Library order: sprk_/scripts/bff_auth.js FIRST, then this library
 *
 * Also the shared core of the "Access > Update Access" ribbon command (sprk_/scripts/access_ribbon.js), which lists
 * this library ahead of itself: ONE sync call, ONE summary - never a second copy.
 *
 * What the user sees (a text-only form notification - owner O1 final; nothing when nothing changed):
 * - "Gave N people access: ..." / "Removed automatic access for N people whose assignment changed."
 * - "N people are suggested in Manage Access (this record is secure)."
 * - "Not given access: ..." with the reason (Restricted, the No Access list, ...).
 * - the server's own message when the update could not be completed. The job retries within 5 minutes.
 *
 * Constraints:
 * - addOnPostSave (not addOnSave): the record is saved first, and the save is NEVER blocked or failed by this call.
 * - The request carries ONLY the record type and id; the server reads the Assigned columns from the record itself.
 * - Never throws; a missing BFF URL, auth helper or token skips the call (the job grants within 5 minutes).
 * - Spaarke.BffAuth.getToken never opens a popup (ADR-028): no token = no call.
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.AssignedAccess = Spaarke.AssignedAccess || {};

Spaarke.AssignedAccess.Config = {
    /** @type {string|null} Resolved BFF base URL (sprk_BffApiBaseUrl environment variable). */
    apiBaseUrl: null,
    syncPath: "/api/v1/external-access/assigned-access/sync",
    notificationId: "sprk_assignedaccess_sync",
    version: "1.1.0"
};

/** The record types the sync route accepts, by table. */
Spaarke.AssignedAccess.RecordTypes = {
    sprk_project: "project",
    sprk_matter: "matter",
    sprk_workassignment: "workassignment"
};

Spaarke.AssignedAccess._cachedApiBaseUrl = null;

/** Resolves the BFF base URL from the sprk_BffApiBaseUrl environment variable (value override, else default). */
Spaarke.AssignedAccess.getApiBaseUrl = function () {
    if (Spaarke.AssignedAccess._cachedApiBaseUrl) {
        return Promise.resolve(Spaarke.AssignedAccess._cachedApiBaseUrl);
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

            Spaarke.AssignedAccess._cachedApiBaseUrl = value.replace(/\/+$/, "");
            return Spaarke.AssignedAccess._cachedApiBaseUrl;
        });
    });
};

/** The record a form shows, as the sync route names it - or null for an unsaved or unsupported record. */
Spaarke.AssignedAccess.recordOf = function (formContext) {
    var entity = formContext && formContext.data && formContext.data.entity;
    if (!entity) {
        return null;
    }

    var recordType = Spaarke.AssignedAccess.RecordTypes[entity.getEntityName()];
    var recordId = (entity.getId() || "").replace(/[{}]/g, "").toLowerCase();
    return recordType && recordId ? { recordType: recordType, recordId: recordId } : null;
};

/**
 * Calls the sync route for one record. Resolves (never rejects) with
 * { ok, status, body, skipped, reason } - `skipped` when no call was made (no helper, no token, no URL).
 */
Spaarke.AssignedAccess.sync = async function (record) {
    try {
        var baseUrl = Spaarke.AssignedAccess.Config.apiBaseUrl || await Spaarke.AssignedAccess.getApiBaseUrl();
        if (typeof Spaarke.BffAuth === "undefined" || !Spaarke.BffAuth.getToken) {
            console.error("[Assigned Access] Spaarke.BffAuth is not loaded (register sprk_/scripts/bff_auth.js first); " +
                "the 5-minute job will update this record.");
            return { ok: false, skipped: true, reason: "auth-helper-missing" };
        }

        var token = await Spaarke.BffAuth.getToken(baseUrl);
        if (!token) {
            return { ok: false, skipped: true, reason: "no-token" };
        }

        var response = await fetch(baseUrl + Spaarke.AssignedAccess.Config.syncPath, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "Authorization": "Bearer " + token
            },
            body: JSON.stringify({ recordType: record.recordType, recordId: record.recordId })
        });

        var body = null;
        try {
            body = await response.json();
        } catch (parseError) {
            body = null;
        }

        return { ok: response.ok, status: response.status, body: body, skipped: false };
    } catch (error) {
        console.error("[Assigned Access] The sync call failed; the 5-minute job will update this record.", error);
        return { ok: false, skipped: false, status: 0, reason: "network" };
    }
};

/** Human labels for the server's skip reasons (text only). */
Spaarke.AssignedAccess.ReasonText = {
    "restricted": "the record is Restricted",
    "organization-on-secure": "the record is secure (organizations are not granted)",
    "organization-on-limited": "the record is Limited (organizations are not granted)",
    "root-inactive": "the record is inactive",
    "no-access": "they are on the record's No Access list",
    "no-access-unverifiable": "the No Access list could not be checked",
    "removed-by-no-access": "the No Access list removed their access",
    "ineligible": "their user account is disabled or not a person",
    "link-unreadable": "their user link could not be read",
    "link-ambiguous": "they are linked to more than one user",
    "subject-inactive": "they are inactive",
    "subject-not-found": "they no longer exist"
};

/**
 * Builds the one-line, text-only summary of a sync response (owner O1: text only). Empty when nothing changed and
 * nothing needs saying.
 */
Spaarke.AssignedAccess.summarize = function (response) {
    var messages = [];
    var outcome = (response && response.assignedAccess) || {};
    var entries = outcome.entries || [];

    var count = function (predicate) { return entries.filter(predicate).length; };
    var given = count(function (e) {
        return ["granted", "raised", "shared", "converted"].indexOf(e.action) >= 0 ||
            (e.action === "restored" && e.state !== "Revoked");
    });
    if (given > 0) {
        messages.push("Gave " + given + " assigned " + (given === 1 ? "person" : "people") + " access to this record.");
    }

    var renewed = count(function (e) { return e.action === "renewed"; });
    if (renewed > 0) {
        messages.push("Renewed " + renewed + " automatic grant(s) for 90 days.");
    }

    var revoked = count(function (e) { return e.action === "revoked" || (e.action === "restored" && e.state === "Revoked"); });
    if (revoked > 0) {
        messages.push("Removed automatic access for " + revoked + " " + (revoked === 1 ? "person" : "people") +
            " whose assignment changed.");
    }

    var pending = count(function (e) { return e.state === "PendingConfirmation" && e.action === "ledger"; });
    if (pending > 0) {
        messages.push(pending + " assigned " + (pending === 1 ? "person is" : "people are") +
            " suggested in Manage Access (this record is secure).");
    }

    var skipped = entries.filter(function (e) {
        return e.state === "Skipped" && Spaarke.AssignedAccess.ReasonText[e.reason];
    });
    if (skipped.length > 0) {
        var reasons = skipped
            .map(function (e) { return Spaarke.AssignedAccess.ReasonText[e.reason]; })
            .filter(function (r, i, all) { return all.indexOf(r) === i; });
        messages.push("Not given access: " + skipped.length + " assigned " + (skipped.length === 1 ? "person" : "people") +
            " (" + reasons.join("; ") + ").");
    }

    var noAccess = (response && response.noAccess) || [];
    var removedShares = noAccess.reduce(function (n, r) { return n + ((r.removed || []).length); }, 0);
    if (removedShares > 0) {
        messages.push("Removed " + removedShares + " share(s) the No Access list forbids.");
    }

    var notEnforced = noAccess.reduce(function (n, r) { return n + ((r.notEnforced || []).length); }, 0);
    if (notEnforced > 0) {
        messages.push("No Access was not enforced on " + notEnforced + " item(s) (see the entry for why).");
    }

    var notEnforceable = noAccess.reduce(function (n, r) { return n + ((r.notEnforceable || []).length); }, 0);
    if (notEnforceable > 0) {
        messages.push("Some access comes from a team or a role and cannot be removed per person; Teams and the portal " +
            "hide the record.");
    }

    // Task 114 (owner round 67): a Restricted record keeps no share of a user flagged external.
    var restricted = (response && response.restrictedExternal) || {};
    var removedExternal = (restricted.removed || []).length;
    if (removedExternal > 0) {
        messages.push("Removed access for " + removedExternal + " external " + (removedExternal === 1 ? "user" : "users") +
            ": this record is Restricted to internal users.");
    }

    var keptExternal = (restricted.keptAsLastReader || []).length;
    if (keptExternal > 0) {
        messages.push(keptExternal + " external " + (keptExternal === 1 ? "user still has" : "users still have") +
            " access: they are the only people who can open this secure record. Share it with an internal person, then save again.");
    }

    return messages.join(" ");
};

/** The message for a sync that did not succeed: the server's own sentence when it sent one. */
Spaarke.AssignedAccess.failureText = function (result) {
    if (result && result.body && result.body.detail) {
        return result.body.detail;
    }

    return "Access for this record's assigned people could not be updated now" +
        (result && result.status ? " (" + result.status + ")" : "") + "; it is retried within 5 minutes.";
};

Spaarke.AssignedAccess._notify = function (formContext, text, level) {
    try {
        formContext.ui.clearFormNotification(Spaarke.AssignedAccess.Config.notificationId);
        if (text) {
            formContext.ui.setFormNotification(text, level, Spaarke.AssignedAccess.Config.notificationId);
        }
    } catch (error) {
        console.error("[Assigned Access] Could not show the notification:", error);
    }
};

/** OnLoad: registers the post-save handler and warms the BFF URL. Never throws. */
Spaarke.AssignedAccess.onLoad = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        formContext.data.entity.addOnPostSave(Spaarke.AssignedAccess.onPostSave);

        // Task 114: the Share command is hidden on a Restricted record (access_ribbon.js isShareAllowed reads this field),
        // so a change to the field refreshes the command bar at once rather than after the next save.
        var accessPermission = formContext.getAttribute && formContext.getAttribute("sprk_accesspermission");
        if (accessPermission) {
            accessPermission.addOnChange(function () {
                try { formContext.ui.refreshRibbon(); } catch (e) { /* never break the form */ }
            });
        }
        Spaarke.AssignedAccess.getApiBaseUrl().then(function (url) {
            Spaarke.AssignedAccess.Config.apiBaseUrl = url;
        }).catch(function (error) {
            console.error("[Assigned Access] Could not resolve the BFF URL; access falls to the 5-minute job.", error);
        });
    } catch (error) {
        console.error("[Assigned Access] Error in onLoad:", error);
    }
};

/** OnPostSave: fire-and-forget sync. Never throws, never blocks the save. */
Spaarke.AssignedAccess.onPostSave = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        var record = Spaarke.AssignedAccess.recordOf(formContext);
        if (!record) {
            return;
        }

        Spaarke.AssignedAccess.sync(record).then(function (result) {
            if (result.skipped) {
                return; // no helper / no token: nothing to say on a save; the job covers it
            }

            if (result.ok) {
                var summary = Spaarke.AssignedAccess.summarize(result.body);
                Spaarke.AssignedAccess._notify(formContext, summary, summary.indexOf("Not given") >= 0 ? "WARNING" : "INFO");
                return;
            }

            Spaarke.AssignedAccess._notify(formContext, Spaarke.AssignedAccess.failureText(result), "WARNING");
        });
    } catch (error) {
        console.error("[Assigned Access] Error in onPostSave:", error);
    }
};
