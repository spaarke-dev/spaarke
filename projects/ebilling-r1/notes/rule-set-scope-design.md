# Rule set scope: client default, firm, core record, firm + core record, business-unit filter

> **Status**: DESIGN NOTE, revised after the owner's second round (2026-10-20). The owner's decisions are **D-171** and **D-172** (section 1); every other statement marked **Proposal** is ebilling's and is checked at the task's review. **Still open:** Q1 and Q11 (uac-r2), and the owner's approval of the final schema script (task 080).
> **Source**: GitHub #2129 (decision tables in its comments), ontology decisions **D-171** and **D-172**, the #2132 owner note. It extends ebilling D-39. Recorded in the ebilling decisions log as **D-136** (scope model) and **D-137** (scope details).
> **Branch read**: `work/ebilling-r1` at `b046154af`; this revision re-checked at `3c83d48cf` (the only `src/` change between them, `e6dcb112d`, touches none of the files cited). File and line references are to `b046154af`.
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
| Q10 | A record's own rule set or card is deleted with the record, or retired if something that survives the delete cites one of its versions. |
| Q12 | The rate-card import also requires AppendTo on its record, **as a separate fix** (not in 080-085). |
| Q13 | Exact business unit only. |
| Q15 | A general Billing Guidelines view, plus a per-record view for record-scoped sets. |

**Census state today.** The waiver is `WaiverKind.Pending` on #2129 (4d51138e9, uac-r2 conditions: the open decision is named, the end of the waiver is stated, and the guard tests are kept until the routing change). Section 4.4 moves it to Routed (task 081).

## 2. Schema (needs owner approval before running)

There is one script, `scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1`, written on `EbillingSchema.psm1` (task 080). It is idempotent, supports `-WhatIf`, and refuses to relabel or renumber an existing option (as `Deploy-EbillingSubmissionIntegrityOption.ps1` does). **The owner approves the final script before it runs.** The SpaarkeMaster sync is **not** done by the schema task. Under the pipeline rule in project CLAUDE.md §3, it goes with deploy task **079** (Deploy WP3, still open), using 029's export-to-temp procedure. 079 also adds the new columns to the rule set and rate card forms and views.

| # | Item | Additive? | Notes |
|---|---|---|---|
| S1 | `sprk_ruleset.sprk_matter`, `sprk_project` and `sprk_workassignment`: lookups to the three core tables. Relationships `sprk_matter_ruleset_matter`, `sprk_project_ruleset_project` and `sprk_workassignment_ruleset_workassignment` (the rate-card naming). | Yes | Copied from `sprk_ratecard` (`scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1:431-436`), **with the module default delete behaviour `RemoveLink`** (`EbillingSchema.psm1:337`). **No Restrict (D-172):** deleting a core record must never require deleting its rule sets first. A cleared lookup makes the set apply to nothing (S2), and cleanup follows Q10 (section 4.5). |
| S2 | `sprk_ruleset.sprk_scope`: **new option values** `Record = 100000002` and `FirmRecord = 100000003` on the existing local choice (ClientDefault 100000000 and Firm 100000001 are unchanged). | Yes (options inserted, none relabelled) | **Decided (D-172): an explicit scope, never derived from which lookups are set.** A Record-scoped set whose lookup was cleared applies to **nothing** (the loader requires exactly one lookup), instead of widening to firm or client default. The explicit value also keeps the existing publish check, which refuses an unknown scope (`RuleSetService.cs:1541-1555`). |
| S3 | `sprk_ruleset.sprk_allbusinessunits`: Two-option, display "All business units", **default Yes**. | Yes | It keeps the owner's default ("applies to all business units") explicit. With No and an empty list, the set applies to nothing, and publish refuses it (`ruleset.publish_invalid`, field `businessUnits`). An empty list therefore never means "all". |
| S4 | The business-unit list: an **N:N** between `sprk_ruleset` and `businessunit`, `sprk_ruleset_businessunit` (intersect `sprk_ruleset_businessunit`). | Yes (new relationship) | Options below; the owner approves the choice with the script. Precedent in this project: `sprk_invoiceapproval_eligibleapprover_contact` (`scripts/ebilling/Deploy-EbillingApprovalSchema.ps1:168`). **Pre-check in the script:** read `EntityDefinitions(LogicalName='businessunit')?$select=CanBeInManyToMany`, and if it is false, stop and fall back to option (b). |
| S5 | Security-role rows: `config/secure-record-owner-role.json` gains `sprk_ruleset` (kind `child`, `prvReadsprk_ruleset`), as `sprk_policy` did at `:281` (#2084). Check the **Read depth** that Basic User holds on `sprk_ruleset`: 076 notes §1 say "Core / Basic User hold Read on `sprk_ruleset`". If that depth is Organization, a Secure-team owner hides nothing. | Role change (uac-r2 owns the file) | **Not in the script:** task 081 edits the JSON after uac-r2 answers Q11. Section 5. |
| S6 | `sprk_ratecard.sprk_scope`: **new** local choice, display "Applies to", `Firm = 100000000`, `CoreRecord = 100000001`; optional, no default. Plus a **one-time backfill** of existing cards. | Yes (new column; the backfill writes only rows whose scope is empty) | **#2132 / DEF-027 (D-172).** The rate card's three core lookups stay **RemoveLink** (live on dev). Today `RateCardResolver` derives the scope from the lookups (`RateCardResolver.cs:133-142`: no lookup = firm-wide), so a card whose record was deleted becomes the firm's general card. With S6, `RateCardResolver` requires `CoreRecord` + exactly one lookup for a scoped card and `Firm` + no lookup for a firm card; anything else (empty scope included) is not used (section 3.5). **Backfill:** a card with exactly one lookup → `CoreRecord`; with none → `Firm`; with two or more → left empty and listed (the resolver already ignores it). The backfill cannot tell a genuine firm card from a card whose lookup a delete already cleared, so its output lists every card it marks `Firm` (name, id, firm) for the owner to check. Idempotent: it never overwrites a set value. |

**Options for storing the business-unit list (S4)**

| Option | For | Against |
|---|---|---|
| **(a) N:N to `businessunit` + the S3 flag (recommended)** | Native and referentially sound: a deleted business unit drops out of the list. One link-entity query in the loader. An admin subgrid comes free on the model-driven form. | Associate and disassociate are a separate write path, not one row update, so publish must re-read the list (ADR-002: the form bypasses the BFF). It depends on `businessunit` allowing N:N (S4 pre-check). |
| (b) An intersect child table `sprk_rulesetbusinessunit` (rule set + BU lookups) | It always works. It can carry its own audit columns. | It is a new table: §11 asks why the N:N is not enough. It is also one more ownership row for the census and for the Secure wall. |
| (c) JSON or text column of BU ids | No new schema surface. | No referential integrity, not queryable, invisible on the form. Rejected. |
| (d) Multi-select choice | — | Business units are data, not a fixed list. Rejected. |

**Signals reader impact: none.** The Signals readers of `sprk_policy` (`SignalReevaluationJob.ReadEnabledPoliciesAsync`, `PolicyScopeResolver.LoadEnabledPoliciesAsync`; inventory in `notes/019-policy-store-ruleset.md`) read no `sprk_ruleset` column. The design writes **no** core record onto `sprk_policy`. `sprk_policy.sprk_matter` stays the ontology's (#2084) and is never set on a billing row (section 4.4). No `sprk_policy` or `sprk_policyversion` column is added.

**Additive order is safe.** With S1-S6 deployed and no code changed: the loader selects only scope ClientDefault and Firm (`BillingFactLoader.cs:300-315`), so a Record set is never evaluated; publish refuses an unknown scope (`RuleSetService.cs:1548`); `GetFirmClauseAsync` returns None (`:2053-2056`); and `RateCardResolver` does not read S6, so rate resolution is unchanged. Nothing widens before the code lands. **One ordering rule:** the S6 backfill must have run on an environment before task 085's resolver reaches it, or every existing card applies to nothing there. 079 re-runs the backfill (idempotent) immediately before deploying the BFF, which also catches cards the old import created in between.

## 3. Selection (BillingFactLoader and InvoiceComplianceEvaluator)

The work splits the way the code does today. The **loader** decides which rule sets are *eligible* for this invoice: the level applies and the business-unit filter passes. The **evaluator** applies *precedence* per subject, where D-39 already lives (`InvoiceComplianceEvaluator.cs:264-299`).

### 3.1 The core-record chain: direct association only (D-172)

Input: `BillingEvaluationRequest.CoreRecord` (`BillingFactLoader.cs:26-27`), the invoice's own core record as the submission stamped it.

| Invoice's core record | Chain: distance 0, then distance 1 | How the direct parent is read |
|---|---|---|
| Matter M | M | — (a matter is filed under no core record) |
| Project P | P, then the matter (or project) P is filed under, if any | A project has **no typed matter lookup**. Its filing is the BFF-managed ADR-024 pair `sprk_regardingrecordid` / `sprk_regardingrecordtype`, which is field-secured, so only the BFF writes it (`SecureRootInheritance.cs:401-404` and `:482`). A pair naming a record that does not exist confers nothing. |
| Work assignment W | W, then each core record a **typed** filing lookup names: `sprk_regardingmatter` → M, `sprk_regardingproject` → P | The typed lookups only (`SecureRootInheritance.TypedFilingColumns`, `:401`). A W pair that no typed lookup repeats is **not** a filing (#1616) and is ignored. |

- **No second hop.** The parent's own parent is never read. A W filed under P inherits P's sets, and **not** the sets of P's matter. A W filed directly under M inherits M's.
- **No inheritance through an intermediate.** A W filed under an invoice or event (#1630, `IntermediateFilingColumns`, `:429`) inherits nothing through it: its chain is W alone. This is narrower than uac-r2's secure wall, which follows #1630 for **access**; billing scope does not, by the owner's decision.
- **Downward only.** A project's set never applies to its parent matter's own invoices.
- **A W carrying both typed lookups** is directly filed under both by uac-r2's definition, so both are at distance 1 and both sets are eligible. If a field-mapping profile copies P's matter onto `sprk_regardingmatter` when W is created under P, the data cannot tell "under P only" from "under P and M", and the owner's example (W under P does not inherit M) would not hold. Task 082 counts such rows on dev first and escalates if any exist.
- **Reuse, not a new map.** `CoreRecordChain` (internal, `Services/Signals/Billing/`, task 082) reads **one row**, the invoice's core record, app-only: its typed filing lookups (W) or its pair (P), and in the same read its business-unit column (section 5.2). It reuses uac-r2's static `TypedFilingColumns` and the project-pair rule; it does not call `FindFilingParentsAsync` (`SecureRootInheritance.Cascade.cs:401`), which needs the scoped provisioning graph (§11: existing = the `SecureRootInheritance` maps; extension = the read is ours, the maps are theirs; cost of doing nothing = a project invoice never sees its matter's terms).
- **Fail closed:** a chain row that cannot be read is a Dataverse fault and propagates (the loader's contract, `BillingFactLoader.cs:243-244`). The job retries. The invoice is **never** judged on a shortened chain, because that would silently drop the parent's negotiated terms.

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
| ExpenseRule | Replace | **ebilling's call.** The owner lists "expense caps" among the limits. The body also carries `nonReimbursableCodes`, so a specific ExpenseRule replaces the general code list as well. To keep the general codes, the specific rule must restate them; the publish notice names the codes it drops. Rejected: Additive (the general $500 cap would still fire against a matter's $1,000 cap, so a limit could never be raised); splitting the type in two (a new `sprk_ruletype` option, schema and handler on the ontology-shared store, out of proportion). |
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
| ExpenseRule | Replace | CD | **CD** | Not covered more specifically: falls through |

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

On publish, a Record or FirmRecord set whose lookup was cleared fails the "exactly one" row: it cannot be re-published, and the loader ignores it (3.2).

**Inputs** follow the 078 doctrine (`RateCardImportEndpoints.cs:20-22` and `:166-187`). `matterId`, `projectId` and `workAssignmentId` are **query** parameters on create and on `PUT /{id}/assignment`, so the authorization filter and the handler read the same values (task 130 defect C8). The business-unit ids go in the assignment body (`RuleSetAssignmentBody` gains `AllBusinessUnits` and `BusinessUnitIds`). They need no record check, because a business unit is not secured data, but the service checks that each one exists.

**Authorization on the core record.** The rate card checks only **Read** (`finance.read`, `RateCardImportEndpoints.cs:120-133`), not AppendTo. **Proposal:** require **Read and AppendTo** as the caller, because the set's lookup is filed *at* the record, which is the `finance.submit_invoice` reasoning (`src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs:195-201`). It uses a new key, `ebilling.ruleset.scope` = `Read | AppendTo`. Reusing `finance.submit_invoice` would describe the act wrongly in every deny, the precedent noted at `:251-253`. Refusal: 403 `sdap.access.deny.*` from the filter, before the service runs. The rate-card import moves to AppendTo separately (Q12).

### 4.2 Business-unit list writes

The service writes the S3 flag and, in the same call, does associate / disassociate against the N:N, as the app identity (like every other rule-set write). Associates are idempotent. A failure part-way through is reported as `500 ruleset.write_failed`, and the next call converges it. **Publish re-reads the list** and refuses No-plus-empty. The list is returned on `RuleSetSummary` (new members `AllBusinessUnits` and `BusinessUnitIds`, both additive).

### 4.3 Changing a published set's scope (decided, Q9)

Today `AssignAsync` changes the scope or firm of a **Published** set; it refuses only a Retired one (`:1919-1922`). Under D-172 Q9 it **refuses**: changing the scope, firm, core record or BU list of a Published set returns `409 ruleset.scope_locked` ("Retire this rule set and create a new one for the new scope."). This also changes today's Firm re-assign of a Published set. On a Draft set the change is allowed, and it re-owns the set and its rows (section 4.4). A later "copy to a new rule set" action can make the retire-and-recreate path cheap.

### 4.4 Routed ownership

| Set level | Owner resolution for `sprk_ruleset`, `sprk_policy` and `sprk_policyversion` |
|---|---|
| Record, FirmRecord | `ResolveOwnerAsync(RecordOwnershipContext.ForParents([core record], caller) with { RequestedBy = caller })`, as `RateCardImportService.cs:200-211` does. A Secure core record resolves the Secure team (secure-if-any), and an ordinary one resolves the record's team. A refusal writes nothing (`OwnerRefused`, 409). |
| ClientDefault, Firm | Unchanged: no parents, so the caller's business-unit team (`OwnerContext`, `RuleSetService.cs:2389-2393`). |

The call sites that change are create (`:405`), `AddDraftsAsync` (`:871`, which creates the policies and versions in `CreateDraftRuleAsync`, `:1060-1100`), and `EditAsync`'s new version (`:1277`, the `VersionIdFor` create that the scanner cannot see). Each passes `OwnerContext(caller, setCore)`, where `setCore` is the set's lookup read in the same load. A Draft scope change (section 4.3) re-owns the set, its policies and its versions through `IRecordOwnershipResolver.ReparentAsync` (`RecordOwnershipResolver.cs:146`). That makes it a REPARENT, so it is listed in the census's `UnscannedWriters` (`RecordOwnerAssignmentCensusTests.cs:306`).

**Census and guard, replaced in the same change (ontology-r1 condition 1, section 12):**

- Add `sprk_ruleset` to `ChildTables` (`RecordOwnerAssignmentCensusTests.cs:47-61`). The `sprk_policy` and `sprk_policyversion` entries (`:265-266`) go from `Waived` to **`Routed`**. A new entry, `("RuleSetService.cs", "sprk_ruleset", 1, Routed)`, is added, and `RuleSetServicePolicyWaiver` (`:127-139`) is deleted.
- `RuleSetServiceNoMatterScopeGuardTests` is **replaced**, not deleted. Its blanket ban on any matter identifier in `RuleSetService.cs` would fail the new code. The new guard is narrower: **no `sprk_matter` is ever written on a `PolicyEntity` or `VersionEntity` row** (that column is the ontology's matter scope). It has a must-fire control (a planted `policy["sprk_matter"] = …` fails) and a must-not-fire control (`ruleSet["sprk_matter"] = …` passes). The behavioural twin `RuleSetServiceOwnerTests.NoWrittenPolicyOrVersionRowCarriesAMatter` stays as it is.
- **Twin guard:** `OntologyAdminAuthoringService` never writes `sprk_ruleset`, so a policy can never carry both `sprk_matter` and `sprk_ruleset`. Ontology-owned code: ebilling writes it, ontology-r1 reviews it.
- `RuleSetServiceOwnerTests` adds: a record-scoped create passes exactly `[core record]` as parents; a Secure matter yields the Secure team as owner of the set, policy and version; and a client-default create still passes none.
- The `AssignCallers` reason for `RuleSetEndpoints.AssignRuleSet` (`:770-772`) is updated: a Draft re-scope now re-owns.

### 4.5 Core-record delete and cleanup (D-172, Q10)

- **No delete-Restrict.** Deleting a core record never needs its rule sets or rate cards deleted first. RemoveLink clears their lookup; the explicit scope (S2, S6) then makes them apply to nothing.
- **Cleanup through the BFF delete flow (uac-r2's core-record delete, D-120).** D-120 already deletes a rate card scoped 1:1 to the deleted record. It is extended (D-137): a record's **own** Record or FirmRecord rule set (with its policies and versions) and its own rate card are **deleted** with the record, or **retired** when something that survives the delete cites one of their versions. Since UAC owner round 107, Decision Records and the Signals that triggered them are deleted with the core record, so a citation only by them no longer keeps the row. The rule goes to uac-r2 as a notice from task 081; the delete step is uac-r2's code.
- **Deletes outside the BFF** (an admin or the named delete role, after UAC delete D6): the rows stay, orphaned and inert. 079 adds two admin views, "Rule sets whose record was deleted" (scope Record or FirmRecord, all three lookups empty) and "Rate cards whose record was deleted" (scope CoreRecord, all three lookups empty), so an admin can retire or delete them.

## 5. Ownership and Secure records

### 5.1 Lineage (uac-r2 files, uac-r2 review; Q11 open)

| Table | Change | File |
|---|---|---|
| `sprk_ruleset` | **New child entry**: `("sprk_matter", Matter)`, `("sprk_project", Project)`, `("sprk_workassignment", WorkAssignment)` | `Services/Access/SecureChildLineage.cs` (beside `sprk_policy`, `:175-178`) |
| `sprk_policy` | Add `("sprk_ruleset", "sprk_ruleset")` next to the existing `("sprk_matter", Matter)` | same |
| `sprk_policyversion` | Unchanged: it reaches the matter through `sprk_policy` → `sprk_ruleset` | — |
| `OwnershipParentEntities` | Add `sprk_ruleset` (a rule set is the parent of its policies, as `sprk_policy` is of its versions, `:1040-1043`) | `Services/Dataverse/RecordOwnershipResolver.cs:1024` |
| `CitationLookups` | Unchanged. `sprk_signal.sprk_policy` still cites rather than files (`:1061-1062`), so a Signal is never pulled into a matter set's wall through the rule it cites. | — |

Without the `sprk_policy` → `sprk_ruleset` edge, a billing policy has no lineage to its matter, because `sprk_policy.sprk_matter` is never set on billing rows. Securing the matter later (Q5 of D-171) would then leave the rule set's policies and versions in the ordinary business unit. With the edge, `FilingParentOf` (`RecordOwnershipResolver.cs:610`) and `SecureChildReconciler` re-own the set, its policies and its versions on secure and unsecure. `SecureChildLineageTests` and `SecureFamilyConsistencyTests` change with it, and the three-hop path (version → policy → ruleset → core) is tested end to end (ontology-r1 condition 2).

### 5.2 Which business unit is matched (Q1 open, uac-r2)

- **Proposal: the invoice's own core record's "home" business unit.** That is the record the invoice is filed under, not the rule set's record and not a parent's. Exact match only (Q13 decided): a child business unit does not match a list that names its parent business unit.
- **The problem:** a Secure matter is re-owned to the Secure Record Owners team, so its `owningbusinessunit` becomes the Secure Record business unit (`RecordOwnershipResolver.cs:86-87`, `:2181`). There is no column today that keeps the business unit it had before. `sprk_matter` has only the system `owningbusinessunit` (`docs/data-model/sprk_matter-related-tables.md:96`). Unsecuring reassigns the record to a configured or calling user (`UnsecureProjectRequest.cs`, `ReassignToSystemUserId`), not back to a stored business unit.
- **Proposal A (recommended, uac-r2 decides, Q1):** a lookup to `businessunit` on matter, project and work assignment, for example `sprk_homebusinessunit`. It is stamped from the owner's business unit at create (and on an ordinary re-own) and is **never** changed by a secure re-own. The loader reads `sprk_homebusinessunit ?? owningbusinessunit`. The column is uac-r2's schema, and it is needed elsewhere too: unsecure could return the record to it.
- **Interim (until Q1 is answered), Proposal B, built in 082:** when the core record is owned in the Secure Record business unit, its home business unit is **unknown**. Only all-BU sets are eligible. A BU-restricted set does not apply, and the evaluator raises **one invoice-level, flag-only finding** through a defined reason on `BILL-SYSTEM-SUBMISSION` (never a free-text Signal; ontology-r1, section 12): "Business-unit rule sets were not checked: the record's business unit is not known." This follows the D-115 currency-skip pattern, so a rule that is silently missing is visible. The Secure Record business unit is never offered in the admin's business-unit picker, and it never matches a list.

### 5.3 Who can see a record-scoped rule set (Q11 open, uac-r2)

- **Dataverse (model-driven):** a set on a Secure record is owned by the Secure team, so a non-member does not read it, **provided** `sprk_ruleset` Read is business-unit depth for ordinary roles (S5).
- **Rule Review page (BFF):** `ListAsync` and `GetAsync` read app-only behind the billing-admin privilege (`RuleSetService.cs:516-551`, `BillingAdminAuthorization`). As things stand, every billing admin would see the name of a Secure matter's rule set and its link to the matter. **Proposal:** List and Get drop, or 404, a record-scoped set whose core record the caller cannot **Read**, checked as the caller with one batched check per page. Every write to a record-scoped set requires Read and AppendTo (section 4.1). uac-r2 to confirm (Q11).
- **Evaluation:** app-only, unaffected (the loader's contract).

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
| **Firm Billing Guidelines view (135)** | `tasks/135-spa-billing-guidelines.poml:47,74`: "client-default rule sets plus that firm's rule sets" | **Decided (Q15):** the general view lists all-BU ClientDefault and Firm sets, as specified today. A per-record view (`coreRecordId`, a record the CIAM caller holds an external grant on) returns the full post-precedence list for that record, including Record, FirmRecord and BU-restricted sets. Without a record the firm sees no record-scoped set. 083 adds this constraint to 135's POML. |
| `RuleSetEndpoints` create / `TryParseScope` | `RuleSetEndpoints.cs:227-231`, `:478` | Accepts `Record` and `FirmRecord`. Error text "scope must be ClientDefault or Firm" changes. |
| Rule Review page | `src/solutions/RuleReview/src/services/ruleSetApi.ts:20` (`RuleSetScope = 'ClientDefault' \| 'Firm'`), `components/useFirmNames.ts:36-40` (`describeScope` → "Not assigned"), `components/AssignmentPanel.tsx`, `components/RuleSetListView.tsx:61-65` | Section 7. |
| Task 102 (adjustment rule list, D-74) | `tasks/102-*.poml` | Must use the loader's eligibility and precedence, not its own scope filter. |
| Ontology / Signals readers | `SignalReevaluationJob`, `PolicyScopeResolver`, `RuleDomain.cs:77` | **None**: no `sprk_policy` column changes; billing rows never carry `sprk_policy.sprk_matter`. |
| `BillingRuleDraftJobHandler`, `BillingFindingSignalMapper` | — | None (they read the rule-set id only, or nothing). |

## 7. Admin UI (Rule Review page, task 084)

- **Scope picker** (`AssignmentPanel.tsx`): a Dropdown with *Client default*, *A firm*, *A matter, project or work assignment* and *A firm on a matter, project or work assignment*. It is shown on Draft sets only; a Published set shows the scope read-only with the hint "Retire and create a new rule set to change who it applies to" (Q9).
- **Record picker:** a **type** choice (Matter / Project / Work assignment) and a `LookupField`, reusing the read-only `Xrm.WebApi` adapter pattern of `services/firmLookup.ts`. Search goes through a new page-local `searchCoreRecordsAsLookup(type, q)` (`services/coreRecordLookup.ts`, beside `firmLookup.ts`) modelled on `searchOrganizationsAsLookup` (`Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts:736`). `PolymorphicPicker` is **not** recommended: it delegates to `Xrm.Utility.lookupObjects`, a host dialog, which gives no testable inline search. The rate card has **no UI** (078 notes: "admins call the routes"); its scope is set by the import.
- **Business-unit multi-select:** "Applies to all business units" (checkbox, default on). When it is off, a `TagPicker` of business units searched through `Xrm.WebApi` (`businessunits`, excluding the Secure Record business unit by name, `SecureRecord:BusinessUnitName`). Publish shows the existing notice plus "Applies only to: Litigation, Corporate".
- **List columns** (`RuleSetListView.tsx`): *Name*, *Applies to* (for example "Client default", "Firm: Smith LLP", "Matter: M-1001", "Smith LLP on Matter: M-1001"), *Business units* ("All" or the names), *Status*, *Visible to firms*, *Rules*. Record names come from the summary: the service returns `coreRecord {type, id, name}`, so the page makes no N+1 lookups. A set whose record was deleted shows "Record deleted: applies to nothing".
- **Publish notices** (computed by RuleSetService and returned on the rule set detail as `publishNotices`, task 084): same-level ties of a Replace type (3.3); for a record-level set, per rule, "Replaces the general <type> rule" or "Adds to the general <type> rule" from `IsAdditive`; for ExpenseRule, the general non-reimbursable codes it drops; for ApprovedTimekeeper, "repeats the general timekeeper check".
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
| Census / ownership | MF: `EveryRoutedEntryRoutesThroughTheResolver` covers the three Routed entries. MF: the new guard fires on a planted `sprk_matter` write on a policy or version. MNF: it does not fire on a rule-set matter write. MF: the twin guard fires on a planted `sprk_ruleset` write in `OntologyAdminAuthoringService`. MF: `RuleSetServiceOwnerTests` asserts parents = `[core record]` and the Secure team as owner for a Secure matter. MNF: client default resolves with no parents. Lineage: `SecureChildLineageTests`, and `SecureFamilyConsistencyTests` plus a `SecureChildReconciler` test over version → policy → ruleset → core, secure and unsecure. |
| Firm clause | MF: MAT's clause shows on M's and P's invoices for any firm (flag on). MNF: it does not show on W1's invoice (W1 under P) or on M2's. MNF: F+M's clause does not show to F2. MNF: flag off shows nothing (existing control, kept). |
| UI | Scope picker states; the record picker requires a choice; the BU picker blocks an empty list; a Published set is read-only; the Replace / Adds notices. |

## 9. Owner decisions (D-172) and open questions

| # | For | Question | Decision or state |
|---|---|---|---|
| Q1 | uac-r2 | Which column holds a Secure record's home business unit? | **OPEN.** Recommendation: a `sprk_homebusinessunit` lookup on the three core tables, stamped at create and untouched by the secure re-own (5.2 A). 082 builds interim behaviour B until it is answered. |
| Q2 | owner | Storage of the BU list | Recommendation carried into 080: N:N `sprk_ruleset_businessunit` plus an "All business units" flag defaulting to Yes; an empty list never means all (S3, S4). Fallback: an intersect child table. **The owner approves it with the final script.** |
| Q3 | — | Where the BU filter sits | **Settled (D-171 points 3 and 6):** an eligibility filter before precedence; at the same level and distance, a BU-listed set beats an all-BU set (Replace types, 3.3). |
| Q4 | — | BU filter per rule or per rule set | **Settled (D-171 point 6):** per rule set only. |
| Q5 | owner | Order within the record level | **Decided (D-172):** the nearer record wins (own record over direct parent). Level comes first, so F+M beats a project-only set. |
| Q6 | owner | Do a Project's sets reach its Work assignments? | **Decided (D-172):** yes, for work assignments filed directly under the project. **Inheritance is by direct association only:** no second hop, and no inheritance through an invoice or event (#1630 proposal dropped). |
| Q7 | owner | Does replacing by type remove all lower rules of that type? | **Decided (D-172): limits replace, checklists add.** Per-type flag `BillingRuleCatalogue.IsAdditive`; full type list in 3.3. |
| Q8 | owner | Should a matter's rate card apply to its child records' invoices? | **Decided (D-172):** yes, by the same direct rule (3.5, task 085). |
| Q9 | owner | May an admin change the scope of a Published set? | **Decided (D-172):** no, `409 ruleset.scope_locked`; retire and create a new set. Drafts stay editable. |
| Q10 | owner, uac-r2 | Core-record delete with a record-scoped rule set or card | **Decided (D-172):** deleted with the record, or retired if something that survives the delete cites one of its versions (Decision Records and Signals no longer count, UAC round 107). No Restrict (S1): a cleared lookup applies to nothing (4.5). |
| Q11 | uac-r2 | `sprk_ruleset` joins the secure family (lineage, ownership parent, role JSON, Read depth). Should billing admins see Secure records' rule sets? | **OPEN.** Recommendation: yes to joining (5.1); the admin list is filtered by the caller's Read on the core record (5.3). |
| Q12 | owner | Should the rate-card import also require AppendTo on its core record? | **Decided (D-172):** yes, as a separate fix, not in 080-085. |
| Q13 | owner | Business-unit hierarchy | **Decided (D-172):** exact business unit only. |
| Q14 | ontology-r1 | Census Waived → Routed; narrower guard; `sprk_policy` → `sprk_ruleset` lineage edge | **Agreed with conditions** (section 12), binding on 081 and 082. |
| Q15 | owner | Billing Guidelines view (135) and record-scoped sets | **Decided (D-172):** a general view plus a per-record view (section 6). |
| S1 / #2132 | owner | Delete-Restrict on the scope lookups? | **Decided (D-172): no Restrict.** Explicit scope on rule sets (S2) and rate cards (S6); loader and resolver require the lookup; nothing widens. |

## 10. Tasks (POMLs created 2026-10-20)

The WP3 band continues at **080**. The order is schema (owner approval) → RuleSetService and census routing → selection → firm clause, rate cards and UI → deploy. **The rate-card scope fix (#2132, DEF-027) is split:** its schema (S6 and the backfill) goes in 080 with the rule-set schema, because it is one owner approval and one script; its code is a task of its own, **085**, because it changes a different service (`Services/Finance/Resolution`, 046 / 078 code) with its own tests and can run beside 083 and 084.

| ID | Title | Rigor | Model / effort | Depends on | Notes |
|---|---|---|---|---|---|
| 080 | Rule set and rate card scope schema: core lookups (RemoveLink), scope options, all-BU flag, BU N:N, rate card scope + backfill, data-model docs | STANDARD | sonnet / high | 019, 017, 029 | **Owner approval of the final script required before running.** Notices to ontology-r1 and uac-r2 before and after. No SpaarkeMaster edit (079 syncs). |
| 081 | RuleSetService scope validation, Q9 lock, BU writes, routed ownership; census Waived → Routed; guard and twin guard; lineage `sprk_ruleset` with three-hop tests; D-120 extension notice | FULL | opus / high | 080 | p-safe **false**: uac-r2 files and the shared census. Q11 blocks S5 and the List/Get filter (escalation). Two adversarial passes (`auth`, `tenant-isolation`). |
| 082 | Selection: `CoreRecordChain` (direct), `RuleSetApplicability`, BU filter, lexicographic precedence for Replace types, `IsAdditive` per type, interim unknown-BU finding | FULL | opus / high | 080, 081 | ontology-r1 owner review; uac-r2 reviews the reuse of its maps. Q1 decides the BU column (interim B until then). |
| 083 | Firm clause applicability: `GetFirmClauseAsync` takes the invoice's core record; 130, 131, 133, 134, 135 POML constraints (Q15) | STANDARD | sonnet / high | 082 | **Before 130.** |
| 084 | Rule Review page: scope picker, record picker, BU multi-select, list columns; server-computed publish notices | FULL | sonnet / high | 081, 082, 083 | UI plus one additive detail member (`publishNotices`, from `IsAdditive` and the applicability predicate). After 083, because both edit `RuleSetService.cs`; may run beside 085. |
| 085 | Rate card explicit scope and direct inheritance: `RateCardResolver`, `InvoiceResolutionService` rate query, import writes the scope (#2132, DEF-027, Q8) | FULL | sonnet / xhigh | 080, 082 | Fail-closed fix; Q12 is not in it. |
| 079 (amended) | Deploy WP3: SpaarkeMaster sync of 080's schema (forms, views, the two "record deleted" views), backfill re-run, BFF, code page | STANDARD | sonnet / high | adds 080-085 | 079 had not run, so the deploy is folded into it. |

## 11. Things found while reading (not part of D-171)

- **Rate card core lookups use RemoveLink** (`Deploy-EbillingTimekeeperRateCardSchema.ps1:431-436`; the module default at `EbillingSchema.psm1:337` is `RemoveLink`), confirmed live on dev and filed as **#2132 / DEF-027**. Fixed by 080 (S6) and 085 under D-172: RemoveLink stays, the explicit scope fails closed.
- **Rate card authorization checks Read, not AppendTo**, on its core record (Q12: a separate fix, not yet filed as its own item).
- **A work assignment can carry both typed filing lookups.** If any live W under a project also carries the project's matter in `sprk_regardingmatter`, direct-only inheritance cannot be read from the data (3.1). 082 checks first.
- **D-120 does not yet cover rule sets.** uac-r2's core-record delete deletes 1:1 rate cards; the rule-set half and the retire-if-cited branch are new (D-137, notice from 081).
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

### uac-r2: review queued (§5.2/Q1, §5.1/Q11, Q10, §3.1). Facts received meanwhile are folded into Q10 and DEF-027.
