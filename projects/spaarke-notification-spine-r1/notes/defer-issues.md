# Deferrals & Issues — spaarke-notification-spine-r1

> Two-write rule: every entry here is mirrored to a GitHub Issue (portfolio board). See `/project-defer-issue-tracking`.

## Deferrals (DEF)

### DEF-001 — Consolidate ad-hoc oid↔systemuserid resolution onto shared ISystemUserIdentityResolver

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-21 |
| **Source** | notification-spine-r1 identity-resolver fix (commit 551756b9e); grep during task 020 follow-up surfaced 6+ independent copies |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/673 |

**Description**

The notification spine added a shared, cached, bidirectional `ISystemUserIdentityResolver` (`src/server/api/Sprk.Bff.Api/Services/Identity/SystemUserIdentityResolver.cs`) over `systemuser.azureactivedirectoryobjectid`. At least 6 pre-existing surfaces each implement their own ad-hoc `oid↔systemuserid` lookup, plus an AI-context resolver with different semantics. They should be migrated onto the one shared resolver. `BriefingService.cs:376` already anticipated this ("extract `ICurrentUserResolver` once a third consumer needs the same lookup").

Concrete failure mode (not "for future flexibility"): **identity-resolution drift** — the copies differ in caching/TTL (some uncached), in `isdisabled` filtering, and in how they read the AAD column (Guid vs string). Divergent resolution of the same user across auth-adjacent surfaces is an **authorization hazard** (one surface authorizes/targets a user another surface resolves differently), not merely duplication.

**Entry-points**

- Shared target: `src/server/api/Sprk.Bff.Api/Services/Identity/SystemUserIdentityResolver.cs`
- Ad-hoc copies to migrate: `Services/Workspace/BriefingService.cs` (~L360-420), `Services/DocumentCheckoutService.cs` (~L832-838), `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` (~L260), `Api/Ai/AnalysisEndpoints.cs` (~L819-842), `Services/Dataverse/Privileges/UserPrivilegeChecker.cs` (~L221), `Services/Registration/RegistrationDataverseService.cs` (~L344)
- Decision needed: `Services/Ai/Context/CallerSystemUserResolver.cs` (`ICallerSystemUserResolver`, oid→systemuserid only, uncached, ClaimsPrincipal-coupled) — collapse into the shared resolver or keep as a claims-facade over it?
- Full context: `projects/spaarke-notification-spine-r1/notes/identity-resolver-consolidation-defer.md`

**Suggested fix**: migrate each copy to inject `ISystemUserIdentityResolver`; delete the private lookups; decide the `ICallerSystemUserResolver` collapse-vs-facade question; add a grep guard against new ad-hoc `azureactivedirectoryobjectid` queries.

**Estimated effort**: 1–2 days (touches auth/identity across many BFF surfaces — needs careful per-surface test coverage)
**Blockers**: none (the shared resolver is shipped)
**Related**: ADR-028 (auth v2); commit 551756b9e

---
