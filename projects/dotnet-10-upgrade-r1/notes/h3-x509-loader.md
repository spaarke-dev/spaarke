# H3 — X509CertificateLoader migration (task 012)

**File**: `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/CiamGraphClientFactory.cs:167`

## Change

```diff
- return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
+ return X509CertificateLoader.LoadPkcs12(pfxBytes, (string?)null, X509KeyStorageFlags.EphemeralKeySet);
```

- `pfxBytes` (`byte[]`) converts implicitly to `ReadOnlySpan<byte>` for the `LoadPkcs12` overload.
- Password argument (`(string?)null`) and `X509KeyStorageFlags.EphemeralKeySet` preserved exactly — no
  behavior change to cert sourcing, lifetime, or key-storage semantics.
- `using System.Security.Cryptography.X509Certificates;` was already present (line 1) — no new using
  needed.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release --nologo`: **0 errors**,
  21 pre-existing warnings (unrelated nullability/obsolete-API warnings elsewhere in the BFF).
- `SYSLIB0057` does not appear anywhere in the build output (was previously only a warning here since
  BFF has `TreatWarningsAsErrors=false`; confirmed fully gone at this site, not just suppressed).

## Test coverage

No existing unit test exercises `CiamGraphClientFactory.LoadCertificateAsync` directly (it depends on a
live/mocked `SecretClient` returning a real base64 PFX, and the method is `private`). The only existing
reference to `CiamGraphClientFactory` in `tests/` is
`tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs`, which does not exercise
the cert-load path.

**Coverage gap documented per acceptance-criterion 2's escape hatch.** Manual verification: the change is
a like-for-like API substitution (both `X509Certificate2(byte[], string?, X509KeyStorageFlags)` and
`X509CertificateLoader.LoadPkcs12(ReadOnlySpan<byte>, string?, X509KeyStorageFlags, Pkcs12LoaderLimits?)`
parse PKCS#12/PFX bytes with the same password + key-storage-flags semantics; `LoadPkcs12` is the
documented direct replacement for the obsolete ctor overload per SYSLIB0057). End-to-end OBO/MI/CIAM auth
smoke is covered holistically in task 051 per the task's own `<notes>`.

## Scope

Only `CiamGraphClientFactory.cs` was modified. No other files touched (confirmed via
`git status --short`).
