/**
 * Field Mapping Push — Ribbon Webresource
 *
 * Provides the two ribbon-invoked functions used by the "Push Updates to
 * Related Records" ribbon button on parent forms (Matter, Project, Invoice, and
 * any other source-entity forms with an active `sprk_fieldmappingprofile`).
 *
 * Wired by ribbon `CustomAction` per spec §FR-B3-01 (canonical reference:
 * `infrastructure/dataverse/ribbon/CommunicationRibbons/Entities/sprk_communication/RibbonDiff.xml`
 * — the Communication "Send" button). Ribbon binding is delivered by
 * companion task SRFR-062.
 *
 *   <EnableRule>
 *     <CustomRule Library="$webresource:sprk_fieldmapping_push"
 *                 FunctionName="Spaarke.FieldMapping.Push.hasSourceProfile" />
 *   </EnableRule>
 *   <Actions>
 *     <JavaScriptFunction Library="$webresource:sprk_fieldmapping_push"
 *                         FunctionName="Spaarke.FieldMapping.Push.pushUpdates">
 *       <CrmParameter Value="PrimaryControl" />
 *     </JavaScriptFunction>
 *   </Actions>
 *
 * # Contract
 *
 * ## `hasSourceProfile(primaryControl)` — visibility rule
 *
 * Returns `true` iff any active `sprk_fieldmappingprofile` has the current
 * form's entity as its SOURCE recordtype. Uses the D-1 two-step lookup pattern
 * (spec Appendix A §A.5 revised):
 *   1. Query `sprk_recordtype_ref` by `sprk_recordlogicalname` to get the
 *      recordtype-ref GUID.
 *   2. Query `sprk_fieldmappingprofile` by `_sprk_sourcerecordtype_value` and
 *      `statecode eq 0`.
 *
 * Result is cached per (entity, formId) in `sessionStorage` so the second and
 * subsequent EnableRule invocations short-circuit (a Dataverse EnableRule
 * cannot safely block on an async query — the ribbon calls it repeatedly).
 * On the first call for a given cache key the function returns the promise
 * resolving to a boolean; Dataverse coerces a pending promise to `false`
 * (button hidden) but the cache is warm for the next invocation. This is the
 * documented pattern for async EnableRules.
 *
 * ## `pushUpdates(primaryControl)` — action (opens the Family view)
 *
 * Opens the `sprk_recordfamily` code page as a `navigateTo` dialog
 * (`pageType: "webresource"`, `target: 2`) with the data
 * `entityName=<logical name>&id=<record id>`; the page hosts `RecordFamilyModal`
 * for the current record (field-mapping-server-write-path-r1 task 075, spec
 * FR-35). Nothing is pushed from this script: the person reviews the
 * record's Family in the modal, and pushing happens only from the modal's
 * actions (the action panel, task 040). Unsaved form edits are saved first (a
 * failed save shows the platform error and opens nothing). The form is
 * refreshed when the dialog closes, as the wizard dialogs do
 * (`sprk_wizard_commands.js` `openWizardDialog`).
 *
 * Nothing in this script calls the BFF or handles a token: the push
 * (preview, confirm, apply) runs inside the Family view, which is a code page
 * authenticated through `@spaarke/auth` (ADR-028). The earlier direct-push flow
 * (a confirm dialog, one `POST /api/v1/field-mappings/push` per target profile
 * with an MSAL token acquired here, and a cached target list) was deleted in
 * v1.3.0 with task 040, which ships together with 075 so that Push Updates is
 * never live without a working Push action.
 *
 * # Constraints (spec R1)
 *
 * - FR-B3-01 — visibility rule returns boolean; non-blocking via
 *   sessionStorage cache.
 * - FR-35 — `pushUpdates` opens the Family view and pushes nothing itself.
 * - Plain JS — NO npm imports, NO ES modules, NO bundler, NO `@spaarke/auth`.
 * - ADR-006 — webresource is an approved surface for ribbon-adjacent host
 *   scripting. The Family view itself is a Code Page (`sprk_recordfamily`),
 *   launched from here with `navigateTo` (ADR-050: a ribbon script has no
 *   React host).
 *
 * # Version
 *
 * v1.0.0 — initial implementation (SRFR-061, 2026-07-02)
 * v1.1.0 — MSAL Bearer token auth for BFF /push (SRFR-053, 2026-07-06).
 * v1.2.0 — `pushUpdates` opens the Family view (`sprk_recordfamily` dialog)
 *          instead of pushing directly (task 075, FR-35). `hasSourceProfile`
 *          unchanged.
 * v1.3.0 — the direct-push helpers, the MSAL token chain, the BFF base-URL
 *          lookup and the targets-list cache are deleted (task 040).
 *          `hasSourceProfile` and `pushUpdates` behave as in v1.2.0.
 *
 * @namespace Spaarke.FieldMapping.Push
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = window.Spaarke || {};
Spaarke.FieldMapping = Spaarke.FieldMapping || {};
Spaarke.FieldMapping.Push = Spaarke.FieldMapping.Push || {};

(function (ns) {
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /** Version for diagnostic logging. */
    ns.VERSION = "1.3.0";

    /** Cache-key prefix for the visibility-check sessionStorage entries. */
    var CACHE_KEY_PREFIX = "sprk_fieldmapping_hasSource_";

    /** Web resource name of the Family code page that `pushUpdates` opens (task 075). */
    var FAMILY_PAGE_WEBRESOURCE = "sprk_recordfamily";

    /** Dialog options for the Family view (modal dialog, same chrome as the wizard dialogs). */
    var FAMILY_DIALOG_OPTIONS = {
        target: 2,
        width: { value: 70, unit: "%" },
        height: { value: 80, unit: "%" },
        title: "Family"
    };

    // -----------------------------------------------------------------------
    // Public — hasSourceProfile (ribbon EnableRule)
    // -----------------------------------------------------------------------

    /**
     * Ribbon EnableRule: returns `true` iff any active
     * `sprk_fieldmappingprofile` has the current entity as its SOURCE.
     *
     * Non-blocking pattern: sessionStorage cache is checked FIRST. On cache
     * miss, the async query is kicked off and the promise is returned; the
     * ribbon will treat a pending promise as `false` (button hidden) on that
     * invocation, but the cache is populated by the time the ribbon
     * re-evaluates.
     *
     * @param {object} primaryControl - Form execution context
     * @returns {boolean|Promise<boolean>}
     */
    ns.hasSourceProfile = function (primaryControl) {
        try {
            if (!primaryControl || !primaryControl.data || !primaryControl.data.entity) {
                return false;
            }
            var entityName = primaryControl.data.entity.getEntityName();
            var formId = primaryControl.data.entity.getId() || "unsaved";
            if (!entityName) {
                return false;
            }
            var cacheKey = CACHE_KEY_PREFIX + entityName + "_" + formId;
            // Cache HIT — synchronous return.
            var cached = safeSessionGet(cacheKey);
            if (cached === "true") {
                return true;
            }
            if (cached === "false") {
                return false;
            }
            // Cache MISS — query async, populate cache, return promise.
            return queryHasSourceProfile(entityName, cacheKey);
        } catch (err) {
            console.error("[FieldMapping.Push v" + ns.VERSION + "] hasSourceProfile error:", err);
            return false;
        }
    };

    /**
     * Async query implementing the D-1 two-step lookup pattern (spec §A.5
     * revised): entity name → recordtype-ref GUID → profiles by source
     * recordtype. Populates sessionStorage on success.
     *
     * @param {string} entityName
     * @param {string} cacheKey
     * @returns {Promise<boolean>}
     */
    function queryHasSourceProfile(entityName, cacheKey) {
        return resolveRecordTypeRefForEntity(entityName).then(function (sourceRtId) {
            if (!sourceRtId) {
                // Catalog gap — treat as no profiles (button hidden).
                safeSessionSet(cacheKey, "false");
                return false;
            }
            var profilesQuery =
                "?$filter=_sprk_sourcerecordtype_value eq " + sourceRtId +
                " and statecode eq 0" +
                "&$select=sprk_fieldmappingprofileid";
            return Xrm.WebApi.retrieveMultipleRecords("sprk_fieldmappingprofile", profilesQuery)
                .then(function (result) {
                    var entities = (result && result.entities) || [];
                    var has = entities.length > 0;
                    safeSessionSet(cacheKey, has ? "true" : "false");
                    return has;
                })
                .catch(function (err) {
                    console.warn(
                        "[FieldMapping.Push v" + ns.VERSION + "] profiles query failed:",
                        err
                    );
                    // Do NOT cache errors — allow retry on next EnableRule invocation.
                    return false;
                });
        }).catch(function (err) {
            console.warn(
                "[FieldMapping.Push v" + ns.VERSION + "] recordtype_ref query failed:",
                err
            );
            return false;
        });
    }

    // -----------------------------------------------------------------------
    // Public — pushUpdates (ribbon Action)
    // -----------------------------------------------------------------------

    /**
     * Ribbon Action: open the Family view (a code-page dialog hosting
     * RecordFamilyModal) for the current record. Pushes nothing (FR-35).
     *
     * @param {object} primaryControl - Form execution context
     */
    ns.pushUpdates = function (primaryControl) {
        try {
            if (!primaryControl || !primaryControl.data || !primaryControl.data.entity) {
                Xrm.Navigation.openAlertDialog({
                    text: "Form context is not available. Save the record and try again."
                });
                return;
            }
            var entityName = primaryControl.data.entity.getEntityName();
            var recordIdRaw = primaryControl.data.entity.getId();
            if (!entityName || !recordIdRaw) {
                Xrm.Navigation.openAlertDialog({
                    text: "Record must be saved before opening its family."
                });
                return;
            }
            var recordId = String(recordIdRaw).replace(/[{}]/g, "");

            // Refresh the form whenever the dialog closes (confirmed, cancelled or errored),
            // as sprk_wizard_commands.js openWizardDialog does.
            var refreshForm = function () {
                if (primaryControl && primaryControl.data) {
                    primaryControl.data.refresh();
                }
            };

            var openFamily = function () {
                try {
                    Xrm.Navigation.navigateTo(
                        {
                            pageType: "webresource",
                            webresourceName: FAMILY_PAGE_WEBRESOURCE,
                            data: "entityName=" + encodeURIComponent(entityName) +
                                "&id=" + encodeURIComponent(recordId)
                        },
                        FAMILY_DIALOG_OPTIONS
                    ).then(refreshForm, refreshForm);
                } catch (err) {
                    console.error("[FieldMapping.Push v" + ns.VERSION + "] open family error:", err);
                    Xrm.Navigation.openAlertDialog({
                        text: "Unable to open the family view. See console for details."
                    });
                }
            };

            // The Family view reads the SAVED record from the server, so unsaved edits are saved
            // first (the same way the form's own Save does). A failed save is shown with the
            // platform error and the Family view is NOT opened.
            var entity = primaryControl.data.entity;
            var isDirty = typeof entity.getIsDirty === "function" && entity.getIsDirty() === true;
            if (isDirty && typeof primaryControl.data.save === "function") {
                primaryControl.data.save().then(openFamily, function (saveErr) {
                    console.error("[FieldMapping.Push v" + ns.VERSION + "] save before family failed:", saveErr);
                    Xrm.Navigation.openErrorDialog({
                        message: (saveErr && saveErr.message) ||
                            "The record could not be saved, so its family was not opened."
                    });
                });
                return;
            }
            openFamily();
        } catch (err) {
            console.error("[FieldMapping.Push v" + ns.VERSION + "] pushUpdates error:", err);
            Xrm.Navigation.openAlertDialog({
                text: "Unable to open the family view. See console for details."
            });
        }
    };

    // -----------------------------------------------------------------------
    // Helpers — Dataverse lookups
    // -----------------------------------------------------------------------

    /**
     * Step 1 of the D-1 lookup: entity logical name → recordtype-ref GUID.
     * Returns `null` on miss.
     */
    function resolveRecordTypeRefForEntity(entityName) {
        var q =
            "?$filter=sprk_recordlogicalname eq '" + escapeODataString(entityName) + "'" +
            "&$select=sprk_recordtype_refid";
        return Xrm.WebApi.retrieveMultipleRecords("sprk_recordtype_ref", q)
            .then(function (r) {
                var ents = (r && r.entities) || [];
                if (!ents.length) return null;
                return ents[0].sprk_recordtype_refid;
            });
    }

    // -----------------------------------------------------------------------
    // Helpers — utilities
    // -----------------------------------------------------------------------

    /**
     * Escape single quotes in an OData string literal. Dataverse OData does
     * not accept backslash-escapes; single-quote is escaped by doubling.
     */
    function escapeODataString(value) {
        if (value === null || value === undefined) return "";
        return String(value).replace(/'/g, "''");
    }

    /**
     * Safe sessionStorage read. Returns `null` on any error (e.g., quota,
     * disabled storage). Never throws.
     */
    function safeSessionGet(key) {
        try {
            return window.sessionStorage.getItem(key);
        } catch (_) {
            return null;
        }
    }

    /**
     * Safe sessionStorage write. Silently no-ops on any error.
     */
    function safeSessionSet(key, value) {
        try {
            window.sessionStorage.setItem(key, value);
        } catch (_) {
            /* no-op */
        }
    }

    // -----------------------------------------------------------------------
    // Exports for test harnesses (no-op in MDA runtime)
    // -----------------------------------------------------------------------

    if (typeof module !== "undefined" && module.exports) {
        module.exports = {
            hasSourceProfile: ns.hasSourceProfile,
            pushUpdates: ns.pushUpdates,
            _internals: {
                escapeODataString: escapeODataString
            },
            VERSION: ns.VERSION
        };
    }
})(Spaarke.FieldMapping.Push);
