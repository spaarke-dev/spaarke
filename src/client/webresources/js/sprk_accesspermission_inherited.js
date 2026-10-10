/**
 * Inherited Access Permission - form library for To Do, Event, Communication and Document (unified-access-control-r2
 * task 173, GitHub #1423; owner rounds 81 and 84), and for Work Assignment and Project (task 175; owner round 87).
 *
 * Web Resource Name: sprk_accesspermission_inherited   (namespace Spaarke.AccessPermissionInherited)
 * Registered by:     scripts/Set-InheritedAccessPermissionFormLock.ps1 (OnLoad, pass execution context)
 *
 * The four CHILD tables (owner round 81, widened by round 84 "if a child has a parent then the access cannot be changed
 * manually"):
 *   - a record WITH a parent shows the parent's Access Permission (the most restrictive across its parents). The BFF
 *     writes it (CoreAncestorResolver, the shared stamp path) and keeps it in step (SecureChildReconciliationJob, every 2
 *     minutes). On the form the field is LOCKED, with an info notification naming the parent;
 *   - a record WITHOUT a parent keeps its own value, which the user sets: the field is editable.
 *
 * Work Assignment and Project (task 175, owner round 87 "the parent sets a FLOOR"): one filed under a matter or project
 * inherits Secure and Access Permission from its parents and may never be looser, but may be made stricter by hand:
 *   - the FLOOR is the most restrictive of the direct parents' sprk_accesspermission (Restricted > Limited > Standard;
 *     null = Standard) and sprk_issecure, each parent read once per form load with Xrm.WebApi.retrieveRecord (again after
 *     a save or a change of what the record is filed under);
 *   - sprk_accesspermission stays EDITABLE: a change to a value looser than the floor is put back to the previous value,
 *     with a warning ("Access permission cannot be lower than {floor} while this record is filed under {parent}."); an
 *     equal or stricter value is kept;
 *   - sprk_issecure, where the form carries it, is disabled while the record is filed (it changes only through Make
 *     Secure / Remove Secure, which the server floors);
 *   - an info notification says whether the value is inherited ("Access permission {value} is inherited from {parent}.")
 *     or set on this record ("... is set on this record (the minimum from {parent} is {floor})."), plus "Secure is
 *     inherited from {parent}." when the floor is secure;
 *   - a parent that cannot be read (or whose sprk_issecure reads empty) fails safe: both columns are LOCKED as on a
 *     child table, with "could not be checked" text;
 *   - with no parent, nothing is locked.
 *
 * This script decides nothing on the server's behalf and writes nothing of its own (ADR-002 WP-2: the server owns the
 * invariant and writes the inherited values; the form only locks, labels and puts back a looser pick). It reads the SAME
 * filing the server reads:
 *   - PARENT_LOOKUPS below is the server's ParentLineage.ChildFiling for the four child tables, pinned literally by
 *     ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap (a lookup that is not a parent there - a contact, an
 *     organization, a service request, a document's own current version - does not lock the field here either);
 *   - ROOT_PARENT_LOOKUPS and PAIR_PARENT_TABLES are the server's SecureRootInheritance.TypedFilingColumns and IsParent
 *     for work assignment and project. Those two tables are also filed through the polymorphic pair
 *     sprk_regardingrecordid (text holding a GUID) + sprk_regardingrecordtype (lookup to sprk_recordtype_ref): the pair is
 *     a parent only when the id is a GUID AND the type's sprk_recordlogicalname is one of PAIR_PARENT_TABLES (read once per
 *     type per form load). A pair whose type cannot be read names no parent (the server floors any looser save).
 *
 * Where the filing is read:
 *   - a column ON the form: its current value (an unsaved pick or clear re-evaluates at once, through OnChange);
 *   - a column NOT on the form: the saved value, read once with Xrm.WebApi.retrieveRecord (and again after each save).
 *     A read that fails leaves only the columns on the form deciding on a child table (logged to the console); on a
 *     work assignment or project it is the fail-safe lock (a parent the form does not carry may be missing).
 *
 * "Locked" means, for each locked column: every control bound to it that this script found enabled is disabled; the
 * column is not submitted (setSubmitMode "never"); and a change that still arrives (a PCF bound to the column - the
 * TrackingFieldTrio pill on the To Do, Event, Work Assignment and Project main forms) is put back to the value the field
 * had when it was locked. Unlocking re-enables only the controls this script disabled, so a read-only form stays
 * read-only.
 *
 * ADR-006 amendment 2.1 (owner round 86, path B, #1462) - a thin form-event script, within its limits: platform form APIs
 * only (formContext, Xrm.WebApi); no UI of its own (the platform's form notifications and control state); no access
 * decision (the server decides and writes the values and refuses a looser save; this only locks, labels and puts back);
 * fails safe (an unreadable parent locks; a failed filing read leaves the columns on the form deciding, and the server's
 * reconcile reverts any edit that slips through); namespaced (Spaarke.AccessPermissionInherited) and idempotent (one
 * wiring per form load); jest-tested (Spaarke.UI.Components/src/__tests__/accessPermissionInherited*.test.ts);
 * registered by a checked-in operator script (scripts/Set-InheritedAccessPermissionFormLock.ps1). The maps it relies on
 * are pinned to the server's rules by .NET tests (PARENT_LOOKUPS by ParentLineageTests; ROOT_PARENT_LOOKUPS /
 * PAIR_PARENT_TABLES by task 175's test).
 *
 * ES5, no build step, never throws into the form.
 */
// A property of window, not a top-level var: the form resolves "Spaarke.AccessPermissionInherited.onLoad" from window,
// and a global var binding could not be replaced by a later load of the library on the same page.
window.Spaarke = window.Spaarke || {};

window.Spaarke.AccessPermissionInherited = (function () {
    "use strict";

    var ns = {};
    // 1.0.0 - task 173: the four child tables. 1.1.0 - task 175 (owner round 84): work assignment and project (typed
    // lookups and the polymorphic pair; sprk_issecure locked with sprk_accesspermission). 1.2.0 - task 175 (owner round
    // 87): on work assignment and project the lock is a FLOOR lock (a stricter value is allowed; a looser one is put
    // back); the four child tables are unchanged.
    ns.VERSION = "1.2.0";

    var LOG = "[Spaarke.AccessPermissionInherited v" + ns.VERSION + "]";
    var COLUMN = "sprk_accesspermission";
    var SECURE_COLUMN = "sprk_issecure";
    var PAIR_ID = "sprk_regardingrecordid";
    var PAIR_TYPE = "sprk_regardingrecordtype";
    var PAIR_NAME = "sprk_regardingrecordname";
    var RECORD_TYPE_TABLE = "sprk_recordtype_ref";
    var RECORD_TYPE_LOGICAL_NAME = "sprk_recordlogicalname";
    var NOTIFICATION_ID = "sprk_accesspermission_inherited";
    var FLOOR_WARNING_ID = "sprk_accesspermission_floor";
    var PARENT_SELECT = "?$select=" + COLUMN + "," + SECURE_COLUMN;
    var FORM_TYPE_CREATE = 1;
    var FORMATTED = "@OData.Community.Display.V1.FormattedValue";
    var GUID = /^\{?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}?$/i;
    var EMPTY_GUID = "00000000-0000-0000-0000-000000000000";

    /** The locked attributes already wired on this page (one entry per form load). */
    var wired = [];

    /* PARENT_LOOKUPS:BEGIN - the server's ParentLineage.ChildFiling for the four tables (column -> parent table). JSON. */
    ns.PARENT_LOOKUPS = {
        "sprk_communication": {
            "sprk_communicationthread": "sprk_communicationthread",
            "sprk_regardinganalysis": "sprk_analysis",
            "sprk_regardingbudget": "sprk_budget",
            "sprk_regardingevent": "sprk_event",
            "sprk_regardinginvoice": "sprk_invoice",
            "sprk_regardingmatter": "sprk_matter",
            "sprk_regardingproject": "sprk_project",
            "sprk_regardingreportcard": "sprk_reportcard",
            "sprk_regardingworkassignment": "sprk_workassignment"
        },
        "sprk_document": {
            "sprk_canonicaldocument": "sprk_document",
            "sprk_invoice": "sprk_invoice",
            "sprk_matter": "sprk_matter",
            "sprk_parentdocument": "sprk_document",
            "sprk_project": "sprk_project",
            "sprk_relatedagreement": "sprk_agreement",
            "sprk_relatedcommunication": "sprk_communication",
            "sprk_relatedevent": "sprk_event",
            "sprk_relatedinvoice": "sprk_invoice",
            "sprk_relatedmatter": "sprk_matter",
            "sprk_relatedproject": "sprk_project",
            "sprk_relatedtodo": "sprk_todo",
            "sprk_relatedworkassignment": "sprk_workassignment",
            "sprk_workassignment": "sprk_workassignment"
        },
        "sprk_event": {
            "sprk_regardingagreement": "sprk_agreement",
            "sprk_regardinganalysis": "sprk_analysis",
            "sprk_regardingbudget": "sprk_budget",
            "sprk_regardingcommunication": "sprk_communication",
            "sprk_regardingevent": "sprk_event",
            "sprk_regardinginvoice": "sprk_invoice",
            "sprk_regardingmatter": "sprk_matter",
            "sprk_regardingproject": "sprk_project",
            "sprk_regardingreportcard": "sprk_reportcard",
            "sprk_regardingworkassignment": "sprk_workassignment"
        },
        "sprk_todo": {
            "sprk_regardingagreement": "sprk_agreement",
            "sprk_regardinganalysis": "sprk_analysis",
            "sprk_regardingbudget": "sprk_budget",
            "sprk_regardingcommunication": "sprk_communication",
            "sprk_regardingdocument": "sprk_document",
            "sprk_regardingevent": "sprk_event",
            "sprk_regardinginvoice": "sprk_invoice",
            "sprk_regardingmatter": "sprk_matter",
            "sprk_regardingproject": "sprk_project",
            "sprk_regardingreportcard": "sprk_reportcard",
            "sprk_regardingworkassignment": "sprk_workassignment"
        }
    };
    /* PARENT_LOOKUPS:END */

    /* ROOT_PARENT_LOOKUPS:BEGIN - the server's SecureRootInheritance.TypedFilingColumns (column -> parent table). JSON. */
    ns.ROOT_PARENT_LOOKUPS = {
        "sprk_project": {},
        "sprk_workassignment": {
            "sprk_regardingmatter": "sprk_matter",
            "sprk_regardingproject": "sprk_project"
        }
    };
    /* ROOT_PARENT_LOOKUPS:END */
    /* PAIR_PARENT_TABLES:BEGIN - the tables a polymorphic regarding pair files a work assignment or project under (SecureRootInheritance.IsParent). JSON. */
    ns.PAIR_PARENT_TABLES = ["sprk_matter", "sprk_project"];
    /* PAIR_PARENT_TABLES:END */

    /** sprk_accesspermission option values (the integers the BFF reads). */
    ns.PERMISSION = Object.freeze({ STANDARD: 100000000, LIMITED: 100000001, RESTRICTED: 100000002 });

    var PERMISSION_LABELS = ["Standard", "Limited", "Restricted"];

    /**
     * How restrictive a sprk_accesspermission value is: Standard 0 < Limited 1 < Restricted 2. Null (and any value this
     * script does not know) is Standard - the server's rule (null = Standard).
     */
    ns.rankOf = function (value) {
        if (value === ns.PERMISSION.RESTRICTED) return 2;
        if (value === ns.PERMISSION.LIMITED) return 1;
        return 0;
    };

    /** The label of a value ("Standard", "Limited" or "Restricted"). */
    ns.permissionLabel = function (value) {
        return PERMISSION_LABELS[ns.rankOf(value)];
    };

    function nameList(names) {
        var shown = names.filter(function (n, i) { return !!n && names.indexOf(n) === i; });
        return shown.length > 0 ? shown.join(", ") : "its parent record";
    }

    /**
     * The notification text on a child table (owner round 81: 'Access permission is inherited from {parent name}').
     * @param {string[]} names - the parents' display names
     * @param {boolean} pending - the parent was picked or changed on this form and is not saved yet
     * @returns {string}
     */
    ns.notificationText = function (names, pending) {
        var who = nameList(names);
        return pending
            ? "Access permission is inherited from " + who + ". It is set when the record is saved."
            : "Access permission is inherited from " + who + ".";
    };

    /**
     * The floor a work assignment's or project's direct parents set (owner round 87). Pure.
     * @param {Array<{name: string, permission: (number|null), secure: boolean}>} parents - each parent as read
     * @returns {{rank: number, secure: boolean, floorNames: string[], secureNames: string[]}} the most restrictive Access
     *          Permission (as a rank) and whether any parent is secure; floorNames - the parents that set the rank (all of
     *          them when it is Standard); secureNames - the secure ones
     */
    ns.floorOf = function (parents) {
        var rank = 0;
        parents.forEach(function (p) { rank = Math.max(rank, ns.rankOf(p.permission)); });
        return {
            rank: rank,
            secure: parents.some(function (p) { return p.secure === true; }),
            floorNames: parents.filter(function (p) { return ns.rankOf(p.permission) === rank; })
                .map(function (p) { return p.name; }),
            secureNames: parents.filter(function (p) { return p.secure === true; }).map(function (p) { return p.name; })
        };
    };

    /**
     * The info notification on a filed work assignment or project (owner round 87), or null when there is nothing to say
     * (Standard under a Standard, non-secure floor). Pure.
     * @param {(number|null)} value - the form's sprk_accesspermission
     * @param {object} floor - ns.floorOf(...)
     * @returns {string|null}
     */
    ns.floorNotificationText = function (value, floor) {
        var rank = ns.rankOf(value);
        var floorLabel = PERMISSION_LABELS[floor.rank];
        var from = nameList(floor.floorNames);
        var text = "";
        if (rank < floor.rank) {
            // Saved below the floor (a parent picked on this form, or a value the server has not raised yet).
            text = "Access permission " + floorLabel + " is inherited from " + from + ". It is set when the record is saved.";
        } else if (rank === floor.rank) {
            if (floor.rank > 0) {
                text = "Access permission " + floorLabel + " is inherited from " + from + ".";
            }
        } else {
            text = "Access permission " + PERMISSION_LABELS[rank] + " is set on this record (the minimum from " + from +
                " is " + floorLabel + ").";
        }
        if (floor.secure) {
            text += (text ? " " : "") + "Secure is inherited from " + nameList(floor.secureNames) + ".";
        }
        return text || null;
    };

    /** The warning when a looser value is put back (owner round 87). */
    ns.floorRevertText = function (floor) {
        return "Access permission cannot be lower than " + PERMISSION_LABELS[floor.rank] +
            " while this record is filed under " + nameList(floor.floorNames) + ".";
    };

    /** The text of the fail-safe lock: a parent (or the record's own saved filing) could not be read. */
    ns.uncheckedText = function (names) {
        var against = names.some(function (n) { return !!n; }) ? nameList(names) : "what this record is filed under";
        return "Access permission and Secure could not be checked against " + against +
            ", so they are locked. Reload the record to try again.";
    };

    /**
     * The parents a CHILD record names: each parent lookup's value from the form when the form carries it, otherwise the
     * saved value. Pure (no Xrm), so it can be reasoned about on its own.
     * @param {object} lookups - PARENT_LOOKUPS[table]
     * @param {function(string): (null|undefined|Array)} formValue - the form attribute's getValue(), or undefined when
     *        the attribute is not on the form
     * @param {object} saved - column -> { name } for the saved, non-null lookups that are NOT on the form
     * @returns {string[]} the parents' display names (empty: no parent)
     */
    ns.parentNames = function (lookups, formValue, saved) {
        var names = [];
        Object.keys(lookups).forEach(function (column) {
            var value = formValue(column);
            if (value !== undefined) {
                if (value && value.length > 0 && value[0] && value[0].id) {
                    names.push(value[0].name || "");
                }
                return;
            }
            if (saved && saved[column]) {
                names.push(saved[column].name || "");
            }
        });
        return names.filter(function (n, i) { return names.indexOf(n) === i; });
    };

    /**
     * Whether a polymorphic pair files the record under a parent: its id is a GUID (not the empty one) AND its type's
     * logical name is one of PAIR_PARENT_TABLES (the server's SecureRootInheritance rule). Pure.
     * @param {string|null} recordId - sprk_regardingrecordid
     * @param {string|null} logicalName - the type's sprk_recordlogicalname (null: not known)
     * @returns {boolean}
     */
    ns.pairIsParent = function (recordId, logicalName) {
        return isRecordId(recordId) && typeof logicalName === "string" &&
            ns.PAIR_PARENT_TABLES.indexOf(logicalName.trim().toLowerCase()) >= 0;
    };

    /** Whether a pair id names a record: a GUID, not the empty one (the server's Guid.TryParse rule). */
    function isRecordId(text) {
        return typeof text === "string" && GUID.test(text.trim()) && normalizeId(text.trim()) !== EMPTY_GUID;
    }

    function normalizeId(id) {
        return String(id || "").replace(/[{}]/g, "").toLowerCase();
    }

    /**
     * OnLoad (pass execution context). Registers the re-evaluation on every filing column on the form, the guard on each
     * locked column itself, and the re-read after a save.
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            var table = formContext.data.entity.getEntityName();
            var root = !ns.PARENT_LOOKUPS[table] && !!ns.ROOT_PARENT_LOOKUPS[table];
            var lookups = ns.PARENT_LOOKUPS[table] || ns.ROOT_PARENT_LOOKUPS[table];
            if (!lookups) {
                return; // not one of the six tables: nothing to lock
            }
            var entries = (root ? [COLUMN, SECURE_COLUMN] : [COLUMN])
                .map(function (column) {
                    var a = formContext.getAttribute(column);
                    return a ? { column: column, attribute: a, locked: false, lockedValue: null, disabledByUs: [] } : null;
                })
                .filter(function (e) { return !!e; });
            if (entries.length === 0) {
                return; // the column is not on this form: nothing to lock
            }
            // Idempotent: a second registration on the same form load (a maker adding the handler twice) wires nothing
            // again. Keyed on the attribute AND the record, so a form the platform reuses for another record still wires.
            var recordKey = formContext.data.entity.getId() || "";
            for (var w = 0; w < wired.length; w++) {
                if (wired[w].attribute === entries[0].attribute && wired[w].record === recordKey) {
                    return;
                }
            }
            wired.push({ attribute: entries[0].attribute, record: recordKey });

            if (root) {
                wireRoot(formContext, table, lookups, entries);
            } else {
                wireChild(formContext, table, lookups, entries);
            }
        } catch (e) {
            console.error(LOG, "onLoad failed", e);
        }
    };

    /** Task 173: a child table - sprk_accesspermission LOCKED while the record has a parent. */
    function wireChild(formContext, table, lookups, entries) {
        var state = { saved: {} };

        var formValue = function (column) {
            var a = formContext.getAttribute(column);
            return a ? a.getValue() : undefined;
        };

        var parentDirty = function () {
            return Object.keys(lookups).some(function (column) {
                var a = formContext.getAttribute(column);
                return !!a && a.getIsDirty();
            });
        };

        var evaluate = function () {
            try {
                var names = ns.parentNames(lookups, formValue, state.saved);
                if (names.length > 0) {
                    entries.forEach(lockEntry);
                    formContext.ui.setFormNotification(ns.notificationText(names,
                        formContext.ui.getFormType() === FORM_TYPE_CREATE || parentDirty()), "INFO", NOTIFICATION_ID);
                } else {
                    entries.forEach(unlockEntry);
                    formContext.ui.clearFormNotification(NOTIFICATION_ID);
                }
            } catch (e) {
                console.error(LOG, "evaluate failed", e);
            }
        };

        Object.keys(lookups).forEach(function (column) {
            var a = formContext.getAttribute(column);
            if (a) {
                a.addOnChange(evaluate);
            }
        });

        entries.forEach(function (entry) {
            entry.attribute.addOnChange(function () {
                if (putBackIfLocked(entry)) {
                    evaluate();
                }
            });
        });

        var reload = function () {
            readSaved(formContext, table, lookups, false).then(function (saved) {
                state.saved = saved.lookups;
                evaluate();
            });
        };

        if (formContext.data.entity.addOnPostSave) {
            formContext.data.entity.addOnPostSave(function () {
                entries.forEach(function (entry) { entry.lockedValue = entry.attribute.getValue(); });
                reload();
            });
        }

        evaluate(); // at once, from the lookups on the form
        reload();   // then with the saved lookups the form does not carry
    }

    /**
     * Task 175 (owner round 87): a work assignment or project - a FLOOR lock. Modes: "none" (no parent: nothing locked),
     * "waiting" (a filing or parent read is in flight: nothing changes until it answers), "floor" (sprk_accesspermission
     * editable but never looser than the floor; sprk_issecure disabled) and "locked" (a parent could not be read: both
     * locked, fail safe).
     */
    function wireRoot(formContext, table, lookups, entries) {
        var permissionEntry = entries.filter(function (e) { return e.column === COLUMN; })[0] || null;
        var secureEntry = entries.filter(function (e) { return e.column === SECURE_COLUMN; })[0] || null;
        var filingColumns = Object.keys(lookups).concat([PAIR_ID, PAIR_TYPE]);
        var hasMissingFiling = filingColumns.some(function (column) { return !formContext.getAttribute(column); });

        var state = {
            mode: "waiting",
            saved: {},
            savedPair: null,
            // Whether the saved filing the form does not carry is known (nothing to read on a new record or when every
            // filing column is on the form).
            savedKnown: !hasMissingFiling || formContext.ui.getFormType() === FORM_TYPE_CREATE,
            types: {},
            parents: {},
            generation: 0,
            floor: null,
            lastAccepted: permissionEntry ? permissionEntry.attribute.getValue() : null
        };

        var clearWarning = function () {
            formContext.ui.clearFormNotification(FLOOR_WARNING_ID);
        };

        /** A looser pick than the floor is put back to the last accepted value; any other value is accepted. */
        var checkPermission = function () {
            if (!permissionEntry || state.mode !== "floor") {
                return;
            }
            var value = permissionEntry.attribute.getValue();
            if (ns.rankOf(value) < state.floor.rank && value !== state.lastAccepted) {
                permissionEntry.attribute.setValue(state.lastAccepted);
                formContext.ui.setFormNotification(ns.floorRevertText(state.floor), "WARNING", FLOOR_WARNING_ID);
                return;
            }
            if (value !== state.lastAccepted) {
                clearWarning();
            }
            state.lastAccepted = value;
        };

        var showFloor = function () {
            var value = permissionEntry ? permissionEntry.attribute.getValue() : state.lastAccepted;
            var text = ns.floorNotificationText(value, state.floor);
            if (text) {
                formContext.ui.setFormNotification(text, "INFO", NOTIFICATION_ID);
            } else {
                formContext.ui.clearFormNotification(NOTIFICATION_ID);
            }
        };

        /** The fail-safe lock: both columns locked as on a child table. */
        var lockUnchecked = function (names) {
            state.mode = "locked";
            state.floor = null;
            entries.forEach(lockEntry);
            clearWarning();
            formContext.ui.setFormNotification(ns.uncheckedText(names), "INFO", NOTIFICATION_ID);
        };

        var evaluate = function () {
            try {
                var found = rootParentRefs(formContext, lookups, state, evaluate);
                if (found.refs.length === 0) {
                    if (found.pending || !state.savedKnown) {
                        state.mode = "waiting";
                        return;
                    }
                    if (state.savedFailed) {
                        lockUnchecked([]); // whether it is filed at all is not known: fail safe
                        return;
                    }
                    state.mode = "none";
                    state.floor = null;
                    entries.forEach(unlockEntry);
                    formContext.ui.clearFormNotification(NOTIFICATION_ID);
                    clearWarning();
                    if (permissionEntry) {
                        state.lastAccepted = permissionEntry.attribute.getValue();
                    }
                    return;
                }

                var reads = found.refs.map(function (ref) { return readParent(ref, state, evaluate); });
                if (reads.some(function (r) { return !r.done; })) {
                    state.mode = "waiting";
                    return;
                }
                // Fail safe: a parent that cannot be read, or a saved filing that could not be read (a parent the form
                // does not carry may be missing from the floor).
                if (state.savedFailed || reads.some(function (r) { return !r.ok; })) {
                    lockUnchecked(found.refs.map(function (r) { return r.name; }));
                    return;
                }

                if (permissionEntry && permissionEntry.locked) {
                    unlockEntry(permissionEntry); // out of a fail-safe lock: the floor rule applies again
                    state.lastAccepted = permissionEntry.attribute.getValue();
                }
                if (secureEntry) {
                    lockEntry(secureEntry); // changed only through Make Secure / Remove Secure
                }
                state.mode = "floor";
                state.floor = ns.floorOf(found.refs.map(function (ref, i) {
                    return { name: ref.name, permission: reads[i].permission, secure: reads[i].secure };
                }));
                checkPermission();
                showFloor();
            } catch (e) {
                console.error(LOG, "evaluate failed", e);
            }
        };

        /** A change of what the record is filed under: the parents are read again. */
        var refiled = function () {
            state.parents = {};
            state.generation++;
            clearWarning();
            evaluate();
        };

        filingColumns.forEach(function (column) {
            var a = formContext.getAttribute(column);
            if (a) {
                a.addOnChange(refiled);
            }
        });

        if (permissionEntry) {
            permissionEntry.attribute.addOnChange(function () {
                if (putBackIfLocked(permissionEntry)) {
                    evaluate();
                    return;
                }
                if (state.mode === "floor") {
                    checkPermission();
                    showFloor();
                } else if (state.mode === "none") {
                    state.lastAccepted = permissionEntry.attribute.getValue();
                }
                // "waiting": checked against the floor when the reads answer (lastAccepted is not moved meanwhile).
            });
        }
        if (secureEntry) {
            secureEntry.attribute.addOnChange(function () {
                putBackIfLocked(secureEntry);
            });
        }

        var reload = function () {
            readSaved(formContext, table, lookups, true).then(function (saved) {
                state.saved = saved.lookups;
                state.savedPair = saved.pair;
                state.savedKnown = true;
                state.savedFailed = saved.failed === true;
                evaluate();
            });
        };

        if (formContext.data.entity.addOnPostSave) {
            formContext.data.entity.addOnPostSave(function () {
                entries.forEach(function (entry) { entry.lockedValue = entry.attribute.getValue(); });
                if (permissionEntry) {
                    state.lastAccepted = permissionEntry.attribute.getValue();
                }
                state.parents = {};
                state.generation++;
                reload();
            });
        }

        evaluate(); // at once, from the filing on the form
        reload();   // then with the saved filing the form does not carry
    }

    /**
     * The direct parents a work assignment or project names, as { refs: [{ table, id, name }], pending } - the typed
     * lookups (from the form, else saved) and the pair parent. `pending`: the pair's type is being read.
     */
    function rootParentRefs(formContext, lookups, state, evaluate) {
        var refs = [];
        var add = function (ref) {
            for (var i = 0; i < refs.length; i++) {
                if (refs[i].table === ref.table && refs[i].id === ref.id) return;
            }
            refs.push(ref);
        };
        Object.keys(lookups).forEach(function (column) {
            var a = formContext.getAttribute(column);
            if (a) {
                var value = a.getValue();
                if (value && value.length > 0 && value[0] && value[0].id) {
                    add({ table: lookups[column], id: normalizeId(value[0].id), name: value[0].name || "" });
                }
            } else if (state.saved[column] && state.saved[column].id) {
                add({ table: lookups[column], id: normalizeId(state.saved[column].id), name: state.saved[column].name || "" });
            }
        });
        var pair = pairParent(formContext, state, evaluate);
        if (pair.ref) {
            add(pair.ref);
        }
        return { refs: refs, pending: pair.pending };
    }

    /**
     * One parent's Access Permission and Secure flag, read once per parent (cached in state.parents until a save or a
     * re-file). Returns the cache entry { done, ok, permission, secure }; a read in flight re-evaluates when it answers.
     * A failed read, or a sprk_issecure that reads empty, is not ok (fail safe).
     */
    function readParent(ref, state, evaluate) {
        var key = ref.table + "|" + ref.id;
        var entry = state.parents[key];
        if (entry) {
            return entry;
        }
        entry = { done: false, ok: false, permission: null, secure: false };
        state.parents[key] = entry;
        var generation = state.generation;
        var settle = function (ok, permission, secure) {
            if (generation !== state.generation) {
                return; // re-filed or saved meanwhile: a newer read decides
            }
            state.parents[key] = { done: true, ok: ok, permission: permission, secure: secure };
            evaluate();
        };
        try {
            Promise.resolve(Xrm.WebApi.retrieveRecord(ref.table, ref.id, PARENT_SELECT))
                .then(function (row) {
                    if (!row || typeof row[SECURE_COLUMN] !== "boolean") {
                        console.warn(LOG, "a parent's Secure flag read empty; the access columns are locked.", ref.table);
                        settle(false, null, false);
                        return;
                    }
                    var permission = typeof row[COLUMN] === "number" ? row[COLUMN] : null;
                    settle(true, permission, row[SECURE_COLUMN] === true);
                })
                .catch(function (error) {
                    console.warn(LOG, "a parent could not be read; the access columns are locked.", error);
                    settle(false, null, false);
                });
        } catch (error) {
            console.warn(LOG, "a parent could not be read; the access columns are locked.", error);
            state.parents[key] = { done: true, ok: false, permission: null, secure: false };
            return state.parents[key];
        }
        return entry;
    }

    /**
     * The parent the polymorphic pair names, as { ref: { table, id, name } | null, pending }. No ref when the pair names
     * none (no id, an id that is not a GUID, no type, a type that is not a parent table, or a type that cannot be read);
     * `pending` while the type is read (once per type id for this form load; the form re-evaluates when it answers).
     */
    function pairParent(formContext, state, evaluate) {
        var idAttribute = formContext.getAttribute(PAIR_ID);
        var typeAttribute = formContext.getAttribute(PAIR_TYPE);
        var saved = state.savedPair || {};

        var recordId = idAttribute ? idAttribute.getValue() : saved.id;
        var typeId = null;
        var typeName = "";
        if (typeAttribute) {
            var value = typeAttribute.getValue();
            if (value && value.length > 0 && value[0] && value[0].id) {
                typeId = normalizeId(value[0].id);
                typeName = value[0].name || "";
            }
        } else if (saved.typeId) {
            typeId = normalizeId(saved.typeId);
            typeName = saved.typeName || "";
        }

        if (!typeId || !isRecordId(recordId)) {
            return { ref: null, pending: false }; // no type, or the id is not a record identifier: no parent
        }

        var type = state.types[typeId];
        if (!type) {
            state.types[typeId] = { done: false, logicalName: null };
            readRecordType(typeId).then(function (logicalName) {
                state.types[typeId] = { done: true, logicalName: logicalName };
                evaluate();
            });
            return { ref: null, pending: true };
        }
        if (!type.done) {
            return { ref: null, pending: true };
        }
        if (!ns.pairIsParent(recordId, type.logicalName)) {
            return { ref: null, pending: false };
        }

        var nameAttribute = formContext.getAttribute(PAIR_NAME);
        var recordName = nameAttribute ? nameAttribute.getValue() : null;
        var name = typeof recordName === "string" && recordName.trim() ? recordName : (typeName || "its parent record");
        return {
            ref: { table: type.logicalName.trim().toLowerCase(), id: normalizeId(recordId.trim()), name: name },
            pending: false
        };
    }

    /** A record type's sprk_recordlogicalname; resolves null when it cannot be read (logged) - never rejects. */
    function readRecordType(typeId) {
        try {
            return Promise.resolve(Xrm.WebApi.retrieveRecord(RECORD_TYPE_TABLE, typeId, "?$select=" + RECORD_TYPE_LOGICAL_NAME))
                .then(function (row) {
                    var name = row ? row[RECORD_TYPE_LOGICAL_NAME] : null;
                    return typeof name === "string" && name ? name : null;
                })
                .catch(function (error) {
                    console.warn(LOG, "the regarding record type could not be read; the pair names no parent.", error);
                    return null;
                });
        } catch (error) {
            console.warn(LOG, "the regarding record type could not be read; the pair names no parent.", error);
            return Promise.resolve(null);
        }
    }

    /** A change that arrives on a locked column (a bound PCF) is put back; true when it was. */
    function putBackIfLocked(entry) {
        if (entry.locked && entry.attribute.getValue() !== entry.lockedValue) {
            entry.attribute.setValue(entry.lockedValue);
            return true;
        }
        return false;
    }

    function lockEntry(entry) {
        if (entry.locked) {
            return;
        }
        entry.locked = true;
        entry.lockedValue = entry.attribute.getValue();
        entry.disabledByUs = [];
        entry.attribute.controls.forEach(function (control) {
            if (!control.getDisabled()) {
                control.setDisabled(true);
                entry.disabledByUs.push(control);
            }
        });
        entry.attribute.setSubmitMode("never");
    }

    function unlockEntry(entry) {
        if (!entry.locked) {
            return;
        }
        entry.locked = false;
        entry.disabledByUs.forEach(function (control) { control.setDisabled(false); });
        entry.disabledByUs = [];
        entry.attribute.setSubmitMode("dirty");
    }

    /**
     * The saved filing the form does not carry: { lookups: column -> { id, name } for the saved parent lookups not on the
     * form, pair: { id, typeId, typeName } for the pair columns not on the form (root tables) or null }. Resolves empty
     * for an unsaved record, when every filing column is on the form, or when the read fails (logged).
     */
    function readSaved(formContext, table, lookups, root) {
        var empty = { lookups: {}, pair: null };
        var missing = Object.keys(lookups).filter(function (column) { return !formContext.getAttribute(column); });
        var pairIdMissing = root && !formContext.getAttribute(PAIR_ID);
        var pairTypeMissing = root && !formContext.getAttribute(PAIR_TYPE);
        var id = (formContext.data.entity.getId() || "").replace(/[{}]/g, "");
        if (formContext.ui.getFormType() === FORM_TYPE_CREATE || !id ||
            (missing.length === 0 && !pairIdMissing && !pairTypeMissing)) {
            return Promise.resolve(empty);
        }

        var select = missing.map(function (column) { return "_" + column + "_value"; });
        if (pairIdMissing) {
            select.push(PAIR_ID);
        }
        if (pairTypeMissing) {
            select.push("_" + PAIR_TYPE + "_value");
        }
        try {
            return Promise.resolve(Xrm.WebApi.retrieveRecord(table, id, "?$select=" + select.join(",")))
                .then(function (row) {
                    var saved = {};
                    missing.forEach(function (column) {
                        var key = "_" + column + "_value";
                        if (row && row[key]) {
                            saved[column] = { id: row[key], name: row[key + FORMATTED] || "" };
                        }
                    });
                    var pair = null;
                    if (pairIdMissing || pairTypeMissing) {
                        var typeKey = "_" + PAIR_TYPE + "_value";
                        pair = {
                            id: pairIdMissing && row ? row[PAIR_ID] || null : null,
                            typeId: pairTypeMissing && row ? row[typeKey] || null : null,
                            typeName: pairTypeMissing && row ? row[typeKey + FORMATTED] || "" : ""
                        };
                    }
                    return { lookups: saved, pair: pair };
                })
                .catch(function (error) {
                    console.warn(LOG, "the saved filing could not be read; only the columns on the form decide.", error);
                    return { lookups: {}, pair: null, failed: true };
                });
        } catch (error) {
            console.warn(LOG, "the saved filing could not be read; only the columns on the form decide.", error);
            return Promise.resolve({ lookups: {}, pair: null, failed: true });
        }
    }

    return ns;
})();
