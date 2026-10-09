/**
 * Smart To Do — Regarding Record Number Hyperlink
 *
 * Companion form script for the sprk_todo main form (formid
 * eca59df4-1364-f111-ab0c-7ced8ddc4cc6). SRFR-036 (post-v1.3.1 owner
 * feedback follow-on).
 *
 * # Purpose
 *
 * On form OnLoad, transform the `sprk_regardingrecordnumber` field cell
 * into a clickable hyperlink that opens the target record identified by
 * `sprk_regardingrecordtype` (lookup to sprk_recordtype_ref carrying the
 * target's entityType via lookup metadata) + `sprk_regardingrecordid`
 * (GUID text field).
 *
 * The RegardingResolver PCF v1.3.1 (SRFR-034) already renders the record
 * number as an inline hyperlink in its Row-2 layout. This companion
 * webresource surfaces the SAME behavior on the OOB form field so makers
 * who prefer OOB-style field display (below the PCF) get the same click
 * affordance.
 *
 * # Fields consumed
 *
 * - `sprk_regardingrecordtype` (lookup) — provides `[0].name` = target
 *   entity logical name (e.g. "sprk_matter", "sprk_event").
 * - `sprk_regardingrecordid` (text) — GUID string of the target record.
 * - `sprk_regardingrecordnumber` (text) — display label for the hyperlink.
 *
 * All three are written by the RegardingResolver PCF via
 * `PolymorphicResolverService.applyResolverFields` (SRFR-020) or by
 * `sprk_todo_regarding_presave.js` on CREATE (SRFR-040).
 *
 * # Form events to register
 *
 * 1. **OnLoad** — `Spaarke.SmartTodo.RegardingRecordNumberHyperlink.onLoad`
 *    (pass execution context: Yes)
 *
 * # Defensive posture (BINDING)
 *
 * - NEVER throws — every failure path returns silently after a
 *   `console.warn` with the version tag.
 * - Bounded polling — waits at most `maxAttempts * intervalMs` = 20 * 200ms
 *   = 4s for the field DOM to appear. After that: give up.
 * - If any of the three form attributes is missing OR the DOM selector
 *   cannot locate the field cell OR the display element inside the cell
 *   cannot be found: silent-fail. The form ALWAYS loads regardless.
 * - Model-Driven-App DOM structure evolves. If the assumed
 *   `[data-lp-id*="sprk_regardingrecordnumber"]` selector no longer
 *   matches, a subsequent Dataverse update may require adjusting the
 *   selector. The console.warn trail helps diagnose.
 *
 * # Version
 *
 * v1.0.0 — SRFR-036: initial implementation (2026-07-03)
 *
 * @namespace Spaarke.SmartTodo.RegardingRecordNumberHyperlink
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = window.Spaarke || {};
Spaarke.SmartTodo = Spaarke.SmartTodo || {};
Spaarke.SmartTodo.RegardingRecordNumberHyperlink = Spaarke.SmartTodo.RegardingRecordNumberHyperlink || {};

(function (ns) {
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /** Version for diagnostic logging. */
    ns.VERSION = "1.0.0";

    /** Form attribute logical names. */
    var ATTR_RECORD_TYPE = "sprk_regardingrecordtype";
    var ATTR_RECORD_ID = "sprk_regardingrecordid";
    var ATTR_RECORD_NUMBER = "sprk_regardingrecordnumber";

    /** Bounded polling parameters. */
    var MAX_ATTEMPTS = 20;
    var INTERVAL_MS = 200;

    // -----------------------------------------------------------------------
    // OnLoad — transform the record-number field into a hyperlink
    // -----------------------------------------------------------------------

    /**
     * Form OnLoad handler. Reads the three regarding-* attributes from the
     * form and — once the DOM has rendered the sprk_regardingrecordnumber
     * field cell — replaces its display element with an anchor pointing at
     * the target record via `Xrm.Navigation.navigateTo`.
     *
     * @param {object} executionContext - Form execution context (pass first param: Yes)
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext && executionContext.getFormContext
                ? executionContext.getFormContext()
                : null;
            if (!formContext) {
                console.warn(
                    "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION + "] onLoad — formContext unavailable, skipping"
                );
                return;
            }

            var attempts = 0;
            var intervalHandle = null;

            var tryTransform = function () {
                attempts++;
                try {
                    // 1. Read the three source attributes.
                    var typeAttr = formContext.getAttribute(ATTR_RECORD_TYPE);
                    var idAttr = formContext.getAttribute(ATTR_RECORD_ID);
                    var numberAttr = formContext.getAttribute(ATTR_RECORD_NUMBER);

                    if (!typeAttr || !idAttr || !numberAttr) {
                        // Attributes not on the form — nothing to do.
                        if (attempts >= MAX_ATTEMPTS) {
                            console.warn(
                                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                                "] Required attributes not on form, giving up"
                            );
                            clearInterval(intervalHandle);
                        }
                        return;
                    }

                    var typeValue = typeAttr.getValue();
                    var recordId = idAttr.getValue();
                    var recordNumber = numberAttr.getValue();

                    if (!typeValue || !typeValue[0] || !typeValue[0].name || !recordId || !recordNumber) {
                        // Values not populated on this record — leave the plain field alone.
                        // Silent-fail (this is the empty-target case, not an error).
                        clearInterval(intervalHandle);
                        return;
                    }

                    // The lookup entry's `name` here is the sprk_recordtype_ref
                    // display name (NOT the linked entity logical name). We need
                    // the linked entity logical name from a different property.
                    //
                    // MDA lookup values expose `entityType` = the entity of the
                    // LOOKUP TARGET (in our case sprk_recordtype_ref). To get the
                    // linked polymorphic target entity we must query
                    // sprk_recordtype_ref.sprk_recordlogicalname.
                    //
                    // OPTIMIZATION: if the caller (RegardingResolver PCF) has
                    // stashed the entityType on the pending payload (SRFR-032),
                    // we can use it directly and skip the query. Otherwise
                    // fall back to Xrm.WebApi retrieveRecord.
                    resolveTargetEntityName(formContext, typeValue[0], function (targetEntityName) {
                        if (!targetEntityName) {
                            console.warn(
                                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                                "] Could not resolve target entity name; leaving field unmodified"
                            );
                            clearInterval(intervalHandle);
                            return;
                        }

                        // 2. Find the field cell in the rendered DOM.
                        var cell = findFieldCell(ATTR_RECORD_NUMBER);
                        if (!cell) {
                            if (attempts >= MAX_ATTEMPTS) {
                                console.warn(
                                    "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                                    "] Field cell not found after " + MAX_ATTEMPTS + " attempts, giving up"
                                );
                                clearInterval(intervalHandle);
                            }
                            return;
                        }

                        // 3. Find the display element and transform it.
                        var replaced = replaceWithHyperlink(cell, recordNumber, targetEntityName, recordId);
                        if (replaced) {
                            console.log(
                                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                                "] Hyperlink applied for " + targetEntityName + " " + recordId
                            );
                        } else if (attempts >= MAX_ATTEMPTS) {
                            console.warn(
                                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                                "] Display element not found inside cell after " + MAX_ATTEMPTS + " attempts, giving up"
                            );
                        }

                        clearInterval(intervalHandle);
                    });
                } catch (innerErr) {
                    console.warn(
                        "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION + "] tryTransform error:",
                        innerErr
                    );
                    if (attempts >= MAX_ATTEMPTS) {
                        clearInterval(intervalHandle);
                    }
                }
            };

            intervalHandle = setInterval(tryTransform, INTERVAL_MS);
            // First attempt immediately to avoid the initial 200ms wait.
            tryTransform();
        } catch (err) {
            console.error(
                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION + "] onLoad error:",
                err
            );
        }
    };

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /**
     * Resolve the target polymorphic entity logical name from the
     * sprk_recordtype_ref lookup value on `sprk_regardingrecordtype`.
     *
     * Strategy:
     *   1. If Xrm.WebApi is available: retrieve the sprk_recordtype_ref
     *      record and read `sprk_recordlogicalname`.
     *   2. Otherwise: fall back to the lookup's own `entityType` — this is
     *      the sprk_recordtype_ref entity name (NOT the target polymorphic
     *      entity), so this fallback is diagnostic-only.
     *
     * @param {object} formContext
     * @param {object} lookupEntry - single-entry EntityReference from getValue()
     * @param {function} callback - callback(targetEntityName)
     */
    function resolveTargetEntityName(formContext, lookupEntry, callback) {
        try {
            var refId = String(lookupEntry.id || "").replace(/[{}]/g, "");
            var refEntity = lookupEntry.entityType || "sprk_recordtype_ref";

            if (!refId) {
                callback(null);
                return;
            }

            if (typeof Xrm === "undefined" || !Xrm.WebApi || !Xrm.WebApi.retrieveRecord) {
                console.warn(
                    "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                    "] Xrm.WebApi unavailable; cannot resolve target entity name from " + refEntity
                );
                callback(null);
                return;
            }

            // Fetch only the field we need for perf.
            Xrm.WebApi.retrieveRecord(refEntity, refId, "?$select=sprk_recordlogicalname").then(
                function (record) {
                    var targetName = record && record.sprk_recordlogicalname
                        ? String(record.sprk_recordlogicalname).toLowerCase()
                        : null;
                    callback(targetName);
                },
                function (err) {
                    console.warn(
                        "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                        "] retrieveRecord failed for " + refEntity + " " + refId + ":",
                        err
                    );
                    callback(null);
                }
            );
        } catch (err) {
            console.warn(
                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                "] resolveTargetEntityName error:",
                err
            );
            callback(null);
        }
    }

    /**
     * Locate the rendered field cell for the given attribute logical name.
     * The MDA form DOM decorates field cells with `data-lp-id` containing
     * the field's logical name; we accept any element whose data-lp-id
     * contains the field name as a substring.
     *
     * Fallback: query by `data-id` / `[aria-label]` patterns.
     *
     * @param {string} fieldName - attribute logical name (e.g. sprk_regardingrecordnumber)
     * @returns {HTMLElement | null}
     */
    function findFieldCell(fieldName) {
        try {
            // Primary: data-lp-id (Dataverse Unified Interface convention).
            var byLpId = document.querySelector('[data-lp-id*="' + fieldName + '"]');
            if (byLpId) {
                return byLpId;
            }
            // Fallback: data-id.
            var byDataId = document.querySelector('[data-id="' + fieldName + '"]') ||
                           document.querySelector('[data-id="' + fieldName + '.fieldControl"]');
            if (byDataId) {
                return byDataId;
            }
            return null;
        } catch (err) {
            console.warn(
                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                "] findFieldCell error:",
                err
            );
            return null;
        }
    }

    /**
     * Replace the display element inside the given field cell with an
     * anchor that opens the target record via Xrm.Navigation.navigateTo.
     *
     * @param {HTMLElement} cell
     * @param {string} recordNumber
     * @param {string} entityName
     * @param {string} recordId
     * @returns {boolean} true if the replacement succeeded, false otherwise
     */
    function replaceWithHyperlink(cell, recordNumber, entityName, recordId) {
        try {
            // Find the visible display element. Order of preference:
            //   1. [role="textbox"] (typical read-only text)
            //   2. input
            //   3. span (fallback for pure-display)
            var display = cell.querySelector('[role="textbox"]') ||
                          cell.querySelector('input') ||
                          cell.querySelector('.ms-TextField-fieldGroup') ||
                          cell.querySelector('span');
            if (!display) {
                return false;
            }

            // Avoid duplicate replacement if already applied (e.g. save + reload).
            if (display.parentNode && display.parentNode.querySelector('[data-sprk-regarding-hyperlink="1"]')) {
                return true;
            }

            var link = document.createElement('a');
            link.href = '#';
            link.textContent = recordNumber;
            link.setAttribute('data-sprk-regarding-hyperlink', '1');
            link.setAttribute('role', 'link');
            link.setAttribute('aria-label', 'Open ' + entityName + ' record ' + recordNumber);
            link.style.color = 'var(--colorBrandForegroundLink, #0f6cbd)';
            link.style.textDecoration = 'underline';
            link.style.cursor = 'pointer';
            link.style.padding = '4px 0';
            link.style.display = 'inline-block';
            link.style.fontWeight = '400';

            var cleanId = String(recordId || "").replace(/[{}]/g, "");

            link.onclick = function (evt) {
                evt.preventDefault();
                try {
                    if (typeof Xrm !== "undefined" && Xrm.Navigation && Xrm.Navigation.navigateTo) {
                        Xrm.Navigation.navigateTo(
                            {
                                pageType: "entityrecord",
                                entityName: entityName,
                                entityId: cleanId
                            },
                            {
                                target: 2,
                                width: { value: 80, unit: "%" },
                                height: { value: 80, unit: "%" }
                            }
                        ).then(
                            function () { /* navigated ok */ },
                            function (navErr) {
                                console.warn(
                                    "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                                    "] navigateTo failed:",
                                    navErr
                                );
                            }
                        );
                    } else {
                        console.warn(
                            "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                            "] Xrm.Navigation.navigateTo unavailable; click ignored"
                        );
                    }
                } catch (clickErr) {
                    console.warn(
                        "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                        "] onclick error:",
                        clickErr
                    );
                }
                return false;
            };

            // Hide the original display element (do NOT remove — preserves any
            // form-scripting hooks that reference the input) and insert the anchor
            // as a sibling in front of it.
            display.style.display = 'none';
            if (display.parentNode) {
                display.parentNode.insertBefore(link, display);
                return true;
            }
            return false;
        } catch (err) {
            console.warn(
                "[SmartTodo.RegardingRecordNumberHyperlink v" + ns.VERSION +
                "] replaceWithHyperlink error:",
                err
            );
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Exports for test harnesses (no-op in MDA runtime)
    // -----------------------------------------------------------------------

    if (typeof module !== "undefined" && module.exports) {
        module.exports = {
            onLoad: ns.onLoad,
            VERSION: ns.VERSION
        };
    }
})(Spaarke.SmartTodo.RegardingRecordNumberHyperlink);
