# A42 — FIC Creation Parity Contract (C# provisioner ⇄ `-FicOnly` script)

> **Date**: 2026-08-25
> **Task**: 205b (punch row **A42** — `g3-ficonly-consume-reconcile`), FR-C4
> **Path chosen**: **(b) contract-parity dual-path** (per owner **Q5 disposition 2026-08-25**: "CONTRACT-PARITY — C# provisioner + `-FicOnly` script both under one contract, parity tests pin (issuer,subject,audience) semantics + AADSTS70025 + exit-2 reporting")
> **Parity test suite**: `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/A42FicReconciliationTests.cs`
> **The two implementations under this contract**:
> 1. **PS (operator path)**: `scripts/Register-EntraAppRegistrations.ps1 -FicOnly` (auth-v4 §10 DELIVERED, 2026-08-21; live-verified contract — function bodies are auth-v4's contract surface, do not alter)
> 2. **C# (L2 runtime path)**: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/EntraAppReg/GraphAppRegistrationProvisioner.cs` (task 130) + `FicExchangeOutcomeClassifier.cs` + `CrossTenantFicRefusedException.cs` (task 205b) + `H3EntraAppRegHandler.cs` marker plumbing
> **Companions**: `auth-v4-integration-remediation-plan.md` §4/§5/§7 · `auth-v4-integration-draft-punch-rows.md` row A42 · `adr-028-a4-integration-conflict-resolution.md` (§9.2 contingency) · `auth-v4-integration-open-questions.md` Q2/Q5/Q9

---

## 1. Why path (b), not path (a) subprocess delegation

| Evidence | Weight |
|---|---|
| **Owner Q5 disposition (2026-08-25)** explicitly chose CONTRACT-PARITY | Binding — postdates and supersedes the task POML step 3's "recommended default (a)" |
| **spec.md:279 MUST NOT** (verbatim): "MUST NOT shell out to pwsh/az/pac from the L2 main-site process (Option D invariant; the sole sanctioned PowerShell path is the H14a sidecar…)" | Path (a) = H3 (L2 Worker) subprocess-invoking pwsh — a direct spec violation |
| **Task 130 already RETIRED the shell-out seam** (`RegisterEntraAppRegScriptProvisioner` retirement banner; `IEntraAppRegProvisioner` header cites "Option D's zero-shell-out invariant") | Path (a) would regress a landed architecture decision |
| The script itself resolves the UAMI via `az identity show` — a subprocess path would need az + pwsh + Graph CLI auth in the worker container | Fragility the POML's own Step 3(ii) test names |
| `pwsh-unavailable-in-l2-worker` escalation trigger | MOOT under (b): no subprocess ships |

**Consequence**: two implementations legitimately coexist — the script is the **operator/manual path** (and the only one that can exchange-verify when run on Azure compute carrying the UAMI); the C# provisioner is the **L2 provisioning-run path**. This document is the ONE contract both obey; the parity tests pin it; §5 states the maintenance obligation.

## 2. The canonical FIC-creation contract (both implementations MUST obey)

1. **FIC object shape** (auth-v4 §3.1, ADR-028 A4): `subject` = the UAMI's **`principalId`** (object id — NEVER `clientId`; the wrong-subject FIC creates cleanly and dies at exchange with **AADSTS700213** — §11 invariant 1); `issuer` = `https://login.microsoftonline.com/{uamiTenantId}/v2.0` (the `/v2.0` suffix is load-bearing); `audiences` = exactly **one** entry, `api://AzureADTokenExchange`.
2. **Issuer tenant per profile** (§9.2 **reading (a)**, owner-ratified Q2 2026-08-25): `customer-owned-model2` → the customer's own tenant (stamp UAMI lives there); every other profile → Spaarke's tenant. C#: `GraphAppRegistrationProvisioner.ResolveUamiTenantId`.
3. **Cross-tenant refusal — unconditional** (SF-5): if app-reg tenant ≠ UAMI tenant, REFUSE loudly at provisioning time — the pair would create successfully and fail only at first OBO, silently, weeks later. PS: `Assert-SpaarkeFicTenancy` (throws). C#: `GraphAppRegistrationProvisioner.AssertFicTenancy` → `CrossTenantFicRefusedException` → H3 rejection code `appreg-cross-tenant-fic-refused` (Resumable). The refusal has **no profile exemption** (Entra's same-tenant rule has none; under reading (a) every sanctioned profile is intra-tenant by derivation, so the guard is inert protection; a hypothetical reading-(b) shape falls back to the A4 **KV certificate**, never a weakened guard).
4. **Idempotency by the (issuer, subject, audience) TRIPLE — never by name** (SF-7): an equivalent triple under ANY name already satisfies a create request (Entra enforces (issuer, subject) uniqueness per application; a name-only check turns a correct no-op into a failed run — hit live by the PS estate on its first run). Audiences must count exactly one. PS: `Find-SpaarkeEquivalentFederatedCredential`. C#: `FindEquivalentByTriple` (used for both the create decision AND re-GET verification).
5. **Propagation retry — EXACT numeric match** (SF-6 / auth-v4 code-review C1): retry ONLY when Entra's structured `error_codes` array contains **70025** (measured live: ~8 intermittent failures over ~130s — §11 invariant 2) or **70021** (Microsoft-documented, never observed). **NEVER substring-match** — `"AADSTS70021" -match` also matches 700211 (wrong issuer) and 700213 (wrong subject), genuine config faults that must fail fast. Non-JSON fallback uses `AADSTS{code}(?![0-9])` negative lookahead. Authorization-layer errors (`invalid_scope`/`invalid_resource`/`invalid_target`/`access_denied`/`insufficient_scope`, and AADSTS500011) are ACCEPTANCE evidence (Entra evaluates the resource only after accepting the credential). Retry cadence: 5s doubling capped at 30s; stop when `elapsed + nextDelay > budget`; default budget 600s. PS: `$script:PropagationErrorCodes` + `Test-SpaarkeFicTokenExchange`. C#: `FicExchangeOutcomeClassifier` (`Classify` + `ExecuteWithPropagationRetryAsync`).
6. **Exit-code semantics** (SF-8):
   | Script | Meaning | C# equivalent |
   |---|---|---|
   | `0` | created + verified by a REAL token exchange (or `-AllowUnverified`) | `FicVerificationState.ExchangeVerified` — **not producible by L2 at creation time** (GOTCHA 2: L2's Worker cannot mint the BFF UAMI's assertion); reserved for exchange-capable verifiers (H13/T4, task-186 E2E runner, Q11 BFF warmup) |
   | `1` | fault — creation failed, drift refused, structurally invalid, exchange rejected | `EntraAppRegOutcome.Failure` / thrown `CrossTenantFicRefusedException` |
   | `2` | structurally-correct-but-unverifiable-from-this-host — the **NORMAL** off-Azure result | `FicVerificationState.PendingPostAppServiceVerification` → H3 records **`InterStepState.FicPendingPostAppServiceVerification = true`** |
   **Exit-2 is NEVER terminal success.** Every exit-2-equivalent outcome carries a recorded obligation: H13/T4 MUST discharge it post-App-Service with a real exchange, using `FicExchangeOutcomeClassifier`'s semantics (§5 below). Treating exit-2 as failure breaks legitimate runs; treating it as terminal success ships an unverified FIC that blocks `RequireSecretFreeIdentity=true` at first BFF boot with an opaque `AADSTS7000215`.
7. **UAMI resolution by ARM resource ID, never by name** (SF-1): 5 UAMIs exist in the dev subscription; `spaarke-bff-identity` is a decoy. PS: `Resolve-SpaarkeUserAssignedIdentity` rejects non-resource-ID input (`/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{name}` shape required). C#: the UAMI principalId arrives via `InterStepState.MiObjectId` from **H2a's `uami.bicep` deployment output** — an ARM-derived value by construction; no name-based lookup exists in the C# path (grep-verified: no `az identity show --name`, no Graph-by-display-name UAMI resolution).

## 3. Parity test suite design

`A42FicReconciliationTests.cs` pins the C# side of every contract row against fixtures whose EXPECTED behavior is transcribed from the script's live-measured semantics (the script itself is not executable in the CI unit suite; its side of the parity is anchored by auth-v4's live verification 2026-08-21 + the A35-merged function bodies, which this contract forbids altering):

| Contract row | Tests |
|---|---|
| §2.4 triple idempotency (name-only fixture MUST fail) | `A42a_*` (6 tests) |
| §2.5 exact-match retry codes / fail-fast faults / acceptance layers | `A42b_*` (7 tests) |
| §2.6 exit-2 marker, never terminal success | `A42c_*` (4 tests incl. exit-code pin) |
| §2.3 cross-tenant refusal + distinct rejection code | `A42d_*` (5 tests) |
| I6 Model 1 zero FIC calls (regression guard on task-130 contract) | `A42e_*` (1 test; task-130's `AcM1_1`/`AcI6_1`/`AcI6_2` remain green + unmodified) |
| §11 invariant 1 (wrong subject → AADSTS700213 named, not retried) | `A42f_*` (1 test) |
| §11 invariant 2 (fresh-FIC flap retried within budget, virtual clock) | `A42g_*` (2 tests) |

## 4. Documented divergences (deliberate, bounded)

| # | Divergence | Rationale |
|---|---|---|
| D-1 | **Drift handling**: PS default REFUSES to overwrite a name-matched-but-triple-mismatched FIC (requires `-ForceFederatedCredentialUpdate`); C# deletes + recreates | The script is an OPERATOR surface acting on possibly-live estates — replacing a FIC in active use is an availability event, so it demands explicit intent. The C# path is the PROVISIONING-RUN reconcile authority for the customer's own stamp (task-130 landed semantics): drift against the run's declared UAMI principalId is a defect to repair, and delete+recreate is the only mechanism (subject/issuer/audience are immutable on PATCH). C# behaves as script-with-`-Force`; both converge on the same final triple. Logged as a WARNING with the A42 citation. |
| D-2 | **Exchange verification at creation time**: PS performs it when an assertion is mintable (exit 0); C# NEVER does (always exit-2 equivalent) | GOTCHA 2 / SF-4: L2's Worker runs under L2's own platform UAMI and cannot mint the BFF UAMI's assertion — structural, not an omission. The C# creation-time verification is an independent re-GET of the triple; the exchange debt is recorded (§2.6) and discharged post-App-Service. |
| D-3 | **UAMI resolution**: PS resolves clientId/principalId/tenantId live via `az identity show --ids`; C# consumes H2a's Bicep outputs | Same ARM-resource-ID discipline, different transport. The C# path's UAMI tenant is derived per profile (§2.2) rather than read from a live resolve — under reading (a) the derivation and the live read agree by construction; if a future shape breaks that assumption, the guard (§2.3) is the backstop and this contract must be amended. |

## 5. Maintenance obligation (BINDING)

- Any change to the FIC semantics of ONE implementation (retry codes, triple matching, exit-code meanings, tenancy rule, subject recipe) **MUST be mirrored in the other AND in this document, and the parity tests re-run**, in the same PR. A one-sided change is the FR-C4 divergent-estate failure this contract exists to prevent (e.g. a future PS retry-code widening that never lands in C# leaves half the fleet retrying correctly and the other half asserting the OPPOSITE verdict after burning its budget).
- The PS script's FIC function bodies are auth-v4's live-verified contract surface (A35 merge-resolution rule: "do not alter during resolution") — r1 proposes changes to them as asks, not edits.
- Exchange-capable consumers (H13/T4, task-186 E2E runner, Q11 BFF warmup) **MUST use `FicExchangeOutcomeClassifier`** rather than re-deriving classification/retry — re-derivation is a third estate.
- `InterStepState.FicPendingPostAppServiceVerification` may be set only by H3 and discharged only by a real exchange verification (H13/T4); nothing else may clear it.

## 6. §11 three-question justification (root CLAUDE.md §11) for the new symbols

- **Existing** — Two FIC-creation surfaces already existed (master PS `-FicOnly`, 16 occurrences, live-verified 2026-08-21; task-130 C# `GraphAppRegistrationProvisioner`). Grep-verified 2026-08-25: the C# surface had NO tenancy guard (`Assert.*Tenancy|CrossTenant` → 0 matches — SF-5), NO AADSTS classification logic (only 2 stale 70021 comments — SF-6 surface), and name-first idempotency (SF-7).
- **Extension** — A42 introduces **no new FIC-creation surface**. `AssertFicTenancy`/`CrossTenantFicRefusedException`/`FicExchangeOutcomeClassifier`/`FindEquivalentByTriple` are PORTS of existing, live-verified PS-script semantics into the existing C# provisioner — they close SF-5/SF-6/SF-7/SF-8, they don't add a layer. `FicVerificationState` + the `InterStepState` marker are the exit-code contract made typed. The alternative — extending the PS script to be the single implementation via subprocess — is foreclosed by spec.md:279 (§1 above).
- **Cost of doing nothing** — (1) any C#-path run silently creates cross-tenant FIC pairs on misconfigured dispatches, failing weeks later at first OBO as AADSTS700213 in production (SF-5); (2) the estates drift on retry codes with opposite verdicts after budget-burn (SF-6/FR-C4); (3) C# `CreateFic` success is treated as terminal — an unverified FIC ships, `RequireSecretFreeIdentity=true` boot-fails a fresh stamp with an opaque `AADSTS7000215` and no diagnostic trail (SF-8; also the A39 ordering-guard dependency — A39 depends on A42 for exactly this reason).

## 7. Escalation-trigger disposition (task 205b POML)

| Trigger | Disposition |
|---|---|
| `task-130-i6-regression` | Did not fire — `AcM1_1`/`AcI6_1`/`AcI6_2` unmodified + green; A42e adds a STRONGER guard |
| `path-a-exit-code-drift` | N/A — path (b); exit-code semantics transcribed (§2.6), not remapped |
| `cross-tenant-fixture-drift` | Did not fire — grep + read of the existing test fixtures found no cross-tenant (app-reg, UAMI) fixture; all task-130 fixtures use a single tenant |
| `q2-customer-owned-m2-dispatch` | Did not fire — no fixture, criterion, or code path dispatches profile `customer-owned-model2` as a RUN; the profile literal appears only in derivation/guard logic + inert-protection tests, which Q2's reading-(a) ratification sanctions |
| `uami-name-based-resolution-detected` | Did not fire — no name-based UAMI resolution in any A42 code path (§2.7) |
| `pwsh-unavailable-in-l2-worker` | Moot — path (b), no subprocess |
| `a35-preconditions-missing` | Did not fire — script guard present (2 matches), ADR-028 A4 + E-3-CLOSED banner present locally |
