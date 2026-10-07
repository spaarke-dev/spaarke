# Task 062 — NFR-05 role-depth standing assertion

> **Delivered**: `tests/integration/auth/UnifiedAccessControl/SecureBuRoleDepthAssertion.cs` (pure
> evaluator) + `SecureBuRoleDepthAssertionTests.cs` (perturbation layer + live census reader).
> **Date**: 2026-09-09 · **Environment inspected**: `spaarkedev1`

---

## 🔔 ESCALATION — the assertion is RED against dev, and that is a live finding

The POML's `<escalation><trigger>` fired on its first live run: the `Secure Project` BU **exists**, and
the census reports **14 violations**. Per CLAUDE.md §6 this is reported, not suppressed, and the test was
**not** softened to accommodate it.

Census summary (live, 2026-09-09): 6 business units · 508 effective grants of
`prvReadsprk_Project` / `prvReadsprk_Matter` (24 held by humans) · 1 holder of `Secure Project Owner`
(the owner team) · **0** human members of the secure owner team.

| Clause | Result |
|---|---|
| 1 — no non-administrator human reaches the secure BU | ❌ **FAIL** (14 findings) |
| 2 — secure owner team has zero human members | ✅ PASS |
| 3 — `Secure Project Owner` held by that team alone | ✅ PASS |

### Finding A — design §5.2's original hole is still open (2 findings)

`Ralph Schroeder` / `ralph.schroeder_hotmail.com#EXT#@spaarke.onmicrosoft.com` — an **enabled,
interactive, non-administrator human**, still sitting in the **root** `Spaarke` BU — holds
`Spaarke Basic User`, which grants `prvReadsprk_Project` **and** `prvReadsprk_Matter` at **Deep**
(`privilegedepthmask = 4`) anchored at root. Root is an ancestor of `Secure Project`, so it reaches.

This is exactly §5.1a-2's census entry, unremediated. Fix A was applied to **`Test User 1` only** —
that user has been moved into `Spaarke Business Unit 1` (a sibling of `Secure Project`) and the
assertion confirms they are correctly isolated. The external Ralph identity was left behind in root.

### Finding B — the root default owner team makes every root-BU human an administrator (12 findings)

**Not previously recorded anywhere in the design.** The root BU's **default owner team** (`Spaarke`,
`teamtype = 0`, `isdefault = true`) holds **`System Administrator`**. A default business-unit owner team
contains every user in that BU automatically and its membership cannot be curated, so **every human in
the root BU holds Global read on everything** — by membership, not by any deliberate assignment.

Affected humans **for whom the team is the ONLY grant path** (6 identities × 2 privileges = 12
findings): `Ralph Schroeder` @`spaarke.onmicrosoft.com`, `Ralph Schroeder` `#EXT#`, `Final Test`,
`Eyal Iffergan`, `Jake Schroeder`, `E2E Test`. `ralph.schroeder@spaarke.com` is also a member but holds
`System Administrator` **directly**, so the assertion suppresses its redundant team-conferred grant —
see "the redundancy rule" below.

**Empirically corroborated before the test was written** (impersonated Web API reads, 2026-09-09):

| Impersonated caller | Direct roles | `sprk_projects` visible | Secure project visible |
|---|---|---|---|
| `Test User 1` (in `Spaarke Business Unit 1`) | `Spaarke Basic User` @ Deep | 0 | **no** ✅ |
| `Jake Schroeder` (root BU) | **none** | 19 | **yes** 🔴 |
| `Ralph Schroeder` `#EXT#` (root BU) | `Spaarke Basic User` @ Deep | 19 | **yes** 🔴 |

Jake holds **zero** directly-assigned security roles and reads the secure project. Elimination leaves
the root default team's `System Administrator` as the only grant path.

This also means design §5.1a-2's depth census — *"System Administrator … held by app users +
`Ralph Schroeder`, `Delegated Admin` (admins — expected)"* — **under-reports**: it was derived from
`systemuserroles` (direct assignment) only and does not see team-conferred roles.

### What the owner has to decide

1. **Finding A**: move the remaining root-BU humans out of root (Fix A, already the decided direction),
   or narrow `Spaarke Basic User`'s depth (Fix B). Either turns finding A green.
2. **Finding B**: remove `System Administrator` from the root `Spaarke` default owner team and assign it
   directly to the identities that should hold it. `Spaarke Demo`'s default team holds it too.
3. Only after both is the NFR-05 assertion capable of going green, which is project success criterion 4.

**Nothing was changed in the environment by this task.** It is test-authoring scope; role and BU
administration is not.

---

## Design decisions, and why

### The assertion is about depth, not about the BU

Design §5.1a-2's 2026-08-25 restatement is the binding form and it is a **bigger change than the
exemption** it also introduced. Reach is a property of *depth held at an ancestor*: `Spaarke Basic User`
never names the secure BU anywhere and reaches it anyway. A test that enumerated "roles scoped to the
secure BU" would have passed while the hole was wide open. So `Reaches(depth, anchorBu, secureBu, tree)`
walks the live BU tree:

| Depth | Reaches the secure BU when |
|---|---|
| `Basic` (1) | never — matches only owned records (which is why clause 2 exists separately) |
| `Local` (2) | the anchor **is** the secure BU |
| `Deep` (4) | the anchor is the secure BU **or any ancestor of it** (full chain, not one level) |
| `Global` (8) | always |
| anything else | **fails closed** — an unknown mask is treated as reaching |

The **anchor** is the role copy's business unit for a directly-assigned role (Dataverse only lets a user
hold the copy living in their own BU) and the **team's** business unit for a team-assigned role.

### The allow-list, and the deliberate friction

`AdministrativeRoleAllowList` is a literal pinned constant: `System Administrator`, `System Customizer`.
Adding to it means editing the file. Two entries were deliberately **left off**:

- **`Secure Project Owner`** — it does not need an exemption. At User (`Basic`) depth it reaches no
  business unit, so the depth model exempts it *structurally*. Allow-listing it by name would hide the
  one edit that matters: somebody widening it to `Local` / `Deep` / `Global`. There is a perturbation
  test for exactly that (`Evaluate_WhenTheSecureOwnerRoleIsWidenedAndGivenToAHuman_ReportsBothViolations`).
- **`Service Reader` / `Service Writer`** — the documented exemption (design §5.2, spec Unresolved
  Questions) is about the **principal** (Microsoft platform application accounts), not the role.
  Exempting the role name would exempt a *human* who ever acquired it. Non-human principals are
  filtered by `PrincipalIsHuman` instead, which survives a role rename.

### The allow-list applies only to DIRECT assignment

This is the rule that keeps the assertion honest against finding B. An allow-listed administrative role
reaching the secure BU **through a team** is reported as `AdministrativeRoleHeldByTeam` rather than
waved through. Rationale: the exemption's premise is a *deliberate per-identity administrator
designation*; a role on a team promotes every current and future member at once, and a default BU owner
team's membership is platform-managed. Without this rule the assertion would have gone **green** on a
dev environment where a role-less user reads the secure project — the precise failure NFR-05 exists to
prevent.

### …except where the team grant is redundant (the redundancy rule)

A principal who **already holds the same administrative role by direct assignment** is not reported a
second time for inheriting it from a team: the team path confers nothing they do not already hold, and
the noise would bury the principals for whom the team *is* the only grant path. Identity is matched on
**domain name**, not display name — dev has three enabled identities all displaying as
`Ralph Schroeder`, and letting one identity's direct assignment excuse another's team-conferred one
would silently exempt exactly the case being hunted. Both directions are pinned by tests
(`…ADirectAdministratorAlsoInheritsTheSameRoleFromATeam_DoesNotReportIt` and
`…ADifferentIdentityHoldsTheRoleDirectly_StillReportsTheTeamHeldGrant`) and by perturbations P11/P12.

### Fail-closed, three ways

Spec acceptance criterion 4 ("query failure is a FAILURE, not a skip") is met structurally:

- The census reader never catches. A non-2xx response throws with the URL, status and body.
- Any page short of the last is followed (`@odata.nextLink`); a truncated census could omit the one
  reaching principal and report isolation.
- Resolving fewer privileges than `GuardedPrivileges` names throws rather than grading a partial census.
- An **empty** census — what a swallowed error, a mistyped privilege name or a broken join all look
  like — is graded `VacuousCensus`, never `Isolated`.

### The loud skip

`SecureBuRoleDepthAssertion.InertMessage` carries the exact required wording — *"Secure Projects BU not
found — NFR-05 assertion inert; UAT environment setup pending"*. `Passed` is **false** for that verdict,
and the message is asserted verbatim by a perturbation test, so acceptance criterion 1 is verified
**without a tenant**. Both BU spellings are pinned (`Secure Projects` plural per design §5.2's target
topology, `Secure Project` singular as dev actually provisioned it); matching only one would have made
the assertion silently inert in the very environment it was written for.

xUnit 2.9 has no dynamic skip (no `Assert.Skip`, no SkippableFact), the same constraint task 034 hit, so
the live test halts as NOT RUN after printing the message rather than failing forever.

---

## Running it

```bash
export SPAARKE_NFR05_DATAVERSE_URL="https://spaarkedev1.crm.dynamics.com"   # or SPAARKE_CANARY_DATAVERSE_URL
export SPAARKE_NFR05_REQUIRED=true          # makes an unconfigured run a FAILURE, not a non-run
export AZURE_TOKEN_CREDENTIALS=dev          # see gotcha below
az login

dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj \
  --filter "FullyQualifiedName~SecureBuRoleDepthAssertionTests"
```

The identity needs read on `businessunits`, `roles`, `roleprivilegescollection`, `systemusers` (with
`systemuserroles_association`) and `teams` (with `teamroles_association` + `teammembership_association`).

> ⚠️ **Gotcha — `DefaultAzureCredential` on Azure.Identity 1.16.** Without
> `AZURE_TOKEN_CREDENTIALS=dev` the chain no longer falls through to `AzureCliCredential` and dies with
> *"EnvironmentCredential authentication unavailable"* even with a valid `az login`. This affects the
> NFR-04 canary's live tests identically (same credential model) and is worth knowing before diagnosing
> it as a permissions problem.

---

## Perturbation results

The evaluator was broken one line at a time, rebuilt and re-run. **No perturbation failed zero tests.**

| # | Perturbation | Tests it kills |
|---|---|---|
| P1 | `Deep` no longer follows the ancestor chain | 2 |
| P2 | allow-list ignores *how* the role is held | 1 |
| P3 | non-human principals are graded too | 1 |
| P4 | unknown depth mask fails OPEN instead of closed | 1 |
| P5 | an empty census is graded as clean | 1 |
| P6 | an absent secure BU reports a pass | 1 |
| P7 | `Local` depth inherits downward | 1 |
| P8 | owner-team human members are ignored | 1 |
| P9 | the owner role escaping the team is ignored | 1 |
| P10 | the headline verdict is taken from census order, not severity | 1 |
| P11 | the redundancy rule swallows every team-held admin grant | 3 |
| P12 | the redundancy rule matches on role only, not on principal | 1 |

---

## CI wiring — acceptance criterion 5 is NOT met, and cannot be met here

The perturbation layer (21 tests) runs in the ordinary suite via the
`tests/integration/auth/**` glob in `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`, so it is
blocking on every CI run today.

The **live** assertion is not, for the same reason task 034 escalated and did not resolve: **no pipeline
in this repo holds a Dataverse credential.** `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` and
`nightly-health.yml` have no environment credential and no target org. The two options are unchanged
from `tests/integration/auth/README.md` § "What is NOT yet wired" — (A) a scheduled job with federated
credentials to dev, or (B) standing Dataverse secrets in GitHub Actions — and choosing between them is
an owner decision that task 062 may not make unilaterally. Whichever is chosen covers **both** standing
live assertions (NFR-04 canary and NFR-05 role depth), which is an argument for doing it once.

---

## ✅ REMEDIATED 2026-09-09 — owner fix, main-session verified

The owner removed `System Administrator` from the root BU's default owner team. Re-verified live:

```
default owner teams, after the fix
  Spaarke (root)            roles=[]                       ← was [System Administrator]
  Spaarke Business Unit 1   roles=[Reporting Viewer, Basic User, AI Analysis, Office Add-In]
  Secure Project            roles=[Secure Project Owner]
  Spaarke Demo              roles=[System Administrator]   ← still armed, see below
  Spaarke Dev 1             roles=[]
  Spaarke Test 1            roles=[]

impersonated re-test
  jake.schroeder@demo.spaarke.com  →  403 Forbidden (no read privilege at all)
  (before the fix: 19/19 projects, INCLUDING the secure one)
```

**The live exposure is closed**, and it was closed by a role change on one team — not by relocating
anyone. Owner direction 2026-09-09: *"let's not focus on relocating users — we have test users that
are in the correct BU."* Fix A's relocation is **not** the remediation path for this finding and
should not be re-raised as one.

### What the mechanism actually was (my first write-up stated it imprecisely)

Security role assignment does **not** follow business-unit assignment. What follows BU assignment is
**default owner team membership**, which is system-managed — every user whose `businessunitid` is
that BU is a member and cannot be removed (the root BU's default team had **168** members against 7
enabled interactive users). Someone had explicitly assigned `System Administrator` to that team, and
team roles are inherited by members. So the path was:

> BU assignment → *automatic* default-team membership → a role someone *explicitly* put on that team.

Only the middle step is automatic. That distinction is why the fix is one unassignment rather than a
data migration — and why the design's census missed it entirely: the census read `systemuserroles`
(DIRECT assignment), and this privilege never appears there.

### Two residuals — neither is a relocation question

1. **`Spaarke Demo`'s default team still holds `System Administrator`.** No enabled interactive users
   are in that BU today, so it is not a live exposure. But it is the identical trap armed in a second
   BU, and **new users land in the ROOT BU by default** in Dataverse — so this class recurs at
   onboarding time unless the root BU's default team is kept role-free. Cheap to check, cheap to fix.
2. **Criterion 5 remains unmet**: the live half of this assertion cannot run in CI because no pipeline
   holds a Dataverse credential — the same blocker as task 034's NFR-04 canary. One credential
   decision arms both standing assertions. Until then this assertion only runs by hand, which is
   precisely how the finding above went unnoticed.

### Consequence for task 036

036's gate was recorded as untrustworthy while every root-BU user inherited `System Administrator`
(the impersonated and app-only sets would be EQUAL, and NFR-04 requires strictly smaller — equality
means impersonation is inert and must fail the build). **That blocker is now lifted in principle.**
⚠️ It has NOT been re-measured: run the NFR-04 canary against the fixed environment before trusting
036's swap, rather than assuming the fix restored a meaningful inequality.
