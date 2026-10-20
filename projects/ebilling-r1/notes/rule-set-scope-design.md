# Rule set scope: client default, firm, core record, firm + core record, business-unit filter

> **Status**: DESIGN NOTE, revised after **uac-r2's review (2026-10-20)**, whose findings are binding, and the owner's answers to Q1 and Q10 (**ebilling D-141**, section 1.3). The owner's earlier decisions are **D-171** and **D-172** (sections 1.1, 1.2); every other statement marked **Proposal** is ebilling's and is checked at the task's review. **Q1, Q10 and Q11 are closed** (D-141; Q11 answered by uac-r2: `sprk_ruleset` joins the secure family now). **Still open:** the owner's approval of the final schema script (task 080).
> **Source**: GitHub #2129 (decision tables in its comments), ontology decisions **D-171** and **D-172**, the #2132 owner note, uac-r2's review of this note (section 12). It extends ebilling D-39. Recorded in the ebilling decisions log as **D-136** (scope model), **D-137** (scope details), **D-138** (rule-type round 3) and **D-141** (Secure business unit and core-record delete).
> **Branch read**: `work/ebilling-r1` at `b046154af`; re-checked at `3c83d48cf`; the uac-r2 revision re-checked every newly cited file and class at `3e11367468`. Older line references are to `b046154af`; references added in the uac-r2 revision are to `3e11367468`.
> **Schema items are marked "needs owner approval before running".** None of them has run.

## 1. Decision summary

### 1.1 D-171 (owner, first round, quoted from #2129)

| Topic | Owner decision |
|---|---|
| Levels | "Client default, Firm, **core record** (Matter, Project or Work assignment) and **firm plus core record**. Scope stays on the rule set; copy `sprk_ratecard`'s optional core-record scope." |
| Precedence | "**Most specific replaces, per rule type**: firm+record > record > firm > client default. This extends D-39. ... rule types not covered at a specific level fall through." This supersedes the earlier "all apply" answer. **Refined by D-172 Q7** (checklist types add). |
| Inheritance | "Yes. A Matter's rule sets also apply to invoices on its Projects and Work assignments." **Narrowed by D-172** to direct association only. |
| Client level | "**No separate client record; use the business unit.** A rule set applies to **all business units by default**, or only to a chosen list ... When a list is set and the record's business unit is not on it, the rule set does not apply." |
| Business-unit filter (point 6) | "The business-unit filter is on the **rule set only**, never on an individual rule. At the same level, a **business-unit-specific** eligible set beats an **all-business-units** set for the same rule type." Points 3 and 4 (eligibility filter; per rule set) are closed. |
| Ownership (Q3) | Route the `sprk_ruleset` / `sprk_policy` / `sprk_policyversion` creates through `IRecordOwnershipResolver` with the core record as parent. The census moves to Routed. |
| Firm visibility (Q4), re-own on a Secure change (Q5) | Unchanged from the issue body. |

### 1.2 D-172 (owner, second round, on this note at `71d28f319`)

| Topic | Owner decision |
|---|---|
| **Inheritance (correction)** | "**Direct association only.** A record inherits only from the record it is *directly* filed under. A work assignment under a project inherits the project's sets but **not** the matter's through that project. A work assignment directly under a matter inherits the matter's. There is **no** inheritance through an intermediate (invoice or event, #1630)." The chain is the record plus its direct parent(s), at distance ≤ 1 (section 3.1). |
| **#2132 / S1 (constraint)** | "Deleting a matter must **never** require first deleting its rate cards or rule sets, because the user may lack permission. **So no delete-Restrict.**" A card or set scoped to a record that no longer exists applies to **nothing**: an explicit scope on `sprk_ruleset` (S2) and on `sprk_ratecard` (S6), and both the rule loader and `RateCardResolver` require the lookup. It never widens to firm-wide or client default. Cleanup is per Q10. |
| Q7 | "**Limits replace, checklists add.**" Numeric limits: the more specific rule replaces the general one of that type. Checklist types (required data, non-billable activities, approved timekeepers): general and specific items **both** apply. A per-type flag in `BillingRuleCatalogue` (section 3.3 lists every type). |
| Q8 | A matter's rate card applies to its child records' invoices, by the same direct rule (section 3.5). |
| Q9 | A Published set's scope is **locked** (`409 ruleset.scope_locked`): retire it and create a new set. Drafts stay editable. |
| Q5 / Q6 | The nearer record wins within a record level. A project's sets reach its own (direct) work assignments. |
| Q10 | A record's own rule set or card is deleted with the record, or retired if something that survives the delete cites one of its versions. **Superseded for rule sets by D-141 (section 1.3): no Retire; an uncited set is deleted, a cited one is kept and unlinked.** |
| Q12 | The rate-card import also requires AppendTo on its record, **as a separate fix** (not in 080-085). |
| Q13 | Exact business unit only. |
| Q15 | A general Billing Guidelines view, plus a per-record view for record-scoped sets. |

### 1.3 D-141 (owner, 2026-10-20, on uac-r2's review)

| Topic | Owner decision |
|---|---|
| **Q1: which business unit is matched** | An ordinary record matches its `owningbusinessunit`. A record held in the Secure Record business unit matches a **snapshot** of the business unit it had before it was made Secure, kept in a new field-secured column on the three core tables (S8). `ProvisionProjectEndpoint`, the one writer of the move into the Secure business unit, writes the snapshot in the same step, before the owner change, and refuses (fail closed) if that write fails. A record created directly under a Secure parent takes the **parent's** snapshot. The snapshot is read only while the record is held Secure; **Unsecure never uses it**. Interim B stays for a Secure record with no snapshot (section 5.2), with three conditions. **No always-maintained home-business-unit lookup** (Proposal A rejected). The column and its writer are **ebilling's** (080 schema, 087 writer), not uac-r2's. |
| **Q10: core-record delete** | The delete job **deletes** the record's own rule set, deepest first (versions, then policies, then the set), when nothing that survives the delete cites it. Otherwise it **keeps** the set and **unlinks** it; under S2 it then applies to nothing. **No Retire disposition**: the delete job is not a lifecycle writer. Surviving citers include Signals and Decision Records on child records' invoices (UAC round 107 deletes only those filed under the deleted record). **Supersedes D-137's "retire if cited".** uac-r2 amended `projects/unified-access-control-r2/notes/core-record-delete-design.md` §1.3 and its new §11 (`86cba5a00`). |
| **Q11** (uac-r2's answer) | `sprk_ruleset` joins the secure family **now**, in one change with every part that the shared guards tie together (section 5.1, task 081). |

**Census state today.** The waiver is `WaiverKind.Pending` on #2129 (4d51138e9, uac-r2 conditions: the open decision is named, the end of the waiver is stated, and the guard tests are kept until the routing change). Section 4.4 moves it to Routed (task 081).

## 2. Schema (needs owner approval before running)

There is one script, `scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1`, written on `EbillingSchema.psm1` (task 080). It carries S1-S4 and S6-S8; S5 (role) and S9 (creator person) go through uac-r2's existing scripts in the code tasks that the shared guards tie them to. It is idempotent, supports `-WhatIf`, and refuses to relabel or renumber an existing option (as `Deploy-EbillingSubmissionIntegrityOption.ps1` does). **The owner approves the final script before it runs.** The SpaarkeMaster sync is **not** done by the schema task. Under the pipeline rule in project CLAUDE.md §3, it goes with deploy task **079** (Deploy WP3, still open), using 029's export-to-temp procedure. 079 also adds the new columns to the rule set and rate card forms and views.

| # | Item | Additive? | Notes |
|---|---|---|---|
| S1 | `sprk_ruleset.sprk_matter`, `sprk_project` and `sprk_workassignment`: lookups to the three core tables. Relationships `sprk_matter_ruleset_matter`, `sprk_project_ruleset_project` and `sprk_workassignment_ruleset_workassignment` (the rate-card naming). | Yes | Copied from `sprk_ratecard` (`scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1:431-436`), **with the module default delete behaviour `RemoveLink`** (`EbillingSchema.psm1:337`). **No Restrict (D-172):** deleting a core record must never require deleting its rule sets first. A cleared lookup makes the set apply to nothing (S2), and cleanup follows Q10 (section 4.5). |
| S2 | `sprk_ruleset.sprk_scope`: **new option values** `Record = 100000002` and `FirmRecord = 100000003` on the existing local choice (ClientDefault 100000000 and Firm 100000001 are unchanged). | Yes (options inserted, none relabelled) | **Decided (D-172): an explicit scope, never derived from which lookups are set.** A Record-scoped set whose lookup was cleared applies to **nothing** (the loader requires exactly one lookup), instead of widening to firm or client default. The explicit value also keeps the existing publish check, which refuses an unknown scope (`RuleSetService.cs:1541-1555`). |
| S3 | `sprk_ruleset.sprk_allbusinessunits`: Two-option, display "All business units", **default Yes**. | Yes | It keeps the owner's default ("applies to all business units") explicit. With No and an empty list, the set applies to nothing, and publish refuses it (`ruleset.publish_invalid`, field `businessUnits`). An empty list therefore never means "all". |
| S4 | The business-unit list: an **N:N** between `sprk_ruleset` and `businessunit`, `sprk_ruleset_businessunit` (intersect `sprk_ruleset_businessunit`). | Yes (new relationship) | Options below; the owner approves the choice with the script. Precedent in this project: `sprk_invoiceapproval_eligibleapprover_contact` (`scripts/ebilling/Deploy-EbillingApprovalSchema.ps1:168`). **Pre-check in the script:** read `EntityDefinitions(LogicalName='businessunit')?$select=CanBeInManyToMany`, and if it is false, stop and fall back to option (b). |
| S5 | Security-role rows: `config/secure-record-owner-role.json` gains `sprk_ruleset` (kind `child`, `prvReadsprk_ruleset`), as `sprk_policy` did at `:281` (#2084), with a **live refusal probe** as its `evidence` (`SecureRecordOwnerRoleSet` refuses an entry without one). Applied with `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Apply` **before** the BFF that routes the owner is deployed; otherwise every Record-scoped create on a Secure matter gets `0x80040299`, as #2084 saw for `sprk_policy`. **Read depth:** `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Basic User.xml:530` and `Spaarke Core User.xml:603` hold `prvReadsprk_ruleset` at **Deep**, the same depth as `prvReadsprk_Matter` (`:507`, `:577`); the NFR-05 depth check (`SecureBuRoleDepthAssertion`) decides live whether that reaches the Secure business unit, for every non-admin role (section 5.1). | Role change (uac-r2 owns the file; uac-r2 reviews) | **Not in the schema script. Not deferred** (Q11 answered): task 081 ships it in one change with the lineage, ownership parent, census, creator-person and NFR-05 parts (section 5.1). |
| S7 | **Field security** on `sprk_ruleset.sprk_scope`, `sprk_firm`, `sprk_matter`, `sprk_project` and `sprk_workassignment` (uac-r2 review item 8). | Changes existing columns (S1's three are new) | A model-driven edit of these columns bypasses the Q9 lock (`409 ruleset.scope_locked` is the BFF's). Copy the reader / writer profile pair of `scripts/Set-ChildRecordCreatorPersonSchema.ps1` (`Ensure-Profile`, `:324-336`): **Read for every user** (a reader profile associated with every business unit's default team, because a secured column is hidden from anyone without Read; views and the 079 "record deleted" views read these columns), **Create / Update for the BFF application user only**. Whether 080 adds the columns to uac-r2's existing pair or creates an ebilling pair is settled in the before-notice with uac-r2. 079 makes the five controls **read-only** on the rule set form. Before securing, 080 lists every reader of the two existing columns (section 6's reader table): a reader running as a user without the profile would read null. |
| S8 | **Secure business-unit snapshot** (D-141, Q1): `sprk_securedfrombusinessunit` ("Business unit before Secure"), a lookup to `businessunit` on `sprk_matter`, `sprk_project` and `sprk_workassignment`, delete behaviour RemoveLink, **field-secured** with **no reader profile** (BFF application user only, Read / Create / Update), **on no form and in no view**. | Yes (new column) | ebilling's column (the coordinator's correction to uac-r2's review: uac-r2 does not build it). Writer: task 087 (section 5.2). Reader: 082. A cleared value (business unit deleted) or an empty one is "not known" (interim B). §11 justification in section 5.2. |
| S9 | **Creator person** (`sprk_createdbyperson`) on `sprk_ruleset`, `sprk_policy` and `sprk_policyversion` (task 081) and on `sprk_ratecard` and `sprk_ratecardrate` (task 086). | Yes (new column, field-secured by the script) | **Not in the schema script:** it is added by `scripts/Set-ChildRecordCreatorPersonSchema.ps1` (its `$Tables` list, `:157`, and its per-table solution map), in the **same change** as `RecordCreatorPerson.StampedChildTables` (`src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordCreatorPerson.cs:78`), because `ChildRecordCreatorPersonSchemaAgreementTests` pins the two lists equal and the census's `EveryChildTableTheServerCreatesCarriesTheCreatorPersonColumn` pins the stamped set inside the role JSON's child set. `sprk_policy` and `sprk_policyversion` are ontology tables: ontology-r1 reviews their rows and names their solution. |
| S6 | `sprk_ratecard.sprk_scope`: **new** local choice, display "Applies to", `Firm = 100000000`, `CoreRecord = 100000001`; optional, no default. Plus a **one-time backfill** of existing cards. | Yes (new column; the backfill writes only rows whose scope is empty) | **#2132 / DEF-027 (D-172).** The rate card's three core lookups stay **RemoveLink** (live on dev). Today `RateCardResolver` derives the scope from the lookups (`RateCardResolver.cs:133-142`: no lookup = firm-wide), so a card whose record was deleted becomes the firm's general card. With S6, `RateCardResolver` requires `CoreRecord` + exactly one lookup for a scoped card and `Firm` + no lookup for a firm card; anything else (empty scope included) is not used (section 3.5). **Backfill:** a card with exactly one lookup → `CoreRecord`; with none → `Firm`; with two or more → left empty and listed (the resolver already ignores it). The backfill cannot tell a genuine firm card from a card whose lookup a delete already cleared, so its output lists every card it marks `Firm` (name, id, firm) for the owner to check. Idempotent: it never overwrites a set value. |

**Options for storing the business-unit list (S4)**

| Option | For | Against |
|---|---|---|
| **(a) N:N to `businessunit` + the S3 flag (recommended)** | Native and referentially sound: a deleted business unit drops out of the list. One link-entity query in the loader. An admin subgrid comes free on the model-driven form. | Associate and disassociate are a separate write path, not one row update, so publish must re-read the list (ADR-002: the form bypasses the BFF). It depends on `businessunit` allowing N:N (S4 pre-check). |
| (b) An intersect child table `sprk_rulesetbusinessunit` (rule set + BU lookups) | It always works. It can carry its own audit columns. | It is a new table: §11 asks why the N:N is not enough. It is also one more ownership row for the census and for the Secure wall. |
| (c) JSON or text column of BU ids | No new schema surface. | No referential integrity, not queryable, invisible on the form. Rejected. |
| (d) Multi-select choice | — | Business units are data, not a fixed list. Rejected. |

**Signals reader impact: none.** The Signals readers of `sprk_policy` (`SignalReevaluationJob.ReadEnabledPoliciesAsync`, `PolicyScopeResolver.LoadEnabledPoliciesAsync`; inventory in `notes/019-policy-store-ruleset.md`) read no `sprk_ruleset` column. The design writes **no** core record onto `sprk_policy`. `sprk_policy.sprk_matter` stays the ontology's (#2084) and is never set on a billing row (section 4.4). No `sprk_policy` or `sprk_policyversion` column is added.

**Additive order is safe.** S7 is the one non-additive item: it secures two existing columns, so 080 checks their readers first (above). S8 and S9 are new columns no code reads until their tasks land. With S1-S6 deployed and no code changed: the loader selects only scope ClientDefault and Firm (`BillingFactLoader.cs:300-315`), so a Record set is never evaluated; publish refuses an unknown scope (`RuleSetService.cs:1548`); `GetFirmClauseAsync` returns None (`:2053-2056`); and `RateCardResolver` does not read S6, so rate resolution is unchanged. Nothing widens before the code lands. **One ordering rule:** the S6 backfill must have run on an environment before task 085's resolver reaches it, or every existing card applies to nothing there. 079 re-runs the backfill (idempotent) immediately before deploying the BFF, which also catches cards the old import created in between.

## 3. Selection (BillingFactLoader and InvoiceComplianceEvaluator)

The work splits the way the code does today. The **loader** decides which rule sets are *eligible* for this invoice: the level applies and the business-unit filter passes. The **evaluator** applies *precedence* per subject, where D-39 already lives (`InvoiceComplianceEvaluator.cs:264-299`).

### 3.1 The core-record chain: direct association only (D-172)

Input: `BillingEvaluationRequest.CoreRecord` (`BillingFactLoader.cs:26-27`), the invoice's own core record as the submission stamped it.

| Invoice's core record | Chain: distance 0, then distance 1 | How the direct parent is read |
|---|---|---|
| Matter M | M | — (a matter is filed under no core record) |
| Project P | P, then the matter (or project) P is filed under, if any | A project has **no typed matter lookup**. Its filing is the BFF-managed ADR-024 pair `sprk_regardingrecordid` / `sprk_regardingrecordtype`, which is field-secured, so only the BFF writes it (`SecureRootInheritance.cs:401-404` and `:482`). A pair naming a record that does not exist confers nothing. **uac-r2 review item 8:** the pair is resolved with **UAC's resolver** (the pair rule of `SecureRootInheritance.NameParentsAsync`, private today at `:3600`; its `Unknown` answer covers an unreadable or non-core type and an id that is not a record id), never parsed again here. A **malformed pair fails closed**: it is not "no parent"; the chain read fails as an unreadable row does (below). |
| Work assignment W | W, then each core record a **typed** filing lookup names: `sprk_regardingmatter` → M, `sprk_regardingproject` → P | The typed lookups only (`SecureRootInheritance.TypedFilingColumns`, `:401`). A W pair that no typed lookup repeats is **not** a filing (#1616) and is ignored. |

- **No second hop.** The parent's own parent is never read. A W filed under P inherits P's sets, and **not** the sets of P's matter. A W filed directly under M inherits M's.
- **No inheritance through an intermediate.** A W filed under an invoice or event (#1630, `IntermediateFilingColumns`, `:429`) inherits nothing through it: its chain is W alone. This is narrower than uac-r2's secure wall, which follows #1630 for **access**; billing scope does not, by the owner's decision.
- **Downward only.** A project's set never applies to its parent matter's own invoices.
- **A W carrying both typed lookups** is directly filed under both by uac-r2's definition, so both are at distance 1 and both sets are eligible. If a field-mapping profile copies P's matter onto `sprk_regardingmatter` when W is created under P, the data cannot tell "under P only" from "under P and M", and the owner's example (W under P does not inherit M) would not hold. Task 082 counts such rows on dev first and escalates if any exist.
- **Reuse, not a new map.** `CoreRecordChain` (internal, `Services/Signals/Billing/`, task 082) reads **one row**, the invoice's core record, app-only: its typed filing lookups (W) or its pair (P), and in the same read `owningbusinessunit` and the S8 snapshot (section 5.2). It reuses uac-r2's static `TypedFilingColumns` and resolves the project pair through UAC's pair resolver (a pair-only entry point, or `NameParentsAsync` made internal: a visibility change uac-r2 reviews, never a copy); it does not call `FindFilingParentsAsync` (`SecureRootInheritance.Cascade.cs:401`), which needs the scoped provisioning graph (§11: existing = the `SecureRootInheritance` maps; extension = the read is ours, the maps are theirs; cost of doing nothing = a project invoice never sees its matter's terms).
- **Fail closed:** a chain row that cannot be read, or a project pair UAC's resolver answers `Unknown` for, is a fault and propagates (the loader's contract, `BillingFactLoader.cs:243-244`). The job retries. The invoice is **never** judged on a shortened chain, because that would silently drop the parent's negotiated terms.

### 3.2 Eligibility (loader)

```
chain    = CoreRecordChain(request.CoreRecord)        // [(table, id, distance 0|1)], empty when no core record
bu       = HomeBusinessUnit(request.CoreRecord)       // section 5.2; null = unknown
sets     = Published billing sprk_ruleset rows WHERE
             scope = ClientDefault
          OR (scope = Firm       AND sprk_firm = firm)
          OR (scope = Record     AND exactly one core lookup set AND that lookup ∈ chain)
          OR (scope = FirmRecord AND sprk_firm = firm AND exactly one core lookup set AND that lookup ∈ chain)
         (the query filters, then every row is re-checked, as BillingFactLoader.cs:304-315 does today)
         // a Record / FirmRecord set whose lookup was cleared has no lookup: never eligible (D-172)
         // a Firm / FirmRecord set whose sprk_firm was cleared (RemoveLink) never matches a firm: never eligible
for each set:
    if not set.allBusinessUnits:
        list = set's BU ids (one link-entity query for all sets, chunked at MaxInValues)
        if bu is null or bu ∉ list: drop the set          // BU filter = eligibility (D-171 point 3, closed)
    candidate.Level      = ClientDefault 0 | Firm 1 | Record 2 | FirmRecord 3
    candidate.Distance   = chain distance of the set's record (0 or 1; 0 for ClientDefault/Firm)
    candidate.BuSpecific = not set.allBusinessUnits
```

`BillingRuleCandidate.Scope` (`BillingFactLoader.cs:76`) is replaced by `Level`, `Distance` and `BuSpecific`. `InvoiceSubmissionAfterSteps.cs:1256` builds the system policy's candidate and sets `Level = ClientDefault` in its place (D-125: never read for that policy). Policies and versions are loaded as today (`:324-379`). The predicate itself is one pure function, `RuleSetApplicability.AppliesTo(set, firmId, chain, homeBu)`, which the firm-clause check reuses (section 6).

### 3.3 Precedence (evaluator): limits replace, checklists add

**What "rule type" means today.** It is `BillingRuleDefinition.RuleType`: the `RuleType` enum that the gate parses from `sprk_policyversion.sprk_ruletype`. The evaluator applies it **per subject** (the invoice, and each line by its index), among the versions selected for that subject's date. Only a firm version that the gate accepts, the handler accepts and that is **not downgraded** counts as "present" and displaces the client default of its type (`InvoiceComplianceEvaluator.cs:272-283`; 072 review B2, D-116). That rule is generalised to a lexicographic rank, and it now applies only to **Replace** types:

```
rank(v) = (v.Level, -v.Distance, v.BuSpecific ? 1 : 0)      // Distance is 0 or 1 (D-172)
   // Level first:  FirmRecord > Record > Firm > ClientDefault           (D-171)
   // then, at a record level, the nearer record: own record (0) > direct parent (1)   (Q5)
   // then, at the same level and distance, a BU-listed set beats an all-BU set        (D-171 point 6)

for each subject s:
    V = versions selected for s on its date        // unchanged: line date, else invoice date, else ReceivedOn (:175-182, A3)
    for each rule type t:
        if BillingRuleCatalogue.IsAdditive(t):
            keep every v of type t that is gate-accepted AND handler-accepts        // no displacement (D-172 Q7)
        else:
            D(t) = max rank over v in V of type t where gate-accepted AND handler-accepts AND not downgraded
                   (none → no displacement for t)
            keep v of type t   iff  gate-accepted AND handler-accepts AND (D(t) is none OR rank(v) >= D(t))
```

**The ordering, rechecked for distance ≤ 1.** Level dominates, so the order is total over twelve ranks: FirmRecord own > FirmRecord parent > Record own > Record parent > Firm > ClientDefault, each split into BU-listed > all-BU. Consequences: a firm+matter set inherited by P's invoice beats a record-only set on P itself (level first, as D-171 orders it); among two Record sets, P's own beats M's (nearer). Two Record sets at the **same** distance can only be two sets on the same record, or a W's two direct parents (P and M, both distance 1); both are ties (below).

**The per-type flag (`BillingRuleCatalogue.IsAdditive`, closed, reviewed as code, D-27).** One exhaustive switch over every billing `RuleType`; a non-billing type throws. A test pins the full list.

| Type | Mode | Reason |
|---|---|---|
| ApprovedTimekeeper | **Additive** | Owner (checklist). Its body has no parameters and the roster is firm-level (D-111), so a second in-force version raises the same Flag finding again under its own policy. Proposal: the publish notice says "this repeats the general timekeeper check". |
| ApprovedRate | Replace | Owner (rate tolerance). |
| TimeIncrement | Replace | Owner (time increment). |
| DailyHoursCap | Replace | Owner (daily hours cap). |
| NonBillableActivity | **Additive** | Owner (checklist of codes). |
| ExpenseRule | **Split** | **Owner (D-172 round 3).** Per-category caps replace: for each category, the highest-ranked version that caps it applies, and an uncapped category falls through. The non-reimbursable checklist adds: the union of every in-force version's `nonReimbursableCodes`. Precedence and the handler treat the two parts separately. The rule type is not split (no new `sprk_ruletype` option), and no codes are dropped. |
| InvoiceAge | Replace | Owner (invoice age). |
| CoreRecordWindow | Replace | **ebilling's call.** `graceDays` is a numeric limit. |
| Duplicate | Replace | **ebilling's call.** `matchFields` defines what counts as a duplicate for that record: one configuration, not a list of independent items. Additive would let the loosest definition always fire, so a record could never narrow it. |
| RequiredData | **Additive** | Owner (checklist of fields). |
| AiObservation | **Additive** | **ebilling's call.** Each version names one observation kind (`blockBilling` or `vagueDescription`) and its threshold. Replace per type would let a matter's blockBilling rule silently remove the general vagueDescription check. Flag only (I-5), so adding never rejects anything. Cost: a record cannot relax the general threshold for the same kind. Rejected for now: replace keyed by (type, observation), a second key the per-type flag does not express; the owner can ask for it later. |
| SubmissionIntegrity | Additive (inert) | System type (D-125): one seeded client-default policy, never authorable, so never displaced and never displacing. Listed so the switch is exhaustive. |

What follows from it:

- **A broken specific rule never removes a check (Replace types).** A matter version that the handler refuses does not set `D`, so the firm or client-default rule stays in force. This is B2, unchanged.
- **A downgraded specific version runs as a Flag beside the next level (Replace types).** It does not set `D`, and its rank is ≥ `D`, so it is kept. This is F1-b, unchanged. For an Additive type nothing is displaced, so a refused version simply does not run and a downgraded one runs as a Flag.
- **The D-171 point 6 BU tie-break applies only to Replace types.** For an Additive type every eligible set applies; the BU filter still decides eligibility. (D-172 Q7 is the later, type-specific decision; 082's review confirms this reading with ontology-r1.)
- **Ties at the same rank** (Replace types; for example two matter-level sets with the same type, or a W's two direct parents): **all apply**, as the code does today for two firm sets. Publish shows a non-blocking warning when another published set at the same level for the same record already has that type. It is not a refusal, because a refusal would also block a deliberate pair.
- **In force on which date (D-74).** Per-subject version selection is unchanged. Rule-set rows carry `sprk_effectivefrom` / `sprk_effectiveto`, but the loader does not read them: a version's `sprk_inforcefrom` / `sprk_inforceto` decides. D-74's adjustment rule list (task 102) reuses this loader and precedence with the invoice date.
- **Determinism:** the rank is total over (Level, Distance, BuSpecific). Ties keep the existing ordinal order (policy code, then id).

### 3.4 Worked example

Firm F. Matter M is in business unit "Litigation". Project P is filed directly under M (its pair). W1 is filed under P (typed `sprk_regardingproject` only). W2 is filed directly under M (typed `sprk_regardingmatter` only).

| Rule set | Level / record / BU | Rules (type: parameter, consequence) |
|---|---|---|
| CD | ClientDefault, all BUs | ApprovedRate tolerance 5% Flag; TimeIncrement 0.1; InvoiceAge 90 days Reject; ExpenseRule (cap $500, E110 not reimbursable); RequiredData (task code); NonBillableActivity {L100} |
| CD-Lit | ClientDefault, BU list = {Litigation} | NonBillableActivity {L120} |
| FIRM | Firm F | InvoiceAge 120 days Reject; DailyHoursCap 10 |
| MAT | Record = M | ApprovedRate tolerance 0% Reject; RequiredData (PO number) |
| F+M | FirmRecord F + M | TimeIncrement 0.25 |
| PRJ | Record = P | ApprovedRate tolerance 2% Flag |
| PRJ-other | Record = P2 (another project) | DailyHoursCap 8 |

**Invoice from F on P** (chain P 0, M 1):

| Rule type | Mode | Eligible (rank) | Applies | Why |
|---|---|---|---|---|
| ApprovedRate | Replace | CD (0,0,0), MAT (2,-1,0), PRJ (2,0,0) | **PRJ** | Same level; P's own set is nearer than M's |
| TimeIncrement | Replace | CD, F+M (3,-1,0) | **F+M** | Firm + record is the highest level |
| InvoiceAge | Replace | CD, FIRM (1,0,0) | **FIRM** | D-39, as today |
| DailyHoursCap | Replace | FIRM | **FIRM** | PRJ-other is not on the chain |
| NonBillableActivity | Additive | CD, CD-Lit | **CD and CD-Lit** | Both lists apply (L100 and L120). CD-Lit is eligible because P's business unit is Litigation (5.2) |
| RequiredData | Additive | CD, MAT | **CD and MAT** | Task code and PO number are both required |
| ExpenseRule | Split | CD | **CD** | Not covered more specifically: caps and codes both come from CD |

**Invoice from F on W1** (chain W1 0, P 1; M is **not** on the chain): ApprovedRate → **PRJ** (MAT is not eligible); TimeIncrement → **CD** (F+M is on M); RequiredData → **CD only**; InvoiceAge, DailyHoursCap → FIRM; NonBillableActivity → CD and CD-Lit, **only if** W1's own business unit is Litigation (5.2 matches the invoice's own record).

**Invoice from F on W2** (chain W2 0, M 1): ApprovedRate → **MAT**; TimeIncrement → **F+M**; RequiredData → CD and MAT; PRJ does not apply (P is not W2's parent).

The owner's "$650/hr against $500/hr" is **rate data, not a rule** (D-23: rules never copy rates). It is a matter-scoped **rate card** that ApprovedRate reads through `RateCardResolver`. The rule set decides the tolerance and the consequence. Under D-172 Q8, M's card applies to P's and W2's invoices and **not** to W1's (section 3.5).

### 3.5 Rate cards: explicit scope and the same direct chain (Q8, #2132, task 085)

Today the rate rows are read by `InvoiceResolutionService.BuildRateQuery` (`:307-347`: firm-wide cards plus cards scoped to the invoice's core record exactly) and judged by `RateCardResolver.Applies` (`:119-145`: no lookup = firm-wide). The change:

- **Applies** when the card is the invoice's firm's, Published and in force (unchanged), **and** either `sprk_scope = Firm` with no core lookup, or `sprk_scope = CoreRecord` with exactly one core lookup whose record is on the chain. Anything else is not used: an empty scope, `CoreRecord` with no lookup (the record was deleted), `Firm` with a lookup, or two lookups. **A card never widens to firm-wide.**
- **Precedence** extends the existing tiers ("card scope decides first, the basis second") with distance (`Tier`, `:113-114`): (own-record card, timekeeper) > (own-record card, classification) > (parent-record card, timekeeper) > (parent-record card, classification) > (firm card, timekeeper) > (firm card, classification). The first tier with any rate decides. Different amounts in the deciding tier stay **Ambiguous** (no rate used), which covers a W whose two direct parents both carry a card.
- **Query:** firm-wide cards (`sprk_scope = Firm`) plus cards scoped to any record on the chain (at most three ids); the resolver re-checks every row.
- **Writer:** `RateCardImportService` sets `sprk_scope` on create: `CoreRecord` when a core record is given, else `Firm` (the 078 writer rule "at most one core lookup" is unchanged).
- **The chain** is 082's `CoreRecordChain`, read once per invoice by the resolution step.
- **Not in this task:** Q12 (AppendTo on the import's core record) is a separate fix.
- **Ownership (uac-r2 review item 5, task 086):** a card's owner is routed from its core record, but the card and its rates sit outside the lineage, the role set and the census. Section 5.4 states the gap and the fix. Q8 makes a matter's card reach its children's invoices; that is a **read** rule (app-only resolution) and does not widen who can read the card. The rule-set design does **not** copy the card's gap: `sprk_ruleset` joins the secure family in 081 before any record-scoped set can be created.

## 4. RuleSetService

### 4.1 Scope and lookup validation

This applies to create (`:387-399`), assign (`:1924-1935`) and **publish** (`:1541-1555`, which re-checks because model-driven edits bypass the BFF). All three return `400 ruleset.invalid` (on publish, `ruleset.publish_invalid`) with field errors (`RuleSetError`):

| Scope | `sprk_firm` | Core lookups | Field error when broken |
|---|---|---|---|
| ClientDefault | none | none | `firmId`: "A client default rule set takes no firmId." / `coreRecord`: "A client default rule set takes no core record." |
| Firm | required, exists | none | `firmId` (existing text) / `coreRecord`: "A firm rule set takes no core record." |
| Record | none | **exactly one** of matter, project or work assignment, which must exist | `coreRecord`: "Name exactly one of matterId, projectId and workAssignmentId." / "The core record was not found." |
| FirmRecord | required, exists | exactly one, which must exist | both, as above |
| any | — | — | `businessUnits`: "Choose at least one business unit, or apply the rule set to all business units." / "Business unit <id> was not found." / "The Secure Record business unit cannot be listed." (D-141 condition 3: the Secure business unit is found through UAC's named resolution, `SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync`; an **ambiguous** or unreadable result refuses the write, `503`, never "not the Secure BU") |

On publish, a Record or FirmRecord set whose lookup was cleared fails the "exactly one" row: it cannot be re-published, and the loader ignores it (3.2).

**Inputs** follow the 078 doctrine (`RateCardImportEndpoints.cs:20-22` and `:166-187`). `matterId`, `projectId` and `workAssignmentId` are **query** parameters on create and on `PUT /{id}/assignment`, so the authorization filter and the handler read the same values (task 130 defect C8). The business-unit ids go in the assignment body (`RuleSetAssignmentBody` gains `AllBusinessUnits` and `BusinessUnitIds`). They need no record check, because a business unit is not secured data, but the service checks that each one exists.

**Authorization on the core record.** The rate card checks only **Read** (`finance.read`, `RateCardImportEndpoints.cs:120-133`), not AppendTo. **Proposal:** require **Read and AppendTo** as the caller, because the set's lookup is filed *at* the record, which is the `finance.submit_invoice` reasoning (`src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:195-201`). It uses a new key, `ebilling.ruleset.scope` = `Read | AppendTo`. Reusing `finance.submit_invoice` would describe the act wrongly in every deny, the precedent noted at `:251-253`. Refusal: 403 `sdap.access.deny.*` from the filter, before the service runs. The rate-card import moves to AppendTo separately (Q12). This covers routes that **name** a core record in the request (create, assignment). Routes keyed by the rule-set id read the record from the set (section 4.6).

### 4.2 Business-unit list writes

The service writes the S3 flag and, in the same call, does associate / disassociate against the N:N, as the app identity (like every other rule-set write). Associates are idempotent. A failure part-way through is reported as `500 ruleset.write_failed`, and the next call converges it. **Publish re-reads the list** and refuses No-plus-empty. The list is returned on `RuleSetSummary` (new members `AllBusinessUnits` and `BusinessUnitIds`, both additive).

### 4.3 Changing a published set's scope (decided, Q9)

Today `AssignAsync` changes the scope or firm of a **Published** set; it refuses only a Retired one (`:1919-1922`). Under D-172 Q9 it **refuses**: changing the scope, firm, core record or BU list of a Published set returns `409 ruleset.scope_locked` ("Retire this rule set and create a new one for the new scope."). This also changes today's Firm re-assign of a Published set. On a Draft set the change is allowed, and it re-owns the set and its rows. A later "copy to a new rule set" action can make the retire-and-recreate path cheap.

**The Draft re-scope sequence (uac-r2 review item 2, binding).** It copies the invoice confirm's re-file (`src/server/api/Sprk.Bff.Api/Services/Finance/Submission/InvoiceDocumentRefile.cs`, `:173` and `:202`):

1. Read whether the set is isolated **before** the write (`wasIsolated`, through `RefileIsolationRead.BeforeWriteAsync`, `Services/Access/RefileIsolationRead.cs:26`, as `InvoiceDocumentRefile.cs:155` does).
2. `IRecordOwnershipResolver.ReparentAsync` (`RecordOwnershipResolver.cs:146`) on the **set only**, with `RecordReparent.SecureExitCaller` set (`:499`, a `SecureRemovalCaller` for the HTTP caller): the F3 check on a move out of a Secure record. A refusal maps to **403 `sdap.unsecure.not_permitted`** (`SecureDesignationRemoval.NotPermittedReasonCode`) and nothing else is written.
3. `SecureChildReconciler.AfterRefileAsync("sprk_ruleset", id, wasIsolated, CancellationToken.None)` (`SecureChildReconciler.cs:849`), passing step 1's answer as `InvoiceDocumentRefile.cs:202` does. That moves the policies and versions through the lineage (section 5.1); a later `ReparentAsync` on those rows would answer Unchanged, so the service does not re-own them one by one.
4. `RuleSetService` joins the writers that `tests/Spaarke.ArchTests/RefileWriterAfterRefileGuardTests.cs` holds to the after-re-file step (its IL scan sees the `ReparentAsync` call; the test's non-vacuity list names `RuleSetService`, so dropping step 3 fails the build).

A Published set never reaches this sequence: its scope is locked (Q9, 409).

### 4.4 Routed ownership

**Each row's owner parents come from that row's own lookups (uac-r2 review item 4, binding):** `RecordOwnershipContext.ParentsOf(row)` (`RecordOwnershipResolver.cs:336`), never the set's core record handed down.

| Row | Its parent (its own lookup) | Owner resolution |
|---|---|---|
| `sprk_ruleset` | the core record (`sprk_matter` / `sprk_project` / `sprk_workassignment`, S1); none for ClientDefault and Firm (`sprk_firm` names `sprk_organization`, not an ownership parent) | `ResolveOwnerAsync(RecordOwnershipContext.ForParents(ParentsOf(setRow), caller) with { RequestedBy = caller })`. A Secure core record resolves the Secure team (secure-if-any); an ordinary one, the record's team; no parent, the caller's business-unit team as today (`OwnerContext`, `RuleSetService.cs:2389-2393`). |
| `sprk_policy` | the set (`sprk_policy.sprk_ruleset`; `sprk_ruleset` is in `OwnershipParentEntities`, section 5.1) | the same call with `ParentsOf(policyRow)`, so the policy follows the set's owner on every level |
| `sprk_policyversion` | the policy (`sprk_policyversion.sprk_policy`) | the same call with `ParentsOf(versionRow)` |

A refusal writes nothing (`OwnerRefused`, 409). The call sites that change are create (`:405`), `AddDraftsAsync` (`:871`, which creates the policies and versions in `CreateDraftRuleAsync`, `:1060-1100`), and `EditAsync`'s new version (`:1277`, the `VersionIdFor` create that the scanner cannot see). `OwnerContext` keeps naming the person (`RequestedBy`), which is what the census's person rule reads. A Draft scope change re-owns through the section 4.3 sequence. That makes it a REPARENT, so it is listed in the census's `UnscannedWriters` (`RecordOwnerAssignmentCensusTests.cs:306`).

**Census and guard, replaced in the same change (ontology-r1 condition 1, section 12):**

- Add `sprk_ruleset` to `ChildTables` (`RecordOwnerAssignmentCensusTests.cs:47-61`). The `sprk_policy` and `sprk_policyversion` entries (`:273-274` at `3e11367468`) go from `Waived` to **`Routed`**. A new entry, `("RuleSetService.cs", "sprk_ruleset", 1, Routed)`, is added, and `RuleSetServicePolicyWaiver` (`:127-139`) is deleted.
- **Creator person (uac-r2 review item 1).** Routed creates make the census's `EveryChildTableTheServerCreatesCarriesTheCreatorPersonColumn` require `sprk_createdbyperson` on all three tables (S9), and `EveryAppCreateWriterThatActsForAPersonNamesThem` require a `PersonBearingWriters` entry (`:2367`): `("RuleSetService.cs", "OwnerContext", "the billing admin creating or editing the rule set (oid)")`. `RecordCreatorPerson.StampedChildTables` and `scripts/Set-ChildRecordCreatorPersonSchema.ps1` gain the three tables in the same change.
- `RuleSetServiceNoMatterScopeGuardTests` is **replaced**, not deleted. Its blanket ban on any matter identifier in `RuleSetService.cs` would fail the new code. The new guard is narrower: **no `sprk_matter` is ever written on a `PolicyEntity` or `VersionEntity` row** (that column is the ontology's matter scope). It has a must-fire control (a planted `policy["sprk_matter"] = …` fails) and a must-not-fire control (`ruleSet["sprk_matter"] = …` passes). The behavioural twin `RuleSetServiceOwnerTests.NoWrittenPolicyOrVersionRowCarriesAMatter` stays as it is.
- **Twin guard:** `OntologyAdminAuthoringService` never writes `sprk_ruleset`, so a policy can never carry both `sprk_matter` and `sprk_ruleset`. Ontology-owned code: ebilling writes it, ontology-r1 reviews it.
- `RuleSetServiceOwnerTests` adds (expectation corrected by uac-r2 review item 4): a record-scoped **set** create passes exactly `[core record]`; its **policy** create passes exactly `[the set]` and its **version** create exactly `[the policy]`, each read from the row being written, never `[core record]`; a Secure matter yields the Secure team as owner of the set, policy and version (secure-if-any through the parent chain); a client-default **set** passes none, and its policy still passes `[the set]`.
- The `AssignCallers` reason for `RuleSetEndpoints.AssignRuleSet` (`:770-772`) is updated: a Draft re-scope now re-owns.
- `RefileWriterAfterRefileGuardTests` holds `RuleSetService` to the after-re-file step (section 4.3).

### 4.5 Core-record delete and cleanup (D-172 S1; D-141 Q10)

- **No delete-Restrict.** Deleting a core record never needs its rule sets or rate cards deleted first. RemoveLink clears their lookup; the explicit scope (S2, S6) then makes them apply to nothing.
- **Cleanup through the BFF delete flow (uac-r2's core-record delete).** D-120 already deletes a rate card scoped 1:1 to the deleted record (nothing cites a card row, so cards need no citation check). For rule sets, **D-141** decides (superseding D-137's "retire if cited"):

| Case | The delete job |
|---|---|
| The record's **own** Record or FirmRecord set, and **nothing that survives the delete cites** the set, its policies or its versions | **Deletes** it, deepest first: `sprk_policyversion`, then `sprk_policy`, then `sprk_ruleset` |
| The record's own set, and **a surviving row cites it** | **Keeps** it and **unlinks** it (the core lookup cleared); under S2 it then applies to nothing |

- **No Retire disposition.** The delete job is not a lifecycle writer; `RuleSetService` stays the one writer of a rule set's status.
- **Surviving citers** include Signals and Decision Records on **child records'** invoices that cite one of the set's versions (under the direct-inheritance rule a matter's set judges its projects' invoices). UAC round 107 deletes only the Signals and Decision Records filed under the deleted record itself.
- **Fail closed:** "cites" is decided from the live rows that survive the planned delete; a citation read that fails fails the preview (503), never "uncited".
- **uac-r2's side is done:** `projects/unified-access-control-r2/notes/core-record-delete-design.md` §1.3 (the Delete and Keep rows, and the one hard-deny exception for R's own uncited set) and its new §11 (`86cba5a00`). Its planner reads the citing lookups from ebilling's schema; **task 081 sends uac-r2 the exact citing lookups** (Signal / Decision Record / invoice → `sprk_policyversion` / `sprk_policy` / `sprk_ruleset`) once they are fixed. The delete step is uac-r2's code.
- **Deletes outside the BFF** (an admin or the named delete role, after UAC delete D6): the rows stay, orphaned and inert. 079 adds two admin views, "Rule sets whose record was deleted" (scope Record or FirmRecord, all three lookups empty) and "Rate cards whose record was deleted" (scope CoreRecord, all three lookups empty), so an admin can retire or delete them.

### 4.6 Routes keyed by the rule-set id (uac-r2 review item 3, REQUIREMENT)

Every route that takes a rule-set id: get (`GET /{ruleSetId}`), add rules (`POST /{ruleSetId}/draft`, and the proposal accept / reject), edit (`PATCH /{ruleSetId}/proposals/{policyId}`), publish, assign (`PUT /{ruleSetId}/assignment`), visibility, retire (when a route exists; none today), and list (`GET /`, per row). Today each carries only the billing-admin privilege filter (`RuleSetEndpoints.cs`, `BillingAdminAuthorization`).

1. **Read the set's core lookup on the server** (app-only, the three S1 lookups only). Never from the request.
2. **Check as the caller, before anything is read back or written:** Read on that core record, plus **AppendTo** for every write route. A ClientDefault or Firm set (no lookup) needs no record check.
3. **No-access and not-found answer the same 404** (`FinanceAuthorizationFilter.UniformRecordNotFound`, as `FinanceDenial.UniformNotFound` renders it), for an unknown set id, a set the caller may not see, and a set whose record the caller may not read. The status is not an existence oracle.
4. **List** leaves out every record-scoped set whose core record the caller cannot Read (one batched check per page).
5. **Declare each route's basis** in `RouteAuthorizationGuardTests` (the `GovernedFiles` entry for `Api/Finance/RuleSetEndpoints.cs`, `RouteAuthorizationGuardTests.Ledger.cs:346`): the billing-admin privilege at the route, plus the record check read from the set.

This answers section 5.3's admin-visibility question: a billing admin sees a Secure record's set only when they can read that record.

## 5. Ownership and Secure records

### 5.1 The secure family: one change (uac-r2 files and review; Q11 answered)

**uac-r2 review item 1 (binding): S5 cannot wait.** Adding `sprk_ruleset` to `OwnershipParentEntities`, or making its creates Routed, fails `SecureFamilyConsistencyTests` (lineage, role set and census must name the same tables) and the census creator-person test. So every part below ships in **one change, task 081**; 080 carries only the schema items section 2 gives it:

| Part | Where |
|---|---|
| Lineage entries (table below), entity set name **`sprk_rulesets`** | `Services/Access/SecureChildLineage.cs` (`T(logicalName, entitySet, …)`, beside `sprk_policy` at `:175-178`) |
| `sprk_ruleset` in `OwnershipParentEntities` | `Services/Dataverse/RecordOwnershipResolver.cs:1024` |
| Role row `sprk_ruleset` (kind `child`, `prvReadsprk_ruleset`) with a **live refusal probe** as evidence; `Set-SecureRecordOwnerRolePrivileges.ps1 -Apply` on dev **before** the BFF deploy (079 re-checks with `-Verify`) | `config/secure-record-owner-role.json` (S5) |
| `sprk_createdbyperson` on `sprk_ruleset`, `sprk_policy`, `sprk_policyversion` | `scripts/Set-ChildRecordCreatorPersonSchema.ps1` + `RecordCreatorPerson.StampedChildTables` (S9) |
| Census: `ChildTables`, Routed entries, `PersonBearingWriters` entry for `RuleSetService` | `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs` (section 4.4) |
| **NFR-05 depth check** on `sprk_ruleset`, `sprk_policy` and `sprk_policyversion` for **every non-admin role**, including the role through which billing admins hold `prvWritesprk_ruleset` (`BillingAdminAuthorization.cs:29`; that role is not in SpaarkeMaster source, so 081 reads it live) | `Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs` (`GuardedPrivileges`, clause 1, `:58`; uac-r2's file) with `tests/integration/auth/UnifiedAccessControl/SecureBuRoleDepthAssertionTests.cs`; the live run on dev records the result |

| Table | Change | File |
|---|---|---|
| `sprk_ruleset` | **New child entry** `T("sprk_ruleset", "sprk_rulesets", …)`: `("sprk_matter", Matter)`, `("sprk_project", Project)`, `("sprk_workassignment", WorkAssignment)` | `Services/Access/SecureChildLineage.cs` (beside `sprk_policy`, `:175-178`) |
| `sprk_policy` | Add `("sprk_ruleset", "sprk_ruleset")` next to the existing `("sprk_matter", Matter)` | same |
| `sprk_policyversion` | Unchanged: it reaches the matter through `sprk_policy` → `sprk_ruleset` | — |
| `OwnershipParentEntities` | Add `sprk_ruleset` (a rule set is the parent of its policies, as `sprk_policy` is of its versions, `:1040-1043`) | `Services/Dataverse/RecordOwnershipResolver.cs:1024` |
| `CitationLookups` | Unchanged. `sprk_signal.sprk_policy` still cites rather than files (`:1061-1062`), so a Signal is never pulled into a matter set's wall through the rule it cites. | — |

Without the `sprk_policy` → `sprk_ruleset` edge, a billing policy has no lineage to its matter, because `sprk_policy.sprk_matter` is never set on billing rows. Securing the matter later (Q5 of D-171) would then leave the rule set's policies and versions in the ordinary business unit. With the edge, `FilingParentOf` (`RecordOwnershipResolver.cs:610`) and `SecureChildReconciler` re-own the set, its policies and its versions on secure and unsecure. `SecureChildLineageTests` and `SecureFamilyConsistencyTests` change with it, and the three-hop path (version → policy → ruleset → core) is tested end to end (ontology-r1 condition 2).

**Read depth.** `Spaarke Basic User` and `Spaarke Core User` hold `prvReadsprk_ruleset` and `prvReadsprk_Policy` at **Deep** (SpaarkeMaster role XML), as they hold `prvReadsprk_Matter`; `Spaarke Console User` and `Spaarke Ontology Administrator` hold Deep Read on the policy tables, and `Spaarke Ontology Service` Global (an application principal, which clause 1 exempts by principal, not by role). Whether a depth reaches the Secure business unit is a property of the live business-unit tree, so the NFR-05 run, not this note, decides; a finding stops 081 (escalation).

### 5.2 Which business unit is matched (decided: D-141, Q1)

The record matched is the invoice's own core record (not the rule set's record, not a parent's). Exact match only (Q13).

| The invoice's core record | Business unit matched |
|---|---|
| Ordinary (not owned in the Secure Record business unit) | its `owningbusinessunit` |
| Held in the Secure Record business unit, S8 snapshot set | the snapshot `sprk_securedfrombusinessunit` |
| Held Secure, snapshot empty (secured before the column existed, or its business unit was deleted) | **not known**: interim B |

- **Why a snapshot.** A Secure record is re-owned to the Secure Record Owners team, so its `owningbusinessunit` becomes the Secure Record business unit (`RecordOwnershipResolver.cs:86-87`, `:2181`). Nothing kept the business unit it had before, and Unsecure reassigns to a configured or calling user (`UnsecureProjectRequest.cs`, `ReassignToSystemUserId`). **Unsecure never reads or restores the snapshot**: a stale value on an ordinary record is never read, because the reader checks "held Secure" first, and the next Make Secure overwrites it.
- **Who writes it (task 087; uac-r2 conditions, binding):**
  - (a) `ProvisionProjectEndpoint` (`Api/ExternalAccess/ProvisionProjectEndpoint.cs:160`) stays the **one writer of the move into Secure**. On a direct Make Secure (`TransitionMakeSecure`, `:426`) it writes the record's own current `owningbusinessunit` in the same step that moves the record, **before the owner change**, and **refuses, fail closed**, when that write fails (nothing is moved).
  - (d) A record **created directly under a Secure parent** takes the **parent's** snapshot, written by the server-side path that creates it into isolation. On `3e11367468` those paths are: the work-assignment secure create through `ChildRecordEndpoints` (the `OwnedChildWrite` core with `SecureRootFilingGate`, completed by `CompleteRootCreateAsync`), and `ProvisionProjectEndpoint.ProvisionInheritedAsync` (`:632`) for a project or work assignment filed under a Secure record after a browser create. `RecordCreationService` exists at `Services/Office/RecordCreationService.cs` (Office quick-create of Matter and Project); 087 checks whether it can create under a Secure parent. field-mapping-server-write-path-r1's W2 moves it to `Services/RecordCreation/` (their 013) and moves the Matter / Project creates onto the child-records route (their 020, 022); uac-r2 named their task 033 (system creators through `RecordCreationService`): 087 coordinates with that project before either lands.
  - (b) **Field-secured**, no form, no view, no user write path (S8). (c) Read only while the record is held Secure; Unsecure never reads it.
  - (e) Added to `MembershipOptions.GlobalFieldExclusions` (seeded from `MembershipOptionsDefaults`, `Services/Ai/Membership/MembershipOptions.cs:265` and `:385`; no appsettings binds the list today) so membership discovery ignores it, with a test in `MembershipOptionsTests` / `MembershipFieldDiscoveryServiceTests`.
  - (f) uac-r2 reviews the diff on `ProvisionProjectEndpoint` and on the child-create path before merge (087's review gate).
- **Proposal A is rejected** (an always-maintained home-business-unit lookup, stamped at create and on every ordinary re-own) on uac-r2's objections: ADR-034 membership discovery, a writable column, no model-driven writer, and children of Secure parents. The snapshot answers each: (e), (b), the one writer (a), and (d).
- **§11 for the S8 column (ebilling owns it).** *Existing:* `owningbusinessunit` is the only business-unit column on the core tables, and the secure re-own overwrites it; Grep of `src/` and `docs/data-model` finds no column that keeps the prior business unit (`UnsecureProjectRequest` reassigns to a user, not a business unit). *Extension:* `owningbusinessunit` cannot be extended (system column, rewritten by the re-own); deriving the business unit from the creator or the parent breaks for a record whose owner moved, and Proposal A's always-maintained column is rejected above. *Cost of doing nothing:* a business-unit-restricted rule set never applies to a Secure record's invoices (interim B forever), so a Secure matter silently loses its business unit's negotiated terms; each such invoice carries only an internal "not checked" finding.
- **Interim B (kept, D-141), with three conditions.** When the business unit is not known, only all-business-unit sets are eligible; a business-unit-restricted set does not apply; the evaluator raises **one invoice-level, flag-only finding** through a defined reason on `BILL-SYSTEM-SUBMISSION` (never a free-text Signal; the D-115 pattern): "Business-unit rule sets were not checked: the record's business unit is not known."
  1. The finding is **internal-only**: never projected to a firm (130, 131, 133, 134, 135), because projecting it tells the firm the record is Secure.
  2. "Held Secure" is decided against the Secure business unit found through **UAC's named resolution** (`SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync`, `Services/Access/SecureChildShareSynchronizer.cs:1233`); an **ambiguous** result fails closed (the evaluation faults and retries, never "not Secure").
  3. `RuleSetService` **refuses** the Secure business unit in `BusinessUnitIds` (section 4.1), so it never matches a list.

### 5.3 Who can see a record-scoped rule set (decided: Q11, uac-r2 review item 3)

- **Dataverse (model-driven):** a set on a Secure record is owned by the Secure team, so a non-member does not read it, **provided** the NFR-05 depth check passes for every non-admin role (section 5.1). The scope columns are field-secured and read-only on the form (S7).
- **Rule Review page (BFF):** every route keyed by the rule-set id checks the caller's Read (and AppendTo for writes) on the set's core record before anything is read back or written, with one uniform 404, and List leaves out what the caller cannot read (section 4.6).
- **Evaluation:** app-only, unaffected (the loader's contract).

### 5.4 Rate cards and rates join the secure family (uac-r2 review item 5, task 086)

**The gap.** `sprk_ratecard` and `sprk_ratecardrate` are outside the lineage (`SecureChildLineage.cs`), the role set (`config/secure-record-owner-role.json`) and the census (`RecordOwnerAssignmentCensusTests` has no `RateCardImportService` entry), yet `RateCardImportService.ImportAsync` routes their owner from the core record (`RateCardImportService.cs:200-211`, applied at `:314` and `:343`). As a result:

1. a rate card import scoped to a **Secure** matter is refused by Dataverse (the Secure Record Owner role holds no Read on `sprk_ratecard`: `0x80040299`);
2. a card on a matter **made Secure later** is never re-owned (no lineage), so it stays readable outside the wall;
3. the import applies the matter's owner to new **firm-level timekeeper** rows (`owner.ApplyTo(row)` at `:287`), so a Secure matter's import would hide new roster rows from the firm's ordinary readers.

**The fix (086, one change, as 081 for rule sets):** lineage entries for `sprk_ratecard` (`sprk_matter`, `sprk_project`, `sprk_workassignment`; entity set `sprk_ratecards`) and `sprk_ratecardrate` (`sprk_ratecard`; entity set `sprk_ratecardrates`); `sprk_ratecard` in `OwnershipParentEntities` (the parent of its rates); role rows for both with a live refusal probe, applied before the BFF deploy; `sprk_createdbyperson` on both (S9); census `ChildTables` and Routed entries for the card and rate creates, and a `PersonBearingWriters` entry for `RateCardImportService.ImportAsync`; the NFR-05 depth check on both tables (Basic and Core User hold Read at Deep). Each row's owner parents come from its own lookups (card → core record; rate → card). **Timekeeper rows get their own owner**: a no-parent resolution (the caller's business-unit team), as a firm-level row, never the matter's. The workbook's `sprk_document` row (created unlinked with the card's owner, `:237`) has the same shape of gap; 086 decides its owner with uac-r2 (escalation), it does not guess.

**Why a task of its own (086), not 085:** 085 is a resolver change in `Services/Finance/Resolution` that runs beside 083 (p-safe true). This fix edits uac-r2's shared files (lineage, resolver parent list, role JSON, census, depth assertion), needs a live probe and the uac-r2 review, and must follow 081, which edits the same files. Folding it in would make 085 p-safe false and serialise it behind 081 for no gain.

## 6. Firm visibility (D-66, D-79)

**Proposal:** a firm sees a record-scoped set's clauses **only on invoices whose direct chain contains that record**, and a FirmRecord set only when it is also the set's firm.

- **`GetFirmClauseAsync(policyVersionId, invoiceFirmId, ct)`** (`RuleSetService.cs:2013-2063`) gains the invoice's core record. Today it returns None for any scope other than ClientDefault or Firm (`:2052-2056`). That is fail-closed, but wrong once Record sets exist. The method calls **the same predicate the loader uses**, `RuleSetApplicability.AppliesTo(set, firmId, chain, homeBu)` (3.2). A clause is then shown to a firm exactly when the rule could have judged that invoice. The caller contract (`:2006-2012`) adds: "the invoice's core record, read server-side from the invoice". No caller exists yet, so changing the signature now costs nothing (task 083, before 130).
- **The `sprk_visibletofirms` flag** (D-79) still decides on top.

**Readers that would break or need the change** (search of `src` on `b046154af`):

| Reader | Where | Impact |
|---|---|---|
| `BillingFactLoader.LoadRulesAsync` | `BillingFactLoader.cs:280-379` | Scope query and the `RuleSetScope` mapping (section 3). |
| `InvoiceComplianceEvaluator` D-39 block | `InvoiceComplianceEvaluator.cs:264-299` | Two ranks become a lexicographic rank, Replace types only (section 3.3). |
| `BillingRuleCandidate.Scope` | `BillingFactLoader.cs:76`; `InvoiceSubmissionAfterSteps.cs:1256` | Replaced by `Level`, `Distance` and `BuSpecific`. |
| `InvoiceResolutionService.BuildRateQuery`, `RateCardResolver` | `InvoiceResolutionService.cs:307-347`; `RateCardResolver.cs:113-145` | Explicit scope and the chain (section 3.5, task 085). |
| `RateCardImportService` card create | `RateCardImportService.cs:294-313` | Sets `sprk_scope` (S6). |
| `RuleSetService` create, assign, publish, `Summarize` | `:387-399`, `:1910-1947`, `:1541-1555`, `:2242` | Validation (section 4.1). `Summarize` maps an unknown scope to `"Unassigned"`, so the new values need names. |
| `RuleSetService.GetFirmClauseAsync` | `:2013-2063` | Above. **Planned callers:** 130 (firm findings projection), 131 (firm notice), 133 (PDF report), 134 (SPA invoice detail), 135 (Billing Guidelines). Each POML cites the current signature (for example `tasks/130-firm-findings-projection.poml:55` and `tasks/135-spa-billing-guidelines.poml:53`). |
| **Firm Billing Guidelines view (135)** | `tasks/135-spa-billing-guidelines.poml:47,74`: "client-default rule sets plus that firm's rule sets" | **Decided (Q15):** the general view lists all-BU ClientDefault and Firm sets, as specified today. A per-record view (`coreRecordId`) returns the full post-precedence list for that record, including Record, FirmRecord and BU-restricted sets. Without a record the firm sees no record-scoped set. **uac-r2 review item 8:** the per-record view is gated by **UAC's evaluator**, `IAccessibleRecordSetService.ComposeForCiamContactAsync` and its Read answer (`Infrastructure/ExternalAccess/AccessibleRecordSetService.cs:86`), **not** by whether a grant row exists (a row can exist with no rights, a Secure-suppressed grant among them). A set inherited from the direct parent is labelled **"from the parent record"**, never naming the parent (the firm may hold no grant on it). The interim-B finding is never shown (section 5.2). 083 adds these constraints to 135's POML. |
| `RuleSetEndpoints` create / `TryParseScope` | `RuleSetEndpoints.cs:227-231`, `:478` | Accepts `Record` and `FirmRecord`. Error text "scope must be ClientDefault or Firm" changes. |
| Rule Review page | `src/solutions/RuleReview/src/services/ruleSetApi.ts:20` (`RuleSetScope = 'ClientDefault' \| 'Firm'`), `components/useFirmNames.ts:36-40` (`describeScope` → "Not assigned"), `components/AssignmentPanel.tsx`, `components/RuleSetListView.tsx:61-65` | Section 7. |
| Task 102 (adjustment rule list, D-74) | `tasks/102-*.poml` | Must use the loader's eligibility and precedence, not its own scope filter. |
| Ontology / Signals readers | `SignalReevaluationJob`, `PolicyScopeResolver`, `RuleDomain.cs:77` | **None**: no `sprk_policy` column changes; billing rows never carry `sprk_policy.sprk_matter`. |
| `BillingRuleDraftJobHandler`, `BillingFindingSignalMapper` | — | None (they read the rule-set id only, or nothing). |

## 7. Admin UI (Rule Review page, task 084)

- **Scope picker** (`AssignmentPanel.tsx`): a Dropdown with *Client default*, *A firm*, *A matter, project or work assignment* and *A firm on a matter, project or work assignment*. It is shown on Draft sets only; a Published set shows the scope read-only with the hint "Retire and create a new rule set to change who it applies to" (Q9).
- **Record picker:** a **type** choice (Matter / Project / Work assignment) and a `LookupField`, reusing the read-only `Xrm.WebApi` adapter pattern of `services/firmLookup.ts`. Search goes through a new page-local `searchCoreRecordsAsLookup(type, q)` (`services/coreRecordLookup.ts`, beside `firmLookup.ts`) modelled on `searchOrganizationsAsLookup` (`Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts:736`). `PolymorphicPicker` is **not** recommended: it delegates to `Xrm.Utility.lookupObjects`, a host dialog, which gives no testable inline search. The rate card has **no UI** (078 notes: "admins call the routes"); its scope is set by the import.
- **Business-unit multi-select:** "Applies to all business units" (checkbox, default on). When it is off, a `TagPicker` of business units searched through `Xrm.WebApi` (`businessunits`, excluding the Secure Record business unit by the id the BFF returns from UAC's named resolution; an ambiguous resolution returns no id and the page offers no business-unit list, fail closed; the server refuses the Secure business unit anyway, section 4.1). Publish shows the existing notice plus "Applies only to: Litigation, Corporate".
- **List columns** (`RuleSetListView.tsx`): *Name*, *Applies to* (for example "Client default", "Firm: Smith LLP", "Matter: M-1001", "Smith LLP on Matter: M-1001"), *Business units* ("All" or the names), *Status*, *Visible to firms*, *Rules*. Record names come from the summary: the service returns `coreRecord {type, id, name}`, so the page makes no N+1 lookups. A set whose record was deleted shows "Record deleted: applies to nothing".
- **Publish notices** (computed by RuleSetService and returned on the rule set detail as `publishNotices`, task 084): same-level ties of a Replace type (3.3); for a record-level set, per rule, "Replaces the general <type> rule" or "Adds to the general <type> rule" from `IsAdditive`; for ExpenseRule, "Replaces the general caps for the categories it caps; adds to the general non-reimbursable codes"; for ApprovedTimekeeper, "repeats the general timekeeper check".
- **Inheritance hint** on the record picker: "Applies to this record's invoices and to records filed directly under it."

## 8. Tests

Each item names its **must-fire** (MF) and **must-not-fire** (MNF) control.

| Area | Controls |
|---|---|
| Precedence (Replace) | MF: on P's invoice, PRJ ApprovedRate displaces MAT and CD of that type; F+M TimeIncrement displaces CD. MF: types not covered fall through (CD ExpenseRule still applies). MNF: an F2+M set never applies to F's invoice. MF: on M's own invoice, MAT beats CD. MF: a FirmRecord set on the parent beats a Record set on the invoice's own record (level first). |
| Combination (Additive) | MF: RequiredData of CD and MAT both apply on P's invoice. MF: NonBillableActivity of CD and CD-Lit both apply. MF: two AiObservation kinds at different levels both apply. MNF: an Additive type is never displaced by a more specific set. MF: an ExpenseRule at M replaces CD's ExpenseRule, codes included. MF: `IsAdditive` covers every billing `RuleType` (pinned list) and throws on a non-billing type. |
| B2 / F1-b generalised | MF: a MAT version the handler refuses leaves FIRM or CD of its type in force. MF: a downgraded MAT version runs as a Flag **and** CD of its type stays. MNF: a valid MAT version is not run beside CD of its type. |
| Inheritance (direct only) | MF: a matter set applies to P's invoice (P under M through the pair) and to W2's invoice (W2 typed under M). MF: a project set applies to W1's invoice (W1 typed under P). **MNF: M's set does not apply to W1's invoice (W1 under P under M): no second hop.** **MNF: a W filed under an invoice or event of M inherits nothing (#1630 not followed).** MNF: P's set does not apply to M's invoice. MNF: M2's set does not apply under M1. MNF: a W **pair** naming M with no typed lookup does not inherit. MF: an unreadable chain row propagates (the job fails and retries), never a shortened chain. |
| BU filter | MF: a set listing Litigation applies when the home BU is Litigation. MNF: it does not apply for Corporate. MF: a filtered-out MAT set falls back to FIRM or CD. MF: at the same level and distance, BU-specific beats all-BU (Replace type). MNF: flag No with an empty list applies to nothing, and publish refuses it. MF (interim B): a Secure core record leaves BU-restricted sets out and raises the one flag finding. MNF: the Secure Record BU never matches a list. MNF: a parent BU on the list does not match a child BU (Q13). |
| Fail-safe scope (rule sets) | MNF: a Record set whose lookup was cleared applies to nothing; it never acts as Firm or ClientDefault. MNF: a FirmRecord set whose firm was cleared applies to nothing. MNF: a Record set with two lookups set is refused at publish and ignored by the loader. |
| Rate cards (085) | MF: a Firm card with no lookup applies. MNF: a CoreRecord card whose lookup was cleared applies to nothing. MNF: a card with an empty scope is not used. MNF: a Firm card that carries a lookup is not used. MF: M's card applies to P's invoice and to W2's. MNF: M's card does not apply to W1's. MF: W1's own card beats P's card, and P's card beats the firm card. MF: two parent cards with different amounts give Ambiguous. MF: the import writes `CoreRecord` or `Firm`. |
| Service | One MF per row of the 4.1 matrix. 403 without AppendTo on the record (contract test beside `tests/integration/contract/Api/Finance/FinanceEndpointsAuthorizationContractTests.cs`). MNF: a caller with AppendTo passes. MF: `409 ruleset.scope_locked` on a Published set, Firm included. MNF: a Draft re-scope succeeds and re-owns. |
| Census / ownership | MF: `EveryRoutedEntryRoutesThroughTheResolver` covers the three Routed entries. MF: the new guard fires on a planted `sprk_matter` write on a policy or version. MNF: it does not fire on a rule-set matter write. MF: the twin guard fires on a planted `sprk_ruleset` write in `OntologyAdminAuthoringService`. MF: `RuleSetServiceOwnerTests` asserts parents = `[core record]` for the set, `[set]` for its policy and `[policy]` for its version, and the Secure team as owner of all three for a Secure matter. MNF: client default resolves with no parents. Lineage: `SecureChildLineageTests`, and `SecureFamilyConsistencyTests` plus a `SecureChildReconciler` test over version → policy → ruleset → core, secure and unsecure. |
| Secure family (081, uac-r2 review) | MF: `SecureFamilyConsistencyTests` and the census creator-person tests pass with `sprk_ruleset` in the lineage, the role JSON, `ChildTables`, `StampedChildTables` and `PersonBearingWriters`; MF (seeding): drop any one of them and a named test fails. MF: the NFR-05 assertion reports a non-admin role whose Read on `sprk_ruleset`, `sprk_policy` or `sprk_policyversion` reaches the Secure business unit; MNF: Basic depth does not. MF: a policy's owner parents are `[set]` and a version's `[policy]` (read from the row). |
| Draft re-scope (081) | MF: re-scoping a Draft set off a Secure matter without the caller's F3 right returns 403 `sdap.unsecure.not_permitted` and writes nothing. MF: an allowed re-scope calls `ReparentAsync` on the set only, then `AfterRefileAsync("sprk_ruleset", …)`, and the policies and versions follow. MF: `RefileWriterAfterRefileGuardTests` fails if `RuleSetService` drops the after-re-file step. MNF: a Published set never reaches the sequence (409). |
| Id-keyed routes (081) | MF: each id-keyed route returns the same 404 for an unknown set, and for a set whose core record the caller cannot Read; nothing is read back or written. MF: a write route without AppendTo on the record returns that 404. MNF: a ClientDefault or Firm set needs no record check. MF: List leaves out a set whose record the caller cannot read. MF: `RouteAuthorizationGuardTests` fails when a route's declared basis is removed. |
| Secure business unit (082, 087) | MF: a Secure record with a snapshot matches the snapshot. MNF: an ordinary record with a stale snapshot matches `owningbusinessunit`. MF: a Secure record with no snapshot gets interim B and the internal-only finding. MNF: the finding is never in a firm projection. MF: an ambiguous Secure business-unit resolution faults (no evaluation on a guess). MF (087): Make Secure writes the snapshot before the owner change; a failed snapshot write refuses the move. MF (087): a work assignment created into isolation under a Secure project takes the project's snapshot. MF (087): membership discovery excludes the column. |
| Rate cards secure family (086) | MF: an import scoped to a Secure matter succeeds, owned by the Secure team (role row present). MF: making a matter Secure re-owns its card and rates (reconciler). MF: timekeeper rows created by a Secure matter's import are owned by the caller's business-unit team, not the Secure team. MF: the census and `SecureFamilyConsistencyTests` name both tables. |
| Firm clause | MF: MAT's clause shows on M's and P's invoices for any firm (flag on). MNF: it does not show on W1's invoice (W1 under P) or on M2's. MNF: F+M's clause does not show to F2. MNF: flag off shows nothing (existing control, kept). |
| UI | Scope picker states; the record picker requires a choice; the BU picker blocks an empty list; a Published set is read-only; the Replace / Adds notices. |

## 9. Owner decisions (D-172) and open questions

| # | For | Question | Decision or state |
|---|---|---|---|
| Q1 | owner, uac-r2 | Which column holds a Secure record's home business unit? | **Decided (D-141):** ordinary → `owningbusinessunit`; held Secure → the S8 snapshot written on entry to Secure by `ProvisionProjectEndpoint` (a child created under a Secure parent takes the parent's); Unsecure never uses it; interim B when there is none, with three conditions (5.2). Proposal A rejected. Column and writer are ebilling's (080, 087). |
| Q2 | owner | Storage of the BU list | Recommendation carried into 080: N:N `sprk_ruleset_businessunit` plus an "All business units" flag defaulting to Yes; an empty list never means all (S3, S4). Fallback: an intersect child table. **The owner approves it with the final script.** |
| Q3 | — | Where the BU filter sits | **Settled (D-171 points 3 and 6):** an eligibility filter before precedence; at the same level and distance, a BU-listed set beats an all-BU set (Replace types, 3.3). |
| Q4 | — | BU filter per rule or per rule set | **Settled (D-171 point 6):** per rule set only. |
| Q5 | owner | Order within the record level | **Decided (D-172):** the nearer record wins (own record over direct parent). Level comes first, so F+M beats a project-only set. |
| Q6 | owner | Do a Project's sets reach its Work assignments? | **Decided (D-172):** yes, for work assignments filed directly under the project. **Inheritance is by direct association only:** no second hop, and no inheritance through an invoice or event (#1630 proposal dropped). |
| Q7 | owner | Does replacing by type remove all lower rules of that type? | **Decided (D-172): limits replace, checklists add.** Per-type flag `BillingRuleCatalogue.IsAdditive`; full type list in 3.3. |
| Q8 | owner | Should a matter's rate card apply to its child records' invoices? | **Decided (D-172):** yes, by the same direct rule (3.5, task 085). |
| Q9 | owner | May an admin change the scope of a Published set? | **Decided (D-172):** no, `409 ruleset.scope_locked`; retire and create a new set. Drafts stay editable. |
| Q10 | owner, uac-r2 | Core-record delete with a record-scoped rule set or card | **Decided (D-141, superseding D-172 / D-137's "retire if cited"):** the record's own set is deleted deepest first when nothing surviving cites it, otherwise kept and unlinked (applies to nothing); no Retire. Rate cards: deleted with the record (D-120). No Restrict (S1). uac-r2's design amended in `86cba5a00` (4.5). |
| Q11 | uac-r2 | `sprk_ruleset` joins the secure family (lineage, ownership parent, role JSON, Read depth). Should billing admins see Secure records' rule sets? | **Decided (uac-r2 review item 1):** joins now, in one change (5.1, task 081). Admins see a set only when they can read its core record (4.6, 5.3). |
| Q12 | owner | Should the rate-card import also require AppendTo on its core record? | **Decided (D-172):** yes, as a separate fix, not in 080-085. |
| Q13 | owner | Business-unit hierarchy | **Decided (D-172):** exact business unit only. |
| Q14 | ontology-r1 | Census Waived → Routed; narrower guard; `sprk_policy` → `sprk_ruleset` lineage edge | **Agreed with conditions** (section 12), binding on 081 and 082. |
| Q15 | owner | Billing Guidelines view (135) and record-scoped sets | **Decided (D-172):** a general view plus a per-record view (section 6). |
| S1 / #2132 | owner | Delete-Restrict on the scope lookups? | **Decided (D-172): no Restrict.** Explicit scope on rule sets (S2) and rate cards (S6); loader and resolver require the lookup; nothing widens. |

## 10. Tasks (POMLs created 2026-10-20)

The WP3 band continues at **080**. The order is schema (owner approval) → RuleSetService and census routing → selection → firm clause, rate cards and UI → deploy. **The rate-card scope fix (#2132, DEF-027) is split:** its schema (S6 and the backfill) goes in 080 with the rule-set schema, because it is one owner approval and one script; its code is a task of its own, **085**, because it changes a different service (`Services/Finance/Resolution`, 046 / 078 code) with its own tests and can run beside 083 and 084. **uac-r2's review adds two tasks:** **086** (rate cards and rates join the secure family; after 081 and 085, which touch the same files) and **087** (the S8 snapshot writer, on uac-r2's hot path; after 080, beside 081-085 except where files overlap). 079 deploys all of them.

| ID | Title | Rigor | Model / effort | Depends on | Notes |
|---|---|---|---|---|---|
| 080 | Rule set and rate card scope schema: core lookups (RemoveLink), scope options, all-BU flag, BU N:N, rate card scope + backfill, field security on the scope columns (S7), the Secure business-unit snapshot column (S8), data-model docs | STANDARD | sonnet / high | 019, 017, 029 | **Owner approval of the final script required before running.** Notices to ontology-r1 and uac-r2 before and after. No SpaarkeMaster edit (079 syncs). |
| 081 | RuleSetService scope validation, Q9 lock, BU writes, routed ownership (each row's own parents); id-keyed route checks (4.6); Draft re-scope sequence (4.3); **the secure family in one change** (lineage, ownership parent, role row with probe, creator person, census, NFR-05); guard and twin guard; three-hop tests; D-141 citing-lookups notice to uac-r2 | FULL | opus / high | 080 | p-safe **false**: uac-r2 files and the shared census. No Q11 gate any more. Two adversarial passes (`auth`, `tenant-isolation`). |
| 082 | Selection: `CoreRecordChain` (direct; project pair through UAC's resolver, malformed fails closed), `RuleSetApplicability`, BU filter (D-141: `owningbusinessunit`, or the snapshot while Secure; interim B with its three conditions), lexicographic precedence for Replace types, `IsAdditive` per type | FULL | opus / high | 080, 081 | ontology-r1 owner review; uac-r2 reviews the reuse of its maps and resolver. Reads the S8 column; the writer is 087. |
| 083 | Firm clause applicability: `GetFirmClauseAsync` takes the invoice's core record; 130, 131, 133, 134, 135 POML constraints (Q15; the 135 per-record gate through UAC's evaluator; "from the parent record"; interim-B finding internal-only) | STANDARD | sonnet / high | 082 | **Before 130.** |
| 084 | Rule Review page: scope picker, record picker, BU multi-select, list columns; server-computed publish notices; uniform 404 handling | FULL | sonnet / high | 081, 082, 083 | UI plus one additive detail member (`publishNotices`, from `IsAdditive` and the applicability predicate). After 083, because both edit `RuleSetService.cs`; may run beside 085. |
| 085 | Rate card explicit scope and direct inheritance: `RateCardResolver`, `InvoiceResolutionService` rate query, import writes the scope (#2132, DEF-027, Q8) | FULL | sonnet / xhigh | 080, 082 | Fail-closed fix; Q12 is not in it. Ownership is 086's. |
| **086** (new) | Rate card and rate join the secure family; timekeeper rows get their own owner (uac-r2 review item 5) | FULL | opus / high | 081, 085 | p-safe **false** (uac-r2 files, census, `RateCardImportService.cs` after 085). Split from 085 (section 5.4). |
| **087** (new) | Secure business-unit snapshot writer: `ProvisionProjectEndpoint` (Make Secure and inherited provisioning) and the secure child-create path; membership exclusion (D-141, uac-r2 conditions a-f) | FULL | opus / high | 080 | p-safe **false** (uac-r2 hot path). uac-r2 reviews the diff before merge. A task of its own so 081 stays one secure-family change. |
| 079 (amended) | Deploy WP3: SpaarkeMaster sync of 080's schema (forms, views, the two "record deleted" views, S7 read-only controls, no form for S8), role `-Verify` / `-Apply` and creator-person `-Verify` **before** the BFF, backfill re-run, BFF, code page | STANDARD | sonnet / high | adds 080-087 | 079 had not run, so the deploy is folded into it. |

## 11. Things found while reading (not part of D-171)

- **Rate card core lookups use RemoveLink** (`Deploy-EbillingTimekeeperRateCardSchema.ps1:431-436`; the module default at `EbillingSchema.psm1:337` is `RemoveLink`), confirmed live on dev and filed as **#2132 / DEF-027**. Fixed by 080 (S6) and 085 under D-172: RemoveLink stays, the explicit scope fails closed.
- **Rate card authorization checks Read, not AppendTo**, on its core record (Q12: a separate fix, not yet filed as its own item).
- **A work assignment can carry both typed filing lookups.** If any live W under a project also carries the project's matter in `sprk_regardingmatter`, direct-only inheritance cannot be read from the data (3.1). 082 checks first.
- **D-120 did not cover rule sets.** Closed by D-141 and uac-r2's `86cba5a00` (section 4.5); the retire-if-cited branch is dropped.
- **Rate cards and rates sit outside the secure family** while their owner is routed from the core record (uac-r2 review item 5): Secure-scoped imports are refused, a later Secure is never followed, and timekeeper rows take the matter's owner (section 5.4, task 086). The workbook `sprk_document` row has the same shape (086 decides with uac-r2).
- **Read depth in SpaarkeMaster roles.** Basic and Core User hold Deep Read on `sprk_ruleset`, `sprk_Policy`, `sprk_ratecard` and `sprk_ratecardrate`, like `sprk_Matter`; the NFR-05 run decides whether it reaches the Secure business unit (081, 086).
- **Where a record created under a Secure parent is written.** On `3e11367468`: the work-assignment secure create (`ChildRecordEndpoints`, `OwnedChildWrite` core) and `ProvisionProjectEndpoint.ProvisionInheritedAsync`; `RecordCreationService` is still at `Services/Office/` (pre-W2). uac-r2 named field-mapping-server-write-path-r1's task 033, which covers system creators (To Do, task action, Signal, Decision Record); that project's 013, 020 and 022 own the core-record creates (087 coordinates with both).
- **Rate rows are loaded by `InvoiceResolutionService`, not by `BillingFactLoader`.** The "loader requires the lookup" rule for rate cards lands in `InvoiceResolutionService.BuildRateQuery` and `RateCardResolver` (085).

## 12. Review record

**Revision, 2026-10-20: this note now follows the owner's second round, ontology D-172 (#2129 comment; #2132 note), recorded as ebilling D-137.** Changes: direct-only chain (3.1); rank with distance 0 or 1 and the Replace / Additive split (3.3, 3.4); no delete-Restrict and an explicit rate-card scope (S1, S6, 3.5, 4.5); §9 turned into the decision table; §10 tasks created as POMLs 080-085 with the deploy folded into 079. The ontology-r1 gate below ("no POMLs until the owner answers Q7") is met.

### ontology-r1, 2026-10-20 (on 71d28f319): Q14 agreed, with conditions binding on 081 / 082

1. Census Waived/Pending → Routed and the narrower guard ("no `sprk_matter` ever written on a policy or version row") land in the same change. `NoWrittenPolicyOrVersionRowCarriesAMatter` stays as it is.
   - **Twin guard, in 081:** `OntologyAdminAuthoringService` never writes `sprk_ruleset`, so a policy can never carry both `sprk_matter` and `sprk_ruleset` (secure-if-any over two unrelated roots).
   - The guard is ontology-owned code: we write it, ontology-r1 reviews it.
2. The `sprk_policy` → `sprk_ruleset` lineage edge goes in, with `sprk_ruleset` in `OwnershipParentEntities`.
   - `SecureFamilyConsistencyTests` and a `SecureChildReconciler` test cover the three-hop path (version → policy → ruleset → core) end to end, both secure and unsecure, not only each edge.
3. `CitationLookups` is unchanged. Signals and Decision Records cite the policy and are owned by the invoice's core record. The ontology readers read no `sprk_ruleset` column, so no ontology-side change is needed.
4. `CoreRecordChain` sits in `Services/Signals/Billing/`, next to `BillingFactLoader`. ontology-r1 does the owner review; uac-r2 reviews its reuse of their static maps.

Not blocking:
- The §5.2 interim "unknown home BU" finding goes through BILL-SYSTEM-SUBMISSION or another defined reason, never a free-text Signal.
- §3.3: ties apply, with a publish warning.
- §3.1: an unreadable chain fails closed.

**Gate:** no 080–084 POMLs until the owner answers Q7, because the answer changes 082's shape. *(Met: Q7 answered in D-172.)*

### uac-r2, 2026-10-20: review of this note and POMLs 080-085 (findings binding)

Folded in by this revision; each item names where it now lives.

1. **S5 cannot wait for Q11.** Adding `sprk_ruleset` to `OwnershipParentEntities`, or making it Routed, fails `SecureFamilyConsistencyTests` and the census creator-person test. One change (081): role row with a live refusal probe, `-Apply` before the BFF deploy (else `0x80040299`, as #2084); `sprk_createdbyperson` on `sprk_ruleset`, `sprk_policy`, `sprk_policyversion`; `PersonBearingWriters` entry; lineage entity set `sprk_rulesets`; NFR-05 depth check on all three tables for every non-admin role, including the billing admins' `prvWritesprk_ruleset` role. Q11 is answered by this: the set joins now. (S5, S9, 4.4, 5.1; task 081.)
2. **Draft re-scope:** `ReparentAsync` on the set only with `SecureExitCaller` (F3; refusal 403 `sdap.unsecure.not_permitted`), then `SecureChildReconciler.AfterRefileAsync("sprk_ruleset", id, wasIsolated)`; `RuleSetService` held by `RefileWriterAfterRefileGuardTests`; a Published set stays locked. (4.3; task 081.)
3. **Routes keyed by the rule-set id (REQUIREMENT):** read the set's core lookup on the server; check Read (plus AppendTo for writes) as the caller before anything is read back or written; one 404 for no-access and not-found; each route's basis declared in `RouteAuthorizationGuardTests`. (4.6; task 081.)
4. **Owner parents for policy and version rows** come from each row's own lookups (`RecordOwnershipContext.ParentsOf(row)`: policy → set, version → policy); the planned `RuleSetServiceOwnerTests` expectation is corrected. (4.4; task 081.)
5. **Rate cards** are outside the lineage, role set and census while their owner is routed from the core record: Secure-scoped imports refused, a later Secure never re-owns, timekeeper rows take the matter's owner (`RateCardImportService.cs:287`). Fixed in a new task 086, not 085 (5.4 says why). Not copied into rule sets (Q8).
6. **Q1** (owner answer, D-141): ordinary → `owningbusinessunit`; `ProvisionProjectEndpoint` snapshots the prior business unit into a field-secured column read only while Secure; a child created under a Secure parent takes the parent's snapshot; Unsecure never uses it. Interim B stays, with an internal-only finding, UAC's named resolution (ambiguous fails closed), and the Secure business unit refused in `BusinessUnitIds`. No always-maintained business-unit lookup (Proposal A objections: ADR-034 membership, writable, no model-driven writer, children of Secure parents). (1.3, S8, 5.2; tasks 080, 082, 087.)
7. **Q10** (owner answer, D-141): delete the record's own uncited set deepest first, else keep and unlink; no Retire; supersedes D-137's "retire if cited"; surviving citers include Signals and Decision Records on child records' invoices. uac-r2 amends its delete design. (1.3, 4.5; task 081.)
8. **Minor:** field-secure `sprk_scope`, `sprk_firm` and the three lookups, read-only on the form (S7; 080, 079); resolve a project's pair with UAC's resolver and fail closed on a malformed pair (3.1; 082); gate the 135 per-record view with `AccessibleRecordSetService`, label inherited sets "from the parent record" without naming the parent (6; 083).

**Owner answers recorded:** Q1 and Q10 as **D-141** (decisions log), 2026-10-20.

**Coordinator corrections, same day (binding):**
- **The S8 column is ebilling's**, not uac-r2's: schema in 080, writer in the new task 087 (kept out of 081 so that 081 stays one secure-family change). uac-r2's conditions (a)-(f) are in section 5.2 and in 087; 087's review gate is uac-r2's review of the `ProvisionProjectEndpoint` and child-create diff before merge. ebilling owns the column's §11 answers (section 5.2). `MembershipOptions.GlobalFieldExclusions` is now needed (condition e); it would also have been needed under Proposal A, which stays rejected.
- **Q10:** uac-r2 amended `core-record-delete-design.md` in `86cba5a00` (§1.3 Delete / Keep rows and the hard-deny exception; new §11: deepest-first delete when uncited, else keep and unlink; no Retire; a failed citation read fails the preview with 503). 081 sends uac-r2 the exact citing lookups once fixed; their planner reads them.

### owner, 2026-10-20 (D-172 round 3, via ontology-r1)

The four rule-type calls are settled:
- (a) **ExpenseRule is split**: caps replace per category, the non-reimbursable checklist adds (tables above; 082's constraints and acceptance criteria).
- (b) CoreRecordWindow replaces.
- (c) Duplicate replaces.
- (d) AiObservation is additive.

ontology-r1 reviewed the revision from its summary and has no objection. Two conditions:
- **Rate-card backfill (080 / 085):** the list of cards marked Firm goes to the owner before 085 reaches any environment. If `sprk_ratecard` has audit history, use it to find cards whose core lookup was cleared, and mark those CoreRecord rather than Firm.
- **D-120 and rule sets:** if uac-r2 does not take the D-120 rule-set delete or retire, track it as a deferred issue. *(Met: uac-r2 took it, `86cba5a00`; D-141 drops the retire.)*
