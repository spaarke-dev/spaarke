"use strict";
/**
 * Matter Insight Widget — Form OnLoad Handler
 *
 * Web Resource Name: sprk_/scripts/matter_insight_onload.js
 *
 * Registered on the Matter main form OnLoad event. Coexists with the
 * pre-existing `Spaarke.MatterKpi.onLoad` handler (KPI subgrid refresh,
 * from `matter-performance-KPI-r1`). Both handlers run on the same form.
 *
 * Purpose (FR-17 / FR-18, spec.md §Requirements):
 *   1. Read `sprk_performancesummary` via Xrm.WebApi (non-blocking).
 *   2. Attempt JSON.parse of the field content.
 *   3. If parse fails (legacy R5 placeholder text) → treat as "no stored summary".
 *   4. If parse succeeds and `.generatedAt` is older than 1 hour → mark stale.
 *   5. Hand off the decision (no-summary | fresh | stale) to the pre-warm trigger
 *      implemented in Task 041 (`Spaarke.MatterInsight._firePrewarm`).
 *
 * Critical constraints:
 *   - NFR-03: must not block form TTI. All async work runs detached from
 *     the synchronous OnLoad return path.
 *   - Q-U6: this is the Power Apps form OnLoad mechanism — NOT Power Automate,
 *     NOT a React Code Page wrapper.
 *   - Q-U1: no `@v1`/`@vN` identifier-suffix vernacular. Version tracked via
 *     `Spaarke.MatterInsight._version` string.
 *
 * Source layout:
 *   - Source: src/dataverse/forms/sprk_matter/insightWidgetOnLoad.ts
 *   - Compiled web resource: src/dataverse/forms/sprk_matter/insightWidgetOnLoad.js
 *
 * Form Events:
 *   Event: OnLoad
 *   Library: sprk_/scripts/matter_insight_onload.js
 *   Function: Spaarke.MatterInsight.onLoad
 *   Pass execution context: Yes
 *
 * Task 041 will append the fire-and-forget POST logic by replacing the body
 * of `Spaarke.MatterInsight._firePrewarm`.
 */
// =============================================================================
// NAMESPACE
// =============================================================================
(function () {
    if (typeof window === "undefined") {
        return;
    }
    var w = window;
    w.Spaarke = w.Spaarke || {};
    w.Spaarke.MatterInsight = w.Spaarke.MatterInsight || {};
    var ns = w.Spaarke.MatterInsight;
    // ===========================================================================
    // CONFIGURATION
    // ===========================================================================
    /** Handler version (informational; bumped per task). */
    ns._version = "0.2.0";
    /** Field name read on OnLoad (spec FR-17). */
    ns._summaryField = "sprk_performancesummary";
    /** Entity logical name (spec scope: Matter Health single-mode). */
    ns._entityName = "sprk_matter";
    /** Staleness threshold in minutes (spec FR-18 default — overridable by topic registry per FR-21). */
    ns._stalenessThresholdMinutes = 60;
    /** Pre-warm endpoint (spec FR-18). Same-origin relative path — form host serves under the Power Apps domain; the BFF is configured behind a reverse-proxy / CORS allow-list so this path resolves. */
    ns._prewarmEndpoint = "/api/insights/ask";
    /** Topic identifier (spec scope: Matter Health). Aligned with `sprk_aitopicregistry.sprk_topicname`. */
    ns._topic = "matter-health";
    /** Mode identifier (spec scope: single-mode). Aligned with `sprk_aitopicregistry.sprk_mode`. */
    ns._mode = "single";
    /**
     * Canonical playbook name resolved from topic+mode via `sprk_aitopicregistry` row at deploy time.
     * Per Task 042 wire-shape resolution (Option b, 2026-06-11): BFF `InsightEndpoints.Ask` accepts
     * `question` as a canonical playbook name (resolved via `InsightsPlaybookNameMapOptions.ResolveOrDefault`),
     * NOT topic+mode. Client must send `{ question: <playbookName>, subject, parameters }`.
     * Earlier draft sent `{ topic, mode, ... }` and got 400 ProblemDetails (Task 041 P1 fix applied 2026-06-11).
     */
    ns._playbookName = "matter-health-single";
    /** Subject scheme prefix per r2 multi-entity subject scheme. r1 uses `matter:` only. */
    ns._subjectScheme = "matter";
    // ===========================================================================
    // PARSING + STALENESS
    // ===========================================================================
    /**
     * Parse the raw `sprk_performancesummary` content as a JSON envelope.
     * Per FR-17: non-JSON content (legacy R5 placeholder text) is gracefully
     * treated as "no stored summary".
     */
    ns._parseEnvelope = function (raw) {
        if (raw === null || raw === undefined || raw === "") {
            return null;
        }
        try {
            var parsed = JSON.parse(raw);
            // Defensive: must be a JSON object, not a JSON primitive (string/number).
            if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
                return null;
            }
            return parsed;
        }
        catch (_e) {
            // Legacy R5 placeholder text — treat as no stored summary.
            return null;
        }
    };
    /**
     * Compute prewarm decision from a parsed envelope (or null if unparseable / absent).
     * Per FR-18: stored summary >1 hour old OR absent → prewarm. Fresh → skip.
     */
    ns._computeDecision = function (envelope, rawValue) {
        if (envelope === null) {
            // Absent vs unparseable disambiguation (both trigger prewarm but inform telemetry).
            var status_1 = rawValue === null || rawValue === undefined || rawValue === "" ? "absent" : "unparseable";
            return { status: status_1, envelope: null, ageMinutes: null, rawValue: rawValue };
        }
        var generatedAt = envelope.generatedAt;
        if (typeof generatedAt !== "string" || generatedAt === "") {
            // Envelope object exists but has no timestamp — treat as stale (force refresh).
            return { status: "stale", envelope: envelope, ageMinutes: null, rawValue: rawValue };
        }
        var generatedMs = Date.parse(generatedAt);
        if (isNaN(generatedMs)) {
            return { status: "stale", envelope: envelope, ageMinutes: null, rawValue: rawValue };
        }
        var ageMinutes = (Date.now() - generatedMs) / 60000;
        var status = ageMinutes > ns._stalenessThresholdMinutes ? "stale" : "fresh";
        return { status: status, envelope: envelope, ageMinutes: ageMinutes, rawValue: rawValue };
    };
    // ===========================================================================
    // PRE-WARM HOOK (Task 041 will implement the POST body here)
    // ===========================================================================
    /**
     * Fire-and-forget pre-warm invocation per FR-18 / FR-19.
     *
     * Behavior:
     *   - When `decision.status` is `stale` | `absent` | `unparseable` → POST to
     *     `/api/insights/ask` with body `{ topic, mode, subject, parameters? }`.
     *     The POST is dispatched as a detached Promise — the function returns
     *     synchronously so NFR-03 (form TTI unaffected) is preserved.
     *   - When `decision.status` is `fresh` → no POST is fired (the stored
     *     envelope is within the staleness window; the card renders it directly).
     *
     * Critical invariants:
     *   - NEVER `await` the fetch (FR-18). The Promise chain is detached and the
     *     handler returns immediately.
     *   - Rejection handling is **silent**: errors are logged via `console.warn`
     *     only; nothing is surfaced to the user (form load must NOT show error
     *     dialogs from this hook).
     *   - The Promise is consumed by `.then(...)` (success swallowed) + `.catch(...)`
     *     (rejection swallowed) so the runtime does NOT emit "Uncaught (in promise)"
     *     warnings.
     *   - `credentials: "include"` carries the Power Apps session cookie context so
     *     the BFF can extract the authenticated principal (per ADR-028 — clients
     *     authenticate via the standard Spaarke Auth surface; this is a same-origin
     *     POST from the form host).
     *
     * Subject scheme (r2 multi-entity convention): `matter:{guid-without-braces}`.
     *
     * @param formContext — Power Apps form context (for record id + entity ref).
     * @param decision    — Parsed envelope + staleness decision from OnLoad.
     */
    ns._firePrewarm = function (formContext, decision) {
        try {
            // Fresh envelope → nothing to pre-warm. Log for observability and return.
            if (decision.status === "fresh") {
                console.log("[Matter Insight] v" + ns._version + " prewarm skipped (fresh envelope; ageMinutes=" +
                    (decision.ageMinutes === null ? "n/a" : decision.ageMinutes.toFixed(1)) + ").");
                return;
            }
            // Resolve record id for the subject ref.
            var recordRef = formContext.data.entity.getEntityReference();
            if (!recordRef || !recordRef.id) {
                // No record id (Create mode) — cannot build a subject ref. Skip silently.
                console.log("[Matter Insight] v" + ns._version + " prewarm skipped (no record id).");
                return;
            }
            var recordId = recordRef.id.replace(/[{}]/g, "");
            var subject_1 = ns._subjectScheme + ":" + recordId;
            var body = {
                question: ns._playbookName,
                subject: subject_1,
                parameters: {}
            };
            var requestInit = {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(body),
                credentials: "include",
                // Hint to the browser that this is a low-priority pre-warm fetch.
                keepalive: true
            };
            // FIRE-AND-FORGET (FR-18): NO `await`. The Promise is detached.
            // Both `.then` and `.catch` swallow their result so the runtime never
            // emits "Uncaught (in promise)" and nothing surfaces to the user.
            var pending = fetch(ns._prewarmEndpoint, requestInit);
            pending.then(function (response) {
                // Log only — we do NOT read the body (the card UI does its own GET).
                // Per FR-19, the existing stored envelope renders immediately while
                // this background invocation runs; the card refreshes when ready.
                console.log("[Matter Insight] v" + ns._version + " prewarm dispatched. status=" +
                    decision.status + ", httpStatus=" + response.status + ", subject=" + subject_1);
            }, function (error) {
                // Rejection path #1 — handled inside .then's onRejected slot.
                // Log only; never surface to user.
                var msg = error && error.message
                    ? error.message
                    : String(error);
                console.warn("[Matter Insight] v" + ns._version + " prewarm fetch failed (silent): " + msg);
            }).catch(function (error) {
                // Rejection path #2 — defensive .catch to ensure the runtime never
                // emits "Uncaught (in promise)" even if the .then handlers themselves
                // throw. Log only; never surface to user.
                var msg = error && error.message
                    ? error.message
                    : String(error);
                console.warn("[Matter Insight] v" + ns._version + " prewarm tail catch (silent): " + msg);
            });
            console.log("[Matter Insight] v" + ns._version + " prewarm POST fired (non-blocking). status=" +
                decision.status + ", playbook=" + ns._playbookName + ", subject=" + subject_1);
        }
        catch (e) {
            // Synchronous failure (e.g., JSON.stringify threw, fetch unavailable) —
            // log only. Per NFR-03 the form must never break because of this hook.
            console.warn("[Matter Insight] v" + ns._version + " prewarm hook failed (silent):", e);
        }
    };
    // ===========================================================================
    // FORM EVENT HANDLER
    // ===========================================================================
    /**
     * OnLoad event handler — registered on the Matter main form.
     *
     * NFR-03 contract: this function returns synchronously after dispatching
     * the field read as a detached Promise chain. The handler does NOT await
     * the Xrm.WebApi response, so form TTI is unaffected.
     *
     * Flow:
     *   1. Resolve record id from form context.
     *   2. If form is in Create mode (no record id), bail — nothing to read.
     *   3. Dispatch `Xrm.WebApi.retrieveRecord` for `sprk_performancesummary`
     *      (Promise chained — NOT awaited).
     *   4. On resolve: parse JSON, compute staleness, hand off to `_firePrewarm`.
     *   5. On reject: log only; do not surface error to the user.
     *
     * @param executionContext — execution context passed by the form runtime.
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext_1 = executionContext.getFormContext();
            var recordRef = formContext_1.data.entity.getEntityReference();
            if (!recordRef || !recordRef.id) {
                // Create mode — no record id yet. Nothing to pre-warm.
                console.log("[Matter Insight] v" + ns._version + " onLoad: no record id (create mode). Skipping.");
                return;
            }
            var recordIdRaw = recordRef.id;
            // Xrm returns id wrapped in `{}` — strip per Web API convention.
            var recordId = recordIdRaw.replace(/[{}]/g, "");
            var select = "?$select=" + ns._summaryField;
            // Fire-and-forget Promise chain — handler returns immediately.
            Xrm.WebApi.retrieveRecord(ns._entityName, recordId, select).then(function (result) {
                var rawValue = (result && result[ns._summaryField]) || null;
                var envelope = ns._parseEnvelope(rawValue);
                var decision = ns._computeDecision(envelope, rawValue);
                console.log("[Matter Insight] v" +
                    ns._version +
                    " onLoad decision: status=" +
                    decision.status +
                    ", ageMinutes=" +
                    (decision.ageMinutes === null ? "n/a" : decision.ageMinutes.toFixed(1)));
                // Hand off to Task 041's prewarm logic.
                ns._firePrewarm(formContext_1, decision);
            }, function (error) {
                // Per FR-17 graceful handling: log only, never surface to user.
                console.warn("[Matter Insight] v" +
                    ns._version +
                    " retrieveRecord failed (continuing without prewarm): " +
                    (error && error.message ? error.message : String(error)));
            });
            console.log("[Matter Insight] v" + ns._version + " loaded. Read dispatched (non-blocking).");
        }
        catch (error) {
            // Catch-all so the handler never breaks form load.
            console.error("[Matter Insight] Error in onLoad (form load unaffected):", error);
        }
    };
})();
