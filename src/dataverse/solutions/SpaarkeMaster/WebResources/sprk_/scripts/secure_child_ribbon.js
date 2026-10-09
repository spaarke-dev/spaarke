/**
 * Secure-record child "New" commands - subgrids of the children of a project, matter or work assignment
 * (unified-access-control-r2 task 147 r1; owner round 28 item 2, "E2").
 *
 * Web Resource Name: sprk_/scripts/secure_child_ribbon.js   (namespace Spaarke.SecureChild.Ribbon)
 *
 * WHY: a child created by the platform's own "+ New" on a subgrid (the parent prefilled) is created AS THE USER and owned
 * by the user - under a SECURE record that leaves it readable by the user's whole business unit until the reconcile job
 * re-owns it. In the product such a create goes through the BFF instead, which creates it owned by the team the
 * ownership rule names (the Secure Record Owners team under a secure record). So, on a subgrid whose host record is
 * secure, this script:
 *   - HIDES the native "+ New" (an extra enable rule on Mscrm.AddNewRecordFromSubGridStandard: nativeNewAllowed), and
 *   - SHOWS a "New <thing>" command (newChildAvailable) that opens the product's BFF-backed create surface for that table,
 *     filed under the host record:
 *       sprk_todo -> To Do wizard, sprk_event -> Event wizard, sprk_invoice -> Invoice wizard,
 *       sprk_reportcard -> Report Card wizard, sprk_document -> Document Upload wizard,
 *       sprk_communication -> the Communication page in compose mode (sent through the BFF),
 *       sprk_budget, sprk_kpiassessment, sprk_billingevent -> POST /api/v1/child-records/{table} (no wizard of their own),
 *       then the new row opens.
 *
 * FAIL CLOSED (ADR-003): the native "+ New" is allowed ONLY when the host is a project / matter / work assignment whose
 * sprk_issecure was read (through Xrm.WebApi) and is false, or a party (contact, organization, account - never an
 * ownership parent). A flag that cannot be read, an empty flag, or any other host (a child of a child - an event, a
 * document, an invoice, an analysis, a budget: its secure ancestor is not known here) hides the native command and shows
 * the BFF one, which is right for any host: the BFF files the new record under whatever it names and decides its owner.
 *
 * Not a security boundary on its own: the Create privilege is NOT removed (owner round 28); a native create that slips
 * past (an API client, an import) is re-owned by the reconcile job's recent-changes pass every 2 minutes.
 *
 * Libraries on every command and rule, in order (ribbon commands do not load form libraries):
 *   1. sprk_/scripts/bff_auth.js                 - Spaarke.BffAuth (silent SSO, never a popup - ADR-028 INV-5)
 *   2. sprk_/scripts/assignedaccess_postsave.js  - Spaarke.AssignedAccess.getApiBaseUrl (the BFF base URL)
 *   3. sprk_/scripts/secure_child_ribbon.js      - this file
 * (the leading two as FunctionName="isNaN" - the established ribbon idiom, see AccessRibbons/README.md).
 *
 * ADR-006: a thin ribbon-command script. No plugin (ADR-002). Never throws; a missing helper shows a message.
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.SecureChild = Spaarke.SecureChild || {};
Spaarke.SecureChild.Ribbon = Spaarke.SecureChild.Ribbon || {};

(function (ns) {
    ns.VERSION = "1.0.0";

    var LOG = "[SecureChild.Ribbon v" + ns.VERSION + "]";

    /** The secure-capable roots: the only hosts whose flag this script reads. */
    ns.ROOTS = { sprk_project: true, sprk_matter: true, sprk_workassignment: true };

    /**
     * r1c: party hosts. A contact, an organization or an account is never an ownership parent (identity tables with no
     * owning business unit - RecordOwnershipResolver.OwnershipParentEntities excludes them), so a child created under
     * one is never under a secure record: the platform "+ New" stays (live inventory: the contact and organization main
     * forms carry a to-do subgrid).
     */
    ns.PARTIES = { contact: true, sprk_organization: true, account: true };

    /** Wizard code pages (envelope: entityType / entityId / bffBaseUrl - sprk_wizard_commands.js). */
    ns.WIZARDS = {
        sprk_todo: { page: "sprk_createtodowizard", title: "Create New To Do" },
        sprk_event: { page: "sprk_createeventwizard", title: "Create New Event" },
        sprk_invoice: { page: "sprk_createinvoicewizard", title: "Create New Invoice" },
        sprk_reportcard: { page: "sprk_createreportcardwizard", title: "Create New Report Card" }
    };

    /**
     * Tables created through POST /api/v1/child-records/{table}, then opened (no wizard of their own). r1c: the live
     * inventory of every main form adds the KPI assessment (matter, project and report card forms) and the billing event
     * (invoice form) - the new row is a named draft filed under the host; its form asks for the rest.
     */
    ns.CREATE_THEN_OPEN = {
        sprk_budget: { label: "budget" },
        sprk_kpiassessment: { label: "KPI assessment" },
        sprk_billingevent: { label: "billing event" }
    };

    /** Every child table this script serves (the template's per-entity instances must stay within it). */
    ns.TABLES = ["sprk_todo", "sprk_event", "sprk_invoice", "sprk_reportcard", "sprk_document", "sprk_communication", "sprk_budget",
        "sprk_kpiassessment", "sprk_billingevent"];

    var DIALOG_OPTIONS = { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" } };
    var COMPOSE_OPTIONS = { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } };

    function cleanId(id) {
        return String(id || "").replace(/[{}]/g, "").toLowerCase();
    }

    /** The host record of a subgrid command: { entity, id } from the form context, or null (unsaved / no form). */
    ns.hostOf = function (primaryControl) {
        try {
            var entity = primaryControl && primaryControl.data && primaryControl.data.entity;
            if (!entity) return null;
            var id = cleanId(entity.getId());
            if (!id) return null;
            return { entity: entity.getEntityName(), id: id };
        } catch (error) {
            return null;
        }
    };

    /**
     * Whether the host is secure: true / false, or null when it cannot be told (unreadable, empty, or not a root).
     * Resolves; never rejects. Read through Xrm.WebApi every time (owner round 28 item 2: "enable rules read sprk_issecure
     * through Xrm.WebApi") - the stored value, never the form's in-memory copy, which Make Secure / Remove Secure may not
     * have refreshed yet.
     */
    ns.hostSecureFlag = function (primaryControl) {
        var host = ns.hostOf(primaryControl);
        if (!host || !ns.ROOTS[host.entity]) {
            return Promise.resolve(null);
        }

        try {
            return Xrm.WebApi.retrieveRecord(host.entity, host.id, "?$select=sprk_issecure").then(function (row) {
                var flag = row && row.sprk_issecure;
                return typeof flag === "boolean" ? flag : null;
            }, function (error) {
                console.warn(LOG, "sprk_issecure could not be read; the platform New stays hidden.", error);
                return null;
            });
        } catch (error) {
            return Promise.resolve(null);
        }
    };

    /**
     * EnableRule added to the native subgrid "+ New" (Mscrm.AddNewRecordFromSubGridStandard): true ONLY when the host is
     * a root read as NOT secure. A host that is not a form record (no parent) keeps the native command - it prefills
     * nothing. Returns a promise (UCI waits for it).
     */
    ns.nativeNewAllowed = function (primaryControl) {
        var host = ns.hostOf(primaryControl);
        if (!host || ns.PARTIES[host.entity]) {
            return true;
        }
        return ns.hostSecureFlag(primaryControl).then(function (flag) { return flag === false; });
    };

    /** EnableRule for the "New <thing>" command: the exact complement of nativeNewAllowed (one of the two is shown). */
    ns.newChildAvailable = function (primaryControl) {
        var host = ns.hostOf(primaryControl);
        if (!host || ns.PARTIES[host.entity]) {
            return false;
        }
        return ns.hostSecureFlag(primaryControl).then(function (flag) { return flag !== false; });
    };

    function alert(title, text) {
        try {
            Xrm.Navigation.openAlertDialog({ title: title, text: text });
        } catch (error) {
            console.error(LOG, title + ": " + text, error);
        }
    }

    function helpersLoaded() {
        return typeof Spaarke.BffAuth !== "undefined" && !!Spaarke.BffAuth.getToken &&
            typeof Spaarke.AssignedAccess !== "undefined" && !!Spaarke.AssignedAccess.getApiBaseUrl;
    }

    function refresh(selectedControl, primaryControl) {
        try {
            if (selectedControl && selectedControl.refresh) selectedControl.refresh();
            else if (primaryControl && primaryControl.data) primaryControl.data.refresh(false);
        } catch (error) {
            console.warn(LOG, "The subgrid could not be refreshed:", error);
        }
    }

    function openPage(page, data, title, options, selectedControl, primaryControl) {
        return Xrm.Navigation.navigateTo(
            { pageType: "webresource", webresourceName: page, data: data },
            Object.assign({}, options, { title: title })
        ).then(function () { refresh(selectedControl, primaryControl); },
            function () { refresh(selectedControl, primaryControl); });
    }

    /** The host's display name, for the upload wizard's label ('' when none). */
    function hostName(primaryControl) {
        try {
            var primary = primaryControl.data.entity.getPrimaryAttributeValue && primaryControl.data.entity.getPrimaryAttributeValue();
            return primary || "";
        } catch (error) {
            return "";
        }
    }

    /** ProblemDetails `detail` (else `title`) of a failed BFF answer - the server's own words. */
    function problemText(response) {
        return response.text().then(function (text) {
            try {
                var problem = JSON.parse(text);
                return problem.detail || problem.title || ("HTTP " + response.status);
            } catch (e) {
                return text || ("HTTP " + response.status);
            }
        }, function () { return "HTTP " + response.status; });
    }

    /** The child's lookup to the host (from the subgrid's relationship) as a Web API bind key, via metadata. */
    function bindKeyFor(childTable, hostEntity, selectedControl) {
        var attribute = null;
        try {
            var relationship = selectedControl && selectedControl.getRelationship && selectedControl.getRelationship();
            attribute = relationship && relationship.attributeName;
        } catch (e) { /* fall through to metadata only */ }

        var url = Xrm.Utility.getGlobalContext().getClientUrl() + "/api/data/v9.2/EntityDefinitions(LogicalName='" + childTable +
            "')/ManyToOneRelationships?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity";
        return fetch(url, { headers: { Accept: "application/json" }, credentials: "include" }).then(function (response) {
            if (!response.ok) throw new Error("metadata HTTP " + response.status);
            return response.json();
        }).then(function (body) {
            var candidates = (body.value || []).filter(function (r) {
                return r.ReferencedEntity === hostEntity && (!attribute || r.ReferencingAttribute === attribute);
            });
            if (candidates.length !== 1) {
                throw new Error("the " + childTable + " -> " + hostEntity + " lookup is not unique (" + candidates.length + ")");
            }
            return candidates[0].ReferencingEntityNavigationPropertyName + "@odata.bind";
        });
    }

    function entitySetOf(logicalName) {
        var url = Xrm.Utility.getGlobalContext().getClientUrl() + "/api/data/v9.2/EntityDefinitions(LogicalName='" + logicalName +
            "')?$select=EntitySetName,PrimaryNameAttribute";
        return fetch(url, { headers: { Accept: "application/json" }, credentials: "include" }).then(function (response) {
            if (!response.ok) throw new Error("metadata HTTP " + response.status);
            return response.json();
        });
    }

    /** sprk_budget: create through the BFF (owned by the team the rule names), then open the new record. */
    function createThenOpen(childTable, host, primaryControl, selectedControl) {
        var label = ns.CREATE_THEN_OPEN[childTable].label;
        return Spaarke.AssignedAccess.getApiBaseUrl().then(function (baseUrl) {
            return Spaarke.BffAuth.getToken(baseUrl).then(function (token) {
                if (!token) {
                    alert("New " + label, "Sign-in needed - reload the page and retry. Nothing was created.");
                    return null;
                }
                return Promise.all([bindKeyFor(childTable, host.entity, selectedControl), entitySetOf(host.entity), entitySetOf(childTable)])
                    .then(function (parts) {
                        var payload = {};
                        payload[parts[2].PrimaryNameAttribute] = "New " + label;
                        payload[parts[0]] = "/" + parts[1].EntitySetName + "(" + host.id + ")";
                        return fetch(baseUrl + "/api/v1/child-records/" + childTable, {
                            method: "POST",
                            headers: { "Content-Type": "application/json", Accept: "application/json", Authorization: "Bearer " + token },
                            body: JSON.stringify(payload)
                        });
                    })
                    .then(function (response) {
                        if (!response.ok) {
                            return problemText(response).then(function (text) {
                                alert("New " + label, text + " Nothing was created.");
                                return null;
                            });
                        }
                        return response.json().then(function (body) {
                            refresh(selectedControl, primaryControl);
                            return Xrm.Navigation.openForm({ entityName: childTable, entityId: cleanId(body.id) });
                        });
                    });
            });
        });
    }

    /**
     * Command: "New <thing>" on a subgrid of a secure (or unverifiable) host. Opens the product's BFF-backed create surface
     * for `childTable`, filed under the host. Never falls back to the platform New.
     * @param {object} primaryControl - the host form context
     * @param {object} selectedControl - the subgrid
     * @param {string} childTable - the subgrid's table (a template parameter)
     */
    ns.newChild = function (primaryControl, selectedControl, childTable) {
        try {
            var host = ns.hostOf(primaryControl);
            if (!host) {
                alert("New record", "Save this record first, then add to it.");
                return;
            }
            if (!helpersLoaded()) {
                alert("New record",
                    "The sign-in helper for this command is not loaded on this form. Reload the page and try again; if it " +
                    "persists, ask an administrator to check the command's libraries.");
                return;
            }

            Spaarke.AssignedAccess.getApiBaseUrl().then(function (baseUrl) {
                var bff = "&bffBaseUrl=" + encodeURIComponent(baseUrl);
                var wizard = ns.WIZARDS[childTable];
                if (wizard) {
                    // The wizard opens filed under the host (useWizardPageBootstrap / the To Do page read entityType,
                    // entityId and recordName); its create goes through the BFF.
                    return openPage(wizard.page,
                        "entityType=" + host.entity + "&entityId=" + host.id +
                        "&recordName=" + encodeURIComponent(hostName(primaryControl)) + bff,
                        wizard.title, DIALOG_OPTIONS, selectedControl, primaryControl);
                }
                if (childTable === "sprk_document") {
                    return openPage("sprk_documentuploadwizard",
                        "parentEntityType=" + encodeURIComponent(host.entity) + "&parentEntityId=" + encodeURIComponent(host.id) +
                        "&parentEntityName=" + encodeURIComponent(hostName(primaryControl)) + bff,
                        "Upload Documents", DIALOG_OPTIONS, selectedControl, primaryControl);
                }
                if (childTable === "sprk_communication") {
                    return openPage("sprk_communicationpage",
                        "mode=compose&associatedTo=" + encodeURIComponent(host.entity + ":" + host.id) + bff,
                        "New Message", COMPOSE_OPTIONS, selectedControl, primaryControl);
                }
                if (ns.CREATE_THEN_OPEN[childTable]) {
                    return createThenOpen(childTable, host, primaryControl, selectedControl);
                }
                alert("New record", "This table has no Spaarke create command. Nothing was created.");
                return null;
            }).catch(function (error) {
                console.error(LOG, "newChild failed:", error);
                alert("New record", "The record could not be created now. Try again in a moment. Nothing was created.");
            });
        } catch (error) {
            console.error(LOG, "newChild failed:", error);
            alert("New record", "The record could not be created now. Try again in a moment.");
        }
    };
})(Spaarke.SecureChild.Ribbon);
