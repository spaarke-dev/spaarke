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
 * ## `pushUpdates(primaryControl)` — action
 *
 * 1. Opens a confirm dialog per FR-B3-02.
 * 2. On confirm, re-queries profiles for the current entity and resolves each
 *    target entity's logical name via a second `sprk_recordtype_ref` lookup
 *    (using `_sprk_targetrecordtype_value`).
 * 3. Sequentially calls the BFF `POST /api/v1/field-mappings/push` endpoint
 *    for EACH target (per FR-B3-03: multi-target = sequential + aggregated,
 *    no picker).
 * 4. Aggregates `updated`, `skipped`, `errors`, `childCount` across targets.
 * 5. On any per-target over-limit response (BFF 400 with `title: "Limit
 *    Exceeded"` in the ProblemDetails payload), short-circuits and shows the
 *    exact FR-B3-04 wording (verbatim; grep-verifiable).
 * 6. On completion, shows a summary toast per FR-B3-03 acceptance.
 *
 * # Auth
 *
 * BFF `/push` requires a Bearer JWT (user-context OBO). Plain JS webresources
 * cannot consume the `@spaarke/auth` TypeScript package, so this webresource
 * mirrors the MSAL-browser CDN pattern used by the canonical
 * `sprk_communication_send.js` (Communication Send ribbon on
 * sprk_communication entity):
 *
 *   1. Load `msal-browser` from the Microsoft CDN at first use.
 *   2. Resolve `sprk_MsalClientId`, `sprk_BffApiAppId`, `sprk_TenantId` from
 *      Dataverse environment variables; derive `redirectUri` from
 *      `Xrm.Utility.getGlobalContext().getClientUrl()`.
 *   3. `PublicClientApplication.acquireTokenSilent` → `ssoSilent` →
 *      `acquireTokenPopup` fallback chain to acquire the BFF scope
 *      `api://{bffAppId}/user_impersonation`.
 *   4. Attach the resulting access token as
 *      `Authorization: Bearer <token>` on the `POST /field-mappings/push`
 *      request.
 *
 * ADR-028 Path A (project-scoped exception): using `msal-browser` directly
 * (instead of `@spaarke/auth`) is the established pattern for vanilla JS
 * ribbon webresources — same rationale as `sprk_communication_send.js`,
 * `sprk_emailactions.js`, and `sprk_DocumentOperations.js`.
 *
 * The previous cookie-credentials pattern (`credentials: "include"` without a
 * Bearer token) was silently broken since v1.0.0 — the BFF rejects it with
 * 401 because it requires JWTs. Fixed in v1.1.0 (SRFR-053, 2026-07-06).
 *
 * The BFF API base URL is read from the `sprk_BffApiBaseUrl` Dataverse
 * environment variable (canonical pattern; see `sprk_analysis_commands.js`
 * `getEnvironmentVariable()`). A dev fallback is provided.
 *
 * # Constraints (spec R1)
 *
 * - FR-B3-01 — visibility rule returns boolean; non-blocking via
 *   sessionStorage cache.
 * - FR-B3-02 — confirm dialog before any BFF call.
 * - FR-B3-03 — sequential per-target push; aggregate toast (no picker).
 * - FR-B3-04 — over-limit toast wording MATCHES VERBATIM:
 *   `"Too many related records to push interactively (X > 500). Contact your
 *   administrator to run the batch cascade job."` where `X` is the childCount
 *   parsed from the BFF ProblemDetails payload.
 * - Plain JS — NO npm imports, NO ES modules, NO bundler, NO `@spaarke/auth`.
 * - ADR-006 — webresource is an approved surface for ribbon-adjacent host
 *   scripting; no PCF or Code Page needed for this rule.
 *
 * # Version
 *
 * v1.0.0 — initial implementation (SRFR-061, 2026-07-02)
 * v1.1.0 — MSAL Bearer token auth for BFF /push (SRFR-053, 2026-07-06):
 *          replaced broken `credentials: "include"` cookie approach with
 *          MSAL silent → SSO → popup token chain (mirrors
 *          `sprk_communication_send.js`).
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
    ns.VERSION = "1.1.0";

    /** Cache-key prefix for the visibility-check sessionStorage entries. */
    var CACHE_KEY_PREFIX = "sprk_fieldmapping_hasSource_";

    /** Cache-key prefix for the targets list (populated by hasSourceProfile). */
    var TARGETS_CACHE_KEY_PREFIX = "sprk_fieldmapping_targets_";

    /**
     * Dev fallback BFF base URL used ONLY when `sprk_BffApiBaseUrl`
     * environment variable is not resolvable. Production environments MUST
     * define the env var per canonical wiring.
     */
    var BFF_FALLBACK_BASE_URL = "https://spe-api-dev.azurewebsites.net";

    /** Dataverse environment variable schema name for the BFF base URL. */
    var BFF_BASE_URL_ENV_VAR = "sprk_BffApiBaseUrl";

    /** Push endpoint path (relative to base URL). */
    var PUSH_ENDPOINT_PATH = "/api/v1/field-mappings/push";

    /**
     * BFF over-limit ProblemDetails signal. The BFF returns HTTP 400 with
     * `title: "Limit Exceeded"` when childCount > 500 (spec §A.5 revised —
     * server enforces cap; client surfaces FR-B3-04 wording).
     */
    var OVER_LIMIT_TITLE = "Limit Exceeded";

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
            return queryHasSourceProfile(entityName, formId, cacheKey);
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
     * @param {string} formId
     * @param {string} cacheKey
     * @returns {Promise<boolean>}
     */
    function queryHasSourceProfile(entityName, formId, cacheKey) {
        return resolveRecordTypeRefForEntity(entityName).then(function (sourceRtId) {
            if (!sourceRtId) {
                // Catalog gap — treat as no profiles (button hidden).
                safeSessionSet(cacheKey, "false");
                return false;
            }
            var profilesQuery =
                "?$filter=_sprk_sourcerecordtype_value eq " + sourceRtId +
                " and statecode eq 0" +
                "&$select=sprk_fieldmappingprofileid,_sprk_targetrecordtype_value";
            return Xrm.WebApi.retrieveMultipleRecords("sprk_fieldmappingprofile", profilesQuery)
                .then(function (result) {
                    var entities = (result && result.entities) || [];
                    var has = entities.length > 0;
                    safeSessionSet(cacheKey, has ? "true" : "false");
                    // Cache the target recordtype GUIDs for pushUpdates to reuse.
                    var targetsCacheKey = TARGETS_CACHE_KEY_PREFIX + entityName + "_" + formId;
                    var targetRtIds = entities
                        .map(function (e) { return e["_sprk_targetrecordtype_value"]; })
                        .filter(function (v) { return !!v; });
                    safeSessionSet(targetsCacheKey, JSON.stringify(targetRtIds));
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
     * Ribbon Action: confirm → sequential per-target push → aggregate toast.
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
                    text: "Record must be saved before pushing field-mapping updates."
                });
                return;
            }
            var recordId = String(recordIdRaw).replace(/[{}]/g, "");
            var formId = recordIdRaw;

            Xrm.Navigation.openConfirmDialog({
                title: "Push Updates",
                text: "This will push field-mapping updates from this record to related child records. Continue?"
            }).then(function (result) {
                if (!result || !result.confirmed) {
                    return;
                }
                executePushForAllTargets(entityName, recordId, formId);
            });
        } catch (err) {
            console.error("[FieldMapping.Push v" + ns.VERSION + "] pushUpdates error:", err);
            Xrm.Navigation.openAlertDialog({
                text: "Unable to start push. See console for details."
            });
        }
    };

    /**
     * Orchestrate the full push flow: resolve targets, resolve BFF base URL,
     * sequential per-target fetch, aggregate, toast.
     */
    function executePushForAllTargets(entityName, recordId, formId) {
        Xrm.Utility.showProgressIndicator("Pushing field-mapping updates...");
        Promise.all([
            resolveTargetEntitiesForSource(entityName, formId),
            resolveBffBaseUrl()
        ]).then(function (parts) {
            var targetEntities = parts[0];
            var baseUrl = parts[1];
            if (!targetEntities || targetEntities.length === 0) {
                Xrm.Utility.closeProgressIndicator();
                Xrm.Navigation.openAlertDialog({
                    text: "No target entities are configured for this record. Nothing to push."
                });
                return;
            }
            if (!baseUrl) {
                Xrm.Utility.closeProgressIndicator();
                Xrm.Navigation.openAlertDialog({
                    text: "BFF API base URL is not configured. Contact your administrator " +
                        "to set the '" + BFF_BASE_URL_ENV_VAR + "' environment variable."
                });
                return;
            }
            return pushSequentially(baseUrl, entityName, recordId, targetEntities)
                .then(function (aggregate) {
                    Xrm.Utility.closeProgressIndicator();
                    presentAggregateOutcome(aggregate, targetEntities.length);
                });
        }).catch(function (err) {
            Xrm.Utility.closeProgressIndicator();
            console.error("[FieldMapping.Push v" + ns.VERSION + "] executePushForAllTargets error:", err);
            Xrm.Navigation.openAlertDialog({
                text: "Push failed to start. See console for details."
            });
        });
    }

    /**
     * Resolve the list of target entity LOGICAL NAMES for the current source
     * entity. Uses the cached target-recordtype GUIDs from hasSourceProfile if
     * present; otherwise runs the full two-step lookup fresh.
     *
     * @returns {Promise<string[]>}
     */
    function resolveTargetEntitiesForSource(entityName, formId) {
        var targetsCacheKey = TARGETS_CACHE_KEY_PREFIX + entityName + "_" + formId;
        var cachedRaw = safeSessionGet(targetsCacheKey);
        var cachedIds = null;
        if (cachedRaw) {
            try {
                cachedIds = JSON.parse(cachedRaw);
            } catch (_) {
                cachedIds = null;
            }
        }
        var idsPromise;
        if (cachedIds && cachedIds.length) {
            idsPromise = Promise.resolve(cachedIds);
        } else {
            idsPromise = resolveRecordTypeRefForEntity(entityName).then(function (sourceRtId) {
                if (!sourceRtId) {
                    return [];
                }
                var q =
                    "?$filter=_sprk_sourcerecordtype_value eq " + sourceRtId +
                    " and statecode eq 0" +
                    "&$select=_sprk_targetrecordtype_value";
                return Xrm.WebApi.retrieveMultipleRecords("sprk_fieldmappingprofile", q)
                    .then(function (r) {
                        var ents = (r && r.entities) || [];
                        return ents
                            .map(function (e) { return e["_sprk_targetrecordtype_value"]; })
                            .filter(function (v) { return !!v; });
                    });
            });
        }
        return idsPromise.then(function (targetRtIds) {
            // De-dupe.
            var unique = [];
            var seen = {};
            for (var i = 0; i < targetRtIds.length; i++) {
                if (!seen[targetRtIds[i]]) {
                    seen[targetRtIds[i]] = true;
                    unique.push(targetRtIds[i]);
                }
            }
            // Resolve each GUID → logical name.
            var resolvers = unique.map(function (rtId) {
                return resolveEntityNameForRecordTypeRef(rtId);
            });
            return Promise.all(resolvers).then(function (names) {
                return names.filter(function (n) { return !!n; });
            });
        });
    }

    /**
     * Sequential per-target push. Aggregates results across ALL targets.
     * FR-B3-03: sequential (NOT Promise.all). FR-B3-04: on over-limit,
     * short-circuit remaining targets and set the specific-message flag.
     *
     * @returns {Promise<{
     *   updated: number,
     *   skipped: number,
     *   errors: number,
     *   childCount: number,
     *   overLimitChildCount: (number|null),
     *   networkErrorCount: number,
     *   completedTargets: number
     * }>}
     */
    function pushSequentially(baseUrl, sourceEntity, sourceRecordId, targetEntities) {
        var agg = {
            updated: 0,
            skipped: 0,
            errors: 0,
            childCount: 0,
            overLimitChildCount: null,
            networkErrorCount: 0,
            completedTargets: 0
        };
        var chain = Promise.resolve();
        targetEntities.forEach(function (targetEntity) {
            chain = chain.then(function () {
                if (agg.overLimitChildCount !== null) {
                    // Short-circuit: over-limit hit on a prior target.
                    return;
                }
                return pushOne(baseUrl, sourceEntity, sourceRecordId, targetEntity)
                    .then(function (outcome) {
                        if (outcome.overLimit) {
                            agg.overLimitChildCount = outcome.childCount;
                            return;
                        }
                        agg.updated += outcome.updated;
                        agg.skipped += outcome.skipped;
                        agg.errors += outcome.errors;
                        agg.childCount += outcome.childCount;
                        agg.completedTargets++;
                    })
                    .catch(function (err) {
                        console.warn(
                            "[FieldMapping.Push v" + ns.VERSION + "] target " +
                            targetEntity + " failed:", err
                        );
                        agg.networkErrorCount++;
                    });
            });
        });
        return chain.then(function () { return agg; });
    }

    /**
     * Push to a single target. Returns a normalized outcome or an over-limit
     * signal. Rejects on hard network errors so the caller can bucket them.
     *
     * @returns {Promise<{
     *   overLimit: boolean,
     *   childCount: number,
     *   updated: number,
     *   skipped: number,
     *   errors: number
     * }>}
     */
    function pushOne(baseUrl, sourceEntity, sourceRecordId, targetEntity) {
        var url = baseUrl + PUSH_ENDPOINT_PATH;
        var body = {
            sourceEntity: sourceEntity,
            sourceRecordId: sourceRecordId,
            targetEntity: targetEntity
        };
        // Acquire a Bearer JWT via MSAL BEFORE issuing the /push fetch.
        // Rationale: BFF `/api/v1/field-mappings/push` REQUIRES an OBO Bearer
        // token; the previous cookie-credentials pattern (v1.0.0) hit 401. See
        // module header §Auth for the ADR-028 Path A rationale. SRFR-053.
        return getAuthTokenForBff().then(function (token) {
            if (!token) {
                // Token acquisition failed hard — surface a bucketed error
                // rather than firing an unauthenticated request that we know
                // will 401. `pushSequentially` counts a thrown rejection here
                // as a network error, which is the correct bucket.
                throw new Error("BFF auth token acquisition failed");
            }
            return fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    "Authorization": "Bearer " + token
                },
                body: JSON.stringify(body)
            });
        }).then(function (response) {
            // 400 = validation error; MAY be over-limit (ProblemDetails
            // with title === "Limit Exceeded"). Parse body to disambiguate.
            if (response.status === 400) {
                return response.json().then(function (payload) {
                    var isOverLimit = false;
                    var childCount = null;
                    if (payload) {
                        // BFF returns ProblemDetails: { title, detail, status, ... }
                        if (payload.title === OVER_LIMIT_TITLE) {
                            isOverLimit = true;
                            childCount = extractChildCountFromDetail(payload.detail);
                        }
                        // Also honor the spec §A.5 example shape as a
                        // forward-compatibility path in case BFF migrates.
                        if (payload.error === "over_limit") {
                            isOverLimit = true;
                            if (typeof payload.childCount === "number") {
                                childCount = payload.childCount;
                            }
                        }
                    }
                    if (isOverLimit) {
                        return {
                            overLimit: true,
                            childCount: (typeof childCount === "number" && childCount > 0)
                                ? childCount
                                : 501, // spec §A.5 min-plausible value if BFF omits
                            updated: 0,
                            skipped: 0,
                            errors: 0
                        };
                    }
                    // Non-over-limit 400 — count as an error.
                    return {
                        overLimit: false,
                        childCount: 0,
                        updated: 0,
                        skipped: 0,
                        errors: 1
                    };
                }).catch(function () {
                    return { overLimit: false, childCount: 0, updated: 0, skipped: 0, errors: 1 };
                });
            }
            if (!response.ok) {
                // 401/403/404/500 etc. — count as an error for this target.
                return { overLimit: false, childCount: 0, updated: 0, skipped: 0, errors: 1 };
            }
            return response.json().then(function (payload) {
                return normalizePushResponse(payload);
            });
        });
    }

    /**
     * Normalize the BFF success payload into the aggregate-friendly shape.
     * The BFF (2026-07-02) returns `PushFieldMappingsResponse` with
     * `UpdatedCount`, `SkippedCount`, `FailedCount`, `TotalRecords`. Spec
     * §A.5 example shape uses `updated`/`skipped`/`errors`/`childCount` —
     * accept BOTH forms so this webresource does not require BFF changes to
     * work today but stays forward-compatible.
     */
    function normalizePushResponse(payload) {
        payload = payload || {};
        var updated = pickNumber(payload, ["updated", "UpdatedCount", "updatedCount"], 0);
        var skipped = pickNumber(payload, ["skipped", "SkippedCount", "skippedCount"], 0);
        var errors = pickNumber(payload, ["errors", "FailedCount", "failedCount"], 0);
        var childCount = pickNumber(
            payload,
            ["childCount", "TotalRecords", "totalRecords"],
            updated + skipped + errors
        );
        return {
            overLimit: false,
            childCount: childCount,
            updated: updated,
            skipped: skipped,
            errors: errors
        };
    }

    /**
     * Present the FINAL toast to the user. FR-B3-04 (over-limit) wording is
     * VERBATIM — do not reword. Non-over-limit path aggregates counts.
     */
    function presentAggregateOutcome(agg, totalTargetCount) {
        // FR-B3-04: exact wording. `X` = childCount reported by BFF for the
        // target that tripped the limit.
        if (agg.overLimitChildCount !== null) {
            var overLimitText =
                "Too many related records to push interactively (" +
                agg.overLimitChildCount +
                " > 500). Contact your administrator to run the batch cascade job.";
            Xrm.Navigation.openAlertDialog({
                title: "Push Not Available",
                text: overLimitText
            });
            return;
        }
        // FR-B3-03 aggregate summary.
        var summary =
            "Updated " + agg.updated + " of " + agg.childCount +
            " records across " + totalTargetCount + " target entities. " +
            agg.skipped + " skipped, " + agg.errors + " errors.";
        if (agg.networkErrorCount > 0) {
            summary += " (" + agg.networkErrorCount + " target(s) failed to reach the server.)";
        }
        Xrm.Navigation.openAlertDialog({
            title: (agg.errors > 0 || agg.networkErrorCount > 0)
                ? "Push Complete (with errors)"
                : "Push Complete",
            text: summary
        });
    }

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

    /**
     * Reverse lookup: recordtype-ref GUID → entity logical name.
     * Used to resolve `_sprk_targetrecordtype_value` GUIDs from a profile
     * into the entity logical name the BFF /push endpoint expects.
     */
    function resolveEntityNameForRecordTypeRef(recordTypeRefId) {
        if (!recordTypeRefId) return Promise.resolve(null);
        return Xrm.WebApi.retrieveRecord(
            "sprk_recordtype_ref",
            recordTypeRefId,
            "?$select=sprk_recordlogicalname"
        ).then(function (r) {
            return r ? r.sprk_recordlogicalname : null;
        }).catch(function (err) {
            console.warn(
                "[FieldMapping.Push v" + ns.VERSION + "] resolveEntityNameForRecordTypeRef failed for " +
                recordTypeRefId + ":", err
            );
            return null;
        });
    }

    // -----------------------------------------------------------------------
    // Helpers — MSAL Bearer token for BFF (SRFR-053, v1.1.0)
    // -----------------------------------------------------------------------
    // Mirrors the pattern in `sprk_communication_send.js`:
    //   1. Load msal-browser lib from Microsoft CDN (cached across pages).
    //   2. Resolve MSAL config from Dataverse env vars (sprk_MsalClientId,
    //      sprk_BffApiAppId, sprk_TenantId); redirectUri from Xrm context.
    //   3. Init PublicClientApplication (reuse across calls; also reuse
    //      Sprk.Communication.Send or Spaarke.Email instance if pre-existing
    //      on the page — avoids duplicate popups for the same user session).
    //   4. acquireTokenSilent → ssoSilent → acquireTokenPopup fallback chain.

    /** Cached MSAL/BFF config resolved from Dataverse env vars. */
    var _msalConfig = null;
    var _configResolvePromise = null;

    /** Cached MSAL PublicClientApplication instance. */
    var _msalInstance = null;
    var _msalInitPromise = null;

    /** Cached logged-in account (for silent-token retry). */
    var _currentAccount = null;

    /**
     * MSAL library CDN URL — SAME version as `sprk_communication_send.js`
     * so a page load that already fetched msal-browser for the Send button
     * won't reload it for the Push button.
     */
    var MSAL_CDN_URL = "https://alcdn.msauth.net/browser/2.38.0/js/msal-browser.min.js";

    /**
     * Resolve MSAL config (clientId, bffAppId, tenantId, redirectUri) from
     * Dataverse Environment Variables. Cached on module.
     */
    function resolveMsalConfig() {
        if (_configResolvePromise) {
            return _configResolvePromise;
        }
        _configResolvePromise = Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariabledefinition",
            "?$filter=" +
                "schemaname eq 'sprk_BffApiAppId' or " +
                "schemaname eq 'sprk_MsalClientId' or " +
                "schemaname eq 'sprk_TenantId'" +
            "&$select=schemaname,defaultvalue" +
            "&$expand=environmentvariabledefinition_environmentvariablevalue($select=value)"
        ).then(function (result) {
            var resolved = {};
            if (result && result.entities) {
                result.entities.forEach(function (def) {
                    var vals = def.environmentvariabledefinition_environmentvariablevalue;
                    if (vals && vals.length > 0 && vals[0].value) {
                        resolved[def.schemaname] = vals[0].value;
                    } else if (def.defaultvalue) {
                        resolved[def.schemaname] = def.defaultvalue;
                    }
                });
            }
            var required = ["sprk_BffApiAppId", "sprk_MsalClientId", "sprk_TenantId"];
            var missing = required.filter(function (k) { return !resolved[k]; });
            if (missing.length) {
                throw new Error(
                    "[FieldMapping.Push v" + ns.VERSION + "] Missing required env vars for MSAL: " +
                    missing.join(", ")
                );
            }
            var redirectUri;
            try {
                redirectUri = Xrm.Utility.getGlobalContext().getClientUrl();
            } catch (e) {
                throw new Error(
                    "[FieldMapping.Push v" + ns.VERSION + "] Cannot determine redirectUri: " + e.message
                );
            }
            _msalConfig = {
                clientId: resolved["sprk_MsalClientId"],
                bffAppId: resolved["sprk_BffApiAppId"],
                tenantId: resolved["sprk_TenantId"],
                redirectUri: redirectUri
            };
            console.log(
                "[FieldMapping.Push v" + ns.VERSION + "] MSAL config resolved:",
                {
                    clientId: _msalConfig.clientId,
                    bffAppId: _msalConfig.bffAppId,
                    tenantId: _msalConfig.tenantId,
                    redirectUri: _msalConfig.redirectUri
                }
            );
            return _msalConfig;
        }).catch(function (err) {
            _configResolvePromise = null;
            console.error("[FieldMapping.Push v" + ns.VERSION + "] MSAL config resolution failed:", err);
            throw err;
        });
        return _configResolvePromise;
    }

    /**
     * Load msal-browser from CDN (idempotent — checks window.msal first).
     */
    function loadMsalLibrary() {
        return new Promise(function (resolve, reject) {
            if (typeof msal !== "undefined" && msal.PublicClientApplication) {
                resolve();
                return;
            }
            var script = document.createElement("script");
            script.src = MSAL_CDN_URL;
            script.onload = function () {
                console.log("[FieldMapping.Push v" + ns.VERSION + "] MSAL library loaded from CDN");
                resolve();
            };
            script.onerror = function () {
                reject(new Error("Failed to load MSAL library from " + MSAL_CDN_URL));
            };
            document.head.appendChild(script);
        });
    }

    /**
     * Initialize (or retrieve) the MSAL PublicClientApplication instance.
     * Reuses `Sprk.Communication.Send._msalInstance` or
     * `Spaarke.Email._msalInstance` if already present on the page — avoids
     * duplicate popups when the user has already authenticated via another
     * ribbon action in the same tab.
     */
    function initMsal() {
        // Reuse existing MSAL instance from sibling ribbon modules if present.
        if (typeof Sprk !== "undefined" && Sprk.Communication && Sprk.Communication.Send &&
            Sprk.Communication.Send._msalInstance) {
            console.log("[FieldMapping.Push v" + ns.VERSION + "] Reusing Sprk.Communication.Send MSAL instance");
            _msalInstance = Sprk.Communication.Send._msalInstance;
            if (Sprk.Communication.Send._currentAccount) {
                _currentAccount = Sprk.Communication.Send._currentAccount;
            }
            // Config for THIS module still resolved (bffAppId needed for scope).
            return resolveMsalConfig().then(function () { return _msalInstance; });
        }
        if (typeof Spaarke !== "undefined" && Spaarke.Email && Spaarke.Email._msalInstance) {
            console.log("[FieldMapping.Push v" + ns.VERSION + "] Reusing Spaarke.Email MSAL instance");
            _msalInstance = Spaarke.Email._msalInstance;
            if (Spaarke.Email._currentAccount) {
                _currentAccount = Spaarke.Email._currentAccount;
            }
            return resolveMsalConfig().then(function () { return _msalInstance; });
        }
        if (_msalInitPromise) {
            return _msalInitPromise;
        }
        _msalInitPromise = Promise.all([
            resolveMsalConfig(),
            loadMsalLibrary()
        ]).then(function (parts) {
            var cfg = parts[0];
            var authorityMetadata = JSON.stringify({
                "authorization_endpoint": "https://login.microsoftonline.com/" + cfg.tenantId + "/oauth2/v2.0/authorize",
                "token_endpoint": "https://login.microsoftonline.com/" + cfg.tenantId + "/oauth2/v2.0/token",
                "issuer": "https://login.microsoftonline.com/" + cfg.tenantId + "/v2.0",
                "jwks_uri": "https://login.microsoftonline.com/" + cfg.tenantId + "/discovery/v2.0/keys",
                "end_session_endpoint": "https://login.microsoftonline.com/" + cfg.tenantId + "/oauth2/v2.0/logout"
            });
            var msalConfig = {
                auth: {
                    clientId: cfg.clientId,
                    authority: "https://login.microsoftonline.com/" + cfg.tenantId,
                    redirectUri: cfg.redirectUri,
                    navigateToLoginRequestUrl: false,
                    knownAuthorities: ["login.microsoftonline.com"],
                    authorityMetadata: authorityMetadata
                },
                cache: {
                    cacheLocation: "sessionStorage",
                    storeAuthStateInCookie: false
                },
                system: {
                    loggerOptions: {
                        loggerCallback: function (level, message, containsPii) {
                            if (containsPii) return;
                            if (level <= 1) {
                                console.log("[FieldMapping.Push MSAL] " + message);
                            }
                        },
                        logLevel: 3
                    }
                }
            };
            _msalInstance = new msal.PublicClientApplication(msalConfig);
            if (typeof _msalInstance.initialize === "function") {
                return _msalInstance.initialize().then(function () {
                    return _msalInstance.handleRedirectPromise();
                });
            }
            return _msalInstance.handleRedirectPromise();
        }).then(function (redirectResponse) {
            if (redirectResponse && redirectResponse.account) {
                _currentAccount = redirectResponse.account;
            } else {
                var accounts = _msalInstance.getAllAccounts();
                if (accounts && accounts.length) {
                    _currentAccount = accounts[0];
                }
            }
            return _msalInstance;
        }).catch(function (err) {
            _msalInitPromise = null;
            console.error("[FieldMapping.Push v" + ns.VERSION + "] MSAL init failed:", err);
            throw err;
        });
        return _msalInitPromise;
    }

    /**
     * Silent → SSO → popup fallback chain. Returns the resulting access token
     * for the BFF scope `api://{bffAppId}/user_impersonation`. Returns `null`
     * on any failure — caller decides whether to proceed unauthenticated
     * (we do not — `pushOne` treats null as a hard failure).
     */
    function getAuthTokenForBff() {
        return initMsal().then(function (msalInstance) {
            var cfg = _msalConfig;
            var scope = "api://" + cfg.bffAppId + "/user_impersonation";
            if (_currentAccount) {
                return msalInstance.acquireTokenSilent({
                    scopes: [scope],
                    account: _currentAccount
                }).then(function (resp) {
                    return resp.accessToken;
                }).catch(function () {
                    return ssoAndPopupFallback(msalInstance, scope);
                });
            }
            return ssoAndPopupFallback(msalInstance, scope);
        }).catch(function (err) {
            console.error("[FieldMapping.Push v" + ns.VERSION + "] getAuthTokenForBff failed:", err);
            return null;
        });
    }

    /** SSO-silent → popup fallback (shared branch). */
    function ssoAndPopupFallback(msalInstance, scope) {
        return msalInstance.ssoSilent({ scopes: [scope] }).then(function (resp) {
            if (resp.account) _currentAccount = resp.account;
            return resp.accessToken;
        }).catch(function () {
            var popupRequest = {
                scopes: [scope],
                loginHint: _currentAccount ? _currentAccount.username : undefined
            };
            return msalInstance.acquireTokenPopup(popupRequest).then(function (resp) {
                if (resp.account) _currentAccount = resp.account;
                return resp.accessToken;
            });
        });
    }

    // -----------------------------------------------------------------------
    // Helpers — BFF base URL resolution
    // -----------------------------------------------------------------------

    /**
     * Resolve the BFF API base URL from the canonical Dataverse env-var
     * (`sprk_BffApiBaseUrl`). Falls back to a hardcoded dev URL if the env
     * var is missing (matches the pattern in `sprk_analysis_commands.js`
     * and `sprk_updaterelated_commands.js`).
     */
    function resolveBffBaseUrl() {
        return getEnvironmentVariable(BFF_BASE_URL_ENV_VAR).then(function (val) {
            var raw = val || BFF_FALLBACK_BASE_URL;
            // Strip trailing slashes AND a trailing `/api` to prevent the
            // recurring `/api/api/` double-prefix bug.
            return raw.replace(/\/+$/, "").replace(/\/api$/i, "");
        }).catch(function () {
            return BFF_FALLBACK_BASE_URL;
        });
    }

    /**
     * Read a Dataverse environment variable's current value (falling back to
     * the definition default). Mirrors `sprk_analysis_commands.js`
     * `getEnvironmentVariable()`.
     */
    function getEnvironmentVariable(schemaName) {
        return Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariabledefinition",
            "?$filter=schemaname eq '" + escapeODataString(schemaName) +
            "'&$select=environmentvariabledefinitionid,defaultvalue"
        ).then(function (defResult) {
            var defs = (defResult && defResult.entities) || [];
            if (!defs.length) {
                console.warn(
                    "[FieldMapping.Push v" + ns.VERSION + "] env var '" + schemaName + "' not found"
                );
                return null;
            }
            var defId = defs[0].environmentvariabledefinitionid;
            var defaultValue = defs[0].defaultvalue;
            return Xrm.WebApi.retrieveMultipleRecords(
                "environmentvariablevalue",
                "?$filter=_environmentvariabledefinitionid_value eq '" + defId +
                "'&$select=value"
            ).then(function (valResult) {
                var vals = (valResult && valResult.entities) || [];
                if (vals.length && vals[0].value) {
                    return vals[0].value;
                }
                return defaultValue || null;
            });
        }).catch(function (err) {
            console.warn(
                "[FieldMapping.Push v" + ns.VERSION + "] getEnvironmentVariable error:", err
            );
            return null;
        });
    }

    // -----------------------------------------------------------------------
    // Helpers — utilities
    // -----------------------------------------------------------------------

    /**
     * Extract the numeric childCount from a BFF over-limit ProblemDetails
     * `detail` string, e.g.:
     *
     *   "Too many child records (731). Maximum allowed per push operation is 500..."
     *
     * Returns `null` if the pattern cannot be matched.
     */
    function extractChildCountFromDetail(detail) {
        if (!detail || typeof detail !== "string") return null;
        var m = detail.match(/\((\d+)\)/);
        if (m && m[1]) {
            var n = parseInt(m[1], 10);
            if (!isNaN(n) && n > 0) return n;
        }
        return null;
    }

    /**
     * Escape single quotes in an OData string literal. Dataverse OData does
     * not accept backslash-escapes; single-quote is escaped by doubling.
     */
    function escapeODataString(value) {
        if (value === null || value === undefined) return "";
        return String(value).replace(/'/g, "''");
    }

    /**
     * Pick the first numeric value from `payload` whose key is in `keys`.
     * Returns `fallback` if none present.
     */
    function pickNumber(payload, keys, fallback) {
        for (var i = 0; i < keys.length; i++) {
            var v = payload[keys[i]];
            if (typeof v === "number" && !isNaN(v)) return v;
        }
        return fallback;
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
                normalizePushResponse: normalizePushResponse,
                extractChildCountFromDetail: extractChildCountFromDetail,
                escapeODataString: escapeODataString,
                pickNumber: pickNumber
            },
            VERSION: ns.VERSION
        };
    }
})(Spaarke.FieldMapping.Push);
