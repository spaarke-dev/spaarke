"use strict";
/**
 * Matter Insight Widget — InsightSummaryCard Mount Glue
 *
 * Web Resource Name: sprk_/scripts/matter_insight_card_mount.js
 *
 * Hosts the React `InsightSummaryCard` component (from `@spaarke/ai-widgets`)
 * on the Matter form's Matter Health card section. Coexists with:
 *   - `Spaarke.MatterInsight.onLoad` (Task 040+041) — form OnLoad pre-warm
 *   - `Spaarke.MatterKpi.onLoad` (matter-performance-KPI-r1) — KPI subgrid refresh
 *
 * Purpose (Task 042 / spec FR-19):
 *   1. Mount the React card into a host `<div>` inside the Matter Health card section.
 *   2. Resolve current Matter Guid → pass as `subject = "matter:{guid}"` prop.
 *   3. Read stored envelope from `sprk_performancesummary` (re-using the read
 *      already done by Task 040's `Spaarke.MatterInsight._parseEnvelope`).
 *   4. Hand the stored envelope into the card via the `onFetchInsight` callback
 *      so FR-19 immediate-render is honoured (no spinner on form load when an
 *      envelope already exists).
 *   5. Wire `onCitationClick` to in-product navigation
 *      (`Xrm.Navigation.openForm` for assessment + document citations).
 *   6. Translate (topic, mode) → canonical playbook name for BFF invocation,
 *      per the wire-shape gap resolution Option (b) — see project current-task.md.
 *
 * Wire-shape contract — `/api/insights/ask` POST body:
 *   { question: "matter-health-single", subject: "matter:{guid}", parameters: {} }
 *
 * Rationale: `BFF.Models.Insights.InsightAskRequest` expects `question` as
 * either a Guid OR a canonical playbook name resolvable via
 * `InsightsPlaybookNameMapOptions.Map` (which already maps
 * "matter-health-single" → the deployed playbook Guid per
 * `notes/handoffs/playbook-deploy.md`). The form OnLoad pre-warm in Task 041
 * uses an INCORRECT shape (`{ topic, mode, subject, parameters }`); that's a
 * Task 041 follow-up to align — silent failure today because the pre-warm POST
 * swallows its 400 response per FR-18 fire-and-forget design. The CARD
 * invocation here is the user-visible path and MUST be correct.
 *
 * Mount mechanism (Phase 4 scope):
 *   Task 042 ships the mount CONTRACT — the registration + host glue. The
 *   production React bundle (esbuild/webpack-bundled @spaarke/ai-widgets +
 *   React 18 + ReactDOM as a single MDA-loadable IIFE) is shipped in Task 043
 *   ("Solution package (FormXml + web resource); deploy"). When the bundle is
 *   absent (e.g., during Phase 4 staging), this script renders a placeholder
 *   that surfaces the contract is wired correctly — diagnostic only, never
 *   blocking.
 *
 * Source layout:
 *   - Source: src/dataverse/forms/sprk_matter/insightCardMount.ts
 *   - Compiled web resource: src/dataverse/forms/sprk_matter/insightCardMount.js
 *
 * Form Events:
 *   Event:   OnLoad
 *   Library: sprk_/scripts/matter_insight_card_mount.js
 *   Function: Spaarke.MatterInsightCard.onLoad
 *   Pass execution context: Yes
 *
 * Critical constraints:
 *   - NFR-03: must not block form TTI. Mount runs after a single
 *     `requestAnimationFrame` so the card render is detached from the
 *     synchronous OnLoad return path.
 *   - Q-U6: Power Apps form OnLoad mechanism — NOT Power Automate, NOT a
 *     React Code Page wrapper.
 *   - Q-U1: no `@v1`/`@vN` identifier-suffix vernacular. Playbook name
 *     `matter-health-single` is BARE.
 *   - FR-19: stored envelope renders IMMEDIATELY — passed via
 *     `onFetchInsight` callback that resolves synchronously when an envelope
 *     is available; spinner appears only on manual refresh (FR-20).
 *   - ADR-021: semantic tokens + dark mode — host detects MDA theme via
 *     `Xrm.Utility.getGlobalContext().userSettings.themeId` (Phase 4 best
 *     effort; full theme propagation Task 037 verification).
 *   - ADR-012: card is host-agnostic; ALL Xrm/MDA glue lives in THIS file.
 */
var __assign = (this && this.__assign) || function () {
    __assign = Object.assign || function(t) {
        for (var s, i = 1, n = arguments.length; i < n; i++) {
            s = arguments[i];
            for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p))
                t[p] = s[p];
        }
        return t;
    };
    return __assign.apply(this, arguments);
};
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
var __generator = (this && this.__generator) || function (thisArg, body) {
    var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g = Object.create((typeof Iterator === "function" ? Iterator : Object).prototype);
    return g.next = verb(0), g["throw"] = verb(1), g["return"] = verb(2), typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
    function verb(n) { return function (v) { return step([n, v]); }; }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while (g && (g = 0, op[0] && (_ = 0)), _) try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [op[0] & 2, t.value];
            switch (op[0]) {
                case 0: case 1: t = op; break;
                case 4: _.label++; return { value: op[1], done: false };
                case 5: _.label++; y = op[1]; op = [0]; continue;
                case 7: op = _.ops.pop(); _.trys.pop(); continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                    if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                    if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                    if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                    if (t[2]) _.ops.pop();
                    _.trys.pop(); continue;
            }
            op = body.call(thisArg, _);
        } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
        if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
    }
};
// =============================================================================
// NAMESPACE
// =============================================================================
(function () {
    if (typeof window === "undefined") {
        return;
    }
    var w = window;
    w.Spaarke = w.Spaarke || {};
    w.Spaarke.MatterInsightCard = w.Spaarke.MatterInsightCard || {};
    var ns = w.Spaarke.MatterInsightCard;
    // ===========================================================================
    // CONFIGURATION
    // ===========================================================================
    /** Handler version (informational; bumped per task). */
    ns._version = "0.1.0";
    /** Field name read on OnLoad to source the stored envelope (FR-17). */
    ns._summaryField = "sprk_performancesummary";
    /** Entity logical name. */
    ns._entityName = "sprk_matter";
    /** Topic identifier — aligned with `sprk_aitopicregistry.sprk_topicname`. */
    ns._topic = "matter-health";
    /** Mode identifier — aligned with `sprk_aitopicregistry.sprk_mode`. */
    ns._mode = "single";
    /**
     * Canonical playbook name registered in `InsightsPlaybookNameMapOptions.Map`.
     * Used as the `question` field on `/api/insights/ask` per wire-shape Option (b).
     * Bare per Q-U1 — no `@v1`/`@vN` suffix.
     */
    ns._playbookName = "matter-health-single";
    /** Subject scheme prefix per r2 multi-entity scheme. r1 uses `matter:` only. */
    ns._subjectScheme = "matter";
    /** BFF endpoint — same-origin per Task 041 design. */
    ns._invocationEndpoint = "/api/insights/ask";
    /**
     * DOM host id for the card mount. The FormXml patch registers a custom HTML
     * web resource at this id inside the Matter Health card section. The mount
     * glue locates it via `document.getElementById`.
     */
    ns._hostElementId = "spaarke-matter-insight-card-host";
    // ===========================================================================
    // ENVELOPE READ — mirrors Task 040's parser; tolerates legacy R5 text.
    // ===========================================================================
    ns._parseEnvelope = function (raw) {
        if (raw === null || raw === undefined || raw === "") {
            return null;
        }
        try {
            var parsed = JSON.parse(raw);
            if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
                return null;
            }
            return parsed;
        }
        catch (_e) {
            return null;
        }
    };
    // ===========================================================================
    // BFF INVOCATION — wire-shape Option (b): question = playbook canonical name.
    // ===========================================================================
    /**
     * POST to `/api/insights/ask` with the wire-shape the BFF actually accepts.
     *
     * Per spec FR-20: when `force === true`, the request bypasses cache via the
     * `force=true` query string (Task 034 contract); the BFF cache layer reads
     * this and skips the playbook execution cache lookup.
     *
     * Returns the envelope on success; throws on failure or decline (the card's
     * reducer maps thrown errors → `error` / `decline` states per FR-06).
     */
    ns._invokeInsight = function (subject, force) {
        return __awaiter(this, void 0, void 0, function () {
            var url, body, response, text, payload, err, artifact;
            return __generator(this, function (_a) {
                switch (_a.label) {
                    case 0:
                        url = ns._invocationEndpoint + (force === true ? "?force=true" : "");
                        body = {
                            question: ns._playbookName,
                            subject: subject,
                            parameters: {},
                        };
                        return [4 /*yield*/, fetch(url, {
                                method: "POST",
                                headers: {
                                    "Content-Type": "application/json",
                                    Accept: "application/json",
                                },
                                body: JSON.stringify(body),
                                credentials: "include",
                            })];
                    case 1:
                        response = _a.sent();
                        if (!!response.ok) return [3 /*break*/, 3];
                        return [4 /*yield*/, response.text().catch(function () { return ""; })];
                    case 2:
                        text = _a.sent();
                        throw new Error("Insight invocation failed: HTTP " +
                            response.status +
                            (text ? " — " + text : ""));
                    case 3: return [4 /*yield*/, response.json()];
                    case 4:
                        payload = (_a.sent());
                        if (payload.decline) {
                            err = new Error(payload.decline.message || "Insufficient evidence");
                            err.kind = "decline";
                            if (payload.decline.recommendedAction) {
                                err.recommendedAction = payload.decline.recommendedAction;
                            }
                            throw err;
                        }
                        artifact = payload.artifact;
                        if (!artifact) {
                            throw new Error("Insight response contained neither artifact nor decline.");
                        }
                        // The BFF artifact carries body + citations; map to the envelope shape the
                        // card understands. `tldr`/`narrative` come from the persisted envelope on
                        // the matter record — the card's body renderer falls back gracefully if
                        // tldr is absent (renders narrative only).
                        return [2 /*return*/, {
                                tldr: undefined,
                                narrative: artifact.body,
                                citations: (artifact.citations || []),
                                generatedAt: new Date().toISOString(),
                                playbookName: ns._playbookName,
                            }];
                }
            });
        });
    };
    // ===========================================================================
    // CITATION NAVIGATION — opens MDA forms for in-product nav (FR-07).
    // ===========================================================================
    ns._onCitationClick = function (citation) {
        try {
            // Assessment citation → open assessment form.
            // Per project data model, assessment lives on `sprk_performanceassessment`
            // (the entity surfaced by the Matter Health card's KPI subgrid).
            var assessmentId = citation.assessmentId;
            if (assessmentId) {
                var options = {
                    entityName: "sprk_performanceassessment",
                    entityId: assessmentId,
                };
                void Xrm.Navigation.openForm(options).then(function () {
                    // Success — no action required.
                }, function (err) {
                    console.warn("[Matter Insight Card] v" +
                        ns._version +
                        " openForm(assessment) failed: " +
                        (err instanceof Error ? err.message : String(err)));
                });
                return;
            }
            // Document citation with SPE href → let the host open it directly
            // (window.open keeps the MDA tab context; document viewer chooses its surface).
            if (citation.speHref) {
                window.open(citation.speHref, "_blank", "noopener");
                return;
            }
            // Document citation with Dataverse documentId → open `sprk_document` form.
            var documentId = citation.documentId;
            if (documentId) {
                var options = {
                    entityName: "sprk_document",
                    entityId: documentId,
                };
                void Xrm.Navigation.openForm(options).then(function () {
                    // Success — no action required.
                }, function (err) {
                    console.warn("[Matter Insight Card] v" +
                        ns._version +
                        " openForm(document) failed: " +
                        (err instanceof Error ? err.message : String(err)));
                });
                return;
            }
            // Unknown citation shape — log only (FR-07 graceful fallback).
            console.warn("[Matter Insight Card] v" +
                ns._version +
                " citation click ignored (no assessmentId / speHref / documentId): ", citation);
        }
        catch (e) {
            // Defensive — citation click MUST NOT break the form.
            console.warn("[Matter Insight Card] v" + ns._version + " citation click failed (silent):", e);
        }
    };
    // ===========================================================================
    // HOST ELEMENT RESOLUTION
    // ===========================================================================
    /**
     * Locate the host `<div>` the FormXml patch registers inside the Matter
     * Health card section. The element is a Power Apps "WebResource" control
     * whose HTML body contains `<div id="spaarke-matter-insight-card-host">`.
     *
     * Returns `null` if the form layout doesn't include the host (e.g., user is
     * on a legacy form or the FormXml patch hasn't been deployed yet) — in that
     * case mount is skipped silently per NFR-03 (form load unaffected).
     */
    ns._resolveHost = function () {
        var el = document.getElementById(ns._hostElementId);
        return el || null;
    };
    // ===========================================================================
    // BUNDLE RESOLUTION
    // ===========================================================================
    /**
     * Locate the `@spaarke/ai-widgets` bundle on the global namespace. The
     * bundle (shipped in Task 043 as a separate web resource) attaches itself
     * to `window.SpaarkeAiWidgets`. When absent (Phase 4 staging), the mount
     * glue renders a placeholder and surfaces the contract is wired correctly.
     */
    ns._resolveBundle = function () {
        var bundle = window.SpaarkeAiWidgets;
        if (bundle && typeof bundle.mountInsightSummaryCard === "function") {
            return bundle;
        }
        return null;
    };
    // ===========================================================================
    // PLACEHOLDER RENDER (bundle absent — Phase 4 staging only)
    // ===========================================================================
    /**
     * Diagnostic placeholder rendered when the @spaarke/ai-widgets bundle is
     * not yet loaded. Shows the resolved contract (topic / subject / mode /
     * envelope status) so operators can verify the mount glue is wired before
     * Task 043 ships the React bundle.
     */
    ns._renderPlaceholder = function (host, subject, envelope) {
        while (host.firstChild) {
            host.removeChild(host.firstChild);
        }
        var wrapper = document.createElement("div");
        wrapper.setAttribute("data-testid", "insight-card-placeholder");
        wrapper.style.padding = "12px";
        wrapper.style.border = "1px dashed #c8c6c4";
        wrapper.style.borderRadius = "4px";
        wrapper.style.fontFamily = "Segoe UI, sans-serif";
        wrapper.style.fontSize = "12px";
        wrapper.style.color = "#605e5c";
        var title = document.createElement("div");
        title.style.fontWeight = "600";
        title.style.marginBottom = "8px";
        title.textContent = "Matter Health insight (Phase 4 placeholder)";
        wrapper.appendChild(title);
        var fact = function (k, v) {
            var row = document.createElement("div");
            row.style.lineHeight = "1.4";
            var key = document.createElement("strong");
            key.textContent = k + ": ";
            row.appendChild(key);
            var val = document.createElement("span");
            val.textContent = v;
            row.appendChild(val);
            return row;
        };
        wrapper.appendChild(fact("Topic", ns._topic));
        wrapper.appendChild(fact("Mode", ns._mode));
        wrapper.appendChild(fact("Subject", subject));
        wrapper.appendChild(fact("Playbook", ns._playbookName));
        wrapper.appendChild(fact("Stored envelope", envelope === null
            ? "(absent — pre-warm POST fires async per FR-18)"
            : "(present — FR-19 immediate-render path active)"));
        // FR-19: when an envelope exists, show its narrative preview inline.
        if (envelope && (envelope.narrative || envelope.body || envelope.tldr)) {
            var preview = document.createElement("div");
            preview.style.marginTop = "8px";
            preview.style.padding = "8px";
            preview.style.background = "#f3f2f1";
            preview.style.borderRadius = "2px";
            preview.style.color = "#323130";
            preview.style.whiteSpace = "pre-wrap";
            var text = envelope.tldr ||
                envelope.narrative ||
                envelope.body ||
                "(empty envelope)";
            preview.textContent = String(text).substring(0, 500);
            wrapper.appendChild(preview);
        }
        host.appendChild(wrapper);
    };
    // ===========================================================================
    // MOUNT — production path (bundle present)
    // ===========================================================================
    /**
     * Mount the React `InsightSummaryCard` into the host element with the
     * resolved props. FR-19 immediate-render is honoured via the
     * `initialEnvelope` prop (when present, the bundle initialises the card
     * state directly to `loaded` and skips the on-open-fetch gate).
     *
     * Returns the mount handle for unmount/update lifecycle (currently unused
     * — the form OnLoad is single-shot and the bundle owns subsequent renders).
     */
    ns._mountCard = function (host, bundle, subject, envelope, themeId) {
        /**
         * `onFetchInsight` adapter — the card calls this on first popover open
         * AND on manual refresh (force=true) per FR-20. We always go to the BFF
         * here because the `initialEnvelope` prop is the FR-19 path (it
         * pre-populates state without invoking the callback). When `force=true`
         * is passed (manual refresh), we propagate the `force=true` query param
         * per Task 034 contract.
         */
        var onFetchInsight = function (options) {
            return ns._invokeInsight(subject, options && options.force === true);
        };
        var props = __assign({ topic: ns._topic, subject: subject, mode: ns._mode, parameters: {}, initialEnvelope: envelope, onFetchInsight: onFetchInsight, onCitationClick: ns._onCitationClick }, (themeId !== undefined ? { themeId: themeId } : {}));
        return bundle.mountInsightSummaryCard(host, props);
    };
    // ===========================================================================
    // FORM EVENT HANDLER
    // ===========================================================================
    /**
     * OnLoad event handler — registered on the Matter main form (separately from
     * `Spaarke.MatterInsight.onLoad` which handles pre-warm).
     *
     * NFR-03 contract: this function returns synchronously after dispatching the
     * envelope read as a detached Promise chain + scheduling the React mount via
     * `requestAnimationFrame`. The handler does NOT await any work, so form TTI
     * is unaffected.
     *
     * Flow:
     *   1. Resolve record id from form context.
     *   2. If form is in Create mode (no record id), bail — nothing to render.
     *   3. Dispatch `Xrm.WebApi.retrieveRecord` for the stored envelope.
     *   4. On resolve: parse JSON → schedule React mount with `initialEnvelope`.
     *   5. On reject: render with `initialEnvelope = null` (the card falls
     *      through its idle → loading path when the user clicks the trigger).
     *
     * @param executionContext — execution context passed by the form runtime.
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext.getFormContext();
            var recordRef = formContext.data.entity.getEntityReference();
            if (!recordRef || !recordRef.id) {
                console.log("[Matter Insight Card] v" +
                    ns._version +
                    " onLoad: no record id (create mode). Skipping mount.");
                return;
            }
            var recordIdRaw = recordRef.id;
            var recordId = recordIdRaw.replace(/[{}]/g, "");
            var subject_1 = ns._subjectScheme + ":" + recordId;
            var select = "?$select=" + ns._summaryField;
            // Resolve theme id best-effort (ADR-021 — actual Fluent v9 theme mapping
            // happens inside the @spaarke/ai-widgets bundle; we just pass the id).
            var themeId_1;
            try {
                var ctx = Xrm.Utility.getGlobalContext();
                themeId_1 = ctx.userSettings.themeId;
            }
            catch (_e) {
                // Theme detection is best-effort; bundle defaults to webLightTheme.
                themeId_1 = undefined;
            }
            // Fire-and-forget Promise chain — handler returns immediately (NFR-03).
            Xrm.WebApi.retrieveRecord(ns._entityName, recordId, select).then(function (result) {
                var rawValue = (result && result[ns._summaryField]) || null;
                var envelope = ns._parseEnvelope(rawValue);
                console.log("[Matter Insight Card] v" +
                    ns._version +
                    " envelope read: " +
                    (envelope === null
                        ? "absent (will idle-render until user opens)"
                        : "present (FR-19 immediate-render)"));
                // Defer mount to next paint so we don't compete with MDA's own
                // form-render frame. NFR-03 — TTI unaffected.
                window.requestAnimationFrame(function () {
                    try {
                        var host = ns._resolveHost();
                        if (!host) {
                            console.warn("[Matter Insight Card] v" +
                                ns._version +
                                " host element '" +
                                ns._hostElementId +
                                "' not found on form. FormXml patch may not be deployed (Task 043).");
                            return;
                        }
                        var bundle = ns._resolveBundle();
                        if (!bundle) {
                            console.log("[Matter Insight Card] v" +
                                ns._version +
                                " @spaarke/ai-widgets bundle not loaded — rendering placeholder. " +
                                "Production bundle ships in Task 043.");
                            ns._renderPlaceholder(host, subject_1, envelope);
                            return;
                        }
                        var handle = ns._mountCard(host, bundle, subject_1, envelope, themeId_1);
                        // Stash handle so a future RESET / subject-change path can call
                        // `unmount` / `update` (not used in Task 042; reserved for r2+).
                        ns._mountHandle = handle;
                        console.log("[Matter Insight Card] v" +
                            ns._version +
                            " mounted. subject=" +
                            subject_1 +
                            ", topic=" +
                            ns._topic +
                            ", mode=" +
                            ns._mode +
                            ", envelope=" +
                            (envelope === null ? "absent" : "present") +
                            ", themeId=" +
                            (themeId_1 === undefined ? "(unset)" : themeId_1));
                    }
                    catch (mountErr) {
                        console.warn("[Matter Insight Card] v" +
                            ns._version +
                            " mount failed (form load unaffected):", mountErr);
                    }
                });
            }, function (error) {
                // Graceful — render with `initialEnvelope = null` so the user can
                // still click the trigger and fetch on demand. Log only (FR-17
                // graceful-handling pattern carried from Task 040).
                console.warn("[Matter Insight Card] v" +
                    ns._version +
                    " retrieveRecord failed (continuing without initial envelope): " +
                    (error && error.message ? error.message : String(error)));
                window.requestAnimationFrame(function () {
                    try {
                        var host = ns._resolveHost();
                        if (!host) {
                            return;
                        }
                        var bundle = ns._resolveBundle();
                        if (!bundle) {
                            ns._renderPlaceholder(host, subject_1, null);
                            return;
                        }
                        var handle = ns._mountCard(host, bundle, subject_1, null, themeId_1);
                        ns._mountHandle = handle;
                    }
                    catch (mountErr) {
                        console.warn("[Matter Insight Card] v" +
                            ns._version +
                            " mount-after-read-failure also failed (silent):", mountErr);
                    }
                });
            });
            console.log("[Matter Insight Card] v" +
                ns._version +
                " loaded. Envelope read dispatched (non-blocking).");
        }
        catch (error) {
            console.error("[Matter Insight Card] Error in onLoad (form load unaffected):", error);
        }
    };
})();
