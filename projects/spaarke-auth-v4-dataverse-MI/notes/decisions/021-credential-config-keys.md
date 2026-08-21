# Task 021 — design, decided before implementation

> **Status**: DESIGN ONLY — no code written yet. Captured 2026-08-21 at task 021 Step 3 (context budget)
> so the next session **implements** rather than re-derives. Every fact below was verified against the
> repo or the live tenant, not recalled.
>
> **Implementation has not started. Nothing under `src/` has been modified for this task.**

---

## 0. Verified preconditions (checked, not assumed)

| Fact | Evidence |
|---|---|
| `Spaarke.Dataverse` already references `Microsoft.Identity.Client` **4.87.0** | `Spaarke.Dataverse.csproj:17` |
| ⇒ an MSAL-typed member in a contract declared there adds **no** package reference, so **FR-14 is unaffected** | same |
| The three CCA caches to collapse | `AgentTokenService.cs:48`, `DataverseUserClient.cs:55`, `DataverseAccessDataSource.cs:42` |
| Exactly **one** of the four call sites is outside the BFF | only `DataverseAccessDataSource` is in `Spaarke.Dataverse` |

## 1. ⚠️ The POML's certificate instruction cannot be followed as written

> *"the certificate branch reuses the proven `CiamGraphClientFactory.cs:154-170` KV PFX load … Do not
> write a second certificate loader."*

`LoadCertificateAsync` is a **`private` instance method** on `CiamGraphClientFactory`, closing over that
class's own `_secretClient` and `_certificateName` fields. It cannot be called from the credential
selector.

So "do not write a second loader" and "reuse this one" can only both hold by **extracting** it — moving
the PFX-decode + `EphemeralKeySet` load into a shared helper that both `CiamGraphClientFactory` and the
new selector call. Extraction is the intent; the literal instruction is unsatisfiable.

The part that must survive extraction verbatim, because it is the security property:
`X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.EphemeralKeySet)` — private key
in memory only, never on disk. Also keep the `FormatException` → "not a base64-encoded PFX / verify it
is a Key Vault *certificate*, not a plain secret" message; that is a real diagnostic.

**Note this is a `CiamGraphClientFactory` edit**, which the POML's `<relevant-files>` does not list.
`CiamGraphClientFactory` is **already A4-compliant** (it is the certificate exemplar) — the extraction
must be behaviour-preserving for it.

## 2. The second contract

```
Spaarke.Dataverse   IClientAssertionProvider      (task 020 — MI-FIC assertion only)
Spaarke.Dataverse   IConfidentialClientProvider   (task 021 — NEW: returns a configured client)
Sprk.Bff.Api        OrderedCredentialClientProvider : IConfidentialClientProvider
```

Shape — MSAL-typed, which is legal here per §0:

```csharp
public interface IConfidentialClientProvider
{
    IConfidentialClientApplication GetClient(string tenantId, string clientId);
}
```

**Why a second contract and not a wider first one** (task 020 decision record §3): ordered selection is
MI-FIC → certificate → secret; only the first *is* an assertion. `.WithCertificate(x509)` and
`.WithClientSecret(...)` have no assertion to return. Widening `IClientAssertionProvider` to cover them
produces a type whose name is a lie.

**Who consumes what** — do not add interfaces to sites that do not need them (ADR-010 prefers concrete):

| Site | Assembly | Injects |
|---|---|---|
| `DataverseAccessDataSource` | `Spaarke.Dataverse` | `IConfidentialClientProvider?` (nullable default — NFR-04, 46 fixtures) |
| `GraphClientFactory`, `DataverseUserClient`, `AgentTokenService` | BFF | the **concrete** provider |

## 3. The cache — one, keyed by three parts

`(tenantId | clientId | credentialKind)`. **`credentialKind` is not optional**: the credential is bound
into the client at `Build()`, so a client built on MI-FIC and one built on the secret are different
objects that must not collide. Omitting it would hand a rollback the pre-rollback client.

This is the cache that closes task **011**'s time-boxed A4 exception at task 022. If 021 does not
author it, that exception becomes permanent.

## 4. Fall-through: ONE predicate, and it is not uniform

`IsFallThroughEligible(MsalServiceException)` — a single testable predicate, never re-derived per site.

| MSAL error code | Decision | Why |
|---|---|---|
| `managed_identity_unreachable_network` | **fall through** | no IMDS route — ordinary local dev |
| `managed_identity_all_sources_unavailable` | **fall through** | same class |
| `managed_identity_request_failed` | **FAIL LOUD** | IMDS reachable, identity absent/wrong — the **FR-B4** signature. Falling through runs production on the secret while looking healthy |
| `user_assigned_managed_identity_not_supported` | **FAIL LOUD** | a deployment-shape error, not an environment one |

Do **not** add an `IsAvailable` probe — a probe is a second network call that races the real one.

## 5. Negative caching — and what task 030 just taught us about its TTL

`ClientAssertionProviderBase` caches successes only, so a failing mint retries on **every** token
acquisition (~80 ms per request off-Azure, measured at task 020). A short-TTL negative memo is required.

**Task 030 measured something that constrains the TTL** (`030-fic-automation.md` §11): after a FIC is
created or changed, Entra **flaps** for ~2 minutes — successes and failures interleaved as replicas
converge, returning `AADSTS70025`. Consequences:

- **TTL must be short (seconds, not minutes).** A minutes-long negative memo would latch onto one
  transient flap failure and keep the process on the *fallback* credential — the secret — long after
  MI-FIC started working. That is a silent downgrade of the exact property this project exists to
  establish.
- **A single failure must not demote.** One flap failure is not evidence the credential is bad.

## 6. Decisions to make during implementation (do not skip)

1. **Q4 — widen `IClientAssertionProvider`?** A certificate assertion needs `aud` = token endpoint and
   `iss`/`sub` = client id; the contract drops both, and `ClientAssertionProviderBase` caches
   options-blind (correct for MI-FIC, a *correctness* bug for a certificate). Decide before six call
   sites bind at 022. If widening: a Spaarke-owned record (`ClientId`, `TokenEndpoint`, `TenantId`) —
   **no MSAL types**.
2. **Secret above a secret-free credential** must be rejected or logged as misconfiguration (A4).
3. **Empty credential list** must fail fast at startup with an actionable message.
4. **Escalation trigger**: if ordered selection cannot be config-only *without a restart*, record it.
   A restart is acceptable under NFR-06; a **redeploy is not**.

## 7. Config shape (draft — validate against `.claude/constraints/config.md` at implementation)

```
Graph:Credentials:Order:0 = ManagedIdentityFederated
Graph:Credentials:Order:1 = KeyVaultCertificate
Graph:Credentials:Order:2 = ClientSecret
Graph:Credentials:KeyVaultCertificate:Name = <kv cert name>
```

Rollback = reorder the list + restart. Bind via Options with `ValidateOnStart`.

## 8. Not yet done

Everything. This file is the plan, not the outcome. Implementation, seam tests
(`tests/integration/seam/Auth/CredentialOrderingSeamTests.cs`), publish-size measurement, CVE scan and
the Step 9.5 gates all remain.
