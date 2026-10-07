# Test diet report — `spaarkeai-compose-r8`

**Run date**: 2026-09-01
**Branch**: `work/spaarkeai-compose-r8`
**Scope**: `.cs` tests touched between `origin/master` and `HEAD` (27 commits)

> **Scope note, stated because it bounds the claim.** This pass covers the work that is still
> UNMERGED. Earlier phases of this project (tasks 001–069, PR #806 and its siblings) already reached
> master and were dieted at their own merges. `/test-diet` also only reads `tests/**/*.cs` — the
> client-side additions are listed at the end, classified by hand against the same criteria, because
> leaving them out entirely would misrepresent what this project added.

## Summary

| Class | Methods | Action |
|---|---|---|
| MAINTAIN (KEEP path, confirmed) | 75 | none |
| FITNESS FUNCTION (`Spaarke.ArchTests/**`, heuristic 0) | 15 | none |
| AMBIGUOUS (reviewer judgment) | 2 | see below |
| PATH-VIOLATION (pre-existing, swept) | 24 | see below — **recommend no action this PR** |
| **SCAFFOLDING (delete candidates)** | **0** | — |
| **Total in scope** | **116** | |

**Zero scaffolding.** No method in scope matched any of B1–B17. That is a result, not an absence of
looking: the scan checked for `Mock<HttpMessageHandler>` / `Mock<IServiceClient>` (B1/B2),
`GetRequiredService` assertions and ctor-null tests (B3/B4), `BindingFlags.NonPublic` (B8), and
bare `NotThrow()`/`NotNull()` assertions (B10). Every `Mock<HttpMessageHandler>` hit in the tree is a
**comment declaring compliance**, not a usage. The one `GetRequiredService` hit obtains a real
`IServiceScopeFactory` for fixture setup; it does not assert that a type is registered.

Split by who wrote it (the touch-radius rule — *"you added this, defend it"* and *"this was already
here, we noticed it in passing"* deserve different scrutiny):

| Origin | Files | Methods |
|---|---|---|
| Written by this project (new files) | 9 | 52 |
| Pre-existing, modified/swept | 7 | 64 |

## Delete commands

**None.** Nothing was classified SCAFFOLDING.

## Path-move commands — emitted, and I recommend NOT executing them in this PR

```bash
# PATH-VIOLATION (heuristic 1): not under integration/{auth,regression,data-mutation,tenant,contract,seam}/**,
# unit/domain/**, or Spaarke.ArchTests/**.
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceApplyTemplateTests.cs \
       tests/integration/data-mutation/Compose/ComposeApplyTemplateBehaviourTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceCreateOnSaveTests.cs \
       tests/integration/data-mutation/Compose/ComposeCreateOnSaveBehaviourTests.cs
```

**Why I am not running these, stated so the skip is a decision rather than an omission.**

Both files **pre-date this project** (r6 task 032 and earlier) and this branch touched them lightly —
106 added lines in one, a 9-line rename port in the other. They are genuine behaviour tests: real
OOXML through the real merge engine, mocked only at the SPE/Dataverse boundaries. They are at the
wrong path, and that is worth fixing.

But moving 24 pre-existing test methods during a wrap-up PR whose subject is Compose defect closure
mixes two changes with different risk profiles, and the diet is the wrong moment to take on a rename
that touches neither this project's code nor its defects. **A whole sibling directory
(`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/**`, 13 files) sits at the same wrong path** — moving
two of thirteen because they happened to be touched would leave the directory in a worse, more
confusing state than leaving all thirteen consistent.

**Recommendation**: file the directory as a single follow-up rather than move two files here. That is a
deferral, so it is named as one — it is not "handled".

## Ambiguous — reviewer judgment

| File : Method | Why ambiguous | My read |
|---|---|---|
| `ComposeDriveProvenanceTests.cs : TryResolveRecordedDriveId_AsksForTheDriveColumn` | B13 — `{Method}_{ExpectedResult}` with no explicit scenario clause | **MAINTAIN.** The scenario is "always", and the test is load-bearing: a widened retrieve that quietly narrows again would make every caller silently fall back to the client's claim while every other test still passed. Renaming it `_AlwaysAsksForTheDriveColumn` would satisfy the heuristic and add nothing |
| `ComposeCitationParityCorpusTests.cs : TheCorpusIsPresentAndNonTrivial` | B13 — no method-under-test prefix | **MAINTAIN.** It is a non-vacuity guard, not a behaviour test: every `[Theory]` case in that file would pass over an empty corpus, so this is what makes a failed load or an emptied file fail loudly. There is no "method under test" to name |

Both are naming-heuristic hits on tests whose deletion would remove real protection. Reported rather
than silently reclassified, per the skill's "ambiguity is honest" contract.

## Maintain — confirmed (sample; all 75 listed by file above)

| File : Method | KEEP path | Why maintain |
|---|---|---|
| `ComposeSaveIdentitySelfHealTests.cs : CreateOnSave_WhenTheGraphItemIdIsDuplicated_LandsOnTheCanonicalRowAndMintsNoNewOne` | `data-mutation` | Regression for the 2026-08-17 dev incident; the load-bearing assertion is that `UpsertAsync` is never called |
| `ComposeDriveProvenanceTests.cs : SaveDocument_WhenTheCallerNamesADifferentDrive_WritesToTheDriveTheRecordRecords` | `data-mutation` | Pins where a write lands; negative control confirmed it fails when the resolution is not applied |
| `ComposeCitationParityCorpusTests.cs : ResolveCitation_OverTheSharedCorpus_MatchesTheClientResolver` | `seam` | Cross-runtime contract — the only mechanism that can detect C#/TS parser drift |
| `ComposeHeaderFooterPageBreakSeamTests.cs : Save_WhenNoBaselineIsCaptured_StillReportsEveryInteriorSectionBreakItDestroys` | `seam` | Guards the fail-open path where the worst outcome would otherwise be the quietest |
| `ComposeIdentityKeyHealthCheckContractTests.cs : Healthz_WhenTheIdentityKeyIsBroken_StaysHealthySoInstancesAreNotRecycled` | `contract` | Pins the `catalog` tag routing — a broken key must not recycle App Service instances |

## Reliability registry

`tests/.reliability-registry.json` holds 2 entries, **neither Compose-related**. No stale entry to
retire under the registry's `_exitRule`.

## Client-side tests (outside `/test-diet`'s `.cs` scope, classified by hand)

| File | Methods | Class |
|---|---|---|
| `composeCitationResolver.parity.test.ts` (new) | 2 (one `it.each` over 45 cases) | MAINTAIN — the client half of the cross-runtime contract |
| `ComposeWorkspace.renderOnSave.test.tsx` (modified) | 2 assertions **inverted** | MAINTAIN — they previously pinned the #858 defect (`containerId === 'bu-container-1'`); they now pin its absence |

No client test was added that a ban would catch. Full client suite: 1,381 / 1,381 across 105 suites.

## Count delta

- Methods added by this project: **52**
- Classified MAINTAIN or FITNESS FUNCTION: **52**
- Classified SCAFFOLDING: **0**
- Net post-diet expected count: **unchanged**

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers
characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17;
heuristic 0 (fitness functions) per ADR-038 Amendment A1.
