# Coordination Memo — `dataverse-access-unification-r1` ↔ `spaarke-auth-v4-dataverse-MI`

> **From**: `spaarke-auth-v4-dataverse-MI` research phase (merged PR #783) · **Date**: 2026-08-19
> **To**: `dataverse-access-unification-r1` — intended for incorporation into `design.md`
> **TL;DR**: The two projects are **complementary, not overlapping**. Unification owns the **app-only** Dataverse paths; auth-v4 owns the **OBO/delegated** paths. Landing unification **first** deletes two client-secret consumers outright and spares auth-v4 a double-touch. ADR-028 Amendment A4 (already merged) explicitly blesses unification's MI-only target.

---

## 1. Why this memo exists

Auth-v4's research phase produced an exhaustive `file:line` audit of every credential in the backend ([`CREDENTIAL-INVENTORY.md`](../../spaarke-auth-v4-dataverse-MI/notes/CREDENTIAL-INVENTORY.md)). Two of the classes this project **deletes** appear in that inventory as client-secret consumers. That makes the sequencing between the two projects consequential rather than incidental — and it works in this project's favour.

## 2. What auth-v4 is, in one paragraph

`BFF-API-ClientSecret` is a single secret fanned out across **five config keys** plus a **sixth lowercase Key Vault alias** (`bff-api-client-secret`, used by the Office add-in deploy). Auth-v4 replaces it with a **secret-free confidential credential** — Managed Identity as a Federated Identity Credential (MI-FIC), or a Key Vault certificate. The research established that MI-as-FIC has been **GA since 2025-05-08**, that OBO works with it, and that both platform prerequisites are already satisfied in our tenant. This is now codified as **ADR-028 Amendment A4** (merged).

## 3. The division of labour — no overlap

| | Owns | Credential target |
|---|---|---|
| **This project** | **App-only** Dataverse (`IDataverseService` family) | `DefaultAzureCredential` (UAMI) — per your Phase 1 |
| **auth-v4** | **OBO / delegated** confidential clients | MI-FIC client assertion (or KV cert) |

`DefaultAzureCredential` **cannot perform an OBO exchange** — that asymmetry is exactly why the two projects exist separately, and it is now written into A4. Your target is app-only, so `DefaultAzureCredential` is correct and sufficient for it.

## 4. What unification gives auth-v4 — and what it does *not*

Your Phase 3 deletes **`DataverseWebApiService`** and **`DataverseWebApiClient`**. Your own design notes *"secret-based auth on both"* — the audit confirms it and identifies which secret each uses:

| Deleted class | Secret path | Config key | Flag-gated? | **Behaviour when the secret is absent** |
|---|---|---|---|---|
| `DataverseWebApiService.cs:83` | `ClientSecretCredential` | `Dataverse:ClientSecret` | ✅ Yes — `Graph:ManagedIdentity:Enabled` | With the flag **on** (dev today) it never reads the secret at all → MI |
| `DataverseWebApiClient.cs:44` | `ClientSecretCredential` | `API_CLIENT_SECRET` | ❌ **No** — see §6 | Falls through to `DefaultAzureCredential` (`:50-52`) → MI |

### Correction to an earlier framing

An earlier draft of this memo claimed unification "removes two secret consumers, unblocking auth-v4." **That was wrong, and the table above is why**: both classes already degrade to Managed Identity when the secret is absent. **Neither one blocks removal of `BFF-API-ClientSecret`**, and neither requires auth-v4 code work in either ordering.

What actually blocks the Key Vault secret removal is a different set entirely — the paths with **no working fallback**:

- `DataverseOptions.ClientSecret` — `[Required]` + `ValidateOnStart` ⇒ **startup crash**, independent of any flag
- `GraphClientFactory` — CCA built with *no* credential ⇒ every OBO call fails at runtime
- `DataverseAccessDataSource` — `_cca = null` ⇒ OBO throws ⇒ fail-closed `AccessRights.None`
- `DataverseUserClient` — fail-closed `OboNotConfigured`
- `AgentTokenService` — fails at first token request

**None of those are yours.** So the honest position is: **the two projects are independent — neither blocks the other.**

### The real (narrower) reasons to land unification first

1. **Contention.** Both projects touch `Spaarke.Dataverse` — your own risk table calls it the "highest-contention shared lib." Serialising avoids a merge fight on files one project is deleting and the other is editing.
2. **Wasted effort, small but real.** If auth-v4 went first it would tidy the secret-fallback branches in two classes you then delete. A few lines, not a migration — but pointless churn on a contended file.
3. **T3 scope halves** (§6).
4. **Smaller verification surface** for auth-v4's staged rollout — fewer files to re-verify per environment.

That is a *convenience* argument, not a dependency argument. Size the sequencing decision accordingly.

## 5. What auth-v4 gives unification

**ADR-028 Amendment A4 already sanctions your target architecture.** A4 splits the old, unsatisfiable rule into two:

- **App-only** outbound → `DefaultAzureCredential`, UAMI-pinned, resolved from DI
- **Confidential clients acting as the BFF identity** → MI-FIC assertion or KV certificate; never a secret

Your Phase 1 ("target impl is MI-only, `DefaultAzureCredential`") lands squarely in the first bucket. **No ADR tension, no §6.5 escalation needed on the credential dimension** — your Phase 0 ADR can cite A4 rather than re-litigate it.

One caveat worth carrying into Phase 0: A4 also requires that credentials be obtained from the **shared provider** rather than constructed per call site, and that MSAL confidential clients be **singleton-cached**. Your single-impl target should resolve the DI `TokenCredential` rather than `new`-ing a credential.

## 6. A defect in code you are deleting (do not fix it — just know)

`DataverseWebApiClient.cs:42` **never reads `Graph:ManagedIdentity:Enabled`**. Secret *presence* alone selects the code path, so on dev — where `API_CLIENT_SECRET` is set — it runs on the client secret **despite MI being enabled**. Every other Dataverse path is flag-gated.

This is filed as [#791](https://github.com/spaarke-dev/spaarke/issues/791) item 1 (checkpoint task **T3**), which covers two sites: `DataverseAccessDataSource.cs:53` **and** `DataverseWebApiClient.cs:42`.

**If unification lands first, T3's scope halves** — the `DataverseWebApiClient` half disappears with the class. Worth a note to whoever picks up T3 so the same fix isn't written twice.

## 7. What unification does *not* touch

- **`GraphClientFactory.cs`** — the Graph OBO confidential client. Untouched by this project; squarely auth-v4's. Note you *do* repoint **`GraphModule.cs`** (the DI module) in Phase 3 — a different file, but adjacent enough to mention in a PR description.
- **`DataverseAccessDataSource`** — implements `IAccessDataSource`, not `IDataverseService`, so it **survives** unification. It remains an auth-v4 OBO target (`:59-63`), and it carries the other half of the T3 defect plus a DI-lifetime hazard (it is a **transient** typed HttpClient at `SpaarkeCore.cs:39`, rebuilding an MSAL client per request — [#791](https://github.com/spaarke-dev/spaarke/issues/791) item 2 / task **T4**).

## 8. Recommended sequencing

1. **Interim `dataverse-access-hardening`** — already your stated dependency; unchanged.
2. **This project** — lands the deletions and the MI-only single impl.
3. **auth-v4 implementation** — then migrates the **four surviving** OBO confidential clients: `GraphClientFactory.cs:83-90`, `DataverseAccessDataSource.cs:59-63`, `DataverseUserClient.cs:91-96`, `AgentTokenService.cs:49-53`.

Auth-v4 is currently at design-complete / pre-`design-to-spec`, so this ordering costs it nothing.

**But it is a preference, not a dependency** (§4). If this project slips, auth-v4 proceeds independently — nothing it needs is gated on you. Equally, **do not accelerate on auth-v4's account**: it is not waiting on you. In either order, serialise the PRs and run `/conflict-check` on each, per your own risk table's "highest-contention shared lib" mitigation.

## 9. Concrete asks

1. **Cite A4 in your Phase 0 ADR** rather than re-deriving the credential position; note that app-only → `DefaultAzureCredential` is explicitly sanctioned.
2. **Note the two deleted secret-reading code paths in the Phase 3 PR description** — worth recording as hygiene (less secret-shaped code in the tree), but state it accurately: neither was blocking the Key Vault secret's removal (§4). Don't claim a security win the change doesn't deliver.
3. **Flag the T3 overlap** so the `DataverseWebApiClient` gating fix isn't duplicated.
4. **Resolve the DI `TokenCredential`** in the single impl rather than constructing credentials inline (A4 shared-provider rule).
5. **Ping auth-v4 when Phase 3 merges** so its inventory is re-baselined from 8 confidential-client sites down to the surviving set.

## 10. Source material

| Document | What it gives you |
|---|---|
| [`CREDENTIAL-INVENTORY.md`](../../spaarke-auth-v4-dataverse-MI/notes/CREDENTIAL-INVENTORY.md) | Every credential call site, `file:line`, with flow / config key / gating / fallback |
| [`RESEARCH-FINDINGS.md`](../../spaarke-auth-v4-dataverse-MI/notes/RESEARCH-FINDINGS.md) | Platform research + live tenant verification + corrections to the original seed |
| [`TENANCY-AND-CREDENTIALS.md`](../../spaarke-auth-v4-dataverse-MI/notes/TENANCY-AND-CREDENTIALS.md) | Credential per deployment shape (Model 1 / Model 2) |
| `.claude/adr/ADR-028-spaarke-auth-architecture.md` | Amendment **A4** + transitional exception **E-3** |
| [`quality-followups-execution-checkpoint-2026-08-19.md`](../../../docs/assessments/quality-followups-execution-checkpoint-2026-08-19.md) | Tasks T3 / T4 in full, with done-when criteria |

---

*Prepared from the auth-v4 research phase. Corrections welcome — in particular, if Phase 2's capability porting turns out to touch `DataverseAccessDataSource` (which the audit says it should not), the division of labour in §3 needs revisiting.*

---

## Recipient correction — 2026-08-19 (`dataverse-access-unification-r1`)

> Added by the recipient project after a code-grounded validation pass. Full evidence:
> [`validation-2026-08-19.md`](validation-2026-08-19.md). **Auth-v4 should be notified of items 1–2.**

1. **§4 table + §6 — `DataverseWebApiClient` is NOT deleted by this project.** The memo inherited that scope
   from our `design.md` rather than re-deriving it. The class has **45 references across 16 files**, registered
   singleton in `SpeAdminModule.cs:56` (not `GraphModule`) and consumed across all 4 `Api/SpeAdmin/*Endpoints.cs`,
   all 6 `Api/ExternalAccess/*Endpoint.cs`, `SpeAdminGraphService`, `SpeAuditService`, `SpeDashboardSyncService`,
   `RegistrationDataverseService`, `ScopeManagementService`. It is an independent REST stack; `DataverseWebApiService`
   never consumes it. It is now an explicit **non-goal**.
2. **§6 "If unification lands first, T3's scope halves" — WITHDRAWN.** T3 keeps **both** sites:
   `DataverseAccessDataSource.cs:53` **and** `DataverseWebApiClient.cs:42`. The gating fix must be written once,
   by T3's owner, regardless of what happens to this project.
3. **§8 sequencing — moot, and the conclusion strengthens.** This project is **PAUSED** as of 2026-08-19 (not
   necessary; as scoped, more risk than reward). Per §4/§8's own framing this costs auth-v4 nothing: it was never
   a dependency. **Auth-v4 should proceed independently and immediately.** With T3 no longer halving, even the
   convenience argument for ordering is weaker than the memo stated.
4. **A residual item auth-v4 may want to claim.** No impersonation characterization / negative-canary test suite
   exists today (baseline is one live-gated method). Auth-v4 is about to change the credential underneath
   `DataverseAccessDataSource`'s OBO path — the same suite that would have protected our port would protect that
   migration. We extracted it to the hardening track; auth-v4 is welcome to it.
5. **Everything else verified clean** — all 12 spot-checked `file:line` citations exact; A4's app-only carve-out
   and shared-provider rule confirmed as quoted; the five real `BFF-API-ClientSecret` blockers all confirmed;
   `DataverseAccessDataSource` confirmed to have zero symbol overlap with the capability groups in either
   direction (the §3 division of labour holds, so the memo's closing caveat does not fire). Two immaterial nits:
   `AgentTokenService` throws at first DI resolution rather than "first token request", and the "−1,414 LOC"
   figure measures as net −1,413.
