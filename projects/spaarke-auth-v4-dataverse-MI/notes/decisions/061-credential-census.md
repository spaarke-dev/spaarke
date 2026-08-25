# Task 061 — the credential census

> Implemented 2026-08-21. FR-F2. `tests/Spaarke.ArchTests/CredentialCensusTests.cs`, 5 tests.

---

## 1. The end-state estate, counted

Seven confidential-client construction sites across six files, verified against source:

| File | Sites | Identity | Credential |
|---|---|---|---|
| `OrderedCredentialClientProvider.cs` | 1 | The BFF's own app registration | Ordered: MI-FIC → certificate → transitional secret |
| `CiamGraphClientFactory.cs` | 1 | CIAM Graph provisioner | **Certificate** (secret-free) |
| `SpeAdminTokenProvider.cs` | 1 | Per-container-type owning apps (E-1) | Secret from Key Vault, per request |
| `SpeAdminGraphService.cs` | **2** | Per-business-unit owning apps (E-1) | Secret from Key Vault |
| `ReportingEmbedService.cs` | 1 | Power BI service principal | **STILL SECRET-BEARING** (deferred) |
| `ReportingProfileManager.cs` | 1 | Power BI SP profiles | **STILL SECRET-BEARING** (deferred) |

The origin assessment counted **five** sites when there were **eight**; the two it missed were the
SpeAdmin paths, found by a later audit rather than by anything automatic. The census makes the count
itself the assertion.

## 2. Per-FILE counts, not a global total

A global total is satisfiable by accident: remove one site, add another elsewhere, and the number still
matches while the estate has changed. Per-file counts localise the failure to the file that changed, and
they are why `SpeAdminGraphService.cs` carries an explicit `Sites: 2` — two constructions in one file, so
swapping one for another inside it cannot pass unnoticed.

## 3. Counts confidential clients by FUNCTION, not by SDK

Two construction forms are detected: MSAL `ConfidentialClientApplicationBuilder.Create(...)` and
`Azure.Identity`'s `ClientSecretCredential` / `ClientAssertionCredential` / `ClientCertificateCredential`.
`SpeAdminGraphService`'s two sites are the second kind. A census that counted only MSAL would report six
sites and call the estate fully inventoried while two secret-bearing clients sat outside it — the same
shape of miss as the origin seed's.

## 4. The blind-spot control, demonstrated live

Task 020 booked this specifically: `ADR010_DITests` scans `typeof(Program).Assembly` — the BFF only — so
the cross-assembly `IClientAssertionProvider` seam was invisible to it, and a ceiling raise that looked
necessary turned out not to be. The entire credential seam this project builds is cross-assembly, so an
assembly-scoped census would under-report **by construction**.

Proven rather than asserted: a scratch site was seeded in **`Spaarke.Dataverse`** (the base layer, not the
BFF) and BOTH guards fired —

```
UNLISTED confidential-client site in ScratchSharedLibClient.cs:
src\server\shared\Spaarke.Dataverse\ScratchSharedLibClient.cs:11:
  MSAL confidential client bound to a client secret -- .WithClientSecret(secret)
Failed!  - Failed: 2, Passed: 47
```

The file was removed and the suite returned to **49 / 49**. `Census_FiresOnASiteOutsideTheBffAssembly`
also asserts the scan reaches `Spaarke.Dataverse`, `Spaarke.Core` and `Sprk.Bff.Api` — a census that
silently enumerated zero files would otherwise pass every other test in the class.

## 5. Detection: whole-file regex, not statements

Task 060's statement-based scanner is the wrong instrument here. A fluent chain puts
`ConfidentialClientApplicationBuilder` on one line and `.Create(` on the next, and the provider's chain
contains an interpolated string whose `{` the statement splitter treats as a boundary — so the sanctioned
site could split across two statements and be missed. Matching over the comment-stripped whole file with
line structure preserved (`SourceScan.CodeText` / `LineOf`) sidesteps both while still reporting a line
number.

Requiring `.Create(` — not merely the type name — is what stops the census counting
`Func<ConfidentialClientApplicationBuilder, ConfidentialClientApplicationBuilder> Apply,`, a real record
parameter in the provider. Counting it would have inflated the census against its own sanctioned site.
Asserted as an explicit negative control.

## 6. ADR-038 ban B3 — no DI resolution

There is no `IServiceCollection`, no `ServiceProvider` and no `GetRequiredService` anywhere in the file. A
census built on container resolution would be the banned shape AND blind to every site that is not
DI-registered, which is most of them.

## 7. Shared machinery extracted

`SourceScan.cs` now holds what both credential guards need: repo-root resolution, the `src/server/**`
enumeration, comment stripping, statement splitting, and whole-file code text. Extracted rather than
duplicated because two copies drift, and the failure mode of drift here is silent under-reporting by one
of them — which for a census is worse than having none, because it manufactures confidence.
`CredentialGuardTests` was rewired onto it; its 8 tests still pass unchanged.

## 8. Escalation trigger — did not fire

*"Source analysis cannot reliably enumerate construction sites (e.g. built via a factory indirection) —
report; a census that undercounts is worse than none."* All seven sites are direct constructions. The one
indirection in the codebase — `ConfidentialClientTokenCredential`, which obtains a client from the
provider — is correctly **not** a site: it constructs nothing, and counting it would double-count the
provider's single client.

## 9. Verification

| Criterion | Evidence |
|---|---|
| Adding a ninth site FAILS the build until the census is updated | **Demonstrated live** (§4) |
| The failure message names the unlisted site | `UNLISTED confidential-client site in ScratchSharedLibClient.cs` + `file:line` |
| Every census entry carries a one-line reason | Enforced by `EveryCensusEntryIsExplained`, not by review |
| Power BI entries carry the deferral reason and read as still-secret-bearing | `CensusTellsTheTruthAboutWhatIsStillSecretBearing` checks the verbatim phrase AND `STILL SECRET-BEARING` |
| No DI container resolution | By inspection: no DI type appears in the file (§6) |
| Negative control proves the detector fires | Both construction forms, plus two must-not-count shapes |
| Scans ALL server assemblies | Asserted directly, and demonstrated live from the base layer (§4) |
| ArchTests | **49 / 49** (44 + 5 new) |

## 10. Booked onward

- **033** — when the secret is removed, the provider's `CredentialSource` line must drop "transitional
  secret". If it still says it after 033, the migration did not finish. Recorded as step 4 of the census's
  own maintenance procedure, so it is enforced where it will actually be read.
- **040-042** — un-deferring Power BI is two census edits plus two allowlist removals in
  `CredentialGuardTests`. The census is what makes the remaining secret impossible to overlook.
- **063** — `tests/Spaarke.ArchTests/` is not an ADR-038 KEEP path, so `/test-diet` at task 090 would
  delete this file, `CredentialGuardTests` and `SourceScan`. **All three** must be pre-declared.
