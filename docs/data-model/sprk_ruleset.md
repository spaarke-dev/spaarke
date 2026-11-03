# sprk_ruleset and the billing columns on the policy store

> **Project**: ebilling-r1, task 019 (FR-09, FR-27, FR-28, D-26, D-66, D-79)
> **Last Updated**: 2026-10-21 (task 080 additions below; run on dev 2026-10-21 and verified against live metadata: projects/ebilling-r1/notes/080-dev-run-2026-10-21.md)
> **Scripts**: `scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1` and, for `sprk_rejectionreason` (task 077, D-113), `scripts/ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1` (both idempotent, additive; a second run changes nothing)
> **Task 080 script**: `scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` (EB-D-148: the Procedure, Procedure Version and Procedure Assignment tables, the record scope and links below, the billing columns on the core records). Table docs for the three new tables are task 189's.
> **Owner of the policy tables**: spaarke-ontology-platform-r1 (`Services/Signals/**`). This task only adds columns to them.

Billing rules share the ontology policy store (D-23, D-30): one rules system, two strategies. A **policy** (`sprk_policy`) holds what is stable across versions, a **policy version** (`sprk_policyversion`) holds one immutable published rule body, and the new **rule set** (`sprk_ruleset`) groups the policies drafted from one guideline, rate card or procedure document so that they can be published, scoped to a client default or a firm, cited and shown to firms as a unit (FR-28). Created additively in dev on 2026-10-15; no plugin, business rule or calculated column (ADR-002). Admin forms and views are task 029's.

Nothing existing changed: no relabel, delete or renumber of a policy column or option (`sprk_ruletype` is still Threshold 100000000, Switch 100000001, Existence 100000002), and no policy row was written (no backfill).

## Global option set `sprk_policydomain`

| Label | Value | Meaning |
|---|---|---|
| Billing | 100000000 | eBilling rules (the 11 billing rule types are added to `sprk_ruletype` by task 070) |
| Ontology | 100000001 | Signals / ontology rules. **An empty `sprk_domain` on a policy reads as Ontology** (no backfill of the existing rows) |
| Docketing | 100000002 | Reserved (D-26), unused in R1 |

## sprk_ruleset (Rule Set)

User-owned (like `sprk_policy`), entity set `sprk_rulesets`, primary name `sprk_name`.

| Logical name | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200), primary name, application-required | Shown to firms when the rule set is visible to firms (D-66). Required by the BFF write path (task 076), not enforced on API writes: Dataverse enforces ApplicationRequired on forms only. |
| `sprk_domain` | Choice, global `sprk_policydomain` | Billing in R1. |
| `sprk_sourcedocument` | Lookup -> `sprk_document` (delete RemoveLink) | The Procedure Library document the rules were drafted from. Internal only (D-63): never exposed to firm users. Relationship `sprk_document_ruleset_sourcedocument`. |
| `sprk_scope` | Choice (local) | Client default = 100000000, Firm = 100000001, **Record = 100000002, Firm record = 100000003** (task 080). A **record-scoped** rule set (Record / Firm record) holds one record's rule changes (model 6.3). **A Record / Firm record set with no record lookup applies to nothing**: the loader requires the scope value *and* a non-null record lookup, so a set unlinked by a record delete can never read as firm-wide. **Field-secured (BFF-only writes)**: Read for every user, Create / Update for the BFF application user only. |
| `sprk_firm` | Lookup -> `sprk_organization` (delete RemoveLink) | Set only when the scope is Firm. **Enforced by task 076, not by schema.** Relationship `sprk_organization_ruleset_firm`. |
| `sprk_visibletofirms` | Two-option, default **No** | See "Firm visibility" below. |
| `sprk_matter`, `sprk_project`, `sprk_workassignment` | Lookup -> the core table (delete **RemoveLink**, optional) | Task 080: one record lookup per core table in `InvoiceFamilyCoreRecordGuard.FilingCores` (read from that source by the script; a core record added there gets its lookup on the next run). Exactly one is set on a Record / Firm record rule set (BFF-enforced). Deleting the record clears the lookup and the set then applies to nothing (D-172). Relationships `sprk_matter_ruleset_matter`, `sprk_project_ruleset_project`, `sprk_workassignment_ruleset_workassignment`. **Field-secured (BFF-only writes).** |
| `sprk_procedure` | Lookup -> `sprk_procedure` (delete **Restrict**, optional) | Task 080: the Procedure this rule set belongs to. Restrict: a Procedure with a rule set cannot be deleted (retire it), because Signals cite its rules. Relationship `sprk_ruleset_procedure_procedure`. **Field-secured (BFF-only writes).** |
| `sprk_createdbyperson` | Lookup -> `systemuser` (delete RemoveLink) | Task 080 (O-23, S9): the person who asked for the record; the BFF stamps it. Created unsecured; task 081 secures it. Relationship `sprk_systemuser_sprk_ruleset_createdbyperson`. |
| `sprk_effectivefrom` / `sprk_effectiveto` | Date only | In-force window (D-74: the rules applied to an invoice are those of rule sets in force on the invoice date). `sprk_effectiveto` empty = open ended. |
| `sprk_notconverted` | Multiline text (100,000) | JSON list of guideline text the drafting Action (`billing-rule-draft`, task 075) could not convert into a supported rule. |

### Status (`statecode` / `statuscode`)

| statecode | statuscode | Label |
|---|---|---|
| 0 (Active) | 1 | Draft (the system value 1, relabelled from Active) |
| 0 (Active) | 659490000 | Published (inserted) |
| 1 (Inactive) | 2 | Retired (the system value 2, relabelled from Inactive) |

A new rule set reads statecode 0 / statuscode 1 = Draft.

### Firm visibility (D-66, D-79)

`sprk_visibletofirms` decides whether a firm sees the **content** of a rule set: the clause quote and section reference on a finding, and the SPA Billing Guidelines view. Firms never see the guideline document itself.

- Flag **on**: the firm-visible output carries the rule set name and version, the section reference and the quoted clause text.
- Flag **off**: the firm sees only the reason and the rule set name. No clause text, no section reference, no Procedure Library link (D-79).
- The column default is **No** (fail closed): a rule set created without the flag set reads No (checked live: create with a name only reads `sprk_visibletofirms = false`). The initial value for billing guidelines and rate cards (on) and internal procedures (off) is set by task 076 from the source kind when it creates the rule set.
- The admin sees "clause quotes will be visible to firms" at publish when the flag is on (FR-28).

## sprk_policy (additive)

Existing columns are unchanged. Added:

| Logical name | Type | Notes |
|---|---|---|
| `sprk_domain` | Choice, global `sprk_policydomain` | Empty = Ontology. Billing policies set Billing. |
| `sprk_ruleset` | Lookup -> `sprk_ruleset` (delete RemoveLink) | The rule set the policy belongs to. Stable across versions. Relationship `sprk_ruleset_policy_ruleset`. |
| `sprk_createdbyperson` | Lookup -> `systemuser` (delete RemoveLink) | Task 080 (D-173, ontology F4-1): the person who asked for the policy; the BFF stamps it. Created unsecured; task 081 secures it. Relationship `sprk_systemuser_sprk_policy_createdbyperson`. |

## sprk_policyversion (additive)

Existing columns are unchanged. Added; clause and consequence sit on the version because a published version is immutable.

| Logical name | Type | Notes |
|---|---|---|
| `sprk_clausereference` | Multiline text (4,000) | The verbatim clause quote from the source guideline (D-66, FR-38). |
| `sprk_clauselocation` | Text (200) | Section and page of the clause. |
| `sprk_consequence` | Choice (local) | Reject submission = 100000000, Flag = 100000001, Suggest adjustment = 100000002. Empty on ontology rows. A rejection also needs the confidence threshold and a reject-eligible invoice type (FR-26, I-5). The type limits the value (task 073, `BillingRuleCatalogue.AllowedConsequences`): Reject only on ApprovedRate, ExpenseRule, InvoiceAge, CoreRecordWindow, Duplicate and RequiredData (not ApprovedTimekeeper, D-89; not TimeIncrement, DailyHoursCap, NonBillableActivity or AiObservation); Suggest adjustment only on ApprovedRate and NonBillableActivity; Flag on every type. Accept, edit, publish and drafting refuse the rest (`consequence_not_allowed`), and the billing gate refuses a stored version that carries one. |
| `sprk_proposalstatus` | Choice (local) | Proposed = 100000000, Accepted = 100000001, Rejected = 100000002. State of a rule drafted by the Action (FR-27, FR-28). Empty on ontology rows. |
| `sprk_rejectionreason` | Multiline text (2,000) | Why an admin rejected the draft (task 077, D-113). Set only by a reject, in the same conditional write as `sprk_proposalstatus = Rejected`; cleared by an accept and by an edit that returns the draft to Proposed. Required (1 to 2,000 characters after trimming) on the reject route, enforced by the BFF, not by schema. **Admin-only**: returned by the Rule Review read, never by a firm projection (`GetFirmClauseAsync` selects its columns by name). Empty on every version that was never rejected. Created by `Deploy-EbillingRuleRejectionReasonSchema.ps1` (task 079 runs it on dev; task 053 syncs SpaarkeMaster). |

### `sprk_ruletype` (local choice) - options and the system value

`sprk_policyversion.sprk_ruletype` is a **local** choice (not global). Options: Threshold 100000000, Switch 100000001, Existence 100000002 (Ontology); the eleven authorable billing types ApprovedTimekeeper 100000003 to AiObservation 100000013 (task 070, `scripts/ebilling/Deploy-EbillingRuleTypeOptions.ps1`); and, added by task 049 (D-125), the **system** type **SubmissionIntegrity = 100000014** (`scripts/ebilling/Deploy-EbillingSubmissionIntegrityOption.ps1`, idempotent, refuses a global choice or a relabelled option). Solution source (`SpaarkeMaster` `sprk_PolicyVersion/Entity.xml`) carries all 15; `RuleTypeOptionSetSyncTests` pins the enum to it.

SubmissionIntegrity is the marker of the seeded policy `BILL-SYSTEM-SUBMISSION` (`scripts/ebilling/Seed-BillingSystemPolicy.ps1`): lane Decide, subject type Invoice, severity Info, domain Billing, **no rule set and no matter**, enabled, one version in force from 2026-10-19 with no end date, consequence Flag, body `{}`. It backs the findings task 049 creates directly: `submission.invoice_number_mismatch` (D-91) and the 047 coding findings `coding.refused_code`, `coding.firm_code_mismatch`, `coding.code_set_needs_review`. Two of its reasons are the variants used when firm-supplied text is not safe to quote (task 049 F1-3; the value is left out, never shortened): `coding.firm_code_mismatch_not_shown` (the billed code is not code-shaped, `BillingRuleCatalogue.IsCodeShaped`; headline "Unrecognised billed code differs from the suggested code") and `submission.invoice_number_mismatch_not_shown` (the number read from the document is not invoice-number-shaped, `BillingRuleCatalogue.IsInvoiceNumberShaped`; the sentence cites only the typed number; headline "Invoice number read differs from the submission"). Both are flag-only like the rest; the invoice-number variant keeps the D-91 reason's precedence (it titles a shared Signal ahead of the coding notes). It is **never authorable** (absent from the drafting Action's options; `PolicyVersionValidator.ValidateBilling` and `ValidateForAuthoring` refuse it as `rule_type_unsupported`, "system rule; not authorable"), never evaluated from a body (no handler; the evaluator skips a stored row), flag-only (`AllowedConsequences` = Flag). An administrator may turn it off (`sprk_enabled`); new versions are refused on every BFF route (the model-driven app has no such refusal); deletion is refused only by the Dataverse privilege on `sprk_policy` (no code-level refusal), so deleting the row in the model-driven app stops these Signals and the ontology sweep closes the open ones as PolicyRetired; the findings stay in the submission's findings JSON either way.

Added by task 080 (EB-D-148, model 7.2):

| Logical name | Type | Notes |
|---|---|---|
| `sprk_procedureversion` | Lookup -> `sprk_procedureversion` (delete **Restrict**, optional) | The Procedure Version this policy version belongs to. Restrict: a Procedure Version with policy versions cannot be deleted. Relationship `sprk_policyversion_procedureversion_procedureversion`. **Field-secured (BFF-only writes).** |
| `sprk_publishstate` | Choice, global `sprk_publishstate` (Draft 100000000, Published 100000001), optional, **no default** | Only Published versions are loaded by the resolver. Existing rows stay empty (dev data is recreated, O-13). **Field-secured (BFF-only writes).** |
| `sprk_basedonversion` | Lookup -> `sprk_policyversion` (delete **RemoveLink**) | For a record rule: the base rule's policy version it was copied from. Never Restrict (ontology F2-a). Relationship `sprk_policyversion_basedonversion_policyversion`. |
| `sprk_createdbyperson` | Lookup -> `systemuser` (delete RemoveLink) | Task 080 (D-173, ontology F4-1): the person who asked for the version; the BFF stamps it. Created unsecured; task 081 secures it. Relationship `sprk_systemuser_sprk_policyversion_createdbyperson`. |

The predecessor of a version is its sibling under the same `sprk_policy` with the next-lower `sprk_versionnumber`; there is no extra link column.

### Existing columns billing rows must still fill

`sprk_policyversion.sprk_proposedaction` (text 400) is **required** and means what the ontology rules propose a human do (free text, for example `send-budget-inquiry`); it is not the consequence. A billing version fills it with a short sentence (task 070 / 076 decide the text) and sets `sprk_consequence` separately. `sprk_policy.sprk_lane` (Decide / Do) is read by every Signals reader and must be a valid value on any enabled billing policy.

## Reader impact (for task 070)

The Signals readers of `sprk_policy` select **every enabled, active policy** regardless of subject or domain (`SignalReevaluationJob.ReadEnabledPoliciesAsync`, `PolicyScopeResolver.LoadEnabledPoliciesAsync`). An enabled billing policy would therefore be picked up by the nightly pass and the event-trigger pass; see `projects/ebilling-r1/notes/019-policy-store-ruleset.md` for the reader inventory and what 070 must do.

## Gotchas

- **Deleting or merging an organization fails while a Procedure or a Procedure Assignment names it** (task 080): `sprk_procedure.sprk_firm` and `sprk_procedureassignment.sprk_firm` are Restrict, because a nulled firm would widen a firm-only Procedure or override to every firm. Retire or re-point the Procedure first.
- **Parental limit (task 080, dev run 2026-10-21).** Dataverse counts Delete = Cascade as a *parental* relationship and a table can have only **one**. So `sprk_procedureversion.sprk_procedure` (the version's only parental relationship) and `sprk_procedureassignment.sprk_excludedpolicy` (the assignment's only parental relationship) are delete-only cascades (Delete Cascade; assign, share, unshare, reparent, merge and rollup view all NoCascade), and the assignment's three core-record lookups (`sprk_matter`, `sprk_project`, `sprk_workassignment`, one per `FilingCores` table) are **RemoveLink** with nothing cascading. **Deleting a core record therefore does not delete its assignment rows by itself**: the core-record delete planner (task 117) removes them first, with the 117 sweep as the backstop. A reassign or share of a parent never reaches these rows (the record owner resolver and the secure-child job own their owner and shares).

- The primary-name `MaxLength` and `RequiredLevel` in an entity-create body are ignored (850 / None); the script shortens `sprk_name` to 200 (only while the table has no rows) and sets it to ApplicationRequired right after creating the table (DEF-007).
- The status relabel is safe because the table is new and empty; the script refuses (exit 2) if value 1 or 2 carries any label other than the system or the target one.
- `sprk_domain` is also the name of an unrelated email-domain text column on `account` and `sprk_organization`. Dataverse names are per table, so there is no clash, but a query joining a rule set to an organization must alias the columns.
