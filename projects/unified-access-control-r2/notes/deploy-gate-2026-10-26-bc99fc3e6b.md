# Dev deploy and server gate: bc99fc3e6b (2026-10-26)

## Deploy

R3 deployed it with the owner's approval in R3's session.

- **Build:** ProductVersion `1.0.0+bc99fc3e6b06`. OneDeploy ended 22:03:10Z; the app was back by 22:04:30Z after one warm-up `/healthz` 503. It replaces 58ffc37da3.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261026T220116Z` (58ffc37da3), in the R3 worktree.
- **UAC content:**
  - #2318 (7479ce22a9): census clause 1 guards Read on every codified child table.
  - #2321 (bc99fc3e6b): access-record readability is proven only by the platform.

## Server gate: PASS

- **Window:** 22:03:00Z → 22:40:00Z.
- **Source:** App Insights `spe-insights-dev-67e2xz`, role `spaarke-bff-dev`. Read-only.
- **Independent check:** R3's read (22:03–22:36Z) agrees.

| Check | Result |
|---|---|
| Requests | 40. Of the 8 failures, one is the warm-up `/healthz` 503; none is a 403 |
| 403 / other 5xx | 0 / 0 |
| `access_record_hidden` / `access_record_not_secured` | 0 / 0. #2321's proof passes live: dev's BFF app user holds System Administrator directly |
| New exception classes | None. RedisTimeoutException ×4 (scheduler leases for the two reconciliation jobs, restart-class) and TaskCanceledException ×1, both in the 7-day baseline |
| `[FOLLOW-PARENT]` / `[SECURE-INHERIT]` | Run normally: unsecured=0, refused=0, failed=0 |

**Coverage is thin.** No user route touched by the content ran in the window. The cascade proof is exercised by the scheduled jobs, and they ran clean.

## Census: the new child-table findings are expected

#2318 adds `SECURE CHILD RECORDS ARE NOT ISOLATED (codified child tables …)` at Critical. That severity is by design:
- exposure findings are Critical;
- a coverage gap is Error;
- a missing secure BU is Warning.

The findings are one aggregated line per principal, role and anchor. They name:

| Role | Depth | How held | Child tables reached |
|---|---|---|---|
| Spaarke Core User | Deep | direct | 20 |
| Spaarke Basic User | Deep | direct, and through the root BU's default team `Spaarke` (so every root-BU user) | 20 |
| Spaarke Office Add In User | Deep | direct | event, invoice, memo |
| Spaarke Console User | Deep | direct | budget revision, decision record, signal |

**Every grant is anchored at the root BU.** The root BU is an ancestor of `Secure Record`, so any Deep grant held there reaches it.

This is the same cause as the `SECURE PROJECTS ARE NOT ISOLATED` lines that have run since task 144. The decided fix is the BU restructure in design §5.2 (spec FR-18): humans move out of the root BU, and the secure BU becomes a sibling. The owner reclassified that work on 2026-08-21 as environment configuration for UAT. Under the restructure, Deep depth stays correct, so **no role-depth change is needed** on the per-table grants.

Not covered by the restructure: the Global grants. Those are Billing Administrator (EB-D-150) and AI Analysis Admin (UAC round 127), and both are pinned accepted exceptions in #2322.

**Pre-existing, unchanged:**
- `[ID-LINK-RECON] spaarke-acme` binding-column failure (#2163);
- `RoutingConsumerTypeHealthCheck` catalog drift ×1;
- reconciliation collisions (R3: 21 deny lines).
