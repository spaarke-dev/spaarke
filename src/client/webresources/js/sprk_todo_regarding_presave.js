/**
 * Smart To Do — Regarding Pre-Save Handler
 *
 * Companion form script for the RegardingResolver virtual PCF
 * (`Spaarke.Controls.RegardingResolver`) bound to the OOB MDA To Do main form
 * `eca59df4-1364-f111-ab0c-7ced8ddc4cc6`. Smart To Do R4 task R4-051 (D).
 *
 * # Purpose
 *
 * The RegardingResolver PCF writes the 5 polymorphic-regarding fields via
 * `Xrm.WebApi.updateRecord` through the shared
 * `PolymorphicResolverService.applyResolverFields` (ADR-024, FR-21). This call
 * succeeds when the host record already has a GUID (UPDATE mode). On a NEW
 * record (CREATE mode, form type === 1), the PCF cannot call `updateRecord`
 * because the row does not yet exist — the resolver payload it computes
 * returns from `applyRegardingSelection` but is not persisted.
 *
 * The bound `regardingRecordType` output property IS propagated to the form
 * natively via `notifyOutputChanged()`, so on CREATE the form already has the
 * lookup discriminator set. This handler bridges the gap for the OTHER FOUR
 * resolver fields (`sprk_regardingrecordid`, `sprk_regardingrecordname`,
 * `sprk_regardingrecordurl`, and the chosen `sprk_regarding<X>` lookup) so they
 * ride the form's INSERT transaction.
 *
 * Mirrors the AssociationResolver convention of calling
 * `Xrm.Page.getAttribute(fieldName).setValue(value)` to stage values into the
 * form's pending-attribute buffer (see
 * `src/client/pcf/AssociationResolver/handlers/FieldMappingHandler.ts`
 * `applyToForm`).
 *
 * # Contract with the RegardingResolver PCF
 *
 * On selection, the PCF SHOULD surface a pending payload on a stable global
 * seam so this handler can pick it up at OnSave time:
 *
 *   ```
 *   window.__sprk_regarding_pending__ = {
 *     hostEntity: "sprk_todo",
 *     entityType: "sprk_matter",
 *     entitySet: "sprk_matters",
 *     navProp: "sprk_RegardingMatter",   // navigation property name (PascalCase)
 *     recordId: "00000000-0000-0000-0000-000000000000",
 *     recordName: "Smith v. Jones",
 *     recordUrl: "https://contoso.crm.dynamics.com/main.aspx?..."   // optional
 *   };
 *   ```
 *
 * If the seam is not populated (older PCF, or user cleared selection),
 * this handler is a no-op (the form save proceeds; on UPDATE mode the PCF has
 * already written all 5 fields directly).
 *
 * # Form events to register
 *
 * 1. **OnLoad** — `Spaarke.SmartTodo.RegardingPreSave.onLoad`
 *    (registers the OnSave handler via `addOnSave`; pass execution context: Yes)
 * 2. **OnSave** is registered programmatically by `onLoad` — DO NOT also wire
 *    OnSave directly in the form designer.
 *
 * # Constraints (smart-todo-r4 spec)
 *
 * - FR-21: All resolver writes route through `applyResolverFields`. This
 *   handler does NOT reimplement mutual-exclusivity — for new records, the
 *   PCF has already prepared the @odata.bind payload via the shared service
 *   and surfaces it on the window seam. This script only TRANSCRIBES that
 *   payload onto form attributes.
 * - FR-23: All 5 fields persisted atomically. On CREATE, by staging via
 *   `setValue` BEFORE save, all 5 fields ride the INSERT transaction.
 * - NFR-03: No hardcoded environment URLs, app IDs, or GUIDs in this script.
 * - NFR-04: Form-designer changes propagate to the SmartTodo hybrid modal
 *   iframe with no R4 source change. This handler is solution-portable
 *   (web resource) so the iframe re-renders with the new behavior on the
 *   next form load.
 * - NFR-09: Deployed via the SmartTodoWebResources solution wrapper (see
 *   `projects/smart-todo-r4/notes/d-form-bind-instructions.md`).
 *
 * # Version
 *
 * v1.0.0 — initial implementation (smart-todo-r4 task R4-051, 2026-06-10)
 *
 * @namespace Spaarke.SmartTodo.RegardingPreSave
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = window.Spaarke || {};
Spaarke.SmartTodo = Spaarke.SmartTodo || {};
Spaarke.SmartTodo.RegardingPreSave = Spaarke.SmartTodo.RegardingPreSave || {};

(function (ns) {
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /** Version for diagnostic logging. */
    ns.VERSION = "1.0.0";

    /**
     * Resolver text/url fields written verbatim from the pending payload.
     * The lookup @odata.bind keys are dynamic per selected entity type.
     */
    var TEXT_FIELDS = ["sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl"];

    /** Global seam name shared with the RegardingResolver PCF. */
    var PENDING_GLOBAL = "__sprk_regarding_pending__";

    // -----------------------------------------------------------------------
    // OnLoad — register OnSave handler
    // -----------------------------------------------------------------------

    /**
     * Form OnLoad handler. Registers the OnSave bridge programmatically so the
     * form designer only needs to wire this one entry point.
     *
     * @param {object} executionContext - Form execution context (pass first param: Yes)
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            if (!formContext || !formContext.data || !formContext.data.entity) {
                console.warn("[SmartTodo.RegardingPreSave v" + ns.VERSION + "] onLoad — formContext unavailable, skipping");
                return;
            }
            formContext.data.entity.addOnSave(ns.onSave);
            console.log("[SmartTodo.RegardingPreSave v" + ns.VERSION + "] OnSave handler registered");
        } catch (err) {
            console.error("[SmartTodo.RegardingPreSave v" + ns.VERSION + "] onLoad error:", err);
        }
    };

    // -----------------------------------------------------------------------
    // OnSave — bridge the PCF's pending payload onto form attributes
    // -----------------------------------------------------------------------

    /**
     * Form OnSave handler. On a CREATE (formType === 1) save, reads the
     * RegardingResolver PCF's pending payload from
     * `window.__sprk_regarding_pending__` and stages all five resolver fields
     * onto the form via `getAttribute(fieldName).setValue(...)` so they ride
     * the INSERT transaction.
     *
     * No-op when:
     *   - formType is not CREATE (PCF already wrote via webApi.updateRecord)
     *   - pending payload is absent (no selection was made, or selection was
     *     made on a saved record where the PCF persisted directly)
     *
     * Never blocks the save: any failure logs to console and returns silently.
     *
     * @param {object} executionContext - Form execution context
     */
    ns.onSave = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            if (!formContext) {
                return;
            }

            // FormType 1 = Create. Other values: 2=Update, 3=Read-Only, 4=Disabled,
            // 6=Bulk Edit, 0=Undefined. Pre-save staging is only meaningful on Create.
            var formType = (formContext.ui && formContext.ui.getFormType) ? formContext.ui.getFormType() : null;
            if (formType !== 1) {
                return;
            }

            var pending = window[PENDING_GLOBAL];
            if (!pending || typeof pending !== "object") {
                // No pending payload — either no selection made in PCF, or this is
                // a saved-record update path where the PCF wrote via webApi directly.
                return;
            }

            // Defensive validation — refuse to stage incomplete payloads.
            if (!pending.entityType || !pending.recordId) {
                console.warn(
                    "[SmartTodo.RegardingPreSave v" + ns.VERSION + "] Pending payload missing required keys (entityType, recordId); skipping",
                    pending
                );
                return;
            }

            // 1. Stage the three text/url fields.
            for (var i = 0; i < TEXT_FIELDS.length; i++) {
                var fieldName = TEXT_FIELDS[i];
                var sourceKey = textKeyForField(fieldName);
                var value = pending[sourceKey];
                if (value === undefined || value === null) {
                    // Allow explicit null to clear, but skip undefined (key not present).
                    if (!(sourceKey in pending)) {
                        continue;
                    }
                }
                setAttributeIfPresent(formContext, fieldName, value === undefined ? null : value);
            }

            // 2. Stage the chosen sprk_regarding<entity> lookup.
            //    The lookup attribute name comes from the catalog entry surfaced
            //    by the PCF as `lookupAttribute` (preferred) or is derived from
            //    `entityType` by convention (`sprk_regarding<entitysuffix>`).
            var lookupAttr = pending.lookupAttribute || deriveLookupAttribute(pending.entityType);
            if (lookupAttr) {
                setLookupIfPresent(
                    formContext,
                    lookupAttr,
                    pending.recordId,
                    pending.recordName || "",
                    pending.entityType
                );
            } else {
                console.warn(
                    "[SmartTodo.RegardingPreSave v" + ns.VERSION + "] Could not derive lookup attribute for entityType",
                    pending.entityType
                );
            }

            // 3. The sprk_regardingrecordtype lookup is already set by the PCF
            //    via notifyOutputChanged() (bound output). No staging needed here.

            console.log(
                "[SmartTodo.RegardingPreSave v" + ns.VERSION + "] Staged " + (TEXT_FIELDS.length + 1) + " resolver fields for INSERT",
                { entityType: pending.entityType, recordId: pending.recordId }
            );

            // 4. Clear the pending global so a subsequent re-save does not re-apply.
            try {
                delete window[PENDING_GLOBAL];
            } catch (_) {
                window[PENDING_GLOBAL] = undefined;
            }
        } catch (err) {
            // NEVER block the save — log and let the form proceed.
            console.error("[SmartTodo.RegardingPreSave v" + ns.VERSION + "] onSave error:", err);
        }
    };

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /**
     * Map a destination attribute logical name to the corresponding key on the
     * pending payload object. Today the schema is 1:1 lowercased; kept as a
     * function so future renames stay isolated here.
     */
    function textKeyForField(fieldName) {
        switch (fieldName) {
            case "sprk_regardingrecordid": return "recordId";
            case "sprk_regardingrecordname": return "recordName";
            case "sprk_regardingrecordurl": return "recordUrl";
            default: return fieldName;
        }
    }

    /**
     * Derive the sprk_regarding<X> lookup attribute logical name from the
     * regarding target entity type. Mirrors the convention encoded in
     * `TODO_REGARDING_CATALOG` (lookupAttribute property). Used only as a
     * fallback when the PCF didn't surface `lookupAttribute` directly.
     *
     * Examples:
     *   sprk_matter         -> sprk_regardingmatter
     *   sprk_communication  -> sprk_regardingcommunication
     *   contact             -> sprk_regardingcontact
     */
    function deriveLookupAttribute(entityType) {
        if (!entityType || typeof entityType !== "string") {
            return null;
        }
        var t = entityType.toLowerCase();
        // Standard Spaarke prefix
        if (t.indexOf("sprk_") === 0) {
            return "sprk_regarding" + t.substring("sprk_".length);
        }
        // OOB entities (contact, etc.)
        return "sprk_regarding" + t;
    }

    /**
     * Set a string / text attribute if it exists on the form. Logs and skips
     * if not present (the field may be hidden on certain form variants).
     */
    function setAttributeIfPresent(formContext, fieldName, value) {
        try {
            var attr = formContext.getAttribute(fieldName);
            if (!attr) {
                console.warn(
                    "[SmartTodo.RegardingPreSave v" + ns.VERSION + "] Attribute not on form: " + fieldName
                );
                return;
            }
            attr.setValue(value);
        } catch (err) {
            console.warn(
                "[SmartTodo.RegardingPreSave v" + ns.VERSION + "] Failed to set " + fieldName + ":",
                err
            );
        }
    }

    /**
     * Set a polymorphic-style lookup attribute (single-entity lookup) if it
     * exists on the form.
     */
    function setLookupIfPresent(formContext, fieldName, recordId, recordName, entityType) {
        try {
            var attr = formContext.getAttribute(fieldName);
            if (!attr) {
                console.warn(
                    "[SmartTodo.RegardingPreSave v" + ns.VERSION + "] Lookup attribute not on form: " + fieldName
                );
                return;
            }
            var cleanId = String(recordId || "").replace(/[{}]/g, "");
            if (!cleanId) {
                attr.setValue(null);
                return;
            }
            attr.setValue([
                {
                    id: cleanId,
                    name: recordName || "",
                    entityType: entityType
                }
            ]);
        } catch (err) {
            console.warn(
                "[SmartTodo.RegardingPreSave v" + ns.VERSION + "] Failed to set lookup " + fieldName + ":",
                err
            );
        }
    }

    // -----------------------------------------------------------------------
    // Exports for test harnesses (no-op in MDA runtime)
    // -----------------------------------------------------------------------

    if (typeof module !== "undefined" && module.exports) {
        module.exports = {
            onLoad: ns.onLoad,
            onSave: ns.onSave,
            _internals: {
                textKeyForField: textKeyForField,
                deriveLookupAttribute: deriveLookupAttribute
            },
            VERSION: ns.VERSION
        };
    }
})(Spaarke.SmartTodo.RegardingPreSave);
