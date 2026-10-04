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
 * - "Make Secure" (task 150, UX amendment; owner round 27 copy) - on a record that is NOT secure, for a caller with
 *   Write: confirms with the owner-authored copy (MAKE_SECURE_CONFIRMATION, the ONE constant), then calls the
 *   provisioning endpoint POST /api/v1/external-access/provision-project with transition "make-secure" (round 33 item 1:
 *   the server holds that path to the Write gate and shares the record to its creator too) (task 144, generalized to the
 *   three roots; task 148's transition carries the existing children, round 26 item 3 its files). Refreshes, shows the
 *   outcome - and, when the server names someone it did not share the record with (`skippedPrincipals`: the creator on
 *   the No Access list, unverifiable, or a failed share), a per-person warning (SKIPPED_PRINCIPAL_COPY; never silent).
 *   Shipped only where task 148's transition is deployed - an import/packaging rule, not a runtime check
 *   (infrastructure/dataverse/ribbon/AccessRibbons/README.md).
 * - "Remove Secure" (task 150) - on a record that IS secure, for a caller with Write: confirms with
 *   REMOVE_SECURE_CONFIRMATION (round 33 item 2, the ONE constant), then calls the unsecure endpoint
 *   POST /api/v1/external-access/unsecure-project. The SERVER decides who may remove the designation (owner F3: Full
 *   Access holders and the record's creator); a refusal shows the endpoint's ProblemDetails message. This script
 *   decides nothing about who may unsecure - its enable rule only hides the command from callers without Write.
 *
 * Secure state: sprk_issecure is on no form, so a ValueRule cannot read it. The rules read it with
 * Xrm.WebApi.retrieveRecord (every user reads the true value under task 150's reader profile). Anything other than a
 * stored true or false - a failed read, a masked (empty) value - hides BOTH commands (fail closed, ADR-003).
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
    // 1.1.0 - task 150: Make Secure / Remove Secure. 1.2.0 - round 33: the make-secure transition, the Remove Secure
    // confirmation, per-person warnings for skippedPrincipals.
    ns.VERSION = "1.2.0";

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
     * never opens an empty menu. Make Secure and Remove Secure (task 150) are each available only to a caller for whom
     * Update Access is - both require the same Write verdict - so "any item available" is exactly Update Access's rule,
     * and no secure-state read is spent on the flyout itself.
     */
    ns.isAccessMenuVisible = function (primaryControl) {
        return ns.canUpdateAccess(primaryControl);
    };

    // =========================================================================================================
    // Task 150 - Make Secure / Remove Secure
    // =========================================================================================================

    var PROVISION_PATH = "/api/v1/external-access/provision-project";
    var MAKE_SECURE_TRANSITION = "make-secure"; // ProvisionProjectEndpoint.TransitionMakeSecure
    var UNSECURE_PATH = "/api/v1/external-access/unsecure-project";
    var SECURE_STATE_TTL_MS = 30000;

    /** The {record} word of the confirmation copy, by table (owner round 27). */
    ns.RECORD_WORDS = {
        sprk_project: "project",
        sprk_matter: "matter",
        sprk_workassignment: "work assignment"
    };

    /**
     * The Make Secure confirmation - owner-authored copy, ACCEPTED verbatim in owner round 27 (2026-10-04; "adjust in UAT
     * if necessary"). The ONE constant: change it only with the owner. {record} is project, matter or work assignment.
     */
    ns.MAKE_SECURE_CONFIRMATION = Object.freeze({
        title: "Make this {record} secure?",
        paragraphs: Object.freeze([
            "Only the person who created this {record} and the people it is shared with will keep access. Everyone else " +
                "in your organization loses access, and external contacts keep only access granted to them directly.",
            "Its existing documents, events, to-dos and other related records become secure too, for the same people, " +
                "and its files move to the {record}'s own secure storage. This can take a few minutes.",
            "To remove the secure designation later, ask someone with Full Access to the {record}, or the person who " +
                "created it."
        ]),
        confirmButtonLabel: "Make Secure",
        cancelButtonLabel: "Cancel"
    });

    /**
     * The Remove Secure confirmation - round 33 item 2 (2026-10-04; owner round 27's stance: the recommended wording,
     * adjustable in UAT). The ONE constant: change it only with the owner. {record} is project, matter or work assignment.
     */
    ns.REMOVE_SECURE_CONFIRMATION = Object.freeze({
        title: "Remove the secure designation from this {record}?",
        paragraphs: Object.freeze([
            "The {record} and its related records return to normal access: people who can see records in its business " +
                "unit will be able to see them, and the individual sharing set up while it was secure is removed.",
            "To secure it again later, use Make Secure."
        ]),
        confirmButtonLabel: "Remove Secure",
        cancelButtonLabel: "Cancel"
    });

    /** One confirmation constant for one table, {record} filled in - the strings openConfirmDialog shows. */
    function confirmationFor(copy, entityName) {
        var word = ns.RECORD_WORDS[entityName];
        if (!word) {
            return null;
        }

        var fill = function (text) { return text.split("{record}").join(word); };
        return {
            title: fill(copy.title),
            text: copy.paragraphs.map(fill).join("\n\n"),
            confirmButtonLabel: copy.confirmButtonLabel,
            cancelButtonLabel: copy.cancelButtonLabel
        };
    }

    /**
     * The Make Secure confirmation for one table. Null for a table that is not one of the three roots (no dialog, no
     * call).
     */
    ns.makeSecureConfirmationFor = function (entityName) {
        return confirmationFor(ns.MAKE_SECURE_CONFIRMATION, entityName);
    };

    /** The Remove Secure confirmation for one table (null for any other table). */
    ns.removeSecureConfirmationFor = function (entityName) {
        return confirmationFor(ns.REMOVE_SECURE_CONFIRMATION, entityName);
    };

    /** Shows one confirmation; resolves true only when the user chose its confirm (primary) button. */
    function confirmFirst(confirmation) {
        return Promise.resolve(Xrm.Navigation.openConfirmDialog(
            {
                title: confirmation.title,
                text: confirmation.text,
                confirmButtonLabel: confirmation.confirmButtonLabel,
                cancelButtonLabel: confirmation.cancelButtonLabel
            },
            { height: 360, width: 560 }
        )).then(function (answer) {
            return !!answer && answer.confirmed === true;
        });
    }

    var secureStates = {};

    function entityNameOf(primaryControl) {
        try {
            return primaryControl.data.entity.getEntityName();
        } catch (error) {
            return null;
        }
    }

    function stateKey(record) {
        return record.recordType + "_" + record.recordId;
    }

    /**
     * The record's stored secure flag: true, false - or null when it is not KNOWN (the read failed, or the value came
     * back empty: a masked field-secured column). Null hides both commands. Cached per record for a short while, so the
     * flyout's rules cost one read; the commands forget it before they refresh the form.
     */
    function readSecureState(entityName, record) {
        var key = stateKey(record);
        var cached = secureStates[key];
        if (cached && Date.now() - cached.at < SECURE_STATE_TTL_MS) {
            return cached.promise;
        }

        var promise;
        try {
            promise = Promise.resolve(Xrm.WebApi.retrieveRecord(entityName, record.recordId, "?$select=sprk_issecure"))
                .then(function (row) {
                    var value = row ? row.sprk_issecure : undefined;
                    if (value === true || value === false) {
                        return value;
                    }

                    console.warn(LOG, "sprk_issecure came back empty; Make Secure and Remove Secure stay hidden.");
                    return null;
                }, function (error) {
                    console.warn(LOG, "sprk_issecure could not be read; Make Secure and Remove Secure stay hidden.", error);
                    return null;
                });
        } catch (error) {
            console.warn(LOG, "sprk_issecure could not be read; Make Secure and Remove Secure stay hidden.", error);
            promise = Promise.resolve(null);
        }

        secureStates[key] = { at: Date.now(), promise: promise };
        return promise;
    }

    function forgetSecureState(record) {
        delete secureStates[stateKey(record)];
    }

    /** Test seam: clears the secure-state cache. */
    ns._resetSecureStateCache = function () {
        secureStates = {};
    };

    /**
     * Shared body of the two enable rules: the caller may manage access (the cached can-manage-access verdict - Write on
     * the record, the same rule as Update Access) AND the stored flag equals `wantSecure`. An unknown flag is never equal,
     * so a failed or masked read hides both commands.
     */
    function secureCommandEnabled(primaryControl, wantSecure) {
        try {
            if (!helpersLoaded()) {
                return false;
            }

            var record = Spaarke.AssignedAccess.recordOf(primaryControl);
            var entityName = entityNameOf(primaryControl);
            if (!record || !ns.RECORD_WORDS[entityName]) {
                return false;
            }

            return Promise.all([Promise.resolve(ns.canUpdateAccess(primaryControl)), readSecureState(entityName, record)])
                .then(function (answers) {
                    return answers[0] === true && answers[1] === wantSecure;
                })
                .catch(function (error) {
                    console.warn(LOG, "A secure-command rule failed; the command stays hidden.", error);
                    return false;
                });
        } catch (error) {
            console.error(LOG, "A secure-command rule failed:", error);
            return false;
        }
    }

    /** EnableRule for "Make Secure": the record is NOT secure and the caller has Write. */
    ns.canMakeSecure = function (primaryControl) {
        return secureCommandEnabled(primaryControl, false);
    };

    /** EnableRule for "Remove Secure": the record IS secure and the caller has Write (the server enforces F3). */
    ns.canRemoveSecure = function (primaryControl) {
        return secureCommandEnabled(primaryControl, true);
    };

    /**
     * POSTs { recordType, recordId } (plus `extra`, e.g. the make-secure transition) to a secure-designation endpoint
     * through Spaarke.BffAuth.authenticatedFetch (no token: no call). Resolves - never rejects - with
     * { ok, status, body } or { skipped, reason }.
     */
    function postDesignation(path, record, extra) {
        var body = { recordType: record.recordType, recordId: record.recordId };
        Object.keys(extra || {}).forEach(function (key) { body[key] = extra[key]; });
        return Spaarke.AssignedAccess.getApiBaseUrl().then(function (baseUrl) {
            return Spaarke.BffAuth.authenticatedFetch(baseUrl + path, {
                method: "POST",
                headers: { "Content-Type": "application/json", "Accept": "application/json" },
                body: JSON.stringify(body)
            }, baseUrl);
        }).then(function (response) {
            if (!response) {
                return { ok: false, skipped: true, reason: "no-token" };
            }

            return response.json().then(function (body) { return body; }, function () { return null; })
                .then(function (body) {
                    return { ok: response.ok, status: response.status, body: body, skipped: false };
                });
        }).catch(function (error) {
            console.error(LOG, "The secure-designation call failed:", error);
            return { ok: false, skipped: false, status: 0, body: null, reason: "network" };
        });
    }

    /** The endpoint's own ProblemDetails message when it sent one; otherwise a status-only line. */
    ns.refusalText = function (commandName, result) {
        if (result && result.body && typeof result.body.detail === "string" && result.body.detail) {
            return result.body.detail;
        }

        return commandName + " did not complete" + (result && result.status ? " (" + result.status + ")" : "") +
            ". Reload the record to see its current state.";
    };

    /** Forgets the cached state, refreshes the form and then its command bar, so every rule reads the new state. */
    function refreshAfter(primaryControl, record) {
        forgetSecureState(record);
        try {
            Promise.resolve(primaryControl.data.refresh(false)).then(function () {
                try { primaryControl.ui.refreshRibbon(); } catch (ribbonError) { /* the next evaluation re-reads */ }
            }, function (refreshError) {
                console.warn(LOG, "The form could not be refreshed:", refreshError);
            });
        } catch (error) {
            console.warn(LOG, "The form could not be refreshed:", error);
        }
    }

    /**
     * The per-person warnings after a call that succeeded but did NOT share the record with someone the server named in
     * `skippedPrincipals` - on Make Secure, the record's creator (round 33 item 1): on its No Access list, that list could
     * not be checked, or the share itself failed. Never silent (round 33 item 5). The ONE constant: round 29's sentences and
     * round 33 item 5's generic one, the same words the Create Project wizard shows, with {record} filled per table the way
     * owner round 27's copy is (adjustable in UAT). {name} is the person's full name, or their id when it cannot be read.
     */
    ns.SKIPPED_PRINCIPAL_COPY = Object.freeze({
        "sdap.provision.principal_no_access":
            "{name} is on this {record}'s No Access list, so the {record} was not shared with them.",
        "sdap.provision.principal_no_access_unverifiable":
            "Whether {name} may access this {record} could not be checked, so the {record} was not shared with them. You " +
            "can share it with them later from Manage Access.",
        "sdap.provision.principal_share_failed":
            "{name} was not given access to this {record}. You can share it with them later from Manage Access."
    });

    /** Round 33 item 5: the warning for a reason code this script does not know (the code is logged as well). */
    ns.SKIPPED_PRINCIPAL_GENERIC = "{name} was not given access to this {record}.";

    /**
     * One person's warning, {name} and {record} filled. An unknown reason code gets the generic warning and is logged -
     * never dropped. The server's own `message` is never shown (the wizard's rule).
     */
    ns.describeSkippedPrincipal = function (reasonCode, name, entityName) {
        var known = Object.prototype.hasOwnProperty.call(ns.SKIPPED_PRINCIPAL_COPY, reasonCode);
        if (!known) {
            console.error(LOG, "A person was not given access, for a reason this script does not know:",
                { reasonCode: reasonCode });
        }

        var word = ns.RECORD_WORDS[entityName] || "record";
        var text = known ? ns.SKIPPED_PRINCIPAL_COPY[reasonCode] : ns.SKIPPED_PRINCIPAL_GENERIC;
        return text.split("{name}").join(name).split("{record}").join(word);
    };

    /** A user's full name for a warning; their id when it cannot be read (the warning is shown either way). */
    function personName(systemUserId) {
        try {
            return Promise.resolve(Xrm.WebApi.retrieveRecord("systemuser", systemUserId, "?$select=fullname"))
                .then(function (row) {
                    return row && typeof row.fullname === "string" && row.fullname ? row.fullname : systemUserId;
                }, function () {
                    return systemUserId;
                });
        } catch (error) {
            return Promise.resolve(systemUserId);
        }
    }

    /** Shows the per-person warnings for a successful call's `skippedPrincipals`, if any (an alert, after the notification). */
    function warnSkipped(commandName, entityName, body) {
        var skipped = body && Array.isArray(body.skippedPrincipals) ? body.skippedPrincipals : [];
        if (skipped.length === 0) {
            return Promise.resolve();
        }

        return Promise.all(skipped.map(function (person) {
            var id = person && person.systemUserId ? String(person.systemUserId) : "";
            return personName(id).then(function (name) {
                return ns.describeSkippedPrincipal(person ? person.reasonCode : undefined, name, entityName);
            });
        })).then(function (lines) {
            alert(commandName, lines.join("\n\n"));
        });
    }

    /** Runs one designation call and shows its outcome as Update Access does (a notification, or an alert). */
    function runDesignation(primaryControl, start, commandName, path, successText, extra) {
        var record = start.record;
        return postDesignation(path, record, extra).then(function (result) {
            if (result.skipped && result.reason === "no-token") {
                alert(commandName, "Sign-in needed - reload the page and retry. Nothing was changed.");
                return result;
            }

            // Whatever the outcome, the record may have changed (a partial pass answers 500): re-read it.
            refreshAfter(primaryControl, record);

            if (!result.ok) {
                alert(commandName, ns.refusalText(commandName, result));
                return result;
            }

            notify(successText);
            return warnSkipped(commandName, start.entityName, result.body).then(function () {
                return result;
            });
        });
    }

    function startCommand(primaryControl, commandName) {
        if (!helpersLoaded() || !Spaarke.BffAuth.authenticatedFetch) {
            alert(commandName,
                "The sign-in helper for this command is not loaded on this form. Reload the page and try again; if " +
                "it persists, ask an administrator to check the Access command's libraries.");
            return null;
        }

        var record = Spaarke.AssignedAccess.recordOf(primaryControl);
        var entityName = entityNameOf(primaryControl);
        if (!record || !ns.RECORD_WORDS[entityName]) {
            alert(commandName, "Save the record first.");
            return null;
        }

        return { record: record, entityName: entityName };
    }

    /**
     * "Make Secure": confirms with the owner-authored copy, then calls the provisioning endpoint. Cancel calls nothing.
     * @param {object} primaryControl - the form context
     * @returns {Promise} settles when the command has finished (a test seam; the ribbon ignores it)
     */
    ns.makeSecure = function (primaryControl) {
        try {
            var start = startCommand(primaryControl, "Make Secure");
            if (!start) {
                return Promise.resolve();
            }

            return confirmFirst(ns.makeSecureConfirmationFor(start.entityName)).then(function (confirmed) {
                if (!confirmed) {
                    return null;
                }

                // Round 33 item 1: the transition tells the server this is Make Secure (the Write gate), not the
                // wizards' create-then-secure path (the creator rule).
                return runDesignation(primaryControl, start, "Make Secure", PROVISION_PATH,
                    "This " + ns.RECORD_WORDS[start.entityName] + " is now secure.", { transition: MAKE_SECURE_TRANSITION });
            });
        } catch (error) {
            console.error(LOG, "makeSecure failed:", error);
            alert("Make Secure", "This command could not run now. Reload the page and try again.");
            return Promise.resolve();
        }
    };

    /**
     * "Remove Secure": confirms with REMOVE_SECURE_CONFIRMATION (round 33 item 2), then calls the unsecure endpoint. Cancel
     * calls nothing. Who may remove the designation is the server's decision (F3); its refusal message is shown as sent.
     * @param {object} primaryControl - the form context
     * @returns {Promise} settles when the command has finished (a test seam; the ribbon ignores it)
     */
    ns.removeSecure = function (primaryControl) {
        try {
            var start = startCommand(primaryControl, "Remove Secure");
            if (!start) {
                return Promise.resolve();
            }

            return confirmFirst(ns.removeSecureConfirmationFor(start.entityName)).then(function (confirmed) {
                if (!confirmed) {
                    return null;
                }

                return runDesignation(primaryControl, start, "Remove Secure", UNSECURE_PATH,
                    "This " + ns.RECORD_WORDS[start.entityName] + " is no longer secure.");
            });
        } catch (error) {
            console.error(LOG, "removeSecure failed:", error);
            alert("Remove Secure", "This command could not run now. Reload the page and try again.");
            return Promise.resolve();
        }
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
