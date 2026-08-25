# Task 021 — provisioning stamping PATCH (C4/C5)

> **STATUS: ✅ COMPLETE, 2026-08-25.** Implemented under the re-scope. See
> [§ What shipped](#-what-shipped-2026-08-25) at the bottom for the delivered behaviour, the
> perturbation counts, and the two findings the work turned up. The escalation history below is kept
> because it is *why* the shipped scope is what it is.
>
> **Previously: ESCALATED — and RE-SCOPED, 2026-08-24.** Two escalations, and the second one
> supersedes the first. Owner review of the mechanism (not the bug) found that **two of the three
> things provisioning stamps should probably not exist at all**, and that fixing the names as written
> would *activate* a data-corruption bug rather than close a gap.

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

---

# ✅ What shipped (2026-08-25)

## Delivered behaviour

`POST /api/v1/external-access/provision-project` now does exactly four things, in this order:

| # | Step | Failure mode |
|---|---|---|
| 1 | Resolve the canonical `Secure Project` BU **by name** from `SecureProject:BusinessUnitName`, `$top=2` | absent → `sdap.provision.secure_bu_not_found`; >1 → `…secure_bu_ambiguous`. **Never** falls back to root or the caller's BU |
| 2 | Resolve that BU's **default owner team** by `_businessunitid_value + isdefault + teamtype=0` | absent → `…secure_owner_team_not_found`; >1 → `…secure_owner_team_ambiguous` |
| 3 | Assign `ownerid` → that team, then **read `_owningteam_value` back** | refused → `…owner_assignment_failed`; accepted-but-not-applied → `…owner_assignment_not_applied` |
| 4 | Create the project's own SPE container, then record it on `sprk_containerid` | cannot record → `…container_not_recorded`, **non-2xx carrying the container id** (ADR-003) |

**Deleted**: BU creation, account creation, both rollback paths, `ResolveRootBusinessUnitIdAsync`,
`ResolveAccountForBuAsync`, `AttemptRollbackBuAsync`, the umbrella-BU branch, `UmbrellaBuId` on the
request, and `AccountId`/`AccountName`/`WasUmbrellaBu` on the response. Net deletion — the endpoint
does strictly less than it did.

## Three decisions worth recording

**1. The idempotency marker is OWNERSHIP, never `sprk_containerid`.**
The 2026-08-23 guard keyed on `sprk_containerid` being non-empty — but the wizard writes that field
at create time from the creating user's BU, so every secure project 409'd and none was ever
provisioned. Ownership by the `Secure Project` owner team is state **only provisioning writes**: the
wizard cannot set it (it does not know the team) and `applyUserBuDefaults` cascades BU-derived
*fields*, not ownership. `_sprk_securitybu_value` is still read as a **legacy** marker so projects
provisioned by the retired mechanism are refused rather than silently migrated.

*Residual edge, stated not hidden*: an administrator who deliberately reassigns a provisioned secure
project away from the owner team makes a later run see it as unprovisioned and create a second
container. That requires a deliberate ownership change on a secure project; the displaced container
id is logged so the first container stays traceable.

**2. Ownership is assigned BEFORE the container is created, and that order is load-bearing.**
Ownership is the *security* step; the container is the *storage* step. If the container fails after
ownership, the project is at least correctly owned inside the Secure Project BU. Reversed, the same
failure leaves a secure project owned by its creating user in an Operations BU — strictly the worse
posture. There is deliberately **no rollback** of the assignment: rolling it back would move the
record back out of the secure BU, turning a storage failure into a disclosure. Pinned by
`ProvisionProject_WhenContainerCreationFails_TheProjectIsStillOwnedByTheSecureTeam`.

**3. The one remaining `@odata.bind` is verified by read-back, not trusted.**
`ownerid@odata.bind` is the only navigation-property write left. Dataverse's behaviour on an
unrecognised `@odata.bind` property is to **accept the request and ignore the property** — the exact
mechanism that hid the old stamping bug for five months, and one that "get the name right" cannot
defend against, because "did I get the name right?" is unanswerable offline. Re-reading
`_owningteam_value` converts an unverifiable assumption into an observed fact. That is also why the
original nav-property blocker dissolved rather than being solved: `sprk_containerid` is
`NVARCHAR(100)`, a plain string, and the two lookups whose PascalCase names could not be recovered
should never have been written at all.

## Perturbations — 9 run, 9 bit

Harness: `scratchpad/perturb021.py`, both task-022 rules enforced (`os.utime` after restore; a
clean-tree baseline that must be 0). Baseline was 0 before and after the sweep.

| # | Perturbation | Failures |
|---|---|---|
| P1 | absent BU no longer fails closed | 1 |
| P2 | BU lookup `$top=2` → `$top=1` (ambiguity invisible) | **1** ← was 0 |
| P3 | **marker reverted to `sprk_containerid`** (the live regression) | **2** |
| P4 | re-swallow the container-stamp failure | 1 |
| P5 | stamp also writes `sprk_externalaccount` (the CLIENT column) | 1 |
| P6 | skip the owner assignment entirely | 3 |
| P7 | trust the ownership PATCH (drop the read-back comparison) | 1 |
| P8 | owner-team filter drops `isdefault` + `teamtype` | **12** ← was 0 |
| P9 | default BU name reverted to the plural | 1 |

### The sweep found two real coverage holes — in the FAKE, not the tests

P2 and P8 first came back **0**, and the cause was neither "test at the wrong level" nor "unreachable
code" — the two causes task 022 taught us to distinguish. It was a third thing: the fixture's
Dataverse double **ignored `$top` and ignored the discriminating `$filter` predicates**, so both
perturbations were invisible to it.

- With `$top` ignored, the double returned two BUs either way, so the ambiguity guard stayed covered
  by accident — while against real Dataverse `$top=1` makes ambiguity undetectable and an arbitrary
  BU is silently accepted.
- With the team filter ignored, the double answered on the BU id alone. Real BUs carry several teams
  (the live root BU has **four owner teams and three access teams**), so that query returns the wrong
  team.

Fixed by making the double honour `$top`, apply only the predicates actually asked for, and seed
**decoy teams** (one access team, one non-default owner team) ahead of the real one — so "took an
arbitrary team" is a visible failure rather than a coincidence that happens to work.

**Third instance of this class in this project**, after task 016's `$select`-ignoring fake and its
guard not being ported one directory over. Generalised: *a fake is evidence only to the extent it
refuses what Dataverse would refuse.* Any part of a query the double discards is a part the
production code can build wrongly for free.

## Two findings for the owner

**(a) The BU name in the docs was wrong — SINGULAR, not plural.** design §5.1, spec FR-28/NFR-05 and
this task's own POML all said `Secure Projects`. Live metadata: the BU is **`Secure Project`**
(`d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`, parented to root `Spaarke`). Shipping the plural as the
config default would have failed closed on every call — right direction, fabricated reason, and it
would have read as a missing environment rather than a wrong string. Corrected in design.md and
spec.md, pinned by `DefaultSecureBusinessUnitName_IsTheNameActuallyDeployed`. **Eighth instance of
"docs lose to live metadata."**

**(b) 🔔 The owner team holds `System Administrator`.** Full detail in design §5.1a. The team is
memberless, so nothing is exposed today — but review §D says of this exact question *"None — and
definitely NOT System Administrator"*, and the posture is one membership row away from full
administrative rights for a human. Environment setup, so out of this task's scope. Note the
consequence though: **this task's escalation trigger for "the team lacks entity privileges" cannot
fire in dev**, because assignment succeeds by omnipotence rather than by correct scoping. A green
provisioning run in dev is not evidence that the `Secure Project Owner` role exists. It does not.

## What this task did NOT achieve

**Document isolation.** Nothing reads the project's `sprk_containerid` yet — that needs the three
container-resolution strategies special-cased, which is `spaarke-secure-project-r1`. And **no human
can reach a secure project yet**: FR-28's explicit share (access teams) is still outstanding, so the
record is isolated but unshared. Both stated in design §5.1c and spec FR-28.

## Verification

- **All seven test projects: 11,443 passed / 0 failed** (was 11,431; +12 = 19 new − 7 replaced).
  `Sprk.Bff.Api.Tests` 10,831 · `Spe.Integration.Tests` 377 · `Sprk.Bff.Api.IntegrationTests` 96 ·
  `Spaarke.Scheduling.Tests` 46 · `Spaarke.Core.Tests` 45 · `Spaarke.ArchTests` 36 ·
  `RecordSyncJob.IsolatedTests` 12
- **Publish 43.70 MB** compressed incl. PDBs — **zero delta** vs baseline; ceiling 60. No packages
  added. `dotnet list package --vulnerable --include-transitive` clean.
- `Spaarke.UI.Components` `tsc --noEmit`: 0 errors in the changed files. Three pre-existing
  `@spaarke/auth` / `@spaarke/sdap-client` module-resolution errors remain in files this task did not
  touch (unbuilt workspace packages in a fresh worktree).

## One production change made for testability

`SpeFileStore.CreateContainerAsync` is now `virtual`. Substituting it at the ADR-007 facade is what
lets a test assert the provisioning SUCCESS path at all — before this, the only available assertion
was that provisioning reached business-unit creation and then failed on unavailable test-host Graph
services, and business-unit creation no longer exists. The alternative, faking `IGraphClientFactory`,
means standing up Graph SDK internals: transport-shaped mocking, banned by ADR-038 B1. Same reasoning
and same precedent as `DocumentCheckoutService.DeleteAsync` in task 022. No behaviour change.

## Client + e2e fallout

Both `provisioningService.ts` copies (`Spaarke.UI.Components` and the `LegalWorkspace` fork) updated
to the new request/response shape, and `PROVISIONING_STEPS` now names the steps that actually run —
the `bu` and `account` steps described work that no longer happens.

`tests/e2e/.../secure-project-creation.spec.ts` is **not CI-gated** (no workflow references
`tests/e2e`) and can only be validated against a live environment. Five cases whose mechanism was
deleted are `test.skip`ped with per-case reasons rather than rewritten blind — assertions I cannot
execute would look verified and would not be. The happy path, the container-isolation case and the
reference-completeness case were updated. Two real defects were fixed there in passing:

- its `queryProject` helper `$select`ed **three columns that do not exist**, so the query 400'd and
  its own `catch` returned `null` — every assertion built on it was **vacuous**;
- the happy-path and isolation cases tracked the business unit **for deletion**. Against the new
  endpoint that deletes the shared canonical `Secure Project` BU. Removed, with a note.

It also still uses `sprk_projectref` and `sprk_description` in its payload builders, neither of which
exists on live `sprk_project` — so it could not have passed as written whatever the endpoint did.
Flagged in the file header for whoever re-authors it.
