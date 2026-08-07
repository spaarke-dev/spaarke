# BFF Publish-Size Baseline — R6 Re-Confirmation (Task 003)

> Re-measures the BFF publish-size baseline per root `CLAUDE.md` §10 bullet 4 + NFR-01
> (`.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule").
> This is the reference number every subsequent R6 BFF-touching task (010/011/012/014) reports
> its absolute size + delta against.

## Measurement

| Item | Value |
|---|---|
| **Date** | 2026-08-05 |
| **Commit SHA** | `511976d7f` (branch `work/spaarkeai-compose-r6`) |
| **Command** | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` |
| **Build result** | Succeeded — 0 errors, 23 warnings (pre-existing nullable/obsolete/CS1998 warnings, not introduced by this measurement task) |

### Sizes

| Convention | Size | Notes |
|---|---|---|
| **Compressed, incl. PDBs** ← **R6 per-task reference convention** | **48.25 MB** | `Compress-Archive -Path deploy\api-publish\* -CompressionLevel Optimal` over the full publish output (matches the 2026-07-08 task-055 recipe cited in `azure-deployment.md`). |
| Compressed, excl. PDBs | 47.4 MB | Same recipe, `*.pdb` files excluded before zipping (only 4 `.pdb` files present in this publish output — the incl./excl. delta is smaller than the 2026-07-08 measurement, which is informational only; not investigated further as it's out of scope for this measurement-only task). |
| On-disk, incl. PDBs | 145.13 MB | `deploy/api-publish/` total, uncompressed, 251 files (4 `.pdb`). |
| On-disk, excl. PDBs | 143.01 MB | Same tree, `.pdb` files excluded from the sum. |
| Zip entry count | 254 | Compare to the ~247-baseline convention noted in `azure-deployment.md` line 43. |

**PDB convention used**: **compressed, incl. PDBs = 48.25 MB** is the number this project (R6) will report as "publish size" in subsequent task notes / PR descriptions, matching the incl.-PDB convention root §10 and the prior 2026-07-08 baseline used.

### Comparison to prior baseline (2026-07-08, `spaarke-ai-architecture-redesign-r1` task 055)

| | Prior baseline (2026-07-08) | This measurement (2026-08-05) | Delta |
|---|---|---|---|
| Incl. PDBs (compressed) | 49.63 MB | 48.25 MB | **−1.38 MB** |
| Excl. PDBs (compressed) | 45.87 MB | 47.4 MB | +1.53 MB (informational — see PDB-count note above) |

**No escalation triggered.** The re-confirmed incl.-PDB baseline (48.25 MB) is below the ~49.63 MB prior reference, well under the ≥55 MB architecture-review threshold and the ≤60 MB hard ceiling.

### Thresholds (restated per root §10 / NFR-01)

- **Ceiling: ≤60 MB compressed** — HARD STOP if exceeded; roll back or extract, do not merge over the ceiling without an ADR-029 amendment.
- **≥55 MB cumulative** — escalate to architecture review before merging the task that tips it over.
- **≥+5 MB single-task delta** — explicit justification required in the task's PR description; reviewer must explicitly accept.
- Current headroom to the 60 MB ceiling: **11.75 MB** (measured against the 48.25 MB incl.-PDB figure).

## Baseline CVE check (informational)

`dotnet list package --vulnerable --include-transitive` (`src/server/api/Sprk.Bff.Api/`):

- **1 vulnerable top-level package**: `System.Security.Cryptography.Xml` 8.0.3 (resolved 8.0.3) — **5 High-severity advisories**:
  - https://github.com/advisories/GHSA-g8r8-53c2-pm3f
  - https://github.com/advisories/GHSA-8q5v-6pqq-x66h
  - https://github.com/advisories/GHSA-cvvh-rhrc-wg4q
  - https://github.com/advisories/GHSA-23rf-6693-g89p
  - https://github.com/advisories/GHSA-mmjf-rqrv-855v

This is a **pre-existing** finding, unrelated to this measurement task (no packages were added/removed/upgraded). Recorded here purely as a baseline reference point so later R6 tasks can diff against it per NFR-01 item 4 ("verify no new HIGH-severity CVEs" — compare future runs to this list, not zero). Not remediated as part of this task (out of scope; task 003 is measurement-only).

## Scope confirmation

- No package, DI, or source-code change was made as part of this measurement.
- `deploy/api-publish/` is **not committed** — confirmed via `git check-ignore -v deploy/api-publish` → matched by `.gitignore:132` (`deploy/`). Temporary zip artifacts created during measurement (`deploy/api-publish.zip`, `deploy/api-publish-noPdb.zip`, `deploy/api-publish-count.zip`, and a scratch `deploy/api-publish-noPdb-temp/` copy) were deleted after measurement; only `deploy/api-publish/` (already gitignored) remains on disk.
- Sole committed/written artifact from this task is this notes file.

## Deviation from POML step 4

The task POML's step 4 ("Update TASK-INDEX.md: set this task's status to ✅") was **not executed**.
The dispatching instruction for this run explicitly constrained the agent to NOT edit `TASK-INDEX.md`,
`current-task.md`, or any `.claude/**` file, and to stay within `projects/spaarkeai-compose-r6/notes/`
for writes. That explicit run-time constraint takes precedence over the POML's default step 4. The
project owner / orchestrating session should update `TASK-INDEX.md` to mark task 003 complete
separately.
