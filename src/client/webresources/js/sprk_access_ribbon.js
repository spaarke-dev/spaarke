/**
 * Access ribbon commands - project, matter and work assignment main forms (unified-access-control-r2 task 142,
 * GitHub #1065; owner round 3 R3 "Update Access" + round 3b UX: ONE shared "Access" group, owned by task 142, which
 * task 150 extends with Make Secure / Remove Secure in THIS script - never a second group or script).
 *
 * Web Resource Name: sprk_/scripts/access_ribbon.js   (namespace Spaarke.Access.Ribbon)
 *
 * Commands:
 * - "Access" flyout (FlyoutAnchor) - shown only when at least one of its menu items is available to the caller
 *   (Spaarke.Access.Ribbon.isAccessMenuVisible).
 * - "Update Access" - re-applies the Assigned-To rule (task 142) AND the record's No Access entries (task 143) to this
 *   record now, in ONE BFF call: POST /api/v1/external-access/assigned-access/sync. Shows both outcomes, then refreshes.
 *
 * Every command definition and enable rule lists, IN THIS ORDER, the libraries this script needs (ribbon commands do
 * not load form libraries):
 *   1. sprk_/scripts/bff_auth.js            - Spaarke.BffAuth (MSAL silent SSO; never a popup - ADR-028 INV-5)
 *   2. sprk_/scripts/assignedaccess_postsave.js - Spaarke.AssignedAccess (the ONE sync call + its summary)
 *   3. sprk_/scripts/access_ribbon.js       - this file
 * (a preceding JavaScriptFunction with FunctionName="isNaN" per library - the established ribbon idiom).
 *
 * Who sees the command: a cached async enable rule asks the SERVER - GET /api/v1/external-access/can-manage-access,
 * the delegation filter's own verdict (Write on the record). Anything other than 200 + canManageAccess === true is
 * "no". It never guesses from a table privilege, and it is a convenience only: the sync route is gated by the same
 * DelegationRuleFilter whatever the ribbon shows.
 *
 * ADR-006: a thin ribbon-command script (no UI component). No plugin (D-1). Never throws; a missing helper shows a
 * message and never breaks the form.
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.Access = Spaarke.Access || {};
Spaarke.Access.Ribbon = Spaarke.Access.Ribbon || {};

(function (ns) {
    ns.VERSION = "1.0.0";

    var LOG = "[Access.Ribbon v" + ns.VERSION + "]";
    var GATE_PATH = "/api/v1/external-access/can-manage-access";
    var CACHE_KEY_PREFIX = "sprk_access_canmanage_";

    function safeSessionGet(key) {
        try { return window.sessionStorage.getItem(key); } catch (e) { return null; }
    }

    function safeSessionSet(key, value) {
        try { window.sessionStorage.setItem(key, value); } catch (e) { /* private mode: no cache, still correct */ }
    }

    function helpersLoaded() {
        return typeof Spaarke.BffAuth !== "undefined" && !!Spaarke.BffAuth.getToken &&
            typeof Spaarke.AssignedAccess !== "undefined" && !!Spaarke.AssignedAccess.sync;
    }

    /** Asks the server whether the caller may manage access on the record; caches true/false (never an error). */
    function queryCanManage(record, cacheKey) {
        return Spaarke.AssignedAccess.getApiBaseUrl().then(function (baseUrl) {
            return Spaarke.BffAuth.getToken(baseUrl).then(function (token) {
                if (!token) {
                    return false; // no silent token: "no" (not cached - a later evaluation may have one)
                }

                var url = baseUrl + GATE_PATH + "?recordType=" + encodeURIComponent(record.recordType) +
                    "&recordId=" + encodeURIComponent(record.recordId);
                return fetch(url, { headers: { "Accept": "application/json", "Authorization": "Bearer " + token } })
                    .then(function (response) {
                        if (response.status !== 200) {
                            safeSessionSet(cacheKey, "false");
                            return false;
                        }

                        return response.json().then(function (body) {
                            // The answer must be about THIS record (the gate echoes the id it answered about).
                            var same = body && String(body.recordId || "").replace(/[{}]/g, "").toLowerCase() === record.recordId;
                            var can = !!(same && body.canManageAccess === true);
                            safeSessionSet(cacheKey, can ? "true" : "false");
                            return can;
                        });
                    });
            });
        }).catch(function (error) {
            console.warn(LOG, "can-manage-access could not be asked; the command stays hidden.", error);
            return false; // not cached: retried on the next evaluation
        });
    }

    /**
     * EnableRule for "Update Access": true only when the server says the caller holds Write on this record. Cache HIT
     * answers synchronously; a MISS returns the pending promise (the ribbon treats it as false until it resolves and
     * re-evaluates) - the `sprk_fieldmapping_push.js` hasSourceProfile pattern.
     * @param {object} primaryControl - the form context
     * @returns {boolean|Promise<boolean>}
     */
    ns.canUpdateAccess = function (primaryControl) {
        try {
            if (!helpersLoaded()) {
                return false;
            }

            var record = Spaarke.AssignedAccess.recordOf(primaryControl);
            if (!record) {
                return false; // unsaved record
            }

            var cacheKey = CACHE_KEY_PREFIX + record.recordType + "_" + record.recordId;
            var cached = safeSessionGet(cacheKey);
            if (cached === "true") return true;
            if (cached === "false") return false;
            return queryCanManage(record, cacheKey);
        } catch (error) {
            console.error(LOG, "canUpdateAccess failed:", error);
            return false;
        }
    };

    /**
     * EnableRule for the "Access" flyout itself: visible only when at least one menu item is available, so the caller
     * never opens an empty menu. Today that is Update Access; task 150 ORs in Make Secure / Remove Secure here.
     */
    ns.isAccessMenuVisible = function (primaryControl) {
        return ns.canUpdateAccess(primaryControl);
    };

    function alert(title, text) {
        try {
            Xrm.Navigation.openAlertDialog({ title: title, text: text });
        } catch (error) {
            console.error(LOG, title + ": " + text, error);
        }
    }

    function notify(message) {
        try {
            Xrm.App.addGlobalNotification({
                type: 2,
                level: 1,
                message: message,
                showCloseButton: true,
                priority: 1
            }).then(function (notificationId) {
                setTimeout(function () { Xrm.App.clearGlobalNotification(notificationId); }, 8000);
            });
        } catch (error) {
            console.error(LOG, "Could not show the notification:", error);
        }
    }

    /**
     * "Update Access": ONE BFF call that re-applies No Access and the Assigned-To rule to this record, shows both
     * outcomes (the precedent's global notification), and refreshes the form. A failure shows the server's
     * ProblemDetails message - never a generic error. No token: a sign-in message and NO call.
     * @param {object} primaryControl - the form context
     */
    ns.updateAccess = function (primaryControl) {
        try {
            if (!helpersLoaded()) {
                alert("Update Access",
                    "The sign-in helper for this command is not loaded on this form. Reload the page and try again; if " +
                    "it persists, ask an administrator to check the Access command's libraries.");
                return;
            }

            var record = Spaarke.AssignedAccess.recordOf(primaryControl);
            if (!record) {
                alert("Update Access", "Save the record first, then update its access.");
                return;
            }

            Spaarke.AssignedAccess.sync(record).then(function (result) {
                if (result.skipped && result.reason === "no-token") {
                    alert("Update Access", "Sign-in needed - reload the page and retry. Nothing was changed.");
                    return;
                }

                if (result.skipped) {
                    alert("Update Access", "This command could not run here. Reload the page and try again.");
                    return;
                }

                if (!result.ok) {
                    alert("Update Access", Spaarke.AssignedAccess.failureText(result));
                    return;
                }

                var summary = Spaarke.AssignedAccess.summarize(result.body);
                notify(summary || "Access is up to date: everyone assigned to this record has the access the rules give them.");
                try {
                    primaryControl.data.refresh(false);
                } catch (refreshError) {
                    console.warn(LOG, "The form could not be refreshed:", refreshError);
                }
            });
        } catch (error) {
            console.error(LOG, "updateAccess failed:", error);
            alert("Update Access", "Access could not be updated now. Try again in a moment.");
        }
    };
})(Spaarke.Access.Ribbon);
