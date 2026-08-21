# Task 021 — ordered credential selection: config keys + decisions

> **Status**: IMPLEMENTED 2026-08-21. This file was written as a design ahead of implementation and is
> now the outcome record. Where implementation contradicted the design, the design is corrected and the
> contradiction is kept on the record rather than tidied away.

---

## 0. Verified preconditions (checked, not assumed)

| Fact | Evidence |
|---|---|
| `Spaarke.Dataverse` already references `Microsoft.Identity.Client` **4.87.0** | `Spaarke.Dataverse.csproj:17` |
| ⇒ an MSAL-typed member in a contract declared there adds **no** package reference — **FR-14 unaffected** | same; `LayerDependencyTests` 36/36 green after the change |
| All four MSAL error codes the fall-through table needs exist in 4.87 | enumerated from the shipped `Microsoft.Identity.Client.xml`, not recalled |
| `.WithClientAssertion(Func<AssertionRequestOptions, Task<string>>)` exists in 4.87 | same source |
| `ADR010_DITests` cannot see this seam | scans `typeof(Program).Assembly`; `IConfidentialClientProvider` is declared in `Spaarke.Dataverse`. Ceiling left at 153, as at task 020 |

## 1. The POML's certificate instruction — resolved by extraction

The task said *"the certificate branch reuses the proven `CiamGraphClientFactory` KV PFX load … Do not
write a second certificate loader."* `LoadCertificateAsync` was a **`private` instance method** closing
over that class's `_secretClient` and `_certificateName`, so it could not be called from the selector:
"reuse it" and "don't write a second one" could both hold only by **extracting** it.

Done: [`KeyVaultCertificateLoader.cs`](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/Auth/KeyVaultCertificateLoader.cs)
holds the method body verbatim; `CiamGraphClientFactory.LoadCertificateAsync` is now a one-line delegation
to it. The extraction is behaviour-preserving **by construction** — the shared helper *is* that method's
body — which matters because `CiamGraphClientFactory` was already A4-compliant and was not in this task's
`<relevant-files>`.

Two properties were preserved deliberately, because they are why the original counts as *proven*:
`X509KeyStorageFlags.EphemeralKeySet` (private key in memory only, never on disk) and the
`FormatException` → "verify it is a Key Vault **certificate**, not a plain secret" diagnostic, which
names the single likeliest misconfiguration on that path.

## 2. What was built

| File | Role |
|---|---|
| `Spaarke.Dataverse/IConfidentialClientProvider.cs` | NEW — the client-level contract |
| `Sprk.Bff.Api/Configuration/CredentialSelectionOptions.cs` | NEW — `CredentialKind` enum, options, validator |
| `Sprk.Bff.Api/Infrastructure/Auth/OrderedCredentialClientProvider.cs` | NEW — selection, the ONE cache, the fall-through predicate, the negative memo |
| `Sprk.Bff.Api/Infrastructure/Auth/KeyVaultCertificateLoader.cs` | NEW — extracted from `CiamGraphClientFactory` |
| `Sprk.Bff.Api/Infrastructure/Graph/CiamGraphClientFactory.cs` | MODIFIED — delegates to the extracted loader |
| `Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` | MODIFIED — options + validator + provider registration |
| `tests/integration/seam/Auth/CredentialOrderingSeamTests.cs` | NEW — 18 tests, all passing |

## 3. Config keys (the Phase 3 runbook surface)

```
Graph__Credentials__Order__0                  = ManagedIdentityFederated
Graph__Credentials__Order__1                  = ClientSecret
Graph__Credentials__KeyVaultCertificateName   = <kv cert name>   # only if KeyVaultCertificate is listed
Graph__Credentials__NegativeCacheSeconds      = 10               # optional, 0..120
Graph__Credentials__FailuresBeforeSuppression = 2                # optional, minimum 2
```

**Rollback = reorder `Order` + restart.** A restart is acceptable under NFR-06; a redeploy is not. The
escalation trigger did **not** fire.

### 3.1 Why the canonical default lives in `AuthorizationModule`, not on the options property

Two independent reasons, both discovered during implementation:

1. **The configuration binder merges into an existing collection.** With `Order` pre-populated to the
   canonical default, an operator narrowing it to `[ManagedIdentityFederated]` — *precisely the edit that
   proves the secret is unused* — would silently get the trailing `ClientSecret` back. The secret they
   were eliminating would still be live, and nothing would say so. `Order` therefore starts **empty**.
2. **An explicitly empty list must fail fast** (acceptance criterion 3). Defaulting inside the options
   class would make "empty" unreachable and would choose a credential for an operator who deliberately
   blanked the list to force the decision.

The default is applied in the DI module **only when the section is absent**, so every existing
environment and test fixture boots unchanged. That bound is deliberate: converting a silent fallback into
fail-fast has unbounded blast radius ([FAILURE-MODES AP-7](../../../../.claude/FAILURE-MODES.md)), and
this project already shipped that regression once at task 010.

> ⚠️ There is **no `appsettings.json`** in the BFF — only `appsettings.template.json`,
> `appsettings.Testing.json` and per-environment templates. A design that relied on shipping the
> canonical order in `appsettings.json` would have failed startup everywhere. Checked, not assumed.

## 4. The contract is ASYNC — and that is a real consequence for task 022

Selection must **prove** a credential before binding it: mint the assertion, fetch the certificate. It
cannot be deferred to first token acquisition, because by then the credential is bound into the built
client and MSAL surfaces the failure as a failed *token request* — far too late to fall through, and on
the OBO path that is every user at once.

```csharp
Task<IConfidentialClientApplication> GetClientAsync(string tenantId, string clientId, CancellationToken ct = default);
```

**Consequence booked onto task 022**: all four call sites currently build their confidential client in a
**constructor**, which cannot await —
`DataverseAccessDataSource:225`, `AgentTokenService:98`, `DataverseUserClient:91`, `GraphClientFactory:83`.
They must move to lazy first-use construction. `CiamGraphClientFactory.GetOrCreateAppAsync` — a
`SemaphoreSlim` guarding a one-time build — is the in-repo precedent to copy. This is not optional
polish; it is the shape the async contract forces.

**This is not the banned availability probe.** The constraint forbids an `IsAvailable` member because a
probe is a second network call racing the real one. The MI-FIC proof calls `GetAssertionAsync` **once**,
and `ManagedIdentityClientAssertion` caches the signed assertion until expiry, so the very same assertion
is what MSAL's callback then returns. It is the *first* call, not a duplicate of one.

## 5. Criterion 5 (Q4) — DECIDED: `IClientAssertionProvider` does **not** widen

Task 020 booked this as a decision 021 must make before six call sites bind at 022. **The question
dissolves once selection moves to the client level**, and that is a better outcome than either answer to
it as posed.

Q4's premise was: a Key Vault certificate assertion is a self-signed JWT whose `aud` must be the token
endpoint and whose `iss`/`sub` must be the client id — exactly what the narrow contract drops — and
`ClientAssertionProviderBase` caches options-blind, which is a *correctness* bug for a certificate.

That premise only holds if a certificate is implemented **as an `IClientAssertionProvider`**. It is not.
The certificate branch calls `.WithCertificate(x509)` and MSAL constructs the assertion itself, deriving
`aud`/`iss`/`sub` from the client id and authority it already has. So:

- `IClientAssertionProvider` stays narrow, MSAL-free, and **MI-FIC-only** — the one credential that
  genuinely *is* an assertion. Its options-blind caching stays correct because MI-FIC is the only
  implementation it will ever have.
- No Spaarke-owned request record (`ClientId`, `TokenEndpoint`, `TenantId`) is needed.
- Task 022 binds call sites against `IConfidentialClientProvider` / the concrete provider, not against a
  widened assertion contract.

**Recorded so the next author does not reopen it:** the widening was avoided, not deferred.

## 6. Fall-through: ONE predicate, allowlist, deny-by-default

`OrderedCredentialClientProvider.IsFallThroughEligible(MsalServiceException)` — public static, one place,
one test.

| MSAL error code | Decision | Why |
|---|---|---|
| `managed_identity_unreachable_network` | **fall through** | no IMDS route — ordinary local dev |
| `managed_identity_all_sources_unavailable` | **fall through** | same class |
| `managed_identity_request_failed` | **FAIL LOUD** | IMDS reachable, identity absent/wrong — the **FR-B4** signature |
| `user_assigned_managed_identity_not_supported` | **FAIL LOUD** | deployment-shape error, not an environment one |
| *anything else, including future MSAL codes* | **FAIL LOUD** | allowlist, not denylist |

The allowlist choice is load-bearing: a denylist would silently grant fall-through to unknown future
error codes, and "unknown error ⇒ quietly downgrade to the secret" is the wrong default for a credential
downgrade.

Key Vault failures split the same way: 403/404 mean the vault answered and the named certificate is
missing or inaccessible — a misconfiguration, so **fail loud**. Throttling, timeouts and 5xx fall through.

**"Not configured" and "configured but broken" are different answers.** Not configured returns `null` and
falls through; broken throws. Collapsing them would reintroduce FR-B4 through the back door — a wrong
certificate name would read as "this environment does not use certificates" and quietly select the secret.

## 7. Negative cache — and automatic recovery

Task 030 measured Entra **flapping** for ~2 minutes after a FIC is created or changed: successes and
failures interleaved as replicas converge, returning `AADSTS70025`
([030-fic-automation.md §11](030-fic-automation.md)). Two rules follow, and both are enforced by the
validator rather than left to convention:

- **TTL in seconds, not minutes** (`NegativeCacheSeconds`, default 10, capped at 120).
- **A single failure must not demote** (`FailuresBeforeSuppression`, minimum 2, default 2).

Implementation added a third property the design had not stated, and it is the one that actually closes
the hazard: **the selection itself expires**. When a lower-priority credential wins, the cached selection
is valid only until the suppression on the skipped higher-priority credentials lifts; after that the next
call re-evaluates from the top. Without it, the first transient MI-FIC failure would pin the process to
the fallback **secret** until someone restarted it — a permanent silent downgrade produced by a
ten-second blip. Recovery is now automatic and bounded in seconds.

## 8. Criterion 8 — a secret above a secret-free credential is logged, not rejected

ADR-028 A4 says never promote a secret above a secret-free credential, which read alone argues for
rejecting the configuration outright. **Rejecting it would be wrong here**, and the reason is not
leniency: the ordered list *is* this project's rollback mechanism, and the rollback of interest is
precisely *"put the secret back on top because MI-FIC is failing in production"*. Refusing to start in
that configuration disables the emergency exit at the one moment it is needed — on the OBO path, which
fails closed for every user simultaneously.

So the deviation is **permitted and made loud**: `LogError` at construction naming A4 and the offending
order. A temporary rollback that quietly becomes the permanent state is exactly how the secret survived
three prior audits; the Phase 6 forcing functions (060/061) are what catch it if it does.

The criterion allows either ("rejected **or** logged as a misconfiguration"). This is the deliberate half.

## 9. The ONE cache

Keyed `(tenant | client | kind | credential-fingerprint)`. Every part is load-bearing:

- **`kind`** — MSAL binds the credential at `Build()`; a client built on MI-FIC and one built on the
  secret are different objects. Omitting it would hand a rollback the pre-rollback client.
- **`fingerprint`** — same binding. A `(tenant|client)`-only key silently reuses a client built with a
  **stale secret** after rotation, presenting as `AADSTS7000215` on OBO while app-only keeps working
  (task 011 finding W-1). Preserved deliberately, and asserted by a rotation test.

The secret is fingerprinted (SHA-256, 16 hex chars), never keyed raw — a raw secret in a dictionary key
widens its memory-dump surface and leaks through any future key-listing diagnostic.

**Certificate caveat, stated rather than discovered later**: the certificate fingerprint is the cert
**name**, not its thumbprint, because the key must be computable without a Key Vault round trip on the
cache-hit path. So rotating a certificate under the same name needs a process restart. That is exactly
how `CiamGraphClientFactory` already behaves (it caches its built client for the process lifetime), so
this is parity with the certificate exemplar, not a new limitation.

Instance state, not `static`: the provider is a singleton and is injected, so the constraint that forced
`static` on the three per-class caches (their owning types are transient) does not apply. Task 022
collapses those three onto this one, which is what **closes task 011's time-boxed A4 exception**.

## 10. Secret resolution precedence — and the one key deliberately excluded

The four call sites read *different* keys today for the same app registration. The provider centralises
the lookup, canonical first:

```
AzureAd:ClientSecret  →  API_CLIENT_SECRET  →  AZURE_CLIENT_SECRET
```

**`AgentToken:ClientSecret` is deliberately excluded.** It is options-bound rather than raw configuration
and nominally describes the same app registration, but silently folding it into this precedence could
change which secret `AgentTokenService` presents. **Booked onto task 022**, where that call site is
migrated and the change is observable.

## 11. Verification

| Check | Result |
|---|---|
| `CredentialOrderingSeamTests` | **18 / 18 pass** |
| BFF build | 0 errors (7 pre-existing `CS0618` warnings, untouched) |
| ArchTests (incl. `LayerDependencyTests` FR-14, `ADR010_DITests`) | **36 / 36** |
| Full BFF suite | see §12 |
| Publish size | see §12 |
| `dotnet list package --vulnerable --include-transitive` | see §12 |

### What the tests deliberately do NOT cover, and why

- **A live MI-FIC mint.** It cannot be deterministic across hosts: a workstation cannot route to IMDS at
  all, whereas GitHub-hosted runners are Azure VMs where IMDS *is* reachable but carries no matching
  identity — the fail-loud case. Pinning either would pass in one environment and fail in the other. The
  stub exercises each row of the fall-through table exactly once instead, which is the entire reason the
  decision was consolidated into a single predicate.
- **The Key Vault certificate load.** Needs a live vault; a faked `SecretClient` would assert the Azure
  SDK's behaviour rather than ours. Covered only as far as configuration validation. The extracted loader
  is behaviour-preserving by construction, and the certificate path is on no live deployment — A4 records
  that the certificate alternative was explicitly not taken.

## 12. Gate results

| Gate | Result |
|---|---|
| `CredentialOrderingSeamTests` | **20 / 20 pass** |
| Full BFF suite | **10,572 / 0** (97 skipped) — see §14 for the one flake observed |
| ArchTests (`LayerDependencyTests` FR-14 + `ADR010_DITests`) | **36 / 36** |
| BFF build | 0 errors (7 pre-existing `CS0618`, untouched) |
| Publish size | **44.98 MB** compressed incl. PDBs, 215 files (`Compress-Archive -CompressionLevel Optimal`, framework-dependent linux-x64) vs the 44.96 MB net10 baseline = **+0.02 MB**. Ceiling 60 MB |
| CVE | `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages** |

**No package was added to the BFF.** The only `PackageReference` added anywhere is
`Microsoft.Extensions.TimeProvider.Testing` on the *test* project (`FakeTimeProvider`, mandated by
tests/CLAUDE.md over `Stopwatch` / `DateTime.UtcNow`). That is why the publish delta is noise-level.

> **Measurement method stated explicitly** because open owner decision #3 records that CLAUDE.md §10's
> baseline is not method-qualified, and two honest measurements of one tree differed by ~1.3 MB purely on
> compression method — more than the +5 MB escalation threshold.

## 13. Findings from the quality gates (self-review), and what changed

Two real defects were found reviewing this task's own output. Both are fixed; both are recorded because
neither was in the design.

**F-1 — the certificate branch leaked a private-key handle.** `GetOrAdd` does not run its factory on a
cache hit, so a certificate freshly materialised from Key Vault was orphaned **undisposed** whenever a
re-evaluation found the client already cached. Because re-evaluation is *time-driven* (§7), this would
have leaked one ephemeral key handle per suppression window — slow, silent, and only on the credential
path. Fixed: the descriptor carries the disposable and releases it exactly when the factory did **not**
run. Disposing unconditionally would be the opposite bug — MSAL owns the certificate for the client's
lifetime, and signing would break.

**F-2 — "fails fast at startup" was not actually proven.** The validator was tested in isolation, which
establishes that the *rules* are right but says nothing about whether `ValidateOnStart` ever reaches
them. A validator that is never invoked passes its own unit test perfectly while the BFF boots on a
misconfigured credential order — the worst outcome available here, because the service would come up
healthy and authenticate as something nobody chose. Fixed by extracting the registration into
`AuthorizationModule.AddCredentialSelection(...)` and booting a real host against it: one test asserts an
invalid order throws `OptionsValidationException` at `StartAsync`, and its **negative control** asserts
that an absent section still boots on the canonical order. That control is the AP-7 bound and is not
optional — there is no `appsettings.json` in the BFF, so *every* environment and fixture in this repo has
no `Graph:Credentials` section.

## 14. Observed flake in a task-020 test — NOT caused by this task, and not dismissed

`ClientAssertionProviderSeamTests.Provider_WhenNoManagedIdentityIsReachable_FailsAtFirstCall_WithACatchableMsalError`
failed **once in two full-suite runs** (run 1: 1 failed / 10,571 passed; run 2: **0 failed / 10,572
passed**), and passes consistently in isolation.

Task 021 does not touch `ManagedIdentityAssertionProvider`, and the new provider is never *resolved*
during the suite (no consumer binds until task 022), so it never mints an assertion. The mechanism is in
the task-020 test itself: it performs a **live network attempt to IMDS** and asserts the error code is
one of three. Its own doc comment already concedes host-dependence — a workstation cannot route to
`169.254.169.254`, whereas GitHub-hosted runners are Azure VMs where IMDS *is* reachable. Under
full-suite load a fourth outcome evidently occurs.

**Not fixed here, deliberately**: the failing run was captured at quiet verbosity, so the actual error
code was not recorded, and fixing a flake blind is how a real signal gets suppressed. **Booked onto task
060** with the reproduction condition (full suite, not isolation) so it is diagnosed with the code in
hand. This matters more than an ordinary flake: it is a test of the credential seam, and an auth gate
that cries wolf is one people learn to re-run rather than read.

## 15. ADR compliance

| ADR | Assessment |
|---|---|
| **ADR-028 A4 / E-3** | Compliant — see §15.1, the one that needed thought |
| **ADR-010** | 4 service descriptors added, via a feature-module extension method (not inline in `Program.cs`). The cross-assembly 1:1 seam is invisible to `ADR010_DITests` (verified at task 020, re-verified here: 36/36). Ceiling **not** raised |
| **ADR-038** | No banned shape: no `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check test, no reflection. Coverage sits at `tests/integration/seam/**` |
| **ADR-009** | See §15.2 |
| **ADR-027** | The cache and the negative memo are keyed by `tenantId`; nothing crosses a tenant boundary |
| **FR-14** | No `ProjectReference` added to `Spaarke.Dataverse`, and no `PackageReference` either. `LayerDependencyTests` green |
| **ADR-003 / ADR-008** | N/A — no endpoints, no filters, no seams changed |

### 15.1 The `.WithClientSecret` question — consolidation, not expansion

Project CLAUDE.md says **"never add a new `.WithClientSecret` site"**, and this task adds one, in
`OrderedCredentialClientProvider.AcquireAsync`. That deserves a direct answer rather than silence.

It is **Path C (comply)**, not an exception, for two reasons:

1. The task's own constraint *requires* the branch — "order is MI-FIC, then KV certificate, then dev
   secret". A selector with no secret branch cannot express the rollback E-3 exists to preserve.
2. The rule's purpose is to stop secret usage **spreading**. This does the opposite: task 022 removes the
   four existing `.WithClientSecret` sites and routes them all through this one, so the count goes
   **down**; task 033 then deletes the branch entirely.

**But the intermediate state is real and must not be glossed:** between tasks 021 and 022 the count is
temporarily **+1** (five sites, not four). **Booked onto task 060**: FR-F1's ArchTest must allowlist this
single binding point *with its reason*, and FR-F2's census must count it as the sanctioned consolidation
point rather than reading it as expansion. Without that entry, the forcing function this project exists
to leave behind would fail on the very mechanism that makes the secret removable.

### 15.2 ADR-009 (Redis-first) — the negative memo is deliberately process-local

ADR-009 prefers Redis for cross-request caching, and the negative memo is cross-request. It is
nevertheless **in-memory and process-local on purpose**:

- **A credential failure is a statement about *this instance's* environment**, not about the tenant. One
  instance failing to reach IMDS says nothing about another. Sharing the memo would let a single
  instance's transient failure suppress the secret-free credential **fleet-wide** — precisely the silent
  downgrade §7 exists to prevent, amplified.
- **It would put Redis on the authentication path.** A Redis outage would then degrade OBO, which fails
  closed for every user at once (NFR-03). Adding a dependency to the credential path in order to cache a
  ten-second negative signal is a bad trade in every direction.

Consistent with task 011's ADR-009 decision for the MSAL client cache
([`011-adr009-token-cache-decision.md`](011-adr009-token-cache-decision.md)).

## 16. Placement Justification (CLAUDE.md §10) + Component Justification (§11)

**Placement** — the ordered selector belongs in the BFF and the contract belongs in `Spaarke.Dataverse`.
The BFF is the only assembly that may hold the credential (A4 gives it the managed identity and the Key
Vault client); `Spaarke.Dataverse` is the base layer and cannot reference it (FR-14), so the contract must
be declared downward and the implementation injected. No other placement compiles.

**Component justification (the three questions):**

| Question | Answer |
|---|---|
| **Existing** | `IClientAssertionProvider` (task 020) is the closest thing. Verified by reading it rather than assumed: it returns `Task<string>`, and only MI-FIC *is* an assertion — a certificate and a secret have none to return |
| **Extension** | No. Widening it to cover all three produces a type whose name is a lie and whose two other implementations must return something they do not have. §5 records that the widening was avoided, not deferred |
| **Cost of doing nothing** | Concrete, not abstract: **rollback would require a code change and a redeploy.** Design §6's "rollback at every phase is a credential reorder" would be false, and task 022 could not collapse the three per-class CCA caches — which means **task 011's time-boxed A4 exception would become permanent** |

## 17. Booked onto downstream tasks

| Task | Obligation |
|---|---|
| **022** | The contract is **async** → all four call sites must move client construction **out of constructors** into lazy first-use (`CiamGraphClientFactory.GetOrCreateAppAsync` is the precedent). Also reconcile `AgentToken:ClientSecret` against the provider's secret precedence (§10) |
| **060** | Allowlist `OrderedCredentialClientProvider` as the sanctioned `.WithClientSecret` binding point **with reason** (§15.1). Diagnose the task-020 seam flake with the error code in hand (§14) |
| **061** | The census must count the provider as ONE consolidated site, not as expansion |
| **033** | Removing the secret means deleting `ClientSecret` from the default order in `AddCredentialSelection`, not just from app settings |
