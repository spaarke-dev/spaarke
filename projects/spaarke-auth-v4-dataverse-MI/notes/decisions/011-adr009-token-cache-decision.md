# Decision record — 011: DI lifetimes, and which token cache is authoritative (ADR-009)

> **Task**: `tasks/011-fix-di-lifetimes.poml` · **Completed**: 2026-08-20 · FULL rigor · FR-A2
>
> ## DECISION: the **application-level** caches stay the sole cross-request token cache.
>
> MSAL's in-process cache is a per-instance optimization. We are **NOT** wiring
> `AddDistributedTokenCache`. Reasons and the consequences we accept are in §4.

---

## 1. The hazard being fixed

`DataverseAccessDataSource` is a transient typed HttpClient (`SpaarkeCore`) and `AgentTokenService`
was registered **scoped** (`AgentModule:24`). Both build an MSAL confidential client in their
constructor, so each resolution produced a **fresh client and threw away its OBO token cache** —
a network token exchange per request.

It gets worse in Phase 2, which is why this is a prerequisite rather than a tidy-up. From task 020
the credential becomes a **Managed-Identity client assertion**, and `ManagedIdentityClientAssertion`
caches its *signed assertion* on the instance. A per-resolution client would therefore also re-mint
an assertion — an **IMDS round-trip on every call**. Shared-client-ness stops being an optimization
and becomes a cost property of the credential itself.

## 2. What was actually wrong with the authored steps

Two of the POML's steps were wrong for the code as it exists. `<steps mode="directional">` permits
adapting them; both deviations are recorded here rather than silently applied.

| Authored step | What was done instead | Why |
|---|---|---|
| *"Apply it to `DataverseAccessDataSource`; change `SpaarkeCore.cs:39` registration accordingly."* | Static client cache applied; **registration left as `AddHttpClient` (transient)** | `SpaarkeCore.cs:39` is `AddHttpClient<DataverseAccessDataSource>`. Promoting a **typed HttpClient** to singleton pins one `HttpMessageHandler` for process lifetime, defeating the handler rotation and DNS refresh `IHttpClientFactory` exists to provide. That trades a token-cache bug for a connection-lifetime bug. `DataverseUserClient` — the canonical reference this task names — is *also* a transient typed HttpClient and solves it with exactly this static cache. The pattern decouples client sharing from DI lifetime, which is the whole point. |
| *(implied)* the app-only credential is part of the hazard | **Half true — corrected at code review.** The MI branch needed nothing; the secret branch needed the same fix as `_cca` | ⚠️ **This row originally read "the app-only path was never rebuilding a credential per request — the hazard is `_cca` only." That was FALSE and is corrected here** (code-review finding W-3). It holds for the **managed-identity** branch: `Program.cs:46` registers `TokenCredential` singleton and injects it. It does **not** hold for the **secret** branch, which does `new ClientSecretCredential(...)` per construction — and since `ClientSecretCredential` caches its token per instance on a *transient* type, that is a fresh `client_credentials` call to Entra on **every authorization check**: exactly the defect class this task exists to fix. Now cached in `SecretCredentialCache` under the same key. Live on local dev with the flag off, and in every contract-test fixture. |

`AgentModule:24` **was** changed, `Scoped` → `Singleton`, as authored. Verified safe: every dependency
is itself singleton (`ITenantCache` → `CacheModule:195`, `IOptions<AgentTokenOptions>`, `ILogger<>`),
and `HttpContext` arrives as a **method parameter** rather than through an injected
`IHttpContextAccessor` — so no scoped state is captured. **The captive-dependency escalation trigger
did not fire.**

`AgentTokenService` **also** got the static cache despite being singleton. Not redundant, for two
reasons: it makes sharing structural rather than contingent on one registration line a future change
could flip, and it is what makes the guarantee testable without a banned test shape (§3).

## 3. Test shape — how sharing is asserted without reflection

The MSAL client is a private field. Comparing it across two instances needs reflection, which is
**ADR-038 ban B8**; resolving twice from a container is **ban B3** (DI-registration test). Both roads
closed, so the assertion is made on the one genuinely observable behaviour:

> **exactly one client is BUILT per (tenant|client|secret-fingerprint), however many instances
> are constructed.**

If any instance built its own client instead of taking the cached one, the count would grow per
construction. That is a real behavioural difference, not a proxy for one. Each type exposes
`ConfidentialClientBuildCountFor(tenant, client, secret)` for this — marked
`[EditorBrowsable(Never)]` and documented as non-contractual, since task 022 relocates it onto
`IClientAssertionProvider`.

Five tests in `tests/integration/seam/Auth/ConfidentialClientSharingSeamTests.cs`: five instances →
**one** client, for each type; plus two negative halves — **two tenants → two clients** (a client
shared *across* tenants would be a cross-tenant token-cache leak, worse than the bug being fixed), and
**a rotated secret → a new client** rather than silent reuse of the stale one.

### Why per-key, and not the process-wide total it started as

The first version asserted a **delta on a process-wide count**, guarded by putting both seam classes in
one xUnit collection. Code review (finding W-2) showed that guard was not sufficient, and proved it
concretely rather than in principle: there is no `xunit.runner.json` and no assembly-level
`CollectionBehavior`, so collections run in parallel by default; ~10,500 tests share this assembly and
that static; and the contract fixtures boot the real `Program.cs` with `TENANT_ID` / `API_APP_ID` /
`API_CLIENT_SECRET` set **without** overriding `IAccessDataSource` — so the first such resolution adds
an entry. Landing inside a delta window would have turned the assertion red once in CI after passing
locally two hundred times.

Counting **per key** is immune: other keys cannot perturb it. It is also strictly stronger — it counts
*builds* rather than *entries*, so a `GetOrAdd` factory that ran twice under contention is visible
instead of silent. The collection is retained as defence in depth, no longer as the load-bearing guard.

**Still deferred to task 060**: these tests prove the cache *is used*; they cannot prove no future
call site *bypasses* it, since a bypassing site simply would not touch the counter. That guard is
source analysis over `ConfidentialClientApplicationBuilder.Create` call sites — the shape ADR-038
sanctions, and the shape task 060 already builds. **This is the second obligation booked onto 060**,
alongside task 010's `_cca`-decoupling guard.

## 4. The ADR-009 decision — stated, because silence was not an acceptable outcome

### What exists today

| Path | Cross-request token cache |
|---|---|
| `GraphClientFactory` (OBO → Graph/SPE — the highest-volume path) | ✅ **Redis** — `GraphTokenCache`, `IDistributedCache`, SHA256(user-token) key, 55-min TTL |
| `AgentTokenService` (OBO → Graph + Dataverse for Copilot) | ✅ **Redis** — `ITenantCache`, tenant-scoped + versioned keys |
| `DataverseAccessDataSource` (OBO → Dataverse, row-level authz) | ❌ **MSAL in-process only** |
| `DataverseUserClient` (OBO → Dataverse, `dataverse.*` chat tools) | ❌ **MSAL in-process only** |

Both options were genuinely available — `Microsoft.Identity.Web` 4.14.2 is referenced and Redis is
wired (`CacheModule:122`), so `AddDistributedTokenCache` is a modest wiring change, not a project.
It is declined on merit, not on effort.

### Decision: **application-level caches are authoritative. MSAL's cache is per-instance.**

1. **It is a security-posture change, not a caching change.** A serialized MSAL cache blob carries
   **refresh-token-bearing** cache state. The existing app-level caches store a single short-lived
   **access token**. Putting refresh material in Redis needs its own encryption-at-rest, eviction and
   ADR-015 logging review. That decision must not be made as a side effect of a DI-lifetime bugfix.
2. **The paths where volume justifies it already have it.** Graph/SPE and the Copilot agent — the two
   high-traffic OBO paths — are already Redis-cached at the application layer. ADR-009's Redis-first
   requirement is satisfied where it earns its keep.
3. **A miss is usually a performance event.** MSAL falls back to a network OBO exchange, so more
   instances means more exchanges, not different answers. **Stated precisely, though**: sustained
   excess exchanges are the direct input to Entra throttling (`AADSTS50196` / 429), and on a
   **fail-closed authorization path** a throttled token request degrades to `AccessRights.None` —
   a correctness-visible outcome, not merely a slow one. So the honest claim is *"a miss is a
   performance event until throttling starts, and the throttling threshold is the real boundary"* —
   which is exactly why the revisit trigger below names those error codes.
4. **Scope.** The task's own constraint is *"lifetime only — do not change the credential TYPE."*
   Distributing MSAL's cache is neither.

### Consequences accepted — recorded, not silent

- `DataverseAccessDataSource` caches OBO tokens **in-process only**. On scale-out to N instances,
  worst case is **N exchanges per user per token lifetime instead of 1**, and each instance starts
  cold after a restart. `DataverseUserClient` has the same property, but it is **outside this diff** —
  noted as context, not accepted on its behalf; it is task 022's to carry when it migrates.
- **This fix creates a memory consequence it must own.** Before the change, a per-resolution client
  meant MSAL's OBO cache was garbage-collected along with it. Now a **process-lifetime** client
  accumulates roughly one cache entry per (user-assertion hash × scope) for the token's lifetime,
  **unbounded and un-evicted**. That is the standard trade for a shared confidential client and it is
  the right trade — but it is a *new* growth surface introduced here, and it was missing from the
  first draft of this record. Raised by `adr-check` finding **W1** at the task 011 quality gate.
- **Dev is single-instance, so none of this is observable today.** That is why it is written down:
  the cost is deferred, not absent.

### Revisit trigger, and the preferred remedy when it fires

Trigger — **any** of:

1. The BFF runs **more than one instance** *and* OBO exchange volume becomes measurable — latency on
   authorization checks, or Entra throttling (`AADSTS50196` / 429s).
2. **Working-set growth attributable to the MSAL in-process cache on a single instance.** This is the
   failure mode the fix newly enables (see the memory consequence above), so it fires independently
   of instance count.

   **Remedy is a different lever from the distributed-cache question answered above** — do not
   conflate them. Bound the shared client's own cache with `AddInMemoryTokenCaches` +
   `MsalMemoryTokenCacheOptions` (size limit + sliding expiration); `Microsoft.Identity.Web` 4.14.2 is
   already referenced, so this needs no new package. Microsoft ships those options precisely because
   MSAL's *default* in-memory cache is unbounded — which is fine for a short-lived client and is
   exactly what stops being true when the client becomes process-lifetime.

The remedy then is **an app-level distributed cache on those two paths, mirroring `GraphTokenCache`**
— *not* distributing MSAL's cache. Same benefit, and it keeps refresh-token material out of Redis.
Recording the preferred remedy now is the point: whoever hits this should not have to re-derive that
the obvious-looking `AddDistributedTokenCache` is the worse of the two answers.

**Escalation trigger 2 fired and was handled as prescribed** — the POML says *"if the ADR-009
decision implies multi-instance token-cache work beyond this project's dev-only scope, record it and
escalate rather than building it."* Recorded above; nothing built.

## 5. An observation for task 020 / 022 (not acted on here)

There are now **three separate per-class static client caches** — `DataverseUserClient`,
`DataverseAccessDataSource`, `AgentTokenService` — so one process can hold up to three confidential
clients for the *same* (tenant|client), each with its own token cache.

Not consolidated here, deliberately: the task constraint says copy the existing shape and *"do not
invent a second caching shape"*, and task **020** is about to introduce `IClientAssertionProvider` as
the shared credential seam with task **022** migrating all these call sites onto it. Consolidating
now would pre-empt that design and then be immediately reworked. **Booked as input to task 020**: the
provider is the natural home for a single shared client cache.

## 6. Acceptance criteria

Criteria 1–2 were **amended in the POML during execution** per CLAUDE.md §8.5's closed-set rule. As
authored they read *"resolving X twice **from DI** yields the same underlying CCA instance"* — and
neither half is provable: resolving from a container is ADR-038 ban **B3**, comparing the private
`_cca` field across instances is ban **B8**. Rather than mark them ✅ against different evidence, the
criteria now state what is provable. The substitute is stronger — it counts **builds per key**, so it
also catches a `GetOrAdd` factory that ran twice. (Raised as code-review finding S-4.)

| # | Criterion (as amended) | Result |
|---|---|---|
| 1 | N constructions of `DataverseAccessDataSource` under one key build **exactly one** client, asserted per-key | ✅ 5 → 1 |
| 2 | N constructions of `AgentTokenService` under one key build **exactly one** client | ✅ 5 → 1 |
| 3 | Negative: two tenants → two clients (sharing is keyed; token caches cannot cross a tenant) | ✅ both types |
| 4 | Negative: a **rotated secret** builds a new client rather than reusing the stale one | ✅ added after code-review W-1 |
| 5 | The ADR-009 decision is written down with its reason | ✅ §4 — consequences, two revisit triggers, preferred remedy per trigger |
| 6 | No behaviour change beyond token-cache reuse; suite stays green | ✅ §7 |
| 7 | Negative: no captive-dependency error at startup | ✅ §7 — verified empirically |
| 8 | Negative: OBO still fails CLOSED for an unauthorised user | ✅ no authorization logic touched; the `_cca == null` fail-closed branch is unchanged. The client is now shared, not differently scoped |
| 9 | Publish size reported; no new HIGH CVE | ✅ §7 |

## 7. Verification

Final numbers, after the Step 9.5 quality-gate fixes (§9) were applied:

| Check | Result |
|---|---|
| Build | ✅ 0 errors, 7 pre-existing obsolete-API warnings |
| Seam tests (`Seam.Auth`) | ✅ **13 passed** — 8 existing + 5 new, 0 reflection |
| `ExternalAccess` contract tests | ✅ **240/240** (was 13 failing — see §8) |
| Full BFF suite | ✅ **10,549 passed / 0 failed** / 97 skipped |
| ArchTests | ✅ **36/36** (was 38 — `GodClassGuardTests` retired on master, not by this task) |
| `LayerDependencyTests` FR-14 | ✅ passes; **zero diff across every `.csproj`** — no ProjectReference, no package |
| ADR-010 registration count | ✅ flat — one `AddScoped` became `AddSingleton`; nothing added |
| Captive dependency | ✅ **verified empirically** — see below |
| Publish size | **43.68 MB** compressed **incl. PDBs** — +0.01 MB vs the pre-task 43.67 (the SHA-256 keying); **1.28 MB BELOW** the 44.96 MB net10 baseline, ceiling 60. Stating the PDB convention per CLAUDE.md §10 |
| CVE scan | ✅ no vulnerable packages |

### How criterion 5 was actually verified

Booting the app was attempted first and blocked for environmental reasons worth recording: the project
pins `<RuntimeIdentifier>linux-x64`, so `dotnet run` on a Windows workstation fails with *"not a valid
application for this OS platform"* — which looks like a crash and is not one. Rebuilding with
`-p:RuntimeIdentifier=win-x64` then surfaced a chain of missing local config
(`SpeAdmin:KeyVaultUri` → `Redis:AllowInMemoryFallback` → `CosmosPersistence:Endpoint`), all thrown during
module registration, i.e. **before** the container is built and validated. None of it is a DI signal.

The contract-test fixtures are no help either — they set `ValidateScopes = false` / `ValidateOnBuild = false`
(32 files do), so a green suite does not prove the absence of a captive dependency.

Verified instead with a **throwaway** check (run, then deleted — a committed version would itself be
ADR-038 ban **B3**): build a container over `AddAgentModule` with `ValidateScopes = true` and
`ValidateOnBuild = true` — the same validation ASP.NET Core enables by default in Development — then
resolve `AgentTokenService` from the root provider and from a child scope. Both succeeded and returned the
same instance. **No captive dependency.** Consistent with inspection: all three constructor dependencies
are singletons (`ITenantCache` → `CacheModule:195`, `IOptions<>`, `ILogger<>`), and `HttpContext` arrives
as a method parameter rather than through an injected `IHttpContextAccessor`.

## 8. A regression from task 010, found and fixed here

Running the **full** suite (rather than only the seam tests) surfaced **13 failing `ExternalAccess`
contract tests**. They were not caused by task 011 — a stash-and-rerun on the clean baseline reproduced
the same 13 — but they were not pre-existing on master either. **They were task 010's.**

Cause: `StubDataverseWebApiClient` (`tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs`)
passed `Dataverse:ClientId` / `:ClientSecret` / `:TenantId` — keys `DataverseWebApiClient` never reads (it
reads `API_APP_ID` / `API_CLIENT_SECRET` / `TENANT_ID`). It worked only because the old constructor
**silently fell through to `DefaultAzureCredential`** when the secret was absent. Task 010 replaced that
silent fallback with fail-fast validation — deliberately, since credential-selection-by-accident is the
exact defect FR-A1 exists to fix — so the stub's placeholder config finally failed loudly.

Fixed by setting `Graph:ManagedIdentity:Enabled = true` on the stub, which takes the MI branch and needs no
credential. The double overrides every virtual seam and issues no HTTP, so it only ever needed the base
constructor to succeed. Full suite is now **10,548 / 0**.

**Process lesson, recorded because it generalises**: task 010 verified with the targeted seam tests, the
build, publish size and CVE scan — but not the full suite — and shipped a 13-test regression. Any change
that converts a **silent fallback into fail-fast** alters behaviour for every caller that was relying on
the fallback, and callers relying on a silent fallback are by definition not visible at the change site.
**Such a change requires a full-suite run, not a targeted one.**

## 9. What the Step 9.5 quality gates changed

Both gates ran as parallel sub-agents. **`adr-check`: 0 violations, 8 warnings. `code-review`: 0
critical, 7 warnings, 17 suggestions — "approve with changes".** Recorded because three findings
changed the code or corrected something false, rather than merely annotating it.

| Finding | What it caught | Action |
|---|---|---|
| **code-review W-3** | §2 and the `SpaarkeCore` comment claimed *"the app-only path was never rebuilding a credential per request."* **False.** True of the MI branch only — the `else` branch does `new ClientSecretCredential(...)` per construction on a transient type, i.e. a fresh `client_credentials` call to Entra on **every authorization check**. The same defect class this task exists to fix, left in place and documented as absent, in a comment task 020 would inherit as ground truth | Claim corrected in both places; credential now cached in `SecretCredentialCache` |
| **code-review W-1** | The cache key was `(tenant\|client)`, omitting the secret. MSAL binds the credential at `Build()` for the client's lifetime, so after a rotation the cache silently returns a client built with the **stale** secret — presenting as `AADSTS7000215` on OBO while app-only keeps working, "fixed" by a restart nobody can explain | Key now includes a **SHA-256 fingerprint** of the secret (never the raw secret — that would widen its memory-dump surface and leak through any key-listing diagnostic). New rotation test locks it in |
| **code-review W-2** | The count-delta assertion was genuinely flaky, proven concretely: no `xunit.runner.json`, no assembly-level `CollectionBehavior`, ~10,500 tests in one assembly, and contract fixtures that boot real `Program.cs` and **do** resolve `IAccessDataSource`. The `[Collection]` only serialised two named classes | Replaced with a **per-key build count** — immune to other keys, and additionally detects a `GetOrAdd` factory that ran twice. Collection retained as defence in depth |
| **adr-check W2** | The three-cache consolidation was booked as **prose in a notes file**, and task 020's POML did not carry it. Would have become a standing ADR-028 A4 violation with no owner | Now a constraint **and** acceptance criterion on **both 020 and 022**, a row in `TASK-INDEX.md`, and a time-boxed path-A row in `spec.md`'s ADR Tensions |
| **adr-check W1 / code-review W-4** | §4 argued the trade-off on hit rate and Redis posture, never on **the memory consequence the fix itself creates** — a process-lifetime client accumulates an unbounded MSAL cache the per-request client used to release | Added to accepted consequences and as a **second revisit trigger**, with `MsalMemoryTokenCacheOptions` named as the remedy (a different lever from distribution) |
| **code-review S-13** | The "stays transient" rationale cited only handler rotation. The decisive reason is that this type holds **mutable per-instance auth state** (`_currentToken`, the `Authorization` header) — a singleton is a **data race that can bleed a token between users**. A reader could otherwise answer "`PooledConnectionLifetime` solves that" and introduce it | Added as the decisive reason |
| **code-review W-6** | The stub fix bound the double to the MI branch — the branch tasks 020/022/033 are about to rewrite, so it would break a third time | `DataverseWebApiClient` given the optional `TokenCredential? credential = null` its sibling already had; the double now needs **no** credential config |
| **code-review S-4** | Acceptance criteria 1–2 said *"resolving twice from DI"*, which is **not provable** — container resolution is ban B3, field comparison is ban B8 | POML criteria **amended** to what is provable rather than ticked against different evidence (§6) |
| **code-review S-14** | `ADR-010`'s example showed `AddSingleton<IAccessDataSource, DataverseAccessDataSource>()` — not what the code does, and dangerous to copy given the data race above | ADR corrected in place |

Declined, with reasons: **S-7** (emit the diagnostic as an OTel gauge) — `Spaarke.Dataverse` references only
`Logging.Abstractions`, so wiring a Meter is a materially wider change than the finding warrants, and task 022
relocates the member anyway; marked `[EditorBrowsable(Never)]` + "non-contractual" instead. **S-8** (escape the
`|` separator) — both components are config-sourced GUIDs and the third is fixed-width hex, so the collision is
unreachable; consistency with the `DataverseUserClient` reference shape matters more with consolidation pending.
**S-12** (extract the constructor) — real, but it is task 022's seam to cut, not this task's.

The generalisable lesson from §8 was promoted out of this note into
[`.claude/FAILURE-MODES.md` **AP-7**](../../../../.claude/FAILURE-MODES.md#ap-7-converting-a-silent-fallback-into-fail-fast-verified-with-targeted-tests-only),
per code-review's observation that it belongs somewhere more durable than a task note.
