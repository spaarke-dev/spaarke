# Task 070 — `ComposeService.cs` seam map

> **Analysed**: 2026-08-28 · **File**: `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs`
> **Size at analysis**: **4,389 lines** (POML said 3,573; the reframe box said 4,385 — it is still growing)

## Binding criterion (NOT the POML's)

The POML's two stated criteria are **obsolete** and must not be chased:

- ~~"under 2,000 lines"~~ — the LOC ratchet was retired 2026-08-20 (root CLAUDE.md §11.5).
- ~~"DELETE its waiver entry from `GodClassGuardTests.cs`"~~ — **that file does not exist** (verified
  2026-08-28). There is no waiver to delete.

Binding instead, per the TASK-INDEX reframe box (owner-approved, §6.5 Path C): **extract each cluster
that has its own reason to change, and state that reason per unit. Line count is an observation, not a
target.** A large *cohesive* remainder is a legitimate outcome.

Everything else in the POML still binds — in particular: **internal collaborators, not new DI
registrations** (ADR-010, ≤15 non-framework budget); **behaviour-preserving only**; a defect found while
decomposing is **recorded against its owning task, never fixed inside the restructure**; no second body
author and no second save entry point.

## The nine clusters

Ordered by extraction risk, lowest first. Line ranges are from the 4,389-line file at analysis time.

| # | Cluster | Members | Reason to change | ~LOC |
|---|---|---|---|---|
| 1 | **Re-anchor / stale-base recovery** | `ReanchorStaleSaveAsync` (2593) · `ApplyBestEffortByParagraph` (2842) · `TryApplyPatchUnit` (2977) · `IndexOfParaId` (3006) · `BuildAllOrphanSummary` (3027) | How we recover when the save baseline moved under the editor | ~470 |
| 2 | **Create-on-save / record lifecycle** | `PromoteIfEphemeralAsync` (3169) · `IsInterimCreateOnSaveSuccess` (3525) · `ResolveFileName` (3536) · `BuildRecordFailedResult` (3596) · `ProjectCreateOnSaveState` (3841) · `BuildContainerFailedResult` (3872) · `RebindSessionDocumentIdAsync` (4036) · `GraduateLinkedCopyIfDivergedAsync` (4104) · `TryFindDocumentByGraphItemIdAsync` (4150) · `TransientKeyMatch` (4187) · `TryFindDocumentByTransientKeyAsync` (4197) | When and how an ephemeral draft becomes a Dataverse record | ~750 |
| 3 | **Save baseline + concurrency** | `ResolveSaveBaselineAsync` (1985) · `GuardBaselineIsNotPdf` (2078) · `ReplaceWithPreconditionAsync` (2108) · `FetchBaselineVersionBytesAsync` (2170) · `ComposeSaveVersionStamp` (2209) · `GetSaveVersionStampAsync` (2217) · `SetSaveVersionStampAsync` (2242) | The storage/concurrency contract (`If-Match`, last-writer-wins) | ~308 |
| 4 | **PDF intake + source markers** | `IsPdfSource` (944) · `ProjectPdfToDocxAsync` (970) · `ComposePdfSourceMarker` (2294) · `ComposePdfDerivedDocument` (2300) · `SetPdfSourceMarkerAsync` (2306) · `ClearPdfSourceMarkerAsync` (2331) · `GetPdfSourceMarkerAsync` (2351) · `SetPdfDerivedDocumentAsync` (2373) · `ResolvePdfDerivedDocumentAsync` (2417) | How a PDF becomes an editable document and how that origin is remembered | ~290 |
| 5 | **Profile / background indexing** | `GetProfiledETagAsync` (2486) · `SetProfiledETagAsync` (2505) · `MaybeRetriggerProfileOnLoadAsync` (2530) · `RefreshProfileAsync` (2557) · `DispatchBackgroundProfile` (3670) · `RunBackgroundProfileAsync` (3713) · `IndexingSignal` (3808) | When a document gets (re)indexed | ~275 |
| 6 | **Annotations** | `GetComposeAnnotationsAsync` (3929) · `SaveComposeAnnotationsAsync` (3948) · `ValidateLedgerRefs` (3998) | The session-annotations contract | ~106 |
| 7 | **Memory capture** | `CaptureDocumentMemoryAsync` (3084) | What we remember about a document for the assistant | ~85 |
| 8 | **Reference/paraId mapping helpers** | `ResolveParaIdForHint` (1079) · `BuildReferenceMap` (1106) · `IsSameCrossVersionBinding` (1139) | The projection coordinate system | ~60 |
| 9 | **Core orchestration (the remainder)** | `UploadAsync` (287) · `ProjectForMount` (312) · `ApplyTemplateAsync` (391) · `LoadAsync` (504/515) · **`SaveAsync` (1169)** · `ReadPersistedOriginAsync` (1042) · `ResolveRevisionAuthor` (3066) · `GetActionHistory` (4285) | The public `IComposeService` contract itself | ~2,000 |

## What the remainder is, and why it may legitimately stay large

`SaveAsync` alone is **~816 lines** (1169→1985). It is the save fork the whole project has been working
on — `ContentModel` path vs op-log path, staleness, outcome mapping. **It is one decision with many
branches, not many responsibilities**, and 074's finding is the proof: the two paths are not
interchangeable, they have different capabilities (`PartialApplySummary` / `ReanchorSummary` are wired
exclusively to the op-log path). Splitting the fork itself would fork the save path, which the POML
explicitly forbids ("MUST NOT create … a second save entry point").

So the target for cluster 9 is *cohesion*, not a number. Extracting 1–8 leaves the public contract plus
the fork; if that is still ~2,000 lines it is a **legitimate outcome under §11.5** and should be stated
as such in the PR rather than sliced further to hit a figure.

## Extraction order + mechanism

**Mechanism**: `internal sealed class` collaborators, constructed in the `ComposeService` constructor
from dependencies it already holds. **No new DI registration** — that is the ADR-010 constraint and the
reason partial classes were offered as an alternative in the POML. Prefer real collaborators where the
cluster has genuine state/behaviour; a partial-class split is the fallback for clusters that are pure
static helpers over the service's own fields.

Order is lowest-risk first, and **the suite runs after each extraction, not once at the end** (POML step 3):

1. Cluster 1 (re-anchor) — most self-contained, narrow interface, high line yield.
2. Cluster 4 (PDF) — marker storage + projection, well isolated.
3. Cluster 5 (profile/indexing) — fire-and-forget paths, few callers.
4. Cluster 6 (annotations) + 7 (memory) + 8 (helpers) — small, mechanical.
5. Cluster 3 (baseline/concurrency) — touches the save path; do it with the fork still intact.
6. Cluster 2 (create-on-save) — largest, most entangled with Dataverse; do it last.

## Verification contract (from the 073 exemplar)

073 is the shipped precedent and it is the standard to match: behaviour proven by **two byte-identical
oracles** plus an independent diff, the oracle **made permanent** as a contract test, and **both tests
observed failing first by mutation**. For 070 the equivalent is:

- The Compose seam suite + op-log suite green after **each** extraction (not once at the end).
- The fidelity gate green.
- DI registration count stated explicitly and unchanged.
- Publish size reported (ADR-029), no new NuGet, no new HIGH CVE.

## Cluster 1 — executable extraction spec (analysis done; move not yet made)

Verified 2026-08-28, so the next session executes rather than re-derives.

**Target**: `internal sealed class ComposeReanchorCoordinator` in
`src/server/api/Sprk.Bff.Api/Services/Compose/ComposeReanchorCoordinator.cs`.
Constructed in the `ComposeService` ctor from fields it already holds. **No DI registration** —
that is the ADR-010 constraint, and the whole reason this is a collaborator rather than a service.

**Source range**: lines **2593–3065** (ends immediately before `ResolveRevisionAuthor` at 3066, which
belongs to cluster 9 and stays).

**Exactly 5 dependencies** — verified by scanning every `_field` reference in the range:

| Field | Declared | Type |
|---|---|---|
| `_spe` | 118 | `ISpeFileOperations` |
| `_logger` | 122 | `ILogger<ComposeService>` — pass as `ILogger` so the collaborator does not re-open the category |
| `_patchEngine` | 176 | `ComposeShadowPatchEngine` |
| `_baselineParaIdStamper` | 195 | `ComposeBaselineParaIdStamper` |
| `_reanchorService` | 227 | `AnnotationReanchorService?` (nullable — kill-switch, ADR-032) |

⚠️ `_username` appears in a `grep` of the range but is a **false positive**: the hits at 3062/3071 are
the literal `preferred_username` inside `ResolveRevisionAuthor`'s doc comment and body, which is
*outside* the cluster. Do not add a sixth dependency for it.

**Public surface of the new type — exactly 2 methods.** Everything else becomes private:

| Member | Line | Becomes |
|---|---|---|
| `ReanchorStaleSaveAsync` | 2593 | `internal` (called from `SaveAsync:1401`) |
| `ApplyBestEffortByParagraph` | 2842 | `internal` (called from `SaveAsync:1473`) |
| `TryApplyPatchUnit` | 2977 | private |
| `IndexOfParaId` | 3006 | private static |
| `BuildAllOrphanSummary` | 3027 | private static |

**Only 2 call sites change** — both inside `SaveAsync`, both a prefix insertion:
`SaveAsync:1401` → `_reanchorCoordinator.ReanchorStaleSaveAsync(...)`,
`SaveAsync:1473` → `_reanchorCoordinator.ApplyBestEffortByParagraph(...)`. Argument lists unchanged.

This is why cluster 1 is first: a 470-line move with a 5-field constructor and two prefix edits is the
lowest-risk way to prove the mechanism before touching the baseline/concurrency and create-on-save
clusters, which are entangled with the save fork itself.

**Verify after this one extraction, before starting cluster 4** (POML step 3 — after *each*, not once
at the end): `dotnet build src/server/api/Sprk.Bff.Api/` · the Compose seam + op-log suites · DI count
unchanged.

## Coverage measurement (2026-08-28) — the evidence that decides extraction order

The POML's own risk statement is the reason this was measured before moving code:

> *"a decomposition that subtly reorders a guard or drops a branch reintroduces a defect the project
> just spent seven tasks removing, **and the tests would still pass if the branch was never covered**."*

Coverage is **observation, not a gate** (ADR-038). It is used here to answer one question: *would a
dropped branch actually be caught?*

**Method**: `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Compose"` with
coverlet scoped to `[Sprk.Bff.Api]Sprk.Bff.Api.Services.Compose.*`. **1,783 tests** (that project
compiles `tests/integration/{seam,contract,regression,auth,…}/**` via `<Compile Include>` at
`Sprk.Bff.Api.Tests.csproj:139`, so the seam suite is included).

⚠️ **Measurement trap, hit and corrected.** The first run excluded `CompilerGeneratedAttribute`, which
is what `async` state machines are marked with — so it silently dropped the body of **every async
method** and reported `SaveAsync` as *11 lines, 0 branches*. `ComposeService` is almost entirely async.
Do not exclude that attribute when measuring this file. The numbers below are from the corrected run.

| Cluster | lines | line % | branches | **branch %** |
|---|---|---|---|---|
| 9 **SaveAsync (the fork)** | 409 | 94.9% | 178 | **86.0%** |
| 7 memory capture | 60 | 91.7% | 24 | **95.8%** |
| 6 annotations | 69 | 87.0% | 46 | **89.1%** |
| 5b background profile dispatch | 98 | 87.8% | 26 | **88.5%** |
| 8 paraId / reference helpers | 49 | 93.9% | 16 | **87.5%** |
| 2b record resolution helpers | 108 | 82.4% | 26 | **80.8%** |
| 2a create-on-save / promotion | 269 | 87.4% | 82 | **76.8%** |
| 1 re-anchor / stale-base | 277 | 76.2% | 124 | **76.6%** |
| 3 save baseline + concurrency | 134 | 73.1% | 48 | **75.0%** |
| 4a PDF intake | 49 | 87.8% | 40 | **75.0%** |
| 4b PDF source markers | 112 | 61.6% | 28 | **75.0%** |
| 5a profile etag + retrigger | 61 | 60.7% | 14 | **64.3%** |
| **whole file** | 2,277 | **86.6%** | — | — |

**This inverts the planned extraction order.** Cluster 1 (re-anchor) was scheduled first because it is
structurally cleanest — but at **76.6% branch** it is mid-pack, not safest. Order by the evidence
instead: **7 → 6 → 5b → 8 → 2b → 2a → 1 → 3 → 4 → 5a**. Cluster 1's clean 5-dependency spec still
stands; it just should not go first.

**`SaveAsync` is the best-covered code in the file** — 94.9% line / 86.0% branch, with only 21
uncovered lines across 8 runs and a single run ≥4 lines (**1533–1536**). The POML's fear is real in
principle and largely unfounded here: a dropped branch in the fork would very likely be caught. This
also strengthens the "leave the fork whole" recommendation — it is both cohesive *and* well guarded.

**Weakest link: cluster 5a (profile etag + retrigger), 64.3% branch.** Small (61 lines) but it should
be extracted **last**, or given tests first.

**Caveat, stated because it cuts one way**: only tests matching `~Compose` were run, so anything
exercising `ComposeService` under another name is missing. Real coverage is therefore **≥** these
numbers — the bias is conservative, which is the safe direction for this decision.

## Extraction progress (2026-08-29)

Order follows the coverage evidence (**7 → 6 → 5b → 8 → 2b → 2a → 1 → 3 → 4 → 5a**), not the
structural order this file originally proposed.

| Cluster | Status | New file | Mutation used to prove non-vacuity |
|---|---|---|---|
| 7 memory capture | ✅ extracted | `ComposeMemoryCapturer.cs` | `FactType` → `"SEEDED-MUTATION"` → 1/16 red |
| 6 annotations | ✅ extracted | `ComposeAnnotationStore.cs` | `LedgerRefPattern` → `@".*"` → 1/7 red |
| 5b profile + step signals | ✅ extracted | `ComposeProfileDispatcher.cs` | `if (result.JobSubmitted)` → `if (!…)` → 8/16 red |
| 8 reference/paraId helpers | ✅ extracted | `ComposeReferenceMapping.cs` | off-by-one in `ResolveParaIdForHint` → 4 red |
| 2b + 2a create-on-save | ⛔ **HELD** | — | `unified-access-control-r2` owns #858 inside this file |
| 1 re-anchor | ✅ extracted | `ComposeReanchorCoordinator.cs` | **six** mutations across six members; four survived the whole suite → three coverage holes closed first (below) |

Every extraction: Compose suite **1,790/1,790**, ArchTests **150/150**, build 0/0, and the DI diff
(`Program.cs` + `Infrastructure/DI/`) **empty** — ADR-010 holds by construction, not by assertion.

`ComposeService.cs`: 4,427 → **3,975** lines. Stated as an observation; the target is cohesion, not a
number (CLAUDE.md §11.5).

**Mechanism held for both**: `internal sealed` collaborator, constructed in the `ComposeService` ctor
from dependencies it already holds. **No new DI registration** (ADR-010) — verified by `git diff` on
`Program.cs` + `Infrastructure/DI/` showing no change.

**Cluster 6 needed one decision cluster 7 did not.** Two of its members are PUBLIC `IComposeService`
methods. They stay on `ComposeService` as thin delegations — the interface is the service's contract
to keep; only the policy moves. Moving the interface implementation itself would change what
`ComposeService` *is*, not just how it is organised. Expect the same call on any later cluster whose
members are public (2 create-on-save, 3 baseline).

### Non-vacuity is proven per extraction, and the first attempt was inconclusive

Worth recording because it nearly produced a false "verified":

- Cluster 7: seeding `FactType: "SEEDED-MUTATION"` did **not** redden
  `ComposeMemoryCaptureRecallTests` — that suite exercises the **facade** directly, not the service
  path. It **does** redden `ComposeServiceCreateOnSaveTests` (1/16), which wires
  `IComposeMemoryCapture` through `ComposeService`.
- Cluster 6: neutering `LedgerRefPattern` to `@".*"` reddens `AnchoredAnnotationPersistenceTests`
  (1/7).

The lesson generalises to the remaining clusters: **pick the mutation target and the suite together**,
and if the seeded fault survives, the suite you chose does not traverse the moved code — that is a
statement about coverage, not a licence to move on.

### Cluster 5b — the two open questions, and how they were decided

Members: `DispatchBackgroundProfile` · `RunBackgroundProfileAsync` · `IndexingSignal`.
Dependencies were fine (`_scopeFactory`, `_documentProfileAi`, `_appLifetime`, `_logger`). Two things
had to be decided before moving code. **Both were decided by the owner on 2026-08-29 and are now
implemented** — recorded here so the reasoning survives the commit log:

1. **`ProfileNotAttemptedSignal` has three callers outside the cluster** (≈1867, 3576, 3851 — the
   save path and two failure projections). It cannot simply travel with the dispatcher. Either it
   stays on `ComposeService` and the collaborator calls back into it (a circular smell), or the
   profile-signal factories move to the collaborator as `internal static` and the three outside
   callers reference them there. **DECIDED: the factories move; the three callers follow.** Calling
   back into `ComposeService` from the collaborator would make the dependency circular for no
   benefit, and the signals describe the profile/indexing steps, so they belong with the code that
   owns them.
2. **`IndexingSignal` is grouped here but is about INDEXING, not the background profile.** Its single
   caller is the save path (≈1905). Suggest leaving it on `ComposeService` and recording the
   deviation from this map, rather than moving it because a table said cluster 5.

### Cluster 1 — done, and the caution below was warranted (2026-08-29)

The extraction itself was mechanical. What it surfaced was not.

**The spec in this file undercounted the move.** It listed 5 members; there are **7** — it omitted
`IsBatchLevelPatchRefusal` and `IsStructuralOrGlobalOp`, and `IsBatchLevelPatchRefusal` has a caller
OUTSIDE the cluster (`SaveAsync`'s `catch … when` filter). That is the cluster-5b situation again, and it
was resolved the same owner-approved way: the predicate defines the refusal taxonomy the recovery is built
on, so it travels with the recovery as `internal static` and `SaveAsync` references it there.

**One dependency points the other way.** `ReanchorStaleSaveAsync` needs `ResolveRevisionAuthor`, which is
cluster 9 and stays with the save path (two of its three callers are there). It was widened from `private
static` to `internal static` rather than duplicated or threaded through an extra argument — a pure function
of its `HttpContext`, so a shared helper, not a cycle. The coordinator holds no reference to
`ComposeService`.

**The `ComposeWritePathTextSearchAuditTests` guard fired, correctly.** Its slice runs from `SaveAsync` to
the `ResolveRevisionAuthor` signature, so widening that signature broke the end marker — precisely the
"fails loudly rather than silently auditing the wrong slice" behaviour it was built for. Two things had to
change, and only fixing the first would have been the wrong repair: the marker was updated, **and the newly
extracted file was added as an audited file in its own right**. The ~470 moved lines had been inside the
audited slice purely by position; without adding the new file, the guard would have silently shrunk to
whatever remained between the markers while still reporting green.

#### The mutation pass found three real coverage holes — all pre-existing, none created by the move

Six mutations across six members. Two died immediately. **Four survived the entire 1,791-test suite**, and
per the cluster-7 lesson that is a coverage statement, not a licence to proceed:

| Mutation | Survived? | What it proved |
|---|---|---|
| `IsBatchLevelPatchRefusal` → always true | killed (4–5 red) | recovery eligibility is guarded |
| `appliedCount += unitOps.Count` → `+= 0` | killed (3–4 red) | partial-apply accounting is guarded |
| `IndexOfParaId` off-by-one | **survived** | hole A |
| AUTO gate `Confidence >= 1.0` → `>= 0.0` | **survived** | hole A |
| `IsStructuralOrGlobalOp` → false for all | **survived** | hole C |
| `BuildAllOrphanSummary` `OrphanCount` → 0 | **survived** | hole B |

- **Hole A — the fuzzy-AUTO rejection gate.** The suite covered exact-paraId AUTO (confidence 1.0) and
  total ORPHAN (0.0) but never a score BETWEEN them. So the one branch separating *"scored well on
  content"* from *"is the same paragraph"* — **invariant I-7 in its sharpest form** — could be deleted
  outright with every test still green. The paragraph-hint off-by-one survived for the same reason: with
  no fuzzy case, the hint feeding the scorer never mattered.
- **Hole B — the fail-closed all-orphan summary.** ORPHAN-as-produced-by-the-scorer was covered; the
  fallback that runs when scoring cannot happen at all (unreadable current bytes, or an AUTO batch that
  throws at patch time) was not. It could report an empty summary and nothing would notice.
- **Hole C — structural-op grouping inside prong-1 recovery.** No recovery test had ever included a
  structural op, so the structural-last all-or-nothing rule was free to change unobserved.

**Three tests were added to close them**, extending the two existing seam files rather than adding a
fixture (§11):

| Test | File | Kills |
|---|---|---|
| `Save_StaleBase_FuzzyAutoBandMatch_SurfacedButNotApplied_ThroughTheWire` | `ConcurrencySaveSeamTests` | A (both) |
| `Save_StaleBase_CurrentBytesUnreadable_EveryOpAndCommentSurfacesAsOrphan_ThroughTheWire` | `ConcurrencySaveSeamTests` | B |
| `Save_RefusingStructuralOp_IsItsOwnUnit_InlineOpOnSameParagraphStillApplied_ThroughTheWire` | `ComposePartialApplyRecoverySeamTests` | C |

The fuzzy-AUTO fixture is worth understanding before editing it: the external version **regenerates every
paraId** (the documented Open-XML-SDK #925 case) and drifts one paragraph's text, so content similarity
≈0.89 × 0.75 + structural 1.0 × 0.25 ≈ **0.92** — over the 0.85 AUTO cut-point, under the exact-id 1.0.
The test asserts the confidence stays in that window, so if a future change collapses it to 1.0 the test
fails rather than quietly stopping exercising the branch. It also asserts `StructuralProximity == 1.0`,
which is what catches the paragraph-hint off-by-one directly instead of relying on the score drifting
across a threshold.

The structural-op test is arranged so the two groupings differ in **outcome, not just bookkeeping**: an
inline op and a refusing `splitParagraph` on the SAME paragraph. Grouped correctly the inline edit
survives; grouped as inline the paragraph is atomic, the refusal takes the good edit down with it,
`AppliedCount` hits 0, and the caller's zero-applied guard re-throws the whole save. That is the difference
between keeping the user's edit and losing the session — which is what prong 1 exists to prevent.

**After the three tests: all six mutations die.** Compose suite 1,794 · ArchTests 150 · solution build 0
errors · DI diff empty.

### Cluster 1 was next — and it was the first one that deserved caution

Everything extracted so far sat at **87–96% branch**. Cluster 1 is **76.6%**, the weakest of the
early group, and at ~470 LOC across five members it is also the largest single move attempted. The
executable extraction spec below still stands; what changes is the verification burden:

- the mutation-target choice matters more here than anywhere so far — at 76.6% branch, a seeded
  fault surviving is genuinely likely, and (per the cluster-7 lesson) that is a statement about
  coverage, **not** a licence to proceed;
- consider seeding **more than one** mutation across different members, since a single green from
  one member says nothing about the other four.

Clusters **2b and 2a remain HELD** — `unified-access-control-r2` owns the #858 create-on-save
container fix inside this file, confirmed still open with no reply as of 2026-08-29. Extracting that
cluster would land their patch against line numbers that no longer exist.

## Findings recorded, not fixed

Per the POML constraint — record, do not fix inside the restructure.

**F-070-01 — three Compose contract tests hang (~63s) instead of returning, then fail on timeout.**

`ComposeSupersedeEndpointContractTests.Supersede_WhenSessionUnknown_Returns404` ·
`ComposeMemoryResumeEndpointContractTests.SaveAnnotations_WhenSessionUnknown_Returns404` ·
`ComposeCreateOnSaveEndpointContractTests.CreateOnSave_WhenSpeCreateSucceeds_Returns200CarryingPersistedOutcome`

All three fail with `TaskCanceledException` → `HttpRequestException: Error while copying content to a
stream` → `IOException: The client aborted the request`. Three tests take **2m06s**, i.e. each hangs
about a minute and then times out. Not a logic failure — a hang.

Ownership, established rather than assumed:

- **Two are pre-existing on master.** Re-run in a master-equivalent worktree (`fix/archtest-guard-adjudication`, verified `git diff origin/master...HEAD -- src/server/api/Sprk.Bff.Api/` is empty): `Supersede_…` and `SaveAnnotations_…` fail there **identically**. Not introduced by compose-r8, and not by the current session, which changed no BFF code.
- **One is compose-r8's own**: `CreateOnSave_WhenSpeCreateSucceeds_…` does not exist on master (`git show origin/master:…` → 0 occurrences; 1 on this branch).
- **Probably environmental, NOT confirmed.** A ~63s hang is the shape of an outbound call waiting on a
  network/DNS timeout, and CI's most recent full unit run reported only ONE `Sprk.Bff.Api.Tests`
  failure — a different test (`StorageRetryPolicyTests`). So these likely pass in CI. **I did not
  confirm the cause**; do not treat "environmental" as established.

> ### ✅ RESOLVED 2026-08-29 — it WAS the network, and the earlier refutation was incomplete
>
> Root cause: the test host held the REAL `DefaultAzureCredential` that `Program.cs` registers, so
> the first request to an outbound-authenticating path probed **IMDS (169.254.169.254)** and blocked
> until `HttpClient`'s 100-second default timeout. Proven, not inferred — a later instance of the
> same hang surfaced the stack trace outright: `MsalServiceException:
> managed_identity_unreachable_network` / `SocketException (10060) … 169.254.169.254:80`.
>
> **"Probably environmental" was right; the instinct to not call it confirmed was also right.** What
> was missing was the decisive experiment, which was cheap: run a **passing sibling test alone**. It
> then fails at ~100s too — so the subject of the test is irrelevant and the host is reaching the
> network. `DefaultAzureCredential` caches which source answered, so only the FIRST caller in a host
> pays; that is why the failing set rotated and why a test could pass in the suite and fail alone.
>
> It also explains why the `.invalid` probe **refuted** the network hypothesis here and should not
> have been trusted: `.invalid` fails DNS instantly, while the real fixture hosts
> (`test.crm.dynamics.com`, `login.microsoftonline.com`) resolve. A refutation is only as good as the
> substitution it makes.
>
> Fixed at the fixture across all 52 `WebApplicationFactory<Program>` factories
> (`services.UseStubTokenCredential()`), guarded by `TestHostCredentialGuardTests`. Full record:
> [`test-host-credential-hang.md`](test-host-credential-hang.md).

Impact on the measurement above: none material — 1,780 of 1,783 passed, and all three are endpoint
contract tests, not the service-level tests that produce the coverage.


Per the POML constraint, anything that looks like a defect during this work goes here and gets filed
against its owning task. **None found yet** — this entry exists so the absence is deliberate rather than
unrecorded.
