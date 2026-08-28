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

## Findings recorded, not fixed

Per the POML constraint, anything that looks like a defect during this work goes here and gets filed
against its owning task. **None found yet** — this entry exists so the absence is deliberate rather than
unrecorded.
