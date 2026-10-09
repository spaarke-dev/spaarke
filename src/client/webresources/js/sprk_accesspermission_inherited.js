/**
 * Inherited Access Permission - form library for To Do, Event, Communication and Document (unified-access-control-r2
 * task 173, GitHub #1423; owner rounds 81 and 84), and for Work Assignment and Project (task 175; owner round 84).
 *
 * Web Resource Name: sprk_accesspermission_inherited   (namespace Spaarke.AccessPermissionInherited)
 * Registered by:     scripts/Set-InheritedAccessPermissionFormLock.ps1 (OnLoad, pass execution context)
 *
 * The rule (owner round 81, widened by round 84 "a child's access always follows its parent ... if a child has a parent
 * then the access cannot be changed manually"):
 *   - a record WITH a parent shows the parent's Access Permission (the most restrictive across its parents). The BFF
 *     writes it (CoreAncestorResolver, the shared stamp path) and keeps it in step (SecureChildReconciliationJob, every 2
 *     minutes). On the form the field is LOCKED, with an info notification naming the parent;
 *   - a record WITHOUT a parent keeps its own value, which the user sets: the field is editable.
 * A work assignment or project filed under a matter or project (task 175) takes the parent's sprk_issecure AND
 * sprk_accesspermission: both are locked while it has a parent ("Access permission and Secure are inherited from ...");
 * a parentless one keeps and edits its own. sprk_issecure is locked only where the form carries it.
 *
 * This script decides nothing about the value and writes nothing (ADR-002 WP-2: the server owns the invariant; the form
 * only locks and labels). It only answers "does this record have a parent?", from the SAME filing the server reads:
 *   - PARENT_LOOKUPS below is the server's ParentLineage.ChildFiling for the four child tables, pinned literally by
 *     ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap (a lookup that is not a parent there - a contact, an
 *     organization, a service request, a document's own current version - does not lock the field here either);
 *   - ROOT_PARENT_LOOKUPS and PAIR_PARENT_TABLES are the server's SecureRootInheritance.TypedFilingColumns and IsParent
 *     for work assignment and project. Those two tables are also filed through the polymorphic pair
 *     sprk_regardingrecordid (text holding a GUID) + sprk_regardingrecordtype (lookup to sprk_recordtype_ref): the pair is
 *     a parent only when the id is a GUID AND the type's sprk_recordlogicalname is one of PAIR_PARENT_TABLES (read once per
 *     type per form load). A pair whose type cannot be read does not lock (the server reverts any edit on a parented
 *     record).
 *
 * Where the parents are read:
 *   - a column ON the form: its current value (an unsaved pick or clear re-evaluates at once, through OnChange);
 *   - a column NOT on the form: the saved value, read once with Xrm.WebApi.retrieveRecord (and again after each save).
 *     A read that fails leaves only the columns on the form deciding (logged to the console).
 *
 * "Locked" means, for each locked column: every control bound to it that this script found enabled is disabled; the
 * column is not submitted (setSubmitMode "never"); and a change that still arrives (a PCF bound to the column - the
 * TrackingFieldTrio pill on the To Do, Event, Work Assignment and Project main forms) is put back to the value the field
 * had when it was locked. Unlocking re-enables only the controls this script disabled, so a read-only form stays
 * read-only.
 *
 * ADR-006 amendment 2.1 (owner round 86, path B, #1462) - a thin form-event script, within its limits: platform form APIs
 * only (formContext, Xrm.WebApi); no UI of its own (the platform's form notification and control state); no access
 * decision (the server decides and writes the values; this only locks and labels); fails safe (a failed read leaves the
 * columns on the form deciding, and the server's reconcile reverts any edit that slips through); namespaced
 * (Spaarke.AccessPermissionInherited) and idempotent (one wiring per form load); jest-tested
 * (Spaarke.UI.Components/src/__tests__/accessPermissionInherited*.test.ts); registered by a checked-in operator script
 * (scripts/Set-InheritedAccessPermissionFormLock.ps1). The values it relies on are pinned to the server's rules by .NET
 * tests (PARENT_LOOKUPS by ParentLineageTests; ROOT_PARENT_LOOKUPS / PAIR_PARENT_TABLES by task 175's test).
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
    // lookups and the polymorphic pair; sprk_issecure locked with sprk_accesspermission).
    ns.VERSION = "1.1.0";

    var LOG = "[Spaarke.AccessPermissionInherited v" + ns.VERSION + "]";
    var COLUMN = "sprk_accesspermission";
    var SECURE_COLUMN = "sprk_issecure";
    var PAIR_ID = "sprk_regardingrecordid";
    var PAIR_TYPE = "sprk_regardingrecordtype";
    var PAIR_NAME = "sprk_regardingrecordname";
    var RECORD_TYPE_TABLE = "sprk_recordtype_ref";
    var RECORD_TYPE_LOGICAL_NAME = "sprk_recordlogicalname";
    var NOTIFICATION_ID = "sprk_accesspermission_inherited";
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

    /**
     * The notification text (owner round 81: 'Access permission is inherited from {parent name}'; task 175 for a work
     * assignment or project: 'Access permission and Secure are inherited from {parent name}').
     * @param {string[]} names - the parents' display names
     * @param {boolean} pending - the parent was picked or changed on this form and is not saved yet
     * @param {boolean} [root] - a work assignment or project (Access permission AND Secure)
     * @returns {string}
     */
    ns.notificationText = function (names, pending, root) {
        var shown = names.filter(function (n) { return !!n; }); // a parent whose name is not known is not named
        var who = shown.length > 0 ? shown.join(", ") : "its parent record";
        if (root) {
            return pending
                ? "Access permission and Secure are inherited from " + who + ". They are set when the record is saved."
                : "Access permission and Secure are inherited from " + who + ".";
        }
        return pending
            ? "Access permission is inherited from " + who + ". It is set when the record is saved."
            : "Access permission is inherited from " + who + ".";
    };

    /**
     * The parents a record names: each parent lookup's value from the form when the form carries it, otherwise the saved
     * value. Pure (no Xrm), so it can be reasoned about on its own.
     * @param {object} lookups - PARENT_LOOKUPS[table] or ROOT_PARENT_LOOKUPS[table]
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
     * OnLoad (pass execution context). Registers the re-evaluation on every parent column on the form, the guard on each
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
            var attributes = (root ? [COLUMN, SECURE_COLUMN] : [COLUMN])
                .map(function (column) { return formContext.getAttribute(column); })
                .filter(function (a) { return !!a; });
            if (attributes.length === 0) {
                return; // the column is not on this form: nothing to lock
            }
            // Idempotent: a second registration on the same form load (a maker adding the handler twice) wires nothing
            // again. Keyed on the attribute AND the record, so a form the platform reuses for another record still wires.
            var recordKey = formContext.data.entity.getId() || "";
            for (var w = 0; w < wired.length; w++) {
                if (wired[w].attribute === attributes[0] && wired[w].record === recordKey) {
                    return;
                }
            }
            wired.push({ attribute: attributes[0], record: recordKey });

            var state = {
                locked: false,
                locks: attributes.map(function (a) { return { attribute: a, lockedValue: null, disabledByUs: [] }; }),
                saved: {},
                savedPair: null,
                types: {}
            };

            var formValue = function (column) {
                var a = formContext.getAttribute(column);
                return a ? a.getValue() : undefined;
            };

            // The columns whose change re-evaluates: the lookups, and on a root the pair.
            var filingColumns = Object.keys(lookups).concat(root ? [PAIR_ID, PAIR_TYPE] : []);

            var parentDirty = function () {
                return filingColumns.some(function (column) {
                    var a = formContext.getAttribute(column);
                    return !!a && a.getIsDirty();
                });
            };

            var evaluate = function () {
                try {
                    var names = ns.parentNames(lookups, formValue, state.saved);
                    if (root) {
                        var pairName = pairParentName(formContext, state, evaluate);
                        if (pairName !== null && names.indexOf(pairName) < 0) {
                            names.push(pairName);
                        }
                    }
                    if (names.length > 0) {
                        lock(formContext, state, ns.notificationText(names,
                            formContext.ui.getFormType() === FORM_TYPE_CREATE || parentDirty(), root));
                    } else {
                        unlock(formContext, state);
                    }
                } catch (e) {
                    console.error(LOG, "evaluate failed", e);
                }
            };

            filingColumns.forEach(function (column) {
                var a = formContext.getAttribute(column);
                if (a) {
                    a.addOnChange(evaluate);
                }
            });

            // A bound PCF (the TrackingFieldTrio pill) changes the value without a control this script can disable.
            state.locks.forEach(function (entry) {
                entry.attribute.addOnChange(function () {
                    if (state.locked && entry.attribute.getValue() !== entry.lockedValue) {
                        entry.attribute.setValue(entry.lockedValue);
                        evaluate();
                    }
                });
            });

            var reload = function () {
                readSaved(formContext, table, lookups, root).then(function (saved) {
                    state.saved = saved.lookups;
                    state.savedPair = saved.pair;
                    evaluate();
                });
            };

            if (formContext.data.entity.addOnPostSave) {
                formContext.data.entity.addOnPostSave(function () {
                    state.locks.forEach(function (entry) { entry.lockedValue = entry.attribute.getValue(); });
                    reload();
                });
            }

            evaluate(); // at once, from the columns on the form
            reload();   // then with the saved columns the form does not carry
        } catch (e) {
            console.error(LOG, "onLoad failed", e);
        }
    };

    /**
     * The display name of the parent the polymorphic pair names, or null when the pair names none (no id, an id that is
     * not a GUID, no type, a type that is not a parent table, or a type not read yet / unreadable). A type not read yet is
     * read once (cached per type id for this form load) and the form re-evaluated when the answer arrives.
     */
    function pairParentName(formContext, state, evaluate) {
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
            return null; // no type, or the id is not a record identifier: the pair names no parent
        }

        var type = state.types[typeId];
        if (!type) {
            state.types[typeId] = { done: false, logicalName: null };
            readRecordType(typeId).then(function (logicalName) {
                state.types[typeId] = { done: true, logicalName: logicalName };
                evaluate();
            });
            return null; // decided when the type is read
        }
        if (!type.done || !ns.pairIsParent(recordId, type.logicalName)) {
            return null;
        }

        var nameAttribute = formContext.getAttribute(PAIR_NAME);
        var recordName = nameAttribute ? nameAttribute.getValue() : null;
        if (typeof recordName === "string" && recordName.trim()) {
            return recordName;
        }
        return typeName || "its parent record";
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
                    console.warn(LOG, "the regarding record type could not be read; the pair does not lock the form.", error);
                    return null;
                });
        } catch (error) {
            console.warn(LOG, "the regarding record type could not be read; the pair does not lock the form.", error);
            return Promise.resolve(null);
        }
    }

    function lock(formContext, state, text) {
        if (!state.locked) {
            state.locked = true;
            state.locks.forEach(function (entry) {
                entry.lockedValue = entry.attribute.getValue();
                entry.disabledByUs = [];
                entry.attribute.controls.forEach(function (control) {
                    if (!control.getDisabled()) {
                        control.setDisabled(true);
                        entry.disabledByUs.push(control);
                    }
                });
                entry.attribute.setSubmitMode("never");
            });
        }
        formContext.ui.setFormNotification(text, "INFO", NOTIFICATION_ID);
    }

    function unlock(formContext, state) {
        if (state.locked) {
            state.locked = false;
            state.locks.forEach(function (entry) {
                entry.disabledByUs.forEach(function (control) { control.setDisabled(false); });
                entry.disabledByUs = [];
                entry.attribute.setSubmitMode("dirty");
            });
        }
        formContext.ui.clearFormNotification(NOTIFICATION_ID);
    }

    /**
     * The saved filing the form does not carry: { lookups: column -> { name } for the saved parent lookups not on the
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
                            saved[column] = { name: row[key + FORMATTED] || "" };
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
                    return empty;
                });
        } catch (error) {
            console.warn(LOG, "the saved filing could not be read; only the columns on the form decide.", error);
            return Promise.resolve(empty);
        }
    }

    return ns;
})();
