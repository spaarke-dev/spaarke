# Dev deploy and server gate: 58ffc37da3 (2026-10-23)

## Deploy

R3 deployed it with the owner's approval in R3's session.

- **Build:** ProductVersion `1.0.0+58ffc37da303a6b169b9b6b3d4d1ad6539dfc94b`. OneDeploy ended 11:18:12Z; read-back at 11:19:20Z. It replaces 1fa0cf5cb8.
- **Rollback snapshot:** `deploy/rollback/wwwroot-20261023T111635Z`, in the R3 worktree.
- **Read-back:** /healthz 200, /healthz/dataverse 200, anonymous /api/me 401.
- **Content, all reviewed and accepted by UAC:**
  - 0317579272: ontology 159/161/163, including the externality mirror in SignalReadService.
  - #2304, #2308, #2309: the email identity route.
  - #2313 and #2314: the task 160 inquiry composer, the footer label gate, and the GrantAccessNotifier link overload.

## Live changes after the deploy (ontology, #1355 read-back)

- **Worklist config:** applied 11:20:10Z; `-Verify -RequireRow` in sync at 11:20:18Z.
- **POL-MATTER-NOBUDGET v2:** 11:20:38Z (D-186).
- **D-185 role change:** `RemovePrivilegeRole` on root-BU Spaarke Console User (`51c924e2`) removed `prvReadsprk_Policy` (11:21:51Z) and `prvReadsprk_PolicyVersion` (11:21:53Z).
  - UAC's own read-only check: 19 privileges, neither Read left, `prvReadsprk_Signal` kept at depth 4.
  - `prvAppendTosprk_Policy` and `prvAppendTosprk_PolicyVersion` are still on the role. The role has no write path that uses them, so no action was taken.
- **Spaarke Basic User:** unchanged. It already lacked the policy, policy-version and ruleset Reads, and matches master apart from ebilling's in-flight tables.
  - The cause of the 00:01:07Z change is unknown: dev's `audit` table holds no rows for the role entity.

## Server gate: PASS

- **Window:** 11:18:00Z → 11:51:05Z. It covers the deploy, the config change and the role change.
- **Source:** App Insights `spe-insights-dev-67e2xz`, role `spaarke-bff-dev`. Read-only.
- **Independent check:** R3's 30-minute read (11:18–11:49Z) agrees.

| Check | Result |
|---|---|
| Requests | 77. The 9 failures are all 401: `GET /` ×7, a robots probe and anonymous `/api/me` |
| 403 / 5xx | 0 / 0 |
| `worklist_unavailable`, `signal_unreadable`, deny-list refusals | 0 |
| New exception classes caused by the deploy | None |

**Coverage is thin.** The only user route in the window is `GET /api/communications/by-regarding/...` (28 × 200). The signals worklist, the email identity route, decision plan/commit and the inquiry composer were not exercised. Those checks stay on the owner's browser checklist.

**Exceptions:** one `RedisConnectionException` at 11:21:20Z. The scheduler could not renew the `secure-child-reconciliation` lease shortly after startup. It is the first of that type in 30 days, but it is restart-class Redis noise like the timeouts on earlier deploys, and scheduler traces continue to 11:50Z.

**Pre-existing warnings, unchanged:**
- `[SECURE-CENSUS] HumanPrincipalReachesSecureBusinessUnit` (task-144 clause 1): 11 users in the window, 12 distinct in the 6 hours before.
- `[ID-LINK-RECON] spaarke-acme` binding-column failure (#2163).
- `RoutingConsumerTypeHealthCheck` catalog drift: 2–6 times a day on each of the previous 8 days.
- Reconciliation collisions `contact_bound_to_different_oid` 14 and `linked_contact_bound_to_different_oid` 7, against 12 and 6 in the same window the day before (R3's count).
