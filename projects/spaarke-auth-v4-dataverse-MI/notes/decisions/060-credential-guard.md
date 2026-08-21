# Task 060 — the credential ban, and the three guards booked onto it

> Implemented 2026-08-21. FR-F1. `tests/Spaarke.ArchTests/CredentialGuardTests.cs`, 8 tests.

---

## 1. Success criterion 12, demonstrated rather than asserted

The project's distinguishing criterion: *introduce a deliberate ninth secret-bearing confidential client
on a scratch branch and the build must fail.* Done for real, not reasoned about — a scratch
`ScratchNinthClient.cs` with a `.WithClientSecret(...)` was added under
`src/server/api/Sprk.Bff.Api/Infrastructure/Auth/`, and:

```
Failed!  - Failed: 1, Passed: 0
Offending sites:
src\server\api\...\Infrastructure\Auth\ScratchNinthClient.cs:12:
  MSAL confidential client bound to a client secret -- .WithClientSecret(secret)
```

The file was then deleted and the suite returned to **44 / 44**. This is what separates this project from
the three audits that preceded it: they produced correct prose, and prose is what failed.

## 2. Two stale references in the POML, both corrected

- **`GodClassGuardTests.cs` does not exist.** The POML names it as the canonical ratchet reference; the
  God-class LOC ratchet was **RETIRED on 2026-08-20** (root CLAUDE.md §11.5) because it gated on line
  count. Conventions were taken from `LayerDependencyTests` (negative control) and
  `DataverseServiceClientDowncastTests` (source scan) instead, both current.
- **The `_cca` field no longer exists.** The booked guard was *"assert that `DataverseAccessDataSource`
  never assigns `_cca` inside the managed-identity branch"*. Task 022 removed `_cca` entirely. The
  **invariant** survives the field, so the guard is expressed against the post-022 shape (§4).

## 3. The allowlist, and why the reason is enforced

Five entries: the provider (E-3), `SpeAdminTokenProvider` + `SpeAdminGraphService` (E-1, other
applications' identities), and Power BI ×2 with the deferral reason recorded **verbatim** as the POML
required.

A second test — `EveryAllowlistEntryCarriesAReasonAndAnAdrReference` — fails if any entry has a blank or
trivially short reason or no ADR citation. That is deliberate: **the allowlist is the part of this
mechanism that decays.** An unexplained exemption is indistinguishable from an oversight six months
later, and "there was an exemption for it" is precisely how the previous audits reached NEVER-REMOVE.

The maintenance procedure is a comment block above the allowlist, as the sibling tests do. Its first step
is the one that matters: *ask whether the site needs its own credential at all* — since task 022, code
authenticating as the BFF needs no entry, it needs an injection.

## 4. The three guards booked from other tasks

| From | Guard | Shape |
|---|---|---|
| **010** | `DataverseAccessDataSource` must not gate DELEGATED access on the managed-identity flag | Two checks: `OboAvailable` must not reference the flag, AND the OBO identity/provider fields must not be assigned inside the `if (useManagedIdentity)` branch. The second is how the re-entanglement actually arrives — a "simplification" that sets the provider to null in the MI arm, silently disabling OBO for every user |
| **023** | No managed identity resolved **by name** | Five UAMIs exist in dev and `spaarke-bff-identity` is named like the BFF's without being attached to it. Task 023 verified by grep that the runtime does not do this — a fact about one day's source, not a guard. Now a guard |
| **020** | `ManagedIdentityClientAssertion` must be held in a **readonly field** | Expressed via `readonly` rather than "not inside a method body", because `readonly` is **compiler-enforced** to mean declaration-initializer-or-constructor. That makes the check exact instead of a brace-counting approximation of the C# grammar |

## 5. The negative controls earned their keep — twice

Both failures were in my own detector, found by running it rather than reading it.

**(a) Line-scoped analysis was wrong.** The assertion-reuse guard checked one line at a time. The real
`ManagedIdentityAssertionProvider` assigns through a **multi-line ternary** — target on one line, both
`new` expressions on the next two — so the sanctioned code was reported as a violation. A guard that
flags the very code it exists to protect gets deleted, not obeyed. Fixed by analysing `;`-terminated
**statements**.

**(b) Braces had to be statement boundaries too.** With `;` alone, a member signature and its opening
brace accumulate into the first statement of the body, so the constructor signature ended up prefixed to
the assignment and the `^\s*(\w+)\s*=` anchor matched `public` instead of the field — the same false
positive by a different route. Caught by the **positive control** in
`AssertionReuseDetector_NegativeControl`, which asserts the sanctioned multi-line-ternary shape is NOT
flagged. Without that half, this would have shipped green and failed the first time somebody touched the
provider.

The detector also carries an explicit anti-over-fire control: every file task 022 migrated *discusses*
the credential it no longer constructs, so comments are stripped and
`Detector_NegativeControl_FiresOnEachSeededForm` asserts prose is not flagged.
`Detector_DoesNotFireOnSecretFreeCredentials` checks `.WithCertificate`, `.WithClientAssertion` and
`ClientAssertionCredential` are ignored, and re-checks the two real secret-free files on disk so the test
cannot pass on stubs while failing against the codebase.

## 6. Escalation trigger — did not fire

*"The detector cannot distinguish E-1 sites from BFF-identity sites reliably — STOP; a test that
over-fires will be suppressed and the mechanism lost."* It distinguishes them by file, which is exact:
E-1 sites are whole files dedicated to other applications' identities. The over-fire risk was real but
came from **comments**, not from E-1, and is handled by comment-stripping plus two negative controls.

## 7. Verification

| Criterion | Evidence |
|---|---|
| A new `.WithClientSecret` site outside the allowlist FAILS the build | **Demonstrated live** (§1) |
| Existing E-1 sites pass unmodified | `git diff` on both files is empty; suite green |
| Negative control proves the detector fires | 3 seeded forms, all detected |
| A seeded re-entanglement of `DataverseAccessDataSource` fails | Both halves asserted against the real file |
| Every allowlist entry carries a reason + ADR reference | Enforced by a test, not by review |
| Power BI allowlisted with the deferral reason verbatim; un-deferral is a one-line removal | Two entries, reason copied exactly from the POML |
| Does NOT fire on `CiamGraphClientFactory` (certificate) or the assertion provider | Asserted both as text patterns and against the real files |
| Maintenance procedure documented in the test file | Comment block above the allowlist |
| Assertion constructed only in ctor/static-init, with a negative control | `readonly`-based check + per-call scratch provider + sanctioned-shape positive control |
| ArchTests | **44 / 44** (36 before + 8 new) |

## 8. Booked onward

- **061** — the census must scan **all server assemblies**; `ConfidentialClientTokenCredential` now lives
  in `Spaarke.Dataverse`. Count the provider as **one consolidated site**. The two Power BI sites stay as
  secret-backed entries. `CredentialGuardTests` and the census are complementary: this file bans
  *unlisted* secret bindings, the census bans *unlisted confidential clients of any credential kind*.
- **062** — ⚠️ **sequencing must be decided explicitly.** A startup assertion that fails outside
  Development when a BFF credential resolves to a secret would fire **today**: `AddCredentialSelection`'s
  default order still contains `ClientSecret`, deliberately, until task 033. The guard must key on the
  credential actually SELECTED (`SelectedKindFor`), not on the order's contents, or it must ship disabled
  until 033.
- **063** — depends on 060 + 061. `tests/Spaarke.ArchTests/` is **not** an ADR-038 KEEP path, so
  `/test-diet` at task 090 would delete exactly this file.
