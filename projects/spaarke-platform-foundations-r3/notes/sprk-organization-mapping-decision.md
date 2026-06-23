# `sprk_organization` User-Mapping Mechanism — Decision Record

> **Task**: 032 — Define + implement `sprk_organization` user-mapping mechanism
> **Date**: 2026-06-21
> **Decided by**: task 032 execution per spec.md Assumption + Q4
> **Status**: Decided — implementation shipped in this PR
> **ADR cross-reference**: ADR-034 (task 037, forthcoming) — Identity normalization & organization mapping
> **Spec reference**: `projects/spaarke-platform-foundations-r3/spec.md` FR-1A.6 + Q4 + Assumption
> **Design reference**: `projects/spaarke-platform-foundations-r3/design.md` Part 1 § Identity normalization contract (row 6 `Lookup → sprk_organization`)

---

## Context

`sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` on `sprk_matter` are `Lookup → sprk_organization` (per Q4 owner clarification; `identityType: "Organization"`). For the membership-resolution endpoint (FR-1A.6) to match a current user against these fields, the system must know which `sprk_organization` records the user belongs to. The spec flagged this as an Assumption: the mapping mechanism was not yet defined.

Spec.md offered two candidates:
- **(a)** Dataverse N:N between `systemuser` and `sprk_organization`.
- **(b)** Configurable Lookup field per `MembershipOptions`.

This task records the chosen mechanism and documents the operator follow-up needed.

---

## Decision

**Option (b) — Configurable Lookup field on `sprk_organization` pointing to `systemuser`.**

Operators set `MembershipOptions.OrganizationLookup.UserLookupField` to the logical name of a Lookup column on `sprk_organization` that targets `systemuser`. The resolver issues a single FetchXml query (`<entity name='sprk_organization'><filter><condition attribute='{field}' operator='eq' value='{systemUserId}' /></filter></entity>`) and returns the matching `sprk_organizationid` GUIDs.

### Why (b) over (a) or (c)

| Criterion | (a) N:N | (b) Lookup (chosen) | (c) Team-per-org |
|---|---|---|---|
| Requires Dataverse schema change to ship the BFF code | ❌ Yes — N:N must be created in Dataverse before BFF can resolve anything | ✅ No (field may already exist; if not, operator adds one — does not block code) | ❌ Yes — one team per organization is a large operator burden |
| Failure mode when no mapping exists | Throws / returns empty depending on metadata; ambiguous | Returns empty, logs Info once; **graceful** | Empty teams need scaffolding before any user matches |
| Cardinality fit | Many-to-many (true for vendors-to-many-users) | Single owner / primary user per org (correct for the common case of "the partner who owns this account") | Many-to-many via team membership |
| Flexibility | Fixed — one N:N | High — operator picks ANY Lookup field; supports `sprk_owneruser`, `sprk_relationshipowner`, or any custom field | Medium — coupled to team mgmt |
| Interface stability under future swap | Same `IOrganizationMembershipResolver` contract | Same contract | Same contract |
| Operator action required to enable | Create N:N + populate cross-table | Add field (or use existing) + set `UserLookupField` config | Create teams + populate |
| Default behaviour if operator does nothing | BFF startup ok; resolution throws on first call (N:N missing) unless guarded | BFF startup ok; resolution returns empty (the common, correct case) | BFF startup ok; resolution returns empty |

**Decisive factors**:
1. Option (b) is the only mechanism that **does NOT require a Dataverse schema change to ship**. Spec instruction was explicit: "DEFER the actual creation [of schema changes]". Option (b) is implementable now in code-only form.
2. Option (b) **fails gracefully by default** — when `UserLookupField` is empty, the resolver returns an empty list and logs an Info once per process. No exception. No cascade failure. This honors the spec acceptance criterion ("User with 0 org mappings returns empty (no exception)").
3. Option (b) **does not preclude (a) or (c) in the future** — the resolver contract is identity-only (`Guid in → IReadOnlyList<Guid> out`). A future implementation that swaps to N:N or team-per-org needs no consumer changes.

### Why both interfaces (coordination with task 031)

Task 031 (`IdentityNormalizationService`, running in parallel) created `IIdentityOrganizationResolver` as a "seam" interface that `IdentityNormalizationService` consumes via `IEnumerable<IIdentityOrganizationResolver>`. The task-032 brief required `IOrganizationMembershipResolver` with a `PersonIdentity?` parameter for direct (playbook-node) consumers.

Resolution: the single concrete `OrganizationMembershipResolver` implements **both** interfaces. The `IIdentityOrganizationResolver` method delegates to the canonical `IOrganizationMembershipResolver.GetOrganizationIdsAsync`, ignoring the `contactId` parameter (Option (b) keys off systemuser, not contact). Both interfaces are registered in `MembershipModule` against the same singleton instance, so DI returns the same resolver to both consumers — no double-instantiation, no diverging behaviour.

---

## Implementation

| Component | File | Responsibility |
|---|---|---|
| Contract (canonical) | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/IOrganizationMembershipResolver.cs` | `GetOrganizationIdsAsync(systemUserId, identityContext, ct)` |
| Contract (seam) | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/IIdentityOrganizationResolver.cs` (authored by task 031) | `ResolveOrganizationsAsync(systemUserId, contactId, ct)` |
| Implementation | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/OrganizationMembershipResolver.cs` | Option (b) FetchXml against configured Lookup field |
| Options | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipOptions.cs` (extended) | New `OrganizationLookupOptions` nested record |
| Identity model | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/Models/PersonIdentity.cs` (extended by task 031) | Full PersonIdentity shape per design.md row 6 |
| DI registration | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MembershipModule.cs` | Singleton resolver behind both interfaces |
| Tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/OrganizationMembershipResolverTests.cs` | 5 unit tests covering happy path, no-mapping, no-results, exceptions, cap |

### Configuration shape

`appsettings.json`:

```jsonc
{
  "Membership": {
    "OrganizationLookup": {
      "UserLookupField": "sprk_owneruser",        // empty = no mapping (default)
      "MaxOrganizationsPerUser": 1000              // safety cap
    }
  }
}
```

When `UserLookupField` is empty (the conservative default), the resolver returns an empty list for every user and logs the operator-setup hint once per process. No exception, no cascade failure.

---

## Operator follow-up needed

**YES — for environments that want non-empty organization mappings**:

1. **Decide which `sprk_organization` Lookup field represents the user→org mapping.** Common conventions:
   - `sprk_owneruser` — the account/partner owning the org relationship
   - `sprk_relationshipowner` — alternative name for the same concept
   - or a new custom Lookup column added for this purpose
2. **Ensure the chosen field is a Dataverse Lookup column on `sprk_organization` targeting `systemuser`.** If it does not exist, create it via standard Power Apps maker UI or PAC CLI (`pac column create`).
3. **Populate the column** for organizations that should match a user (typical pattern: backfill via maker UI or one-off PowerShell against the Dataverse Web API).
4. **Set `Membership:OrganizationLookup:UserLookupField`** in `appsettings.json` / Azure App Configuration / Key Vault reference to the chosen logical name.
5. **Optionally tune `MaxOrganizationsPerUser`** (default 1000; raise if any user legitimately maps to more orgs).
6. **Restart the BFF** so `IOptionsMonitor<MembershipOptions>` picks up the change. (`IOptionsMonitor` reloads on change, so this is usually automatic in App Service.)

**For environments that DO NOT want organization mappings yet**: do nothing. The resolver returns empty for all users; matter / event lookups targeting `sprk_organization` simply produce zero matches via the org-membership path. Other identity paths (systemuser, contact, team, BU, account) continue to resolve normally.

---

## Future migration paths

If operational data outgrows Option (b) (e.g., true many-to-many with thousands of users per organization), swap the resolver implementation:

- **Option (a) — N:N**: Add Dataverse N:N between `systemuser` and `sprk_organization`. Replace `OrganizationMembershipResolver` with `NToNOrganizationMembershipResolver` that queries the intersect entity. Interface unchanged.
- **Option (c) — Team-per-organization**: Provision one team per `sprk_organization`. Implement `TeamBasedOrganizationMembershipResolver` that consults `teammembership` joined to the org-team mapping. Interface unchanged.

Both swaps are single-file changes to the DI registration in `MembershipModule.cs`. Consumers (`IdentityNormalizationService`, future `LookupUserMembershipNodeExecutor`) are unaffected.

---

## Cross-references for ADR-034 (task 037)

When authoring ADR-034, cite this note as the source of truth for:
- Option (b) as the chosen mechanism
- The fail-soft default (empty `UserLookupField` → empty list, single Info log per process)
- The double-interface registration pattern in `MembershipModule`
- The migration path to (a) or (c) without consumer changes

ADR-034's "Decision" section should reference this file by relative path; the operator-setup section of any deployment guide should link here for the configuration steps above.
