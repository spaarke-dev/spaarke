# Rule set scope: client default, firm, core record, firm + core record, business-unit filter

> **Status**: DESIGN NOTE, draft for review (2026-10-20). Nothing here is built, and nothing in it is decided except the owner's own words, which are quoted in section 1. Every other statement marked **Proposal** needs a decision first: from the owner, ontology-r1 or uac-r2, as named in section 9.
> **Source**: GitHub #2129, ontology decision **D-171** (owner, 2026-10-20). It extends ebilling D-39. Recorded in the ebilling decisions log as **D-136**.
> **Branch read**: `work/ebilling-r1` at `b046154af`. File and line references are to that commit.
> **Schema items are marked "needs owner approval before running".** None of them has run.

## 1. Decision summary (D-171, quoted from #2129)

| Topic | Owner decision |
|---|---|
| Levels | "Client default, Firm, **core record** (Matter, Project or Work assignment) and **firm plus core record**. Scope stays on the rule set; copy `sprk_ratecard`'s optional core-record scope." |
| Precedence | "**Most specific replaces, per rule type**: firm+record > record > firm > client default. This extends D-39. ... rule types not covered at a specific level fall through." This supersedes the earlier "all apply" answer. |
| Inheritance | "Yes. A Matter's rule sets also apply to invoices on its Projects and Work assignments." |
| Client level | "**No separate client record; use the business unit.** A rule set applies to **all business units by default**, or only to a chosen list ... When a list is set and the record's business unit is not on it, the rule set does not apply." |
| Ownership (Q3) | Route the `sprk_ruleset` / `sprk_policy` / `sprk_policyversion` creates through `IRecordOwnershipResolver` with the core record as parent. The census moves to Routed. |
| Firm visibility (Q4), re-own on a Secure change (Q5) | Unchanged from the issue body. |

**Census state today.** The waiver is `WaiverKind.Pending` on #2129 (4d51138e9, uac-r2 conditions: the open decision is named, the end of the waiver is stated, and the guard tests are kept until the routing change). Section 4.4 moves it to Routed.

## 2. Schema (needs owner approval before running)

There is one script, `scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1`, written on `EbillingSchema.psm1`. It is idempotent, supports `-WhatIf`, and refuses to relabel or renumber an existing option (as `Deploy-EbillingSubmissionIntegrityOption.ps1` does). The SpaarkeMaster sync is **not** done by the schema task. Under the pipeline rule in project CLAUDE.md §3, it goes with deploy task **079** (Deploy WP3, still open), using 029's export-to-temp procedure. 079 also adds the new columns to the rule set form and views.

| # | Item | Additive? | Notes |
|---|---|---|---|
| S1 | `sprk_ruleset.sprk_matter`, `sprk_project` and `sprk_workassignment`: lookups to the three core tables. Relationships `sprk_matter_ruleset_matter`, `sprk_project_ruleset_project` and `sprk_workassignment_ruleset_workassignment` (the rate-card naming). | Yes | Copied from `sprk_ratecard` (`scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1:431-436`). **One change from the copy: delete behaviour `Restrict`, not the module default `RemoveLink`** (`EbillingSchema.psm1:337`). With RemoveLink, deleting a matter in the model-driven app would clear the lookup. A rule set that had applied to one matter would then have no record, and under a derived scope it would read as a firm or client-default set and apply everywhere. D-120's delete path then deletes the set explicitly (section 9, Q10). |
| S2 | `sprk_scope`: **new option values** `Record = 100000002` and `FirmRecord = 100000003` on the existing local choice (ClientDefault 100000000 and Firm 100000001 are unchanged). | Yes (options inserted, none relabelled) | **Proposal: keep an explicit scope; do not derive it from which lookups are set.** The reason is fail-closed behaviour. A Record-scoped set whose lookup was cleared then applies to **nothing** (the loader requires the lookup), instead of silently widening to firm or client default. The explicit value also keeps the existing publish check, which refuses an unknown scope (`RuleSetService.cs:1541-1555`). The rate card derives its scope (`RateCardResolver.cs:133-142`), but a card that loses its record widens only one firm's rates. |
| S3 | `sprk_ruleset.sprk_allbusinessunits`: Two-option, display "All business units", **default Yes**. | Yes | It keeps the owner's default ("applies to all business units") explicit. With No and an empty list, the set applies to nothing, and publish refuses it (`ruleset.publish_invalid`, field `businessUnits`). An empty list therefore never means "all". |
| S4 | The business-unit list: an **N:N** between `sprk_ruleset` and `businessunit`, `sprk_ruleset_businessunit` (intersect `sprk_ruleset_businessunit`). | Yes (new relationship) | See the options below. Precedent in this project: `sprk_invoiceapproval_eligibleapprover_contact` (`scripts/ebilling/Deploy-EbillingApprovalSchema.ps1:168`). **Pre-check in the script:** read `EntityDefinitions(LogicalName='businessunit')?$select=CanBeInManyToMany`, and if it is false, stop and fall back to option (b). |
| S5 | Security-role rows: `config/secure-record-owner-role.json` gains `sprk_ruleset` (kind `child`, `prvReadsprk_ruleset`), as `sprk_policy` did at `:281` (#2084). Check the **Read depth** that Basic User holds on `sprk_ruleset`: 076 notes §1 say "Core / Basic User hold Read on `sprk_ruleset`". If that depth is Organization, a Secure-team owner hides nothing. | Role change (uac-r2 owns the file) | uac-r2 review. Section 5. |

**Options for storing the business-unit list (S4)**

| Option | For | Against |
|---|---|---|
| **(a) N:N to `businessunit` + the S3 flag (recommended)** | Native and referentially sound: a deleted business unit drops out of the list. One link-entity query in the loader. An admin subgrid comes free on the model-driven form. | Associate and disassociate are a separate write path, not one row update, so publish must re-read the list (ADR-002: the form bypasses the BFF). It depends on `businessunit` allowing N:N (S4 pre-check). |
| (b) An intersect child table `sprk_rulesetbusinessunit` (rule set + BU lookups) | It always works. It can carry its own audit columns. | It is a new table: §11 asks why the N:N is not enough. It is also one more ownership row for the census and for the Secure wall. |
| (c) JSON or text column of BU ids | No new schema surface. | No referential integrity, not queryable, invisible on the form. Rejected. |
| (d) Multi-select choice | — | Business units are data, not a fixed list. Rejected. |

**Signals reader impact: none.** The Signals readers of `sprk_policy` (`SignalReevaluationJob.ReadEnabledPoliciesAsync`, `PolicyScopeResolver.LoadEnabledPoliciesAsync`; inventory in `notes/019-policy-store-ruleset.md`) read no `sprk_ruleset` column. The design writes **no** core record onto `sprk_policy`. `sprk_policy.sprk_matter` stays the ontology's (#2084) and is never set on a billing row (section 4.4). No `sprk_policy` or `sprk_policyversion` column is added.

**Additive order is safe.** With S1-S4 deployed and no code changed: the loader selects only scope ClientDefault and Firm (`BillingFactLoader.cs:300-315`), so a Record set is never evaluated; publish refuses an unknown scope (`RuleSetService.cs:1548`); and `GetFirmClauseAsync` returns None (`:2053-2056`). Nothing widens before the code lands.

## 3. Selection (BillingFactLoader and InvoiceComplianceEvaluator)

The work splits the way the code does today. The **loader** decides which rule sets are *eligible* for this invoice: the level applies and the business-unit filter passes. The **evaluator** applies *precedence* per subject, where D-39 already lives (`InvoiceComplianceEvaluator.cs:264-299`).

### 3.1 The core-record chain (inheritance)

Input: `BillingEvaluationRequest.CoreRecord` (`BillingFactLoader.cs:26-27`), the invoice's own core record as the submission stamped it.

| Invoice's core record | Chain, nearest first (distance 0, 1, 2) | How each parent is read |
|---|---|---|
| Matter M | M | — |
| Project P | P → M (if P is filed under a matter) | A project has **no typed matter lookup**. Its filing is the BFF-managed ADR-024 pair `sprk_regardingrecordid` / `sprk_regardingrecordtype`, which is field-secured, so only the BFF writes it (`SecureRootInheritance.cs:401-404` and `:482`, `PairNamesAParentOnItsOwn`). |
| Work assignment W | W → P (if `sprk_regardingproject`) → M (P's pair), and W → M (if `sprk_regardingmatter`) | The typed lookups only (`TypedFilingColumns`, `:401`). A W pair that no typed lookup repeats is **not** a filing (#1616) and is ignored. **Proposal:** follow #1630's intermediate filing as well, so that a W filed under an invoice or event inherits that intermediate's matter or project (`IntermediateFilingColumns`, `:429`). |

- **Proposal (owner, Q6):** a **Project's** rule sets also apply to its work assignments' invoices. D-171 names only the Matter, but the same reading ("a parent's sets reach its children") gives this.
- Inheritance runs **downward only**. A project's set never applies to its parent matter's own invoices.
- **Reuse, not a new map.** The chain comes from uac-r2's one definition of root-to-root filing: `SecureRootInheritance.TypedFilingColumns`, the project pair rule and `IntermediateFilingColumns`. Calling `FindFilingParentsAsync` (`SecureRootInheritance.Cascade.cs:401`) needs the scoped provisioning graph. **Proposal:** a small internal `CoreRecordChain` reader in `Services/Signals/Billing/`. It reads at most three rows app-only and reuses those static maps (§11: existing = `SecureRootInheritance` maps; extension = the read is ours, the maps are theirs; cost of doing nothing = a project invoice never sees its matter's terms).
- **Fail closed:** a chain row that cannot be read is a Dataverse fault and propagates (the loader's contract, `BillingFactLoader.cs:243-244`). The job retries. The invoice is **never** judged on a shortened chain, because that would silently drop the matter's negotiated terms.

### 3.2 Eligibility (loader)

```
chain    = CoreRecordChain(request.CoreRecord)        // [(table, id, distance)], empty when no core record
bu       = HomeBusinessUnit(request.CoreRecord)       // section 5.2; null = unknown
sets     = Published billing sprk_ruleset rows WHERE
             scope = ClientDefault
          OR (scope = Firm       AND sprk_firm = firm)
          OR (scope = Record     AND exactly one core lookup set AND that lookup ∈ chain)
          OR (scope = FirmRecord AND sprk_firm = firm AND exactly one core lookup set AND that lookup ∈ chain)
         (the query filters, then every row is re-checked, as BillingFactLoader.cs:304-315 does today)
for each set:
    if not set.allBusinessUnits:
        list = set's BU ids (one link-entity query for all sets, chunked at MaxInValues)
        if bu is null or bu ∉ list: drop the set          // BU filter = eligibility
    candidate.Level      = ClientDefault 0 | Firm 1 | Record 2 | FirmRecord 3
    candidate.Distance   = chain distance of the set's record (0 for ClientDefault/Firm)
    candidate.BuSpecific = not set.allBusinessUnits
```

`BillingRuleCandidate.Scope` (`BillingFactLoader.cs:76`) is replaced by `Level`, `Distance` and `BuSpecific`. `InvoiceSubmissionAfterSteps.cs:1256` builds the system policy's candidate and sets `Level = ClientDefault` in its place (D-125: never read for that policy). Policies and versions are loaded as today (`:324-379`).

### 3.3 Precedence (evaluator): most specific replaces, per rule type

**What "rule type" means today.** It is `BillingRuleDefinition.RuleType`: the `RuleType` enum that the gate parses from `sprk_policyversion.sprk_ruletype`. The evaluator applies it **per subject** (the invoice, and each line by its index), among the versions selected for that subject's date. Only a firm version that the gate accepts, the handler accepts and that is **not downgraded** counts as "present" and displaces the client default of its type (`InvoiceComplianceEvaluator.cs:272-283`; 072 review B2, D-116). That rule is generalised from two ranks to a lexicographic rank:

```
rank(v) = (v.Level, -v.Distance, v.BuSpecific ? 1 : 0)
   // FirmRecord > Record > Firm > ClientDefault;
   // within a record level, nearer record wins (W > P > M);
   // at the same level and distance, a BU-listed set beats an all-BU set (#2129 point 3)

for each subject s:
    V = versions selected for s on its date        // unchanged: line date, else invoice date, else ReceivedOn (:175-182, A3)
    for each rule type t:
        D(t) = max rank over v in V of type t where gate-accepted AND handler-accepts AND not downgraded
               (none → no displacement for t)
        keep v of type t   iff  gate-accepted AND handler-accepts AND (D(t) is none OR rank(v) >= D(t))
```

What follows from it:

- **A broken specific rule never removes a check.** A matter version that the handler refuses does not set `D`, so the firm or client-default rule stays in force. This is B2, unchanged.
- **A downgraded specific version runs as a Flag beside the next level.** It does not set `D`, and its rank is ≥ `D`, so it is kept. This is F1-b, unchanged.
- **Ties at the same rank** (for example, two matter-level sets in force with the same type): **all apply**. That is what the code does today for two firm sets or two client-default sets. **Proposal:** keep it, and add a non-blocking warning at publish: "another published rule set at this level for this record already has a <type> rule; both will apply". It is not a refusal, because a refusal would also block a deliberate pair.
- **In force on which date (D-74).** Per-subject version selection is unchanged. Rule-set rows carry `sprk_effectivefrom` / `sprk_effectiveto`, but the loader does not read them: a version's `sprk_inforcefrom` / `sprk_inforceto` decides. D-74's adjustment rule list (task 102) reuses this loader and precedence with the invoice date.
- **Determinism:** the rank is total over (Level, Distance, BuSpecific). Ties keep the existing ordinal order (policy code, then id).

### 3.4 Worked example

Firm F. Matter M is in business unit "Litigation". Project P is filed under M. The invoice is from F on P, so the chain is P (distance 0) → M (distance 1).

| Rule set | Level / record / BU | Rules (type: parameter, consequence) |
|---|---|---|
| CD | ClientDefault, all BUs | ApprovedRate tolerance 5% Flag; TimeIncrement 0.1; InvoiceAge 90 days Reject; ExpenseRule (cap $500); RequiredData (task code) |
| CD-Lit | ClientDefault, BU list = {Litigation} | NonBillableActivity {L100 admin} |
| FIRM | Firm F | InvoiceAge 120 days Reject; DailyHoursCap 10 |
| MAT | Record = M | ApprovedRate tolerance 0% Reject |
| F+M | FirmRecord F + M | TimeIncrement 0.25 |
| PRJ-other | Record = P2 (another project) | DailyHoursCap 8 |

| Rule type | Candidates eligible for the invoice on P | Winner | Why |
|---|---|---|---|
| ApprovedRate | CD (0,0,0), MAT (2,-1,0) | **MAT** | Record beats ClientDefault; inherited from M |
| TimeIncrement | CD, F+M (3,-1,0) | **F+M** | Firm + record is the most specific |
| InvoiceAge | CD, FIRM (1,0,0) | **FIRM** | D-39, as today |
| DailyHoursCap | FIRM | **FIRM** | PRJ-other is not on the chain, so it is not eligible |
| NonBillableActivity | CD-Lit | **CD-Lit** | P's business unit is Litigation (section 5.2). If it were not, CD-Lit would not be eligible and nothing would apply |
| ExpenseRule, RequiredData | CD | **CD** | Not covered at a more specific level: they fall through |

The owner's "$650/hr against $500/hr" is **rate data, not a rule** (D-23: rules never copy rates). It is a matter-scoped **rate card** that ApprovedRate reads through `RateCardResolver`. The rule set decides the tolerance and the consequence. **There is a gap here:** `RateCardResolver` matches a card's core record **exactly** (`RateCardResolver.cs:133-142`), so M's card does not apply to P's invoice today. D-171's inheritance implies that it should. See section 9, Q8.

## 4. RuleSetService

### 4.1 Scope and lookup validation

This applies to create (`:387-399`), assign (`:1924-1935`) and **publish** (`:1541-1555`, which re-checks because model-driven edits bypass the BFF). All three return `400 ruleset.invalid` (on publish, `ruleset.publish_invalid`) with field errors (`RuleSetError`):

| Scope | `sprk_firm` | Core lookups | Field error when broken |
|---|---|---|---|
| ClientDefault | none | none | `firmId`: "A client default rule set takes no firmId." / `coreRecord`: "A client default rule set takes no core record." |
| Firm | required, exists | none | `firmId` (existing text) / `coreRecord`: "A firm rule set takes no core record." |
| Record | none | **exactly one** of matter, project or work assignment, which must exist | `coreRecord`: "Name exactly one of matterId, projectId and workAssignmentId." / "The core record was not found." |
| FirmRecord | required, exists | exactly one, which must exist | both, as above |
| any | — | — | `businessUnits`: "Choose at least one business unit, or apply the rule set to all business units." / "Business unit <id> was not found." |

**Inputs** follow the 078 doctrine (`RateCardImportEndpoints.cs:20-22` and `:166-187`). `matterId`, `projectId` and `workAssignmentId` are **query** parameters on create and on `PUT /{id}/assignment`, so the authorization filter and the handler read the same values (task 130 defect C8). The business-unit ids go in the assignment body (`RuleSetAssignmentBody` gains `AllBusinessUnits` and `BusinessUnitIds`). They need no record check, because a business unit is not secured data, but the service checks that each one exists.

**Authorization on the core record.** The rate card checks only **Read** (`finance.read`, `RateCardImportEndpoints.cs:120-133`), not AppendTo. The brief's "AppendTo as the 017 rate card does" does not match the code. **Proposal:** require **Read and AppendTo** as the caller, because the set's lookup is filed *at* the record, which is the `finance.submit_invoice` reasoning (`src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:195-201`). It would use a new key, `ebilling.ruleset.scope` = `Read | AppendTo`. Reusing `finance.submit_invoice` would describe the act wrongly in every deny, the precedent noted at `:251-253`. Refusal: 403 `sdap.access.deny.*` from the filter, before the service runs. Whether the rate card import should also move to AppendTo is a separate question (section 9, Q12).

### 4.2 Business-unit list writes

The service writes the S3 flag and, in the same call, does associate / disassociate against the N:N, as the app identity (like every other rule-set write). Associates are idempotent. A failure part-way through is reported as `500 ruleset.write_failed`, and the next call converges it. **Publish re-reads the list** and refuses No-plus-empty. The list is returned on `RuleSetSummary` (new members `AllBusinessUnits` and `BusinessUnitIds`, both additive).

### 4.3 Changing a published set's scope

Today `AssignAsync` changes the scope or firm of a **Published** set; it refuses only a Retired one (`:1919-1922`). Under D-171 that re-targets rules already in force, and does so retroactively for any re-evaluation (task 101). **Proposal: refuse.** Changing the scope, firm, core record or BU list of a Published set returns `409 ruleset.scope_locked` ("Retire this rule set and create a new one for the new scope."). On a Draft set the change is allowed, and it re-owns the set and its rows (section 4.4). A later "copy to a new rule set" action can make the retire-and-recreate path cheap. This is a behaviour change for Firm sets too, so the owner needs to confirm it (Q9).

### 4.4 Routed ownership

| Set level | Owner resolution for `sprk_ruleset`, `sprk_policy` and `sprk_policyversion` |
|---|---|
| Record, FirmRecord | `ResolveOwnerAsync(RecordOwnershipContext.ForParents([core record], caller) with { RequestedBy = caller })`, as `RateCardImportService.cs:200-211` does. A Secure core record resolves the Secure team (secure-if-any), and an ordinary one resolves the record's team. A refusal writes nothing (`OwnerRefused`, 409). |
| ClientDefault, Firm | Unchanged: no parents, so the caller's business-unit team (`OwnerContext`, `RuleSetService.cs:2389-2393`). |

The call sites that change are create (`:405`), `AddDraftsAsync` (`:871`, which creates the policies and versions in `CreateDraftRuleAsync`, `:1060-1100`), and `EditAsync`'s new version (`:1277`, the `VersionIdFor` create that the scanner cannot see). Each passes `OwnerContext(caller, setCore)`, where `setCore` is the set's lookup read in the same load. A Draft scope change (section 4.3) re-owns the set, its policies and its versions through `IRecordOwnershipResolver.ReparentAsync` (`RecordOwnershipResolver.cs:146`). That makes it a REPARENT, so it is listed in the census's `UnscannedWriters` (`RecordOwnerAssignmentCensusTests.cs:306`).

**Census and guard, replaced in the same change:**

- Add `sprk_ruleset` to `ChildTables` (`RecordOwnerAssignmentCensusTests.cs:47-61`). The `sprk_policy` and `sprk_policyversion` entries (`:265-266`) go from `Waived` to **`Routed`**. A new entry, `("RuleSetService.cs", "sprk_ruleset", 1, Routed)`, is added, and `RuleSetServicePolicyWaiver` (`:127-139`) is deleted.
- `RuleSetServiceNoMatterScopeGuardTests` is **replaced**, not deleted. Its blanket ban on any matter identifier in `RuleSetService.cs` would fail the new code. The new guard is narrower: **no `sprk_matter` is ever written on a `PolicyEntity` or `VersionEntity` row** (that column is the ontology's matter scope). It has a must-fire control (a planted `policy["sprk_matter"] = …` fails) and a must-not-fire control (`ruleSet["sprk_matter"] = …` passes). The behavioural twin `RuleSetServiceOwnerTests.NoWrittenPolicyOrVersionRowCarriesAMatter` stays as it is.
- `RuleSetServiceOwnerTests` adds: a record-scoped create passes exactly `[core record]` as parents; a Secure matter yields the Secure team as owner of the set, policy and version; and a client-default create still passes none.
- The `AssignCallers` reason for `RuleSetEndpoints.AssignRuleSet` (`:770-772`) is updated: a Draft re-scope now re-owns.

## 5. Ownership and Secure records

### 5.1 Lineage (uac-r2 files, uac-r2 review)

| Table | Change | File |
|---|---|---|
| `sprk_ruleset` | **New child entry**: `("sprk_matter", Matter)`, `("sprk_project", Project)`, `("sprk_workassignment", WorkAssignment)` | `Services/Access/SecureChildLineage.cs` (beside `sprk_policy`, `:175-178`) |
| `sprk_policy` | Add `("sprk_ruleset", "sprk_ruleset")` next to the existing `("sprk_matter", Matter)` | same |
| `sprk_policyversion` | Unchanged: it reaches the matter through `sprk_policy` → `sprk_ruleset` | — |
| `OwnershipParentEntities` | Add `sprk_ruleset` (a rule set is the parent of its policies, as `sprk_policy` is of its versions, `:1040-1043`) | `Services/Dataverse/RecordOwnershipResolver.cs:1024` |
| `CitationLookups` | Unchanged. `sprk_signal.sprk_policy` still cites rather than files (`:1061-1062`), so a Signal is never pulled into a matter set's wall through the rule it cites. | — |

Without the `sprk_policy` → `sprk_ruleset` edge, a billing policy has no lineage to its matter, because `sprk_policy.sprk_matter` is never set on billing rows. Securing the matter later (Q5) would then leave the rule set's policies and versions in the ordinary business unit. With the edge, `FilingParentOf` (`RecordOwnershipResolver.cs:610`) and `SecureChildReconciler` re-own the set, its policies and its versions on secure and unsecure. `SecureChildLineageTests` and `SecureFamilyConsistencyTests` change with it.

### 5.2 Which business unit is matched (Proposal for uac-r2)

- **Proposal: the invoice's own core record's "home" business unit.** That is the record the invoice is filed under, not the rule set's record and not an ancestor's.
- **The problem:** a Secure matter is re-owned to the Secure Record Owners team, so its `owningbusinessunit` becomes the Secure Record business unit (`RecordOwnershipResolver.cs:86-87`, `:2181`). There is no column today that keeps the business unit it had before. `sprk_matter` has only the system `owningbusinessunit` (`docs/data-model/sprk_matter-related-tables.md:96`). Unsecuring reassigns the record to a configured or calling user (`UnsecureProjectRequest.cs`, `ReassignToSystemUserId`), not back to a stored business unit.
- **Proposal A (recommended, uac-r2 decides):** a lookup to `businessunit` on matter, project and work assignment, for example `sprk_homebusinessunit`. It is stamped from the owner's business unit at create (and on an ordinary re-own) and is **never** changed by a secure re-own. The loader reads `sprk_homebusinessunit ?? owningbusinessunit`. The column is uac-r2's schema, and it is needed elsewhere too: unsecure could return the record to it.
- **Interim (until A exists), Proposal B:** when the core record is owned in the Secure Record business unit, its home business unit is **unknown**. Only all-BU sets are eligible. A BU-restricted set does not apply, and the evaluator raises **one invoice-level, flag-only finding**: "Business-unit rule sets were not checked: the matter's business unit is not known." This follows the D-115 currency-skip pattern, so a rule that is silently missing is visible. The Secure Record business unit is never offered in the admin's business-unit picker, and it never matches a list.
- **Proposal: exact business unit only.** A child business unit does not match a list that names its parent business unit (Q13).

### 5.3 Who can see a matter-scoped rule set

- **Dataverse (model-driven):** a set on a Secure record is owned by the Secure team, so a non-member does not read it, **provided** `sprk_ruleset` Read is business-unit depth for ordinary roles (S5).
- **Rule Review page (BFF):** `ListAsync` and `GetAsync` read app-only behind the billing-admin privilege (`RuleSetService.cs:516-551`, `BillingAdminAuthorization`). As things stand, every billing admin would see the name of a Secure matter's rule set and its link to the matter. **Proposal:** List and Get drop, or 404, a record-scoped set whose core record the caller cannot **Read**, checked as the caller with one batched check per page. Every write to a record-scoped set requires Read and AppendTo (section 4.1). uac-r2 to confirm.
- **Evaluation:** app-only, unaffected (the loader's contract).

## 6. Firm visibility (D-66, D-79)

**Proposal:** a firm sees a record-scoped set's clauses **only on invoices whose core-record chain contains that record**, and a FirmRecord set only when it is also the set's firm.

- **`GetFirmClauseAsync(policyVersionId, invoiceFirmId, ct)`** (`RuleSetService.cs:2013-2063`) gains the invoice's core record. Today it returns None for any scope other than ClientDefault or Firm (`:2052-2056`). That is fail-closed, but wrong once Record sets exist. The method should call **the same applicability predicate the loader uses**, extracted as one pure function, `RuleSetApplicability.AppliesTo(set, firmId, chain, homeBu)`. A clause is then shown to a firm exactly when the rule could have judged that invoice. The caller contract (`:2006-2012`) adds: "the invoice's core record, read server-side from the invoice". No caller exists yet, so changing the signature now costs nothing.
- **The `sprk_visibletofirms` flag** (D-79) still decides on top.

**Readers that would break or need the change** (search of `src` on `b046154af`):

| Reader | Where | Impact |
|---|---|---|
| `BillingFactLoader.LoadRulesAsync` | `BillingFactLoader.cs:280-379` | Scope query and the `RuleSetScope` mapping (section 3). |
| `InvoiceComplianceEvaluator` D-39 block | `InvoiceComplianceEvaluator.cs:264-299` | Two ranks become a lexicographic rank (section 3.3). |
| `BillingRuleCandidate.Scope` | `BillingFactLoader.cs:76`; `InvoiceSubmissionAfterSteps.cs:1256` | Replaced by `Level`, `Distance` and `BuSpecific`. |
| `RuleSetService` create, assign, publish, `Summarize` | `:387-399`, `:1910-1947`, `:1541-1555`, `:2242` | Validation (section 4.1). `Summarize` maps an unknown scope to `"Unassigned"`, so the new values need names. |
| `RuleSetService.GetFirmClauseAsync` | `:2013-2063` | Above. **Planned callers:** 130 (firm findings projection), 131 (firm notice), 133 (PDF report), 134 (SPA invoice detail), 135 (Billing Guidelines). Each POML cites the current signature (for example `tasks/130-firm-findings-projection.poml:55` and `tasks/135-spa-billing-guidelines.poml:53`). |
| **Firm Billing Guidelines view (135)** | `tasks/135-spa-billing-guidelines.poml:47,74`: "client-default rule sets plus that firm's rule sets" | **Proposal:** the general view lists all-BU ClientDefault and Firm sets, as specified today. An optional `coreRecordId` (a record the CIAM caller holds an external grant on) returns the full post-precedence list for that record, including Record, FirmRecord and BU-restricted sets. Without a record the firm sees no record-scoped set. 135's POML needs this constraint before it runs. |
| `RuleSetEndpoints` create / `TryParseScope` | `RuleSetEndpoints.cs:227-231`, `:478` | Accepts `Record` and `FirmRecord`. Error text "scope must be ClientDefault or Firm" changes. |
| Rule Review page | `src/solutions/RuleReview/src/services/ruleSetApi.ts:20` (`RuleSetScope = 'ClientDefault' \| 'Firm'`), `components/useFirmNames.ts:36-40` (`describeScope` → "Not assigned"), `components/AssignmentPanel.tsx`, `components/RuleSetListView.tsx:61-65` | Section 7. |
| Task 102 (adjustment rule list, D-74) | `tasks/102-*.poml` | Must use the loader's eligibility and precedence, not its own scope filter. |
| Ontology / Signals readers | `SignalReevaluationJob`, `PolicyScopeResolver`, `RuleDomain.cs:77` | **None**: no `sprk_policy` column changes; billing rows never carry `sprk_policy.sprk_matter`. |
| `BillingRuleDraftJobHandler`, `BillingFindingSignalMapper` | — | None (they read the rule-set id only, or nothing). |

## 7. Admin UI (Rule Review page, task 077)

- **Scope picker** (`AssignmentPanel.tsx`): a Dropdown with *Client default*, *A firm*, *A matter, project or work assignment* and *A firm on a matter, project or work assignment*. It is shown on Draft sets only; a Published set shows the scope read-only with the hint "Retire and create a new rule set to change who it applies to" (section 4.3).
- **Record picker:** a **type** choice (Matter / Project / Work assignment) and a `LookupField`, reusing the read-only `Xrm.WebApi` adapter pattern of `services/firmLookup.ts`. Search goes through a new `searchCoreRecordsAsLookup(type, q)` modelled on `searchOrganizationsAsLookup` (`Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts:736`). `PolymorphicPicker` is **not** recommended: it delegates to `Xrm.Utility.lookupObjects`, a host dialog, which works inside the model-driven host but gives no testable inline search. As a reference, the rate card has **no UI** (078 notes: "admins call the routes").
- **Business-unit multi-select:** "Applies to all business units" (checkbox, default on). When it is off, a `TagPicker` of business units searched through `Xrm.WebApi` (`businessunits`, excluding the Secure Record business unit by name, `SecureRecord:BusinessUnitName`). Publish shows the existing notice plus "Applies only to: Litigation, Corporate".
- **List columns** (`RuleSetListView.tsx`): *Name*, *Applies to* (for example "Client default", "Firm: Smith LLP", "Matter: M-1001", "Smith LLP on Matter: M-1001"), *Business units* ("All" or the names), *Status*, *Visible to firms*, *Rules*. Record names come from the summary: the service returns `coreRecord {type, id, name}`, so the page makes no N+1 lookups.
- **Publish warning** for same-level ties of a rule type (section 3.3).

## 8. Tests

Each item names its **must-fire** (MF) and **must-not-fire** (MNF) control.

| Area | Controls |
|---|---|
| Precedence | MF: on P's invoice, an F+M ApprovedRate displaces MAT, FIRM and CD versions of that type (worked example). MF: types not covered fall through (CD ExpenseRule still applies). MNF: an F2+M set never applies to F's invoice. MF: on M's own invoice, MAT beats CD. MF (distance): a project set beats the matter set of the same type on P's invoice. |
| B2 / F1-b generalised | MF: a MAT version the handler refuses leaves FIRM or CD of its type in force. MF: a downgraded MAT version runs as a Flag **and** CD of its type stays. MNF: a valid MAT version is not run beside CD of its type. |
| Inheritance | MF: a matter set applies to P's invoice (P under M through the pair), to W's invoice (W under M), and to W's invoice when W is under P under M. MNF: P's set does not apply to M's invoice. MNF: M2's set does not apply under M1. MNF: a W **pair** naming M with no typed lookup does not inherit. MF (if adopted): W filed under an invoice of M inherits. MF: an unreadable chain row propagates (the job fails and retries), never a shortened chain. |
| BU filter | MF: a set listing Litigation applies when the home BU is Litigation. MNF: it does not apply for Corporate. MF: a filtered-out MAT set falls back to FIRM or CD. MF: at the same level and distance, BU-specific beats all-BU. MNF: flag No with an empty list applies to nothing, and publish refuses it. MF (interim B): a Secure core record leaves BU-restricted sets out and raises the one flag finding. MNF: the Secure Record BU never matches a list. |
| Fail-safe scope | MNF: a Record set whose lookup was cleared applies to nothing; it never acts as Firm or ClientDefault. MNF: a Record set with two lookups set is refused at publish and ignored by the loader. |
| Service | One MF per row of the 4.1 matrix. 403 without AppendTo on the record (integration, `tests/integration/auth/Finance/`). MNF: a caller with AppendTo passes. MF: `409 ruleset.scope_locked` on a Published set. MNF: a Draft re-scope succeeds and re-owns. |
| Census / ownership | MF: `EveryRoutedEntryRoutesThroughTheResolver` covers the three Routed entries. MF: the new guard fires on a planted `sprk_matter` write on a policy or version. MNF: it does not fire on a rule-set matter write. MF: `RuleSetServiceOwnerTests` asserts parents = `[core record]` and the Secure team as owner for a Secure matter. MNF: client default resolves with no parents. Lineage: `SecureChildLineageTests` and `SecureFamilyConsistencyTests` with `sprk_ruleset`. |
| Firm clause | MF: MAT's clause shows on M's and P's invoices for any firm (flag on). MNF: it does not show on M2's invoice. MNF: F+M's clause does not show to F2. MNF: flag off shows nothing (existing control, kept). |
| UI | Scope picker states; the record picker requires a choice; the BU picker blocks an empty list; a Published set is read-only. |

## 9. Open questions (each with a recommendation)

| # | For | Question | Recommendation |
|---|---|---|---|
| Q1 | uac-r2 | Which column holds a Secure record's home business unit? | A `sprk_homebusinessunit` lookup on the three core tables, stamped at create and untouched by the secure re-own (5.2 A). Interim behaviour B until then. |
| Q2 | owner | Storage of the BU list | N:N `sprk_ruleset_businessunit` plus a "All business units" flag defaulting to Yes; an empty list never means all (S3, S4). Fallback: an intersect child table. |
| Q3 | owner | Where the BU filter sits | An eligibility filter before precedence: a filtered-out specific set falls back to a less specific one. At the same level and distance, a BU-listed set beats an all-BU set (#2129 point 3). |
| Q4 | owner | BU filter per rule or per rule set | Per rule set only (#2129 point 4): a set publishes, scopes and cites as a unit. |
| Q5 | owner | Order within the record level | The nearer record wins (W > P > M). Level comes first, so F+M beats a project-only set. |
| Q6 | owner | Do a Project's sets reach its Work assignments? | Yes (3.1), the same rule as Matter → children. Also: a W filed under an invoice or event inherits through it (#1630). |
| Q7 | owner | Replacing by **type** removes **all** lower rules of that type. For example, a matter RequiredData rule (PO number) removes the client default's RequiredData rule (task code), and the same goes for the code lists of NonBillableActivity and ExpenseRule. Is that intended? | Confirm before 082. If not, mark those types *additive* in `BillingRuleCatalogue`, a closed per-type flag reviewed as code (D-27). |
| Q8 | owner | Should a matter's **rate card** apply to its projects' and work assignments' invoices? Today it does not (`RateCardResolver.cs:133-142`, exact match). | Yes, for consistency with D-171. Same chain and same nearer-wins rule, as a follow-up to 082. |
| Q9 | owner | May an admin change the scope of a Published set? | No: `409 ruleset.scope_locked`; retire it and create a new set (4.3). This changes today's Firm re-assign. |
| Q10 | owner, uac-r2 | Core-record delete (D-120) with a record-scoped rule set | As with the rate card: deleted with the record when it is scoped 1:1 to that record and no Signal or Decision Record cites one of its versions; otherwise kept and Retired. Delete behaviour `Restrict` (S1) so nothing widens. |
| Q11 | uac-r2 | `sprk_ruleset` joins the secure family (lineage, ownership parent, role JSON, Read depth). Should billing admins see Secure matters' rule sets? | Yes to joining (5.1). The admin list is filtered by the caller's Read on the core record (5.3). |
| Q12 | owner | Should the rate-card import also require AppendTo on its core record (today Read only)? | Yes, the same `ebilling.ruleset.scope`-style key. A separate fix, filed rather than folded in. |
| Q13 | owner | Business-unit hierarchy | Exact business unit only. A parent business unit on the list does not cover its children. |
| Q14 | ontology-r1 | Census change Waived → Routed; guard replaced by a narrower one; `sprk_policy` → `sprk_ruleset` lineage edge | Agree. `CitationLookups` and the Signals readers are unaffected (2, 5.1). |
| Q15 | owner | Billing Guidelines view (135) and record-scoped sets | A general view plus an optional per-record view (section 6). |

## 10. Proposed tasks (POML files not created)

The next free numbers in the WP3 band are **080-085** (`tasks/TASK-INDEX.md` stops at 079 in phase 3). The order is schema (owner approval) → RuleSetService and census routing → loader selection → UI → deploy.

| ID | Title | Rigor | Model / effort | Depends on | Notes |
|---|---|---|---|---|---|
| 080 | Rule set scope schema: core lookups (Restrict), scope options, all-BU flag, BU N:N, data-model doc | STANDARD | sonnet / high | owner approval of S1-S4 (Q2); 019 | `Deploy-EbillingRuleSetScopeSchema.ps1`; additive; applied to dev inside the task; `docs/data-model/sprk_ruleset.md` updated. No SpaarkeMaster edit (079 syncs). |
| 081 | RuleSetService scope validation, BU writes, routed ownership; census Waived → Routed; guard replaced; lineage `sprk_ruleset` | FULL (tags: `auth`, `tenant-isolation`, `testing` → two adversarial passes) | opus / high | 080; uac-r2 answers to Q1 and Q11; ontology-r1 answer to Q14 | p-safe **false**: uac-r2 files (`SecureChildLineage.cs`, `RecordOwnershipResolver.cs`, `config/secure-record-owner-role.json`) and the shared census. New `OperationAccessPolicy` key. `/conflict-check` before the PR. |
| 082 | Selection: core-record chain, eligibility and BU filter in `BillingFactLoader`; lexicographic precedence in `InvoiceComplianceEvaluator`; Secure-BU interim finding | FULL | opus / high | 080, 081 (shared predicate `RuleSetApplicability`); owner answers to Q3, Q5, Q6 and Q7 | Seeded proof of every MF/MNF in section 8; determinism; NFR-01 budget re-measured on a 150-line invoice. |
| 083 | Firm clause applicability: `GetFirmClauseAsync` takes the invoice's core record; 130/135 POML constraints updated | STANDARD | sonnet / high | 082 | Must land before 130 starts. Edits 130, 131, 133, 134 and 135 POML constraints (main session). |
| 084 | Rule Review page: scope picker, record picker, BU multi-select, list columns, publish notices | STANDARD | sonnet / high | 081 | `src/solutions/RuleReview/**`; a shared `searchCoreRecordsAsLookup` in `Spaarke.UI.Components` (ADR-012). |
| 079 (amended) or 085 | Deploy: SpaarkeMaster sync of 080's schema (form, views), BFF through SPA-r3's build, code page | STANDARD | sonnet / high | 080-084 | Fold into **079** if it has not run (add 080-084 to its deps). Otherwise 085 with 079's procedure. |

A follow-up for Q8 (rate-card inheritance), if the owner agrees, is a separate STANDARD task after 082.

## 11. Things found while reading (not part of D-171)

- **Rate card core lookups use RemoveLink** (`Deploy-EbillingTimekeeperRateCardSchema.ps1:431-436`, which passes no `-DeleteBehavior`; the module default at `EbillingSchema.psm1:337` is `RemoveLink`). A matter deleted outside uac-r2's delete path would clear the card's lookup and turn a matter card into a **firm-wide** card. This is unverified live. The main session should check `RelationshipDefinitions(SchemaName='sprk_matter_ratecard_matter')` and file it if it is confirmed.
- **Rate card authorization checks Read, not AppendTo**, on its core record (Q12).
