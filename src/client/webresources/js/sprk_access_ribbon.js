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
 * - "Make Secure" (task 150, UX amendment; owner round 27 copy) - on a record that is NOT secure, or whose secure
 *   transition did not finish (round 40 item 1: flagged but not provisioned - see "Secure state" below), for a caller with
 *   Write: confirms with the owner-authored copy (MAKE_SECURE_CONFIRMATION, the ONE constant), then calls the
 *   provisioning endpoint POST /api/v1/external-access/provision-project with transition "make-secure" (round 33 item 1:
 *   the server holds that path to the Write gate and shares the record to its creator too; round 40 item 2: the caller
 *   keeps access on the forward path and when the call finishes an earlier run) (task 144, generalized to the three
 *   roots; task 148's transition carries the existing children, round 26 item 3 its files). Refreshes, shows the
 *   outcome - and, when the server names someone it did not share the record with (`skippedPrincipals`: the creator on
 *   the No Access list, unverifiable, or a failed share), a per-person warning (SKIPPED_PRINCIPAL_COPY; never silent).
 *   A failure after the secure flag was written that the server answers as "the same caller may call again"
 *   (MAKE_SECURE_RETRY_IN_PLACE) offers that call in place: a confirm dialog with the server's message and Make Secure /
 *   Cancel (round 40 item 1). Any other refusal is an alert with the server's message - except a code this script
 *   carries its own words for (REFUSAL_COPY, round 53 item 1: caller_rights_unverifiable).
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
 * stored true or false - a failed read, a masked (empty) value - hides BOTH commands (fail closed, ADR-003). The same
 * read takes sprk_containerid and _owninguser_value (round 40 item 1): a PROVISIONED secure record is owned by the
 * Secure Record Owners TEAM and records its own container, so a flagged record with no container, or one a USER owns, is
 * one whose secure transition did not finish, and Make Secure is offered on it as well as Remove Secure. A flagged
 * record with a container that a TEAM owns asks the server who that team is (round 46 item 4: can-manage-access with
 * includeOwner=true; which team and business unit are the Secure Record ones is the server's configuration): the Secure
 * Record Owners team - finished; another team INSIDE the Secure Record business unit (the retired default team before
 * task 144's migration) - secure and isolated already, nothing to finish, so Make Secure stays hidden (round 53 item 2;
 * moving it to the named team is task 144's migration, and the server refuses it 409 owned_by_other_secure_team); a
 * team in ANOTHER business unit - a secure record reassigned outside Spaarke - unfinished, Make Secure offered. An
 * answer that cannot be had keeps Make Secure hidden on that record (Remove Secure still follows the flag).
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
    // confirmation, per-person warnings for skippedPrincipals. 1.3.0 - round 40: Make Secure offered on an unfinished
    // secure transition, the in-place retry, "Someone" for a name that cannot be resolved. 1.4.0 - round 46 item 4: a
    // flagged record a team OTHER than the Secure Record Owners team owns is unfinished too (the server says which team).
    // 1.5.0 - round 53: another team INSIDE the Secure Record business unit is already isolated (Make Secure hidden); the
    // caller_rights_unverifiable refusal in this script's own words. 1.6.0 - task 114 (owner round 67 amendment 4(a)):
    // isShareAllowed, the rule that hides the platform's Share command on a Restricted record.
    ns.VERSION = "1.6.0";

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

    /** The can-manage-access URL for one record; `includeOwner` also asks who owns it (round 46 item 4). */
    function gateUrl(baseUrl, record, includeOwner) {
        return baseUrl + GATE_PATH + "?recordType=" + encodeURIComponent(record.recordType) +
            "&recordId=" + encodeURIComponent(record.recordId) + (includeOwner ? "&includeOwner=true" : "");
    }

    /** Whether a gate answer is a 200-body about THIS record (the gate echoes the id it answered about). */
    function answersFor(body, record) {
        return !!body && String(body.recordId || "").replace(/[{}]/g, "").toLowerCase() === record.recordId;
    }

    /** Asks the server whether the caller may manage access on the record; caches true/false (never an error). */
    function queryCanManage(record, cacheKey) {
        return Spaarke.AssignedAccess.getApiBaseUrl().then(function (baseUrl) {
            return Spaarke.BffAuth.getToken(baseUrl).then(function (token) {
                if (!token) {
                    return false; // no silent token: "no" (not cached - a later evaluation may have one)
                }

                var url = gateUrl(baseUrl, record, false);
                return fetch(url, { headers: { "Accept": "application/json", "Authorization": "Bearer " + token } })
                    .then(function (response) {
                        if (response.status !== 200) {
                            safeSessionSet(cacheKey, "false");
                            return false;
                        }

                        return response.json().then(function (body) {
                            // The answer must be about THIS record (the gate echoes the id it answered about).
                            var can = answersFor(body, record) && body.canManageAccess === true;
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
    // Task 114 - the platform's Share command on a Restricted record (owner round 67 amendment 4(a))
    // =========================================================================================================

    /** sprk_accesspermission = Restricted (internal use only). The same integer the BFF reads (Restricted 100000002). */
    ns.ACCESS_PERMISSION_RESTRICTED = 100000002;

    /**
     * EnableRule ADDED to the platform's own form Share command on the project, matter and work assignment main forms
     * (Merge-AccessRibbon.ps1 copies that command from the live ribbon and appends this rule): Share is hidden on a
     * Restricted record, so sharing goes through Manage Access "+ User", which refuses a user flagged external there. A
     * convenience only - the BFF removes such a share wherever it came from (task 114's Restricted remover, on the save
     * and every 5 minutes).
     *
     * Reads the form's own sprk_accesspermission when the form carries it (so an unsaved change to Restricted hides Share
     * once the ribbon refreshes - assignedaccess_postsave.js refreshes it on change); otherwise the saved value with ONE
     * Xrm.WebApi.retrieveRecord. A read that fails hides Share (fail closed): "+ User" is still there. An unsaved record
     * answers true - the platform's own rules keep Share off a new form.
     * @param {object} primaryControl - the form context
     * @returns {boolean|Promise<boolean>}
     */
    ns.isShareAllowed = function (primaryControl) {
        try {
            var attribute = primaryControl && typeof primaryControl.getAttribute === "function"
                ? primaryControl.getAttribute("sprk_accesspermission")
                : null;
            if (attribute) {
                return attribute.getValue() !== ns.ACCESS_PERMISSION_RESTRICTED;
            }

            var entity = primaryControl && primaryControl.data && primaryControl.data.entity;
            var recordId = entity ? (entity.getId() || "").replace(/[{}]/g, "").toLowerCase() : "";
            if (!recordId) {
                return true;
            }

            return Promise.resolve(Xrm.WebApi.retrieveRecord(entity.getEntityName(), recordId, "?$select=sprk_accesspermission"))
                .then(function (row) {
                    return !!row && row.sprk_accesspermission !== ns.ACCESS_PERMISSION_RESTRICTED;
                }, function (error) {
                    console.warn(LOG, "sprk_accesspermission could not be read; Share stays hidden (use Manage Access).", error);
                    return false;
                });
        } catch (error) {
            console.error(LOG, "isShareAllowed failed; Share stays hidden.", error);
            return false;
        }
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

    /** The ONE read the secure-state rules make (round 40 item 1: the flag, and what tells a finished transition apart). */
    var SECURE_STATE_SELECT = "?$select=sprk_issecure,sprk_containerid,_owninguser_value";

    var UNKNOWN_STATE = Object.freeze({ secure: null, unfinished: false });

    /**
     * Where a flagged, team-owned record's owner sits - the server's answer to can-manage-access with includeOwner=true
     * (round 46 item 4; round 53 item 2). The ONE place the answer's three owner facts are read.
     */
    ns.OWNER_PLACEMENT = Object.freeze({
        SECURE_OWNER_TEAM: "secure-owner-team",       // finished (with its container): provisioned
        OTHER_TEAM_INSIDE: "other-team-inside",       // another team in the Secure Record business unit: already isolated
        OTHER_TEAM_OUTSIDE: "other-team-outside"      // a team in another business unit: the secure transition did not finish
    });

    /**
     * The owner placement in one gate answer, or null when the answer does not say it definitely: an answer about another
     * record, one that is not a delegation "yes", an ownedBySecureOwnerTeam that is not exactly true or false, or - for
     * another team - an owningTeamInSecureBusinessUnit that is not exactly true or false. Null is never either answer.
     */
    function ownerPlacementOf(body, record) {
        if (!answersFor(body, record) || body.canManageAccess !== true) {
            return null;
        }

        var owned = body.ownedBySecureOwnerTeam;
        if (owned === true) {
            return ns.OWNER_PLACEMENT.SECURE_OWNER_TEAM;
        }

        if (owned !== false) {
            return null;
        }

        var inside = body.owningTeamInSecureBusinessUnit;
        if (inside === true) {
            return ns.OWNER_PLACEMENT.OTHER_TEAM_INSIDE;
        }

        return inside === false ? ns.OWNER_PLACEMENT.OTHER_TEAM_OUTSIDE : null;
    }

    /**
     * Asks the server where the record's owner sits (ownerPlacementOf). Resolves a placement, or null when it cannot be
     * had (no token, a non-200, a failure, or an answer that does not say it); never rejects, never cached beyond the
     * secure state it feeds.
     */
    function queryOwnerPlacement(record) {
        return Spaarke.AssignedAccess.getApiBaseUrl().then(function (baseUrl) {
            return Spaarke.BffAuth.getToken(baseUrl).then(function (token) {
                if (!token) {
                    return null;
                }

                return fetch(gateUrl(baseUrl, record, true),
                    { headers: { "Accept": "application/json", "Authorization": "Bearer " + token } })
                    .then(function (response) {
                        if (response.status !== 200) {
                            return null;
                        }

                        return response.json().then(function (body) {
                            return ownerPlacementOf(body, record);
                        });
                    });
            });
        }).catch(function (error) {
            console.warn(LOG, "Who owns this record could not be asked.", error);
            return null;
        });
    }

    /**
     * The record's secure state, from ONE read: `secure` is the stored flag - true, false, or null when it is not KNOWN
     * (the read failed, or the value came back empty: a masked field-secured column; null hides both commands).
     * `unfinished` is true for a record flagged secure whose transition did not finish (round 40 item 1; round 46 item
     * 4): provisioning leaves a finished secure record owned by the Secure Record Owners TEAM with its own container
     * recorded, so one with no container recorded, one a USER owns, or one a team in ANOTHER business unit owns is not
     * finished - every failure after the flag write leaves one of the first two shapes (the flag is never cleared), a
     * reassignment outside Spaarke the third, and the server finishes each when Make Secure is called again. Another team
     * INSIDE the Secure Record business unit (round 53 item 2: the retired default team before task 144's migration) is
     * secure and isolated already - not unfinished; the server refuses it 409 and task 144's migration moves it. The
     * owner question goes to the server only when the read leaves it open (flagged, a container recorded, no owning
     * user); an answer that cannot be had is not "unfinished" - Make Secure stays hidden on that record. Cached per record
     * for a short while, so the flyout's rules cost one read; the commands forget it before they refresh the form.
     */
    function readSecureState(entityName, record) {
        var key = stateKey(record);
        var cached = secureStates[key];
        if (cached && Date.now() - cached.at < SECURE_STATE_TTL_MS) {
            return cached.promise;
        }

        var promise;
        try {
            promise = Promise.resolve(Xrm.WebApi.retrieveRecord(entityName, record.recordId, SECURE_STATE_SELECT))
                .then(function (row) {
                    var value = row ? row.sprk_issecure : undefined;
                    if (value !== true && value !== false) {
                        console.warn(LOG, "sprk_issecure came back empty; Make Secure and Remove Secure stay hidden.");
                        return UNKNOWN_STATE;
                    }

                    var container = row.sprk_containerid;
                    var hasContainer = typeof container === "string" && container.trim().length > 0;
                    var userOwned = typeof row._owninguser_value === "string" && row._owninguser_value.length > 0;
                    if (value !== true || !hasContainer || userOwned) {
                        return { secure: value, unfinished: value === true };
                    }

                    // Flagged, its container recorded, owned by a team: unfinished only when that team owns it OUTSIDE the
                    // Secure Record business unit (round 46 item 4; round 53 item 2).
                    return queryOwnerPlacement(record).then(function (placement) {
                        if (placement === null) {
                            console.warn(LOG, "Who owns this record could not be told; Make Secure stays hidden on it.");
                        } else if (placement === ns.OWNER_PLACEMENT.OTHER_TEAM_INSIDE) {
                            console.info(LOG, "Another team inside the Secure Record business unit owns this record: it " +
                                "is isolated already, so Make Secure stays hidden (an administrator's migration moves it).");
                        }

                        return { secure: true, unfinished: placement === ns.OWNER_PLACEMENT.OTHER_TEAM_OUTSIDE };
                    });
                }, function (error) {
                    console.warn(LOG, "sprk_issecure could not be read; Make Secure and Remove Secure stay hidden.", error);
                    return UNKNOWN_STATE;
                });
        } catch (error) {
            console.warn(LOG, "sprk_issecure could not be read; Make Secure and Remove Secure stay hidden.", error);
            promise = Promise.resolve(UNKNOWN_STATE);
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
     * the record, the same rule as Update Access) AND `wanted(state)` holds for the record's secure state. An unknown flag
     * satisfies neither rule, so a failed or masked read hides both commands.
     */
    function secureCommandEnabled(primaryControl, wanted) {
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
                    return answers[0] === true && wanted(answers[1]) === true;
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

    /**
     * EnableRule for "Make Secure": the caller has Write, and the record is NOT secure - or it is flagged secure but its
     * transition did not finish (round 40 item 1; acceptance (e) amended: hidden on a PROVISIONED secure record). Calling
     * it again then finishes the transition: the server resumes or re-runs it.
     */
    ns.canMakeSecure = function (primaryControl) {
        return secureCommandEnabled(primaryControl, function (state) {
            return state.secure === false || (state.secure === true && state.unfinished === true);
        });
    };

    /** EnableRule for "Remove Secure": the record IS secure and the caller has Write (the server enforces F3). */
    ns.canRemoveSecure = function (primaryControl) {
        return secureCommandEnabled(primaryControl, function (state) {
            return state.secure === true;
        });
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

    /**
     * Round 53 item 1: the refusals this script shows in its OWN words, by reason code - the copy round 53 ratified, the
     * same words the server sends, {record} filled per table. The ONE constant; every other refusal shows the server's
     * message (refusalText).
     * - sdap.provision.caller_rights_unverifiable (500, Make Secure only): which access the caller holds could not be read,
     *   so the share they keep could not be floored on it (round 46 item 1). Refused before any write: the record is
     *   unchanged and Make Secure stays offered on it - that is the "try again" (a pre-write refusal, so not one of
     *   MAKE_SECURE_RETRY_IN_PLACE, which are the failures AFTER the flag write).
     */
    ns.REFUSAL_COPY = Object.freeze({
        "sdap.provision.caller_rights_unverifiable":
            "Which access you hold on this {record} could not be read, so securing it could not make sure you keep that " +
            "access. Nothing was changed; you may try again."
    });

    /** A refusal's text: this script's own words for a code in REFUSAL_COPY ({record} filled), else refusalText. */
    ns.refusalFor = function (commandName, result, entityName) {
        var code = reasonCodeOf(result);
        if (code !== null && Object.prototype.hasOwnProperty.call(ns.REFUSAL_COPY, code)) {
            return ns.REFUSAL_COPY[code].split("{record}").join(ns.RECORD_WORDS[entityName] || "record");
        }

        return ns.refusalText(commandName, result);
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
     * round 33 item 5's generic one, the same words the Create Project wizard shows, generalized to {record} per table the
     * way owner round 27's copy is (round 40 item 3; adjustable in UAT). {name} is the person's full name, or
     * UNNAMED_PERSON ("Someone") when it cannot be resolved (round 40 item 3).
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
        var who = typeof name === "string" && name.trim() ? name : ns.UNNAMED_PERSON;
        return text.split("{name}").join(who).split("{record}").join(word);
    };

    /**
     * Round 40 item 3: the {name} of a warning whose person cannot be named - an empty id, a name that cannot be read,
     * an empty name. Never an empty name, and the warning is shown either way (never silent).
     */
    ns.UNNAMED_PERSON = "Someone";

    /** A user's full name for a warning; UNNAMED_PERSON when there is no id or the name cannot be resolved. */
    function personName(systemUserId) {
        if (typeof systemUserId !== "string" || systemUserId.trim().length === 0) {
            return Promise.resolve(ns.UNNAMED_PERSON); // no id: nothing to read
        }

        try {
            return Promise.resolve(Xrm.WebApi.retrieveRecord("systemuser", systemUserId, "?$select=fullname"))
                .then(function (row) {
                    return row && typeof row.fullname === "string" && row.fullname.trim()
                        ? row.fullname
                        : ns.UNNAMED_PERSON;
                }, function () {
                    return ns.UNNAMED_PERSON;
                });
        } catch (error) {
            return Promise.resolve(ns.UNNAMED_PERSON);
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

    /**
     * Round 40 item 1: the Make Secure failures AFTER its first write (the secure flag) that the server answers as "the
     * same caller may call again" - one per step of the transition. Each is offered again IN PLACE: the alert becomes a
     * confirm dialog with the server's message and Make Secure / Cancel, and Make Secure repeats the same call (the
     * repeat call round 26 promises). After Step 7 the record reads as provisioned, so the enable rule no longer offers the
     * command and this dialog is the ribbon's retry (the scheduled backstop is task 147's SecureChildReconciliationJob,
     * every 2 minutes: the children, and - round 46 item 2, wired at integration - the pending Make Secure file
     * relocations through the ONE DocumentContainerRelocator); before it, the enable rule offers it again too. Refusals that need an
     * administrator first (creator_share_failed_resumable, cascade_children_not_restored, owner_assignment_failed,
     * owner_assignment_not_applied) are NOT here: repeating them repeats the refusal, so they stay an alert naming the
     * administrator's step.
     */
    ns.MAKE_SECURE_RETRY_IN_PLACE = Object.freeze([
        "sdap.provision.secure_flag_not_set",          // Step 4.1: the flag write or its read-back
        "sdap.provision.shared_container_not_cleared",  // Step 4.2: a shared container's unlink
        "sdap.provision.creator_share_failed",          // Steps 4.5 / 5.5 (ownership restored), or a resume's proven share
        "sdap.provision.owner_assignment_unverified",   // Step 5: the move could not be read back
        "sdap.provision.container_creation_failed",     // Step 6
        "sdap.provision.container_not_recorded",        // Step 7
        "sdap.provision.children_incomplete",           // Step 8, after Step 7 (round 40)
        "sdap.provision.files_incomplete"               // the file relocation, after Step 7 (round 40; round 26 item 3)
    ]);

    /** The failure's reason code (ProblemDetails extension), or null. */
    function reasonCodeOf(result) {
        return result && result.body && typeof result.body.reasonCode === "string" ? result.body.reasonCode : null;
    }

    /** Offers a retryable failure's call again (round 40 item 1); Cancel leaves the outcome as it is. */
    function offerRetry(primaryControl, start, commandName, path, successText, extra, retryCodes, result) {
        return confirmFirst({
            title: commandName,
            text: ns.refusalFor(commandName, result, start.entityName),
            confirmButtonLabel: commandName,
            cancelButtonLabel: "Cancel"
        }).then(function (again) {
            return again
                ? runDesignation(primaryControl, start, commandName, path, successText, extra, retryCodes)
                : result;
        });
    }

    /**
     * Runs one designation call and shows its outcome as Update Access does (a notification, or an alert) - or, for a
     * failure in `retryCodes`, offers the same call again in place.
     */
    function runDesignation(primaryControl, start, commandName, path, successText, extra, retryCodes) {
        var record = start.record;
        return postDesignation(path, record, extra).then(function (result) {
            if (result.skipped && result.reason === "no-token") {
                alert(commandName, "Sign-in needed - reload the page and retry. Nothing was changed.");
                return result;
            }

            // Whatever the outcome, the record may have changed (a partial pass answers 500): re-read it.
            refreshAfter(primaryControl, record);

            if (!result.ok) {
                if (retryCodes && retryCodes.indexOf(reasonCodeOf(result)) >= 0) {
                    return offerRetry(primaryControl, start, commandName, path, successText, extra, retryCodes, result);
                }

                alert(commandName, ns.refusalFor(commandName, result, start.entityName));
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
                // wizards' create-then-secure path (the creator rule). Round 40 item 1: a retryable failure after the
                // flag write is offered again in place.
                return runDesignation(primaryControl, start, "Make Secure", PROVISION_PATH,
                    "This " + ns.RECORD_WORDS[start.entityName] + " is now secure.", { transition: MAKE_SECURE_TRANSITION },
                    ns.MAKE_SECURE_RETRY_IN_PLACE);
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
