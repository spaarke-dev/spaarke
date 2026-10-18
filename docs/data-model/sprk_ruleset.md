# sprk_ruleset and the billing columns on the policy store

> **Project**: ebilling-r1, task 019 (FR-09, FR-27, FR-28, D-26, D-66, D-79)
> **Last Updated**: 2026-10-18
> **Scripts**: `scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1` and, for `sprk_rejectionreason` (task 077, D-113), `scripts/ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1` (both idempotent, additive; a second run changes nothing)
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
| `sprk_scope` | Choice (local) | Client default = 100000000, Firm = 100000001. |
| `sprk_firm` | Lookup -> `sprk_organization` (delete RemoveLink) | Set only when the scope is Firm. **Enforced by task 076, not by schema.** Relationship `sprk_organization_ruleset_firm`. |
| `sprk_visibletofirms` | Two-option, default **No** | See "Firm visibility" below. |
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

## sprk_policyversion (additive)

Existing columns are unchanged. Added; clause and consequence sit on the version because a published version is immutable.

| Logical name | Type | Notes |
|---|---|---|
| `sprk_clausereference` | Multiline text (4,000) | The verbatim clause quote from the source guideline (D-66, FR-38). |
| `sprk_clauselocation` | Text (200) | Section and page of the clause. |
| `sprk_consequence` | Choice (local) | Reject submission = 100000000, Flag = 100000001, Suggest adjustment = 100000002. Empty on ontology rows. A rejection also needs the confidence threshold and a reject-eligible invoice type (FR-26, I-5). |
| `sprk_proposalstatus` | Choice (local) | Proposed = 100000000, Accepted = 100000001, Rejected = 100000002. State of a rule drafted by the Action (FR-27, FR-28). Empty on ontology rows. |
| `sprk_rejectionreason` | Multiline text (2,000) | Why an admin rejected the draft (task 077, D-113). Set only by a reject, in the same conditional write as `sprk_proposalstatus = Rejected`; cleared by an accept and by an edit that returns the draft to Proposed. Required (1 to 2,000 characters after trimming) on the reject route, enforced by the BFF, not by schema. **Admin-only**: returned by the Rule Review read, never by a firm projection (`GetFirmClauseAsync` selects its columns by name). Empty on every version that was never rejected. Created by `Deploy-EbillingRuleRejectionReasonSchema.ps1` (task 079 runs it on dev; task 053 syncs SpaarkeMaster). |

The predecessor of a version is its sibling under the same `sprk_policy` with the next-lower `sprk_versionnumber`; there is no extra link column.

### Existing columns billing rows must still fill

`sprk_policyversion.sprk_proposedaction` (text 400) is **required** and means what the ontology rules propose a human do (free text, for example `send-budget-inquiry`); it is not the consequence. A billing version fills it with a short sentence (task 070 / 076 decide the text) and sets `sprk_consequence` separately. `sprk_policy.sprk_lane` (Decide / Do) is read by every Signals reader and must be a valid value on any enabled billing policy.

## Reader impact (for task 070)

The Signals readers of `sprk_policy` select **every enabled, active policy** regardless of subject or domain (`SignalReevaluationJob.ReadEnabledPoliciesAsync`, `PolicyScopeResolver.LoadEnabledPoliciesAsync`). An enabled billing policy would therefore be picked up by the nightly pass and the event-trigger pass; see `projects/ebilling-r1/notes/019-policy-store-ruleset.md` for the reader inventory and what 070 must do.

## Gotchas

- The primary-name `MaxLength` and `RequiredLevel` in an entity-create body are ignored (850 / None); the script shortens `sprk_name` to 200 (only while the table has no rows) and sets it to ApplicationRequired right after creating the table (DEF-007).
- The status relabel is safe because the table is new and empty; the script refuses (exit 2) if value 1 or 2 carries any label other than the system or the target one.
- `sprk_domain` is also the name of an unrelated email-domain text column on `account` and `sprk_organization`. Dataverse names are per table, so there is no clash, but a query joining a rule set to an organization must alias the columns.
