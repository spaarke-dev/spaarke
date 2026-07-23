---
name: dataverse-record-access-security-2026-07-16
description: Dataverse record-access/security model for Spaarke messaging privacy (open/private/internal-only threads); union composition, additive sharing, POA APIs, teams, app-only impersonation.
metadata:
  type: project
---

# Dataverse record-access / security model (messaging privacy) — 2026-07-16

**Question**: How does Dataverse compose record access, and how to implement open / private (only-named-individuals) / internal-only messaging threads with app-only BFF reads + "Manage access" (POA) + internal/external team orchestration?

## Load-bearing findings

**Composition = UNION (additive, never subtractive).** Access check has 4 sources — Ownership, Role access (privilege depth User/BU/BU+children/Org), Shared access (POA), Hierarchy access — all checked, any one suffices. Two-phase: privilege check (does role grant the privilege at all) THEN access check (does the specific row qualify). "All access is accumulative across all those concepts." "If you gave broad organization level read access... you can't go back and hide a single record." Sharing can NEVER restrict — a user's rights on a record are "the union of all the rights." To EXCLUDE someone you must remove the *source* of their access (team membership / role scope / ownership), not add a share.

**Restrict BELOW a team (private/named-individuals) is only possible via BASE SCOPING, not by any subtractive share.** Options: (a) narrow ownership — assign the thread to a single user or a dedicated restricted owner team; (b) dedicated restricted Business Unit; (c) don't grant the broad matter team access to the private thread in the first place, then share (POA) with only named principals. OOB has NO "deny"/exclusion primitive. Microsoft's ethical-wall / need-to-know pattern = BU + team scoping, NOT sharing.

**POA APIs (all Web API actions/functions, all callable app-only):**
- `GrantAccess` / `ModifyAccess` / `RevokeAccess` — manipulate the POA (share) row only. Revoke removes only the *share*, never role/team/owner access.
- `RetrievePrincipalAccess(Target)` bound to systemuser/team/organization → returns `AccessRights` = the principal's EFFECTIVE/cumulative rights from ALL sources (owner+role+team+share), not just shares. One call per (principal, record) — NO bulk.
- `RetrieveSharedPrincipalsAndAccess(Target)` → principals a record was SHARED with (POA only), + their rights. Not the full-access set.
- `RetrieveAccessOrigin` → sentence explaining WHY a principal has access (owner/team/poa/hierarchy/parent). Great for debugging walls.

**Teams:** Owner team (owns records, has security roles, manual membership) vs Access team (no roles, no ownership; gets access only via record share/POA; membership manual or via access-team template auto-team) vs Entra group team (owns records + roles like owner team, but membership is DYNAMIC from Entra security-group membership — managed by Entra admin, not in Dataverse). For internal/external orchestration where membership drives access: Entra group teams (membership-driven) as owner-style grant, OR access teams + GrantAccess for record-scoped shares.

**App-only + row-level security:** Application user is a non-interactive S2S account with a security role; it does NOT bypass row-level security — it is subject to the same model, but Spaarke's app user typically holds an org-scoped role so it effectively sees ALL rows. Therefore the BFF MUST filter explicitly; the platform won't filter for it.

**Impersonation (key perf lever):** Web API `CallerObjectId` header (Entra object id; preferred) or `MSCRMCallerID` (systemuserid; legacy). App user needs `prvActOnBehalfOfAnotherUser` (Delegate role). Effective privileges = INTERSECTION of app-user's and impersonated user's — so an org-scoped app user impersonating user B yields exactly B's effective access. A single impersonated query returns only the rows B can see → the cleanest, most performant way to filter a LIST (e.g., a thread's messages) to one user without computing the union manually.

## Recommendation for Spaarke messaging
- Do NOT hand-compute the union from POA+team+role queries (fragile; misses hierarchy/BU depth). Two robust options: (1) **impersonation** (`CallerObjectId`) for list reads — platform does row-level filtering, single query, ideal for ~5s polling; (2) `RetrievePrincipalAccess` for single per-(user,thread) authorization gates. Use both: impersonated queries for message-list polling, `RetrievePrincipalAccess` for explicit access decisions.
- Wire open/private/internal-only via BASE SCOPING + additive POA: open = thread owned by/shared to the matter team; internal-only = grant only the internal team (never grant external); private = narrow ownership (single user or restricted team) + GrantAccess to named principals only. "Manage access" UI == GrantAccess/RevokeAccess on POA — additive only, so it can build up open/internal/private but CANNOT enforce exclusion; exclusion must be a scoping/ownership decision.
- Existing precedent: `PlaybookSharingService` already uses `GrantAccess`/`RevokeAccess` + reads `principalobjectaccessset` app-only via Web API v9.2 (`Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs`). Its `UserHasSharedAccessAsync` computes only owner+public+shared-team — it does NOT capture role/BU/hierarchy access, so it is NOT a correct effective-access check; replace with impersonation or RetrievePrincipalAccess for messaging.

## Sources
- learn.microsoft.com/power-platform/admin/wp-security-cds (security concepts; additive/accumulative; owner vs access teams)
- learn.microsoft.com/power-platform/admin/how-record-access-determined (privilege+access check; 4 sources union)
- learn.microsoft.com/power-apps/developer/data-platform/security-sharing-assigning (Grant/Modify/RevokeAccess; union of rights; RetrieveAccessOrigin)
- learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/retrieveprincipalaccess (+response: AccessRights = effective)
- learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/retrievesharedprincipalsandaccess (shared-only)
- learn.microsoft.com/power-platform/admin/manage-teams (owner/access/Entra group teams)
- learn.microsoft.com/power-platform/admin/create-users (application users, S2S, non-interactive)
- learn.microsoft.com/power-apps/developer/data-platform/webapi/impersonate-another-user-web-api (CallerObjectId, prvActOnBehalfOfAnotherUser, intersection)

## Open questions
- Does `principalobjectaccessset` (POA entity set) reliably expose all shares for a custom table app-only in all tenants? PlaybookSharingService relies on it; confirm no view/privilege gate for sprk_communicationthread.
- Access-team template auto-team behavior for custom tables (sprk_communicationthread) — verify template applies + AddUserToRecordTeam / RemoveUserFromRecordTeam messages if going the access-team route.
- Impersonation requires the impersonated user to exist as an enabled SystemUser with a role; external participants may be guests — confirm guest users get SystemUser records + can be impersonated.
