/**
 * Inherited Access Permission - form library for To Do, Event, Communication and Document
 * (unified-access-control-r2 task 173, GitHub #1423; owner rounds 81 and 84).
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
 *
 * This script decides nothing about the value and writes nothing (ADR-002 WP-2: the server owns the invariant; the form
 * only locks and labels). It only answers "does this record have a parent?", from the SAME lookups the server walks:
 * PARENT_LOOKUPS below is the server's ParentLineage.ChildFiling for these four tables, pinned literally by
 * ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap (a lookup that is not a parent there - a contact, an
 * organization, a service request, a document's own current version - does not lock the field here either).
 *
 * Where the parents are read:
 *   - a lookup ON the form: its current value (an unsaved pick or clear re-evaluates at once, through OnChange);
 *   - a lookup NOT on the form: the saved value, read once with Xrm.WebApi.retrieveRecord (and again after each save).
 *     A read that fails leaves only the lookups on the form deciding (logged to the console).
 *
 * "Locked" means: every control bound to sprk_accesspermission that this script found enabled is disabled; the column is
 * not submitted (setSubmitMode "never"); and a change that still arrives (a PCF bound to the column - the
 * TrackingFieldTrio pill on the To Do and Event main forms) is put back to the value the field had when it was locked.
 * Unlocking re-enables only the controls this script disabled, so a read-only form stays read-only.
 *
 * ES5, no build step, never throws into the form.
 */
// A property of window, not a top-level var: the form resolves "Spaarke.AccessPermissionInherited.onLoad" from window,
// and a global var binding could not be replaced by a later load of the library on the same page.
window.Spaarke = window.Spaarke || {};

window.Spaarke.AccessPermissionInherited = (function () {
    "use strict";

    var ns = {};
    ns.VERSION = "1.0.0";

    var LOG = "[Spaarke.AccessPermissionInherited v" + ns.VERSION + "]";
    var COLUMN = "sprk_accesspermission";
    var NOTIFICATION_ID = "sprk_accesspermission_inherited";
    var FORM_TYPE_CREATE = 1;
    var FORMATTED = "@OData.Community.Display.V1.FormattedValue";

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

    /**
     * The notification text (owner round 81: 'Access permission is inherited from {parent name}').
     * @param {string[]} names - the parents' display names
     * @param {boolean} pending - the parent was picked or changed on this form and is not saved yet
     * @returns {string}
     */
    ns.notificationText = function (names, pending) {
        var who = names.length > 0 ? names.join(", ") : "its parent record";
        return pending
            ? "Access permission is inherited from " + who + ". It is set when the record is saved."
            : "Access permission is inherited from " + who + ".";
    };

    /**
     * The parents a record names: each parent lookup's value from the form when the form carries it, otherwise the saved
     * value. Pure (no Xrm), so it can be reasoned about on its own.
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
     * OnLoad (pass execution context). Registers the re-evaluation on every parent lookup on the form, the guard on the
     * column itself, and the re-read after a save.
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            var table = formContext.data.entity.getEntityName();
            var lookups = ns.PARENT_LOOKUPS[table];
            var attribute = formContext.getAttribute(COLUMN);
            if (!lookups || !attribute) {
                return; // not one of the four tables, or the column is not on this form: nothing to lock
            }

            var state = { locked: false, lockedValue: null, disabledByUs: [], saved: {} };

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
                        lock(formContext, attribute, state, ns.notificationText(names,
                            formContext.ui.getFormType() === FORM_TYPE_CREATE || parentDirty()));
                    } else {
                        unlock(formContext, attribute, state);
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

            // A bound PCF (the TrackingFieldTrio pill) changes the value without a control this script can disable.
            attribute.addOnChange(function () {
                if (state.locked && attribute.getValue() !== state.lockedValue) {
                    attribute.setValue(state.lockedValue);
                    evaluate();
                }
            });

            var reload = function () {
                readSaved(formContext, table, lookups).then(function (saved) {
                    state.saved = saved;
                    evaluate();
                });
            };

            if (formContext.data.entity.addOnPostSave) {
                formContext.data.entity.addOnPostSave(function () {
                    state.lockedValue = attribute.getValue();
                    reload();
                });
            }

            evaluate(); // at once, from the lookups on the form
            reload();   // then with the saved lookups the form does not carry
        } catch (e) {
            console.error(LOG, "onLoad failed", e);
        }
    };

    function lock(formContext, attribute, state, text) {
        if (!state.locked) {
            state.locked = true;
            state.lockedValue = attribute.getValue();
            state.disabledByUs = [];
            attribute.controls.forEach(function (control) {
                if (!control.getDisabled()) {
                    control.setDisabled(true);
                    state.disabledByUs.push(control);
                }
            });
            attribute.setSubmitMode("never");
        }
        formContext.ui.setFormNotification(text, "INFO", NOTIFICATION_ID);
    }

    function unlock(formContext, attribute, state) {
        if (state.locked) {
            state.locked = false;
            state.disabledByUs.forEach(function (control) { control.setDisabled(false); });
            state.disabledByUs = [];
            attribute.setSubmitMode("dirty");
        }
        formContext.ui.clearFormNotification(NOTIFICATION_ID);
    }

    /**
     * The saved parent lookups the form does not carry, as column -> { name }. Resolves {} for an unsaved record, when
     * every parent lookup is on the form, or when the read fails (logged).
     */
    function readSaved(formContext, table, lookups) {
        var missing = Object.keys(lookups).filter(function (column) { return !formContext.getAttribute(column); });
        var id = (formContext.data.entity.getId() || "").replace(/[{}]/g, "");
        if (formContext.ui.getFormType() === FORM_TYPE_CREATE || !id || missing.length === 0) {
            return Promise.resolve({});
        }

        var select = missing.map(function (column) { return "_" + column + "_value"; }).join(",");
        return Promise.resolve(Xrm.WebApi.retrieveRecord(table, id, "?$select=" + select))
            .then(function (row) {
                var saved = {};
                missing.forEach(function (column) {
                    var key = "_" + column + "_value";
                    if (row && row[key]) {
                        saved[column] = { name: row[key + FORMATTED] || "" };
                    }
                });
                return saved;
            })
            .catch(function (error) {
                console.warn(LOG, "the saved parent lookups could not be read; only the lookups on the form decide.", error);
                return {};
            });
    }

    return ns;
})();
