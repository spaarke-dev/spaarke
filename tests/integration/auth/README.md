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
| **Live tenant** (Tests 1–3) | Yes | No — see below | The actual row-set comparison against the provisioned canary user. |
| **Config tripwire** (`Fr20ImpersonatedRootSetFlag_*`) | No | **Yes — blocking** | The FR-20 flag cannot be enabled in checked-in configuration while the canary is unprovisioned. |

## Provisioning the canary user (once per environment)

Performed by a Dataverse System Administrator.

1. **Create a custom security role** — suggested name `Spaarke Impersonation Canary`. Its **only**
   privilege is **User-level (basic) Read on `sprk_matter`**. No Business Unit / Parent-Child /
   Organization depth on anything, and no privileges on any other entity. The role is what makes the
   comparison meaningful; a canary that can read the org proves nothing.
2. **Create (or designate) a dedicated, enabled `systemuser`** and assign it that role, and only that
   role. Record its **`systemuserid`** — the Dataverse row id, *not* the Entra object id. (The oid/
   systemuserid confusion is documented at `Spaarke.Dataverse/DataverseImpersonation.cs:20-21`.)
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
flag being on — the three live tests **halt as NOT RUN** rather than fail, because xUnit 2.9 offers no
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

## ⚠️ What is NOT yet wired (open decision — task 034 escalation)

**No pipeline in this repo can reach Dataverse.** `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` and
`nightly-health.yml` hold no environment credential, no canary identity, and no seeded org. So the LIVE
canary (Tests 1–3) runs **only when an operator runs it**, and the automated gate is limited to the
perturbation layer plus the config tripwire.

Two options, neither of which task 034 may choose unilaterally:

- **(A) Scheduled canary + required manual gate.** Add a `nightly-health.yml` job with federated
  credentials to the dev environment; the FR-20 rollout checklist requires a green canary run recorded
  in the PR. Cost: one federated credential and a canary identity per environment.
- **(B) Provision Dataverse secrets into CI.** Full blocking check on every PR. Cost: a standing
  Dataverse credential in GitHub Actions — a materially larger blast radius than (A), on a repo whose
  auth-v4 work has been *removing* standing secrets.

Until one is chosen, the FR-20 flag must stay `false` in checked-in configuration (which the tripwire
enforces) and every rollout must cite an operator-run canary. Recorded in
`projects/unified-access-control-r2/notes/task-034-negative-canary.md`.
