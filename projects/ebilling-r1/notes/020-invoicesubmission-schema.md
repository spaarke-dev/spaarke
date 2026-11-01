# Task 020 - sprk_invoicesubmission schema

> Project: ebilling-r1 (FR-10, FR-05, FR-17, FR-20, D-37). Executed 2026-10-15 against dev (`https://spaarkedev1.crm.dynamics.com`, solution `SpaarkeMaster`).
> Script: `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1`. Data model: `docs/data-model/sprk_invoicesubmission.md`.

## Result

New table created in dev and verified; the last run created and changed nothing (`created : 0, changed : 0, skipped : 29`, "nothing changed - no publish", exit 0). Nothing existing was touched or deleted. No file under `src/dataverse/solutions/SpaarkeMaster/**` and no C# file changed.

## Discovery (step 0)

`sprk_invoicesubmission` did not exist in dev (EntityDefinitions 404), so the shape-conflict escalation did not fire. All eight lookup target tables exist (`contact`, `sprk_organization`, `sprk_document`, `sprk_invoice`, `sprk_matter`, `sprk_project`, `sprk_workassignment`, `sprk_recordtype_ref`). `sprk_invoice.sprk_vendororg` is a lookup to `sprk_organization` and `sprk_invoice.sprk_correlationid` is text 100, so the same names and lengths are used here.

Naming trigger 2 (task 012 vs `sprk_regarding*`) did not fire: the main session settled it as the planned mixed scheme (task 013): the typed `sprk_matter` / `sprk_project` stay on `sprk_invoice`; new tables use the `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment` trio.

## Run history

| Run | Result |
|---|---|
| `-WhatIf` | 27 items "would create" (table, 11 columns, 10 lookups, 4 resolver fields, 1 key), no write, exit 0. |
| Apply 1 | Created everything, shortened `sprk_name` 850 -> 200, key created and Active after about 90 s, published (one 429 customization lock, retried), exit 0. The verify printed an empty max length for two string columns (stale read) - script fix below. |
| Apply 2 | Failed with exit 1: the existence read of `sprk_invoice` lookup was stale, the script POSTed the relationship again and Dataverse answered `0x80072551 NavigationPropertyName ... is not unique within an entity`. Nothing written. Script fix below (filed as DEF-008 for the other scripts). |
| Apply 3, 4 | `created : 0, changed : 0, skipped : 28`, exit 0. Apply 3's verify printed an empty target for one lookup (stale read) - script fix below. |
| Apply 5 | After a script change: set `sprk_name` required level None -> ApplicationRequired (`changed : 1`), published, exit 0. |
| Apply 6 | `created : 0, changed : 0, skipped : 29`, "nothing changed - no publish", all 26 expected columns and 10 relationships verified, key Active, exit 0. |

## Live schema (metadata read, run 6)

User-owned, entity set `sprk_invoicesubmissions`, primary name `sprk_name` (text 200, application-required). Member of `SpaarkeMaster` (root component, `rootcomponentbehavior` 0, so all subcomponents) - task 029's export picks it up.

- Text: `sprk_trackingnumber` 100, `sprk_firminvoicenumber` 100, `sprk_outcomereasoncode` 100, `sprk_ponumber` 100, `sprk_correlationid` 100, `sprk_regardingrecordid` 100, `sprk_regardingrecordname` 200, `sprk_regardingrecordnumber` 100, `sprk_regardingrecordurl` 500 (Url).
- Whole number: `sprk_submissionsequence`, min 1, max 2,147,483,647.
- Memo: `sprk_findingsjson` 1,048,576; `sprk_firmnoticetext` 100,000.
- Date and time (user local): `sprk_receivedon`.
- Local choice `sprk_channel`: SPA 100000000, Internal 100000001, Mailbox 100000002, Outlook 100000003, API 100000004.
- Local choice `sprk_processingstatus`: Received 100000000, Processing 100000001, Accepted 100000002, Rejected 100000003, Failed 100000004.
- Lookups (all Delete = RemoveLink, other cascades NoCascade): `sprk_submitter` -> contact (`sprk_invoicesubmission_submitter_contact`); `sprk_vendororg` -> sprk_organization (`..._vendororg_organization`); `sprk_document` -> sprk_document (`..._document_document`); `sprk_reportdocument` -> sprk_document (`..._reportdocument_document`); `sprk_invoice` -> sprk_invoice (`..._invoice_invoice`); `sprk_resubmissionof` -> sprk_invoicesubmission (`..._resubmissionof`); `sprk_regardingmatter` -> sprk_matter (`..._regmatter`); `sprk_regardingproject` -> sprk_project (`..._regproject`); `sprk_regardingworkassignment` -> sprk_workassignment (`..._regworkassignment`); `sprk_regardingrecordtype` -> sprk_recordtype_ref (`..._regrecordtype_recordtype_ref`). Prefix of each relationship is `sprk_invoicesubmission`.
- Alternate key `sprk_invoicesubmission_firm_trackingnumber` on (`sprk_vendororg`, `sprk_trackingnumber`); Dataverse lists the attributes alphabetically; index status **Active**.
- statecode / statuscode: system defaults only (Active 1, Inactive 2); no statuscode values were added.

## Key behaviour tests (acceptance criteria 2 and 3)

Raw Web API, dev, test rows named `ZZTEST...`, all deleted afterwards (0 `sprk_invoicesubmission` rows remain):

- Row A (firm 1, `ZZTEST-020.1`): created.
- Row A' (firm 1, `ZZTEST-020.1`): **refused**, HTTP 412, `0x80060892 "Entity Key Firm and Tracking Number violated. A record with the same value for Tracking Number, Firm already exists. A duplicate record cannot be created."`
- Row C (firm 2, same `ZZTEST-020.1`): **accepted** (the key is firm-scoped).
- A new row reads `statecode 0`, `statuscode 1`.
- `sprk_submissionsequence = 0` is refused: `0x8004432f ... outside the valid range. Minimum Value: 1`.

## Deviations and judgment calls

- **Primary name length and required level are ignored in the entity-create body** (850 and None). The script shortens the name to 200 while the table is empty (`Set-PrimaryNameLength`, from 018/019) and sets it to ApplicationRequired (`Set-PrimaryNameRequired`, from 017). The required-level step is new to the 020 script because DEF-006 (found by task 017) is the same defect.
- **Script hardening found while testing (all eventual-consistency)**: (a) a relationship create for a lookup that already exists returns `0x80072551` and must count as "exists" (after a settled read; the script throws if the column never becomes readable); (b) the verify step re-reads typed attribute metadata and lookup targets until they are readable instead of printing blanks; (c) the key create retries on MetadataCache errors for columns created seconds earlier.
- Task 013 (not this task) enforces "at most one regarding lookup" and resolver-field completeness; the resolver does not read `sprk_invoicesubmission` until 013 registers it.
- The POML says `sprk_channel` / `sprk_processingstatus` are choices with exactly the listed members; values are sequential from 100000000 in the listed order. Reserved channels (Mailbox, Outlook, API) are present but unused in R1.
- The open question in the POML notes (does this table join the secure-record family and need `sprk_createdbyperson`) stays open for 013 / uac-r2 and is repeated in the data-model doc.

## Class check (task-execute Step 9.5 item 6)

Pattern: "primary name RequiredLevel ignored in the entity-create body". Result: `sprk_ruleset.sprk_name` (019) is None, as are `sprk_billingcode` / `sprk_billingcodeset` (DEF-006). Filed as DEF-007 (#1937). `sprk_ratecard`, `sprk_ratecardrate` (017) and `sprk_invoicesubmission` are fixed.

Pattern: "lookup-create helper does not treat `0x80072551` as already-exists". `grep -L 0x80072551 scripts/ebilling/Deploy-*.ps1` lists 010, 012, 017, 018, 019 and `Deploy-EbillingApprovalSchema.ps1` (not individually verified). Filed as DEF-008 (#1938). A guard (a shared helper file) is named in the issue; not built (scope).

## Quality gates

STANDARD rigor, no C#/TS code and no `tests/**` change: code-review and adr-check not required by the protocol. ADR-002 (no plugin / business rule / calculated column / flow) and ADR-024 (three lookups, five resolver fields) were checked against the live metadata above; no ADR conflict.
