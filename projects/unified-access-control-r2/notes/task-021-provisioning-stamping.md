# Task 021 — provisioning stamping PATCH (C4/C5)

> **STATUS: ESCALATED — and RE-SCOPED, 2026-08-24.** Two escalations, and the second one supersedes
> the first. Owner review of the mechanism (not the bug) found that **two of the three things
> provisioning stamps should probably not exist at all**, and that fixing the names as written would
> *activate* a data-corruption bug rather than close a gap.

---

## 🛑 STOP — do NOT "just fix the names"

Repairing this PATCH as specified would turn a dormant defect into a live one. The write has been
failing since 2026-03, and **that failure is the only reason project client data has not been
corrupted.** Details in "Second escalation" below. Task 021 must be re-scoped before any code change.

---

## What the task said, and how live metadata changed it

The POML and the code's own `KNOWN BROKEN` comment both frame this as "**three** wrong property names,
all needing `@odata.bind` navigation properties from `$metadata`". Live `sprk_project` metadata
(Dataverse MCP `describe`, 2026-08-24) says otherwise — **only two are lookups**:

| Code writes today | Real column (live metadata) | Type | Needs a nav property? |
|---|---|---|---|
| `sprk_securitybuid@odata.bind` | `sprk_securitybu` | **LOOKUP** → `businessunit` | ✅ yes — **blocked** |
| `sprk_specontainerid` | `sprk_containerid` | **NVARCHAR(100)** | ❌ **no** — plain string write |
| `sprk_externalaccountid@odata.bind` | `sprk_externalaccount` | **LOOKUP** → `account` | ✅ yes — **blocked** |

So one third of the task is trivially certain and needs no metadata at all. That is worth knowing, but
it does **not** unblock the task — see "why nothing shipped" below.

## 🚨 ROOT CAUSE FOUND: a stale schema doc, not a typo

[`src/solutions/SpaarkeCore/entities/sprk_project/secure-project-fields-schema.md`](../../../src/solutions/SpaarkeCore/entities/sprk_project/secure-project-fields-schema.md)
documents these columns authoritatively — and **every name in it is wrong**:

| Doc says (lines 15-16, 45-72) | Live metadata |
|---|---|
| `sprk_securitybuid`, relationship `sprk_project_sprk_securitybuid_businessunit` | `sprk_securitybu` |
| `sprk_externalaccountid`, relationship `sprk_project_sprk_externalaccountid_account` | `sprk_externalaccount` |

The code did not drift. **It was implemented faithfully from a document that had never been reconciled
with the deployed table**, including the `pac data export` example on line 117 which would also fail.
That reframes C4/C5: not a careless typo but a doc-to-code propagation failure, which is why it
survived five months and why fixing only the code leaves the next author to make the same mistake from
the same file.

**Seventh instance of "schema docs lose to live metadata" in this project.** The first six were
incidental; this one *caused* a Critical finding. Filed as a constraint on **task 026**.

## 🔔 ESCALATION — the two navigation-property names cannot be recovered

`@odata.bind` requires the **case-sensitive PascalCase navigation property**, which is not derivable
from the logical name. Every available source was tried:

| Source | Result |
|---|---|
| Dataverse MCP `describe('tables/sprk_project')` | Logical names only — no schema names, no nav properties |
| Dataverse MCP `read_query` | SQL over **data**; cannot reach `EntityDefinitions` / `RelationshipDefinitions` metadata |
| Repo solution files | Only `secure-project-fields-schema.md`, which is stale and names columns that do not exist |
| A deployed secure project to read back | None exists in dev (recorded when task 009's sibling fix deferred this) |

Repo-verified examples of the convention are PascalCase — `sprk_BusinessUnit`, `sprk_Contact`,
`sprk_Organization`, `sprk_GrantedBy` — but that establishes a *pattern*, not these two names.
`sprk_securitybu` could be `sprk_SecurityBU`, `sprk_SecurityBu`, or something else entirely, and
**a wrong nav property is silently accepted as an unknown property and the write does not happen** —
the exact failure class this task exists to close. Guessing would re-commit the original sin.

### Why nothing was shipped, including the certain part

`sprk_containerid` is verified and could be corrected right now. But the three writes go out in **one
PATCH**: if either lookup bind is still wrong, Dataverse rejects the whole request, so fixing the
container alone changes no behaviour. And the POML's own ordering constraint is explicit — fixing the
swallow alone would **hard-block provisioning** on names known to be wrong. Half of this task is worse
than none of it.

### Options for the owner

| # | Option | Cost / effect |
|---|---|---|
| **A** | Read `$metadata` from any environment with the solution deployed and supply the two names — e.g. `GET {org}/api/data/v9.2/$metadata` and search for the `sprk_project` `NavigationProperty` entries targeting `businessunit` and `account`; or `EntityDefinitions(LogicalName='sprk_project')/Attributes` → `SchemaName` | One lookup, unblocks immediately. **Recommended** |
| **B** | Deploy/identify a secure project in dev and read the value back | Slower; also gives a live regression target for task 034 |
| **C** | Switch this stamping write from `DataverseWebApiClient` to the SDK path (`Entity` + `EntityReference`), which addresses lookups by **logical** name and needs no navigation property at all | Removes the entire hazard class permanently, but changes this endpoint's data-access shape — an architectural decision beyond what 021 scoped, and `ProvisionProjectEndpoint` uses the Web API client throughout |

Option **C** is worth genuine consideration rather than a footnote: it is the only one that stops this
recurring. Every `@odata.bind` in the codebase carries the same silent-failure risk, and this project
has now hit it twice (021 here, and the same caution deferred the fix in task 009's sibling).

---

# Second escalation (2026-08-24) — the mechanism contradicts design §5.1

Raised by the owner: *"we discussed that we would have one Secure Project business unit, not a
business unit for every secure project"* and *"the firm already has a record — we use
`sprk_organization`"*. Both challenges are correct, and checking them found a third problem neither
of us had in view.

## (a) BU-per-project directly contradicts the design

[`design.md` §5.1](../design.md) — operator decision 2026-08-21, **five months after this code shipped**:

> - One **`Secure Projects` business unit**; secure records live there.
> - A **service account in that BU owns the records**, so they stay there naturally — no
>   matrix-data-access dependency, **no BU-per-project proliferation**.
> - **All human access is by explicit Dataverse share**, including the creating attorney's.

The phrase "no BU-per-project proliferation" is a verbatim rejection of what the code does. The
topology diagram lists `(future per-project secure BUs)` under `Secure Projects` — explicitly
parenthesised as a *future* option, not the current model.

The code (`ProvisionProjectEndpoint` Step 2) creates a **new BU per project**, named `SP-{ProjectRef}`,
described as *"Secure Project isolation BU for project: {projectName}"*.

**And it parents that BU to the ROOT business unit** (`ResolveRootBusinessUnitIdAsync` →
`parentbusinessunitid@odata.bind`). Not to `Secure Projects`. That has a sharp consequence:

> **NFR-05's standing assertion — "no security role may reach the `Secure Projects` BU" — would not
> cover these BUs at all**, because they are not underneath `Secure Projects`. The guardrail this
> project is building to protect secure records would silently not protect the ones provisioning
> creates.

## (b) The account is not just redundant — the column means something else entirely

`sprk_externalaccount` on `sprk_project` (and `sprk_matter`) has a **real, live consumer**, and it is
not external access:

```
ProjectLiveFactResolver.cs:33  ·  MatterLiveFactResolver.cs:35
    <item><c>client</c> → <c>sprk_externalaccount</c> (LOOKUP → account)</item>
```

**That column is the CLIENT** — the customer the project is for. The Insights engine reads it to
answer "who is the client on this matter?".

Provisioning creates a synthetic account named `External Access — {projectName}` and, in the broken
PATCH, stamps it over that column. **If the names were simply corrected, provisioning a secure project
would overwrite the project's client with a junk record.** The five-month failure is the only thing
that has prevented it.

The owner's point stands independently and is confirmed by live metadata — Spaarke already models
firms as `sprk_organization`:

| Concept | Column on `sprk_project` | Target table |
|---|---|---|
| Law firm | `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` | **`sprk_organization`** |
| Client | `sprk_externalaccount` | `account` |
| External grants | `_sprk_organization_value` on the grant row | **`sprk_organization`** |

`ExternalGrantLifecycle` and `ExternalParticipationService` — the actual external-access model — use
`sprk_organization`. Nothing in the external-access plane reads an `account`. So the provisioning
account is a **fourth concept** invented per project, consumed by nothing, written onto a column that
means something else.

## What this means for task 021

The task as written ("fix 3 wrong names + the swallow") is **repairing a mechanism the design
superseded**. Re-scope required:

| Step | Current behaviour | Assessment |
|---|---|---|
| Create BU per project | new BU under **root** | ❌ contradicts §5.1; also escapes NFR-05's guard. Design says use the ONE `Secure Projects` BU + service-account owner |
| Create SPE container | per-project container → `sprk_containerid` | ✅ **legitimate, keep**. Column verified `NVARCHAR(100)`, in active use on real rows |
| Create external account | synthetic `account`, stamped on `sprk_externalaccount` | ❌ redundant (firms are `sprk_organization`) **and destructive** (that column is the client) |
| Swallow the failure | catch + `LogWarning` + return 200 | ❌ fix regardless — this is what let all of the above stay invisible |

**Recommended re-scope**: the only stamp that survives review is the container. The BU and account
steps need an owner decision before any repair, because "fix the names" on either one makes things
worse. The fail-loud change is safe and valuable on its own **once the payload is reduced to fields
that should actually be written**.

## Why the original nav-property blocker now barely matters

`sprk_containerid` is a plain `NVARCHAR(100)` — **no navigation property needed**. If the BU and
account stamps are dropped per the above, the two blocked lookup names are no longer required at all,
and the first escalation dissolves. That is the cheapest path to a correct fix, and it is a
consequence of the design question, not a workaround for it.

---

## What is ready to land the moment the re-scope is decided

1. `sprk_specontainerid` → `sprk_containerid` (plain string; no bind) — **verified**.
2. Both lookup binds, once named.
3. Replace the `catch` + `LogWarning` with a fail-loud path carrying a reason code **and the ids that
   WERE created**, so an operator can reconcile the orphaned BU / container / account.
4. Tests + perturbations: revert each name individually; re-swallow the error.
5. Port task 016's `$select`-validating fake so a wrong projection cannot pass — already noted as the
   reason 5 of 5 provisioning tests stayed green while the endpoint 500'd.
