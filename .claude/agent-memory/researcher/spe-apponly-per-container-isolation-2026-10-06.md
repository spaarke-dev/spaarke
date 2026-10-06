---
name: spe-apponly-per-container-isolation-2026-10-06
description: Can SPE app-only access be scoped per container within one container type/tenant? (No.) Delegated is membership-limited; 25 CT cap (raisable via support); 1:1 owning app; isolation options for N customer stamps in one consuming tenant.
metadata:
  type: reference
---

## 2026-10-06: SPE app-only isolation across customer containers in one container type
**Question**: With one standard CT registered in Spaarke's tenant and per-customer UAMI + app reg, does applicationPermissions on the CT registration let customer A's UAMI reach customer B's container, and is there any per-container app scoping?

**Findings**:
- YES, cross-customer reach. Learn: "An app-only token can access all containers enabled by its container type application permissions"; "Container permissions apply only to delegated access." `FileStorageContainer.Selected` is only the Graph gate — "selected" = selected via CT registration, not per container.
- NO supported per-container app scoping. `POST /storage/fileStorage/containers/{id}/permissions` documents only user `grantedToV2` (roles reader/writer/manager/owner); `driveItem` POST permissions: "For SharePoint Embedded, you can only use this method to create a new sharePointGroup permission with app-only access. You can't create a permission on the root item of a container." Nothing in What's New through Jul 2026 adds it.
- Delegated/OBO is membership-limited: "effective permissions are the intersection of application permissions and user container permissions. The user must be a member of the container."
- Limits: 25 CTs per tenant by default (1 may be trial), "You can request an increase through Microsoft support or your SharePoint Embedded onboarding contact"; 1:1 owning app↔CT ("A single owning app can only own one container type at a time"); standard CTs can't be deleted ("deletion of standard container types isn't yet supported").
- Best isolation: CT per customer with that customer's own app reg as OWNING app, its UAMI as guest app on that CT only → hard platform isolation for both app-only and delegated; costs a CT slot per customer permanently (offboarded customers still consume slots).

**Sources**:
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization (upd 2026-08-24)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/authentication-permissions (upd 2026-08-24)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/register-application-permissions
- https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-post-permissions?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/graph/api/driveitem-post-permissions?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/container-types-containers (25 cap + increase via support)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/create-container-type (1:1, no standard delete)
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/whats-new

**Open questions**: Max raise above 25 and whether support grants it routinely; whether undocumented `grantedToV2.application` on container permissions is accepted (and even if accepted, app-only ignores container perms, so moot).
