# BFF Publish-Size Report — Template + Workflow (Task 070)

> **Operationalizes**: `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule (NFR-01)" + `docs/adr/ADR-029-bff-publish-hygiene.md`.
> **Harness**: [`scripts/Measure-BffPublishSize.ps1`](../scripts/Measure-BffPublishSize.ps1)
> **Baseline ledger**: [`notes/publish-size-baseline.json`](publish-size-baseline.json)

---

## 1. Who must run this

**Every task in `spaarke-ai-architecture-redesign-r2` that touches** `src/server/api/Sprk.Bff.Api/` (or `Spaarke.Core` / `Spaarke.Dataverse` consumed by BFF) — endpoint additions, service additions, DI registration changes, NuGet package changes, background-job work — MUST run the harness once, AFTER changes land and BEFORE merge, per root CLAUDE.md section 10 item 4 and `.claude/constraints/azure-deployment.md`.

This is the per-task reporting workflow referenced by CLAUDE.md section 10; task-execute Step 9 (Verify Acceptance Criteria) is where a BFF-touching task should paste the report line into its task notes / PR description.

## 2. How to run it

```powershell
# One-shot: publish + measure + classify + record as a new baseline entry
# attributed to your task id (recommended before merge)
.\projects\spaarke-ai-architecture-redesign-r2\scripts\Measure-BffPublishSize.ps1 `
    -TaskId <task-id> -Notes "<one-line description>" -RecordBaseline

# Ad-hoc / iterative check without persisting to the baseline ledger
.\projects\spaarke-ai-architecture-redesign-r2\scripts\Measure-BffPublishSize.ps1

# Re-measure existing output without re-publishing (fast iteration)
.\projects\spaarke-ai-architecture-redesign-r2\scripts\Measure-BffPublishSize.ps1 -SkipPublish
```

Run from the repo root (or any subdirectory — the script auto-detects the repo root via `git rev-parse --show-toplevel`).

## 3. What it reports

The harness prints a single copy-pasteable report line in this format (matches the CLAUDE.md section 10 citation convention):

```
BFF Hygiene section 10 + NFR-01 verified: publish size = <X> MB (incl. PDBs; <Y> MB excl.), delta = <+/-Z> MB vs prior (<prior source>), CVE check: <summary>.
```

Paste this line into:
- The task's `current-task.md` / task notes section, AND
- The PR description (per `.claude/constraints/azure-deployment.md` rule 5: "cross-reference CLAUDE.md section 10 in the task notes / PR description").

## 4. PDB convention (binding — do not silently switch)

`compressed_incl_pdb_mb` is the **canonical deploy-lineage figure** — it matches the actual zip `Deploy-BffApi.ps1` ships (including the 4 `.pdb` files). **All threshold comparisons use this figure.** `compressed_excl_pdb_mb` is informational only (source-hygiene lineage; PDBs currently compress to ~0.7–3.8 MB depending on symbol volume). This mirrors the exact convention ADR-029's "Publish-Size Baseline Ratchet" table uses.

## 5. Thresholds (ADR-029 / NFR-01 — binding)

| Condition | Action |
|---|---|
| Compressed (incl. PDB) ≥ 60 MB | **HARD STOP.** Do not merge. Roll back or extract; do not exceed the ceiling without an ADR-029 amendment. |
| Compressed (incl. PDB) ≥ 55 MB | **Architecture review** required before merging the task that tips it over. |
| Delta vs the last recorded baseline ≥ +5 MB (single task) | **Explicit justification required** in the PR description; reviewer must explicitly accept. |
| Otherwise | OK — report the figures, no escalation needed. |

The harness prints all applicable classifications (a measurement can trigger more than one, e.g. both the cumulative-review threshold and the single-task delta).

## 6. R2 baseline (recorded 2026-07-10, task 070)

| Metric | r1 close (2026-07-08, task 055, prior baseline) | R2 branch (2026-07-10, task 070) | Delta |
|---|---:|---:|---:|
| Compressed, incl. PDBs (canonical) | 49.63 MB | **46.59 MB** | **-3.04 MB** |
| Compressed, excl. PDBs (informational) | 45.87 MB | 45.83 MB | -0.04 MB |
| Uncompressed, incl. PDBs | 143.75 MB | 140.67 MB | -3.08 MB |
| File count | 247 | 240 | -7 |
| `runtimes/` file count | 0 | 0 | — |
| Sourcemap (`.map`) count | 0 | 0 | — |
| CVE check | 1 pre-existing accepted-risk HIGH (Kiota `GHSA-7j59-v9qr-6fq9`) | Same 1 pre-existing accepted-risk HIGH — **no new HIGH CVEs** | — |

**Classification: OK — within all ADR-029 / NFR-01 thresholds.** Ceiling headroom at R2 start: 13.41 MB (46.59 → 60 MB HARD STOP); 8.41 MB to the 55 MB architecture-review threshold.

**Reconciliation vs the r1 "G-P3-close ~46.8 MB" figure cited in the task 070 POML/spec**: r1 never measured publish size specifically at gate G-P3 — it measured only at G-P0 (project start, 46.87 MB) and G-P4 (project close, 49.63 MB). The ~46.8 MB figure the POML references corresponds to the **G-P0 project-start actual** (46.87 MB), not a distinct G-P3 measurement. This harness treats the r1 **G-P4 close-out figure (49.63 MB)** as the authoritative prior baseline to diff against, per ADR-029's own statement that it "reset [the baseline] to 49.63 MB so downstream diffs are honest." Full detail in [`publish-size-baseline.json`](publish-size-baseline.json) `reconciliation_note`.

**Why R2's branch measures lower than r1's close-out**: the r1 → r2 branch point already carries the Track-B deletions and does not (yet) carry the R2-specific net-new capability that will accumulate through this project's own tasks (Memory Service, gate/completion machinery, Context Binder, etc.). Expect this figure to rise as R2 tasks land; the harness's per-task `-RecordBaseline` invocations will track that trajectory against the same three thresholds.

## 7. Baseline ledger format

`publish-size-baseline.json` is an append-only history array plus a `current` pointer (the most recent entry). Each entry:

```json
{
  "date": "YYYY-MM-DD",
  "source": "spaarke-ai-architecture-redesign-r2 task <id>",
  "task_id": "<id>",
  "compressed_incl_pdb_mb": 0.0,
  "compressed_excl_pdb_mb": 0.0,
  "uncompressed_incl_pdb_mb": 0.0,
  "file_count": 0,
  "runtimes_file_count": 0,
  "sourcemap_count": 0,
  "delta_vs_prior_mb": 0.0,
  "classification": "...",
  "cve_check": "...",
  "notes": "..."
}
```

`-RecordBaseline` appends a new entry and updates `current` automatically — do not hand-edit the ledger except to seed reconciliation history (as this task did for the r1 lineage).

## 8. Negative constraint honored

This harness measures existing publish output only. It does **not** modify `Sprk.Bff.Api.csproj` and adds **no new runtime NuGet dependency** to the BFF — verified by `git diff --stat -- src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` returning no output after a harness run.
