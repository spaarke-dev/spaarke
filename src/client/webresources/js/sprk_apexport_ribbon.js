/**
 * AP export and Mark Paid commands - the sprk_invoice views and form, and the sprk_billingexport form
 * (ebilling-r1 task 151b, EB-D-181).
 *
 * Web Resource Name: sprk_/scripts/apexport_ribbon.js   (namespace Spaarke.ApExport.Ribbon)
 *
 * Commands:
 *   - "Export to AP" (invoice views: a selection of 1 to 200; invoice form: an Approved invoice) and "Mark Paid" (invoice
 *     views: a selection of Sent to AP invoices; invoice form: a Sent to AP invoice) open the AP Export dialog code page
 *     (web resource sprk_apexport) through Xrm.Navigation.navigateTo, with the mode and the invoice ids. The page calls the
 *     BFF: POST /api/finance/export-batches (task 150) and POST /api/finance/invoices/{id}/mark-paid (task 151a).
 *   - "Download CSV" / "Download JSON" (Billing Export form) fetch GET /api/finance/export-batches/{id}/files/{csv|json}
 *     (task 151a) and save the file.
 *
 * DISPLAY ONLY: the enable rules below only decide when a button is offered. The server decides every export, payment and
 * download (FR-41, FR-42): a selection that slips past a rule is refused by the server, per invoice, in the dialog.
 *
 * Libraries on the download command, in order (ribbon commands do not load form libraries):
 *   1. sprk_/scripts/bff_auth.js                 - Spaarke.BffAuth (silent SSO, never a popup - ADR-028 INV-5)
 *   2. sprk_/scripts/assignedaccess_postsave.js  - Spaarke.AssignedAccess.getApiBaseUrl (the BFF base URL)
 *   3. sprk_/scripts/apexport_ribbon.js          - this file
 * (the leading two as FunctionName="isNaN" - the established ribbon idiom, as sprk_secure_child_ribbon.js uses).
 *
 * ADR-006: a thin ribbon-command script. ADR-050: navigateTo is the launch from a hostless ribbon script. No plugin
 * (ADR-002). Never throws; a missing helper or a refusal shows a message.
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.ApExport = Spaarke.ApExport || {};
Spaarke.ApExport.Ribbon = Spaarke.ApExport.Ribbon || {};

(function (ns) {
    ns.VERSION = "1.0.0";

    var LOG = "[ApExport.Ribbon v" + ns.VERSION + "]";

    /** The dialog code page (src/solutions/ApExport, built to sprk_apexport.html). */
    ns.PAGE = "sprk_apexport";

    /** sprk_invoice statuscode values (Entity.xml, sprk_invoice_statuscode). */
    ns.INVOICE_STATUS = { approved: 659490000, sentToAp: 659490001, paid: 659490002 };

    /** sprk_billingexport.sprk_exportstatus: Completed. Only a Completed batch has files (task 150). */
    ns.EXPORT_COMPLETED = 100000001;

    /** The largest selection the export route accepts (task 150: 1 to 200 invoices). Mark Paid uses the same cap. */
    ns.MAX_SELECTION = 200;

    /** The two dialog modes the page knows. */
    ns.MODES = { export: "Export to AP", markpaid: "Mark Paid" };

    /** Same size as the wizard dialogs (sprk_wizard_commands.js). */
    ns.DIALOG_OPTIONS = { target: 2, position: 1, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" } };

    /** A record id without braces, lower case. */
    ns.cleanId = function (id) {
        return String(id || "").replace(/[{}]/g, "").toLowerCase();
    };

    /** The navigateTo `data` string the page reads (parseDataParams): mode and comma-separated ids. */
    ns.launchData = function (mode, ids) {
        return "mode=" + encodeURIComponent(mode) + "&ids=" + encodeURIComponent(ids.join(","));
    };

    /** 151a's download route for one batch file. `baseUrl` is the BFF host without "/api". */
    ns.fileUrl = function (baseUrl, batchId, format) {
        return String(baseUrl || "").replace(/\/+$/, "") + "/api/finance/export-batches/" + encodeURIComponent(ns.cleanId(batchId)) +
            "/files/" + encodeURIComponent(format);
    };

    /** The file name of a download: the server's Content-Disposition name, else ap-export-{batchId}.{format}. */
    ns.fileNameOf = function (contentDisposition, batchId, format) {
        var match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(contentDisposition || "");
        if (match && match[1]) {
            try {
                return decodeURIComponent(match[1].trim());
            } catch (e) {
                return match[1].trim();
            }
        }
        return "ap-export-" + ns.cleanId(batchId) + "." + format;
    };

    function alert(title, text) {
        try {
            Xrm.Navigation.openAlertDialog({ title: title, text: text });
        } catch (error) {
            console.error(LOG, title + ": " + text, error);
        }
    }

    function recordIdOf(primaryControl) {
        try {
            var entity = primaryControl && primaryControl.data && primaryControl.data.entity;
            var id = entity && entity.getId && entity.getId();
            return id ? ns.cleanId(id) : null;
        } catch (error) {
            return null;
        }
    }

    /** The value of a column on the form, or `undefined` when the form does not carry it. */
    function formValue(primaryControl, column) {
        try {
            var attribute = primaryControl.getAttribute && primaryControl.getAttribute(column);
            return attribute ? attribute.getValue() : undefined;
        } catch (error) {
            return undefined;
        }
    }

    /** The value of `column` on the open record: from the form when it carries it, else read through Xrm.WebApi. */
    function recordValue(primaryControl, table, column) {
        var id = recordIdOf(primaryControl);
        if (!id) {
            return Promise.resolve(null);
        }
        var onForm = formValue(primaryControl, column);
        if (onForm !== undefined) {
            return Promise.resolve(onForm);
        }
        return Xrm.WebApi.retrieveRecord(table, id, "?$select=" + column).then(function (row) {
            return row ? row[column] : null;
        });
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Enable rules (display only)
    // ---------------------------------------------------------------------------------------------------------------------

    /**
     * Invoice form: the open invoice has the named status ("approved" or "sentToAp"). False on a new (unsaved) form, on
     * an unknown name and when the status cannot be read.
     */
    ns.formStatusIs = function (primaryControl, statusName) {
        try {
            var wanted = ns.INVOICE_STATUS[statusName];
            if (wanted === undefined) {
                return false;
            }
            return recordValue(primaryControl, "sprk_invoice", "statuscode").then(function (value) {
                return value === wanted;
            }, function (error) {
                console.warn(LOG, "The invoice status could not be read:", error);
                return false;
            });
        } catch (error) {
            console.warn(LOG, "formStatusIs failed:", error);
            return false;
        }
    };

    /**
     * Invoice views: every selected invoice (1 to MAX_SELECTION) has the named status, read through Xrm.WebApi. False
     * on an empty or too large selection, an unknown name and a failed read.
     */
    ns.selectionHasStatus = function (selectedIds, statusName) {
        try {
            var wanted = ns.INVOICE_STATUS[statusName];
            var ids = (selectedIds || []).map(ns.cleanId).filter(Boolean);
            if (wanted === undefined || ids.length === 0 || ids.length > ns.MAX_SELECTION) {
                return false;
            }
            var values = ids.map(function (id) { return "'" + id + "'"; }).join(",");
            var query = "?$select=statuscode&$filter=Microsoft.Dynamics.CRM.In(PropertyName='sprk_invoiceid',PropertyValues=[" +
                values + "])";
            return Xrm.WebApi.retrieveMultipleRecords("sprk_invoice", query).then(function (result) {
                var rows = (result && result.entities) || [];
                return rows.length === ids.length && rows.every(function (row) { return row.statuscode === wanted; });
            }, function (error) {
                console.warn(LOG, "The selected invoices' status could not be read:", error);
                return false;
            });
        } catch (error) {
            console.warn(LOG, "selectionHasStatus failed:", error);
            return false;
        }
    };

    /** Billing Export form: the batch is Completed (only a Completed batch has files to download). */
    ns.batchIsCompleted = function (primaryControl) {
        try {
            return recordValue(primaryControl, "sprk_billingexport", "sprk_exportstatus").then(function (value) {
                return value === ns.EXPORT_COMPLETED;
            }, function (error) {
                console.warn(LOG, "The batch status could not be read:", error);
                return false;
            });
        } catch (error) {
            console.warn(LOG, "batchIsCompleted failed:", error);
            return false;
        }
    };

    // ---------------------------------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------------------------------

    function openDialog(mode, ids, onClose) {
        return Xrm.Navigation.navigateTo(
            { pageType: "webresource", webresourceName: ns.PAGE, data: ns.launchData(mode, ids) },
            Object.assign({}, ns.DIALOG_OPTIONS, { title: ns.MODES[mode] })
        ).then(onClose, onClose);
    }

    /**
     * Command on the invoice views: open the dialog in `mode` ("export" or "markpaid") for the selected invoices, then
     * refresh the grid (the invoices may have changed status).
     */
    ns.openFromGrid = function (selectedIds, selectedControl, mode) {
        try {
            if (!ns.MODES[mode]) {
                console.error(LOG, "Unknown mode:", mode);
                return;
            }
            var ids = (selectedIds || []).map(ns.cleanId).filter(Boolean);
            if (ids.length === 0) {
                alert(ns.MODES[mode], "Select one or more invoices first.");
                return;
            }
            if (ids.length > ns.MAX_SELECTION) {
                alert(ns.MODES[mode], "Select at most " + ns.MAX_SELECTION + " invoices at a time.");
                return;
            }
            openDialog(mode, ids, function () {
                try {
                    if (selectedControl && selectedControl.refresh) selectedControl.refresh();
                } catch (error) {
                    console.warn(LOG, "The grid could not be refreshed:", error);
                }
            });
        } catch (error) {
            console.error(LOG, "openFromGrid failed:", error);
        }
    };

    /** Command on the invoice form: open the dialog in `mode` for the open invoice, then refresh the form and its ribbon. */
    ns.openFromForm = function (primaryControl, mode) {
        try {
            if (!ns.MODES[mode]) {
                console.error(LOG, "Unknown mode:", mode);
                return;
            }
            var id = recordIdOf(primaryControl);
            if (!id) {
                alert(ns.MODES[mode], "Save this invoice first.");
                return;
            }
            openDialog(mode, [id], function () {
                try {
                    var refreshed = primaryControl.data && primaryControl.data.refresh ? primaryControl.data.refresh(false) : null;
                    Promise.resolve(refreshed).then(function () {
                        if (primaryControl.ui && primaryControl.ui.refreshRibbon) primaryControl.ui.refreshRibbon();
                    });
                } catch (error) {
                    console.warn(LOG, "The form could not be refreshed:", error);
                }
            });
        } catch (error) {
            console.error(LOG, "openFromForm failed:", error);
        }
    };

    function helpersLoaded() {
        return typeof Spaarke.BffAuth !== "undefined" && !!Spaarke.BffAuth.getToken &&
            typeof Spaarke.AssignedAccess !== "undefined" && !!Spaarke.AssignedAccess.getApiBaseUrl;
    }

    /** ProblemDetails `detail` (else `title`) of a failed BFF answer - the server's own words, with its stable code. */
    function problemText(response) {
        return response.text().then(function (text) {
            try {
                var problem = JSON.parse(text);
                var words = problem.detail || problem.title || ("HTTP " + response.status);
                var code = problem.errorCode || problem.reasonCode;
                return code ? words + " (" + code + ")" : words;
            } catch (e) {
                return text || ("HTTP " + response.status);
            }
        }, function () { return "HTTP " + response.status; });
    }

    /** Save a downloaded file in the browser. Replaced in the tests. */
    ns.saveBlob = function (blob, fileName) {
        var url = URL.createObjectURL(blob);
        var link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        link.style.display = "none";
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
    };

    /**
     * Command on the Billing Export form: download the batch's CSV or JSON file (`format` "csv" or "json") through 151a's
     * route. Resolves when the file is saved or a message is shown; never rejects.
     */
    ns.downloadBatchFile = function (primaryControl, format) {
        var title = "Download " + String(format || "").toUpperCase();
        try {
            if (format !== "csv" && format !== "json") {
                console.error(LOG, "Unknown format:", format);
                return Promise.resolve();
            }
            var batchId = recordIdOf(primaryControl);
            if (!batchId) {
                alert(title, "Save this export batch first.");
                return Promise.resolve();
            }
            if (!helpersLoaded()) {
                alert(title,
                    "The sign-in helper for this command is not loaded on this form. Reload the page and try again; if it " +
                    "persists, ask an administrator to check the command's libraries.");
                return Promise.resolve();
            }
            return Spaarke.AssignedAccess.getApiBaseUrl().then(function (baseUrl) {
                return Spaarke.BffAuth.getToken(baseUrl).then(function (token) {
                    if (!token) {
                        alert(title, "Sign-in needed - reload the page and retry.");
                        return null;
                    }
                    return fetch(ns.fileUrl(baseUrl, batchId, format), {
                        method: "GET",
                        headers: { Authorization: "Bearer " + token }
                    }).then(function (response) {
                        if (!response.ok) {
                            return problemText(response).then(function (text) { alert(title, text); });
                        }
                        var name = ns.fileNameOf(response.headers && response.headers.get("Content-Disposition"), batchId, format);
                        return response.blob().then(function (blob) { ns.saveBlob(blob, name); });
                    });
                });
            }).catch(function (error) {
                console.error(LOG, "The download failed:", error);
                alert(title, "The file could not be downloaded. Try again.");
            });
        } catch (error) {
            console.error(LOG, "downloadBatchFile failed:", error);
            return Promise.resolve();
        }
    };
})(Spaarke.ApExport.Ribbon);
