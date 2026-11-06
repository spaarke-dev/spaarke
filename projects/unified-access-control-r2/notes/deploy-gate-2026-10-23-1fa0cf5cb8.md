# Dev deploy and server gate: 1fa0cf5cb8 (2026-10-23)

## Deploy

R3 deployed it with the owner's direct approval in R3's session.

- **Build:** ProductVersion `1.0.0+1fa0cf5cb88443d9a4279851e558495d71ec9169`, first read 2026-10-23 00:00:36Z (app start 23:59:42Z). It replaces 1283c41817.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261022T235642Z`, in the R3 worktree.
- **Read-back:** /healthz 200, /healthz/dataverse 200, anonymous /api/me 401.
- **UAC content:**
  - #2253: the configuration deny-lists, #2251.
  - #2268: task 192 / #1830.
  - #2258: #2257, a person's run writes as the person.
  - #2277: #2273, creator-person trust only on stamped tables.
- **Also in the build:** R3 #2279 and field-mapping #2282 (#2281).

## #1830 container steps (owner round 125)

All steps were approved by the owner in R3's session.

- **Step 3, before the deploy:** `SharePointEmbedded__OwnedContainerIds` went from 2 ids to 1. `b!vzGD…` was kept and `b!MVasATu…` removed.
- **Steps 0–2, project 65a3fab2:**
  - Step 0 read-back matched.
  - The container id was cleared under If-Match.
  - `provision-project` returned 200 at 00:22:42Z (resumed=true), with a new container `b!euxXOoXL…`. The new container is held by 1 project only. The owner team and the single share are unchanged.

## Server gate: PASS

- **Window:** 00:00:00Z → 00:30:36Z.
- **Source:** App Insights `spe-insights-dev-67e2xz`, role `spaarke-bff-dev`. Read-only.

| Check | Result |
|---|---|
| "Invalid hostname for this tenancy" | 0 (about 6 per 30 min before the deploy) |
| `[SPE-MEMBERSHIP-SYNC]` heartbeats | 7 runs, all `failed=0 unresolvable=0`, 7 secure containers |
| 65a3fab2 "not one of this stamp's containers" | Present until 00:20; absent from the 00:25 and 00:30 runs |
| Deny-list refusals | 0 |
| Requests | 133; 0 5xx; the 9 failures are 401 and 404 |
| New exception classes caused by the deploy | None |

**Exceptions explained:**
- **Restart noise.** Redis lease timeouts ×14 and StackExchange.Redis "Reading is not allowed after reader was completed" ×13, all between 00:00:04 and 00:00:15Z. Redis timeouts were also seen on earlier deploys. None recur after 00:10.
- **Three Dataverse 4-minute timeouts at 00:04:00Z,** on calls started at the restart.
- **`'matter' … not found in the Metadata`:** pre-existing, 37 in 30 days.
- **`sprk_userentityassociation` alternate-key fault:** pre-existing, about 850/day. It is a real defect: the membership junction is never populated (0 rows on dev). Filed as #2292. It is not an access input.

**Pre-existing warnings:**
- `[SECURE-CENSUS] HumanPrincipalReachesSecureBusinessUnit` (task-144 clause 1);
- `[ID-LINK-RECON] spaarke-acme` (#2163).

**Closed by this gate:** task 192, plus the #2251 (#2253), #2257 (#2258) and #2273 (#2277) fixes. Owner browser checks stay on the checklist.
