# The ontology writer's identity and field-mapping FR-21: keep it

> **Date**: 2026-11-02 · **Asked by**: field-mapping-server-write-path-r1 (task 033, FR-21, "system creators"), at the owner's request.
> **Question**: should the Signal, Decision Record and budget-revision writers keep their own managed identity
> (`mi-ontology-writer-dev`), or create through `RecordCreationService`'s system principal (the shared BFF identity, `mi-bff-api-dev`)?
> **Answer**: keep the dedicated identity. Record a named FR-21 exception: these writers stay off `RecordCreationService`.
> Evidence: code on `origin/master`, live dev reads, and this project's decisions. File:line facts below.

## 1. Why the identity exists

It closes the task 002 escalation (`notes/002-escalation-append-only-writer-principal.md`, 2026-10-03):

- The project's §0 claim is that **the Decision Record is append-only**, enforced **by privilege**. ADR-002 forbids the plugin that would otherwise enforce it.
- The Create-only role `Spaarke Ontology Service` was first assigned to `SDAP-BFF-SPE-API`, which is also **System Administrator**. Dataverse privileges are additive, with no deny. So the writer held Write and Delete on the ledger, and the Create-only role constrained nothing.
- The same holds for `# mi-bff-api-dev` and `# spaarke-bff-api-prod`: both are System Administrator. On 2026-11-02 the dev BFF identity held only System Administrator.

The owner chose **option A** on 2026-10-03/04: a dedicated, least-privileged application user. That is decision D-1 ("ADR-028 path A"), `decisions.md:33`; plan in `notes/006-writer-identity-plan.md`; shape amended 2026-10-07. Its reasons:

- **Least privilege and append-only (primary).** The sentence "the writer cannot update a Decision Record" is true only if the writing principal lacks `prvWritesprk_DecisionRecord`. Under a System Administrator identity it is a code-discipline promise that degrades silently.
- **Attribution.** `createdby` on every Signal and Decision Record names the writer, never a person and never the generic BFF. Section 3 explains why code now depends on this.
- **Blast radius.** A defect in a writer cannot touch any table the role does not grant.
- **Not key rotation.** There is no secret: it is an app-only managed-identity token, with no client secret, app registration or Key Vault secret. ADR-028 E-1 to E-3 (the KV credential lifecycle) are not engaged.

Spec §ADR Tensions records the ADR-028 A4 exception (path A, owner-approved 2026-10-04). A4's single shared provider governs confidential-client credentials; this writer builds none.

**Precedent**: the provisioning control plane uses the same pattern. It runs as its own UAMI application user (`# sprk-controlplane-dev-uami`) holding a narrow role, not as the BFF (`CustomerRunGuardOptions.cs:84`).

## 2. What each identity can do (live dev, 2026-11-02)

| | `# mi-ontology-writer-dev` (`3121bf1b-…`, root BU) | `# mi-bff-api-dev` (`8793f4b0-…`) |
|---|---|---|
| Roles | `Spaarke Ontology Service` only | `System Administrator` only |
| `sprk_signal` | Create, Read, Write, Append, AppendTo, Assign | everything |
| `sprk_decisionrecord` | Create, Read, Append, AppendTo, Assign. **No Write, no Delete** | everything, including Write and Delete |
| `sprk_budgetrevision` | Create, Read, Append, Assign. No Write | everything |
| Other tables | Read only (matter, project, budget, policy, communication, …); AppendTo where a lookup needs it. **No Write on any business table** | everything |
| Field security | Members of "Spaarke Ontology Writer Field Writers" (D-136; `sprk_createdbyperson` on the three tables only) and the ebilling "Firm Reason Writers" | "Spaarke BFF-Managed Field Writers" (≈35 tables, including `sprk_issecure` and `sprk_accessinheritance`) |

The live privileges are read from `roleprivileges` on role `b1fb7ee0-bfbe-f111-aaaf-0022482913fc`, at Global depth. Source: `SpaarkeMaster/Roles/Spaarke Ontology Service.xml`. Pinned by `scripts/provisioning/role-privilege-expectations/spaarkemaster-1.2.4.0.json:56-57` (`"absent": ["prvWritesprk_DecisionRecord"]`).

**Can the shared identity write Signals and Decision Records today?** Yes. System Administrator holds every privilege, which is exactly why it must not be the writer: it can also update and delete the ledger. On dev, 280/280 `sprk_signal` and 51/51 `sprk_decisionrecord` rows are `createdby = mi-ontology-writer-dev`, so the shared identity writes none of them today.

## 3. What depends on the identity

**Code**: `OntologyWriterCredentialFactory` → `OntologyWriterDataverseClient` (DI `SignalsModule.cs:51`, a singleton built lazily).

- **Writes through it:**
  - `SignalWriter`: create, close, refresh, re-own (`sprk_signal`).
  - `SignalSuppressionService`: `sprk_signal` updates.
  - `DecisionRecordWriter`: create, assign (`sprk_decisionrecord`).
  - `GateDecisionRecordSharing`.
  - `DecisionActionExecutors:455`: budget revision create.
- The shared sysadmin `IGenericEntityService` is used for **reads only** in this area.
- **No fallback:** an empty `Ontology:Writer:ManagedIdentityClientId`, or an empty tenant, throws (`OntologyWriterCredentialFactory.cs:107`). The app still starts, and the first write is refused.

**Correctness keyed on `createdby = writer`** (not just attribution):

- `DecisionRecordWriter.cs:733`, `:1017`, `:1662`: the "already recorded" (idempotency) lookups match only rows the writer created. Task 153 (adversarial pass 2, F1): a Console user with Deep create could otherwise create a Decision Record carrying the same key, and the gate's own record would never be written. If the shared identity wrote Decision Records, this filter would match rows that any other BFF path or flow running as that identity created.
- `DecisionCommitService.cs:1868`, `SignalReadService.cs:1181`, `SignalReevaluationJob.cs:954`, `GateDecisionRecordSharing.cs:518`: the same writer-user-id condition.
- The writer's own user id comes from WhoAmI on its connection (`OntologyWriterDataverseClient.GetWriterUserIdAsync`, `:224`).

**Guards and telemetry**:

- ArchTests:
  - `SpaarkeRoleLedgerPrivilegeGuardTests`: only Ontology Service may Create a Decision Record, and no role may Write or Delete one.
  - `OntologyWriterAppendToGuardTests`.
  - `RecordOwnerAssignmentCensusTests:950-980` (pins `OntologyWriterDataverseClient.AssignAsync` callers).
- Script: `Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1`.
- Meter and alert: `ontology.writer.*` and `alerts.bicep:283-309` (`ontology-writer-failures`).

**Field security**: `Set-ChildRecordCreatorPersonSchema.ps1` and `Set-EbillingFirmReasonFieldSecurity.ps1` both require `-OntologyWriterApplicationId`.

**Provisioning: a gap, now filed.**

- Nothing in `infrastructure/**`, `.github/**`, `scripts/provisioning/**` or the control plane creates the UAMI, its App Service attachment, its application user, its role assignment or the config key.
- Dev was set up by hand (task 006). A new customer environment gets no writer, so Signal and Decision Record writes fail closed there.
- Filed as **DI-138, #2518**. It sits with #1808, the D-136 profile membership.
- Moving to the shared identity would remove this provisioning step, but section 4 shows the price.

## 4. Cost of moving versus keeping

**Moving the writers to `RecordCreationService`'s system principal (`mi-bff-api-dev`) would:**

1. **Break the append-only guarantee.** The writer would hold Write and Delete on `sprk_decisionrecord` through System Administrator. The §0 claim would fall back to "by code", the outcome the owner rejected in task 002. Avoiding that would need option B: removing System Administrator from the BFF identities, a portfolio-wide change.
2. **Weaken idempotency and gate correctness.** `createdby = writer` would no longer identify "a row the ledger writer made". Every lookup in section 3 would need a different discriminator, and none of today's columns is server-only for every path.
3. **Widen the blast radius.** The ledger, Signal and budget-revision writes would run with the power to change every table and field-secured column, including `sprk_issecure` and `sprk_accessinheritance` via the BFF-Managed Field Writers profile.
4. **Rework tests, scripts and the alert,** which all name the writer identity: about 5 ArchTests, 3 scripts, the field-security profiles and the alert.
5. **Bring no functional gain.** The ontology position from 2026-10 stands: nothing is field-mapped into Signal or Decision Record rows. These writers build their rows from the decision, not from a mapped payload, so `RecordCreationService`'s mapping step has nothing to do for them.

**Keeping it costs:**

- One more identity per environment to provision (DI-138, #2518).
- An exception in FR-21.
- A second "product principal" for your task 050's created-outside-the-product marker, which you already plan to include.

## 5. Recommendation

**Keep the dedicated identity. Record a named FR-21 exception:** "the Signal, Decision Record and budget-revision writers (`SignalWriter`, `SignalSuppressionService`, `DecisionRecordWriter`, `GateDecisionRecordSharing`, `DecisionActionExecutors`' revise-budget step) create through `OntologyWriterDataverseClient` as the ontology writer identity, not through `RecordCreationService`."

- **Do not teach `RecordCreationService` a second principal for these writers.** No mapping applies to them (section 4, point 5). A second principal inside the service would move the ledger's least-privilege boundary into shared code, where a change to the service could route other creates through the writer's identity, or the writer's creates through the sysadmin client. The writer's client is a separate, concrete, fail-closed class precisely so that cannot happen (`design.md` §2, F7/F13/F14).
- **For task 050's marker:** treat both `mi-bff-api-*` and the ontology writer's application user as product principals. Resolve the writer's id the way the ontology code does (WhoAmI on its connection, or the configured client id → `systemuser.applicationid`), not from a hard-coded dev GUID.
- **Provisioning:** DI-138 (#2518) makes every customer environment create the identity. Until then, Signals and Decision Records do not write outside dev, by design.

Revisit this only if System Administrator is ever removed from the BFF identities (task 002's option B). Then the shared identity could be least-privileged, and the question becomes a real trade-off.
