# Phase 0 Proof Gate — Decision (task 006)

> **Date**: 2026-07-22
> **Decider**: task-execute (main session, autonomous per owner directive) — Phase 0 gate is machine-verifiable
> **Verdict**: 🟢 **GREEN — cutover AUTHORIZED**
> **Spec basis**: spec NFR-08 (hard-replace safety net), Success Criterion 8; design §6 Phase 0.

---

## What this gate authorizes

A GREEN gate is the HARD prerequisite (spec FR-12 / NFR-08) that unblocks the **cutover / old-path deletion tasks**: **023** (delete client paragraph-diff export), **032** (retire both legacy writers), **060** (hard-replace completion / remove mammoth). Those tasks remain `blocked` in TASK-INDEX until this gate is green — it now is.

## Evidence (all three criteria GREEN)

| # | Criterion | Verifying artifact | Result |
|---|---|---|---|
| a | Operation schema implemented + round-trips (FR-11) | task 003 — `Services/Compose/Operations/ComposeOperation.cs` (C# union) + `compose-operations.ts` (TS discriminated union); `ComposeOperationSchemaTests.cs` + `compose-operations.test.ts` | ✅ 10-op closed set; round-trips server 4/4, client 9/9; Tier-1 NetArchTest (`ADR013_ComposeFacadeTests`) green; publish 46.11 MB |
| b | Byte-diff harness green on corpus (NFR-01) | task 004 — `tests/integration/seam/Compose/ComposeNoOpRoundTripByteDiffSeamTests.cs` (+ glob fixture locator + OPC part comparer) | ✅ 3/3 corpus docs pass no-op byte-diff in BOTH strict-byte-identity and loose-structural modes |
| c | Applier spike lands interior edits on CIPO + A/B decision (FR-04/FR-11) | task 005 — `notes/patch-engine-ab-decision.md` + `spike/ComposeApplierSpike/` | ✅ 3 interior ops (insertText / deleteRange / para-mark-delete) landed by `w14:paraId`+runIndex+offset with **zero write-path text-search**, 0 `OpenXmlValidator` (Office2019) errors |

**Escalation triggers**: the "neither engine works on the CIPO doc" trigger (task 005) did **not** fire — Candidate B (build-on-OpenXML-SDK) works cleanly. No §6.5 ADR conflict surfaced beyond the already-ratified Path-B (ADR-049).

## Binding decision recorded: patch-engine A/B (FR-04/FR-11)

**Build the `ComposeShadowPatchEngine` on `DocumentFormat.OpenXml`** (already a BFF dependency — **zero new runtime package**). **Docxodus is REJECTED**:
- Its only redline surface is `WmlComparer` — a whole-document *differ*, not an offset applier; no `(paraId,runIndex,offset)` surface to drive.
- R3's NFR-09 gate already proved `WmlComparer` **strips `w14:paraId` and drops tables** — destroys invariant I-3.
- +12.9 MB uncompressed (SkiaSharp native libs) — threatens the 60 MB HARD STOP.
- Only 6.4.0 is net8-compatible (7.1.0 is net10-only).

This resolves the spec Unresolved Question "Docxodus adoption." Task **030** builds on the spike's `SpikeOpenXmlApplier` as the production nucleus.

## Documented limitation (NOT a gate failure) — corpus coverage

Per task 002's honest finding (`notes/task-002-corpus-deviations.md`), the 3 sample docs carry **fewer OOXML worst-offender features than the WBS assumed** (CIPO doc is track-changes-clean as saved; only its footer page-number SDT is real SDT coverage; no numbered clauses; no field codes). The gate therefore certifies that the **architecture is sound on real documents** (schema + offset applier + byte-preservation all proven), but the corpus does not yet exercise:
- pre-existing tracked-changes **import round-trip** (FR-10),
- rich **fields / content controls / SDT** (FR-02),
- complex multi-level numbering / multi-section fidelity.

**Owner-directed resolution (2026-07-22)**: build to the current corpus as the "worst case we have"; keep the fidelity design **flexible** for additional example docs (the harness already globs the fixtures dir — new docs are auto-covered). Owner worst-offenders folded in during later phases raise the NFR-01/FR-10 certification bar; they do **not** gate Phase 0. Tracked as the two placeholder rows in `corpus-manifest.md`.

## Outcome

🟢 **Phase 0 gate GREEN.** Cutover/deletion tasks 023 / 032 / 060 are authorized (still subject to their own deps). Proceed to Phase 1 (backend ingest).
