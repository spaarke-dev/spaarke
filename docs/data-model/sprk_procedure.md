# Procedure tables: `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`

> **Project**: ebilling-r1, EB-D-148 (tasks 080, 081, 089, 116; docs task 189)
> **Last Updated**: 2026-11-25
> **Solution**: `SpaarkeMaster` (unmanaged). Source: `src/dataverse/solutions/SpaarkeMaster/Entities/{sprk_procedure,sprk_procedureversion,sprk_procedureassignment}/Entity.xml`, `Other/Relationships/sprk_procedure.xml`, `Other/Relationships/sprk_procedureversion.xml` and the global choices under `OptionSets/`.
> **Scripts**: `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` (tables, columns, relationships, field security; idempotent), `scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1` (`sprk_publishedon`).
> **Rationale**: [procedure-and-file-model.md](../../projects/ebilling-r1/notes/procedure-and-file-model.md) sections 3, 5, 6. Where this page and the model differ, this page follows the deployed schema.

A **Procedure** is the internal record of a document that drives policy or rules: client billing guidelines, a firm's rate card, billing instructions, an internal standard. It is provenance only: it is never evaluated and never cited in place of a policy version. Each issue of it is a **Procedure Version**, which carries the effective dates and status, owns the policy versions and the rate card published from it, and is the parent of its source file (a [Stored File](sprk_storedfile.md)). A **Procedure Assignment** makes one core record (matter, project or work assignment) subject to a Procedure by hand, or overrides it there: pin a version, exclude the Procedure, or exclude one rule.

No plugin, business rule, calculated or rollup column and no flow (ADR-002). The invariants below (no overlapping versions, scope lock, assignment rules) are enforced by the BFF, not by schema.

```
sprk_procedure (1) ──< sprk_procedureversion (N)   ──< sprk_storedfile (source file)
      │                       ├──< sprk_policyversion.sprk_procedureversion
      │                       └──< sprk_ratecard.sprk_procedureversion
      ├──< sprk_ruleset.sprk_procedure
      └──< sprk_procedureassignment.sprk_procedure
sprk_procedureassignment ──> core record (sprk_matter | sprk_project | sprk_workassignment), exactly one set
```

"Core record" means every table in `InvoiceFamilyCoreRecordGuard.FilingCores` (`sprk_matter`, `sprk_project`, `sprk_workassignment` in R1). The assignment and the record-scoped rule set carry one typed lookup per core table.

## sprk_procedure (Procedure)

User-owned, entity set `sprk_procedures`, primary name `sprk_name`, auditing on.

### Fields

| Display name | Logical name | Type | Required | Description |
|---|---|---|---|---|
| Name | `sprk_name` | Text (200), primary name | Application (BFF write path) | Internal name. Not enforced on API writes. |
| Firm-facing Name | `sprk_firmfacingname` | Text (200) | No | What a firm sees where the rule set name is shown (D-66, D-79). Defaults to Name. |
| Area | `sprk_area` | Choice, global `sprk_policydomain` | No | Ontology area. Billing now. |
| Type | `sprk_proceduretype` | Choice, global `sprk_proceduretype` | No | Guideline, Rate card, Billing instructions, Internal Standard, Other. |
| Applies To | `sprk_appliesto` | Choice, global `sprk_procedureappliesto` | No | All records, Firm or Assigned records only. Locked once a version is Active (BFF). |
| Firm | `sprk_firm` | Lookup -> `sprk_organization` | No (BFF: required when Applies To is Firm and for a Rate card) | The firm the Procedure governs. When set, the firm condition applies from every source. Locked once a version is Active (BFF). |
| Issued By | `sprk_issuedby` | Lookup -> `sprk_organization` | No | The organization that issued the document. |
| External Contact | `sprk_externalcontact` | Lookup -> `contact` | No | The external contact for the document. |
| Internal Contact | `sprk_internalcontact` | Lookup -> `contact` | No | The internal contact for the document. |
| Status | `sprk_status` | Choice, global `sprk_procedurestatus` | No | Active or Retired. A custom choice, not `statecode`, so it can be field-secured. |
| Notes | `sprk_notes` | Multiline text (100,000) | No | Free text. |
| Created By (Person) | `sprk_createdbyperson` | Lookup -> `systemuser` | No | The person who asked for the record; stamped by the BFF (D-173 shape). |

`statecode` / `statuscode` keep the platform defaults (Active 1 / Inactive 2) and carry no Procedure meaning; the lifecycle is `sprk_status`.

**Field security.** `sprk_appliesto`, `sprk_firm`, `sprk_area`, `sprk_proceduretype`, `sprk_status` and `sprk_createdbyperson` are field-secured: read for every user, create and update for the BFF application user only (the "Spaarke BFF-Managed Field Writers" and "Readers" profiles; `Set-ChildRecordCreatorPersonSchema.ps1` for the creator column).

### Relationships

| Related table | Type | Lookup field | Delete behaviour | Notes |
|---|---|---|---|---|
| `sprk_organization` | N:1 | `sprk_firm` (`sprk_procedure_firm_organization`) | **Restrict** | A firm named by a Procedure cannot be deleted: a nulled firm would widen a firm-only Procedure to every firm. |
| `sprk_organization` | N:1 | `sprk_issuedby` (`sprk_procedure_issuedby_organization`) | RemoveLink | |
| `contact` | N:1 | `sprk_externalcontact`, `sprk_internalcontact` (`sprk_procedure_externalcontact_contact`, `sprk_procedure_internalcontact_contact`) | RemoveLink | |
| `systemuser` | N:1 | `sprk_createdbyperson` (`sprk_systemuser_sprk_procedure_createdbyperson`) | RemoveLink | |
| `sprk_procedureversion` | 1:N | `sprk_procedureversion.sprk_procedure` | **Cascade** (delete only) | Deleting a Procedure deletes its versions. See the parental note below. |
| `sprk_procedureassignment` | 1:N | `sprk_procedureassignment.sprk_procedure` | **Restrict** | An assigned Procedure cannot be deleted; retire it. |
| `sprk_ruleset` | 1:N | `sprk_ruleset.sprk_procedure` | **Restrict** | A Procedure with a rule set cannot be deleted; Signals cite its rules. |

Retiring a Procedure (set Status Retired) is the normal end of life; the BFF reconcile switches its policies Off in the same operation (`Services/Finance/Procedures/ProcedureService.cs`).

### Access classification

A Procedure has no filing lookup. Its secure-child and external-marker classification is not stated on this page; the BFF routes refuse flagged-external callers before any read (step 0, `Api/Finance/ProcedureEndpoints.cs`).

## sprk_procedureversion (Procedure Version)

User-owned, entity set `sprk_procedureversions`, primary name `sprk_name` (Text 200, application-required; for example "Version 2"), auditing on.

### Fields

| Display name | Logical name | Type | Required | Description |
|---|---|---|---|---|
| Name | `sprk_name` | Text (200), primary name | Application | Name of the version. |
| Procedure | `sprk_procedure` | Lookup -> `sprk_procedure` | **System** | The Procedure this is one issue of. Part of the alternate key. |
| Version Number | `sprk_versionnumber` | Whole number, 1 .. 2,147,483,647 | **System** | Assigned by the BFF. Part of the alternate key. |
| Previous Version | `sprk_previousversion` | Lookup -> `sprk_procedureversion` | No | The version this one replaces. |
| Effective From | `sprk_effectivefrom` | Date and time (BFF writes a date only) | No | First work date covered. Half-open `[from, to)`. |
| Effective To | `sprk_effectiveto` | Date and time (BFF writes a date only) | No | Work date the version stops covering (exclusive). Empty = open ended. |
| Status | `sprk_status` | Choice, global `sprk_procedureversionstatus` | No | Draft, Active, Superseded, Retired. A custom choice, not `statecode`. |
| Published On | `sprk_publishedon` | Date and time (UTC instant) | No | When the BFF published the version. Written once in the publish write and never moved; empty on a Draft and on a version published before the column existed. Decides whether the version opened a window (D-203). Not field-secured. |
| Not Converted | `sprk_notconverted` | Two options, default No | No | Drafted marker (task 116, EB-D-211): the BFF writes Yes last, after the rules and the not-converted list, so a redelivered draft job that reads Yes calls no model. |
| Not Converted List | `sprk_notconvertedlist` | Multiline text (100,000) | No | JSON list of the parts of the version's document that drafting could not convert into rules. Written before the marker. |
| Created By (Person) | `sprk_createdbyperson` | Lookup -> `systemuser` | No | Stamped by the BFF. |

The version has no file column: its source file is the [Stored File](sprk_storedfile.md) row (purpose Procedure source) whose `sprk_procedureversion` names it.

`statecode` / `statuscode` keep the platform defaults (Active 1 / Inactive 2); the lifecycle is `sprk_status`.

**Field security** (read for every user, create and update for the BFF application user only): `sprk_effectivefrom`, `sprk_effectiveto`, `sprk_status`, `sprk_notconverted`, `sprk_notconvertedlist`, `sprk_createdbyperson`.

### Alternate key

| Key name | Fields | Used By |
|---|---|---|
| `sprk_procedure_versionnumber` | `sprk_procedure`, `sprk_versionnumber` | One version number per Procedure; the BFF allocates the next number (`Services/Finance/Procedures/ProcedureService.cs`). |

### Relationships

| Related table | Type | Lookup field | Delete behaviour | Notes |
|---|---|---|---|---|
| `sprk_procedure` | N:1 | `sprk_procedure` (`sprk_procedureversion_procedure_procedure`) | **Cascade** (delete only) | Dataverse treats Delete = Cascade as parental and allows one per table, so this is the version's only parental relationship. Assign, share, unshare, reparent and rollup view are NoCascade: a reassign or share of the Procedure never reaches its versions. |
| `sprk_procedureversion` (self) | N:1 | `sprk_previousversion` (`sprk_procedureversion_previousversion_procedureversion`) | RemoveLink | |
| `systemuser` | N:1 | `sprk_createdbyperson` (`sprk_systemuser_sprk_procedureversion_createdbyperson`) | RemoveLink | |
| `sprk_policyversion` | 1:N | `sprk_policyversion.sprk_procedureversion` (`sprk_policyversion_procedureversion_procedureversion`) | **Restrict** | A version with policy versions cannot be deleted; Signals cite them. |
| `sprk_ratecard` | 1:N | `sprk_ratecard.sprk_procedureversion` (`sprk_ratecard_procedureversion_procedureversion`) | **Restrict** | A version with a rate card cannot be deleted. |
| `sprk_procedureassignment` | 1:N | `sprk_procedureassignment.sprk_pinnedversion` (`sprk_procedureassignment_pinnedversion_procedureversion`) | **Restrict** | Only stops deleting a pinned version, never a core record. |
| `sprk_storedfile` | 1:N | `sprk_storedfile.sprk_procedureversion` (`sprk_storedfile_procedureversion_procedureversion`) | RemoveLink | A version deleted outside the BFF leaves its file row for the orphan reconcile. |

### Rules the BFF enforces (not schema)

- **No overlap (I-P1).** Dates are date-only, half-open. A Procedure's Active versions never overlap; publishing a version from D sets the previous Active version's end to D in one operation. The resolver faults when it reads two Active versions holding one date (`Services/Signals/Billing/ProcedureResolver.cs`).
- **Scope lock (I-P4).** Once any version is Active, the Procedure's Applies To and Firm do not change; changing who a Procedure governs means a new Procedure.
- **Rate cards name their firm (I-P3).** A Procedure of type Rate card always names its Firm; `sprk_ratecard.sprk_firm` is set from it.
- **Who may publish, retire or change dates.** BFF routes only, behind the billing-admin check (`Api/Finance/ProcedureEndpoints.cs`, `Api/Finance/BillingAdminAuthorization.cs`).

### Access classification

A Procedure Version has no filing lookup. Its secure-child and external-marker classification is not stated on this page; see the Procedure section above.

## sprk_procedureassignment (Procedure Assignment)

User-owned, entity set `sprk_procedureassignments`, primary name `sprk_name` (Text 200, application-required), auditing on. One row either assigns a Procedure to a record by hand, or overrides it there. Four shapes, by `sprk_mode` and the optional lookups:

| Shape | `sprk_mode` | `sprk_procedure` | `sprk_pinnedversion` | `sprk_excludedpolicy` |
|---|---|---|---|---|
| Manual assignment | Follow latest | set | empty | empty |
| Pin a version | Pinned | set | set | empty |
| Exclude a Procedure | Excluded | set | empty | empty |
| Exclude one rule | Excluded | empty | empty | set |

Optionally `sprk_firm` limits an override to one firm's invoices. The shapes and the one-row-per-record rules are the BFF's (`Services/Finance/Procedures/ProcedureAssignmentService.cs`); the schema holds none of them.

### Fields

| Display name | Logical name | Type | Required | Description |
|---|---|---|---|---|
| Name | `sprk_name` | Text (200), primary name | Application | Name of the assignment. |
| Mode | `sprk_mode` | Choice (local) | No | Follow latest = 100000000, Pinned = 100000001, Excluded = 100000002. |
| Procedure | `sprk_procedure` | Lookup -> `sprk_procedure` | No | The Procedure assigned, pinned or excluded. Empty on a rule-level override. |
| Pinned Version | `sprk_pinnedversion` | Lookup -> `sprk_procedureversion` | No | Mode Pinned: the version used whatever is newer. |
| Excluded Rule | `sprk_excludedpolicy` | Lookup -> `sprk_policy` | No | A rule-level override switches this rule off on the record. |
| Firm | `sprk_firm` | Lookup -> `sprk_organization` | No | Limits the override to that firm's invoices. |
| Matter | `sprk_matter` | Lookup -> `sprk_matter` | No | One of the three core lookups; exactly one is set (BFF). |
| Project | `sprk_project` | Lookup -> `sprk_project` | No | As above. |
| Work Assignment | `sprk_workassignment` | Lookup -> `sprk_workassignment` | No | As above. |
| Reason | `sprk_reason` | Multiline text (2,000) | No | Why the override exists. |
| Created By (Person) | `sprk_createdbyperson` | Lookup -> `systemuser` | No | The caller who made the change; stamped by the BFF. |

`statecode` / `statuscode` keep the platform defaults.

**Field security.** Only `sprk_createdbyperson` is field-secured; the other columns are written by the BFF route, which checks the caller's own rights on the record first.

### Relationships

| Related table | Type | Lookup field | Delete behaviour | Notes |
|---|---|---|---|---|
| `sprk_matter` | N:1 | `sprk_matter` (`sprk_matter_procedureassignment_matter`) | **RemoveLink** | RemoveLink, not Cascade, because Dataverse allows one parental relationship per table (below). Never Restrict to a core record. |
| `sprk_project` | N:1 | `sprk_project` (`sprk_project_procedureassignment_project`) | **RemoveLink** | As above. |
| `sprk_workassignment` | N:1 | `sprk_workassignment` (`sprk_workassignment_procedureassignment_workassignment`) | **RemoveLink** | As above. |
| `sprk_procedure` | N:1 | `sprk_procedure` (`sprk_procedureassignment_procedure_procedure`) | **Restrict** | |
| `sprk_procedureversion` | N:1 | `sprk_pinnedversion` (`sprk_procedureassignment_pinnedversion_procedureversion`) | **Restrict** | |
| `sprk_policy` | N:1 | `sprk_excludedpolicy` (`sprk_procedureassignment_excludedpolicy_policy`) | **Cascade** (delete only) | The table's only parental relationship: a deleted rule deletes its override, so a nulled rule can never read as a Procedure-level Exclude. |
| `sprk_organization` | N:1 | `sprk_firm` (`sprk_procedureassignment_firm_organization`) | **Restrict** | A nulled firm would widen the override to every firm. |
| `systemuser` | N:1 | `sprk_createdbyperson` (`sprk_systemuser_sprk_procedureassignment_createdbyperson`) | RemoveLink | |

**Deleting a core record does not delete its assignment rows by itself.** The core-record delete planner (task 117) deletes them first, with the 117 sweep as the backstop. A row left with no core lookup applies to nothing: the resolver ignores it and never reads it as "applies to all".

### Access classification

A **secure child** of the one core record it names: lineage entries on `sprk_matter`, `sprk_project` and `sprk_workassignment` (`Services/Access/SecureChildLineage.cs`, entry `sprk_procedureassignment`), so it follows that record's isolation, including a Secure record's. Its `sprk_procedure`, `sprk_pinnedversion` and `sprk_excludedpolicy` lookups cite and file nothing (`RecordOwnershipResolver.CitationLookups`). It is never visible to external callers (hard-deny marker in the registry), and the BFF routes refuse flagged-external callers before any read.

## Global choices

| Choice | Values | Used by |
|---|---|---|
| `sprk_proceduretype` | Guideline 100000000, Rate card 100000001, Billing instructions 100000002, Internal Standard 100000003, Other 100000004 | `sprk_procedure.sprk_proceduretype` |
| `sprk_procedureappliesto` | All records 100000000, Firm 100000001, Assigned records only 100000002 | `sprk_procedure.sprk_appliesto` |
| `sprk_procedurestatus` | Active 100000000, Retired 100000001 | `sprk_procedure.sprk_status` |
| `sprk_procedureversionstatus` | Draft 100000000, Active 100000001, Superseded 100000002, Retired 100000003 | `sprk_procedureversion.sprk_status` |
| `sprk_policydomain` | Billing 100000000, Ontology 100000001, Docketing 100000002 (reserved) | `sprk_procedure.sprk_area` (shared with the policy store, see [sprk_ruleset.md](sprk_ruleset.md)) |
| `sprk_publishstate` | Draft 100000000, Published 100000001 | `sprk_policyversion.sprk_publishstate` |

`sprk_mode` on the assignment is a local choice (values above).

## Columns on other tables that point here

| Table | Column | Delete behaviour | Page |
|---|---|---|---|
| `sprk_ruleset` | `sprk_procedure` | Restrict | [sprk_ruleset.md](sprk_ruleset.md) |
| `sprk_policyversion` | `sprk_procedureversion`, `sprk_publishstate` | Restrict | [sprk_ruleset.md](sprk_ruleset.md) |
| `sprk_ratecard` | `sprk_procedureversion` | Restrict | [sprk_ratecard.md](sprk_ratecard.md) |
| `sprk_storedfile` | `sprk_procedureversion` | RemoveLink | [sprk_storedfile.md](sprk_storedfile.md) |
| `sprk_matter`, `sprk_project`, `sprk_workassignment` | `sprk_billingfirm`, `sprk_billingresponsible` (not access-conferring) | RemoveLink | [sprk_matter-related-tables.md](sprk_matter-related-tables.md) section 4 |

## BFF owners

| Area | Code |
|---|---|
| Procedure and version lifecycle (create, versions, publish, retire, drafting) | `src/server/api/Sprk.Bff.Api/Services/Finance/Procedures/ProcedureService.cs`, routes in `Api/Finance/ProcedureEndpoints.cs` |
| Assignments and overrides | `Services/Finance/Procedures/ProcedureAssignmentService.cs`, `Services/Finance/Procedures/RecordProcedureReadService.cs`, routes in `Api/Finance/RecordProcedureEndpoints.cs` |
| Which rules apply to an invoice | `Services/Signals/Billing/ProcedureResolver.cs` |
| Retiring record-scoped rule sets of deleted records | `Services/Finance/Procedures/RecordRuleSetRetireJob.cs` |
| The Procedure Version's file | [sprk_storedfile.md](sprk_storedfile.md), [EBILLING-FILE-STORE.md](../architecture/EBILLING-FILE-STORE.md) |
