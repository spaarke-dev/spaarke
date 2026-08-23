# CLAUDE.md — spaarke-auth-v4-dataverse-MI

> **Project AI context.** Loads with every task in this project. Root `CLAUDE.md` still applies.
> **Generated**: 2026-08-19 by `/project-pipeline`

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: execute tasks in this project via the `task-execute` skill. Do NOT read a `.poml` and
implement manually. Bypassing loses ADR constraints, checkpointing, and the Step 9.5 quality gates.

Trigger phrases → `task-execute`: *"work on task X"* · *"continue"* · *"next task"* · *"resume task X"* ·
*"pick up where we left off"*.

---

## What this project is

Replace `BFF-API-ClientSecret` with a **Managed-Identity-issued federated credential (MI-FIC)** across every
BFF-identity confidential client — including the **OBO** (delegated user auth) paths that three prior audits
concluded could never be secret-free.

**Read [`spec.md`](spec.md) before any task.** 23 FRs across 6 workstreams.

## The one thing to understand first

Prior audits did **not** miss the code. `code-quality-and-assurance-r3`'s auth surface map inventoried all nine
secret consumers at `file:line`, then concluded **"NEVER-REMOVE"** — because of one false sentence in
`.claude/constraints/auth.md:108`: *"OBO flow (OAuth spec requires confidential client + secret)."*

OAuth requires a confidential **credential**. A secret is one of three ways to satisfy it, and Microsoft ranks it
last. That premise is now corrected (ADR-028 **A4** + **E-3**, applied 2026-08-17) — but **text is what failed
last time**, which is why Phase 6's forcing functions are a graduation criterion, not a nice-to-have.

**Do not re-derive "OBO needs a secret" from any stale doc you encounter.** If you find one, fix it.

## Non-negotiables

| Rule | Why |
|---|---|
| **OBO fails CLOSED** | Breaking it locks out every user immediately and totally: SPE documents, chat tool calls, Office add-ins, the Copilot agent, send-as-user email, and row-level authorization on every document + AI endpoint |
| **No in-session flips** | `#3b` attempt 1 took dev down (SIGABRT, eager connect under `ValidateOnBuild`). Deploy to the slot, verify, swap |
| **Secret stays until Phase 3 task 033** | It is the ordered fallback and the rollback mechanism. Removal happens after a soak, not before |
| **Never add a new `.WithClientSecret` site** | ADR-028 A4 + E-3. E-3 is transitional and time-boxed to THIS project; it does not license expansion |
| **`DefaultAzureCredential` is NOT a fix on OBO paths** | It produces app-only tokens and cannot perform an OBO exchange. The mechanism is a **client assertion** |
| **Resolve managed identities by resource ID, never by name** | Five UAMIs exist in the dev subscription; `spaarke-bff-identity` is named like the BFF's but is **not** attached to it |
| **`Spaarke.Dataverse` gains no ProjectReference** | CI-enforced by `LayerDependencyTests.cs:43` (FR-14). It is the base layer |

## Architecture — the credential seam

`Spaarke.Dataverse` is the **base layer** and cannot reference the BFF or `Spaarke.Core`
(`Spaarke.Core` → `Spaarke.Dataverse` already; the reverse is circular and fails FR-14). So:

```
Spaarke.Dataverse   declares  IClientAssertionProvider          (contract only)
Sprk.Bff.Api        implements ManagedIdentityAssertionProvider  (+ the Certificateless package)
                    registers it in a DI feature module (NOT inline in Program.cs — ADR-010)
```

Shared-lib constructors take `IClientAssertionProvider? assertion = null` — **nullable with a null default**,
mirroring the existing `TokenCredential? credential = null` at `DataverseAccessDataSource.cs:32`. This is what
keeps all **46 test fixtures** compiling. Adding a required parameter breaks every one of them.

### E4′ — the declarative path does not exist here

`Microsoft.Identity.Web`'s ordered `ClientCredentials` list is Microsoft's documented mechanism, but this
codebase has **zero** `EnableTokenAcquisition` / `ITokenAcquisition` / `IDownstreamApi` / `ClientCredentials` in
any `.cs`. `AddMicrosoftIdentityWebApi` is **inbound validation only**. `Spaarke.Dataverse` has no Identity.Web
reference at all.

**Use `.WithClientAssertion(Func<AssertionRequestOptions,Task<string>>)` + `ManagedIdentityClientAssertion`
(`Microsoft.Identity.Web.Certificateless`) — and reuse the instance, it caches until expiry.** The ordered
fallback the whole rollback story depends on must be **built**, not inherited.

## Live environment (verified 2026-08-19)

| | |
|---|---|
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| App registration | `SDAP-BFF-SPE-API` · `1e40baad-e065-4aea-a8d4-4b7ab273458c` · `AzureADMultipleOrgs` |
| Secret | 1, `Dataverse-Checkout-20251218`, expires 2027-12-19 (shared with the Dataverse app user) |
| FICs | 2 — GitHub OIDC + **`mi-bff-api-dev-assertion`** (`66bac39a-…`, created for this project) |
| App Service | `spaarke-bff-dev` in `rg-spaarke-dev` — **UserAssigned only** |
| UAMI | `mi-bff-api-dev` · clientId `5967251e-…` · **principalId `9fd47efb-…`** ← the FIC subject |
| Plan | `spaarke-dev-plan` — **P1v3**. 🔴 **CORRECTED 2026-08-23: a `staging` slot NOW EXISTS and is Running** (this row previously said "0 exist"). It has its OWN app settings and reports the same `cloud_RoleName` to App Insights — it caused a 40-min dev outage during task 051 by holding a rotated credential while prod was being fixed. **031 must not assume it needs creating; 033 must purge BOTH slots.** |

FIC shape: issuer `https://login.microsoftonline.com/{tenant}/v2.0` · subject = UAMI **principalId** (not
clientId — the commonest silent error) · audience exactly `api://AzureADTokenExchange`.

## Applicable ADRs

**ADR-028** (canonical — A4 + E-3) · **ADR-003** (server seams + OBO) · **ADR-008** (endpoint filters — the
row-level auth surface) · **ADR-009** (Redis-first — interacts with the CCA cache) · **ADR-010** (DI minimalism —
**live CI ceiling**) · **ADR-027** (tenancy) · **ADR-032** (kill-switch) · **ADR-038** (testing).

Load [`.claude/constraints/auth.md`](../../.claude/constraints/auth.md) and
[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) on every task.

### Two live CI gates that will bite

1. **`ADR010_DITests.cs`** — 1:1-interface ceiling is **153**. ~~`IClientAssertionProvider` makes 154; raise
   it to 154 in the same PR as task 020.~~ **CORRECTED at task 020 (2026-08-20) — do NOT raise it.**
   Measured at the gate: the real count is **151**, not 153, and `IClientAssertionProvider` does not move it
   at all, because the test scans `typeof(Program).Assembly` — the **BFF only** — while the interface is
   declared in `Spaarke.Dataverse`. The ratchet is *blind* to cross-assembly seams. Raising it would have
   widened the existing slack from 2 to 3 and let a future in-assembly interface land unreviewed.
   The blind spot itself is filed as an open owner decision (**#809**), not something task 020 changed.
2. **`LayerDependencyTests.cs:43`** — fails if `Spaarke.Dataverse` gains a ProjectReference. It must not.
   Note it enforces only *that* half; the PackageReference half every task here cites is **not** enforced
   (open owner decision **#810**).

## Per-task obligations (BFF=Y)

Every BFF-touching task: state the **Placement Justification** in the PR citing
`.claude/constraints/bff-extensions.md`; measure publish size and report absolute + delta against the
**44.96 MB incl. PDBs** net10 baseline (ceiling **60 MB**); run
`dotnet list package --vulnerable --include-transitive`. `Microsoft.Identity.Web.Certificateless` is a **new**
reference — measure it, don't assume.

## Testing

Per ADR-038, credential-seam coverage goes to **`tests/integration/seam/**`**. Banned: `Mock<HttpMessageHandler>`,
**DI-registration tests** (`Assert.NotNull(services.GetRequiredService<X>())`), ctor null-check tests.

The Phase 6 census (task 061) must be **source/assembly analysis**, not DI resolution — otherwise it becomes the
banned shape it exists to prevent.

`tests/Spaarke.ArchTests/` is **not** one of the 7 KEEP paths. Task 063 pre-declares the forcing functions
MAINTAIN-class so `/test-diet` at wrap-up does not delete the mechanism this project exists to leave behind.

## Cross-project

- **`customer-provisioning-orchestration-r1`** — change request accepted + applied. **We owe them task 030**
  (the `Register-EntraAppRegistrations.ps1` FIC extension) before their Wave G-3, or their task 130 builds a
  duplicate. One item raised back: `PROVISIONING-CHANGE-REQUEST.md` §9.2 (Model 2 FIC issuer tenancy).
- **`dataverse-access-unification-r1`** — ⛔ **INACTIVE / NOT SCHEDULED** (owner, 2026-08-20). The four-file
  interlock in `COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md` §4 is **cleared** — no sequencing, no contention.
  `DataverseWebApiService` + `DataverseWebApiClient` are **not** being deleted, so edits to them are permanent.
- Run `/conflict-check` on every PR. `.claude/` tasks are **main-session-only** (sub-agent write boundary).

## Definition of done

The distinguishing criterion is **success criterion 12**: introduce a deliberate ninth secret-bearing
confidential client on a scratch branch and **the build must fail**. Everything else is table stakes; that one
is what makes this project different from the three that preceded it.
