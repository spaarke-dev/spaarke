# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-21 (task **030 COMPLETE** — both quality gates passed)
> **Recovery**: Read "Quick Recovery" first. Everything needed to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — eliminate `BFF-API-ClientSecret`; migrate every BFF-identity confidential client (incl. **OBO**) to a Managed-Identity federated credential |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **021 — Ordered credential selection** (MI-FIC → KV certificate → dev secret) — not started |
| **Task file** | `projects/spaarke-auth-v4-dataverse-MI/tasks/030-fic-provisioning-automation.poml` |
| **Status** | not-started. **030 complete** (adr-check 0 violations; code-review 2 criticals found + fixed) |
| **Next Action** | `task-execute` on `tasks/021-ordered-credential-selection.poml`. **Read `notes/decisions/020-assertion-seam.md` FIRST — 021's scope GREW.** Ordered selection is MI-FIC → certificate → secret, and only the FIRST is an assertion, so it cannot live behind `IClientAssertionProvider`; 021 must author a second, client-level contract |
| **Why 030 first** | ⏩ Owner pulled it forward 2026-08-19 for `customer-provisioning-orchestration-r1`'s Wave G-3. **Delivered** — their PR #779 has zero FIC code, so the duplicate-work risk did not materialise |
| **Progress** | **7 of 26 active complete** (001, 002, 003, 010, 011, 020, **030**) · **19 remaining** · 3 deferred |
| **Portfolio** | [#800](https://github.com/spaarke-dev/spaarke/issues/800) · Epic [#426](https://github.com/spaarke-dev/spaarke/issues/426) |

### Task 030 — files modified this session

| File | Purpose |
|---|---|
| `scripts/Register-EntraAppRegistrations.ps1` | **+835/−3 additive.** 7 FIC functions + execution section + 12 params. Inert without `-CreateFederatedCredential`/`-FicOnly`/`-ExportFunctionsOnly` |
| `scripts/README.md` | Registry entry: FIC usage, exit codes, idempotency contract |
| `projects/.../notes/decisions/030-fic-automation.md` | **NEW** — design rationale + 11-check verification table + what was NOT verified |
| `projects/.../notes/PROVISIONING-CHANGE-REQUEST.md` | **§10 appended** — delivery notice, invocation contract, exit codes, §9.2 re-raise, merge note |
| `projects/.../CLAUDE.md` | Corrected the stale "raise the ADR-010 ceiling to 154" instruction that task 020 disproved |

### Task 030 — quality gates

| Gate | Result |
|---|---|
| `adr-check` | **0 violations**, 7 warnings — all applied, or booked onto the tasks that own them (031, 033) |
| `code-review` | **2 CRITICAL** + 14 warnings. Both criticals **fixed**; 10 warnings applied, 4 deferred with reasons |
| Verification | 15 live/offline checks + a 14-assertion regression harness, all passing |

**C1** — `-match "AADSTS70021"` is a regex *substring* test, so it also matched `AADSTS700211`
(unrecognised issuer): a configuration fault retried for the full 600 s budget, then reported with a
diagnosis asserting the opposite of the truth. Sat on task 031's critical path. Now classified on
Entra's structured `error_codes` / OAuth2 `error` fields, with codes stored as **numbers** so
substring matching cannot return by accident.

**C2** — the `-ExportFunctionsOnly` dot-source mode ran `param()` in the **caller's** scope, silently
replacing a consumer's `$TenantId` with this script's production default. Wrong tenant = wrong issuer
= a credential that creates cleanly and never works. Mode **removed**; consumers invoke `-FicOnly`.

### Task 030 — E2E verified, and two error codes this project had wrong

Verified against the live tenant on 2026-08-21 using a throwaway ACI carrying `mi-bff-api-dev`, plus
two throwaway app registrations (all deleted; nothing left behind):

- FIC created by the script → **token issued**
- wrong-subject FIC → **rejected**, `AADSTS700213`
- existing dev BFF FIC → **token issued** (closes the credential half of PHASE-0's "remaining spike")

**Two corrections that matter downstream:**

| Case | This project said | Actually |
|---|---|---|
| Wrong subject | `AADSTS70021` | **`AADSTS700213`** |
| Propagation | `AADSTS70021` | **`AADSTS70025`**, and it **flaps** ~2 min |

The propagation one was a live bug: the retry list held only `70021`, so a real propagation failure
would have failed fast — the opposite of criterion 4. Both corrected in the script, in PHASE-0 §7 at
source, and booked onto tasks 031/032 (a single green check inside the flap window proves nothing).

### Task 030 — three things a fresh session must not re-derive

1. **Idempotency is keyed on `(issuer, subject, audience)`, NOT the credential name.** Entra enforces that
   triple's uniqueness itself and *rejects* a duplicate — so a name-only check does not create a duplicate,
   it produces a **failed run against an already-correct credential**. Verified live 2026-08-21.
2. **The structural check runs BEFORE the retry loop** — but *not* for the reason first written. The two
   cases have different error codes (above). It runs first because it is the **only verification possible
   off-Azure**, because it names the fault, and because it does not depend on undocumented error codes.
3. **Exit `2` ≠ failure.** Structurally correct but not exchange-provable from *this host* — a workstation
   cannot mint a managed-identity assertion. Not a deferral: the exchange itself is now verified E2E.

### Task 030 — open item carried forward

⚠️ **PROVISIONING-CHANGE-REQUEST §9.2 is still unanswered** (Model 2 cross-tenant FIC issuer). The script
now *refuses* cross-tenant pairs at runtime, so the failure is loud rather than silent — but the question
must be answered **before `customer-provisioning-orchestration-r1` Wave G-3 task 130 executes**.

### Repo + live state (verified at handoff)

| Check | Value |
|---|---|
| Working tree | clean at task-030 close |
| Pushed through | `0e73c014d` |
| Behind `origin/master` | **0** — merged `418718295` at task-030 start |
| Slot `/healthz` | **200** (`spaarke-bff-dev-staging`) |
| Production `/healthz` | **200** — **never swapped**, untouched all project |
| Full BFF suite | **10,554 / 0** (97 skipped) |
| ArchTests | **36 / 36** |
| Publish | **43.68 MB** compressed incl. PDBs (`Compress-Archive`, 215 files) · ceiling 60 |
| CVE | clean |

### Critical context (read before touching code)

**The premise is PROVEN.** Task 002 demonstrated on the wire that OBO works under a Managed-Identity-issued
client assertion — Graph/SPE, Dataverse `user_impersonation` (with `upn` preserved, so row-level
authorization still evaluates as the *user*), and long-running OBO. **No pivot to a certificate.**
Three prior audits concluded the secret could never be removed, on one false sentence. **Do not re-derive
"OBO needs a secret" from any stale doc — fix the doc.**

**OBO fails CLOSED.** A bad change locks out every user instantly and totally. The `staging` slot exists so
the credential mechanism is the only variable under test. **Never swap outside task 032.**

---

## ⚠️ Open owner decisions (nothing is blocked on these)

1. **ADR-010 ratchet** — [#809](https://github.com/spaarke-dev/spaarke/issues/809). The gate is **blind to
   cross-assembly seams** (scans the BFF assembly only), and its ceiling is **153 against a real count of
   151**, so two in-assembly interfaces can land unreviewed today. Both are one-line detector fixes with
   repo-wide blast radius. Tightening could redden CI for other in-flight projects — owner's call.
2. **`LayerDependencyTests`** — [#810](https://github.com/spaarke-dev/spaarke/issues/810). Enforces
   `ProjectReference` but **not** `PackageReference`, so half the constraint every task in this project
   cites rests on reviewer attention.
3. **CLAUDE.md §10 publish-size baseline is not method-qualified.** Two honest measurements of one tree
   differed by ~1.3 MB purely on compression method — more than the +5 MB escalation threshold.

---

## Completed (6 of 26)

| Task | Outcome |
|---|---|
| **001** | `staging` slot on `spaarke-bff-dev`, UAMI-assigned, healthy, **not swapped** |
| **002** | **OBO PROVEN under MI-FIC.** T0–T4 pass; T5 negative control fails as required |
| **003** | Credential decision recorded; ADR-028 A4 adoption status + E4′ correction |
| **010** | MI-flag gating fixed; **app-only decoupled from OBO** in `DataverseAccessDataSource` |
| **011** | Confidential clients shared process-wide; **ADR-009 token-cache decision made** |
| **020** | `IClientAssertionProvider` seam; **ADR-010 ceiling deliberately NOT raised** |

Decision records: [`notes/decisions/`](notes/decisions/) — `001-slot-creation.md`, `002-spike-results.md`,
`003-credential-decision.md`, `010-credential-gating.md`, `011-adr009-token-cache-decision.md`,
`020-assertion-seam.md`. **Read `020` before task 021 — it changes 021's scope.**

---

## The three findings that change downstream work

### 1. Task 021's scope grew (task 020 decision record §3)

Ordered selection is **MI-FIC → KV certificate → dev secret**. Only the **first** is an assertion —
a certificate uses `.WithCertificate(x509)`, a secret uses `.WithClientSecret(...)`. So ordered selection
**cannot live behind `IClientAssertionProvider`** (`Task<string> GetAssertionAsync`), and neither can the
shared confidential-client cache.

**Task 021 must author a second, client-level contract** in `Spaarke.Dataverse` that returns a configured
`IConfidentialClientApplication`, owns the ordered selection, and owns the ONE shared cache keyed
`(tenant|client|credential-kind)`. Only `DataverseAccessDataSource` needs the contract — the three BFF-side
sites can inject the cache **concretely** (ADR-010 prefers that). Both 021 and 022 POMLs are already amended.

### 2. The ADR-010 ratchet cannot see this project's central seam

Task 020's POML said "raise the ceiling 153 → 154, without it the build fails." **False**, verified twice
and reproduced independently by the quality gate: ArchTests pass at 153, the real count is **151**, and
`IClientAssertionProvider` is **absent** from the counted list because `ADR010_DITests` scans
`typeof(Program).Assembly` while the interface lives in `Spaarke.Dataverse`. Ceiling left untouched.

### 3. Task 010 shipped a regression that only a full-suite run caught

Its fail-fast validation broke **13 `ExternalAccess` contract tests** via a stub relying on the removed
silent fallback. Fixed at task 011. Generalised into
[`.claude/FAILURE-MODES.md` **AP-7**](../../.claude/FAILURE-MODES.md): *converting a silent fallback into
fail-fast has unbounded blast radius by construction — callers relying on it supplied nothing, so there is
nothing to grep for, and a targeted test run excludes them by definition.* **Run the full suite for that
change class.** Also: stash and re-run before calling failures pre-existing.

---

## Carried-forward obligations (booked in POMLs, not just here)

| Onto | Obligation |
|---|---|
| **030** | ⏩ **RUN NEXT** — before 021/022; provisioning Wave G-3 soft-blocked. Verify by performing a **real token exchange**, not just script output |
| **021** | Author the **client-level seam** (see finding 1) · one `IsFallThroughEligible(MsalServiceException)` predicate — `managed_identity_request_failed` is the FR-B4 wrong-identity signature and MUST **fail loud**, not fall through to the secret · short-TTL **negative memo** (a failing mint costs ~80 ms *per request* otherwise) · decide whether the contract widens to a Spaarke-owned request record |
| **022** | Collapse the three per-class CCA caches — **task 011's A4 exception EXPIRES HERE**; escalate rather than defer · migrate `ConfidentialClientSharingSeamTests` when the diagnostics move · converge the **UAMI precedence** (four shared-lib sites read the two keys in the OPPOSITE order and don't call the shared resolver) |
| **024** | Workstation user-secret `API_CLIENT_SECRET` is **STALE** → `AADSTS7000215` |
| **031** | Verify slot `keyVaultReferenceIdentity` **before** the OBO checklist · do **not** use `Deploy-BffApi.ps1 -UseSlotDeploy` (it always swaps) · needs a second test principal for the fails-closed case |
| **032** | Site properties **do not swap** — verify identity + `keyVaultReferenceIdentity` on **both** slots first |
| **033** | Purge the secret from **BOTH** slots; on dev these are **plaintext app settings**, not KV references · the lowercase `bff-api-client-secret` KV alias breaks the Office add-in if missed |
| **060** | `_cca`-decoupling source guard (010) · no-call-site-bypasses-the-cache guard (011) · `ManagedIdentityClientAssertion` constructed only in a ctor/static-init, never in a method body (020) · Power BI allowlist entry **with the deferral reason** |
| **061** | Census must scan **ALL server assemblies**, not just the BFF (020's blind spot) · keep both Power BI sites as **secret-backed** entries |
| **090** | Power BI criterion 10 **waived with reason** · `/test-diet` must adjudicate the three auth files under `seam/Auth/` — note `tests/integration/auth/**` is **empty AND not compiled into any csproj**, so a test authored there would run never |

---

## Owner directives (standing)

1. **Autonomous execution + parallel task agents where safe.** Escalation triggers and fail-closed gates
   (022, 031, 032, 033) still stop for judgment.
2. **CI issues handled separately.** The god-class ratchet one was already fixed on master
   (`866f9c101` retired it → complexity guidance).
3. **Power BI deferred** (tasks 040/041/042 ⏭️) — [DEF-001 / #804](https://github.com/spaarke-dev/spaarke/issues/804).
   `PowerBi:ClientSecret` stays. Made visible not silent via the 060/061/090 obligations above.
4. **`dataverse-access-unification-r1` is INACTIVE / not scheduled** — interlock cleared everywhere.
   Consequence: `DataverseWebApiService` + `DataverseWebApiClient` are **not** being deleted.
5. **PR #801** — fixed as recommended and closed.

---

## Live environment (verified 2026-08-19)

| | |
|---|---|
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| App registration | `SDAP-BFF-SPE-API` · `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| UAMI | `mi-bff-api-dev` · clientId `5967251e-…` · **principalId `9fd47efb-…`** ← the FIC subject |
| FIC | `mi-bff-api-dev-assertion` · audience `api://AzureADTokenExchange` |
| App Service | `spaarke-bff-dev` in `rg-spaarke-dev` — **UserAssigned only**, plan P1v3 |

**Five UAMIs exist in the dev subscription; `spaarke-bff-identity` is named like the BFF's but is NOT
attached to it. Resolve by resource ID, never by name.**

### Reusable recipe (tasks 031 / 041)

The BFF app registration pre-authorizes the Azure CLI, so this yields a **real delegated user token**:

```bash
az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
```

### Distinguishable auth failure modes (hard-won)

| Condition | Error |
|---|---|
| No MI present (workstation) | `managed_identity_unreachable_network` — fails in **~80 ms**, not a timeout |
| Wrong identity requested | `managed_identity_request_failed` — "No User Assigned … found". **FR-B4 signature: fail loud, never fall through** |
| Wrong/stale **secret** | `AADSTS7000215` — opaque; no hint the value is merely wrong |
| Fresh FIC not yet propagated | `AADSTS70021` — **retry before concluding anything** |
| Slot missing `keyVaultReferenceIdentity` | container exit **134 / SIGABRT** — looks like a crash, is a KV-reference failure |
| Project pins `linux-x64` | `dotnet run` on Windows fails "not a valid application for this OS platform" — **not** a code fault |

---

## Cross-project

| Direction | Item |
|---|---|
| **We owe** `customer-provisioning-orchestration-r1` | **Task 030** before their Wave G-3 — this is why it is next |
| **They owe us** | Model 2 FIC issuer tenancy — `PROVISIONING-CHANGE-REQUEST.md` §9.2. **Still open**; may be structurally impossible (A4 same-tenant rule) |
| **Watch** | PR #293 — `Azure.Identity` 1.17.1→1.21.0 affects `ClientAssertionCredential` |

## Open questions

1. **`Analysis:PromptFlowKey`** — still in use? Task 055.
2. **Model 2 FIC issuer tenancy** — with provisioning; does not gate this project (dev-only, Model 1).
3. **Power BI service-principal profiles under a managed identity** — deferred with DEF-001, **still
   unanswered**, travels with task 040 and gates 041/042.

---

## Recovery commands

```bash
cd c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI
git merge origin/master                     # 1 behind, docs-only
cat projects/spaarke-auth-v4-dataverse-MI/tasks/TASK-INDEX.md
cat projects/spaarke-auth-v4-dataverse-MI/notes/decisions/020-assertion-seam.md   # changes 021's scope
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev-staging.azurewebsites.net/healthz  # 200
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz          # 200
# then: task-execute on tasks/030-fic-provisioning-automation.poml
```

## Blockers

**None.** Task 030 is startable immediately after the merge.
