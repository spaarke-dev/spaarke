# Task 090: project wrap-up notes

## Pre-work (2026-11-03, owner decisions)

- The owner's researcher memory files were committed on the R3 branch (02d024f9d3). origin/master was merged in (e9759b5d5a). `MEMORY.md` was resolved as a union: the owner's shortened index lines plus master's 8 new entries, with every link resolving. The BFF builds with 0 warnings.
- 073 (production go-live) moved to customer-provisioning-orchestration-r1: #2532, ISS-030. R3 closes without it.

## Step 1: code review and ADR check

### Scope
- **R3's code surface** is the union of files changed by its merged PRs (#1795, #1799, #1880, #2086, #2141, #2175, #2279, #2360, #2386, #2450, #2461, #2500): 514 files that still exist.
- **Every task already ran its own code review and ADR check**, with adversarial passes on the auth tasks.
- **This pass:**
  - a check of every finding R3 raised;
  - the mechanical suites;
  - one full adversarial pass (opus) over the 75 core auth and authorization files, looking for defects between tasks.

### Findings R3 raised
- All are fixed or filed on GitHub.
- `notes/defer-issues.md` was synced with GitHub: ISS-013, ISS-015, ISS-016 and ISS-018 marked resolved.
- Open filed issues are listed in the README hand-off section, with an owner for each.

### Mechanical results
| Check | Result |
|---|---|
| BFF build | 0 warnings, 0 errors |
| BFF unit tests, external-access filter | 5,233 / 5,233 pass |
| ArchTests (full) | 1,911 / 1,911 pass |
| SPA typecheck / lint | pass / pass |
| SPA vitest | the first run, alongside dotnet builds, showed 26 / 1,290 failing (60 s timeouts and DOM-cleanup errors); the reviewer's quiet run passed 1,290 / 1,290. A re-run on its own is recorded below |

The `--filter` run on the integration project matched no tests. The `tests/integration/auth/**` and `contract/**` suites compile into `Sprk.Bff.Api.Tests` (namespace `Sprk.Bff.Api.Tests.AccessControl`). The full unit project run is recorded below.

### Adversarial pass: findings
| # | Class | Finding | Action |
|---|---|---|---|
| 1 | F1 (UX) | Self-registration showed "Please try again" for the two 500s that only an operator can fix (`registration_owner_unconfigured`, `registration_environment`). /me and the account-link path already treat these as non-retry | Fixed in this task (see below) |
| 2 | F1 (low, behaviour) | Invite-and-grant on a self-registered (WorkforceHome) contact refuses with 409 "identity collision" and writes the collision flag on a healthy contact, while `/grant` grants the same contact. The flag feeds only the operator's collision view and never blocks sign-in. Task 047 chose this deliberately ("refused exactly like the Workforce plane") | **Owner approved 2026-11-03** ("yes make that change"): a WorkforceHome-bound contact gets a non-collision 409 (`sdap.access.invite.workforce_home_contact`, "Already signs in with a work account", pointing staff to Grant access), and no flag is written. The Workforce plane is unchanged. Earlier false flags clear on reconciliation |
| 3 | F2 | `ExternalUserContextEndpoint.cs` header described the retired Power Pages portal JWT and filter | Fixed in this task (comment only) |

Known limits from the pass (not defects on a real path):
- **K1:** CIAM and WorkforceHome requests fail the default JwtBearer scheme first, which logs a warning and runs up to three validations. This predates R3.
- **K2:** the feed marker and the identity cache are keyed by the token's tid, so a user signing in both at home and through the Spaarke tenant sees two unread counts.
- **K3:** a licensed user later linked to the same home (tid, oid) as a WorkforceHome contact resolves as the systemuser, so the contact's grants stop applying. Partly covered by 079.
- **K4:** service-request delete checks the requester (owner decision, 2026-10-11, option A). In dev-mock mode, the `pick` backend selection activates `options[0]` (dev only).

### Full BFF test project (2026-11-03)
- **Full run:** 39,105 passed and 255 failed of 39,388 (28 skipped) in 1 h 48 m. Two other sessions were running full BFF suites on the machine at the same time (project CLAUDE.md gotcha, 2026-10-11).
- **Isolated re-run of the 96 failing classes:** 3,444 / 3,445 passed. **Second pass**, the 12 classes behind failures reported only by display name: 380 / 380 passed.
- **The one repeat failure:** `ProvisionProjectIdempotencyTests.ProvisionProject_WhenRecordingTheContainerTimesOut_FailsLoudlyAndLogsTheContainerId` (UAC-r2's, #1755). It is a real-clock 30 s `WaitAsync` timeout under contention, and it passed 5 / 5 run alone. The endpoint has no logic race (its bound is created on the host `TimeProvider` before the write). Filed to UAC-r2 as #2543.
- **Verdict:** no code defect. Every failure was load.

### SPA vitest (2026-11-03)
- **Full runs** under machine load: 26 and then 28 of 1,292 failed (15 hit the 60 s per-test timeout; 13 could not find an element in the default wait).
- **The 15 failing files re-run with 2 workers:** 465 / 465 passed. The implementer's quiet full run after the registration fix passed 1,292 / 1,292.
- **Verdict:** load, no code defect.

### BFF hygiene (§10): publish size and CVEs (2026-11-03)
| Side | SHA | Files (PDBs) | Zip |
|---|---|---|---|
| master | `249809ce0b` | 192 (4) | 41,211,230 B |
| branch | `06d4d176d9` | 192 (4) | 41,217,245 B = 39.308 MB |
| **delta** | | 0 | **+6,015 B (+0.006 MB)** |

Both sides are fresh detached worktrees at short paths (`C:\wt090m`, `C:\wt090b`; removed with `Remove-WorktreeSafe.ps1`). Each was built with `dotnet publish -c Release` and zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`. The size is far below the 55 MB review line and the 60 MB ceiling. `dotnet list package --vulnerable --include-transitive`: no vulnerable packages on either side. BFF Hygiene §10 and NFR-01 are verified.

## Open owner questions
- None. Finding 2 was answered 2026-11-03.

## Test diet
- Report: `notes/test-diet-report.md`. Reviewer decisions are at its end: 32 R3 test files moved to KEEP paths; 1 test rewritten; the Stopwatch timing assertion removed; no deletions.

## Repo cleanup (`/repo-cleanup`, project scope, 2026-11-03)
| Category | Result |
|---|---|
| Test diet (Step 1.5) | Done: `notes/test-diet-report.md`, with the reviewer decisions at its end |
| Ephemeral directories | `notes/debug/` and `notes/handoffs/` held only `.gitkeep`: removed. `notes/drafts/` (ADR-028 A7/A9 drafts, now merged into ADR-028) and `notes/spikes/fr-12-self-registration-decision.md` (decision record) are **kept**, because task notes and POMLs 001, 003, 056 and 060 link to them |
| Naming / spaces | None |
| Large files (> 1 MB) | None |
| Conflict markers | None (project, external SPA, tests) |
| Untracked source files | None. Untracked notes (`lessons-learned.md`, `test-diet-report.md`) are committed with the wrap-up |
| Required project files | spec, README, CLAUDE.md, plan all present |
| Archive (portfolio hook) | Not yet. The branch is not merged; run `/devops-project-archive` after `/merge-to-master` |
