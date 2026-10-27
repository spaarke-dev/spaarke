# 089: assignment and override service, secure-child registration, K-2

Branch `task/ebill-089-assignments`, based on `work/ebilling-r1` 78f7c390ff. Rigor FULL. The code and tests are done. The live steps are listed below and have **not been run**. Owner EB-D-158 pre-approved them on dev, and the main session runs them. No live write was made from this task.

## What was built

- **`Services/Finance/Procedures/ProcedureAssignmentService.cs` (new).** The service implements:
  - assign, pin and exclude a Procedure;
  - exclude a rule, optionally for one firm;
  - change a rule. This writes an Exclude row plus a record rule (set, policy, Published version and current-version pointer) in one `ExecuteTransactionAsync`. The set is created on first use and reused after that, with `sprk_scope` Record (100000002), or Firm record (100000003) with `sprk_firm`;
  - a removal preview and a removal. A changed rule is switched Off in the same transaction as the delete, and its open Signals close PolicyRetired through 081's `ClosePolicySignalsRetiredAsync(..., withBillingSubject: true)`.

  Every row is owned through `IRecordOwnershipResolver` with the caller as RequestedBy:
  - assignment rows and a new set: parents [core record];
  - a record rule in an existing set: parents [the set].
  
  I-A1: one row per (record, subject). This is enforced twice: a read, and a v5-derived row id, so two racing creates collide on the primary key and the second gets 409 `assignment.duplicate`.
- **`Api/Finance/ProcedureAssignmentEndpoints.cs` (new).** 15 literal routes: 5 per core (matters, projects, workassignments). Each route runs these checks in order, as the caller, failing closed:
  1. step 0 on the group;
  2. Write and AppendTo on the core record (`ebilling.record.override`, uniform 404);
  3. the subject and the role (403). That is Read on the Procedure or rule, plus the billing-admin marker. A change also needs Create on rule set, policy and policy version; a removal needs Write on policy;
  4. for an existing override, Read on what the **stored** row overrides (`RequireReadOnTheOverridesSubject`);
  5. the eBilling flag.
- **Registration in one change.** It touches these files:
  - `SecureChildLineage`: sprk_procedureassignment and sprk_ruleset under the 3 core lookups, and sprk_policy gains sprk_ruleset;
  - `RecordOwnershipResolver.OwnershipParentEntities`: both tables added, and `CitationLookups` gains the assignment's procedure, pinned version and excluded policy;
  - `config/secure-record-owner-role.json`: two child rows, with evidence `PENDING-LIVE-PROBE`;
  - `OWNERSHIP_CHILD_TABLES` in `bffChildWriteAdapter.ts`;
  - the census entries.
  
  `RecordCreatorPerson.StampedChildTables` and `Set-ChildRecordCreatorPersonSchema.ps1` already carried all four tables (task 090), so neither changed.
- **Census.**
  - `RuleSetServicePolicyWaiver` is rewritten and the waivers are now Permanent: RuleSetService creates only client, firm and Procedure sets.
  - The Routed entries for ProcedureAssignmentService are sprk_procedureassignment 2, sprk_ruleset 1, sprk_policy 1 and sprk_policyversion 1.
  - The new guard `RecordScopedRuleSetWritersAreRouted` has controls.
  - `RuleSetServiceNoMatterScopeGuardTests` is widened: no core lookup, record scope or record-scope literal may appear in RuleSetService.
- **NFR-05.** `SecureBuRoleDepthAssertion.GuardedChildPrivileges` is a separate, non-throwing list:
  - prvReadsprk_procedureassignment
  - prvReadsprk_ruleset
  - prvReadsprk_Policy
  - prvReadsprk_PolicyVersion
  - prvReadsprk_StoredFile

  The names were read live from the `privilege` table on spaarkedev1, 2026-10-27. Clause 1 grades reach on these privileges, and each unresolved name is one `GuardedChildPrivilegeNotResolved` finding. `GuardedPrivileges` and its pin are unchanged.
- **K-2.**
  - `BillingLookupsConferNoAccessTests` covers three things: the registry and appsettings, access code in five folders, and external modules. Each has controls.
  - `MembershipResolverServiceTests.ResolveAsync_AccessConferringOnly_BillingFirmAndBillingResponsible_ConferNothing` is the behavioural twin.
- **Other changes.**
  - `RuleSetService` hides record-scoped sets from the admin list and get. It exposes `ValidateCopiedRule`, `PolicyColumns` and `ToVersion` as internal helpers.
  - `InvoiceFamilyCoreRecordGuard.FilingCoreTables` is new.
  - `OperationAccessPolicy` gains `ebilling.record.override` (Write | AppendTo).
  - DI is registered in `FinanceModule`, and the routes are mapped in `EndpointMappingExtensions`.

## Prototype questions h-j (notes/084-prototype-questions.md)

- **h. Removal preview.** 089 owns it, at `GET /api/finance/{matters|projects|workassignments}/{recordId}/overrides/{assignmentId}/removal-preview`. It returns `{ id, kind, closesRule, warning, affectedText, visibleOpenInvoices }`.
  - For a changed rule, `kind` is "RuleChanged". `warning` is "Removing this override will close the rule it changed and remove pending decisions on active invoices.". `affectedText` is "N open invoices you can see are affected.". Both are counted exactly as for the Procedure retire: caller-readable open invoices with an open Signal of the record rule. On a rights fault `affectedText` and `visibleOpenInvoices` are null.
  - For any other override, `closesRule` is false and the warning and count are null.
  - It does **not** list the invoices or the findings. A per-invoice banner list belongs to 084's read model, if the UI needs one.
  
  `DELETE` on the same path (no `/removal-preview`) removes the override. It returns `{ id, kind, rulesSwitchedOff, closeComplete, affectedText, visibleOpenInvoices, message }`.
- **i. Change rule.** 089 does not expose a typed parameter schema per rule type (D-27 templates). The editor starts from the base rule's current parameters and sends the whole edited rule body as `parameters`. The server validates it with the same validator RuleSetService uses for a copied rule (`ValidateCopiedRule`, i.e. PolicyVersionValidator for the base rule's type). A failure is 400 `assignment.invalid`, with `errors[].field` naming the problem.
- **j. Request shapes.** These are all POSTs under `/api/finance/{matters|projects|workassignments}/{recordId}`, and each returns 201 with an `OverrideView`:
  - `procedure-assignments`: `{ procedureId, mode?: "FollowLatest"|"Pinned"|"Excluded", pinnedVersionId?, reason? }`. A pin names an Active or Superseded version of that Procedure.
  - `rule-overrides/exclude`: `{ policyId, reason, firmId? }`. A reason is required.
  - `rule-overrides/change`: `{ policyId, parameters: {<rule body>}, reason, firmId? }`. A firm-named base rule can be changed only for its own firm.
  
  `OverrideView` is `{ id, kind, mode, procedureId, pinnedVersionId, policyId, firmId, reason, recordRulePolicyId, recordRuleVersionId, recordRuleSetId, recordRuleCode }`. The DTOs are in `src/server/api/Sprk.Bff.Api/Services/Finance/Procedures/ProcedureAssignmentService.cs`. Send this path to spaarke-prototype. The main session sends it.

## Deviations and decisions

1. **The policy owner of a NEW set takes the core resolution** (POML: the policy's parents are [the set]). The resolver cannot read a parent created in the same transaction. That resolution is exactly what it would answer for a child of a row it owns. When the set already exists, the policy is resolved with parent (sprk_ruleset, set). The version always takes its policy's answer.
2. **The record-rule code is `BRR-` + the policy id (32 hex, upper case).** ontology-r1 has not yet given the D-13 key format, which is a POML escalation trigger. The format is isolated in `RecordScopedRuleSet.CodeFor`, and ontology-r1 must agree it before the PR.
3. **The set is created directly as statecode 0 / statuscode Published (659490000) with `sprk_visibletofirms` false.** Verify on the first live create that Dataverse accepts that statuscode on create.
4. **Delete behaviour.** The POML (uac Q10) says Restrict on the core lookups, but 080 built RemoveLink (the parental limit, uac-r2 ACCEPT 2026-10-21), and uac-r2's latest condition says RemoveLink. No schema change was made here.
5. **The RuleSetService waiver kind changed from Pending to Permanent.** This is for ontology-r1 review, with the widened scope guard and the census guard.
6. **Not evaluated until 082.** The resolver (BillingFactLoader) does not read record sets or Exclude rows yet, and `FirmClauseText` returns no clause for a record set. Both are by design for this task.
7. **Client package test not run.** `OWNERSHIP_CHILD_TABLES` is pinned by `SecureChildNewCommandAgreementTests`, and its seeding proof is below. The worktree has no `node_modules`, and no jest test names the list.
8. **A change needs a base rule in force** (review fix). The rule must be switched On, and its set must be a Procedure set (Draft by design, in force through its Procedure Version) or a Published client or firm set. Otherwise it is 409 `assignment.rule_not_in_force`, because a change would revive a switched-off or retired rule on the record. Excluding such a rule is harmless and stays allowed.
9. **`CoreAncestorResolver.ChildRecordEntities`.** Unchanged, per the coordinator. None of the new tables carries a core stamp in its sense.

## uac-r2 conditions 1-6

1. Every create goes through the resolver; no `ownerid` is written outside it. The census proves this with a seeding proof.
2. Assign, Share and Reparent are NoCascade, and delete is RemoveLink (080 schema). This task adds no plugin.
3. No column 089 writes decides who works on or sees a record. `sprk_firm` is a rule filter only (K-2 tests).
4. An override only restricts which rules check an invoice. It grants nothing.
5. The role JSON adds Read (Basic) on sprk_procedureassignment and sprk_ruleset to **Secure Record Owner only**, the role held by the memberless owner team. It adds nothing to External Collaborator, nothing to any role in a solution, and nothing on sprk_procedure or sprk_procedureversion (`ProcedureRolePrivilegeGuardTests` passes). **uac-r2 must confirm explicitly** that the Secure Record Owner row on sprk_procedureassignment meets its "no role privilege on the Procedure tables until 191" rule. The role reaches no person, but it is a role privilege on that table.
6. Both tables are in SecureChildLineage.Children, the role JSON, the census, Set-ChildRecordCreatorPersonSchema (since 090) and OWNERSHIP_CHILD_TABLES, all in this change. There is no follow-the-parent mechanism.

## Escalation (raise before the live NFR-05 run)

**Billing roles read the new guarded child tables beyond the Secure BU.** A read-only metadata read on dev, before compaction, found:
- **Spaarke Billing Administrator** holds Read at **Global** on sprk_ruleset, sprk_Policy and sprk_PolicyVersion (D-108: "Read stays Global").
- **Spaarke Ontology Administrator** holds **Deep** on Policy and PolicyVersion.

Now that these are guarded child privileges, the live NFR-05 run and the scheduled census will report `HumanPrincipalReachesSecureBusinessUnit` for every human who holds those roles. This is not just noise: Global Read lets those users read a Secure record's record-scoped rule set and record rules. This is the POML trigger "a non-admin role whose Read on a listed table reaches the Secure BU", so it needs a human decision. The options are:
- (A) lower those Reads to Deep in a sibling BU, or to Basic;
- (B) allow-list the billing and ontology admin roles for these privileges only, as an ADR-028 exception;
- (C) accept it and narrow the list.

Recommendation: A for sprk_ruleset (D-185 already makes Billing Administrator the only holder), decided jointly with ontology-r1 for Policy and PolicyVersion.

## Seeding proofs (each mutation applied alone, then restored)

| Mutation | Result |
|---|---|
| SecureChildLineage without sprk_ruleset | unit 6 failed; arch 2 failed |
| SecureChildLineage without sprk_procedureassignment | unit 5 failed; arch 2 failed |
| sprk_policy without its sprk_ruleset lookup | unit 3 failed |
| role JSON without sprk_procedureassignment | arch 2 failed |
| role JSON without sprk_ruleset | arch 2 failed |
| `OWNERSHIP_CHILD_TABLES` without sprk_ruleset | arch 1 failed |
| `OWNERSHIP_CHILD_TABLES` without sprk_procedureassignment | arch 1 failed |
| OwnershipParentEntities without sprk_ruleset | arch 1 failed; unit 3 failed |
| OwnershipParentEntities without sprk_procedureassignment | arch 1 failed; unit 1 failed |
| CitationLookups without the excluded policy | unit 1 failed |
| census ChildTables without both | arch 5 failed |
| ProcedureAssignmentService without `policyOwner.ApplyTo(policy)` | arch 2 failed (census guard); unit 1 failed |
| removal close with `withBillingSubject: false` | unit 1 failed |
| matter DELETE route without `RequireReadOnTheOverridesSubject` | unit 1 failed |
| `GuardedChildPrivileges` without prvReadsprk_ruleset | unit 3 failed |
| change without the in-force check | unit 1 failed |

## Tests

| Suite | Result |
|---|---|
| ArchTests, full | 1566 passed |
| Sprk.Bff.Api.Tests, broad filter plus touched classes (Finance, Billing, Signals, RuleSet, Invoice, Compliance, Rejection, Decision, AccessTable, ParentLineage, Ebilling, Procedure, SecureChild, Submission, MembershipResolverServiceTests, SecureBuRoleDepth, SecureRecordIsolationCensus, OperationAccessPolicy, RecordOwnership, UnifiedAccessControl) | 10123 passed, 7 skipped |
| Sprk.Bff.Api.IntegrationTests | 199 passed, 8 skipped |
| Spaarke.Core.Tests | 64 passed |

Spaarke.Scheduling.Tests builds. Of the new test classes:
- `ProcedureAssignmentServiceTests`: 20;
- `ProcedureAssignmentEndpointsTests`: 74 cases;
- `SecureChildRecordOverrideLineageTests`: 3, covering version → policy → ruleset → core, secure and unsecure, with citations that are not parents;
- `SecureBuRoleDepthAssertionTests`: 3 new facts and theories;
- `BillingLookupsConferNoAccessTests`: 4.

Fixture updates:
- `SecureRecordIsolationCensusJobTests` now registers the guarded child privileges in its fake directory.
- `Build_WhenACodifiedPrivilegeIsNotInTheEnvironment...` passes them as resolved.

## BFF hygiene

- **Publish size** (Release, framework-dependent linux-x64, zip):
  - master de38296499: 39,623,980 bytes;
  - task base 78f7c390ff: 40,483,864 bytes;
  - this task: 40,519,313 bytes.
  
  That is **+35,449 bytes (+0.03 MB) for 089**, and +0.85 MB against master for the whole work branch. Both are below every threshold.
- **Packages and CVEs.** No package was added. `dotnet list package --vulnerable`: none.
- **Placement.** Concrete scoped service in `AddFinanceModule`, with endpoints in the Finance group.
- **/conflict-check.** No open PR overlaps except the project's own PR #1911 (`config/secure-record-owner-role.json`). Master has changed 7 of these files since the work branch's merge base 58ffc37da3: the role JSON, SecureBuRoleDepthAssertion.cs, SecureChildLineage.cs, the census test, the two NFR-05 test files and SecureChildLineageTests. These are uac-r2 merges. The next master merge into work/ebilling-r1 must reconcile them.

## Live steps (main session; owner EB-D-158)

Run them in this order on spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`). The Secure Record Owners team is 6eabc7f9-13be-f111-a05b-0022482913fc.

1. **Refusal probe for sprk_procedureassignment, before -Apply.** `POST /api/data/v9.2/sprk_procedureassignments` with:

   ```json
   { "sprk_name": "089 probe", "sprk_mode": 100000002, "ownerid@odata.bind": "/teams(6eabc7f9-13be-f111-a05b-0022482913fc)" }
   ```

   Expect HTTP 403 with code 0x80040299, "Read Privilege Check For Owner failed ... is missing prvReadsprk_procedureassignment privilege", and **no row created**. Quote the response verbatim into the `evidence` field of the role JSON row. If the create **succeeds**: delete the row, record it and STOP (POML escalation).
2. **Refusal probe for sprk_ruleset.** `POST /api/data/v9.2/sprk_rulesets` with:

   ```json
   { "sprk_name": "089 probe", "ownerid@odata.bind": "/teams(6eabc7f9-13be-f111-a05b-0022482913fc)" }
   ```

   Expect 0x80040299, missing prvReadsprk_ruleset. Record it the same way.
3. **The role.**
   - Dry run: `pwsh scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com`. Expect exactly two additions: Read (Basic) on prvReadsprk_procedureassignment and prvReadsprk_ruleset.
   - Then run it with `-Apply`, then with `-Verify`. Expect exit 0, and the 38 tables at Basic.
4. **Creator person.** `pwsh scripts/Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify`. Expect 0; it was already live from 090.
5. **Re-run both probes (optional).** They should now succeed. Delete the probe rows straight away.
6. **Live NFR-05.**
   - Set `SPAARKE_NFR05_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com` and `SPAARKE_NFR05_REQUIRED=1`.
   - Run `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SecureBuRoleDepthAssertionTests"`.
   - **Expected given the escalation above:** findings for the Billing Administrator (Global) and Ontology Administrator (Deep) holders on the new guarded child privileges. Decide the escalation before relying on this run.
7. Write the probe text, the -Apply / -Verify output and the NFR-05 result here and in the role JSON evidence. Then commit.

## Reviews

- **Adversarial review.** The two `adversarial-reviewer` agents could not be launched: the session's concurrent sub-agent limit was reached and the tool said not to retry. I reviewed the change myself instead, in two passes: authorization and tenant isolation, then correctness and ADRs.
  - **Fix-now, fixed:** F2, change of a rule that is not in force (deviation 8).
  - **Known limits:**
    - K1: the 201 Location points to a path with no GET route. The read is 084's.
    - K2: the preview or removal throws (500) if a record set holds more than one page of rules.
    - K3: the census logs an unresolved secure-child privilege name as Critical, the default for a new verdict.
    - K4: excluding a firm-named base rule "for another firm" is accepted but restricts nothing.
  - **The main session should run one independent adversarial pass on this branch before the PR (two are required for auth / tenant-isolation work).**
- **ADR check.** Every point complies:
  - ADR-001: Minimal API.
  - ADR-008: endpoint filters.
  - ADR-010: concrete scoped service, no new interface.
  - ADR-019: ProblemDetails with a stable `errorCode`.
  - ADR-002: no plugin.
  - ADR-038: maintain-class tests with seeding proofs.
  - ADR-028: every route is authorized as the caller and fails closed.
- **uac-r2 and ontology-r1 reviews.** The main session sends both requests.
