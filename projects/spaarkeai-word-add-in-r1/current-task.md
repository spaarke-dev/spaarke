# Current Task

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | none — 032 complete |
| **Task File** | — |
| **Phase** | 3 Surfacing Spaarke |
| **Status** | not-started |
| **Started** | — |
| **Next Action** | Operator picks the next task. **033 is NOT yet startable**: 032 (its security gate) is ✅, but 033 also depends on **015** and **013**, both 🔲. |

## Critical Context

Task **032 closed on outcome (a) — hardened, not descoped.** The visualization similarity surface now
authorizes every result row against its own `sprk_document` record, both routes refuse (500) without the
published per-row obligation, and `Api/Ai/VisualizationEndpoints.cs` is in the `RouteAuthorizationGuardTests`
governed-file census. The NFR-02 negative test passes and was verified to FAIL (7 of 11) with the row check
disabled. **The Find gate is met — 033 may be built on this surface.**

Nothing is committed or pushed. Full record: `notes/032-authorization-hardening.md`.

## For whoever picks up 033/034

- Size the Find view against **~1–3 s** for a first uncached similarity call at the default limit, near-instant
  inside the 60 s access cache. Authorization is **not** the dominant latency term — `GetDocumentMetadataAsync`
  already does an unbounded sequential Dataverse fetch per document. Arithmetic in `notes/032-…` §5.
- `?countOnly=true` is no longer a cheap call: its service-side fast path was a count side channel and is
  closed, so a count query now costs the same as a full one.
- **A short result page may mean "withheld", not "nothing matched"** — rows past the 100-check budget are
  dropped with no warning to the client, because `GraphMetadata` has no warnings channel (deferral D-032-2).
- Orphan-file nodes (indexed SPE files with no Dataverse row) are never served. If the Find UI expected them,
  that is a product decision to raise, not a filter to relax.

## Completed Steps (task 032)

- [x] 0 context + rigor declaration + conflict check
- [x] 1 fail-closed forcing function on both handlers
- [x] 2 per-row trimming, dedup memo, 100-check budget, edge + orphaned-hub pruning, counts recomputed
- [x] 3 authorization filter on `POST /related-from-content`, publishing the same obligation
- [x] 4 ArchTests `GovernedFiles` census entry (191/191 pass; validated by detaching the filter → Rule A FAIL)
- [x] 5 NFR-02 negative test (11 pass; verified to fail 7/11 with the row check disabled)
- [x] 6 contract coverage for both routes incl. the forcing-function 500 case
- [x] 7 publish size vs fresh `origin/master`: 45.35 → 45.36 MB (+0.01), Compress-Archive Optimal
- [x] 8 `dotnet build` clean (`-warnaserror` too); full BFF suite 12,078 passed / 0 failed / 58 skipped
- [x] 9 TASK-INDEX 032 → ✅
- [x] 9.5 quality gates — `code-review` + `adr-check`: 0 critical, 0 ADR violations, 2 warnings fixed (Path C)
- [x] 10 `notes/032-authorization-hardening.md`; 2 findings filed in `notes/defer-issues.md`

## Decisions Made

- **Trim in the ENDPOINT, not `VisualizationService`** — mirrors `RecordSearchEndpoints`; keeps `HttpContext`
  and claims out of the service layer (ADR-008). `VisualizationService.cs` is unmodified despite being listed
  `role="modify"` in the POML. Deviation recorded in `notes/032-…` §7.
- **`countOnly` does not take the service fast path** — that path computes a total from unauthorized rows with
  no nodes to trim. Latency cost accepted and recorded rather than traded for a leak.
- **Orphan-file nodes dropped; hubs with no surviving document dropped** — "no record to evaluate" must not
  resolve to "serve it", and a hub's bare existence is itself a count.
- **`tests/integration/tenant/Ai/TenantSelectionByRequestTests.cs` updated** (outside the POML file list) —
  the new forcing function correctly broke two pre-existing tests that call these handlers directly.
- **No §6.5 ADR conflict arose.** Both Step 9.5 findings resolved on Path C.
