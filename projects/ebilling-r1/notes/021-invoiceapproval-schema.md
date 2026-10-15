# Task 021 — sprk_invoiceapproval schema (notes)

> Run 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`). Script: `scripts/ebilling/Deploy-EbillingApprovalSchema.ps1`. Rigor STANDARD. Data model: `docs/data-model/sprk_invoiceapproval.md`.
> **Status: DONE. No escalation fired.**

## 1. Discovery (before any write)

- `sprk_invoiceapproval` did not exist (404).
- Escalation trigger 1 (D-33 fields not all contact): did **not** fire. Every assignment lookup that exists on `sprk_matter`, `sprk_project` and `sprk_workassignment` targets `contact` (attorney 1 / 2, paralegal 1 / 2, to internal, to external on all three; `sprk_assignedto` on Work Assignment only). `contact.sprk_systemuser` targets `systemuser`. The script repeats this read-only check on every run and stops with exit 2 before any write if it ever fails.
- Escalation trigger 2 (task 012 chose different lookup names): settled by the main session. Task 012 kept the typed `sprk_matter` / `sprk_project` on `sprk_invoice` and added `sprk_regardingworkassignment` (task 013's mixed scheme); this new table uses `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment`.
- Observations for other tasks (filed as DEF-011): `sprk_matter.sprk_billingapproverrole` is a single-select picklist, Project and Work Assignment are multi-select; Matter and Project have no `sprk_assignedto` lookup, so the option "Assigned To" resolves only on Work Assignment.

## 2. Runs

| Run | Result |
|---|---|
| `-WhatIf` | 17 creations planned, 0 writes, exit 0. |
| Apply 1 | Table, `sprk_name` required level, choice, money, two dates, 7 lookups and 4 resolver fields created (two `0x80040216` and one `0x80071151` publish-lock retries handled by backoff). Failed at the end with exit 1: the N:N was created but its read did not appear within 60 s (metadata lag; a manual read a minute later found it). Nothing lost. |
| Apply 2 | Failed with exit 1: `0x8009050d Entities already exist` (stale table read, then a refused create) — not matched by the copied regex. Script fixed (see deviations). |
| Apply 3 | `changed : 1` (`sprk_name` length 850 -> 200), published, verified, exit 0. |
| Apply 4 | Added `Set-LookupRequiredLevel` (SystemRequired, see below). `-WhatIf` planned 1 change; apply changed `sprk_invoice` ApplicationRequired -> SystemRequired, published, exit 0. (Before this run `sprk_invoice` was reset to ApplicationRequired by hand to exercise the change path.) |
| Apply 5 (after adding `0x80072551`) and final `-WhatIf` | `created : 0  changed : 0  skipped : 19-20`, "nothing changed - no publish", exit 0. |

## 3. Live dev schema changes (all additive)

Table `sprk_invoiceapproval` (user-owned, entity set `sprk_invoiceapprovals`, in unmanaged `SpaarkeMaster`, component type 1 confirmed). Columns: `sprk_name` (text 200, application-required), `sprk_approvalstatus` (local choice Pending 100000000 default, Approved 100000001, Rejected 100000002, Returned 100000003), `sprk_approvedamount` (money, precision 2, plus `_base`), `sprk_eligibleresolvedon`, `sprk_decidedon` (date and time, user local), resolver text `sprk_regardingrecordid` (100), `sprk_regardingrecordname` (200), `sprk_regardingrecordnumber` (100), `sprk_regardingrecordurl` (500, Url). Lookups (all delete RemoveLink, other cascades none): `sprk_invoice` -> `sprk_invoice` (**SystemRequired**), `sprk_decisionrecord`, `sprk_decidedby` -> `contact`, `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingworkassignment`, `sprk_regardingrecordtype` -> `sprk_recordtype_ref`. Relationships: `sprk_invoiceapproval_invoice_invoice`, `sprk_invoiceapproval_decisionrecord_decisionrecord`, `sprk_invoiceapproval_decidedby_contact`, `sprk_invoiceapproval_regmatter`, `sprk_invoiceapproval_regproject`, `sprk_invoiceapproval_regworkassignment`, `sprk_invoiceapproval_regrecordtype_recordtype_ref`. N:N `sprk_invoiceapproval_eligibleapprover_contact` (intersect of the same name; menu labels "Eligible Approvers" / "Invoice Approvals (eligible approver)"). System statuscode left at 1 Active / 2 Inactive. No file under `src/dataverse/solutions/SpaarkeMaster/` changed.

## 4. Acceptance evidence (independent raw Web API read, then live tests)

- Metadata read: all columns and lookups above with the stated types and targets; the choice has exactly the four values.
- Test row with an existing invoice: created, default `sprk_approvalstatus = 100000000` (Pending); one contact associated through `sprk_invoiceapproval_eligibleapprover_contact`; read back through the approval's navigation, through the contact's reverse navigation, and through the view-style filter `sprk_invoiceapproval_eligibleapprover_contact/any(c:c/contactid eq <id>)` (1 row each). The test approval was deleted afterwards (the intersect row goes with it); the table holds 0 rows. The existing invoice was not modified. A stray test row without an invoice was created once, before the SystemRequired change (see deviation 1), and deleted by id.
- Create without `sprk_invoice`: **refused** by the platform, `Attribute 'sprk_invoice' cannot be NULL. Source: RequiredFieldValidator.ValidateForCreate`.
- Second run: `created : 0  changed : 0`, no publish, exit 0; `-WhatIf` makes no change.

## 5. Deviations and judgment calls

1. **`sprk_invoice` is SystemRequired, not ApplicationRequired.** The POML says "required"; with ApplicationRequired the criterion "creating a row without it is refused" failed live (the API accepted the row). SystemRequired is enforced by the platform on every create and update and the metadata API accepts it on a custom lookup. The cost: the level cannot be relaxed except by another metadata change, and Dataverse cannot clear the lookup when an invoice is deleted (invoices are closed by status, not deleted). Same class in earlier schema scripts: DEF-009.
2. **Script helper fixes** (copied helpers had latent defects, fixed here, filed for the others as DEF-010): `0x8009050d` and `already exist` recognised; `Set-PrimaryNameLength` counts rows with `$count=true` and repeats until a count appears (a false "has rows" left the name at 850); N:N visibility wait 240 s; `0x80072551` added (DEF-008) and a duplicate refusal now fails if the item never becomes readable. The duplicate-refusal branch of the lookup helper was not exercised live (no stale read occurred in the final runs).
3. **Delete = RemoveLink on the required lookup** was accepted by Dataverse as written in the POML; kept.
4. **`sprk_approvalstatus` is a local choice with a default**, created with `DefaultFormValue` (Dataverse honoured it; read back 100000000).
5. **"One portion per invoice"** is not a schema constraint (no alternate key on `sprk_invoice`); the writer (task 103) owns it. Left open on purpose because multi-portion approval is a planned extension.
6. Open for 013 / uac-r2 (unchanged from the POML): secure-record family membership and `sprk_createdbyperson` for this table.

## 6. Quality gates and class check

- STANDARD rigor, configuration-only task (no `tests/**`, no BFF or app code): Step 9.5 code-review and adr-check skipped per the skip rule; checked by hand against ADR-002 (no plugin, business rule, calculated column or flow; the approver store is written by task 103 on the server), ADR-024 (entity-specific lookups plus the five resolver fields), CLAUDE.md section 11 (justification in the POML) and the project constraints (additive only, no delete or rename of schema, SpaarkeMaster source untouched).
- Class check (Step 9.5 item 6) for two repeatable defects found: (a) "ApplicationRequired assumed to be enforced on API writes" — grep of `scripts/ebilling` lists `sprk_ratecardrate.sprk_ratecard`, `sprk_billingcode.sprk_codeset` / `sprk_codetype` and the primary-name steps of 017-020; listed in DEF-009, not verified or changed (other tasks' scripts). (b) "stale existence read not treated as already-exists" and "false has-rows" — grep lists the 6 scripts / 4 scripts in DEF-010. A guard that would catch both: a shared helper file for the eBilling schema scripts (type-checked by being the only copy); not built (scope change, ask).
- Defects filed: DEF-009 (#1939), DEF-010 (#1940), DEF-011 (#1941).

rework: fix-round-1 task 021
