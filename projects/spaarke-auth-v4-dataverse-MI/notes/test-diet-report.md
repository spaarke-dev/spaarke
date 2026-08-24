# Test diet report — spaarke-auth-v4-dataverse-MI

**Run date**: 2026-08-24
**Branch**: `work/spaarke-auth-v4-dataverse-MI`
**Scope**: tests touched between `418718295` (merge-base with master) and `HEAD`
**Classifier**: ADR-038 §7, bans B1–B17, with heuristic 0 (fitness functions) applied first

---

## Summary

| Class | Files | Methods | Action |
|---|---|---|---|
| **FITNESS FUNCTION** (KEEP — heuristic 0) | 5 (+1 helper) | 25 | confirmed, no action |
| **MAINTAIN** (KEEP path, behavioral) | 10 | 74 | confirmed, no action |
| **PATH-VIOLATION-PROTECTED** | 2 | 8 | reviewer judgment — behaviorally sound, wrong path |
| **AMBIGUOUS** | — | 1 | reviewer judgment |
| **SCAFFOLDING (DELETE)** | **0** | **0** | — |
| **OUT OF SCOPE** (touched, not authored) | 4 | 0 added | excluded, see §5 |

**No deletions are recommended.** That is an unusual outcome and it is worth saying why rather than
presenting it as a clean bill of health: this project authored its tests *after* task 063 audited the
classifier itself, so the categories were known in advance. Tests were written to the KEEP paths on
purpose. A zero-delete diet here reflects **timing**, not virtue — and it does not extend to the two
path violations below, which are real.

---

## 1. FITNESS FUNCTION — KEEP (heuristic 0, no further heuristics applied)

`tests/Spaarke.ArchTests/**`. Structural invariants over source/assemblies, not runtime behavior.

| File | Methods | Note |
|---|---|---|
| `CredentialGuardTests.cs` | 8 | FR-F1 credential ban + the 010/023/020 guards |
| `CredentialCensusTests.cs` | 5 | FR-F2 per-file construction-site census |
| `ServiceBusClientGuardTests.cs` | 4 | 051's single-registration guard |
| `FabricatedResultGuardTests.cs` | 3 | 056's fabricated-web-results guard |
| `ADR010_DITests.cs` *(modified)* | 5 | pre-existing ratchet |
| `SourceScan.cs` | 0 | shared helper — **load-bearing for compilation**, not a test |

**Why heuristic 0 exists at all** (task 063): heuristic 1 would flag every one of these as a path
violation with no canonical path to move to, and therefore recommend deleting them — contradicting
ADR-038, which names *"NetArchTest-style architecture tests at Tier 1"* as the sanctioned **replacement**
for the discovery lost to bans B1–B5. The classifier would have deleted the mechanism the ADR prescribes.

✅ **RATIFIED 2026-08-24 — ADR-038 Amendment A1.** This was flagged here as the weakest link in the
project's forcing functions, and it was fixed rather than re-booked: `tests/Spaarke.ArchTests/**` is now the
**eighth KEEP path** in the ADR itself, not merely in skill/module text. All four surfaces (ADR-038,
`.claude/constraints/testing.md`, `/test-diet`, `tests/CLAUDE.md`) were moved together so they cannot
disagree. Heuristic 0 is retained deliberately — the path fix alone would still let heuristics 2–12
mis-flag fitness functions on naming (B13) and setup-ratio (B15) grounds.

---

## 2. MAINTAIN — KEEP path, behavioral (no action)

### Added by this project — `tests/integration/seam/Auth/**` (65 methods)

| File | Methods | Bans triggered |
|---|---|---|
| `CredentialOrderingSeamTests.cs` | 20 | none |
| `IdentityConflationSeamTests.cs` | 15 | none |
| `CredentialSelectionSeamTests.cs` | 10 | none |
| `ServiceBusCredentialSeamTests.cs` | 7 | none |
| `ClientAssertionProviderSeamTests.cs` | 5 | none |
| `ConfidentialClientMigrationSeamTests.cs` | 4 | none |
| `ConfidentialClientSharingSeamTests.cs` | 4 | none |

Zero `Mock<HttpMessageHandler>` (B1), zero `new Mock<>` at all, zero constructor null-check tests (B4).

**One B3 near-miss, examined rather than pattern-matched.** `CredentialOrderingSeamTests.cs:397,434` call
`GetRequiredService<IOptions<CredentialSelectionOptions>>()`. The banned shape (B3) is
`Assert.NotNull(services.GetRequiredService<X>())` *as the whole test* — asserting that DI is wired. These
lines instead read a **bound options value from a really-booted host** and assert its **content**
(`.Value.Order.Should().Equal(...)`). That is behavior — and it is the assertion that caught the
regression described in 033 §6. **MAINTAIN.**

> `tests/integration/seam/**` is a KEEP path per ADR-038 §2 (since 2026-07-09). Task 063 found this skill's
> heuristic 1 still enumerated only six paths and omitted it — meaning **every vertical-slice-seam test in
> the repo was a delete candidate** whenever `/test-diet` ran. Fixed in the same task. Worth remembering
> that the classifier itself was the defect, not the tests.

### Modified by this project — 0 methods added (KEEP paths)

| File | Change | Class |
|---|---|---|
| `seam/Ai/SemanticScopeProviderSeamTests.cs` | 1 deletion | MAINTAIN |
| `contract/Api/ExternalAccess/ExternalAccessContractTests.cs` | +28/−4, 0 methods | MAINTAIN |
| `tenant/Ai/ReferenceRetrievalTenantPinTests.cs` | 1 deletion | MAINTAIN |

`ExternalAccessContractTests.cs` contains one `Mock<HttpMessageHandler>` (B1). **Pre-existing and not
authored here** — this project added no methods to it. Flagged for its owning project; acting on it from
this diet would be scope creep into someone else's tests.

---

## 3. PATH-VIOLATION-PROTECTED — reviewer judgment

Both are **behaviorally sound** — they would classify MAINTAIN at a KEEP path. Neither is a delete
candidate. The issue is location only, so per this skill's *path-check protective* contract they are
flagged, not removed.

### 3a. `tests/unit/Sprk.Bff.Api.Tests/OptionsValidation/DocumentIntelligenceOptionsValidatorTests.cs` (ADDED, task 054 — 5 methods)

```
Succeeds_WhenOpenAiKeyIsAbsent_BecauseManagedIdentityIsTheAlternative
StillFails_WhenOpenAiEndpointIsAbsent
Succeeds_WhenRecordMatchingEnabledAndAiSearchKeyIsAbsent
StillFails_WhenRecordMatchingEnabledAndEndpointOrIndexIsAbsent
SkipsEverything_WhenFeatureIsDisabled
```

All five assert real validator outcomes with `{Scenario}_{ExpectedResult}` names, and they exist because
054 closed a genuine gap — clearing an AI Search key would otherwise have **failed startup**. The
`StillFails_*` pair is the negative control.

`tests/unit/Sprk.Bff.Api.Tests/**` is not a KEEP path (`tests/unit/domain/**` is).

**Recommendation**: these are startup/config *seam* tests. Proposed move:

```bash
mkdir -p tests/integration/seam/Config
git mv tests/unit/Sprk.Bff.Api.Tests/OptionsValidation/DocumentIntelligenceOptionsValidatorTests.cs \
       tests/integration/seam/Config/DocumentIntelligenceOptionsValidatorSeamTests.cs
```

⚠️ Verify the seam project compiles them (the seam files are linked into `Sprk.Bff.Api.Tests.csproj`, so a
move within that compilation is low-risk) and re-run `dotnet test` after.

### 3b. `tests/unit/Sprk.Bff.Api.Tests/Services/RecordMatching/RecordMatchServiceTests.cs` (3 methods ADDED, task 053)

```
Constructor_SelectsManagedIdentity_WhenAiSearchKeyMissing            <- see §4
Constructor_SelectsManagedIdentity_WhenFlagIsSet_EvenWithKeyConfigured
SearchClientFactory_PrefersKey_WhenFlagIsUnsetAndKeyPresent
```

The latter two assert `SearchClientFactory.UseManagedIdentity(...)` returns true/false — the credential
**selection rule**, which is precisely the seam category, and precisely what 053's live cutover exercised.

**This is NOT a `git mv`** — 3 methods sit inside a 17-method pre-existing file that this project does not
own. Correct remedy is method-level extraction into
`tests/integration/seam/Auth/CredentialSelectionSeamTests.cs` (which already exists and already covers the
ordered-selection rule), leaving the other 14 methods untouched. Reviewer's call; not mechanical.

---

## 4. AMBIGUOUS — reviewer judgment

| File:Method | Ambiguity | Suggestion |
|---|---|---|
| `RecordMatchServiceTests.cs:Constructor_SelectsManagedIdentity_WhenAiSearchKeyMissing` | Its only assertion is `.Should().NotThrow(...)` → **B10 coverage-filler** signal. But the name states a real scenario, and "the constructor does not throw when the key is absent" *is* the behavior 053 changed (previously an absent key un-registered six services). | Keep, but **strengthen**: assert `UseManagedIdentity(config, null) == true` like its two siblings do, instead of merely not throwing. Then it is unambiguously MAINTAIN. |

---

## 5. OUT OF SCOPE — touched but not authored here

Four files show as modified with **zero methods added**. Each carries the identical one-line mechanical
edit removing a now-dead option:

```
-            ApiKeySecretName = "test-api-key",
```

- `tests/unit/.../Services/Ai/RagServiceTests.cs`
- `tests/unit/.../Services/Ai/Security/PrivilegeAwareRagServiceTests.cs`
- `tests/unit/.../Services/Ai/Memory/SessionFileRetrievalBindingGuardTests.cs`
- *(plus the KEEP-path modified files listed in §2)*

Required for compilation after task 053 removed `AiSearch:ApiKeySecretName`. **Excluded from the diet** —
the diet reconciles tests a project *authored*, and classifying someone else's tests off the back of a
one-line compile fix would be scope creep. Their path violations (`tests/unit/Sprk.Bff.Api.Tests/**`) are
real but pre-existing and belong to their owning projects.

---

## Count delta

| | |
|---|---|
| Test methods **added** by this project | **73** (65 auth-seam + 5 options-validator + 3 record-match) |
| Classified MAINTAIN / FITNESS FUNCTION | **65 + 25 (incl. modified)** |
| Classified SCAFFOLDING (delete) | **0** |
| PATH-VIOLATION-PROTECTED | **8** |
| AMBIGUOUS | **1** |
| **Net post-diet expected count** | **unchanged — 73 added, 0 removed** |

---

## Commands (DO NOT auto-execute — reviewer's decision per FR-B09)

```bash
# No deletions recommended.

# Path moves (3a) — verify compilation and re-run tests after:
mkdir -p tests/integration/seam/Config
git mv tests/unit/Sprk.Bff.Api.Tests/OptionsValidation/DocumentIntelligenceOptionsValidatorTests.cs \
       tests/integration/seam/Config/DocumentIntelligenceOptionsValidatorSeamTests.cs

# (3b) requires method-level extraction, not git mv — see §3b.
```

## Open item this diet surfaced

✅ **CLOSED the same day (2026-08-24) — ADR-038 Amendment A1.** Every forcing function this project leaves
behind — the credential ban, the census, the Service Bus guard, the fabricated-results guard — lives in
`tests/Spaarke.ArchTests/**`, which is now a KEEP path in the **ADR**, with the same MUST force as the other
seven: deleting a file there requires a same-PR replacement. A `/test-diet` run that recommends deleting one
is now reporting a **classifier defect**, not a finding.

## Citation

ADR-038 §7 build-vs-maintain criteria (Beck "delete the scaffolding"; Feathers characterization-vs-behavior;
Google test sizes). 17-ban classifier B1–B17. Heuristic 0 + the `seam/**` path fix: task 063.
