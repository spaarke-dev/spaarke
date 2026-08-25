# Task 021 — provisioning stamping PATCH (C4/C5)

> **STATUS: ESCALATED at step 0, 2026-08-24.** The mandatory first step could not be completed with
> the tooling available, and the POML forbids guessing. Verification work IS done and is below.

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

## What is ready to land the moment a name arrives

1. `sprk_specontainerid` → `sprk_containerid` (plain string; no bind) — **verified**.
2. Both lookup binds, once named.
3. Replace the `catch` + `LogWarning` with a fail-loud path carrying a reason code **and the ids that
   WERE created**, so an operator can reconcile the orphaned BU / container / account.
4. Tests + perturbations: revert each name individually; re-swallow the error.
5. Port task 016's `$select`-validating fake so a wrong projection cannot pass — already noted as the
   reason 5 of 5 provisioning tests stayed green while the endpoint 500'd.
