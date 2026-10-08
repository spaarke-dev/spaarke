/**
 * Project / Matter / Work Assignment main forms - access-status banner (unified-access-control-r2 task 153; owner
 * round 3b, O1 final + round 83 item 11 "BOTH": this red text banner, plus the clickable indicator in TrackingFieldTrio)
 *
 * Shows every reader of the record, in the standard form notification bar, that the record is SECURE and/or under a No
 * Access restriction. Text only (setFormNotification, documented text-only; never the undocumented fourth parameter,
 * never Xrm.App.addGlobalNotification). Manage Access is opened from the tracking panel, not from here.
 *
 * Web Resource Name: sprk_/scripts/accessstatus_banner.js
 *
 * Form Events (sprk_project, sprk_matter and sprk_workassignment MAIN forms only, not quick-create):
 * - OnLoad: Spaarke.AccessStatus.onLoad (pass execution context). It evaluates once and registers, once per form
 *   load, a data OnLoad handler (a data refresh, e.g. the Access ribbon's Make Secure / Remove Secure / Update Access,
 *   which call formContext.data.refresh(false)) and an OnPostSave handler.
 * - Library order: sprk_/scripts/bff_auth.js FIRST, then this library.
 *
 * The answer: task 064's per-record read, GET /api/v1/records/{sprk_project|sprk_matter|sprk_workassignment}/{id}/
 * no-access (frozen contract: projects/unified-access-control-r2/notes/phase4-access-report-contract.md). Any reader
 * of the record may call it (Read-gated as the caller over OBO; no Read or no such record is a uniform 404). This
 * script uses only `recordId`, `secure` and `noAccess`; it never shows an entry, a count, a name or a reason.
 *
 * FAIL CLOSED (ADR-003; the absence of a banner is itself a claim):
 * - `applies` shows that signal's red (ERROR) notification; `doesNotApply` shows nothing for it.
 * - ANY `unknown`, a missing or unrecognised signal, a non-200 (404 included), an unparseable body, an answer naming
 *   another record, a missing Spaarke.BffAuth, an unresolved BFF URL, a null token or null Response from
 *   Spaarke.BffAuth.authenticatedFetch, or a thrown call shows ONLY the neutral (INFO) "Access status unavailable"
 *   notification. Nothing anywhere says "not secure" or "not restricted".
 * - Unlike sprk_kpiassessment_quickcreate.js, a missing helper or token is NOT skipped silently.
 *
 * A re-evaluation of the record already shown on THIS form (after a save or a data refresh) keeps the current
 * notifications until its answer replaces them, so a save never opens a "no restriction" gap; a different record, or
 * none, clears them first. A late answer for a record the form has left, or older than a newer evaluation on the same
 * form, is dropped. A request that does not answer within Config.timeoutMs is "unavailable". A create form (no record
 * id) makes no call. Never throws: the form always loads and saves.
 *
 * Task 174 (owner rounds 82/84): for a filed work assignment or project the Secure signal should be the parent-derived
 * value. 064's `secure` is the record's own flag until 174 exposes the effective one; `signalsOf` is the ONE place
 * this script reads it (its twin in the PCF is parseAccessStatusResponse in TrackingFieldTrio/accessStatus.ts).
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.AccessStatus = Spaarke.AccessStatus || {};

Spaarke.AccessStatus.Config = {
    /** @type {string|null} Resolved BFF base URL (sprk_BffApiBaseUrl environment variable). */
    apiBaseUrl: null,
    /** A request that has not answered by then shows "unavailable" (a hung call must not leave the form silent). */
    timeoutMs: 20000,
    version: "1.0.0"
};

/** The tables 064's route answers for (the route segment is the table's logical name). */
Spaarke.AccessStatus.Tables = {
    sprk_project: true,
    sprk_matter: true,
    sprk_workassignment: true
};

/** The notification ids this script owns (fixed; cleared on every evaluation). */
Spaarke.AccessStatus.Ids = {
    secure: "sprk_access_secure",
    noAccess: "sprk_access_noaccess",
    unavailable: "sprk_access_unavailable"
};

/**
 * Where the banner tells managers Manage Access is opened from. OWNER CHANGE POINT (a), pending: to point at the new
 * indicator instead, set this one line to e.g. "the red Secure / No Access marker in the tracking panel".
 */
Spaarke.AccessStatus.ManageAccessFrom = "the person icon in the tracking panel";

/** The closed copy (task 153 POML "banner copy"; implement the owner's revision verbatim, compose no other strings). */
Spaarke.AccessStatus.Text = {
    secure: "SECURE RECORD — only people given access explicitly can see this record. People who can manage access " +
        "see and change who has access in Manage Access (" + Spaarke.AccessStatus.ManageAccessFrom + ").",
    noAccess: "NO ACCESS RESTRICTION — named people or organizations are blocked from this record. People who can " +
        "manage access see the list in Manage Access › No Access (" + Spaarke.AccessStatus.ManageAccessFrom + ").",
    unavailable: "Access status unavailable — whether this record is secure or under a No Access restriction could " +
        "not be checked. Do not assume it is unrestricted; reload the form to try again."
};

Spaarke.AccessStatus._cachedApiBaseUrl = null;

/**
 * Per-FORM evaluation state, keyed by the form context: { seq, lastKey }. `seq` numbers the form's evaluations (an older
 * answer is dropped); `lastKey` is the record ("table:id") whose answer the form shows now. Per form, not per library or
 * per record: two forms can host this library at once (a record opened in a dialog over another form, possibly the
 * same record), and one form's evaluation must never drop or overwrite the other's. Kept if the library loads again.
 */
Spaarke.AccessStatus._forms = Spaarke.AccessStatus._forms || (typeof WeakMap === "function" ? new WeakMap() : null);

/** Fallback when no per-form state is available (no WeakMap, or no object form context): a counter per record. */
Spaarke.AccessStatus._seqByRecord = Spaarke.AccessStatus._seqByRecord || {};

/** The form's evaluation state, or null when it cannot be kept (the caller then clears up front, as before). */
Spaarke.AccessStatus._stateOf = function (formContext) {
    var forms = Spaarke.AccessStatus._forms;
    if (!forms || !formContext || typeof formContext !== "object") {
        return null;
    }

    var state = forms.get(formContext);
    if (!state) {
        state = { seq: 0, lastKey: null };
        forms.set(formContext, state);
    }

    return state;
};

/** Resolves with the promise's value, or with `fallback` after `ms` (never rejects on the timer). */
Spaarke.AccessStatus._within = function (promise, ms, fallback, onTimeout) {
    return new Promise(function (resolve, reject) {
        var timer = setTimeout(function () {
            try { if (onTimeout) { onTimeout(); } } catch (e) { /* ignore */ }
            resolve(fallback);
        }, ms);
        promise.then(function (value) {
            clearTimeout(timer);
            resolve(value);
        }, function (error) {
            clearTimeout(timer);
            reject(error);
        });
    });
};

/** Resolves the BFF base URL from the sprk_BffApiBaseUrl environment variable (value override, else default). */
Spaarke.AccessStatus.getApiBaseUrl = function () {
    if (Spaarke.AccessStatus._cachedApiBaseUrl) {
        return Promise.resolve(Spaarke.AccessStatus._cachedApiBaseUrl);
    }

    return Xrm.WebApi.retrieveMultipleRecords(
        "environmentvariabledefinition",
        "?$filter=schemaname eq 'sprk_BffApiBaseUrl'&$select=environmentvariabledefinitionid,defaultvalue"
    ).then(function (definitions) {
        if (!definitions.entities || definitions.entities.length === 0) {
            throw new Error("Environment variable sprk_BffApiBaseUrl is not defined.");
        }

        var definition = definitions.entities[0];
        return Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariablevalue",
            "?$filter=_environmentvariabledefinitionid_value eq '" + definition.environmentvariabledefinitionid +
            "'&$select=value"
        ).then(function (values) {
            var value = values.entities && values.entities.length > 0 ? values.entities[0].value : definition.defaultvalue;
            if (!value) {
                throw new Error("Environment variable sprk_BffApiBaseUrl has no value.");
            }

            Spaarke.AccessStatus._cachedApiBaseUrl = value.replace(/\/+$/, "");
            return Spaarke.AccessStatus._cachedApiBaseUrl;
        });
    });
};

/** A GUID in the one comparable spelling (ADR-044): lowercase, no braces, no surrounding space. */
Spaarke.AccessStatus._canonical = function (id) {
    return typeof id === "string" ? id.replace(/[{}]/g, "").trim().toLowerCase() : "";
};

/** The record a form shows - or null for a create form, an unsaved record or a table the route does not answer for. */
Spaarke.AccessStatus.recordOf = function (formContext) {
    var entity = formContext && formContext.data && formContext.data.entity;
    if (!entity) {
        return null;
    }

    var table = entity.getEntityName();
    var recordId = Spaarke.AccessStatus._canonical(entity.getId() || "");
    return Spaarke.AccessStatus.Tables[table] === true && recordId ? { table: table, recordId: recordId } : null;
};

/** One signal as this script trusts it: a value the contract does not define is "unknown". */
Spaarke.AccessStatus._state = function (value) {
    return value === "applies" || value === "doesNotApply" || value === "unknown" ? value : "unknown";
};

/**
 * The two signals of a 200 body, or null when the body cannot be trusted (not an object, or it does not name THIS
 * record). See the task 174 note in the header: this is the one place the Secure signal is read.
 */
Spaarke.AccessStatus.signalsOf = function (body, recordId) {
    if (!body || typeof body !== "object") {
        return null;
    }

    if (typeof body.recordId !== "string" ||
        Spaarke.AccessStatus._canonical(body.recordId) !== Spaarke.AccessStatus._canonical(recordId)) {
        return null;
    }

    return {
        secure: Spaarke.AccessStatus._state(body.secure),
        noAccess: Spaarke.AccessStatus._state(body.noAccess)
    };
};

/**
 * Asks 064's route about one record. Resolves (never rejects) with the signals, or null for every answer that cannot
 * be trusted - including a missing helper, no BFF URL, no token and a null Response.
 */
Spaarke.AccessStatus.fetchSignals = function (record) {
    var controller = typeof AbortController === "function" ? new AbortController() : null;
    var timedOut = {};
    return Spaarke.AccessStatus._within(
        Spaarke.AccessStatus._fetchSignals(record, controller ? controller.signal : undefined),
        Spaarke.AccessStatus.Config.timeoutMs,
        timedOut,
        function () { if (controller) { controller.abort(); } }
    ).then(function (result) {
        if (result === timedOut) {
            console.warn("[Access Status] The access status route did not answer in time; shown as unavailable.");
            return null;
        }
        return result;
    }, function () {
        return null;
    });
};

/** The request itself (fetchSignals bounds it in time). Resolves (never rejects) with the signals or null. */
Spaarke.AccessStatus._fetchSignals = async function (record, signal) {
    try {
        if (typeof Spaarke.BffAuth === "undefined" || typeof Spaarke.BffAuth.authenticatedFetch !== "function") {
            console.error("[Access Status] Spaarke.BffAuth is not loaded (register sprk_/scripts/bff_auth.js first); " +
                "the access status is shown as unavailable.");
            return null;
        }

        var baseUrl = Spaarke.AccessStatus.Config.apiBaseUrl || await Spaarke.AccessStatus.getApiBaseUrl();
        if (!baseUrl) {
            return null;
        }

        var url = baseUrl + "/api/v1/records/" + record.table + "/" + encodeURIComponent(record.recordId) + "/no-access";
        var options = { method: "GET", headers: { "Accept": "application/json" } };
        if (signal) {
            options.signal = signal;
        }

        var response = await Spaarke.BffAuth.authenticatedFetch(url, options, baseUrl);

        if (!response) {
            console.warn("[Access Status] No BFF token could be acquired; the access status is shown as unavailable.");
            return null;
        }

        if (!response.ok) {
            console.warn("[Access Status] The access status route answered " + response.status + ".");
            return null;
        }

        var body = null;
        try {
            body = await response.json();
        } catch (parseError) {
            return null;
        }

        return Spaarke.AccessStatus.signalsOf(body, record.recordId);
    } catch (error) {
        console.error("[Access Status] The access status could not be read.", error);
        return null;
    }
};

/** Clears every notification this script owns. Never throws. */
Spaarke.AccessStatus._clear = function (formContext) {
    var ids = Spaarke.AccessStatus.Ids;
    [ids.secure, ids.noAccess, ids.unavailable].forEach(function (id) {
        try {
            formContext.ui.clearFormNotification(id);
        } catch (error) {
            /* never break the form */
        }
    });
};

Spaarke.AccessStatus._show = function (formContext, text, level, id) {
    try {
        formContext.ui.setFormNotification(text, level, id);
    } catch (error) {
        console.error("[Access Status] Could not show the notification:", error);
    }
};

/**
 * Shows one result: the red notification of each signal that applies, or ONLY the unavailable notice when the result
 * is missing or any signal is unknown. Every id not shown is cleared; an id shown again is replaced in place (no gap).
 */
Spaarke.AccessStatus.render = function (formContext, signals) {
    var ids = Spaarke.AccessStatus.Ids;
    var text = Spaarke.AccessStatus.Text;
    var show = [];

    if (!signals || signals.secure === "unknown" || signals.noAccess === "unknown") {
        show.push([text.unavailable, "INFO", ids.unavailable]);
    } else {
        if (signals.secure === "applies") {
            show.push([text.secure, "ERROR", ids.secure]);
        }
        if (signals.noAccess === "applies") {
            show.push([text.noAccess, "ERROR", ids.noAccess]);
        }
    }

    var shown = show.map(function (n) { return n[2]; });
    [ids.secure, ids.noAccess, ids.unavailable].forEach(function (id) {
        if (shown.indexOf(id) < 0) {
            try {
                formContext.ui.clearFormNotification(id);
            } catch (error) {
                /* never break the form */
            }
        }
    });
    show.forEach(function (n) {
        Spaarke.AccessStatus._show(formContext, n[0], n[1], n[2]);
    });
};

/** One evaluation of the form's record. Resolves when it has rendered (or was dropped). Never throws. */
Spaarke.AccessStatus.evaluate = async function (formContext) {
    try {
        var record = Spaarke.AccessStatus.recordOf(formContext);
        var key = record ? record.table + ":" + record.recordId : null;
        var state = Spaarke.AccessStatus._stateOf(formContext);

        // Keep what the form shows only while re-evaluating the SAME record on THIS form (a save, a data refresh), so a
        // save never opens a "no restriction" gap. Anything else - no record, another record, no per-form state - clears
        // first, so one record's banner is never shown on another.
        if (!record || !state || state.lastKey !== key) {
            Spaarke.AccessStatus._clear(formContext);
            if (state) {
                state.lastKey = null;
            }
        }

        if (!record) {
            return; // a create form or an unsaved record: nothing to ask about
        }

        var seq;
        if (state) {
            seq = ++state.seq;
        } else {
            seq = (Spaarke.AccessStatus._seqByRecord[key] || 0) + 1;
            Spaarke.AccessStatus._seqByRecord[key] = seq;
        }

        var signals = await Spaarke.AccessStatus.fetchSignals(record);

        // Drop an answer older than a newer evaluation on this form, or for a record the form has left.
        if (state ? seq !== state.seq : seq !== Spaarke.AccessStatus._seqByRecord[key]) {
            return;
        }

        var now = Spaarke.AccessStatus.recordOf(formContext);
        if (!now || now.table !== record.table || now.recordId !== record.recordId) {
            return;
        }

        Spaarke.AccessStatus.render(formContext, signals);
        if (state) {
            state.lastKey = key;
        }
    } catch (error) {
        console.error("[Access Status] Error while evaluating:", error);
    }
};

/** Data OnLoad (after a data refresh). Never throws. */
Spaarke.AccessStatus.onDataLoad = function (executionContext) {
    try {
        void Spaarke.AccessStatus.evaluate(executionContext.getFormContext());
    } catch (error) {
        console.error("[Access Status] Error in onDataLoad:", error);
    }
};

/** OnPostSave. Never throws, never blocks the save. */
Spaarke.AccessStatus.onPostSave = function (executionContext) {
    try {
        void Spaarke.AccessStatus.evaluate(executionContext.getFormContext());
    } catch (error) {
        console.error("[Access Status] Error in onPostSave:", error);
    }
};

/** Form OnLoad: registers the two re-evaluation handlers (once per form load) and evaluates. Never throws. */
Spaarke.AccessStatus.onLoad = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();

        try {
            // Remove first, so a handler is never registered twice on the same form context.
            formContext.data.removeOnLoad(Spaarke.AccessStatus.onDataLoad);
            formContext.data.addOnLoad(Spaarke.AccessStatus.onDataLoad);
        } catch (error) {
            console.error("[Access Status] Could not register the data OnLoad handler:", error);
        }

        try {
            formContext.data.entity.removeOnPostSave(Spaarke.AccessStatus.onPostSave);
            formContext.data.entity.addOnPostSave(Spaarke.AccessStatus.onPostSave);
        } catch (error) {
            console.error("[Access Status] Could not register the OnPostSave handler:", error);
        }

        void Spaarke.AccessStatus.evaluate(formContext);
    } catch (error) {
        console.error("[Access Status] Error in onLoad:", error);
    }
};

/* eslint-enable no-undef */
