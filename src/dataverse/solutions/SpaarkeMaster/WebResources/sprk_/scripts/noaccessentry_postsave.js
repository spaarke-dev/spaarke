/**
 * No Access Entry main form library (unified-access-control-r2 tasks 143 and 154)
 *
 * The ONE form library on the sprk_noaccessentry main form (after sprk_/scripts/bff_auth.js).
 *
 * Task 143 - post-save enforcement: after an entry is saved, asks the BFF to enforce it NOW, so the direct shares it walls
 * off on secure records are removed within seconds (owner round 3 R3). The 5-minute NoAccessShareReconciliationJob is the
 * safety net for anything this call does not reach.
 *
 * Task 154 - a usable entry form (owner round 3b; reduced by owner round 59):
 * - OnSave shape check (a PREVIEW, write-path rule WP-2): exactly one subject (contact, organization or user) and exactly
 *   one object (an organization, or a record type AND record id). A half pair, a non-record id and an object type the
 *   deny readers do not evaluate are refused with a form notification, and the save is prevented. The server-side owners
 *   of the shape are the BFF reader's and enforcer's malformed-entry rule (NoAccessListReader.TryParseObjectRecordId /
 *   SubjectKindOf); see docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md section 5.
 * - The record id is NORMALISED to the canonical lowercase form without braces before it is saved. The readers match the
 *   id by string equality, so a braced id walled nothing (batch-5 review, side finding 4).
 * - Record picker (Spaarke.NoAccessEntry.Picker, self-contained and replaceable): choosing an Object Record Type
 *   (filtered to project, matter and work assignment) opens the platform's
 *   lookup dialog for that table, the same dialog the shared PolymorphicPicker uses. The picked id is written to
 *   sprk_objectrecordid. No typed GUID is needed.
 * - Exactly one object survives: choosing an organization clears the record pair; choosing a record clears the organization.
 * - Name suggestion: fills sprk_name ("{subject} - {object}") only while it is empty or still holds the last suggestion.
 *
 * Web Resource Name: sprk_/scripts/noaccessentry_postsave.js
 *
 * Form Events (registered by task 154's scripts/Deploy-NoAccessEntryForms.ps1):
 * - OnLoad: Spaarke.NoAccessEntry.onLoad (pass execution context). It registers OnSave, OnPostSave and every OnChange,
 *   each exactly once (Unified Interface fires OnLoad again after a save).
 * - A stored record id the access checks do not match (braces, a leading space, blank) is flagged on load. An EXISTING
 *   entry is never written on load (autosave would make the viewer its author for enforcement, owner N5).
 * - Library order: sprk_/scripts/bff_auth.js FIRST, then this library.
 *
 * What the user sees after a save (text-only form notifications - owner O1 final):
 * - "not enforced ... you do not hold Write on ..." with the records concerned (owner N5);
 * - "access through a team or a role cannot be removed per person ..." (owner N2) - Teams/SPA hides the record;
 * - "kept: the last person who can open ..." when a removal would leave a secure record with nobody (owner S5);
 * - the server's own message when enforcement could not be completed. The job retries within 5 minutes.
 *
 * Constraints:
 * - addOnPostSave (not addOnSave) for enforcement: the record is saved first, and the save is never blocked by that call.
 * - The enforce request carries ONLY the entry id; the server reads everything else from the entry.
 * - Every handler catches its own errors; the form never breaks.
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.NoAccessEntry = Spaarke.NoAccessEntry || {};

Spaarke.NoAccessEntry.Config = {
    /** @type {string|null} Resolved BFF base URL (sprk_BffApiBaseUrl environment variable). */
    apiBaseUrl: null,
    enforcePath: "/api/v1/external-access/no-access/enforce",
    notificationId: "sprk_noaccess_enforcement",
    shapeNotificationId: "sprk_noaccess_shape",
    objectNotificationId: "sprk_noaccess_object",
    storedIdNotificationId: "sprk_noaccess_storedid",
    /**
     * The object record types a deny reader evaluates: the three roots (NoAccessShareEnforcer.SecureRootTypes;
     * AccessibleRecordSetService composes only these). An entry on any other table is stored and enforced nowhere.
     */
    allowedObjectTypes: ["sprk_project", "sprk_matter", "sprk_workassignment"],
    version: "1.1.1"
};

/** Xrm save mode of the Deactivate command (getEventArgs().getSaveMode()). */
Spaarke.NoAccessEntry.SaveModeDeactivate = 5;

Spaarke.NoAccessEntry.Fields = {
    subjectContact: "sprk_subjectcontact",
    subjectOrganization: "sprk_subjectorganization",
    subjectUser: "sprk_subjectsystemuser",
    objectOrganization: "sprk_objectorganization",
    objectRecordType: "sprk_objectrecordtype",
    objectRecordId: "sprk_objectrecordid",
    name: "sprk_name"
};

/** Per-form state: the resolved object record type and the picked record's display name. */
Spaarke.NoAccessEntry._state = {
    /** @type {{refId: string, logicalName: (string|null), displayName: (string|null), status: string}|null} */
    recordType: null,
    /** @type {string|null} Display name of the object record, when known. */
    recordName: null,
    /** @type {string|null} The last name this script suggested (overwritten only while unchanged). */
    lastSuggestedName: null,
    /** @type {boolean} Whether the user changed a subject or object field on this form (name suggestion gate). */
    userChanged: false
};

Spaarke.NoAccessEntry._cachedApiBaseUrl = null;

// ---------------------------------------------------------------------------------------------------------------------
// Pure helpers (unit-tested in Spaarke.UI.Components/src/__tests__/noAccessEntryForm.test.ts)
// ---------------------------------------------------------------------------------------------------------------------

var CANONICAL_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
var EMPTY_ID = "00000000-0000-0000-0000-000000000000";
/**
 * The value as Dataverse's text comparison sees it, the mirror of NoAccessListReader.FoldLikeDataverse (measured live,
 * task 154): U+FEFF and the combining diacritical marks U+0300-U+036F removed (every OTHER combining mark, e.g. Arabic
 * or Thai, is significant to Dataverse and stays), full-width forms mapped to ASCII, U+3000 mapped to a space.
 */
Spaarke.NoAccessEntry._foldLikeDataverse = function (value) {
    var decomposed = typeof value.normalize === "function" ? value.normalize("NFD") : value;
    var out = "";
    for (var i = 0; i < decomposed.length; i++) {
        var ch = decomposed.charAt(i);
        var code = decomposed.charCodeAt(i);
        if (code === 0xFEFF || (code >= 0x0300 && code <= 0x036F)) {
            continue;
        }

        if (code === 0x3000) {
            out += " ";
        } else if (code >= 0xFF01 && code <= 0xFF5E) {
            out += String.fromCharCode(code - 0xFEE0);
        } else {
            out += ch;
        }
    }

    return out;
};

/**
 * Whether the server's deny readers match this stored id as it is (NoAccessListReader.TryParseObjectRecordId): after the
 * Dataverse folding and without trailing spaces, a hyphenated record id in any case. A braced id, a leading space or a
 * trailing tab is NOT matched, so such an entry walls nothing until it is saved in canonical form.
 */
Spaarke.NoAccessEntry.readerMatches = function (raw) {
    if (raw === null || raw === undefined) {
        return false;
    }

    var value = Spaarke.NoAccessEntry._foldLikeDataverse(String(raw)).replace(/ +$/, "").toLowerCase();
    return CANONICAL_ID.test(value) && value !== EMPTY_ID;
};

/**
 * The canonical form of a record id: trimmed, without braces, lower case. Returns null when the value is not a record id
 * (including the all-zero id). The readers match this exact text (NoAccessListReader.BuildRecordObjectFilter). Lenient by
 * design: whatever it accepts is written back in canonical form.
 */
Spaarke.NoAccessEntry.normalizeRecordId = function (raw) {
    if (raw === null || raw === undefined) {
        return null;
    }

    var value = Spaarke.NoAccessEntry._foldLikeDataverse(String(raw)).trim();
    if (value.charAt(0) === "{" && value.charAt(value.length - 1) === "}") {
        value = value.substring(1, value.length - 1).trim();
    }

    value = value.toLowerCase();
    if (!CANONICAL_ID.test(value) || value === EMPTY_ID) {
        return null;
    }

    return value;
};

/**
 * The problems that make an entry malformed, in plain words; an empty list means well-formed.
 * @param {{subjectCount: number, hasObjectOrganization: boolean, hasRecordType: boolean, recordId: (string|null),
 *          recordTypeStatus: (string|null)}} v
 *   recordTypeStatus: "allowed", "not-allowed", "unreadable", "pending", or null when no record type is set.
 * @returns {string[]}
 */
Spaarke.NoAccessEntry.shapeProblems = function (v) {
    var problems = [];
    if (v.subjectCount === 0) {
        problems.push("Choose who is denied: a contact, an organization or a user.");
    } else if (v.subjectCount > 1) {
        problems.push("Name only one of contact, organization or user as the subject. Create one entry for each.");
    }

    var hasId = v.recordId !== null && v.recordId !== undefined && String(v.recordId).trim() !== "";
    var hasRecordPart = v.hasRecordType || hasId;
    if (!v.hasObjectOrganization && !hasRecordPart) {
        problems.push("Choose what they are denied: an organization, or a record (choose the record type, then the record).");
    } else if (v.hasObjectOrganization && hasRecordPart) {
        problems.push("Choose either an organization or a record as the object, not both.");
    } else if (hasRecordPart) {
        if (!v.hasRecordType) {
            problems.push("Choose the record type of the object record.");
        } else if (!hasId) {
            problems.push("Pick the object record: the record type is set but no record is chosen.");
        } else if (Spaarke.NoAccessEntry.normalizeRecordId(v.recordId) === null) {
            problems.push("The object record id is not a record id. Choose the record type again to pick the record.");
        }

        if (v.hasRecordType) {
            if (v.recordTypeStatus === "not-allowed") {
                problems.push("No Access covers projects, matters and work assignments only. Choose one of those record types.");
            } else if (v.recordTypeStatus === "unreadable" || v.recordTypeStatus === "pending") {
                problems.push("The object record type could not be checked yet. Save again in a moment.");
            }
        }
    }

    return problems;
};

/** "{subject} - {object}", or null when either part has no name. */
Spaarke.NoAccessEntry.suggestName = function (subjectName, objectName) {
    if (!subjectName || !objectName) {
        return null;
    }

    return subjectName + " – " + objectName;
};

// ---------------------------------------------------------------------------------------------------------------------
// Form access helpers
// ---------------------------------------------------------------------------------------------------------------------

Spaarke.NoAccessEntry._attr = function (formContext, name) {
    try {
        return formContext.getAttribute(name);
    } catch (error) {
        return null;
    }
};

Spaarke.NoAccessEntry._value = function (formContext, name) {
    var attr = Spaarke.NoAccessEntry._attr(formContext, name);
    return attr ? attr.getValue() : null;
};

/** The first lookup value of a lookup attribute, or null. */
Spaarke.NoAccessEntry._lookup = function (formContext, name) {
    var value = Spaarke.NoAccessEntry._value(formContext, name);
    return value && value.length > 0 ? value[0] : null;
};

Spaarke.NoAccessEntry._setValue = function (formContext, name, value) {
    var attr = Spaarke.NoAccessEntry._attr(formContext, name);
    if (!attr) {
        return;
    }

    attr.setValue(value);
    if (typeof attr.setSubmitMode === "function") {
        attr.setSubmitMode("always");
    }
};

Spaarke.NoAccessEntry._controlNotify = function (formContext, name, text) {
    try {
        var control = formContext.getControl(name);
        if (!control) {
            return;
        }

        if (text) {
            control.setNotification(text, "sprk_noaccess_" + name);
        } else {
            control.clearNotification("sprk_noaccess_" + name);
        }
    } catch (error) {
        console.error("[No Access] Could not show the field notification:", error);
    }
};

Spaarke.NoAccessEntry._formNotify = function (formContext, id, text, level) {
    try {
        formContext.ui.clearFormNotification(id);
        if (text) {
            formContext.ui.setFormNotification(text, level, id);
        }
    } catch (error) {
        console.error("[No Access] Could not show the notification:", error);
    }
};

/**
 * Registers a handler exactly once: the platform keeps every add*, and Unified Interface fires OnLoad again after a save,
 * so an unguarded add* would run the shape check, the picker and the enforcement twice. remove* first is idempotent.
 */
Spaarke.NoAccessEntry._registerOnce = function (target, addName, removeName, handler) {
    if (!target || typeof target[addName] !== "function") {
        return;
    }

    if (typeof target[removeName] === "function") {
        target[removeName](handler);
    }

    target[addName](handler);
};

Spaarke.NoAccessEntry._cleanId = function (id) {
    return (id || "").replace(/[{}]/g, "").toLowerCase();
};

// ---------------------------------------------------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------------------------------------------------

/**
 * The BFF base URL as the HOST only (#1488): trailing slashes removed, then a trailing "/api" (any case). Every path
 * this script and sprk_/scripts/bff_auth.js append starts with "/api/", and sprk_BffApiBaseUrl carries "/api" on some
 * environments (dev: https://spaarke-bff-dev.azurewebsites.net/api), so an unnormalised value called /api/api/... (401).
 * The repo rule (sprk_emailactions.js, Spaarke.Auth resolveRuntimeConfig.ts). An empty value gives "".
 */
Spaarke.NoAccessEntry._normalizeBaseUrl = function (value) {
    return value ? String(value).replace(/\/+$/, "").replace(/\/api$/i, "") : "";
};

/** Resolves the BFF base URL from the sprk_BffApiBaseUrl environment variable (value override, else default). */
Spaarke.NoAccessEntry.getApiBaseUrl = function () {
    if (Spaarke.NoAccessEntry._cachedApiBaseUrl) {
        return Promise.resolve(Spaarke.NoAccessEntry._cachedApiBaseUrl);
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
            var baseUrl = Spaarke.NoAccessEntry._normalizeBaseUrl(value);
            if (!baseUrl) {
                throw new Error("Environment variable sprk_BffApiBaseUrl has no value.");
            }

            Spaarke.NoAccessEntry._cachedApiBaseUrl = baseUrl;
            return Spaarke.NoAccessEntry._cachedApiBaseUrl;
        });
    });
};

/** OnLoad: registers OnSave, OnPostSave and the OnChange handlers. Never throws. */
Spaarke.NoAccessEntry.onLoad = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        var F = Spaarke.NoAccessEntry.Fields;
        Spaarke.NoAccessEntry._state = { recordType: null, recordName: null, lastSuggestedName: null, userChanged: false };

        var entity = formContext.data.entity;
        Spaarke.NoAccessEntry._registerOnce(entity, "addOnSave", "removeOnSave", Spaarke.NoAccessEntry.onSave);
        Spaarke.NoAccessEntry._registerOnce(entity, "addOnPostSave", "removeOnPostSave", Spaarke.NoAccessEntry.onPostSave);

        var onChange = function (name, handler) {
            Spaarke.NoAccessEntry._registerOnce(Spaarke.NoAccessEntry._attr(formContext, name), "addOnChange", "removeOnChange", handler);
        };
        onChange(F.objectOrganization, Spaarke.NoAccessEntry.onObjectOrganizationChange);
        onChange(F.objectRecordId, Spaarke.NoAccessEntry.onObjectRecordIdChange);
        onChange(F.subjectContact, Spaarke.NoAccessEntry.onSubjectChange);
        onChange(F.subjectOrganization, Spaarke.NoAccessEntry.onSubjectChange);
        onChange(F.subjectUser, Spaarke.NoAccessEntry.onSubjectChange);

        // The record picker is self-contained: replacing it (e.g. by a PCF) means removing this one call and
        // registering a Record Type OnChange that calls _resolveRecordType, which the shape check needs.
        Spaarke.NoAccessEntry.Picker.register(formContext);
        Spaarke.NoAccessEntry._loadObjectRecord(formContext);
        Spaarke.NoAccessEntry._applySuggestedName(formContext);

        Spaarke.NoAccessEntry.getApiBaseUrl().then(function (url) {
            Spaarke.NoAccessEntry.Config.apiBaseUrl = url;
        }).catch(function (error) {
            console.error("[No Access] Could not resolve the BFF URL; enforcement falls to the 5-minute job.", error);
        });
    } catch (error) {
        console.error("[No Access] Error in onLoad:", error);
    }
};

// ---------------------------------------------------------------------------------------------------------------------
// The record picker (self-contained; see onLoad). It owns: the Record Type lookup filter, opening the platform lookup
// dialog when a type is chosen, and writing the picked id. Everything else (shape check, normalisation, verification,
// mutual exclusion, name suggestion) is the form's and does not depend on how the record was picked.
// ---------------------------------------------------------------------------------------------------------------------

Spaarke.NoAccessEntry.Picker = Spaarke.NoAccessEntry.Picker || {};

/** Registers the picker's handlers on the form, once. */
Spaarke.NoAccessEntry.Picker.register = function (formContext) {
    try {
        var F = Spaarke.NoAccessEntry.Fields;
        Spaarke.NoAccessEntry.Picker._formContext = formContext;
        Spaarke.NoAccessEntry._registerOnce(Spaarke.NoAccessEntry._attr(formContext, F.objectRecordType),
            "addOnChange", "removeOnChange", Spaarke.NoAccessEntry.Picker.onRecordTypeChange);
        Spaarke.NoAccessEntry._registerOnce(formContext.getControl(F.objectRecordType),
            "addPreSearch", "removePreSearch", Spaarke.NoAccessEntry.Picker.onRecordTypePreSearch);
    } catch (error) {
        console.error("[No Access] Could not register the record picker:", error);
    }
};

/** PreSearch of the Record Type lookup: only the types a deny reader evaluates. A stable function, so it registers once. */
Spaarke.NoAccessEntry.Picker.onRecordTypePreSearch = function (executionContext) {
    try {
        var control = executionContext && typeof executionContext.getEventSource === "function"
            ? executionContext.getEventSource()
            : null;
        if (!control || typeof control.addCustomFilter !== "function") {
            control = Spaarke.NoAccessEntry.Picker._formContext.getControl(Spaarke.NoAccessEntry.Fields.objectRecordType);
        }

        var values = Spaarke.NoAccessEntry.Config.allowedObjectTypes.map(function (t) {
            return "<value>" + t + "</value>";
        }).join("");
        control.addCustomFilter(
            "<filter type=\"and\"><condition attribute=\"sprk_recordlogicalname\" operator=\"in\">" + values +
            "</condition></filter>", "sprk_recordtype_ref");
    } catch (error) {
        console.error("[No Access] Could not filter the record types:", error);
    }
};

/**
 * Reads the sprk_recordtype_ref row behind the lookup and records whether it is a type the readers evaluate.
 * @returns {Promise<object|null>} the resolved state, or null when no type is set.
 */
Spaarke.NoAccessEntry._resolveRecordType = function (formContext) {
    var typeRef = Spaarke.NoAccessEntry._lookup(formContext, Spaarke.NoAccessEntry.Fields.objectRecordType);
    if (!typeRef) {
        Spaarke.NoAccessEntry._state.recordType = null;
        return Promise.resolve(null);
    }

    var refId = Spaarke.NoAccessEntry._cleanId(typeRef.id);
    var state = { refId: refId, logicalName: null, displayName: typeRef.name || null, status: "pending" };
    Spaarke.NoAccessEntry._state.recordType = state;

    return Xrm.WebApi.retrieveRecord("sprk_recordtype_ref", refId, "?$select=sprk_recordlogicalname")
        .then(function (row) {
            state.logicalName = row && row.sprk_recordlogicalname ? String(row.sprk_recordlogicalname) : null;
            state.status = state.logicalName &&
                Spaarke.NoAccessEntry.Config.allowedObjectTypes.indexOf(state.logicalName) >= 0 ? "allowed" : "not-allowed";
            return state;
        })
        .catch(function (error) {
            console.error("[No Access] The record type could not be read:", error);
            state.status = "unreadable";
            return state;
        });
};

/**
 * OnLoad of an existing entry: corrects a stored id that is not in canonical form (the field becomes dirty), warns when
 * the stored id walls nothing as it is, resolves the type and shows which record the entry names.
 */
Spaarke.NoAccessEntry._loadObjectRecord = function (formContext) {
    try {
        Spaarke.NoAccessEntry._checkStoredRecordId(formContext);
    } catch (error) {
        console.error("[No Access] Error checking the stored record id:", error);
    }

    Spaarke.NoAccessEntry._resolveRecordType(formContext).then(function (type) {
        var id = Spaarke.NoAccessEntry.normalizeRecordId(
            Spaarke.NoAccessEntry._value(formContext, Spaarke.NoAccessEntry.Fields.objectRecordId));
        if (type && type.status === "allowed" && id) {
            // Non-blocking on load: an entry whose record was deleted must stay deactivatable from its form.
            Spaarke.NoAccessEntry._verifyRecord(formContext, type, id, false);
        }
    }).catch(function (error) {
        console.error("[No Access] Error loading the object record:", error);
    });
};

/**
 * Confirms that a record of the type has the id and shows its name. A record that does not exist walls nothing: for an id
 * typed by hand (blocking = true) a field notification blocks the save; on load it is a warning, so the entry can still
 * be deactivated or corrected.
 */
Spaarke.NoAccessEntry._verifyRecord = function (formContext, type, id, blocking) {
    var F = Spaarke.NoAccessEntry.Fields;
    return Xrm.Utility.getEntityMetadata(type.logicalName, []).then(function (metadata) {
        var nameColumn = metadata && metadata.PrimaryNameAttribute;
        return Xrm.WebApi.retrieveRecord(type.logicalName, id, nameColumn ? "?$select=" + nameColumn : "")
            .then(function (row) {
                var name = nameColumn && row ? row[nameColumn] : null;
                Spaarke.NoAccessEntry._showObjectRecord(formContext, type, name || id);
                Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId, null);
            });
    }).catch(function (error) {
        var status = error && (error.errorCode === -2147220969 || /not exist|not found/i.test(error.message || ""));
        if (status) {
            var text = "No " + (type.displayName || type.logicalName) + " has this id, so this entry walls nothing. " +
                "Choose the record type again to pick the record.";
            if (blocking) {
                Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId, text);
            } else {
                Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.objectNotificationId, text, "WARNING");
            }
        } else {
            console.error("[No Access] The object record could not be read:", error);
        }
    });
};

/**
 * A stored id the readers do not match (braces, a leading space, a blank value) walls nothing: say so.
 *
 * NEVER writes on an EXISTING entry (task 154 verifier pass 2): a load-time write dirties the form, the org's autosave
 * then saves it, the viewer becomes the entry's last modifier, and the enforcer uses the last modifier as the author
 * whose Write decides enforcement (owner N5). So merely opening the form would change who the author is. On an
 * existing entry the warning asks the user to correct it and says that saving makes them the author; an id the readers
 * already match (e.g. upper case) is left as stored and normalised by the next real save (onSave). On a NEW entry
 * nothing is saved yet, so the value is corrected in place.
 */
Spaarke.NoAccessEntry._checkStoredRecordId = function (formContext) {
    var C = Spaarke.NoAccessEntry.Config;
    var F = Spaarke.NoAccessEntry.Fields;
    Spaarke.NoAccessEntry._formNotify(formContext, C.storedIdNotificationId, null);
    var raw = Spaarke.NoAccessEntry._value(formContext, F.objectRecordId);
    if (raw === null || raw === undefined || raw === "") {
        return;
    }

    var create = Spaarke.NoAccessEntry._isCreateForm(formContext);
    var authorNote = " Saving makes you this entry's author: whether it is then enforced on each record depends on " +
        "your Write on that record.";
    var normalized = Spaarke.NoAccessEntry.normalizeRecordId(raw);
    if (normalized === null) {
        var blank = String(raw).trim() === "";
        if (blank && create) {
            Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, null);
        }

        Spaarke.NoAccessEntry._formNotify(formContext, C.storedIdNotificationId, (blank
            ? "The stored record id is blank, so this entry walls nothing."
            : "The stored record id is not a record id, so this entry walls nothing.") +
            " To fix it, choose the record type again and pick the record, then save." + (create ? "" : authorNote),
            "WARNING");
        return;
    }

    if (normalized === raw) {
        return;
    }

    if (create) {
        Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, normalized);
        return;
    }

    if (!Spaarke.NoAccessEntry.readerMatches(raw)) {
        Spaarke.NoAccessEntry._formNotify(formContext, C.storedIdNotificationId,
            "The stored record id is not in the form the access checks match, so this entry walls nothing as it is. " +
            "To fix it, choose the record type again and pick the record, then save." + authorNote, "WARNING");
    }
};

/** Whether the form is creating a new entry (form type 1). Unknown is treated as an existing entry: no writes. */
Spaarke.NoAccessEntry._isCreateForm = function (formContext) {
    try {
        return typeof formContext.ui.getFormType === "function" && formContext.ui.getFormType() === 1;
    } catch (error) {
        return false;
    }
};

Spaarke.NoAccessEntry._showObjectRecord = function (formContext, type, name) {
    Spaarke.NoAccessEntry._state.recordName = name;
    Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.objectNotificationId,
        "Object record: " + (type.displayName || type.logicalName) + " “" + name + "”. " +
        "To choose another record, choose the record type again.", "INFO");
    Spaarke.NoAccessEntry._applySuggestedName(formContext);
};

Spaarke.NoAccessEntry._clearRecordObject = function (formContext) {
    var F = Spaarke.NoAccessEntry.Fields;
    Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordType, null);
    Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, null);
    Spaarke.NoAccessEntry._state.recordType = null;
    Spaarke.NoAccessEntry._state.recordName = null;
    Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordType, null);
    Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId, null);
    Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.objectNotificationId, null);
};

// ---------------------------------------------------------------------------------------------------------------------
// OnChange handlers
// ---------------------------------------------------------------------------------------------------------------------

/** An organization was chosen as the object: the record pair is cleared, so exactly one object survives. */
Spaarke.NoAccessEntry.onObjectOrganizationChange = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        Spaarke.NoAccessEntry._state.userChanged = true;
        if (Spaarke.NoAccessEntry._lookup(formContext, Spaarke.NoAccessEntry.Fields.objectOrganization)) {
            Spaarke.NoAccessEntry._clearRecordObject(formContext);
        }

        Spaarke.NoAccessEntry._applySuggestedName(formContext);
    } catch (error) {
        console.error("[No Access] Error in onObjectOrganizationChange:", error);
    }
};

/**
 * The record picker. Choosing a record type opens the lookup dialog for that table; the picked record's id is written to
 * sprk_objectrecordid in canonical form and the object organization is cleared. Clearing the type clears the id, so the
 * pair is never left half set. A type the readers do not evaluate is refused at once.
 */
Spaarke.NoAccessEntry.Picker.onRecordTypeChange = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        Spaarke.NoAccessEntry._state.userChanged = true;
        var F = Spaarke.NoAccessEntry.Fields;
        Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordType, null);
        Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, null);
        Spaarke.NoAccessEntry._state.recordName = null;
        Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.objectNotificationId, null);

        Spaarke.NoAccessEntry._resolveRecordType(formContext).then(function (type) {
            if (!type) {
                Spaarke.NoAccessEntry._applySuggestedName(formContext);
                return;
            }

            if (type.status === "not-allowed") {
                Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordType,
                    "No Access covers projects, matters and work assignments only.");
                return;
            }

            if (type.status !== "allowed") {
                Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordType,
                    "The record type could not be read. Choose it again.");
                return;
            }

            Spaarke.NoAccessEntry._setValue(formContext, F.objectOrganization, null);
            Spaarke.NoAccessEntry.Picker.pickRecord(formContext, type);
        }).catch(function (error) {
            console.error("[No Access] Error resolving the record type:", error);
        });
    } catch (error) {
        console.error("[No Access] Error in Picker.onRecordTypeChange:", error);
    }
};

/** Opens the platform lookup dialog for the type and writes the picked record. */
Spaarke.NoAccessEntry.Picker.pickRecord = function (formContext, type) {
    var F = Spaarke.NoAccessEntry.Fields;
    return Xrm.Utility.lookupObjects({
        entityTypes: [type.logicalName],
        defaultEntityType: type.logicalName,
        allowMultiSelect: false
    }).then(function (results) {
        if (!results || results.length === 0) {
            Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId, null);
            return;
        }

        var id = Spaarke.NoAccessEntry.normalizeRecordId(results[0].id);
        if (!id) {
            Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId,
                "The picked record has no usable id. Choose the record type again.");
            return;
        }

        Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, id);
        Spaarke.NoAccessEntry._setValue(formContext, F.objectOrganization, null);
        Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId, null);
        Spaarke.NoAccessEntry._showObjectRecord(formContext, type, results[0].name || id);
    }).catch(function (error) {
        console.error("[No Access] The record lookup could not be opened:", error);
        Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId,
            "The record could not be picked. Choose the record type again.");
    });
};

/** A record id typed or pasted by hand: normalised at once, and checked against the record type. */
Spaarke.NoAccessEntry.onObjectRecordIdChange = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();
        Spaarke.NoAccessEntry._state.userChanged = true;
        var F = Spaarke.NoAccessEntry.Fields;
        var raw = Spaarke.NoAccessEntry._value(formContext, F.objectRecordId);
        Spaarke.NoAccessEntry._state.recordName = null;
        Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.objectNotificationId, null);

        if (raw === null || String(raw).trim() === "") {
            if (raw !== null) {
                // A blank-but-not-empty id is malformed server-side (it walls nothing): store nothing instead.
                Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, null);
            }

            Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId, null);
            Spaarke.NoAccessEntry._applySuggestedName(formContext);
            return;
        }

        var id = Spaarke.NoAccessEntry.normalizeRecordId(raw);
        if (!id) {
            Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId,
                "This is not a record id. Choose the record type to pick the record instead.");
            return;
        }

        if (id !== raw) {
            Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, id);
        }

        Spaarke.NoAccessEntry._controlNotify(formContext, F.objectRecordId, null);
        Spaarke.NoAccessEntry._setValue(formContext, F.objectOrganization, null);
        var type = Spaarke.NoAccessEntry._state.recordType;
        if (type && type.status === "allowed") {
            Spaarke.NoAccessEntry._verifyRecord(formContext, type, id, true);
        }
    } catch (error) {
        console.error("[No Access] Error in onObjectRecordIdChange:", error);
    }
};

Spaarke.NoAccessEntry.onSubjectChange = function (executionContext) {
    try {
        Spaarke.NoAccessEntry._state.userChanged = true;
        Spaarke.NoAccessEntry._applySuggestedName(executionContext.getFormContext());
    } catch (error) {
        console.error("[No Access] Error in onSubjectChange:", error);
    }
};

/**
 * Fills sprk_name while it is empty or still holds the last suggestion; a name the user typed is never replaced. Only
 * on a NEW entry or after the user changed a subject or object field: on an existing entry a load-time write would
 * dirty the form and autosave would make the viewer the entry's author (see _checkStoredRecordId).
 */
Spaarke.NoAccessEntry._applySuggestedName = function (formContext) {
    try {
        var F = Spaarke.NoAccessEntry.Fields;
        var state = Spaarke.NoAccessEntry._state;
        if (!state.userChanged && !Spaarke.NoAccessEntry._isCreateForm(formContext)) {
            return;
        }

        var current = Spaarke.NoAccessEntry._value(formContext, F.name);
        if (current && current !== state.lastSuggestedName) {
            return;
        }

        var subject = Spaarke.NoAccessEntry._lookup(formContext, F.subjectContact) ||
            Spaarke.NoAccessEntry._lookup(formContext, F.subjectOrganization) ||
            Spaarke.NoAccessEntry._lookup(formContext, F.subjectUser);
        var objectOrg = Spaarke.NoAccessEntry._lookup(formContext, F.objectOrganization);
        var objectName = objectOrg ? objectOrg.name : state.recordName;
        var suggestion = Spaarke.NoAccessEntry.suggestName(subject ? subject.name : null, objectName);
        if (suggestion && suggestion !== current) {
            Spaarke.NoAccessEntry._setValue(formContext, F.name, suggestion);
            state.lastSuggestedName = suggestion;
        }
    } catch (error) {
        console.error("[No Access] Could not suggest a name:", error);
    }
};

// ---------------------------------------------------------------------------------------------------------------------
// OnSave: the shape check (a preview; the server rules own the shape)
// ---------------------------------------------------------------------------------------------------------------------

/** Normalises the record id and prevents the save of a malformed entry, naming the problem. */
Spaarke.NoAccessEntry.onSave = function (executionContext) {
    var formContext;
    try {
        formContext = executionContext.getFormContext();
        var F = Spaarke.NoAccessEntry.Fields;

        // Deactivating lifts a wall and is how a malformed entry is retired: never block it.
        var args = executionContext.getEventArgs();
        if (args && typeof args.getSaveMode === "function" && args.getSaveMode() === Spaarke.NoAccessEntry.SaveModeDeactivate) {
            return;
        }

        var rawId = Spaarke.NoAccessEntry._value(formContext, F.objectRecordId);
        if (rawId !== null && rawId !== undefined && String(rawId).trim() === "") {
            // Blank but not empty is malformed server-side: save no id, so the shape check names the real problem.
            Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, null);
            rawId = null;
        }

        var normalized = Spaarke.NoAccessEntry.normalizeRecordId(rawId);
        if (normalized && normalized !== rawId) {
            Spaarke.NoAccessEntry._setValue(formContext, F.objectRecordId, normalized);
        }

        var subjectCount = [F.subjectContact, F.subjectOrganization, F.subjectUser]
            .filter(function (name) { return Spaarke.NoAccessEntry._lookup(formContext, name) !== null; }).length;
        var hasRecordType = Spaarke.NoAccessEntry._lookup(formContext, F.objectRecordType) !== null;
        var type = Spaarke.NoAccessEntry._state.recordType;
        var status = null;
        if (hasRecordType) {
            var typeRef = Spaarke.NoAccessEntry._lookup(formContext, F.objectRecordType);
            status = type && type.refId === Spaarke.NoAccessEntry._cleanId(typeRef.id) ? type.status : "pending";
            if (status === "pending" && (!type || type.status !== "pending")) {
                Spaarke.NoAccessEntry._resolveRecordType(formContext);
            }
        }

        var problems = Spaarke.NoAccessEntry.shapeProblems({
            subjectCount: subjectCount,
            hasObjectOrganization: Spaarke.NoAccessEntry._lookup(formContext, F.objectOrganization) !== null,
            hasRecordType: hasRecordType,
            recordId: normalized || rawId,
            recordTypeStatus: status
        });

        if (problems.length > 0) {
            args.preventDefault();
            Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.shapeNotificationId,
                "This entry was not saved. " + problems.join(" "), "ERROR");
            return;
        }

        Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.shapeNotificationId, null);
        Spaarke.NoAccessEntry._applySuggestedName(formContext);
    } catch (error) {
        // The check is a preview: the server's malformed-entry rule still holds, and the post-save notice reports it.
        console.error("[No Access] Error in onSave; the server's rules still apply:", error);
    }
};

// ---------------------------------------------------------------------------------------------------------------------
// OnPostSave: enforcement (task 143)
// ---------------------------------------------------------------------------------------------------------------------

/** OnPostSave: fire-and-forget enforcement. Never throws, never blocks the save. */
Spaarke.NoAccessEntry.onPostSave = function (executionContext) {
    try {
        var args = executionContext.getEventArgs ? executionContext.getEventArgs() : null;
        if (args && typeof args.getIsSaveSuccess === "function" && !args.getIsSaveSuccess()) {
            return; // the save failed: nothing was saved, so there is nothing to enforce or describe
        }

        var formContext = executionContext.getFormContext();
        var entryId = (formContext.data.entity.getId() || "").replace(/[{}]/g, "");
        if (!entryId) {
            return;
        }

        Spaarke.NoAccessEntry._enforce(formContext, entryId);
    } catch (error) {
        console.error("[No Access] Error in onPostSave:", error);
    }
};

Spaarke.NoAccessEntry._notify = function (formContext, text, level) {
    Spaarke.NoAccessEntry._formNotify(formContext, Spaarke.NoAccessEntry.Config.notificationId, text, level);
};

/** The records a list of report items names, as "Matter 1b4e28ba, Project 9f2c..." (at most 10, then "and N more"). */
Spaarke.NoAccessEntry._recordList = function (items) {
    var labels = { sprk_project: "Project", sprk_matter: "Matter", sprk_workassignment: "Work assignment" };
    var seen = {};
    var names = [];
    (items || []).forEach(function (n) {
        var key = (n.recordType || "") + ":" + (n.recordId || "");
        if (!n.recordId || seen[key]) {
            return;
        }

        seen[key] = true;
        names.push((labels[n.recordType] || n.recordType || "Record") + " " + String(n.recordId).substring(0, 8));
    });

    if (names.length > 10) {
        return names.slice(0, 10).join(", ") + " and " + (names.length - 10) + " more";
    }

    return names.join(", ");
};

/** Builds the user-facing summary from a 200 report. Text only (owner O1); not-enforced records are named (owner N5). */
Spaarke.NoAccessEntry._summarize = function (report) {
    var messages = [];
    var removed = (report.removed || []).length;
    if (removed > 0) {
        messages.push("Removed " + removed + " direct share(s) this entry walls off.");
    }

    var notEnforced = report.notEnforced || [];
    var lacksWrite = notEnforced.filter(function (n) { return n.reason === "author-lacks-write"; });
    if (lacksWrite.length > 0) {
        messages.push("Not enforced on " + Spaarke.NoAccessEntry._recordList(lacksWrite) +
            ": you do not hold Write on these records, so their shares were not removed.");
    }

    if (notEnforced.some(function (n) { return n.reason === "author-not-a-person"; })) {
        messages.push("Not enforced: the entry's author is not an enabled person.");
    }

    if ((report.notEnforceable || []).length > 0) {
        messages.push("Some access comes from a team or a role and cannot be removed per person; Teams and the " +
            "portal hide the record, the model-driven app may still show it.");
    }

    if (report.truncated) {
        messages.push("The entry covers more than one pass handles; the rest is enforced within 5 minutes.");
    }

    return messages.join(" ");
};

Spaarke.NoAccessEntry._enforce = async function (formContext, entryId) {
    try {
        var baseUrl = Spaarke.NoAccessEntry._normalizeBaseUrl(Spaarke.NoAccessEntry.Config.apiBaseUrl) ||
            await Spaarke.NoAccessEntry.getApiBaseUrl();
        if (typeof Spaarke.BffAuth === "undefined" || !Spaarke.BffAuth.getToken) {
            console.error("[No Access] Spaarke.BffAuth is not loaded (register sprk_/scripts/bff_auth.js first); " +
                "the 5-minute job will enforce this entry.");
            Spaarke.NoAccessEntry._notify(formContext,
                "This entry could not be enforced from the form (its sign-in helper is not loaded); it is enforced " +
                "within 5 minutes.", "WARNING");
            return;
        }

        var token = await Spaarke.BffAuth.getToken(baseUrl);
        if (!token) {
            console.warn("[No Access] No BFF token; the 5-minute job will enforce this entry.");
            Spaarke.NoAccessEntry._notify(formContext,
                "This entry could not be enforced now; it is enforced within 5 minutes.", "WARNING");
            return;
        }

        var response = await fetch(baseUrl + Spaarke.NoAccessEntry.Config.enforcePath, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "Authorization": "Bearer " + token
            },
            body: JSON.stringify({ entryId: entryId })
        });

        var body = null;
        try {
            body = await response.json();
        } catch (parseError) {
            body = null;
        }

        if (response.ok) {
            var summary = Spaarke.NoAccessEntry._summarize(body || {});
            Spaarke.NoAccessEntry._notify(formContext, summary, summary.indexOf("Not enforced") >= 0 ? "WARNING" : "INFO");
            return;
        }

        var detail = body && body.detail
            ? body.detail
            : "This entry could not be enforced now (" + response.status + "); it is retried within 5 minutes.";
        Spaarke.NoAccessEntry._notify(formContext, detail, response.status === 409 ? "WARNING" : "ERROR");
    } catch (error) {
        console.error("[No Access] Enforcement call failed; the 5-minute job will enforce this entry.", error);
        Spaarke.NoAccessEntry._notify(formContext,
            "This entry could not be enforced now; it is retried within 5 minutes.", "WARNING");
    }
};
