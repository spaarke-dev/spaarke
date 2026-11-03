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
| 2 | F1 (low, behaviour) | Invite-and-grant on a self-registered (WorkforceHome) contact refuses with 409 "identity collision" and writes the collision flag on a healthy contact, while `/grant` grants the same contact. The flag feeds only the operator's collision view and never blocks sign-in. Task 047 chose this deliberately ("refused exactly like the Workforce plane") | Owner decision (identity binding); see "Open owner questions" |
| 3 | F2 | `ExternalUserContextEndpoint.cs` header described the retired Power Pages portal JWT and filter | Fixed in this task (comment only) |

Known limits from the pass (not defects on a real path):
- **K1:** CIAM and WorkforceHome requests fail the default JwtBearer scheme first, which logs a warning and runs up to three validations. This predates R3.
- **K2:** the feed marker and the identity cache are keyed by the token's tid, so a user signing in both at home and through the Spaarke tenant sees two unread counts.
- **K3:** a licensed user later linked to the same home (tid, oid) as a WorkforceHome contact resolves as the systemuser, so the contact's grants stop applying. Partly covered by 079.
- **K4:** service-request delete checks the requester (owner decision, 2026-10-11, option A). In dev-mock mode, the `pick` backend selection activates `options[0]` (dev only).

## Open owner questions
- **Finding 2.** Should invite-and-grant on a self-registered workforce contact refuse with a non-collision message and no flag, or go on to grant as `/grant` does? Recommendation: no flag, and a message pointing staff to Grant access. That stops the false operator flag without changing who can get access.
