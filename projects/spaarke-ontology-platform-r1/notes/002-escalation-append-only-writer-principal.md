# Task 002 escalation - the append-only guarantee does not bind the writer

> **Date**: 2026-10-03 · **Raised by**: task 002 step 4 (union check) · **Status**: OPEN, owner decision needed
> **Trigger**: the task 002 POML `<escalation><trigger>` - *"If any HUMAN user or any Spaarke application
> identity holds Write or Delete on sprk_decisionrecord, STOP and escalate."* It fired.

## The finding

`Spaarke Ontology Service` - the Create-only role built so the writer physically cannot update a Decision
Record - is assigned to exactly **one** principal: **`SDAP-BFF-SPE-API`**. That same principal is a member
of **`System Administrator`**, which holds `prvWritesprk_DecisionRecord` and `prvDeletesprk_DecisionRecord`.

Dataverse privileges are **additive across roles, effective depth is the maximum, and there is no deny**.
So the union for `SDAP-BFF-SPE-API` includes Write and Delete on the ledger. The Create-only role
constrains nothing for the principal it was created for.

Two other Spaarke BFF identities, `# mi-bff-api-dev` and `# spaarke-bff-api-prod`, are also
`System Administrator`.

## Why this is not a nitpick

The project's §0 claim is that the Decision Record is **append-only**, and §0.3 binds every capability to
**test what its message claims**. "Append-only" is a statement about what the system *cannot* do. After this
check the accurate statement is narrower:

- **Holds**: no human in `Spaarke Console User` or `Spaarke Ontology Administrator` can update or delete a
  Decision Record. Verified by role privilege enumeration across all 6 BU copies.
- **Does not hold**: the **writer** is unconstrained. The only thing stopping an update is that the code
  does not issue one - and ADR-002 forbids the plugin that would otherwise enforce it server-side.

A code-discipline guarantee is a real control, but it is not the one the message claims, and it degrades
silently: any future task that adds an update call to the writer will succeed.

## Options

| | Option | Cost | Risk |
|---|---|---|---|
| **A** | **Dedicated least-privileged application user for the evaluator.** Register a new Dataverse application user holding `Spaarke Ontology Service` and nothing else; the evaluator authenticates as it rather than as the BFF's sysadmin identity. | New app registration + Dataverse app user + a credential path for the evaluator. Scoped to this project. | The BFF would hold two Dataverse identities; need to be sure the scheduled job can use the narrow one. **This is what the Create-only role was built for.** |
| **B** | **Remove `System Administrator` from the BFF identities and grant least privilege instead.** | Large. Those identities serve every Spaarke surface; least-privilege sets would have to be derived per table across all projects. | High blast radius - would affect every active project. Not this project's call. |
| **C** | **Accept and restate the claim.** Keep one identity; document that append-only binds humans by privilege and the writer by code, and add a test that fails if the writer ever issues an update, plus Dataverse auditing on the table as the detection control. | Small - one test and a documentation correction. | The §0 claim must be reworded. The control becomes detective rather than preventive. |

**Recommendation: A, with C's test as a backstop regardless of which is chosen.** A is the only option that
makes the sentence *"the writer cannot update a Decision Record"* true, and the role it needs already exists
and is already verified to carry exactly the three privileges the evaluator requires
(`prvCreatesprk_Signal`, `prvCreatesprk_DecisionRecord`, `prvAssignsprk_Signal`). C alone leaves a
preventive claim resting on a detective control. B is correct in the abstract and wrong to attempt here -
its blast radius is every active project, which is a portfolio decision, not a task-002 decision.

## Blocks

- **Task 002** cannot be marked complete: criterion 3 is satisfied only under the classification
  *"platform or admin role"*, and `SDAP-BFF-SPE-API` is neither - it is a Spaarke application identity.
- **Task 041** (append-only + relationship direction) tests this guarantee as a real non-admin. Whatever is
  decided here defines what 041 is allowed to assert.
- Tasks **030 / 031 / 040** (Signal writer, scheduled evaluator, Decision Record writer) choose the
  principal at implementation time. If option A is chosen it should be chosen **before** 030, not retrofitted.

## Not blocked by this

Criteria 1 and 2 passed as designed, and `prvAssignsprk_Signal` is confirmed present on
`Spaarke Ontology Service`, so **task 030's owner-from-matter requirement needs no privilege change**
regardless of how this escalation resolves. Full evidence: `notes/security-roles.md` section 8.
