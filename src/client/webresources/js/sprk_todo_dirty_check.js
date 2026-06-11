/**
 * Smart To Do — Cross-Frame Dirty-Check Listener
 *
 * Iframe-side JS Web Resource for the OOB MDA To Do main form
 * (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`). Smart To Do R4 task R4-041 (C).
 *
 * # Purpose
 *
 * The SmartTodo hybrid modal (`<RecordNavigationModalShell>` + iframe-embedded
 * OOB form, R4 task 040) renders the To Do main form inside an iframe. When the
 * user clicks the modal's `<` / `>` navigation chrome, the parent shell MUST
 * detect any unsaved changes on the iframe's form BEFORE swapping the iframe
 * `src` to the next record. This script is the iframe-side counterpart of the
 * dirty-check round-trip defined in spec FR-14.
 *
 * # Protocol
 *
 * Authored in `RecordNavigationModalShell/types.ts`:
 *
 *   Parent → iframe:
 *     { type: "request-dirty-check", correlationId: string }
 *
 *   Iframe → parent (this script's response):
 *     { type: "dirty-check-result", correlationId: string, dirty: boolean }
 *
 * # Round-trip flow
 *
 * 1. Parent posts `request-dirty-check` to the iframe's `contentWindow`.
 * 2. This script's `message` listener validates `event.origin` against the
 *    Spaarke allow-list (`https://*.dynamics.com` + same-origin).
 * 3. Computes dirty via `formContext.data.entity.getIsDirty()` (cached
 *    formContext from OnLoad, fallback to `Xrm.Page.data.entity.getIsDirty()`
 *    for deprecated-API parity).
 * 4. Responds via `event.source.postMessage(response, event.origin)` — echo
 *    origin per MDN security guidance (NEVER `"*"` for responses).
 *
 * # Origin allow-list (security)
 *
 * `event.origin` MUST match one of:
 *   - `https://*.dynamics.com` (any customer or MDA origin)
 *   - `window.location.origin` (same-origin embedding — Code Page → Code Page)
 *
 * Untrusted-origin requests are silently dropped — the parent shell's timeout
 * fallback treats no-response as clean (spec FR-14).
 *
 * # Form events to register
 *
 * 1. **OnLoad** — `Spaarke.SmartTodo.DirtyCheck.onLoad`
 *    (registers the `message` listener; pass execution context: Yes)
 *
 * The listener is global — only ONE handler is registered per iframe page-load.
 * On subsequent form re-loads (different record in same iframe), the cached
 * formContext is refreshed so dirty-state always reflects the current record.
 *
 * # Constraints (smart-todo-r4 spec)
 *
 * - FR-14: Cross-frame postMessage protocol with request/response correlation
 *   and origin allow-list. No `"*"` target-origin for responses.
 * - NFR-03: No hardcoded environment URLs / app IDs / GUIDs. Allow-list uses
 *   wildcard `https://*.dynamics.com` so the script works in every Spaarke
 *   tenant without per-env edits.
 * - NFR-04: Form-designer changes propagate to the SmartTodo hybrid modal
 *   iframe with no R4 source change. This handler is solution-portable
 *   (web resource) so the iframe re-renders with the new behavior on the
 *   next form load.
 * - NFR-07: Accessibility — the parent shell renders the WCAG 2.1 AA discard
 *   dialog; this script does NOT surface its own UI.
 * - NFR-09: Deployed via the SmartTodoWebResources solution wrapper (see
 *   `projects/smart-todo-r4/notes/c-dirty-check-bind-instructions.md`).
 *
 * # Version
 *
 * v1.0.0 — initial implementation (smart-todo-r4 task R4-041, 2026-06-11)
 *
 * @namespace Spaarke.SmartTodo.DirtyCheck
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = window.Spaarke || {};
Spaarke.SmartTodo = Spaarke.SmartTodo || {};
Spaarke.SmartTodo.DirtyCheck = Spaarke.SmartTodo.DirtyCheck || {};

(function (ns) {
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /** Version for diagnostic logging. */
    ns.VERSION = "1.0.0";

    /**
     * Message-type discriminators. MUST stay in lockstep with
     * `RecordNavigationModalShell/types.ts` (`DIRTY_CHECK_REQUEST_TYPE`,
     * `DIRTY_CHECK_RESULT_TYPE`).
     */
    var REQUEST_TYPE = "request-dirty-check";
    var RESULT_TYPE = "dirty-check-result";

    /**
     * Inbound-origin allow-list (Spaarke domains only). Patterns:
     *   - exact match (`https://contoso.crm.dynamics.com`)
     *   - subdomain wildcard (`https://*.dynamics.com` — requires at least
     *     one non-empty subdomain label before the suffix)
     *
     * Same-origin (the iframe's own `window.location.origin`) is also
     * accepted at runtime so same-origin Code-Page-in-Code-Page embeds work
     * without configuration.
     */
    var DEFAULT_ALLOWED_ORIGIN_PATTERNS = ["https://*.dynamics.com"];

    /**
     * Sentinel used to track listener registration. Prevents double-binding
     * the global `message` handler if `onLoad` fires more than once
     * (e.g., on form re-load inside the same iframe).
     */
    var LISTENER_INSTALLED = "__sprk_todo_dirty_check_listener__";

    /**
     * Sentinel for the cached formContext — refreshed on every OnLoad so the
     * listener always reflects the currently-loaded record. Stored on the
     * window because the listener closure outlives the form's React tree.
     */
    var FORM_CONTEXT_HOLDER = "__sprk_todo_dirty_check_formctx__";

    // -----------------------------------------------------------------------
    // OnLoad — register message listener + cache formContext
    // -----------------------------------------------------------------------

    /**
     * Form OnLoad handler. Registers the global `message` listener (idempotent;
     * only the FIRST OnLoad installs it) and refreshes the cached formContext
     * so subsequent dirty-checks reflect the current record.
     *
     * @param {object} executionContext - Form execution context (pass first param: Yes)
     */
    ns.onLoad = function (executionContext) {
        try {
            var formContext = executionContext && typeof executionContext.getFormContext === "function"
                ? executionContext.getFormContext()
                : null;
            if (!formContext || !formContext.data || !formContext.data.entity) {
                console.warn("[SmartTodo.DirtyCheck v" + ns.VERSION + "] onLoad — formContext unavailable, skipping");
                return;
            }

            // Refresh the cached formContext (per-load — new record may differ).
            window[FORM_CONTEXT_HOLDER] = formContext;

            // Install the message listener exactly once per iframe lifetime.
            if (!window[LISTENER_INSTALLED]) {
                window.addEventListener("message", ns._handleMessage, false);
                window[LISTENER_INSTALLED] = true;
                console.log("[SmartTodo.DirtyCheck v" + ns.VERSION + "] message listener installed");
            } else {
                console.log("[SmartTodo.DirtyCheck v" + ns.VERSION + "] formContext refreshed for new record");
            }
        } catch (err) {
            console.error("[SmartTodo.DirtyCheck v" + ns.VERSION + "] onLoad error:", err);
        }
    };

    // -----------------------------------------------------------------------
    // Message handler — validate, compute dirty, respond
    // -----------------------------------------------------------------------

    /**
     * Listens for `request-dirty-check` messages from the parent shell;
     * responds with `dirty-check-result` carrying the form's dirty flag.
     *
     * Defensive — NEVER throws into the host page; all failure paths log
     * and silently drop. The parent shell's timeout fallback recovers
     * (treats no-response as clean per spec FR-14).
     *
     * @param {MessageEvent} event - Browser MessageEvent
     */
    ns._handleMessage = function (event) {
        try {
            // 1. Validate payload shape (cheap, do first).
            var data = event && event.data;
            if (!data || typeof data !== "object") return;
            if (data.type !== REQUEST_TYPE) return;
            if (typeof data.correlationId !== "string" || data.correlationId.length === 0) return;

            // 2. Validate origin against the allow-list (security gate).
            if (!isOriginAllowed(event.origin)) {
                console.warn(
                    "[SmartTodo.DirtyCheck v" + ns.VERSION + "] Rejected dirty-check from untrusted origin:",
                    event.origin
                );
                return;
            }

            // 3. Compute dirty state. Prefer cached formContext (refreshed in
            //    OnLoad); fall back to deprecated `Xrm.Page` if cache is empty
            //    (e.g., if the form designer wired the listener without OnLoad).
            var dirty = computeDirtyState();

            // 4. Respond. Echo `event.origin` per MDN security guidance —
            //    never `"*"` for responses. `event.source` is the parent shell's
            //    Window reference.
            var response = {
                type: RESULT_TYPE,
                correlationId: data.correlationId,
                dirty: dirty
            };

            if (event.source && typeof event.source.postMessage === "function") {
                event.source.postMessage(response, event.origin);
            } else {
                console.warn(
                    "[SmartTodo.DirtyCheck v" + ns.VERSION + "] event.source.postMessage unavailable; cannot respond"
                );
            }
        } catch (err) {
            // NEVER block the host page — log and let the parent's timeout fire.
            console.error("[SmartTodo.DirtyCheck v" + ns.VERSION + "] _handleMessage error:", err);
        }
    };

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /**
     * Returns whether `origin` matches the allow-list (default patterns +
     * same-origin). Single leading `*.` subdomain wildcards are supported;
     * the wildcard requires at least one non-empty subdomain label.
     *
     * Exposed on the namespace for unit testing.
     */
    ns._isOriginAllowed = isOriginAllowed;

    function isOriginAllowed(origin) {
        if (!origin || typeof origin !== "string") return false;

        // Same-origin always allowed (covers same-origin Code Page embeds).
        try {
            if (window.location && window.location.origin && origin === window.location.origin) {
                return true;
            }
        } catch (_) {
            // location may be inaccessible in exotic sandboxes — fall through.
        }

        for (var i = 0; i < DEFAULT_ALLOWED_ORIGIN_PATTERNS.length; i++) {
            var pattern = DEFAULT_ALLOWED_ORIGIN_PATTERNS[i];
            if (pattern === origin) return true;

            // Wildcard subdomain pattern: "https://*.foo.com"
            var wildcardMatch = pattern.match(/^(https?:\/\/)\*\.(.+)$/);
            if (wildcardMatch) {
                var scheme = wildcardMatch[1];
                var suffix = wildcardMatch[2];
                if (origin.indexOf(scheme) !== 0) continue;
                var host = origin.substring(scheme.length);
                // Require at least one non-empty subdomain label before suffix.
                var dotSuffix = "." + suffix;
                if (
                    host.length > dotSuffix.length &&
                    host.substring(host.length - dotSuffix.length) === dotSuffix
                ) {
                    return true;
                }
            }
        }
        return false;
    }

    /**
     * Computes dirty state from the cached formContext, falling back to the
     * deprecated `Xrm.Page` API for parity with legacy form-script registrations.
     * Returns `false` on any failure (parent treats as clean → no prompt).
     *
     * Exposed on the namespace for unit testing.
     */
    ns._computeDirtyState = computeDirtyState;

    function computeDirtyState() {
        try {
            var ctx = window[FORM_CONTEXT_HOLDER];
            if (ctx && ctx.data && ctx.data.entity && typeof ctx.data.entity.getIsDirty === "function") {
                return Boolean(ctx.data.entity.getIsDirty());
            }
            // Legacy fallback — `Xrm.Page` is deprecated but still resolves to the
            // active form context in classic-MDA contexts. Wrapped in defensive
            // try because some sandboxes strip it.
            if (typeof Xrm !== "undefined" && Xrm.Page && Xrm.Page.data && Xrm.Page.data.entity &&
                typeof Xrm.Page.data.entity.getIsDirty === "function") {
                return Boolean(Xrm.Page.data.entity.getIsDirty());
            }
        } catch (err) {
            console.warn("[SmartTodo.DirtyCheck v" + ns.VERSION + "] computeDirtyState failed:", err);
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // Exports for test harnesses (no-op in MDA runtime)
    // -----------------------------------------------------------------------

    if (typeof module !== "undefined" && module.exports) {
        module.exports = {
            onLoad: ns.onLoad,
            _handleMessage: ns._handleMessage,
            _internals: {
                isOriginAllowed: isOriginAllowed,
                computeDirtyState: computeDirtyState,
                REQUEST_TYPE: REQUEST_TYPE,
                RESULT_TYPE: RESULT_TYPE,
                LISTENER_INSTALLED: LISTENER_INSTALLED,
                FORM_CONTEXT_HOLDER: FORM_CONTEXT_HOLDER
            },
            VERSION: ns.VERSION
        };
    }
})(Spaarke.SmartTodo.DirtyCheck);
