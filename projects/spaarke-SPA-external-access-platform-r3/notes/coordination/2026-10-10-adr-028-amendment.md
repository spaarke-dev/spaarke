# ADR-028 amendment split — R3 A8 vs the auth project's §12b (2026-10-10)

**To:** `spaarke-auth-system-of-record-r1`. Its `current-task.md` plans "amend ADR-028 — record §12b, plus the R3 decisions".
**From:** `spaarke-SPA-external-access-platform-r3`, task 001.

## Decision (owner-approved 2026-10-10)

The ADR-028 changes go out as **two separate dated amendments**:

| Amendment | Carries | Owner | Status |
|---|---|---|---|
| **A8** (2026-10-10) | The R3 decisions: A2 single-tenant workforce client, NAA then popup, no `webApplicationInfo` or SSO fallback; `user_impersonation` on both planes; A3 default Tier-1 modules by user type; A1 :52/:56 scoped to CIAM partners; the workforce-contact self-registration rule | R3 | **Applied** to `.claude/adr/ADR-028-spaarke-auth-architecture.md` on branch `work/spaarke-SPA-external-access-platform-r3` (merges with R3's first PR) |
| **A8 or later** | The §12b items from `auth-system-of-record.md` (rules to rewrite, retire or re-scope from the §10 verdicts) | auth-system-of-record-r1 | Not started |

**Why separate:**
- They change different clauses.
- §12b is not finished.
- R3's Wave 2 (tasks 011, 012, 040) needs A8 first.

**What the auth project should do:** drop "plus the R3 decisions" from its amendment scope. Rebase onto A8 and treat A8's rules as given.

## Follow-ups the auth project owns (not changed by R3)

- **`.claude/patterns/auth/oauth-scopes.md:20`** still says the BFF scope is `api://{clientId}/access_as_user`.
  - That is still true for the internal surfaces (PCFs, Code Pages).
  - For the module-host SPA and the Teams host, A8 replaces it with `user_impersonation`.
  - Update the pattern when the auth project does its pattern sweep.
- **`.claude/constraints/auth.md`** and **`spaarke-sso-binding.md`**: no contradiction found. INV-3 (tenant-specific authority) matches A8.
- **The architecture doc §5.3, §5.8 and §8.2:** update once R3 lands tasks 011, 012 and 040 in code (already in the auth project's plan).
