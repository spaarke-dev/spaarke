# `tests/integration/auth/**` — security-auth KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md) — Testing Strategy
> **Constraint loader**: [`.claude/constraints/testing.md`](../../../.claude/constraints/testing.md)
> **Standard**: [`docs/standards/TEST-ARCHITECTURE.md`](../../../docs/standards/TEST-ARCHITECTURE.md)

## What lives here

Integration tests covering **authentication, authorization, OBO exchange, claims handling, token validation**. This is one of the 6 KEEP-protected path categories.

## Deletion-safety rule

Removing a file under this path requires a **same-PR replacement** covering the same scenario. Enforced at code-review (`task-execute` Step 9.5) by path inspection — see ADR-038 §2.

## Authoring template

See [`tests/CLAUDE.md`](../../../tests/CLAUDE.md) integration-first AAA template.

## Inventory status (2026-06-26)

Per `notes/test-inventory-summary.md`: **25 KEEP-security-auth files** identified in the pre-reorg inventory. Bulk move pending (see `notes/path-reorganization-design.md` for csproj strategy decision).

## ~~⚠️ This directory is EMPTY and NOT COMPILED~~ — RESOLVED 2026-08-25

> **Superseded.** The warning below described the state on 2026-08-20 and is retained for provenance
> only. `auth/**` **is** compiled today: `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` globs
> `..\..\integration\auth\**\*.cs` (added independently by `sdap-SPE-admin-app-r2` task 012 and
> `unified-access-control-r2` task 001; the duplicate glob was deduplicated during task 045's master
> merge). Tests live under `tests/integration/auth/{Module}/` and run in the ordinary suite.

<details><summary>Original 2026-08-20 warning</summary>

The bulk move above never happened. This directory contains only this README, and — more importantly —
**it is not included in any test project**. `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`
globs `contract/`, `regression/`, `seam/` and `tenant/`, but **not `auth/`**. A test authored here today
would compile nowhere and run never, silently.

**Where auth/OBO tests actually live**: [`tests/integration/seam/Auth/`](../seam/Auth/) — currently
`CredentialSelectionSeamTests.cs` and `ConfidentialClientSharingSeamTests.cs`
(`spaarke-auth-v4-dataverse-MI` tasks 010 / 011). `seam/**` is the only KEEP path that is both
deletion-protected *and* compiled, so it is the correct home until this directory is wired up.

**To fix properly**, either add `auth/**` to the csproj `<Compile Include>` set and move the files, or
retire this directory and fold security-auth into `seam/`. Recorded rather than fixed here because it is
a test-architecture decision owned by ADR-038, not by an auth project. Surfaced by `adr-check` finding
**W5** at task 011's quality gate.

</details>

---

# NFR-04 impersonation negative canary

> **Owner**: `unified-access-control-r2` task 034 · **Gates**: task 036 (the FR-20 root-set swap)
> **Code**: [`UnifiedAccessControl/ImpersonationNegativeCanaryTests.cs`](UnifiedAccessControl/ImpersonationNegativeCanaryTests.cs),
> [`ImpersonationNegativeCanary.cs`](UnifiedAccessControl/ImpersonationNegativeCanary.cs),
> [`ImpersonationCanaryEnvironment.cs`](UnifiedAccessControl/ImpersonationCanaryEnvironment.cs)

## The failure being guarded

An impersonated Dataverse read that loses its `MSCRMCallerID` header does not error. It runs as the BFF
application user — a System Administrator on dev — and returns the **org-wide** row set with HTTP 200.
No exception, no log line, no ProblemDetails. Everything downstream then behaves correctly on a silently
wrong set. The only observable difference between "impersonation works" and "org-wide disclosure" is
that the impersonated answer stopped being *smaller* than the app-only answer.

**Equality between the two row sets means impersonation is inert, and fails the build.** It is never a
skip and never a warning: a test that goes green when impersonation does nothing is worse than no test,
because it converts an unknown into a gate signature on a merge.

## What runs where

| Layer | Needs a tenant? | Runs in CI today? | What it proves |
|---|---|---|---|
| **Perturbation** (`Evaluate_*`, `Require_*`) | No | **Yes — blocking** | The invariant reports FAILURE for the inert case, the not-a-subset case, the duplicate-row case, the vacuous-baseline case, and the empty-impersonated case; and that missing canary config throws with the provisioning contract. Weakening "strictly fewer" to "fewer or equal" turns these red. |
| **Live tenant** (Tests 1–4) | Yes | No — see below | The actual row-set comparison against the provisioned canary user. |
| **Config tripwire** (`Fr20ImpersonatedRootSetFlag_*`) | No | **Yes — blocking** | The FR-20 flag cannot be enabled in checked-in configuration while the canary is unprovisioned. |

## Provisioning the canary user (once per environment)

Performed by a Dataverse System Administrator.

### The variant in use: an EXISTING non-admin test user (owner directive, task 034 amendment 2)

Do **not** create a new systemuser, do **not** move a user between business units and do **not** build a
dedicated canary role for this. Point the canary at an existing, enabled, non-admin test user who sits in
a child business unit, and derive the expected set (Test 2) **independently of the impersonated read**:

1. Confirm the user holds **no Organization-depth Read on `sprk_matter`**, directly or through a team
   (the 2026-09-09 hazard: a root-BU default team carrying System Administrator makes both reads equal —
   a confident false pass). Record the user's roles and their `prvReadsprk_matter` depth masks.
2. Confirm hierarchy security is off (`organization.ishierarchicalsecuritymodelenabled = false`) or account
   for it.
3. Derive `SPAARKE_CANARY_SEEDED_MATTER_IDS` app-only from what that depth grants: for **Deep** (4), the
   matters whose `owningbusinessunit` is the user's business unit or a descendant; for Local (2) the
   user's own business unit only; for Basic (1) the matters the user owns — **plus** any
   `principalobjectaccess` rows (Read bit) for the user and each of their teams. Never copy the
   impersonated result into the expected set: that makes Test 2 a tautology and is a failed gate.
4. Confirm the org holds strictly more matters than the expected set, or "strictly fewer" is unsatisfiable.
5. The BFF's Dataverse application users must hold `prvActOnBehalfOfAnotherUser` (System Administrator
   carries it at Global depth); never grant it to a Spaarke role as a workaround.

The dedicated-role recipe below remains valid for an environment that has no suitable existing user.

### The dedicated-role recipe (alternative)

1. **Create a custom security role** — suggested name `Spaarke Impersonation Canary`. Its **only**
   privilege is **User-level (basic) Read on `sprk_matter`**. No Business Unit / Parent-Child /
   Organization depth on anything, and no privileges on any other entity. The role is what makes the
   comparison meaningful; a canary that can read the org proves nothing.
2. **Create (or designate) a dedicated, enabled `systemuser`** and assign it that role, and only that
   role. Record its **`systemuserid`** — the Dataverse row id, *not* the Entra object id. (The oid/
   systemuserid confusion is documented in the header contract of `Spaarke.Dataverse/DataverseImpersonation.cs`.)
3. **Seed exactly K > 0 `sprk_matter` rows owned by that user**, and confirm the org holds **strictly
   more** matters than K that it cannot read. If the org has only the canary's own matters, "strictly
   fewer" is unsatisfiable and the canary cannot pass no matter how well impersonation works.
4. **Confirm the BFF Dataverse application user holds `prvActOnBehalfOfAnotherUser` (Delegate)** and
   remains **broadly scoped**. A narrowed app user silently *narrows* impersonated results — a
   wrong-answer mode, caught by the not-a-subset verdict (investigation 08 §3c).
5. **Record the values** in the environment's secret store; they are identifiers, not secrets, but they
   drift.

## Running it

```bash
export SPAARKE_CANARY_DATAVERSE_URL="https://<env>.crm.dynamics.com"
export SPAARKE_CANARY_SYSTEMUSERID="<canary systemuserid GUID>"
export SPAARKE_CANARY_SEEDED_MATTER_IDS="<guid>,<guid>,<guid>"   # the K seeded matters
# export SPAARKE_CANARY_MI_CLIENT_ID="<user-assigned MI client id>"  # optional
export SPAARKE_CANARY_REQUIRED=true      # makes missing provisioning a FAILURE, not a non-run
export AZURE_TOKEN_CREDENTIALS=AzureCliCredential   # REQUIRED: the test assembly otherwise restricts the
                                                    # credential chain to EnvironmentCredential (TestOutboundNetworkGuard,
                                                    # Layer 1) and every live test fails with CredentialUnavailableException
export SPAARKE_TESTS_ALLOW_OUTBOUND=1               # lifts the test host's outbound-HTTP block (Layer 2)

az login   # the ambient credential must map to the BFF Dataverse application user

dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj \
  --filter "FullyQualifiedName~ImpersonationNegativeCanaryTests"
```

Authentication uses `DefaultAzureCredential` (the `Graph:ManagedIdentity:Enabled=true` branch of
`DataverseWebApiService`). The managed-identity-disabled branch is deliberately unavailable here: it
needs an `IConfidentialClientProvider` only the BFF's DI container builds, and reaching for a client
secret in a test is what auth-v4 removed.

## The blocking-gate wiring

`SPAARKE_CANARY_REQUIRED` is what a canary run asserts about itself. Without it — and without the FR-20
flag being on — the four live tests **halt as NOT RUN** rather than fail, because xUnit 2.9 offers no
dynamic skip and a permanently red test is a deleted test.

The gate is instead held by `Fr20ImpersonatedRootSetFlag_WhenEnabledInCheckedInConfiguration_RequiresAProvisionedCanary`,
which runs unconditionally with no tenant and no secrets. It text-scans every checked-in
`src/server/api/Sprk.Bff.Api/appsettings*.json` for
`ExternalAccess:ImpersonatedRootSets:Enabled` and fails unless the value is literally `false`. A
tokenized value (`#{...}#`) or a Key Vault reference counts as enabled: indeterminate at review time
means a deployment could turn it on with no canary provisioned, and a security flag resolves toward
requiring the canary. It text-scans rather than parsing because `appsettings.template.json` is a
deploy-token template and is **not valid JSON** — a JSON parser throws on it, and skipping unparseable
files would have blinded the gate to the one file a deployment actually renders.

**Net effect**: task 036 cannot ship the impersonated root-set path enabled-by-default without the
canary being provisioned and run. That is the mechanical half of "034 is a blocking merge gate for 036".

## The live layer is a MANUAL gate (decided — task 034 amendment 1)

**No Dataverse test runs in CI** (owner, 2026-09-10; a standing directive since 2026-09-30). Neither a
scheduled canary with a federated credential nor Dataverse secrets in CI is built. The gate has two halves:

- **Mechanical, in CI (blocking):** the perturbation layer and the config tripwire above. The FR-20 flag
  cannot be enabled in checked-in configuration.
- **Manual, before task 036 merges and before every FR-20 rollout:** an operator runs the four live tests
  with `SPAARKE_CANARY_REQUIRED=true` (and the two variables above), and the run is recorded — both set
  sizes, the canary user's roles, and how the expected set was derived — in
  `projects/unified-access-control-r2/notes/task-034-negative-canary.md`. All four must PASS; a NOT RUN is
  not a pass, and equal sets (INERT) are a failed gate.

**One-time inversion check:** point `SPAARKE_CANARY_SYSTEMUSERID` at an admin user. Tests 1 and 4 must FAIL
with "IMPERSONATION IS INERT". If they pass, the canary cannot see the failure it exists to catch.

First manual run: 2026-10-03, dev, canary `uac.child.user@demo.spaarke.com` — all four PASS, impersonated
59 vs app-only 61; inversion check FAILED as designed (61 = 61, INERT).
